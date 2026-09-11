using System.ClientModel;
using System.Text;
using System.Text.RegularExpressions;
using Aquifer.Common.Clients;
using Aquifer.Common.Utilities;
using OpenAI;
using OpenAI.Chat;

namespace Aquifer.AI;

public interface ITranslationService
{
    /// <summary>
    /// Translates plain text.
    /// Note: It's up to the caller to guarantee that the text length is not too long.
    /// </summary>
    Task<string> TranslateTextAsync(
        string text,
        (string Iso6393Code, string EnglishName) destinationLanguage,
        IDictionary<string, string> translationPairs,
        CancellationToken cancellationToken);

    /// <summary>
    /// Translates HTML content while preserving the HTML tags.
    /// Note: Translations are done on each individual paragraph in order to avoid hitting length limitations.
    /// </summary>
    Task<string> TranslateHtmlAsync(
        string html,
        (string Iso6393Code, string EnglishName) destinationLanguage,
        IDictionary<string, string> translationPairs,
        bool shouldOnlyPerformTextImprovement,
        CancellationToken cancellationToken);
}

public class OpenAiOptions
{
    public required string Model { get; init; }

    /// <summary>
    /// Whether the configured model accepts a temperature value. Reasoning models (e.g. GPT-5 and o-series) reject it.
    /// </summary>
    public bool SupportsTemperature { get; init; } = true;

    /// <summary>
    /// Optional reasoning effort for reasoning models (e.g. "low", "medium", "high"). Not sent when null.
    /// </summary>
    public string? ReasoningEffortLevel { get; init; }

    /// <summary>
    /// Optional max output token count. Not sent when null.
    /// </summary>
    public int? MaxOutputTokens { get; init; }
}

public sealed class OpenAiTranslationOptions
{
    public required string HtmlBasePrompt { get; init; }
    public required Dictionary<string, string> LanguageSpecificTextImprovementPromptAppendixByLanguageIso6393CodeMap { get; init; }
    public required string PlainTextTranslationPromptFormatString { get; init; }
    public required float Temperature { get; init; }
    public required string TextImprovementPromptFormatString { get; init; }
    public required string TranslationPromptFormatString { get; init; }
}

public sealed class OpenAiChatCompletionException(string message, string prompt, string text)
    : Exception($"{message} Prompt:{Environment.NewLine}{prompt}{Environment.NewLine}Text:{Environment.NewLine}{text}");

public sealed partial class OpenAiTranslationService : ITranslationService
{
    private const int MaxContentLength = 5_000;
    private const int MaxParallelizationForSingleTranslation = 3;

    private static readonly TimeSpan s_openAiNetworkTimeout = TimeSpan.FromMinutes(10);

    private readonly ChatClient _chatClient;

    private readonly OpenAiOptions _openAiOptions;
    private readonly OpenAiTranslationOptions _options;
    private readonly float _temperature;

    public OpenAiTranslationService(
        OpenAiTranslationOptions openAiTranslationOptions,
        OpenAiOptions openAiOptions,
        IAzureKeyVaultClient keyVaultClient)
    {
        _openAiOptions = openAiOptions;
        _options = openAiTranslationOptions;

        const string openAiApiKeySecretName = "OpenAiApiKey";

        // TODO Inject a ChatClient instead of building one here. This will require changing how we fetch key vault secrets.
#pragma warning disable VSTHRD002
        var openApiKey = keyVaultClient.GetSecretAsync(openAiApiKeySecretName).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002

        _chatClient = new ChatClient(
            openAiOptions.Model,
            new ApiKeyCredential(openApiKey),
            new OpenAIClientOptions
            {
                NetworkTimeout = s_openAiNetworkTimeout,
            });

        _temperature = _options.Temperature;
    }

    public async Task<string> TranslateTextAsync(
        string text,
        (string Iso6393Code, string EnglishName) destinationLanguage,
        IDictionary<string, string> translationPairs,
        CancellationToken cancellationToken)
    {
        if (text.Length > MaxContentLength)
        {
            throw new ArgumentException(
                $"{nameof(text)} must have fewer than {MaxContentLength} characters but has {text.Length}.",
                nameof(text));
        }

        var fullReplacement = TryGetFullTranslationPairReplacement(text, translationPairs);

        if (fullReplacement is not null)
        {
            return fullReplacement;
        }

        var prompt = GetPlainTextTranslationPrompt(destinationLanguage);

        var (maskedText, placeholderMap) = MaskTranslationPairs(text, translationPairs);

        var translatedText = await CompleteChatAsync(prompt, maskedText, cancellationToken);

        return UnmaskTranslationPairs(translatedText, placeholderMap);
    }

    public async Task<string> TranslateHtmlAsync(
        string html,
        (string Iso6393Code, string EnglishName) destinationLanguage,
        IDictionary<string, string> translationPairs,
        bool shouldOnlyPerformTextImprovement,
        CancellationToken cancellationToken)
    {
        var htmlTranslationPrompt = shouldOnlyPerformTextImprovement ? null : GetHtmlTranslationPrompt(destinationLanguage);
        var htmlTextImprovementPrompt = GetHtmlTextImprovementPrompt(destinationLanguage);

        var translatedHtml = new StringBuilder();

        // process the translations in multiple parallel batches
        foreach (var paragraphs in ParagraphRegex()
            .Split(html)
            .Where(x => !string.IsNullOrWhiteSpace(x) && x.Length > 2)
            .Chunk(MaxParallelizationForSingleTranslation))
        {
            // Operate on the paragraphs in each batch in parallel,
            // but wait for all paragraphs in the batch to finish before starting the next batch.
            //
            // Order of operations:
            // 1. Minify HTML (reduces the amount of text we need to send to Open AI).
            // 2. Replace translation pairs (resulting in a mix of English and non-English text).
            // 3. Translate the HTML content via Open AI (note that Aquiferization skips this step).
            // 4. Improve the text's grammar and clarity via Open AI.
            // 5. Expand the minified HTML.
            var paragraphTranslationTasks = paragraphs
                .Select(paragraph => HtmlUtilities.ProcessHtmlContentAsync(
                    paragraph,
                    async minifiedHtmlChunk =>
                    {
                        var (maskedHtmlChunk, placeholderMap) = MaskTranslationPairs(minifiedHtmlChunk, translationPairs);

                        var translatedHtmlChunk = htmlTranslationPrompt == null
                            ? maskedHtmlChunk
                            : await CompleteChatAsync(htmlTranslationPrompt, maskedHtmlChunk, cancellationToken);

                        var improvedHtmlChunk = await CompleteChatAsync(htmlTextImprovementPrompt, translatedHtmlChunk, cancellationToken);

                        return UnmaskTranslationPairs(improvedHtmlChunk, placeholderMap);
                    }))
                .ToList();

            await Task.WhenAll(paragraphTranslationTasks);

            foreach (var paragraphTranslationTask in paragraphTranslationTasks)
            {
                translatedHtml.Append(await paragraphTranslationTask);
            }
        }

        return translatedHtml.ToString();
    }

    /// <summary>
    /// Returns the translation pair's value when <paramref name="text"/> exactly matches (case-insensitively) one of the
    /// translation pair keys, otherwise returns null. This lets callers skip AI translation entirely for exact matches.
    /// </summary>
    internal static string? TryGetFullTranslationPairReplacement(string text, IDictionary<string, string> translationPairs)
    {
        foreach (var pair in translationPairs)
        {
            if (string.Equals(pair.Key, text, StringComparison.InvariantCultureIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Replaces every occurrence of a translation pair key in <paramref name="text"/> with a unique placeholder token,
    /// so that the (already-translated) pair values are never sent to the AI for translation. The returned map lets
    /// <see cref="UnmaskTranslationPairs"/> restore the real values once AI processing is complete.
    /// </summary>
    internal static (string Text, IDictionary<string, string> PlaceholderMap) MaskTranslationPairs(
        string text,
        IDictionary<string, string> translationPairs)
    {
        var placeholderMap = new Dictionary<string, string>();

        foreach (var pair in translationPairs.OrderByDescending(x => x.Key.Length))
        {
            var pattern = $"""\b(?:{pair.Key})\b""";

            if (!Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
            {
                continue;
            }

            var placeholder = $"__TRANSLATION_PAIR_{placeholderMap.Count}__";

            text = Regex.Replace(text, pattern, placeholder, RegexOptions.IgnoreCase);
            placeholderMap[placeholder] = pair.Value;
        }

        return (text, placeholderMap);
    }

    /// <summary>
    /// Replaces every placeholder token produced by <see cref="MaskTranslationPairs"/> with its real translation pair
    /// value. Placeholders that are no longer present in <paramref name="text"/> (e.g. dropped during AI processing)
    /// are left alone.
    /// </summary>
    internal static string UnmaskTranslationPairs(string text, IDictionary<string, string> placeholderMap)
    {
        foreach (var (placeholder, value) in placeholderMap)
        {
            text = text.Replace(placeholder, value, StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }

    private async Task<string> CompleteChatAsync(string prompt, string text, CancellationToken cancellationToken)
    {
        var chatCompletion = await _chatClient.CompleteChatAsync(
            [
                ChatMessage.CreateSystemMessage(prompt),
                ChatMessage.CreateUserMessage(text),
            ],
            // DO NOT reuse ChatCompletionOptions because the Open AI client mutates this object under the hood
            CreateChatCompletionOptions(_openAiOptions, _temperature),
            cancellationToken);

        if (chatCompletion.Value.FinishReason != ChatFinishReason.Stop)
        {
            throw new OpenAiChatCompletionException(
                $"OpenAI chat completion returned an unhandled finish reason: {chatCompletion.Value.FinishReason}.",
                prompt,
                text);
        }

        return chatCompletion.Value.Content[0].Text.Replace(Environment.NewLine, "");
    }

    internal static ChatCompletionOptions CreateChatCompletionOptions(OpenAiOptions openAiOptions, float temperature)
    {
        var options = new ChatCompletionOptions();

        if (openAiOptions.SupportsTemperature)
        {
            options.Temperature = temperature;
        }

#pragma warning disable OPENAI001 // ReasoningEffortLevel is experimental in the OpenAI SDK
        if (openAiOptions.ReasoningEffortLevel is not null)
        {
            options.ReasoningEffortLevel = new ChatReasoningEffortLevel(openAiOptions.ReasoningEffortLevel);
        }
#pragma warning restore OPENAI001

        if (openAiOptions.MaxOutputTokens is not null)
        {
            options.MaxOutputTokenCount = openAiOptions.MaxOutputTokens.Value;
        }

        return options;
    }

    private string GetHtmlTranslationPrompt((string Iso6393Code, string EnglishName) destinationLanguage)
    {
        var translationPrompt = string.Format(_options.TranslationPromptFormatString, destinationLanguage.EnglishName);

        return $"{_options.HtmlBasePrompt} {translationPrompt}";
    }

    private string GetHtmlTextImprovementPrompt((string Iso6393Code, string EnglishName) destinationLanguage)
    {
        var textImprovementPrompt = string.Format(_options.TextImprovementPromptFormatString, destinationLanguage.EnglishName);

        var languageSpecificTextImprovementPromptAppendix =
            _options.LanguageSpecificTextImprovementPromptAppendixByLanguageIso6393CodeMap
                .GetValueOrDefault(destinationLanguage.Iso6393Code.ToUpper());

        return
            $"{_options.HtmlBasePrompt} {textImprovementPrompt}{(languageSpecificTextImprovementPromptAppendix == null ? "" : $" {languageSpecificTextImprovementPromptAppendix}")}";
    }

    private string GetPlainTextTranslationPrompt((string Iso6393Code, string EnglishName) destinationLanguage)
    {
        return string.Format(_options.PlainTextTranslationPromptFormatString, destinationLanguage.EnglishName);
    }

    [GeneratedRegex("(?=<([hH][1-6]|[pP])\\b[^>]*>)")]
    private static partial Regex ParagraphRegex();
}