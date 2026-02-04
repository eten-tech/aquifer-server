using Aquifer.Data;
using Aquifer.Data.Entities;
using Dapper;
using Microsoft.EntityFrameworkCore;
using static Aquifer.Common.Services.ResourceContentSearchResult;

namespace Aquifer.Common.Services;

public interface IResourceContentSearchService
{
    Task<(int Total, IReadOnlyList<ResourceContentSearchResult> ResourceContentSummaries)> SearchAsync(
        ResourceContentSearchIncludeFlags includeFlags,
        ResourceContentSearchFilter filter,
        ResourceContentSearchSortOrder sortOrder,
        int offset,
        int? limit,
        CancellationToken ct);
}

public sealed class ResourceContentSearchFilter
{
    public int? ParentResourceId { get; set; }
    public int? ResourceId { get; set; }
    public string? ResourceEnglishLabelQuery { get; set; }
    public int? LanguageId { get; set; }
    public IReadOnlyList<ResourceContentMediaType>? ExcludeContentMediaTypes { get; set; }
    public IReadOnlyList<ResourceContentMediaType>? IncludeContentMediaTypes { get; set; }
    public IReadOnlyList<ResourceContentStatus>? ExcludeContentStatuses { get; set; }
    public IReadOnlyList<ResourceContentStatus>? IncludeContentStatuses { get; set; }
    public int? AssignedUserId { get; set; }
    public int? AssignedUserCompanyId { get; set; }
    public bool? IsPublished { get; set; }
    public bool? IsDraft { get; set; }
    public bool? IsNewestResourceContentVersion { get; set; }
    public IReadOnlyList<VerseIdRange>? VerseIdRanges { get; set; }
    public bool? HasAudio { get; set; }
    public bool? HasUnresolvedCommentThreads { get; set; }
    public bool? IsInProject { get; set; }
    public bool? ExcludeActiveProjects { get; set; }
    public bool? IsTranslated { get; set; }
    public int? TranslationSourceLanguageId { get; set; }
}

// The property names of this class must match the column names on the dbo.VerseIdRanges User-Defined Table Type!
public sealed class VerseIdRange(int _startVerseId, int _endVerseId)
{
    public VerseIdRange((int StartVerseId, int EndVerseId) verseIdRange) : this(verseIdRange.StartVerseId, verseIdRange.EndVerseId)
    {
    }

    public int StartVerseId { get; } = _startVerseId;
    public int EndVerseId { get; } = _endVerseId;
}

/// <summary>
/// These things are expensive so you have to specifically request them.
/// </summary>
[Flags]
public enum ResourceContentSearchIncludeFlags
{
    None = 0,

    Project = 1,
    ResourceContentVersions = 2,
    HasAudioForLanguage = 4,
    HasUnresolvedCommentThreads = 8,

    All = Project | ResourceContentVersions | HasAudioForLanguage | HasUnresolvedCommentThreads,
}

public enum ResourceContentSearchSortOrder
{
    ResourceContentId = 0,

    /// <summary>
    /// Sort: ParentResource.DisplayName, Resource.SortOrder, Resource.EnglishLabel, ResourceContent.LanguageId
    /// </summary>
    ParentResourceAndResourceName = 1,

    /// <summary>
    /// Sort: Project.ProjectedDeliveryDate (soonest first), Project.Name, ParentResource.DisplayName, Resource.SortOrder,
    /// Resource.EnglishLabel, ResourceContent.LanguageId
    /// </summary>
    ProjectProjectedDeliveryDate = 2,
}

public sealed class ResourceContentSearchResult
{
    /// <summary>
    /// Always populated.
    /// </summary>
    public required ResourceContentSummary ResourceContent { get; init; }

    /// <summary>
    /// Always populated.
    /// </summary>
    public required ResourceSummary Resource { get; init; }

    /// <summary>
    /// Always populated.
    /// </summary>
    public required ParentResourceSummary ParentResource { get; init; }

    /// <summary>
    /// Will not be populated unless the <see cref="ResourceContentSearchIncludeFlags.Project" /> flag is specified.
    /// Even if that flag is specified the result may still be <c>null</c> if the ResourceContent is not in a Project.
    /// Will not be <c>null</c> if <see cref="ResourceContentSearchFilter.IsInProject" /> is <c>true</c>.
    /// </summary>
    public required ProjectSummary? Project { get; init; }

    /// <summary>
    /// Is populated by default, but will not be populated if the <see cref="ResourceContentSearchIncludeFlags.ResourceContentVersions" /> flag is
    /// specified.
    /// </summary>
    public required ResourceContentVersionSummary? ResourceContentVersion { get; init; }

    /// <summary>
    /// Will not be populated unless the <see cref="ResourceContentSearchIncludeFlags.ResourceContentVersions" /> flag is specified.
    /// </summary>
    public required ResourceContentVersionsSummary? ResourceContentVersions { get; init; }

    public sealed class ResourceContentSummary
    {
        public required int Id { get; init; }
        public required int LanguageId { get; init; }
        public required ResourceContentStatus Status { get; init; }
        public required DateTime? ContentUpdated { get; init; }
        public required DateTime Updated { get; init; }
    }

    public sealed class ResourceSummary
    {
        public required int Id { get; init; }
        public required string EnglishLabel { get; init; }
        public required int SortOrder { get; init; }

        /// <summary>
        /// Will not be populated unless the <see cref="ResourceContentSearchIncludeFlags.HasAudioForLanguage" /> flag is specified.
        /// </summary>
        public required bool? HasAudioForLanguage { get; init; }
    }

    public sealed class ParentResourceSummary
    {
        public required int Id { get; init; }
        public required string DisplayName { get; init; }
    }

    public sealed class ProjectSummary
    {
        public required int Id { get; init; }
        public required string Name { get; init; }
        public required DateOnly? ProjectedDeliveryDate { get; init; }
        public required int? DaysUntilDeadline { get; init; }
    }

    public sealed class ResourceContentVersionSummary
    {
        public required int Id { get; init; }
        public required int? AssignedUserId { get; init; }
        public required int? AssignedReviewerUserId { get; init; }
        public required int? SourceWordCount { get; init; }
        public required int? WordCount { get; init; }
        public required ResourceContentVersionReviewLevel ReviewLevel { get; set; }

        /// <summary>
        /// Will not be populated unless the <see cref="ResourceContentSearchIncludeFlags.HasUnresolvedCommentThreads" /> flag is specified.
        /// </summary>
        public required bool? HasUnresolvedCommentThreads { get; init; }
    }

    /// <summary>
    /// A summary of all ResourceContentVersions for a ResourceContent.
    /// </summary>
    public sealed class ResourceContentVersionsSummary
    {
        public required int Id { get; init; }
        public required int MaxVersion { get; init; }
        public required bool AnyIsPublished { get; init; }
        public required bool AnyIsDraft { get; init; }

        /// <summary>
        /// Will not be populated unless the <see cref="ResourceContentSearchIncludeFlags.HasUnresolvedCommentThreads" /> flag is specified
        /// and <see cref="AnyIsDraft" /> is <c>true</c>.
        /// </summary>
        public required bool? HasUnresolvedCommentThreads { get; init; }
    }
}

public sealed class ResourceContentSearchService(AquiferDbContext dbContext) : IResourceContentSearchService
{
    private const string VerseIdRangesTableTypeName = "VerseIdRanges";

    public async Task<(int Total, IReadOnlyList<ResourceContentSearchResult> ResourceContentSummaries)> SearchAsync(
        ResourceContentSearchIncludeFlags includeFlags,
        ResourceContentSearchFilter filter,
        ResourceContentSearchSortOrder sortOrder,
        int offset,
        int? limit,
        CancellationToken ct)
    {
        if (offset < 0)
        {
            throw new ArgumentException($"\"{nameof(offset)}\" must be greater than or equal to 0.", nameof(offset));
        }

        if (limit is <= 0)
        {
            throw new ArgumentException($"\"{nameof(limit)}\" must be greater than 0.", nameof(limit));
        }

        if (limit is null && offset > 0)
        {
            throw new ArgumentException($"\"{nameof(limit)}\" must be passed if \"{nameof(offset)}\" is greater than 0.", nameof(limit));
        }

        if (!includeFlags.HasFlag(ResourceContentSearchIncludeFlags.ResourceContentVersions) &&
            filter is { IsDraft: not null, IsPublished: not null })
        {
            throw new ArgumentException(
                $"\"{nameof(filter.IsDraft)}\" and \"{nameof(filter.IsPublished)}\" cannot both be passed when the \"{nameof(ResourceContentSearchIncludeFlags.ResourceContentVersions)}\" flag is not included.",
                nameof(filter));
        }

        if (!includeFlags.HasFlag(ResourceContentSearchIncludeFlags.ResourceContentVersions) &&
            filter is { IsDraft: null, IsPublished: null, IsNewestResourceContentVersion: null })
        {
            throw new ArgumentException(
                $"One of \"{nameof(filter.IsDraft)}\", \"{nameof(filter.IsPublished)}\", or \"{nameof(filter.IsNewestResourceContentVersion)}\" must be passed when the \"{nameof(ResourceContentSearchIncludeFlags.ResourceContentVersions)}\" flag is not included.",
                nameof(filter));
        }

        if (includeFlags.HasFlag(ResourceContentSearchIncludeFlags.ResourceContentVersions) && filter.IsTranslated.HasValue)
        {
            throw new ArgumentException(
                $"The \"{nameof(ResourceContentSearchIncludeFlags.ResourceContentVersions)}\" flag must not included when filtering by \"{nameof(filter.IsTranslated)}\".",
                nameof(filter));
        }

        if (filter is { IsTranslated: not null, LanguageId: null })
        {
            throw new ArgumentException(
                $"The \"{nameof(filter.LanguageId)}\" must also be passed when filtering by \"{nameof(filter.IsTranslated)}\".",
                nameof(filter));
        }

        if (filter is { IsTranslated: true, IsPublished: not true })
        {
            throw new ArgumentException(
                $"The \"{nameof(filter.IsPublished)}\" filter must also be true when filtering by an \"{nameof(filter.IsTranslated)}\" value of true.",
                nameof(filter));
        }

        if (filter is { IsTranslated: false, TranslationSourceLanguageId: null })
        {
            throw new ArgumentException(
                $"The \"{nameof(filter.TranslationSourceLanguageId)}\" filter must also be passed when filtering by an \"{nameof(filter.IsTranslated)}\" value of false.",
                nameof(filter));
        }

        if (filter is { IsTranslated: not false, TranslationSourceLanguageId: not null })
        {
            throw new ArgumentException(
                $"The \"{nameof(filter.TranslationSourceLanguageId)}\" may not be be passed unless filtering by an \"{nameof(filter.IsTranslated)}\" value of false.",
                nameof(filter));
        }

        if (filter.IsInProject.HasValue && !includeFlags.HasFlag(ResourceContentSearchIncludeFlags.Project))
        {
            throw new ArgumentException(
                $"Filtering on \"{nameof(filter.IsInProject)}\" requires also including the \"{nameof(ResourceContentSearchIncludeFlags.Project)}\" flag.",
                nameof(filter));
        }

        if (filter.ExcludeActiveProjects.HasValue && !includeFlags.HasFlag(ResourceContentSearchIncludeFlags.Project))
        {
            throw new ArgumentException(
                $"Filtering on \"{nameof(filter.ExcludeActiveProjects)}\" requires also including the \"{nameof(ResourceContentSearchIncludeFlags.Project)}\" flag",
                nameof(filter));
        }

        if (sortOrder == ResourceContentSearchSortOrder.ProjectProjectedDeliveryDate &&
            !includeFlags.HasFlag(ResourceContentSearchIncludeFlags.Project))
        {
            throw new ArgumentException(
                $"Sorting on \"{nameof(ResourceContentSearchSortOrder.ProjectProjectedDeliveryDate)}\" requires also including the \"{nameof(ResourceContentSearchIncludeFlags.Project)}\" flag.",
                nameof(filter));
        }

        if (filter is { AssignedUserId: not null, IsDraft: not true })
        {
            throw new ArgumentException(
                $"Filtering on \"{nameof(filter.AssignedUserId)}\" requires also passing \"{nameof(filter.IsDraft)}\" as true.",
                nameof(filter));
        }

        if (filter is { AssignedUserCompanyId: not null, IsDraft: not true })
        {
            throw new ArgumentException(
                $"Filtering on \"{nameof(filter.AssignedUserCompanyId)}\" requires also passing \"{nameof(filter.IsDraft)}\" as true.",
                nameof(filter));
        }

        if (filter is { HasUnresolvedCommentThreads: not null, IsDraft: not true })
        {
            throw new ArgumentException(
                $"Filtering on \"{nameof(filter.HasUnresolvedCommentThreads)}\" requires also passing \"{nameof(filter.IsDraft)}\" as true.",
                nameof(filter));
        }

        if (filter.VerseIdRanges is not null)
        {
            foreach (var verseIdRange in filter.VerseIdRanges)
            {
                if (verseIdRange.StartVerseId > verseIdRange.EndVerseId)
                {
                    throw new ArgumentException(
                        $"\"{nameof(verseIdRange.StartVerseId)}\" must be less than or equal to \"{nameof(verseIdRange.EndVerseId)}\".",
                        nameof(filter));
                }
            }
        }

        if (filter is { ExcludeContentMediaTypes: not null, IncludeContentMediaTypes: not null })
        {
            throw new ArgumentException(
                $"\"{nameof(filter.ExcludeContentMediaTypes)}\" and \"{nameof(filter.IncludeContentMediaTypes)}\" cannot both be passed.",
                nameof(filter));
        }

        if (filter is { ExcludeContentStatuses: not null, IncludeContentStatuses: not null })
        {
            throw new ArgumentException(
                $"\"{nameof(filter.ExcludeContentStatuses)}\" and \"{nameof(filter.IncludeContentStatuses)}\" cannot both be passed.",
                nameof(filter));
        }

        const string selectCountSql = "SELECT COUNT(rc.Id) AS Count";

        // Facts that help improve search performance:
        // 1. If a ResourceContentVersion is in the Draft status then it must be the most recent (by Id) for a Resource Content.
        // 2. If a ResourceContentVersion has an AssignedUserId then it must also be in the Draft status.
        // 3. For unresolved comment threads, only comment threads on a ResourceContentVersion in the Draft status are considered.
        //    Older ResourceContentVersions that have unresolved comment threads are ignored.
        // 4. Only one ResourceContentVersion can be in the Published status for a ResourceContent.
        // 5. Only one ResourceContentVersion can be in the Draft status for a ResourceContent.
        var selectPropertiesSql = $"""
            SELECT
                rc.Id AS {nameof(ResourceContentSummary.Id)},
                rc.LanguageId AS {nameof(ResourceContentSummary.LanguageId)},
                rc.Status AS {nameof(ResourceContentSummary.Status)},
                rc.ContentUpdated AS {nameof(ResourceContentSummary.ContentUpdated)},
                rc.Updated AS {nameof(ResourceContentSummary.Updated)},
                r.Id AS {nameof(ResourceSummary.Id)},
                r.EnglishLabel AS {nameof(ResourceSummary.EnglishLabel)},
                r.SortOrder AS {nameof(ResourceSummary.SortOrder)},
            {(includeFlags.HasFlag(ResourceContentSearchIncludeFlags.HasAudioForLanguage)
                ? $"    IIF(ISNULL(a.AudioCount, 0) > 0, 1, 0) AS {nameof(ResourceSummary.HasAudioForLanguage)},"
                : $"    NULL AS {nameof(ResourceSummary.HasAudioForLanguage)},")}
                pr.Id AS {nameof(ParentResourceSummary.Id)},
                pr.DisplayName AS {nameof(ParentResourceSummary.DisplayName)},
            {(includeFlags.HasFlag(ResourceContentSearchIncludeFlags.Project)
                ?
                $"""
                    prc.ProjectId AS {nameof(ProjectSummary.Id)},
                    p.Name AS {nameof(ProjectSummary.Name)},
                    p.ProjectedDeliveryDate AS {nameof(ProjectSummary.ProjectedDeliveryDate)},
                """
                : $"    NULL AS {nameof(ProjectSummary.Id)},")}
            {(includeFlags.HasFlag(ResourceContentSearchIncludeFlags.ResourceContentVersions)
                ?
                $"""
                    rcvd.MaxRcvId AS {nameof(ResourceContentVersionsSummary.Id)},
                    rcvd.MaxVersion AS {nameof(ResourceContentVersionsSummary.MaxVersion)},
                    rcvd.AnyIsPublished AS {nameof(ResourceContentVersionsSummary.AnyIsPublished)},
                    rcvd.AnyIsDraft AS {nameof(ResourceContentVersionsSummary.AnyIsDraft)},
                    {(includeFlags.HasFlag(ResourceContentSearchIncludeFlags.HasUnresolvedCommentThreads)
                        ? $"ISNULL(c.CommentThreads, 0) AS {nameof(ResourceContentVersionSummary.HasUnresolvedCommentThreads)},"
                        : $"NULL AS {nameof(ResourceContentVersionSummary.HasUnresolvedCommentThreads)},")}
                    NULL AS {nameof(ResourceContentVersionSummary.Id)}
                """
                :
                $"""
                    NULL AS {nameof(ResourceContentVersionsSummary.Id)},
                    rcv.Id AS {nameof(ResourceContentVersionSummary.Id)},
                    rcv.AssignedUserId AS {nameof(ResourceContentVersionSummary.AssignedUserId)},
                    rcv.AssignedReviewerUserId AS {nameof(ResourceContentVersionSummary.AssignedReviewerUserId)},
                    rcv.SourceWordCount AS {nameof(ResourceContentVersionSummary.SourceWordCount)},
                    rcv.WordCount AS {nameof(ResourceContentVersionSummary.WordCount)},
                    rcv.ReviewLevel AS {nameof(ResourceContentVersionSummary.ReviewLevel)},
                    {(includeFlags.HasFlag(ResourceContentSearchIncludeFlags.HasUnresolvedCommentThreads)
                        ? $"ISNULL(c.CommentThreads, 0) AS {nameof(ResourceContentVersionSummary.HasUnresolvedCommentThreads)}"
                        : $"NULL AS {nameof(ResourceContentVersionSummary.HasUnresolvedCommentThreads)}")}
                """)}
            """;

        // Using CROSS APPLY and OUTER APPLY with inner groupings significantly improves performance over using JOINs with a main grouping.
        var fromSql = $"""
            FROM ResourceContents rc
                JOIN Resources r ON r.Id = rc.ResourceId
                JOIN ParentResources pr ON pr.Id = r.ParentResourceId
            {(!includeFlags.HasFlag(ResourceContentSearchIncludeFlags.ResourceContentVersions)
                ? "    JOIN ResourceContentVersions rcv ON rcv.ResourceContentId = rc.Id"
                :
                """
                    CROSS APPLY
                    (
                        SELECT
                            MAX(rcv.Id) AS MaxRcvId,
                            MAX(rcv.Version) AS MaxVersion,
                            IIF(MAX(CAST(rcv.IsPublished AS INT)) = 1, 1, 0) AS AnyIsPublished,
                            IIF(MAX(CAST(rcv.IsDraft AS INT)) = 1, 1, 0) AS AnyIsDraft
                        FROM ResourceContentVersions rcv
                        WHERE rcv.ResourceContentId = rc.Id
                        GROUP BY rcv.ResourceContentId
                    ) rcvd
                """)}
            {(includeFlags.HasFlag(ResourceContentSearchIncludeFlags.Project)
                ?
                """
                    LEFT JOIN ProjectResourceContents prc ON prc.ResourceContentId = rc.Id
                    LEFT JOIN Projects p ON p.Id = prc.ProjectId
                """
                : "")}
            {(filter.AssignedUserCompanyId.HasValue
                ? "    LEFT JOIN Users u ON rcv.AssignedUserId = u.Id"
                : "")}
            {(includeFlags.HasFlag(ResourceContentSearchIncludeFlags.HasAudioForLanguage)
                ?
                $"""
                    OUTER APPLY
                    (
                        SELECT COUNT(rcAudio.Id) AS AudioCount
                        FROM ResourceContents rcAudio
                        WHERE
                            rcAudio.ResourceId = r.Id AND
                            rcAudio.LanguageId = rc.LanguageId AND
                            rcAudio.Id <> rc.Id AND
                            rcAudio.MediaType = {(int)ResourceContentMediaType.Audio}
                        GROUP BY rcAudio.ResourceId
                    ) a
                """
                : "")}
            {(includeFlags.HasFlag(ResourceContentSearchIncludeFlags.HasUnresolvedCommentThreads)
                ?
                $"""
                    OUTER APPLY
                    (
                        SELECT IIF(MIN(CAST(ct.Resolved AS INT)) = 0, 1, 0) AS CommentThreads
                        FROM ResourceContentVersionCommentThreads rcvct
                            JOIN CommentThreads ct ON rcvct.CommentThreadId = ct.Id
                        {(includeFlags.HasFlag(ResourceContentSearchIncludeFlags.ResourceContentVersions)
                            ? "WHERE rcvd.AnyIsDraft = 1 AND rcvct.ResourceContentVersionId = rcvd.MaxRcvId"
                            : "WHERE rcvct.ResourceContentVersionId = rcv.Id")}
                        GROUP BY rcvct.ResourceContentVersionId
                    ) c
                """
                : "")}
            """;

        var coreParameters = new DynamicParameters();
        var whereClausesSql = new List<string>();

        if (filter.VerseIdRanges?.Count > 0)
        {
            const string verseIdRangesParamName = "verseIdRanges";
            coreParameters.Add(verseIdRangesParamName, filter.VerseIdRanges.AsTableValuedParameter(VerseIdRangesTableTypeName));
            whereClausesSql.Add(
                """
                (
                    EXISTS
                    (
                        SELECT NULL
                        FROM @verseIdRanges vir
                            JOIN VerseResources vr ON vr.ResourceId = r.Id
                        WHERE vr.VerseId BETWEEN vir.StartVerseId AND vir.EndVerseId
                    )
                    OR
                    EXISTS
                    (
                        SELECT NULL
                        FROM @verseIdRanges vir
                            JOIN PassageResources pr ON pr.ResourceId = r.Id
                            JOIN Passages p ON p.Id = pr.PassageId
                        WHERE
                        (
                            p.StartVerseId BETWEEN vir.StartVerseId AND vir.EndVerseId OR
                            p.EndVerseId BETWEEN vir.StartVerseId AND vir.EndVerseId OR
                            (p.StartVerseId <= vir.StartVerseId AND p.EndVerseId >= vir.EndVerseId)
                        )
                    )
                )
                """);
        }

        if (filter.ParentResourceId.HasValue)
        {
            const string parentResourceIdParamName = "parentResourceId";

            coreParameters.Add(parentResourceIdParamName, filter.ParentResourceId.Value);
            whereClausesSql.Add($"r.ParentResourceId = @{parentResourceIdParamName}");
        }

        if (filter.ResourceId.HasValue)
        {
            const string resourceIdParamName = "resourceId";

            coreParameters.Add(resourceIdParamName, filter.ResourceId.Value);
            whereClausesSql.Add($"rc.ResourceId = @{resourceIdParamName}");
        }

        if (!string.IsNullOrWhiteSpace(filter.ResourceEnglishLabelQuery))
        {
            const string queryParamName = "query";

            var queryTrimmed = filter.ResourceEnglishLabelQuery.Trim();
            var queryWithoutQuotations = queryTrimmed.Trim('"');
            var isExactSearch = queryWithoutQuotations.Length + 2 == queryTrimmed.Length;

            if (isExactSearch)
            {
                coreParameters.Add(queryParamName, queryWithoutQuotations);
                whereClausesSql.Add($"r.EnglishLabel = @{queryParamName}");
            }
            else
            {
                coreParameters.Add(queryParamName, queryTrimmed);
                whereClausesSql.Add($"r.EnglishLabel LIKE N'%' + @{queryParamName} + N'%'");
            }
        }

        const string languageIdParamName = "languageId";
        if (filter.IsTranslated.HasValue && !filter.IsTranslated.Value)
        {
            const string translationSourceLanguageIdParamName = "translationSourceLanguageId";

            // TranslationSourceLanguageId is verified to not be null above
            coreParameters.Add(translationSourceLanguageIdParamName, filter.TranslationSourceLanguageId!.Value);
            whereClausesSql.Add($"rc.LanguageId = @{translationSourceLanguageIdParamName}");
        }
        else if (filter.LanguageId.HasValue)
        {
            coreParameters.Add(languageIdParamName, filter.LanguageId.Value);
            whereClausesSql.Add($"rc.LanguageId = @{languageIdParamName}");
        }

        if (filter.IsInProject.HasValue)
        {
            whereClausesSql.Add($"prc.ProjectId IS {(filter.IsInProject.Value ? "NOT " : "")}NULL");
        }
        
        if (filter.ExcludeActiveProjects.HasValue)
        {
            if (filter.ExcludeActiveProjects.Value)
            {
                // Include if NOT in a project OR if the project is completed (has ActualPublishDate)
                whereClausesSql.Add("(prc.ProjectId IS NULL OR p.ActualPublishDate IS NOT NULL)");
            }
            else
            {
                // Include if in a project AND the project is not completed (does not have ActualPublishDate)
                whereClausesSql.Add("(prc.ProjectId IS NOT NULL AND p.ActualPublishDate IS NULL)");
            }
        }

        const string excludeContentMediaTypesParamName = "contentMediaTypeIds";
        if (filter.ExcludeContentMediaTypes is { Count: > 0 })
        {
            coreParameters.Add(
                excludeContentMediaTypesParamName,
                filter.ExcludeContentMediaTypes
                    .Select(x => (int)x)
                    .ToArray());
            whereClausesSql.Add($"rc.MediaType NOT IN @{excludeContentMediaTypesParamName}");
        }

        const string includeContentMediaTypesParamName = "contentMediaTypeIds";
        if (filter.IncludeContentMediaTypes is { Count: > 0 })
        {
            coreParameters.Add(
                includeContentMediaTypesParamName,
                filter.IncludeContentMediaTypes
                    .Select(x => (int)x)
                    .ToArray());
            whereClausesSql.Add($"rc.MediaType IN @{includeContentMediaTypesParamName}");
        }

        if (filter.ExcludeContentStatuses is { Count: > 0 })
        {
            const string excludeContentStatusesParamName = "contentStatusIds";

            coreParameters.Add(
                excludeContentStatusesParamName,
                filter.ExcludeContentStatuses
                    .Select(x => (int)x)
                    .ToArray());
            whereClausesSql.Add($"rc.Status NOT IN @{excludeContentStatusesParamName}");
        }

        if (filter.IncludeContentStatuses is { Count: > 0 })
        {
            const string includeContentStatusesParamName = "contentStatusIds";

            coreParameters.Add(
                includeContentStatusesParamName,
                filter.IncludeContentStatuses
                    .Select(x => (int)x)
                    .ToArray());
            whereClausesSql.Add($"rc.Status IN @{includeContentStatusesParamName}");
        }

        if (filter.HasUnresolvedCommentThreads.HasValue)
        {
            whereClausesSql.Add($"ISNULL(c.CommentThreads, 0) = {(filter.HasUnresolvedCommentThreads.Value ? "1" : "0")}");
        }

        if (filter.HasAudio.HasValue)
        {
            whereClausesSql.Add($"ISNULL(a.AudioCount, 0) {(filter.HasAudio.Value ? ">" : "=")} 0");
        }

        if (filter is { IsTranslated: not null, LanguageId: not null })
        {
            coreParameters.Add(languageIdParamName, filter.LanguageId.Value);
            if (filter.IsTranslated.Value)
            {
                // If the resource has a published (and no draft) version in the target language, it is considered already translated.
                // The published check happens elsewhere by requiring the IsPublished filter to be true.
                whereClausesSql.Add(
                    """
                        NOT EXISTS
                        (
                            SELECT NULL
                            FROM ResourceContentVersions rcv2
                            WHERE
                                rcv2.ResourceContentId = rc.Id AND
                                rcv2.IsDraft = 1
                        )
                    """);
            }
            else
            {
                whereClausesSql.Add(
                    $"""
                        NOT EXISTS
                        (
                            SELECT NULL
                            FROM ResourceContents rc2
                            WHERE
                                rc2.ResourceId = rc.ResourceId AND
                                {(filter.ExcludeContentMediaTypes is { Count: > 0 }
                                    ? $"rc2.MediaType NOT IN @{excludeContentMediaTypesParamName} AND"
                                    : "")}
                                {(filter.IncludeContentMediaTypes is { Count: > 0 }
                                    ? $"rc2.MediaType IN @{includeContentMediaTypesParamName} AND"
                                    : "")}
                                rc2.LanguageId = @{languageIdParamName} AND
                                (rc2.Status NOT IN ({(int)ResourceContentStatus.TranslationAwaitingAiDraft}, {(int)ResourceContentStatus.TranslationAiDraftComplete})
                                    OR EXISTS (SELECT NULL FROM ProjectResourceContents prc2 WHERE prc2.ResourceContentId = rc2.Id))
                        )
                    """);
            }
        }

        if (filter.AssignedUserId.HasValue)
        {
            const string assignedUserIdParamName = "assignedUserId";

            coreParameters.Add(assignedUserIdParamName, filter.AssignedUserId.Value);
            whereClausesSql.Add($"rcv.AssignedUserId = @{assignedUserIdParamName}");
        }

        if (filter.AssignedUserCompanyId.HasValue)
        {
            const string assignedUserCompanyIdParamName = "assignedUserCompanyId";

            coreParameters.Add(assignedUserCompanyIdParamName, filter.AssignedUserCompanyId.Value);
            whereClausesSql.Add($"u.CompanyId = @{assignedUserCompanyIdParamName}");
        }

        if (filter.IsPublished.HasValue)
        {
            if (filter.IsPublished.Value)
            {
                whereClausesSql.Add(
                    includeFlags.HasFlag(ResourceContentSearchIncludeFlags.ResourceContentVersions)
                        ? "rcvd.AnyIsPublished = 1"
                        : "rcv.IsPublished = 1");
            }
            else
            {
                // supporting this search will add complexity and should only be added if needed
                throw new NotImplementedException($"A \"{nameof(filter.IsPublished)}\" value of false is not yet supported.");
            }
        }

        if (filter.IsDraft.HasValue)
        {
            if (filter.IsDraft.Value)
            {
                whereClausesSql.Add(
                    includeFlags.HasFlag(ResourceContentSearchIncludeFlags.ResourceContentVersions)
                        ? "rcvd.AnyIsDraft = 1"
                        : "rcv.IsDraft = 1");
            }
            else
            {
                // supporting this search will add complexity and should only be added if needed
                throw new NotImplementedException($"A \"{nameof(filter.IsDraft)}\" value of false is not yet supported.");
            }
        }

        if (filter.IsNewestResourceContentVersion.HasValue)
        {
            whereClausesSql.Add(
                $"""
                    rcv.Id {(filter.IsNewestResourceContentVersion.Value ? "=" : "<>")}
                    (
                        SELECT TOP 1 rcv4.id
                        FROM ResourceContentVersions rcv4
                        WHERE rcv.ResourceContentId = rcv4.ResourceContentId
                        ORDER BY rcv4.Created DESC
                    )
                """);
        }

        var whereSql = whereClausesSql.Count > 0
            ? $"WHERE{Environment.NewLine}    {string.Join($" AND{Environment.NewLine}    ", whereClausesSql)}"
            : "";

        var hasPaging = offset != 0 || limit != null;

        const string offsetLiteralName = "offset";
        const string limitLiteralName = "limit";
        const string pagingSql = $$"""
            OFFSET {={{offsetLiteralName}}} ROWS FETCH NEXT {={{limitLiteralName}}} ROWS ONLY
            """;

        var pagingParameters = new DynamicParameters(coreParameters);
        if (hasPaging)
        {
            pagingParameters.Add(offsetLiteralName, offset);
            pagingParameters.Add(limitLiteralName, limit);
        }

        var sortSql = sortOrder switch
        {
            ResourceContentSearchSortOrder.ResourceContentId => "ORDER BY rc.Id",
            ResourceContentSearchSortOrder.ParentResourceAndResourceName => """
            ORDER BY
                pr.DisplayName,
                r.SortOrder,
                r.EnglishLabel,
                rc.LanguageId
            """,
            ResourceContentSearchSortOrder.ProjectProjectedDeliveryDate => """
            ORDER BY
                COALESCE(p.ProjectedDeliveryDate, '2100-12-31'),
                p.Name,
                pr.DisplayName,
                r.SortOrder,
                r.EnglishLabel,
                rc.LanguageId
            """,
            _ => throw new ArgumentOutOfRangeException(
                nameof(sortOrder),
                sortOrder,
                $"Unexpected \"{nameof(sortOrder)}\": \"{sortOrder}\"."),
        };

        var dataSql = $"""
            {selectPropertiesSql}
            {fromSql}
            {whereSql}
            {sortSql}
            {(hasPaging ? pagingSql : "")}
            """;

        var resourceContentSearchResults = (await dbContext.Database
                .GetDbConnection()
                .QueryWithRetriesAsync<
                    ResourceContentSummary,
                    ResourceSummary,
                    ParentResourceSummary,
                    ProjectSummary,
                    ResourceContentVersionsSummary,
                    ResourceContentVersionSummary,
                    ResourceContentSearchResult>(
                    dataSql,
                    (rc, r, pr, p, rcvd, rcv) => new ResourceContentSearchResult
                    {
                        ResourceContent = rc,
                        Resource = r,
                        ParentResource = pr,
                        Project = p,
                        ResourceContentVersions = rcvd,
                        ResourceContentVersion = rcv,
                    },
                    pagingParameters,
                    splitOn: "Id",
                    cancellationToken: ct))
            .ToList()
            .AsReadOnly();

        // if there is only one page (a common case) then we can determine the total without another query
        int total;
        if (offset == 0 && (!limit.HasValue || limit.Value > resourceContentSearchResults.Count))
        {
            total = resourceContentSearchResults.Count;
        }
        else
        {
            var totalSql = $"""
                {selectCountSql}
                {fromSql}
                {whereSql}
                """;

            total = await dbContext.Database
                .GetDbConnection()
                .ExecuteScalarWithRetriesAsync<int>(totalSql, coreParameters, cancellationToken: ct);
        }

        return (total, resourceContentSearchResults);
    }
}