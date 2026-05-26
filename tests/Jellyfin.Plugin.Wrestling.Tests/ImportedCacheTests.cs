using Jellyfin.Plugin.Wrestling.Services;
using Xunit;

namespace Jellyfin.Plugin.Wrestling.Tests;

public class ImportedCacheTests
{
    [Fact]
    public void ImportCsv_ParsesWorkbookStyleRows()
    {
        const string Csv = """"
            Date,Match Fixture,Match Type,Event,WON,Rating on CageMatch,Votes
            23.03.1997,Bret Hart vs. Steve Austin,No Holds Barred Submission,"WWF WrestleMania 13 - ""Heat""",*****,9.67,507
            23.03.1997,The Undertaker vs. Sycho Sid,Singles,"WWF WrestleMania 13 - ""Heat""",,5.10,100
            """";

        var service = new ImportedMatchCacheService();

        var result = service.ImportCsv(Csv);

        Assert.Equal(1, result.ImportedEvents);
        Assert.Equal(2, result.ImportedMatches);
        Assert.Contains("Imported 1 events", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportedCacheMatcher_ScoresEventTitleAndYear()
    {
        var score = ImportedMatchCacheMatcher.Score(
            "WWF WrestleMania 13 - Heat",
            new DateTime(1997, 3, 23),
            "WrestleMania 13",
            1997,
            null);

        Assert.True(score >= 80);
    }
}
