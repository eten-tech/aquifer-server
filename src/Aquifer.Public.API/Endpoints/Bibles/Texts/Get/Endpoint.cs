using Aquifer.Common.Utilities;
using Aquifer.Data;
using Aquifer.Data.Entities;
using Aquifer.Data.Enums;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Aquifer.Public.API.Endpoints.Bibles.Texts.Get;

public class Endpoint(AquiferDbReadOnlyContext dbContext) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/bibles/{BibleId}/texts");
        Description(d => d
            .WithTags("Bibles")
            .ProducesProblemFE()
            .ProducesProblemFE(404));
        Summary(s =>
        {
            s.Summary = "Get the Bible text contained within a book of the Bible.";
            s.Description =
                "For a given Bible and book of the Bible, returns the Bible text (and optional audio information if available) for all verses within the chapter and verse parameters.";
        });
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var bookData = await dbContext.BibleBookContents
            .Where(bbc =>
                bbc.Bible.Enabled &&
                !bbc.Bible.RestrictedLicense &&
                bbc.Bible.Id == request.BibleId &&
                bbc.Book.Code == request.BookCode.ToUpper())
            .Select(bbc => new IntermediateBookData
            {
                BibleId = bbc.Bible.Id,
                BibleName = bbc.Bible.Name,
                BibleAbbreviation = bbc.Bible.Abbreviation,
                BookCode = bbc.Book.Code,
                BookId = bbc.BookId,
                BookName = bbc.DisplayName,
                AudioUrls = bbc.AudioUrls,
            })
            .FirstOrDefaultAsync(ct);

        if (bookData is not null)
        {
            bookData.Chapters = await dbContext.BibleTexts
                .Where(bt => bt.BibleId == bookData.BibleId &&
                    bt.BookId == bookData.BookId &&
                    bt.ChapterNumber >= request.StartChapter &&
                    bt.ChapterNumber <= request.EndChapter &&
                    (bt.ChapterNumber != request.StartChapter || bt.VerseNumber >= request.StartVerse) &&
                    (bt.ChapterNumber != request.EndChapter || bt.VerseNumber <= request.EndVerse))
                .OrderBy(bt => bt.ChapterNumber)
                .GroupBy(bt => bt.ChapterNumber)
                .Select(bt => new IntermediateChapter
                {
                    Number = bt.Key,
                    Verses = bt
                        .OrderBy(bti => bti.VerseNumber)
                        .Select(bti => new IntermediateChapterVerse
                        {
                            Number = bti.VerseNumber,
                            Text = bti.Text,
                        })
                        .ToList(),
                })
                .ToListAsync(ct);
        }

        if (bookData is null)
        {
            await SendNotFoundAsync(ct);
        }
        else
        {
            // The JSON in the DB might have duplicate chapter numbers in the audio data.
            // If we clean that up then this can use a standard ToDictionary() call.
            var audioUrlsByChapter = !request.ShouldReturnAudioData
                ? null
                : DeserializeAudioUrls(bookData.AudioUrls)
                    ?.Chapters
                    ?.ToDictionaryIgnoringDuplicates(c => int.Parse(c.Number));

            var response = new Response
            {
                BibleId = bookData.BibleId,
                BibleName = bookData.BibleName,
                BibleAbbreviation = bookData.BibleAbbreviation,
                BookCode = bookData.BookCode,
                BookName = bookData.BookName,
                Chapters = bookData.Chapters!
                    .Select(ch =>
                    {
                        var chapterAudio = audioUrlsByChapter?.GetValueOrDefault(ch.Number);

                        return new ResponseChapter
                        {
                            Number = ch.Number,
                            Audio = MapToResponseChapterAudio(chapterAudio),
                            Verses = ch.Verses
                                .Select(v =>
                                {
                                    // The JSON in the DB sometimes has duplicate verse numbers in the audio timestamps.
                                    // If we clean that up then this can use a standard ToDictionary() call.
                                    var audioTimestampByVerseNumber = chapterAudio
                                        ?.AudioTimestamps
                                        ?.ToDictionaryIgnoringDuplicates(at => int.Parse(at.VerseNumber));

                                    return new ResponseChapterVerse
                                    {
                                        Number = v.Number,
                                        AudioTimestamp =
                                            MapToResponseAudioTimestamp(audioTimestampByVerseNumber?.GetValueOrDefault(v.Number)),
                                        Text = v.Text,
                                    };
                                })
                                .ToList(),
                        };
                    })
                    .ToList(),
            };

            await SendOkAsync(response, ct);
        }
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

    private class IntermediateBookData
    {
        public required int BibleId { get; set; }
        public required BookId BookId { get; set; }
        public required string BibleName { get; set; }
        public required string BibleAbbreviation { get; set; }
        public required string BookCode { get; set; }
        public required string BookName { get; set; }
        public required string? AudioUrls { get; set; }
        public List<IntermediateChapter>? Chapters { get; set; }
    }

    private class IntermediateChapter
    {
        public required int Number { get; set; }
        public required List<IntermediateChapterVerse> Verses { get; set; }
    }

    private class IntermediateChapterVerse
    {
        public required int Number { get; set; }
        public required string Text { get; set; }
    }
}