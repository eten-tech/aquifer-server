using Aquifer.Common.Messages.Models;
using Aquifer.Jobs.Subscribers;

namespace Aquifer.Jobs.UnitTests.Subscribers;

/// <summary>
/// Covers the two decisions the project pre-translation flow makes from the run options published on the queue message.
/// These are extracted as pure helpers because the flows that use them are a queue subscriber and a durable orchestrator, neither of
/// which is directly unit testable.
/// </summary>
public sealed class ProjectPreTranslationOptionsTests
{
    [Fact]
    public void GetProjectTranslationOrigin_WhenNotForcingRetranslation_ReturnsProject()
    {
        TranslationMessageSubscriber.GetProjectTranslationOrigin(false).Should().Be(TranslationOrigin.Project);
    }

    [Fact]
    public void GetProjectTranslationOrigin_WhenForcingRetranslation_ReturnsBasicTranslationOnly()
    {
        // TranslateResourceCoreAsync throws unless forced retranslation is paired with BasicTranslationOnly.
        TranslationMessageSubscriber.GetProjectTranslationOrigin(true).Should().Be(TranslationOrigin.BasicTranslationOnly);
    }

    [Fact]
    public void FilterRequestedResourceContentIds_WhenNoIdsRequested_ReturnsAllProjectIds()
    {
        var result = TranslationMessageSubscriber.FilterRequestedResourceContentIds([1, 2, 3], null);

        result.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void FilterRequestedResourceContentIds_WhenRequestedIdsAreEmpty_ReturnsAllProjectIds()
    {
        var result = TranslationMessageSubscriber.FilterRequestedResourceContentIds([1, 2, 3], []);

        result.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void FilterRequestedResourceContentIds_WhenIdsRequested_ReturnsOnlyThoseIds()
    {
        var result = TranslationMessageSubscriber.FilterRequestedResourceContentIds([1, 2, 3, 4], [2, 4]);

        result.Should().Equal(2, 4);
    }

    [Fact]
    public void FilterRequestedResourceContentIds_WhenRequestedIdIsNotInTheProject_IgnoresIt()
    {
        var result = TranslationMessageSubscriber.FilterRequestedResourceContentIds([1, 2, 3], [2, 99]);

        result.Should().Equal(2);
    }

    [Fact]
    public void FilterRequestedResourceContentIds_WhenRequestedIdsContainDuplicates_DeduplicatesThem()
    {
        var result = TranslationMessageSubscriber.FilterRequestedResourceContentIds([1, 2, 3], [2, 2, 3]);

        result.Should().Equal(2, 3);
    }

    [Fact]
    public void FilterRequestedResourceContentIds_WhenNoRequestedIdIsInTheProject_ReturnsEmpty()
    {
        var result = TranslationMessageSubscriber.FilterRequestedResourceContentIds([1, 2, 3], [99]);

        result.Should().BeEmpty();
    }
}
