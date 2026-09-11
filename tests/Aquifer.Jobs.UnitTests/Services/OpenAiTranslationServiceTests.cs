using Aquifer.AI;
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
    public void MaskTranslationPairs_WhenTextContainsPairKey_ReplacesItWithAPlaceholderAndReturnsTheMap()
    {
        var translationPairs = new Dictionary<string, string> { ["World"] = "Monde" };

        var (maskedText, placeholderMap) = OpenAiTranslationService.MaskTranslationPairs("Hello World", translationPairs);

        var placeholder = Assert.Single(placeholderMap).Key;
        Assert.Equal("Monde", placeholderMap[placeholder]);
        Assert.Equal($"Hello {placeholder}", maskedText);
        Assert.DoesNotContain("World", maskedText);
    }

    [Fact]
    public void MaskTranslationPairs_WhenTextDoesNotContainAnyPairKey_ReturnsOriginalTextAndAnEmptyMap()
    {
        var translationPairs = new Dictionary<string, string> { ["Foo"] = "Bar" };

        var (maskedText, placeholderMap) = OpenAiTranslationService.MaskTranslationPairs("Hello World", translationPairs);

        Assert.Equal("Hello World", maskedText);
        Assert.Empty(placeholderMap);
    }

    [Fact]
    public void MaskTranslationPairs_WhenMultipleKeysMatch_MasksBothIndependently()
    {
        var translationPairs = new Dictionary<string, string> { ["Hello"] = "Bonjour", ["World"] = "Monde" };

        var (maskedText, placeholderMap) = OpenAiTranslationService.MaskTranslationPairs("Hello World", translationPairs);

        Assert.Equal(2, placeholderMap.Count);
        Assert.DoesNotContain("Hello", maskedText);
        Assert.DoesNotContain("World", maskedText);
        Assert.Equal("Bonjour Monde", OpenAiTranslationService.UnmaskTranslationPairs(maskedText, placeholderMap));
    }

    [Fact]
    public void UnmaskTranslationPairs_WhenPlaceholderIsPresent_RestoresThePairValue()
    {
        var placeholderMap = new Dictionary<string, string> { ["__TRANSLATION_PAIR_0__"] = "Monde" };

        var text = OpenAiTranslationService.UnmaskTranslationPairs("Hello __TRANSLATION_PAIR_0__", placeholderMap);

        Assert.Equal("Hello Monde", text);
    }

    [Fact]
    public void UnmaskTranslationPairs_WhenPlaceholderIsMissing_LeavesTextUnchanged()
    {
        var placeholderMap = new Dictionary<string, string> { ["__TRANSLATION_PAIR_0__"] = "Monde" };

        var text = OpenAiTranslationService.UnmaskTranslationPairs("Hello World", placeholderMap);

        Assert.Equal("Hello World", text);
    }

    [Fact]
    public void TryGetFullTranslationPairReplacement_WhenTextExactlyMatchesPairKey_ReturnsPairValue()
    {
        var translationPairs = new Dictionary<string, string> { ["World"] = "Monde" };

        var replacement = OpenAiTranslationService.TryGetFullTranslationPairReplacement("world", translationPairs);

        Assert.Equal("Monde", replacement);
    }

    [Fact]
    public void TryGetFullTranslationPairReplacement_WhenTextDoesNotMatchAnyPairKey_ReturnsNull()
    {
        var translationPairs = new Dictionary<string, string> { ["World"] = "Monde" };

        var replacement = OpenAiTranslationService.TryGetFullTranslationPairReplacement("Hello World", translationPairs);

        Assert.Null(replacement);
    }
}
