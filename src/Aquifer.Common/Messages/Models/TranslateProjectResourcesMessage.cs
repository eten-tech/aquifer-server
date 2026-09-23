namespace Aquifer.Common.Messages.Models;

/// <summary>
/// Requests pre-translation of a Project's Resource Contents.
/// The run options all default to the behavior of the normal "start project" flow so that messages published before those options
/// existed (including any replayed from the poison queue) continue to behave as they always have.
/// <see cref="ShouldForceRetranslation" /> makes the fan-out run with <see cref="TranslationOrigin.BasicTranslationOnly" /> instead of
/// <see cref="TranslationOrigin.Project" />, which re-translates from the original snapshot and can overwrite existing content.
/// <see cref="ResourceContentIds" /> restricts the run to specific Resource Contents; null or empty means the whole Project.
/// </summary>
public sealed record TranslateProjectResourcesMessage(
    int ProjectId,
    int StartedByUserId,
    bool ShouldForceRetranslation = false,
    bool ShouldSkipCompanyLeadAssignment = false,
    bool ShouldSkipProjectStartedNotification = false,
    IReadOnlyList<int>? ResourceContentIds = null);
