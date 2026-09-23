using System.Collections.ObjectModel;
using Aquifer.Data.Entities;

namespace Aquifer.Common;

public static class Constants
{
    public const int EnglishLanguageId = 1;

    /// <summary>
    /// Only Projects on the Aquifer platform are eligible for AI pre-translation.
    /// </summary>
    public const int AquiferProjectPlatformId = 1;

    public const string HttpContextItemCachedApiKey = "ApiKey";

    public const string TelemetryBnApiPropertyName = "bnApi";
    public const string TelemetryBnApiCallerPropertyName = "bnApiCaller";
    public const string TelemetryBnApiCallerIdPropertyName = "bnApiCallerId";
    public const string TelemetryAppInsightsUserIdPropertyName = "appInsightsUserId";
    public const string TelemetryUserPropertyName = "user";

    // Some guide types have predetermined passages (rather than freeform "select any Bible section").
    // ID 1 = FIA (formerly CBBT-ER)
    public static readonly ReadOnlyCollection<int> PredeterminedPassageGuideIds = new([1]);

    // this represents the media types that we default to English for
    public static readonly ReadOnlyCollection<ResourceContentMediaType> FallbackToEnglishForMediaTypes =
        new([ResourceContentMediaType.Image, ResourceContentMediaType.Video]);

    public static readonly ReadOnlyCollection<ResourceContentStatus> AquiferizationStatuses = new(
    [
        ResourceContentStatus.AquiferizeAwaitingAiDraft,
        ResourceContentStatus.AquiferizeAiDraftComplete,
        ResourceContentStatus.AquiferizeEditorReview,
        ResourceContentStatus.AquiferizeReviewPending,
        ResourceContentStatus.AquiferizePublisherReview,
        ResourceContentStatus.AquiferizeCompanyReview,
    ]);

    public static readonly ReadOnlyCollection<ResourceContentStatus> TranslationStatuses = new(
    [
        ResourceContentStatus.TranslationAwaitingAiDraft,
        ResourceContentStatus.TranslationAiDraftComplete,
        ResourceContentStatus.TranslationEditorReview,
        ResourceContentStatus.TranslationReviewPending,
        ResourceContentStatus.TranslationPublisherReview,
        ResourceContentStatus.TranslationCompanyReview,
    ]);

    public static readonly ReadOnlyCollection<ResourceContentStatus> ReviewPendingStatuses =
        new([ResourceContentStatus.TranslationReviewPending, ResourceContentStatus.AquiferizeReviewPending]);

    public static readonly ReadOnlyCollection<ResourceContentStatus> PublisherReviewStatuses = new(
        [ResourceContentStatus.TranslationPublisherReview, ResourceContentStatus.AquiferizePublisherReview]);

    public static readonly ReadOnlyCollection<ResourceContentStatus> CompanyReviewStatuses =
        new([ResourceContentStatus.TranslationCompanyReview, ResourceContentStatus.AquiferizeCompanyReview]);
}