namespace LectureAgent.Infrastructure.QualityCheck;

using System.Text;
using System.Text.RegularExpressions;
using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;
using LectureAgent.Domain.Services;
using LectureAgent.Infrastructure.Matching;

/// <summary>
/// Inspects locally available notes PDFs without modifying them. The lightweight
/// extractor is deliberate: recordings must never be blocked by optional QC tools.
/// </summary>
public sealed class QcChecker : IQcChecker
{
    private const int MaximumSanePageCount = 500;

    public Task<QcReport> CheckAsync(LectureSession lecture, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var checks = new List<QcCheckResult>();
        var pdfPath = lecture.PdfFileLocalPath;
        var hasNotes = !string.IsNullOrWhiteSpace(pdfPath) && File.Exists(pdfPath);
        checks.Add(new QcCheckResult
        {
            Name = "Notes PDF present",
            Passed = hasNotes,
            Detail = hasNotes ? Path.GetFileName(pdfPath!) : "No local notes PDF is attached to this lecture."
        });

        if (!hasNotes)
        {
            checks.AddRange(new[]
            {
                NotChecked("Title slide", "A notes PDF is required to inspect the title slide."),
                NotChecked("Chapter name", "A notes PDF is required to extract a chapter name."),
                NotChecked("Lecture number", "A notes PDF is required to extract a lecture number."),
                NotChecked("Page count", "A notes PDF is required to count pages.")
            });
            return Task.FromResult(new QcReport { Status = QcStatus.Failed, Checks = checks });
        }

        var notesPdfPath = pdfPath!;
        var metadata = PdfTextExtractor.Extract(notesPdfPath);
        var titleFound = !string.IsNullOrWhiteSpace(metadata.RawText);
        checks.Add(new QcCheckResult
        {
            Name = "Title slide",
            Passed = titleFound,
            Detail = titleFound ? "Readable cover-slide text was found." : "No readable title-slide text was detected."
        });
        checks.Add(new QcCheckResult
        {
            Name = "Chapter name",
            Passed = !string.IsNullOrWhiteSpace(metadata.ChapterName),
            Detail = metadata.ChapterName is { Length: > 0 } chapter ? chapter : "No chapter name was detected."
        });
        checks.Add(new QcCheckResult
        {
            Name = "Lecture number",
            Passed = metadata.LectureNumber.HasValue,
            Detail = metadata.LectureNumber.HasValue ? $"Lecture {metadata.LectureNumber.Value}" : "No lecture number was detected."
        });

        var pageCount = CountPdfPages(notesPdfPath);
        var sanePageCount = pageCount is > 0 and <= MaximumSanePageCount;
        checks.Add(new QcCheckResult
        {
            Name = "Page count",
            Passed = sanePageCount,
            Detail = pageCount > 0
                ? $"{pageCount} page(s) detected{(sanePageCount ? string.Empty : $"; expected 1–{MaximumSanePageCount}")}."
                : "Could not determine a valid page count."
        });

        return Task.FromResult(new QcReport
        {
            Status = checks.All(check => check.Passed) ? QcStatus.Passed : QcStatus.Failed,
            Checks = checks
        });
    }

    private static QcCheckResult NotChecked(string name, string detail) => new()
    {
        Name = name,
        Passed = false,
        Detail = detail
    };

    private static int CountPdfPages(string pdfPath)
    {
        try
        {
            const int maxBytes = 16 * 1024 * 1024;
            using var stream = new FileStream(pdfPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bytes = new byte[(int)Math.Min(stream.Length, maxBytes)];
            var bytesRead = stream.Read(bytes, 0, bytes.Length);
            var content = Encoding.Latin1.GetString(bytes, 0, bytesRead);
            return Regex.Matches(content, @"/Type\s*/Page\b", RegexOptions.CultureInvariant).Count;
        }
        catch
        {
            return 0;
        }
    }
}
