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
}
