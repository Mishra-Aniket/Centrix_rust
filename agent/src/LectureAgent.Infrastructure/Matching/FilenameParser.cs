namespace LectureAgent.Infrastructure.Matching;

using System.Text.RegularExpressions;

/// <summary>
/// Structured hints extracted from a recording file name.
/// </summary>
public sealed class ParsedFilename
{
    /// <summary>Batch codes like AJ251NA, LJ151MA found in the name.</summary>
    public List<string> BatchCodes { get; set; } = new();

    /// <summary>Canonical subject names (physics, chemistry, ...) detected via keywords.</summary>
    public List<string> Subjects { get; set; } = new();

    /// <summary>Dates normalized to yyyy-MM-dd.</summary>
    public List<string> Dates { get; set; } = new();

    /// <summary>Room numbers like "603" found as standalone tokens.</summary>
    public List<string> RoomNumbers { get; set; } = new();

    /// <summary>All lowercased word tokens (separators already split).</summary>
    public List<string> Tokens { get; set; } = new();

    public bool HasHints => BatchCodes.Count > 0 || Subjects.Count > 0;
}

/// <summary>
/// Extracts batch codes, subject keywords, dates and room numbers from recording file
/// names so the matching engine gets real signals instead of a blind substring check.
/// Batch naming follows the PW tracker convention, e.g. "27-AJ251NA 2026 Physics.mp4".
/// Pure and static so it is trivially unit-testable.
/// </summary>
public static partial class FilenameParser
{
    /// <summary>Letter-letter + 2-5 digits + letter-letter, e.g. AJ251NA, LJ151MA.</summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9])([A-Za-z]{2}\d{2,5}[A-Za-z]{2})(?![A-Za-z0-9])")]
    private static partial Regex BatchCodeRegex();

    /// <summary>yyyy-MM-dd, yyyy.MM.dd or yyyy.MM.dd.</summary>
    [GeneratedRegex(@"(?<!\d)(20\d{2})[.\-_](\d{1,2})[.\-_](\d{1,2})(?!\d)")]
    private static partial Regex IsoDateRegex();

    /// <summary>dd-MM-yyyy / dd.MM.yyyy (ambiguous dd/M vs MM/dd kept as day-first, the
    /// dominant convention at the centers; both parts are validated 01..31 / 01..12).</summary>
    [GeneratedRegex(@"(?<!\d)(\d{1,2})[.\-_](\d{1,2})[.\-_](20\d{2})(?!\d)")]
    private static partial Regex DayFirstDateRegex();

    /// <summary>Compact 20260913 next to separators only.</summary>
    [GeneratedRegex(@"(?<!\d)(20\d{2})(0[1-9]|1[0-2])(0[1-9]|[12]\d|3[01])(?!\d)")]
    private static partial Regex CompactDateRegex();

    private static readonly Dictionary<string, string[]> SubjectKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["physics"] = new[] { "physics", "phy" },
        ["chemistry"] = new[] { "chemistry", "chem" },
        ["mathematics"] = new[] { "mathematics", "maths", "math" },
        ["maths"] = new[] { "mathematics", "maths", "math" },
        ["botany"] = new[] { "botany", "bot" },
        ["zoology"] = new[] { "zoology", "zoo" },
        ["biology"] = new[] { "biology", "bio" },
        ["sst"] = new[] { "sst", "social science", "social studies" },
        ["english"] = new[] { "english", "eng" }
    };

    public static ParsedFilename Parse(string? fileName)
    {
        var parsed = new ParsedFilename();
        if (string.IsNullOrWhiteSpace(fileName))
            return parsed;

        var name = Path.GetFileNameWithoutExtension(fileName);

        foreach (Match match in BatchCodeRegex().Matches(name).Cast<Match>())
            parsed.BatchCodes.Add(match.Groups[1].Value.ToUpperInvariant());

        foreach (Match match in IsoDateRegex().Matches(name).Cast<Match>())
        {
            var normalized = FormatDate(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value);
            if (normalized != null)
                parsed.Dates.Add(normalized);
        }

        foreach (Match match in DayFirstDateRegex().Matches(name).Cast<Match>())
        {
            var normalized = FormatDate(match.Groups[3].Value, match.Groups[2].Value, match.Groups[1].Value);
            if (normalized != null && !parsed.Dates.Contains(normalized, StringComparer.Ordinal))
                parsed.Dates.Add(normalized);
        }

        foreach (Match match in CompactDateRegex().Matches(name).Cast<Match>())
        {
            var normalized = FormatDate(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value);
            if (normalized != null && !parsed.Dates.Contains(normalized, StringComparer.Ordinal))
                parsed.Dates.Add(normalized);
        }

        parsed.Tokens = name
            .Split([' ', '.', '_', '-', '(', ')', '[', ']', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim().ToLowerInvariant())
            .Where(token => token.Length > 0)
            .ToList();

        foreach (var token in parsed.Tokens)
        {
            foreach (var (canonical, keywords) in SubjectKeywords)
            {
                if (keywords.Contains(token) && !parsed.Subjects.Contains(canonical, StringComparer.OrdinalIgnoreCase))
                    parsed.Subjects.Add(canonical);
            }

            // Room numbers: 2-4 digit standalone tokens; skip parts of dates/batch codes.
            if (token.Length is >= 2 and <= 4 && token.All(char.IsDigit) && parsed.RoomNumbers.Count == 0
                && !parsed.Dates.Any(date => date.Contains(token, StringComparison.Ordinal)))
            {
                parsed.RoomNumbers.Add(token);
            }
        }

        return parsed;
    }

    /// <summary>
    /// True when the file name points at the timetable slot's batch. Matching is
    /// normalization-based: the code "AJ251NA" matches batch "27-AJ251NA 2026", and a
    /// code-less batch like "BATCH-A" matches the same token sequence in the name.
    /// </summary>
    public static bool MatchesBatch(ParsedFilename parsed, string? batchId)
    {
        if (string.IsNullOrWhiteSpace(batchId))
            return false;

        var normalizedBatch = Normalize(batchId);
        if (normalizedBatch.Length == 0)
            return false;

        return parsed.BatchCodes.Any(code =>
            normalizedBatch.Contains(Normalize(code), StringComparison.Ordinal))
            // Batch names without a recognizable code ("BATCH-A", "2A") still match
            // when the whole normalized name appears inside the file name.
            || parsed.Tokens.Count > 0
                && JoinTokens(parsed).Contains(normalizedBatch, StringComparison.Ordinal);
    }

    private static string JoinTokens(ParsedFilename parsed) => string.Concat(parsed.Tokens);

    /// <summary>
    /// True when the file name's subject keywords align with the slot's subject.
    /// Handles keyword aliases (maths → mathematics) and tolerant ids ("CHEM-101").
    /// </summary>
    public static bool MatchesSubject(ParsedFilename parsed, string? subjectId)
    {
        if (parsed.Subjects.Count == 0 || string.IsNullOrWhiteSpace(subjectId))
            return false;

        var canonical = CanonicalizeSubject(subjectId);

        if (parsed.Subjects.Contains(canonical, StringComparer.OrdinalIgnoreCase))
            return true;

        // Fall back to raw containment for non-standard ids, e.g. "chem101".
        var normalizedSubject = Normalize(subjectId);
        return normalizedSubject.Length > 0 && parsed.Tokens.Any(token =>
            token.Contains(normalizedSubject, StringComparison.Ordinal) ||
            normalizedSubject.Contains(token, StringComparison.Ordinal) && token.Length >= 3);
    }

    public static string CanonicalizeSubject(string? subjectId)
    {
        if (string.IsNullOrWhiteSpace(subjectId))
            return string.Empty;

        var normalized = Normalize(subjectId);
        foreach (var (canonical, keywords) in SubjectKeywords)
        {
            if (keywords.Any(keyword => normalized.Contains(Normalize(keyword), StringComparison.Ordinal)))
                return canonical;
        }

        return normalized;
    }

    /// <summary>Lowercase with separators removed, for tolerant comparisons.</summary>
    public static string Normalize(string value) =>
        new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string? FormatDate(string year, string month, string day)
    {
        if (!int.TryParse(year, out var y) || !int.TryParse(month, out var m) || !int.TryParse(day, out var d))
            return null;

        if (m is < 1 or > 12 || d is < 1 or > 31 || y is < 2020 or > 2100)
            return null;

        return $"{y:D4}-{m:D2}-{d:D2}";
    }
}
