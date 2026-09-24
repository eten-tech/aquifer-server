using System.Text.Json.Serialization;
using Aquifer.Data.Enums;

namespace Aquifer.Well.API.Endpoints.Bibles.Get;

public class GetBibleResponse
{
    public required int BibleId { get; init; }
    public required string BibleName { get; init; }
    public required string BibleAbbreviation { get; init; }
    public required IReadOnlyList<ResponseBook> Books { get; init; }
}

public class ResponseBook
{
    public required BookId BookId { get; set; }
    public required string BookName { get; init; }
    public required string BookCode { get; init; }
    public required IReadOnlyList<ResponseChapter> Chapters { get; init; }
}

public class ResponseChapter
{
    public required int Number { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public required ResponseChapterAudio? Audio { get; init; }

    public required IReadOnlyList<ResponseChapterVerse> Verses { get; init; }
}

public class ResponseChapterAudio
{
    public required ResponseAudioFile? Webm { get; init; }
    public required ResponseAudioFile? Mp3 { get; init; }
}

public class ResponseAudioFile
{
    public required string Url { get; init; }
    public required int Size { get; init; }
}

public class ResponseChapterVerse
{
    public required int Number { get; init; }
    public required string Text { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public required ResponseChapterVerseAudioTimestamp? AudioTimestamp { get; init; }
}

public class ResponseChapterVerseAudioTimestamp
{
    public required decimal Start { get; init; }
    public required decimal End { get; init; }
}