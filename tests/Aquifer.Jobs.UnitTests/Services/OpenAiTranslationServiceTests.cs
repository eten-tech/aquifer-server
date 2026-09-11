using Aquifer.AI;
using Aquifer.Common.Clients;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI.Chat;

#pragma warning disable OPENAI001 // ReasoningEffortLevel is experimental in the OpenAI SDK

namespace Aquifer.Jobs.UnitTests.Services;

public sealed class OpenAiTranslationServiceTests
{
    private const float Temperature = 0.2f;

    private static OpenAiOptions CreateOptions()
    {
        return new OpenAiOptions
        {
            Model = "gpt-4o",
        };
    }

    private static OpenAiTranslationService CreateService(OpenAiTranslationOptions? translationOptions = null)
    {
        return new OpenAiTranslationService(
            translationOptions ?? new OpenAiTranslationOptions
            {
                HtmlBasePrompt = "html-base-prompt",
                LanguageSpecificTextImprovementPromptAppendixByLanguageIso6393CodeMap = [],
                PlainTextTranslationPromptFormatString = "translate-to-{0}",
                Temperature = Temperature,
                TextImprovementPromptFormatString = "improve-{0}",
                TranslationPromptFormatString = "translate-then-{0}",
            },
            CreateOptions(),
            new FakeAzureKeyVaultClient(),
            NullLogger<OpenAiTranslationService>.Instance);
    }

    private sealed class FakeAzureKeyVaultClient : IAzureKeyVaultClient
    {
        public Task<string> GetSecretAsync(string secretName)
        {
            return Task.FromResult("fake-api-key");
        }
    }

    [Fact]
    public void CreateChatCompletionOptions_WhenTemperatureIsSupported_ShouldSetTemperature()
    {
        var options = OpenAiTranslationService.CreateChatCompletionOptions(CreateOptions(), Temperature);

        Assert.Equal(Temperature, options.Temperature);
    }

    [Fact]
    public void CreateChatCompletionOptions_WhenTemperatureIsNotSupported_ShouldNotSetTemperature()
    {
        var options = OpenAiTranslationService.CreateChatCompletionOptions(
            new OpenAiOptions { Model = "gpt-5", SupportsTemperature = false },
            Temperature);

        Assert.Null(options.Temperature);
    }

    [Fact]
    public void CreateChatCompletionOptions_WhenReasoningEffortLevelIsNotConfigured_ShouldNotSetReasoningEffortLevel()
    {
        var options = OpenAiTranslationService.CreateChatCompletionOptions(CreateOptions(), Temperature);

        Assert.Null(options.ReasoningEffortLevel);
    }

    [Fact]
    public void CreateChatCompletionOptions_WhenReasoningEffortLevelIsConfigured_ShouldSetReasoningEffortLevel()
    {
        var options = OpenAiTranslationService.CreateChatCompletionOptions(
            new OpenAiOptions { Model = "gpt-5", ReasoningEffortLevel = "low" },
            Temperature);

        Assert.Equal(ChatReasoningEffortLevel.Low, options.ReasoningEffortLevel);
    }

    [Fact]
    public void CreateChatCompletionOptions_WhenMaxOutputTokensIsNotConfigured_ShouldNotSetMaxOutputTokenCount()
    {
        var options = OpenAiTranslationService.CreateChatCompletionOptions(CreateOptions(), Temperature);

        Assert.Null(options.MaxOutputTokenCount);
    }

    [Fact]
    public void CreateChatCompletionOptions_WhenMaxOutputTokensIsConfigured_ShouldSetMaxOutputTokenCount()
    {
        var options = OpenAiTranslationService.CreateChatCompletionOptions(
            new OpenAiOptions { Model = "gpt-4o", MaxOutputTokens = 1_000 },
            Temperature);

        Assert.Equal(1_000, options.MaxOutputTokenCount);
    }

    [Fact]
    public void TryGetFullTranslationPairReplacement_WhenTextExactlyMatchesPairKey_ReturnsPairValue()
    {
        var translationPairs = new Dictionary<string, string> { ["Yahweh"] = "YHWH-translated" };

        var result = OpenAiTranslationService.TryGetFullTranslationPairReplacement("yahweh", translationPairs);

        Assert.Equal("YHWH-translated", result);
    }

    [Fact]
    public void TryGetFullTranslationPairReplacement_WhenTextDoesNotMatchAnyPairKey_ReturnsNull()
    {
        var translationPairs = new Dictionary<string, string> { ["Yahweh"] = "YHWH-translated" };

        var result = OpenAiTranslationService.TryGetFullTranslationPairReplacement("Yahweh is great", translationPairs);

        Assert.Null(result);
    }

    [Fact]
    public void MaskTranslationPairs_WhenTextContainsPairKey_ReplacesItWithAPlaceholderAndReturnsTheMap()
    {
        var translationPairs = new Dictionary<string, string> { ["Yahweh"] = "YHWH-translated" };

        var (maskedText, placeholderValueMap) = OpenAiTranslationService.MaskTranslationPairs("Yahweh is great", translationPairs);

        Assert.DoesNotContain("Yahweh", maskedText);
        Assert.Single(placeholderValueMap);
        var placeholder = Assert.Single(placeholderValueMap.Keys);
        Assert.Contains(placeholder, maskedText);
        Assert.Equal("YHWH-translated", placeholderValueMap[placeholder]);
    }

    [Fact]
    public void MaskTranslationPairs_WhenTextDoesNotContainAnyPairKey_ReturnsOriginalTextAndAnEmptyMap()
    {
        var translationPairs = new Dictionary<string, string> { ["Yahweh"] = "YHWH-translated" };

        var (maskedText, placeholderValueMap) = OpenAiTranslationService.MaskTranslationPairs("The Lord is great", translationPairs);

        Assert.Equal("The Lord is great", maskedText);
        Assert.Empty(placeholderValueMap);
    }

    [Fact]
    public void MaskTranslationPairs_WhenMultipleKeysMatch_MasksBothIndependently()
    {
        var translationPairs = new Dictionary<string, string>
        {
            ["Yahweh"] = "YHWH-translated",
            ["Moses"] = "Moses-translated",
        };

        var (maskedText, placeholderValueMap) = OpenAiTranslationService.MaskTranslationPairs("Yahweh spoke to Moses", translationPairs);

        Assert.DoesNotContain("Yahweh", maskedText);
        Assert.DoesNotContain("Moses", maskedText);
        Assert.Equal(2, placeholderValueMap.Count);
    }

    [Fact]
    public void UnmaskTranslationPairs_WhenPlaceholderIsPresent_RestoresThePairValue()
    {
        var placeholderValueMap = new Dictionary<string, string> { ["⟦AQP0⟧"] = "YHWH-translated" };

        var result = OpenAiTranslationService.UnmaskTranslationPairs(
            "⟦AQP0⟧ is great",
            placeholderValueMap,
            NullLogger.Instance);

        Assert.Equal("YHWH-translated is great", result);
    }

    [Fact]
    public void UnmaskTranslationPairs_WhenPlaceholderIsMissing_LeavesTextUnchanged()
    {
        var placeholderValueMap = new Dictionary<string, string> { ["⟦AQP0⟧"] = "YHWH-translated" };

        var result = OpenAiTranslationService.UnmaskTranslationPairs(
            "The text no longer has the token",
            placeholderValueMap,
            NullLogger.Instance);

        Assert.Equal("The text no longer has the token", result);
    }

    [Fact]
    public void GetHtmlTranslationPrompt_ShouldIncludeThePlaceholderPreservationInstruction()
    {
        var service = CreateService();

        var prompt = service.GetHtmlTranslationPrompt(("ENG", "English"));

        Assert.Contains(OpenAiTranslationService.PlaceholderPreservationInstruction, prompt);
    }

    [Fact]
    public void GetHtmlTextImprovementPrompt_ShouldIncludeThePlaceholderPreservationInstruction()
    {
        var service = CreateService();

        var prompt = service.GetHtmlTextImprovementPrompt(("ENG", "English"));

        Assert.Contains(OpenAiTranslationService.PlaceholderPreservationInstruction, prompt);
    }

    [Fact]
    public void GetPlainTextTranslationPrompt_ShouldIncludeThePlaceholderPreservationInstruction()
    {
        var service = CreateService();

        var prompt = service.GetPlainTextTranslationPrompt(("ENG", "English"));

        Assert.Contains(OpenAiTranslationService.PlaceholderPreservationInstruction, prompt);
    }
}
