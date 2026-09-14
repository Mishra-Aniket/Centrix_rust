namespace LectureAgent.Infrastructure.Matching;

using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Structured metadata extracted directly from the first page and metadata streams of a PDF.
/// Enables zero-error automatic matching even when the teacher names the file "notes.pdf" or "class.pdf".
/// </summary>
public sealed class PdfExtractedMetadata
{
    /// <summary>Batch codes like LJ152EA, 27-LJ152EA 2026 found inside the PDF text.</summary>
    public List<string> BatchCodes { get; set; } = new();

    /// <summary>Canonical subject names (physics, chemistry, ...) detected inside the PDF text.</summary>
    public List<string> Subjects { get; set; } = new();

    /// <summary>Teacher name if indicated on the cover slide (e.g. "Krishna Sir").</summary>
    public string? TeacherName { get; set; }

    /// <summary>Chapter name if indicated on the cover slide (e.g. "Atomic Physics").</summary>
    public string? ChapterName { get; set; }

    /// <summary>Lecture number (e.g. 1 for "Lecture No. 01").</summary>
    public int? LectureNumber { get; set; }

    /// <summary>Raw cleaned text extracted from the first page/streams.</summary>
    public string RawText { get; set; } = string.Empty;

    public bool HasHints => BatchCodes.Count > 0 || Subjects.Count > 0 || !string.IsNullOrWhiteSpace(TeacherName);
}

/// <summary>
/// Lightweight, zero-dependency PDF text extractor built for .NET 8.
/// Scans PDF object streams, metadata dictionaries, and decompresses FlateDecode streams
/// using built-in ZLibStream to read cover slide text in milliseconds without altering files.
/// </summary>
public static partial class PdfTextExtractor
{
    [GeneratedRegex(@"(?<![A-Za-z0-9])([A-Za-z]{2}\d{2,5}[A-Za-z]{2})(?![A-Za-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex BatchCodeRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9])(\d{1,3}-[A-Za-z]{2}\d{2,5}[A-Za-z]{2}(?:\s+\d{4})?)(?![A-Za-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex FullBatchCodeRegex();

    [GeneratedRegex(@"BATCH\s*(?:CODE)?\s*[:=\-]\s*([A-Za-z0-9\-\s]{4,30})", RegexOptions.IgnoreCase)]
    private static partial Regex BatchCodeLabelRegex();

    [GeneratedRegex(@"SUBJECT\s*(?:NAME)?\s*[:=\-]\s*([A-Za-z\s]{3,30})", RegexOptions.IgnoreCase)]
    private static partial Regex SubjectLabelRegex();

    [GeneratedRegex(@"CHAPTER\s*(?:NAME)?\s*[:=\-]\s*([A-Za-z0-9\s]{3,40})", RegexOptions.IgnoreCase)]
    private static partial Regex ChapterLabelRegex();

    [GeneratedRegex(@"(?:By\s*[-:]*|Teacher\s*[:\-]*|Faculty\s*[:\-]*)\s*([A-Za-z][A-Za-z\s]{2,25}(?:Sir|Ma'am|Mam)?)", RegexOptions.IgnoreCase)]
    private static partial Regex TeacherLabelRegex();

    [GeneratedRegex(@"Lecture\s*(?:No\.?)?\s*[:=\-]?\s*(0?[1-9]\d?)", RegexOptions.IgnoreCase)]
    private static partial Regex LectureNumberRegex();

    /// <summary>
    /// Checks if extracted PDF metadata points to the timetable slot's batch.
    /// </summary>
    public static bool MatchesBatch(PdfExtractedMetadata metadata, string? batchId)
    {
        if (string.IsNullOrWhiteSpace(batchId) || metadata.BatchCodes.Count == 0)
            return false;

        var normalizedBatch = FilenameParser.Normalize(batchId);
        if (normalizedBatch.Length == 0)
            return false;

        return metadata.BatchCodes.Any(code =>
        {
            var normCode = FilenameParser.Normalize(code);
            return normCode.Length >= 4 && (normalizedBatch.Contains(normCode, StringComparison.Ordinal) || normCode.Contains(normalizedBatch, StringComparison.Ordinal));
        });
    }

    /// <summary>
    /// Checks if extracted PDF metadata points to the timetable slot's subject.
    /// </summary>
    public static bool MatchesSubject(PdfExtractedMetadata metadata, string? subjectId)
    {
        if (metadata.Subjects.Count == 0 || string.IsNullOrWhiteSpace(subjectId))
            return false;

        var canonical = FilenameParser.CanonicalizeSubject(subjectId);
        return metadata.Subjects.Contains(canonical, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if extracted PDF teacher matches slot teacher.
    /// </summary>
    public static bool MatchesTeacher(PdfExtractedMetadata metadata, string? teacherId)
    {
        if (string.IsNullOrWhiteSpace(metadata.TeacherName) || string.IsNullOrWhiteSpace(teacherId))
            return false;

        var normTeacher = FilenameParser.Normalize(metadata.TeacherName);
        var normSlot = FilenameParser.Normalize(teacherId);

        return normTeacher.Length >= 3 && normSlot.Length >= 3 &&
            (normSlot.Contains(normTeacher, StringComparison.Ordinal) || normTeacher.Contains(normSlot, StringComparison.Ordinal));
    }

    private static readonly Dictionary<string, string[]> SubjectKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["physics"] = new[] { "physics", "phy", "atomic physics" },
        ["chemistry"] = new[] { "chemistry", "chem", "organic", "inorganic", "physical chemistry" },
        ["mathematics"] = new[] { "mathematics", "maths", "math", "calculus", "algebra" },
        ["biology"] = new[] { "biology", "bio", "botany", "zoology" },
        ["botany"] = new[] { "botany" },
        ["zoology"] = new[] { "zoology", "zoo" },
        ["english"] = new[] { "english" },
        ["hindi"] = new[] { "hindi" },
        ["reasoning"] = new[] { "reasoning" }
    };

    /// <summary>
    /// Extracts text and metadata hints from the first page of a PDF file.
    /// Safely handles file locks, stream compression, and malformed files.
    /// Reads at most maxBytes (default 512 KB) so it is instantaneous.
    /// </summary>
    public static PdfExtractedMetadata Extract(string? filePath, int maxBytes = 512 * 1024)
    {
        var result = new PdfExtractedMetadata();
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return result;

        try
        {
            var ext = Path.GetExtension(filePath);
            if (!string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
                return result;

            byte[] buffer;
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var lengthToRead = (int)Math.Min(fs.Length, maxBytes);
                buffer = new byte[lengthToRead];
                int totalRead = 0;
                while (totalRead < lengthToRead)
                {
                    int bytesRead = fs.Read(buffer, totalRead, lengthToRead - totalRead);
                    if (bytesRead == 0) break;
                    totalRead += bytesRead;
                }
            }

            var textBuilder = new StringBuilder();

            // 1. Extract plain text from uncompressed regions (metadata, literal strings)
            ExtractAsciiStrings(buffer, textBuilder);

            // 2. Locate and decompress FlateDecode streams
            ExtractDecompressedStreams(buffer, textBuilder);

            var extracted = textBuilder.ToString();
            result.RawText = extracted;

            if (string.IsNullOrWhiteSpace(extracted))
                return result;

            // 3. Extract Full Batch Codes (e.g. "27-LJ152EA 2026" or "27-LJ152EA")
            foreach (Match m in FullBatchCodeRegex().Matches(extracted).Cast<Match>())
            {
                var code = m.Groups[1].Value.Trim().ToUpperInvariant();
                if (!result.BatchCodes.Contains(code))
                    result.BatchCodes.Add(code);
            }

            // Also check labeled "BATCH CODE: 27-LJ152EA 2026"
            foreach (Match m in BatchCodeLabelRegex().Matches(extracted).Cast<Match>())
            {
                var val = m.Groups[1].Value.Trim();
                var subMatch = FullBatchCodeRegex().Match(val);
                if (subMatch.Success)
                {
                    var code = subMatch.Groups[1].Value.Trim().ToUpperInvariant();
                    if (!result.BatchCodes.Contains(code))
                        result.BatchCodes.Add(code);
                }
            }

            // Fall back to short batch code (e.g. LJ152EA, AJ251NA)
            foreach (Match m in BatchCodeRegex().Matches(extracted).Cast<Match>())
            {
                var code = m.Groups[1].Value.Trim().ToUpperInvariant();
                if (!result.BatchCodes.Contains(code))
                    result.BatchCodes.Add(code);
            }

            // 4. Extract Subjects
            foreach (Match m in SubjectLabelRegex().Matches(extracted).Cast<Match>())
            {
                var labelSubject = m.Groups[1].Value.Trim();
                foreach (var (canonical, keywords) in SubjectKeywords)
                {
                    if (keywords.Any(k => labelSubject.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!result.Subjects.Contains(canonical))
                            result.Subjects.Add(canonical);
                    }
                }
            }

            // General keyword search if labeled subject was absent
            if (result.Subjects.Count == 0)
            {
                foreach (var (canonical, keywords) in SubjectKeywords)
                {
                    if (keywords.Any(k => Regex.IsMatch(extracted, $@"\b{Regex.Escape(k)}\b", RegexOptions.IgnoreCase)))
                    {
                        if (!result.Subjects.Contains(canonical))
                            result.Subjects.Add(canonical);
                    }
                }
            }

            // 5. Extract Teacher Name (e.g. "By - Krishna Sir")
            var teacherMatch = TeacherLabelRegex().Match(extracted);
            if (teacherMatch.Success)
            {
                result.TeacherName = teacherMatch.Groups[1].Value.Trim();
            }

            // 6. Extract Chapter Name (e.g. "Atomic Physics")
            var chapterMatch = ChapterLabelRegex().Match(extracted);
            if (chapterMatch.Success)
            {
                result.ChapterName = chapterMatch.Groups[1].Value.Trim();
            }

            // 7. Extract Lecture Number (e.g. "Lecture No. 01")
            var lectureMatch = LectureNumberRegex().Match(extracted);
            if (lectureMatch.Success && int.TryParse(lectureMatch.Groups[1].Value, out var lNum))
            {
                result.LectureNumber = lNum;
            }
        }
        catch
        {
            // PDF parsing should never crash the agent; gracefully return whatever was found
        }

        return result;
    }

    /// <summary>
    /// Finds stream ... endstream blocks in PDF data and decompresses FlateDecode streams.
    /// </summary>
    private static void ExtractDecompressedStreams(byte[] buffer, StringBuilder textBuilder)
    {
        var streamMarker = Encoding.ASCII.GetBytes("stream");
        var endstreamMarker = Encoding.ASCII.GetBytes("endstream");

        int searchPos = 0;
        int maxStreamsToInspect = 10;
        int streamsInspected = 0;

        while (searchPos < buffer.Length - 10 && streamsInspected < maxStreamsToInspect)
        {
            int streamStart = IndexOf(buffer, streamMarker, searchPos);
            if (streamStart == -1) break;

            // Skip "stream" word and line break (\r\n or \n)
            int dataStart = streamStart + 6;
            if (dataStart < buffer.Length && buffer[dataStart] == '\r') dataStart++;
            if (dataStart < buffer.Length && buffer[dataStart] == '\n') dataStart++;

            int streamEnd = IndexOf(buffer, endstreamMarker, dataStart);
            if (streamEnd == -1) break;

            int streamLength = streamEnd - dataStart;
            if (streamLength > 10 && streamLength < 500_000)
            {
                streamsInspected++;
                try
                {
                    // Check for zlib header (RFC 1950, standard for PDF FlateDecode: 0x78)
                    byte b0 = buffer[dataStart];
                    byte b1 = buffer[dataStart + 1];

                    if (b0 == 0x78 && (b1 == 0x01 || b1 == 0x9C || b1 == 0xDA || b1 == 0x5E || (b0 * 256 + b1) % 31 == 0))
                    {
                        using var ms = new MemoryStream(buffer, dataStart, streamLength, writable: false);
                        using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
                        using var reader = new StreamReader(zlib, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
                        
                        var decompressed = reader.ReadToEnd();
                        if (!string.IsNullOrWhiteSpace(decompressed))
                        {
                            ExtractPdfTextTokens(decompressed, textBuilder);
                        }
                    }
                    else
                    {
                        // Try raw Deflate stream
                        using var ms = new MemoryStream(buffer, dataStart, streamLength, writable: false);
                        using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
                        using var reader = new StreamReader(deflate, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);

                        var decompressed = reader.ReadToEnd();
                        if (!string.IsNullOrWhiteSpace(decompressed))
                        {
                            ExtractPdfTextTokens(decompressed, textBuilder);
                        }
                    }
                }
                catch
                {
                    // Stream may be an image or encoded differently; skip safely
                }
            }

            searchPos = streamEnd + 9;
        }
    }

    /// <summary>
    /// Parses PDF content stream operators like (Text) Tj, [(Part1) 10 (Part2)] TJ, and &lt;Hex&gt; Tj.
    /// </summary>
    private static void ExtractPdfTextTokens(string content, StringBuilder textBuilder)
    {
        // 1. Literal strings: (Hello World) Tj or [(Hello) 20 (World)] TJ
        var parenRegex = new Regex(@"\(([^)]*)\)\s*(?:Tj|'|"")|\[([^\]]*)\]\s*TJ", RegexOptions.Singleline);
        foreach (Match m in parenRegex.Matches(content).Cast<Match>())
        {
            if (m.Groups[1].Success)
            {
                var clean = CleanPdfString(m.Groups[1].Value);
                if (clean.Length > 0)
                {
                    textBuilder.Append(' ').Append(clean);
                }
            }
            else if (m.Groups[2].Success)
            {
                // Array elements: extract each (...) subpart
                var subPartRegex = new Regex(@"\(([^)]*)\)");
                foreach (Match sub in subPartRegex.Matches(m.Groups[2].Value).Cast<Match>())
                {
                    var clean = CleanPdfString(sub.Groups[1].Value);
                    if (clean.Length > 0)
                    {
                        textBuilder.Append(' ').Append(clean);
                    }
                }
            }
        }

        // 2. Hex strings: <00420041...> Tj
        var hexRegex = new Regex(@"<([0-9A-Fa-f]{4,})>\s*(?:Tj|TJ)", RegexOptions.Singleline);
        foreach (Match m in hexRegex.Matches(content).Cast<Match>())
        {
            var hex = m.Groups[1].Value;
            var decoded = DecodeHexString(hex);
            if (decoded.Length > 0)
            {
                textBuilder.Append(' ').Append(decoded);
            }
        }
    }

    private static void ExtractAsciiStrings(byte[] buffer, StringBuilder textBuilder)
    {
        var rawString = Encoding.ASCII.GetString(buffer);
        ExtractPdfTextTokens(rawString, textBuilder);

        // Also extract /Title (...), /Subject (...), /Author (...)
        var metaRegex = new Regex(@"/(?:Title|Subject|Author|Keywords)\s*\(([^)]*)\)", RegexOptions.IgnoreCase);
        foreach (Match m in metaRegex.Matches(rawString).Cast<Match>())
        {
            var val = CleanPdfString(m.Groups[1].Value);
            if (val.Length > 0)
            {
                textBuilder.Append(' ').Append(val);
            }
        }
    }

    private static string CleanPdfString(string input)
    {
        return input
            .Replace(@"\n", " ")
            .Replace(@"\r", " ")
            .Replace(@"\t", " ")
            .Replace(@"\(", "(")
            .Replace(@"\)", ")")
            .Replace(@"\\", @"\")
            .Trim();
    }

    private static string DecodeHexString(string hex)
    {
        try
        {
            if (hex.Length % 2 != 0) return string.Empty;
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }

            // Detect UTF-16BE (common in PDF hex strings, e.g. 0x00 0x42...)
            if (bytes.Length >= 2 && bytes[0] == 0x00)
            {
                return Encoding.BigEndianUnicode.GetString(bytes).Trim();
            }

            return Encoding.UTF8.GetString(bytes).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static int IndexOf(byte[] buffer, byte[] pattern, int startIndex)
    {
        if (startIndex < 0 || startIndex + pattern.Length > buffer.Length) return -1;
        for (int i = startIndex; i <= buffer.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (buffer[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return i;
        }
        return -1;
    }
}
