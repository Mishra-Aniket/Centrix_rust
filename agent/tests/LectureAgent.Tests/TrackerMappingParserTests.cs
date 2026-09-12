using LectureAgent.Infrastructure.Tracker;
using Xunit;

namespace LectureAgent.Tests;

public class TrackerMappingParserTests
{
    private const string SampleResponse = """
    {
        "message": "Giving all the data from the master sheet",
        "selectedCenter": "Pune - PCMC Vidyapeeth",
        "data": [
            {
                "center": "Pune - PCMC Vidyapeeth",
                "rooms": [
                    {
                        "room": "603",
                        "batches": [
                            {},
                            { "batchName": "27-AJ251NA 2026", "batchId": "6981a120a395967a9fd44c7a" },
                            { "batchName": "27-AJ254MA 2026", "batchId": "6981a11b81aec98be9aa708e" },
                            { "batchName": "27-LJ251EA 2026", "batchId": "698ee72ebec45a61ad4de384" }
                        ]
                    },
                    {
                        "room": "502",
                        "batches": [
                            {},
                            { "batchName": "27-LJ251MA 2026", "batchId": "69abe432914b16fb2864b593" }
                        ]
                    }
                ]
            }
        ]
    }
    """;

    [Fact]
    public void ParseMapping_ExtractsEveryBatchWithItsRoom()
    {
        var mapping = TrackerMappingSyncService.ParseMapping(SampleResponse);

        Assert.Equal(4, mapping.Count);
        Assert.Equal("603", mapping["27-AJ251NA 2026"]);
        Assert.Equal("603", mapping["27-AJ254MA 2026"]);
        Assert.Equal("603", mapping["27-LJ251EA 2026"]);
        Assert.Equal("502", mapping["27-LJ251MA 2026"]);
    }

    [Fact]
    public void ParseMapping_SkipsEmptyBatchPlaceholders()
    {
        var mapping = TrackerMappingSyncService.ParseMapping(SampleResponse);

        // The API emits {} row separators; they must not produce entries.
        Assert.DoesNotContain("", mapping.Keys);
        Assert.All(mapping.Keys, key => Assert.False(string.IsNullOrWhiteSpace(key)));
    }

    [Fact]
    public void ParseMapping_ReturnsEmptyDictionary_ForMalformedPayload()
    {
        var mapping = TrackerMappingSyncService.ParseMapping("""{ "data": [] }""");

        Assert.Empty(mapping);
    }
}
