using Aquifer.Common.Messages;
using Aquifer.Common.Messages.Models;

namespace Aquifer.Common.UnitTests.Messages;

/// <summary>
/// The pre-translation run options were added to this message after it was already in production use, so messages queued by an older
/// deployment (including any sitting on the poison queue) must still deserialize and behave exactly as they did before.
/// </summary>
public sealed class TranslateProjectResourcesMessageTests
{
    [Fact]
    public void Deserialize_WhenJsonHasOnlyTheOriginalFields_UsesDefaultsForTheNewOptions()
    {
        const string json = """{"projectId": 42, "startedByUserId": 7}""";

        var message = MessagesJsonSerializer.Deserialize<TranslateProjectResourcesMessage>(json)!;

        message.ProjectId.Should().Be(42);
        message.StartedByUserId.Should().Be(7);
        message.ShouldForceRetranslation.Should().BeFalse();
        message.ShouldSkipCompanyLeadAssignment.Should().BeFalse();
        message.ShouldSkipProjectStartedNotification.Should().BeFalse();
        message.ResourceContentIds.Should().BeNull();
    }

    [Fact]
    public void SerializeThenDeserialize_WithAllOptionsSet_RoundTrips()
    {
        var original = new TranslateProjectResourcesMessage(42, 7, true, true, true, [1, 2, 3]);

        var result = MessagesJsonSerializer.Deserialize<TranslateProjectResourcesMessage>(
            MessagesJsonSerializer.Serialize(original))!;

        result.Should().BeEquivalentTo(original);
    }
}
