using LectureAgent.Infrastructure.Matching;
using Xunit;

namespace LectureAgent.Tests;

public class FilenameParserTests
{
    [Theory]
    [InlineData("27-AJ251NA 2026 Physics.mp4", "AJ251NA")]
    [InlineData("AJ452NA_chemistry.mp4", "AJ452NA")]
    [InlineData("LJ151MA 2026-09-13.mp4", "LJ151MA")]
    public void Parse_ExtractsBatchCodes(string fileName, string expectedCode)
    {
        var parsed = FilenameParser.Parse(fileName);
        Assert.Contains(expectedCode, parsed.BatchCodes);
    }

    [Fact]
    public void Parse_IgnoresNonBatchTokens()
    {
        // "603" has no letter runs, "physics" has no digits — neither is a batch code.
        var parsed = FilenameParser.Parse("room603_physics_class.mp4");
        Assert.Empty(parsed.BatchCodes);
    }

    [Fact]
    public void Parse_ExtractsSubjectKeywords()
    {
        var parsed = FilenameParser.Parse("27-AJ251NA maths lecture.mp4");
        Assert.Contains("mathematics", parsed.Subjects, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("REC_2026-09-13.mp4", "2026-09-13")]
    [InlineData("REC_13-09-2026.mp4", "2026-09-13")]
    [InlineData("REC_20260913.mp4", "2026-09-13")]
    public void Parse_NormalizesDates(string fileName, string expected)
    {
        var parsed = FilenameParser.Parse(fileName);
        Assert.Contains(expected, parsed.Dates);
    }

    [Fact]
    public void MatchesBatch_TrackerStyleBatchId_MatchesEmbeddedCode()
    {
        var parsed = FilenameParser.Parse("AJ251NA physics.mp4");
        Assert.True(FilenameParser.MatchesBatch(parsed, "27-AJ251NA 2026"));
        Assert.False(FilenameParser.MatchesBatch(parsed, "27-AJ452NA 2026"));
    }

    [Fact]
    public void MatchesSubject_AliasesResolveToCanonicalSubject()
    {
        var parsed = FilenameParser.Parse("chem chapter 4.mp4");
        Assert.True(FilenameParser.MatchesSubject(parsed, "CHEMISTRY"));
        Assert.False(FilenameParser.MatchesSubject(parsed, "PHYSICS"));
    }

    [Fact]
    public void MatchesSubject_SlotWithoutSubject_ReturnsFalse()
    {
        var parsed = FilenameParser.Parse("physics.mp4");
        Assert.False(FilenameParser.MatchesSubject(parsed, ""));
        Assert.False(FilenameParser.MatchesSubject(parsed, null));
    }

    [Fact]
    public void Parse_EmptyOrNullName_ReturnsNoHints()
    {
        Assert.False(FilenameParser.Parse(null).HasHints);
        Assert.False(FilenameParser.Parse("").HasHints);
        Assert.False(FilenameParser.Parse("   ").HasHints);
    }
}
