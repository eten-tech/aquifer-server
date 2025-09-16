using Aquifer.Data.Entities;

namespace Aquifer.API.Endpoints.Resources.Content.Translations.Get;

public class Response
{
    public required IEnumerable<TranslationResponse> ContentTranslations { get; set; }
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