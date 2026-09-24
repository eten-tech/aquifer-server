using Aquifer.Data.Entities;
using Aquifer.Jobs.Subscribers;

namespace Aquifer.Jobs.UnitTests.Subscribers;

/// <summary>
/// A forced retranslation overwrites the existing AI draft snapshot rather than adding another one, but that snapshot only exists if a
/// previous translation succeeded. Resources that never finished translating are exactly the ones an admin re-runs, so a missing AI
/// draft snapshot must be a normal case rather than a failure.
/// </summary>
public sealed class ForcedRetranslationSnapshotTests
{
    private static ResourceContentVersionSnapshotEntity Snapshot(ResourceContentStatus status, int daysOld)
    {
        return new ResourceContentVersionSnapshotEntity
        {
            Status = status,
            Created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(-daysOld),
            DisplayName = $"{status} {daysOld}",
            Content = $"content {status} {daysOld}",
        };
    }

    [Fact]
    public void TryGetAiDraftSnapshotToOverwrite_WhenAnAwaitingStatusSnapshotExists_ReturnsIt()
    {
        var awaiting = Snapshot(ResourceContentStatus.TranslationAwaitingAiDraft, 1);
        var snapshots = new[] { Snapshot(ResourceContentStatus.New, 2), awaiting };

        var result = TranslationMessageSubscriber.TryGetAiDraftSnapshotToOverwrite(
            snapshots,
            ResourceContentStatus.TranslationAwaitingAiDraft);

        result.Should().BeSameAs(awaiting);
    }

    [Fact]
    public void TryGetAiDraftSnapshotToOverwrite_WhenSeveralAwaitingStatusSnapshotsExist_ReturnsTheMostRecent()
    {
        var older = Snapshot(ResourceContentStatus.TranslationAwaitingAiDraft, 5);
        var newest = Snapshot(ResourceContentStatus.TranslationAwaitingAiDraft, 1);

        // Deliberately out of chronological order to prove the method sorts rather than trusting the input order.
        var snapshots = new[] { newest, Snapshot(ResourceContentStatus.New, 9), older };

        var result = TranslationMessageSubscriber.TryGetAiDraftSnapshotToOverwrite(
            snapshots,
            ResourceContentStatus.TranslationAwaitingAiDraft);

        result.Should().BeSameAs(newest);
    }

    [Fact]
    public void TryGetAiDraftSnapshotToOverwrite_WhenOnlyANewSnapshotExists_ReturnsNullRatherThanThrowing()
    {
        // This is the state left by Projects/Start when the original pre-translation never completed: a New snapshot and nothing else.
        var snapshots = new[] { Snapshot(ResourceContentStatus.New, 1) };

        var result = TranslationMessageSubscriber.TryGetAiDraftSnapshotToOverwrite(
            snapshots,
            ResourceContentStatus.TranslationAwaitingAiDraft);

        result.Should().BeNull();
    }

    [Fact]
    public void TryGetAiDraftSnapshotToOverwrite_WhenOnlyTheOtherMediaTypesAwaitingStatusExists_ReturnsNull()
    {
        // An aquiferization snapshot must not be overwritten by a translation retranslation, or vice versa.
        var snapshots = new[] { Snapshot(ResourceContentStatus.AquiferizeAwaitingAiDraft, 1) };

        var result = TranslationMessageSubscriber.TryGetAiDraftSnapshotToOverwrite(
            snapshots,
            ResourceContentStatus.TranslationAwaitingAiDraft);

        result.Should().BeNull();
    }

    [Fact]
    public void TryGetAiDraftSnapshotToOverwrite_WhenThereAreNoSnapshots_ReturnsNull()
    {
        var result = TranslationMessageSubscriber.TryGetAiDraftSnapshotToOverwrite(
            [],
            ResourceContentStatus.TranslationAwaitingAiDraft);

        result.Should().BeNull();
    }
}
