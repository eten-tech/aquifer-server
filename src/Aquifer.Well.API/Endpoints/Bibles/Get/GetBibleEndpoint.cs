using Aquifer.Common.Utilities;
using Aquifer.Data;
using Aquifer.Data.Entities;
using Aquifer.Data.Enums;
using Aquifer.Well.API.Helpers;
using Dapper;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Aquifer.Well.API.Endpoints.Bibles.Get;

public class GetBibleEndpoint(AquiferDbReadOnlyContext dbContext) : Endpoint<GetBibleRequest, GetBibleResponse>
{
    public override void Configure()
    {
        Get("/bibles/{BibleId}");
        Options(EndpointHelpers.ServerCacheInSeconds(EndpointHelpers.OneHourInSeconds));
        Description(d => d.ProducesProblemFE().ProducesProblemFE(404));
        Summary(s =>
        {
            s.Summary = "Get Bible text for a given BibleId";
            s.Description = "For a given Bible, returns the Bible text (and audio information if available).";
        });
    }

    public override async Task HandleAsync(GetBibleRequest req, CancellationToken ct)
    {
        var bookData = await dbContext.Bibles
            .Where(b => b.Enabled && b.Id == req.BibleId)
            .Select(b => new
            {
                BibleId = b.Id,
                BibleName = b.Name,
                BibleAbbreviation = b.Abbreviation,
                Books = b.BibleBookContents.Select(bbc => new
                {
                    BookCode = bbc.Book.Code,
                    bbc.BookId,
                    bbc.DisplayName,
                    bbc.AudioUrls,
                }),
            })
            .FirstOrDefaultAsync(ct);

        if (bookData is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        const string query = """
                SELECT BookId, ChapterNumber, VerseNumber, Text
                FROM BibleTexts
                WHERE BibleId = @BibleId
            """;

        var connection = dbContext.Database.GetDbConnection();
        var bibleTexts = await connection.QueryAsync<BibleTextResult>(query, new { req.BibleId });

        var bibleTextsByChapter = bibleTexts.GroupBy(bt => new
        {
            bt.BookId,
            bt.ChapterNumber,
        });

        var response = new GetBibleResponse
        {
            BibleId = bookData.BibleId,
            BibleName = bookData.BibleName,
            BibleAbbreviation = bookData.BibleAbbreviation,
            Books =
            [
                .. bookData.Books.Select(b =>
                {
                    var audioUrlsByChapter = DeserializeAudioUrls(b.AudioUrls)
                        ?.Chapters
                        ?.ToDictionaryIgnoringDuplicates(c => int.Parse(c.Number));

                    return new ResponseBook
                    {
                        BookId = b.BookId,
                        BookCode = b.BookCode,
                        BookName = b.DisplayName,
                        Chapters =
                        [
                            .. bibleTextsByChapter.Where(bt => bt.Key.BookId == b.BookId)
                                .Select(bt =>
                                {
                                    var chapterNumber = bt.Key.ChapterNumber;
                                    var chapterAudio = audioUrlsByChapter?.GetValueOrDefault(chapterNumber);

                                    return new ResponseChapter
                                    {
                                        Number = chapterNumber,
                                        Audio = MapToResponseChapterAudio(chapterAudio),
                                        Verses =
                                        [
                                            .. bt.Where(v => v.ChapterNumber == chapterNumber)
                                                .Select(v =>
                                                {
                                                    var audioTimestampByVerseNumber =
                                                        chapterAudio?.AudioTimestamps?.ToDictionaryIgnoringDuplicates(at =>
                                                            int.Parse(at.VerseNumber));

                                                    return new ResponseChapterVerse
                                                    {
                                                        Number = v.VerseNumber,
                                                        AudioTimestamp =
                                                            MapToResponseAudioTimestamp(
                                                                audioTimestampByVerseNumber?.GetValueOrDefault(v.VerseNumber)),
                                                        Text = v.Text,
                                                    };
                                                }),
                                        ],
                                    };
                                }),
                        ],
                    };
                }),
            ],
        };

        await SendOkAsync(response, ct);
    }

    private static BibleBookContentEntity.AudioUrlsData? DeserializeAudioUrls(string? json)
    {
        return json is null ? null : JsonUtilities.DefaultDeserialize<BibleBookContentEntity.AudioUrlsData>(json);
    }

    private static ResponseChapterAudio? MapToResponseChapterAudio(BibleBookContentEntity.AudioUrlsData.Chapter? audioChapter)
    {
        return audioChapter == null
            ? null
            : new ResponseChapterAudio
            {
                Webm = MapToResponseAudioFile(audioChapter.Webm),
                Mp3 = MapToResponseAudioFile(audioChapter.Mp3),
            };
    }

    private static ResponseAudioFile? MapToResponseAudioFile(BibleBookContentEntity.AudioUrlsData.Chapter.AudioUrl? audioUrl)
    {
        return audioUrl == null
            ? null
            : new ResponseAudioFile
            {
                Url = audioUrl.Url,
                Size = audioUrl.Size,
            };
    }

    private static ResponseChapterVerseAudioTimestamp? MapToResponseAudioTimestamp(
        BibleBookContentEntity.AudioUrlsData.Chapter.AudioTimestamp? audioTimestamp)
    {
        return audioTimestamp == null
            ? null
            : new ResponseChapterVerseAudioTimestamp
            {
                Start = audioTimestamp.Start,
                End = audioTimestamp.End,
            };
    }

    private record BibleTextResult(BookId BookId, int ChapterNumber, int VerseNumber, string Text);
}