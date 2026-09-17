using System.IO.Compression;
using System.Text;
using LectureAgent.Infrastructure.Matching;
using Xunit;

namespace LectureAgent.Tests;

public class PdfTextExtractorTests
{
    private static string CreateSamplePdf(string text, bool compress = false)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_notes_{Guid.NewGuid():N}.pdf");

        byte[] streamData;
        string streamHeader;

        var contentBytes = Encoding.UTF8.GetBytes($"BT /F1 18 Tf ({text}) Tj ET");

        if (compress)
        {
            using var ms = new MemoryStream();
            using (var zlib = new ZLibStream(ms, CompressionLevel.Optimal))
            {
                zlib.Write(contentBytes, 0, contentBytes.Length);
            }
            streamData = ms.ToArray();
            streamHeader = $"<< /Length {streamData.Length} /Filter /FlateDecode >>";
        }
        else
        {
            streamData = contentBytes;
            streamHeader = $"<< /Length {streamData.Length} >>";
        }

        var sb = new StringBuilder();
        sb.AppendLine("%PDF-1.4");
        sb.AppendLine("1 0 obj");
        sb.AppendLine("<< /Type /Catalog /Pages 2 0 R >>");
        sb.AppendLine("endobj");
        sb.AppendLine("2 0 obj");
        sb.AppendLine("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        sb.AppendLine("endobj");
        sb.AppendLine("3 0 obj");
        sb.AppendLine("<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>");
        sb.AppendLine("endobj");
        sb.AppendLine("4 0 obj");
        sb.AppendLine(streamHeader);
        sb.AppendLine("stream");

        using var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write);
        var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
        fs.Write(headerBytes, 0, headerBytes.Length);
        fs.Write(streamData, 0, streamData.Length);

        var footer = "\r\nendstream\r\nendobj\r\nxref\r\n0 5\r\ntrailer\r\n<< /Root 1 0 R >>\r\n%%EOF\r\n";
        var footerBytes = Encoding.ASCII.GetBytes(footer);
        fs.Write(footerBytes, 0, footerBytes.Length);

        return tempFile;
    }

    [Fact]
    public void Extract_UncompressedStream_ExtractsBatchCodeAndSubject()
    {
        var sampleText = "VIDYAPEETH BATCH CODE: 27-LJ152EA 2026 SUBJECT NAME: PHYSICS CHAPTER NAME: Atomic Physics By - Krishna Sir Lecture No. 01";
        var pdfPath = CreateSamplePdf(sampleText, compress: false);

        try
        {
            var meta = PdfTextExtractor.Extract(pdfPath);
            Assert.True(meta.HasHints);
            Assert.Contains("27-LJ152EA 2026", meta.BatchCodes);
            Assert.Contains("physics", meta.Subjects);
            Assert.Equal("Krishna Sir", meta.TeacherName);
            Assert.Equal("Atomic Physics", meta.ChapterName);
            Assert.Equal(1, meta.LectureNumber);

            Assert.True(PdfTextExtractor.MatchesBatch(meta, "27-LJ152EA 2026"));
            Assert.True(PdfTextExtractor.MatchesSubject(meta, "PHYSICS"));
            Assert.True(PdfTextExtractor.MatchesTeacher(meta, "Krishna Sir"));
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public void Extract_FlateDecodeStream_DecompressesAndExtractsData()
    {
        var sampleText = "BATCH CODE: 27-LJ152EA 2026 SUBJECT NAME: PHYSICS By - Krishna Sir";
        var pdfPath = CreateSamplePdf(sampleText, compress: true);

        try
        {
            var meta = PdfTextExtractor.Extract(pdfPath);
            Assert.True(meta.HasHints);
            Assert.Contains("27-LJ152EA 2026", meta.BatchCodes);
            Assert.Contains("physics", meta.Subjects);
            Assert.Equal("Krishna Sir", meta.TeacherName);

            Assert.True(PdfTextExtractor.MatchesBatch(meta, "27-LJ152EA 2026"));
            Assert.True(PdfTextExtractor.MatchesSubject(meta, "PHYSICS"));
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public void Extract_GenericFilenameNotesPdf_StillMatchesWithFirstPageData()
    {
        // When teacher saves as "notes.pdf" with zero batch in filename:
        var sampleText = "BATCH CODE: 27-LJ152EA 2026 SUBJECT NAME: PHYSICS By - Krishna Sir";
        var pdfPath = CreateSamplePdf(sampleText, compress: false);

        try
        {
            var meta = PdfTextExtractor.Extract(pdfPath);
            Assert.True(PdfTextExtractor.MatchesBatch(meta, "27-LJ152EA 2026"));
            Assert.False(PdfTextExtractor.MatchesBatch(meta, "27-AJ999NA 2026"));
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public void Extract_RealWhiteboardRasterPdf_ExtractsBatchAndSubjectViaOcr()
    {
        var testPath = "/Users/aniketmishra/Documents/anike1509261733001.pdf";
        if (!File.Exists(testPath))
            return;

        var meta = PdfTextExtractor.Extract(testPath);
        Assert.True(meta.HasHints);
        Assert.True(meta.BatchCodes.Count > 0);
        Assert.Contains("chemistry", meta.Subjects);
        Assert.Equal("Proanant Sir", meta.TeacherName);
        Assert.Equal(3, meta.LectureNumber);
    }
}
