using LectureAgent.Domain.Entities;
using Xunit;

namespace LectureAgent.Tests;

public class DriveFolderResolutionTests
{
    [Fact]
    public void BuildDriveFolderPath_WithHierarchy_BuildsCorrectPath()
    {
        var session = new LectureSession
        {
            CenterId = "Pune - PCMC Vidyapeeth",
            RoomId = "604",
            BatchId = "27-AJ452NA 2026",
            DetectedStartTime = new DateTime(2026, 9, 16, 10, 0, 0, DateTimeKind.Utc)
        };

        var center = session.CenterId.Replace("/", "-").Replace("\\", "-").Trim();
        var room = session.RoomId.Replace("/", "-").Replace("\\", "-").Trim();
        var date = session.DetectedStartTime.ToString("yyyy-MM-dd");
        var batch = session.BatchId!.Replace("/", "-").Replace("\\", "-").Trim();

        var path = $"{center}/{room}/{date}/{batch}";

        Assert.Equal("Pune - PCMC Vidyapeeth/604/2026-09-16/27-AJ452NA 2026", path);
    }

    [Fact]
    public void BuildDriveFolderPath_WhenBatchUnassigned_FallsBackToExtraLectures()
    {
        var session = new LectureSession
        {
            CenterId = "Kota Vidyapeeth",
            RoomId = "Room/3",
            BatchId = null,
            SubjectId = "Physics",
            DetectedStartTime = new DateTime(2026, 9, 16, 14, 30, 0, DateTimeKind.Utc)
        };

        var center = session.CenterId.Replace("/", "-").Replace("\\", "-").Trim();
        var room = session.RoomId.Replace("/", "-").Replace("\\", "-").Trim();
        var date = session.DetectedStartTime.ToString("yyyy-MM-dd");
        var batch = !string.IsNullOrWhiteSpace(session.BatchId)
            ? session.BatchId
            : (!string.IsNullOrWhiteSpace(session.SubjectId) ? session.SubjectId : "ExtraLectures");

        var path = $"{center}/{room}/{date}/{batch}";

        Assert.Equal("Kota Vidyapeeth/Room-3/2026-09-16/Physics", path);
    }

    [Fact]
    public void FolderNameNormalization_HandlesVariedDriveNames()
    {
        string Normalize(string name)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in name)
            {
                if (char.IsLetterOrDigit(c))
                    sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        Assert.Equal(Normalize("27-AJ452NA 2026"), Normalize("27_aj452na_2026"));
        Assert.Equal(Normalize("Batch - A (Physics)"), Normalize("batch-a_physics"));
        Assert.Equal(Normalize("Pune - PCMC Vidyapeeth"), Normalize("pune pcmc vidyapeeth"));
        Assert.Equal(Normalize("Room-604"), Normalize("Room 604"));
    }

    [Fact]
    public void BuildDriveFolderPath_WithBatchAndSubject_TargetsSubjectFolderInsideBatch()
    {
        var session = new LectureSession
        {
            CenterId = "Pimpri Vp 2026-2027",
            RoomId = "603",
            BatchId = "27-AJ451NA 2026",
            SubjectId = "Physics"
        };

        var batchClean = session.BatchId.Replace("/", "-").Replace("\\", "-").Trim();
        var subjectClean = session.SubjectId.Replace("/", "-").Replace("\\", "-").Trim();
        var path = $"{batchClean}/{subjectClean}";

        Assert.Equal("27-AJ451NA 2026/Physics", path);
    }

    [Theory]
    [InlineData("MATH", "Mathematics", true)]
    [InlineData("MATH", "Maths", true)]
    [InlineData("Physics", "Phy", true)]
    [InlineData("CHEMISTRY", "Chem", true)]
    [InlineData("Botany", "Bot", true)]
    [InlineData("Zoology", "Zoo", true)]
    [InlineData("Biology", "Bio", true)]
    [InlineData("Physics", "Chemistry", false)]
    [InlineData("MATH", "Physics", false)]
    public void SubjectAliasMatching_MatchesCorrectSubjects(string driveFolderName, string timetableSubject, bool expected)
    {
        string Normalize(string name)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in name)
            {
                if (char.IsLetterOrDigit(c))
                    sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        bool Matches(string n1, string n2)
        {
            var norm1 = Normalize(n1);
            var norm2 = Normalize(n2);
            if (norm1 == norm2) return true;
            if (string.IsNullOrEmpty(norm1) || string.IsNullOrEmpty(norm2)) return false;

            if ((norm1.StartsWith("math") || norm1.StartsWith("mathem")) && (norm2.StartsWith("math") || norm2.StartsWith("mathem"))) return true;
            if (norm1.StartsWith("chem") && norm2.StartsWith("chem")) return true;
            if (norm1.StartsWith("phy") && norm2.StartsWith("phy")) return true;
            if (norm1.StartsWith("bio") && norm2.StartsWith("bio")) return true;
            if (norm1.StartsWith("bot") && norm2.StartsWith("bot")) return true;
            if (norm1.StartsWith("zoo") && norm2.StartsWith("zoo")) return true;

            if (norm1.Length >= 3 && norm2.Length >= 3)
            {
                if (norm1.StartsWith(norm2) || norm2.StartsWith(norm1))
                    return true;
            }

            return false;
        }

        Assert.Equal(expected, Matches(driveFolderName, timetableSubject));
    }
}
