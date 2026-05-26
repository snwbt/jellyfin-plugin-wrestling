using Wrestling.CacheWorker;
using Xunit;

namespace Jellyfin.Plugin.Wrestling.Tests;

public class BrowserWorkerParserTests
{
    [Fact]
    public void ChooseBestCandidate_UsesTitleAndYear()
    {
        const string Html = """
            <a href="?id=1&amp;nr=100">CZW Cage Of Death 3 2001</a>
            <a href="?id=1&amp;nr=200">CZW Cage Of Death 4 2002</a>
            """;

        var candidate = CagematchPageParser.ChooseBestCandidate(Html, "CZW: Cage Of Death 4", 2002, null);

        Assert.NotNull(candidate);
        Assert.Equal("200", candidate.EventId);
    }

    [Fact]
    public void ParseEventPage_ExtractsMatchesForCacheSync()
    {
        var html = File.ReadAllText(Path.Combine("Fixtures", "cagematch-event.html"));

        var result = CagematchPageParser.ParseEventPage(html, "12345", "https://example.test");

        Assert.Equal("12345", result.CagematchEventId);
        Assert.Equal(new DateTime(2021, 11, 13), result.EventDate);
        Assert.Equal(2, result.Matches.Count);
        Assert.Equal("MJF vs. Darby Allin", result.Matches[0].Participants);
        Assert.Equal("MJF", result.Matches[0].Result);
    }

    [Fact]
    public void IsBlockedGate_DetectsForbiddenPage()
    {
        Assert.True(CagematchPageParser.IsBlockedGate("<html><title>Forbidden</title></html>"));
    }

    [Fact]
    public void ChooseBestCandidate_ReturnsNullForTie()
    {
        const string Html = """
            <a href="?id=1&amp;nr=100">Heat Wave 1998</a>
            <a href="?id=1&amp;nr=200">Heat Wave 1998</a>
            """;

        Assert.Null(CagematchPageParser.ChooseBestCandidate(Html, "Heat Wave", 1998, null));
    }

    [Fact]
    public void ChooseBestCandidate_ReturnsNullForLowConfidenceSingleCandidate()
    {
        const string Html = """
            <a href="?id=1&amp;nr=100">Completely Different Show 2007</a>
            """;

        Assert.Null(CagematchPageParser.ChooseBestCandidate(Html, "CZW Cage Of Death 4", 2002, null));
    }
}
