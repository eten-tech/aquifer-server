using System.Text.Json.Serialization;
using Aquifer.API.Common.Dtos;
using Aquifer.Common.Utilities;
using Aquifer.Data.Entities;
using Aquifer.Data.Schemas;

namespace Aquifer.API.Endpoints.Resources.Content.Get;

public class Response
{
    public required string ParentResourceName { get; set; }
    public required ParentResourceLicenseInfoSchema ParentResourceLicenseInfo { get; set; }
    public required string EnglishLabel { get; set; }
    public IEnumerable<VerseReferenceResponse> VerseReferences { get; set; } = null!;
    public IEnumerable<PassageReferenceResponse> PassageReferences { get; set; } = null!;
    public required IEnumerable<AssociatedContentResponse> AssociatedResources { get; set; }
    public IReadOnlyList<AudioContentResponse> AudioResources { get; set; } = null!;
    public bool HasAudio => AudioResources.Count > 0;
    public required int ResourceContentId { get; set; }
    public required ResourceContentMediaType MediaType { get; set; }
    public required ResourceContentStatus Status { get; set; }
    public required LanguageResponse Language { get; set; }
    public required IEnumerable<TranslationResponse> ContentTranslations { get; set; }
    public required bool HasPublishedVersion { get; set; }
    public int ResourceContentVersionId { get; set; }
    public DateTime ResourceContentVersionCreated { get; set; }
    public bool IsDraft { get; set; }
    public ResourceContentVersionReviewLevel ReviewLevel { get; set; }
    public bool CanPullBackToCompanyReview { get; set; }
    public object Content => JsonUtilities.DefaultDeserialize(ContentValue);
    public int ContentSize { get; set; }
    public string DisplayName { get; set; } = null!;
    public int? WordCount { get; set; }
    public UserResponse? AssignedUser { get; set; }
    public IEnumerable<SnapshotResponse> Snapshots { get; set; } = null!;
    public IEnumerable<VersionResponse> Versions { get; set; } = null!;
    public MachineTranslationResponse? MachineTranslation { get; set; }
    public IEnumerable<MachineTranslationResponse> MachineTranslations { get; set; } = null!;

    public ProjectResponse? Project =>
        ProjectEntity == null
            ? null
            : new ProjectResponse
            {
                Id = ProjectEntity.Id,
                Name = ProjectEntity.Name,
                IsComplete = ProjectEntity.ActualPublishDate is not null,
            };

    public bool HasUnresolvedCommentThreads => CommentThreads?.Threads.Any(t => !t.Resolved) ?? false;
    public bool HasAdditionalReviewer { get; set; }
    public CommentThreadsResponse? CommentThreads { get; set; }

    [JsonIgnore]
    public string ContentValue { get; set; } = null!;

    [JsonIgnore]
    public int ResourceId { get; set; }

    [JsonIgnore]
    public ProjectEntity? ProjectEntity { get; set; }
}

public class LanguageResponse
{
    public required int Id { get; set; }
    public required string EnglishDisplay { get; set; }
    public required ScriptDirection ScriptDirection { get; set; }

    [JsonPropertyName("iso6393Code")]
    public required string ISO6393Code { get; set; }
}

public class AssociatedContentResponse
{
    [JsonIgnore]
    public ResourceContentEntity? ResourceContent { get; set; }

    public int? ContentId => ResourceContent?.Id;
    public required string ParentResourceName { get; set; }
    public required string EnglishLabel { get; set; }
    public IEnumerable<ResourceContentMediaType> MediaTypes { get; set; } = null!;
}

public class AudioContentResponse
{
    public required int ContentId { get; set; }
}

public class ProjectResponse
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public required bool IsComplete { get; set; }
}

public class VerseReferenceResponse
{
    public required int VerseId { get; init; }

    private (Data.Enums.BookId BookId, int Chapter, int Verse) TranslatedVerse => BibleUtilities.TranslateVerseId(VerseId);

    public string Book => BibleBookCodeUtilities.FullNameFromId(TranslatedVerse.BookId);
    public int Chapter => TranslatedVerse.Chapter;
    public int Verse => TranslatedVerse.Verse;
}

public class PassageReferenceResponse
{
    public required int StartVerseId { get; init; }
    public required int EndVerseId { get; init; }

    private (Data.Enums.BookId BookId, int Chapter, int Verse) StartTranslatedVerse => BibleUtilities.TranslateVerseId(StartVerseId);

    public string StartBook => BibleBookCodeUtilities.FullNameFromId(StartTranslatedVerse.BookId);
    public int StartChapter => StartTranslatedVerse.Chapter;
    public int StartVerse => StartTranslatedVerse.Verse;

    private (Data.Enums.BookId BookId, int Chapter, int Verse) EndTranslatedVerse => BibleUtilities.TranslateVerseId(EndVerseId);

    public string EndBook => BibleBookCodeUtilities.FullNameFromId(EndTranslatedVerse.BookId);
    public int EndChapter => EndTranslatedVerse.Chapter;
    public int EndVerse => EndTranslatedVerse.Verse;
}

public class SnapshotResponse
{
    public required int Id { get; set; }
    public required DateTime Created { get; set; }
    public required string? AssignedUserName { get; set; }
    public required string Status { get; set; }
}

public class VersionResponse
{
    public required int Id { get; set; }
    public required DateTime Created { get; set; }
    public required int Version { get; set; }
    public required bool IsPublished { get; set; }
}

public class UserResponse
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required int CompanyId { get; init; }
}

public class TranslationResponse
{
    public required int ContentId { get; init; }
    public required int LanguageId { get; init; }
    public required string Status { get; set; }
    public required bool HasDraft { get; set; }
    public required bool HasPublished { get; set; }
    public required ResourceContentStatus ResourceContentStatus { get; set; }
}

public class CommentThreadsResponse
{
    public required int ThreadTypeId { get; set; }
    public required List<ThreadResponse> Threads { get; set; } = [];
}

public class ThreadResponse
{
    public required int Id { get; set; }
    public required bool Resolved { get; set; }
    public required List<CommentResponse> Comments { get; set; } = [];
}

public class CommentResponse
{
    public required int Id { get; set; }
    public required UserDto User { get; set; }
    public required string Comment { get; set; }
    public required DateTime DateTime { get; set; }
}

public class MachineTranslationResponse
{
    public int Id { get; set; }
    public int ContentIndex { get; set; }
    public int? UserId { get; set; }
    public byte UserRating { get; set; }
    public bool ImproveClarity { get; set; }
    public bool ImproveTone { get; set; }
    public bool ImproveConsistency { get; set; }
    public bool HadRetranslation { get; set; }
    public DateTime Created { get; set; }
}