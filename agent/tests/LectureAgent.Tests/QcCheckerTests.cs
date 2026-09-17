using LectureAgent.Domain.Entities;
using LectureAgent.Domain.Enums;
using LectureAgent.Infrastructure.QualityCheck;
using Xunit;

namespace LectureAgent.Tests;

public sealed class QcCheckerTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"centrix-qc-{Guid.NewGuid():N}");

    public QcCheckerTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    [Fact]
    public async Task CheckAsync_WithoutNotesPdf_FailsAndExplainsMissingAsset()
    {
        var report = await new QcChecker().CheckAsync(new LectureSession
        {
            LectureSessionId = "LSN-QC-NO-PDF"
        });

        Assert.Equal(QcStatus.Failed, report.Status);
        var notesCheck = Assert.Single(report.Checks.Where(check => check.Name == "Notes PDF present"));
        Assert.False(notesCheck.Passed);
    }

    [Fact]
    public async Task CheckAsync_ReadablePdfWithMetadata_PassesAllChecks()
    {
        var pdfPath = Path.Combine(_tempDirectory, "notes.pdf");
        await File.WriteAllTextAsync(pdfPath, """
            %PDF-1.4
            1 0 obj << /Type /Page >> endobj
            BT (Chapter: Atomic Physics) Tj (Lecture No. 01) Tj ET
            %%EOF
            """);

        var report = await new QcChecker().CheckAsync(new LectureSession
        {
            LectureSessionId = "LSN-QC-PASS",
            PdfFileLocalPath = pdfPath
        });

        Assert.Equal(QcStatus.Passed, report.Status);
        Assert.All(report.Checks, check => Assert.True(check.Passed, check.Detail));
    }
}
