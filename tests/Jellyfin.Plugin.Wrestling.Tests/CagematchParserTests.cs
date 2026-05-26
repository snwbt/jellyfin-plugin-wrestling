using Jellyfin.Plugin.Wrestling.Cagematch;
using Xunit;

namespace Jellyfin.Plugin.Wrestling.Tests;

public class CagematchParserTests
{
    [Fact]
    public void ParseEventPage_ExtractsMatchesWithoutLiveRequests()
    {
        var html = File.ReadAllText(Path.Combine("Fixtures", "cagematch-event.html"));

        var result = CagematchParser.ParseEventPage(html, "12345", "aew full gear|2021-11-13", "https://example.test");

        Assert.Equal("12345", result.CagematchEventId);
        Assert.Equal(new DateTime(2021, 11, 13), result.EventDate);
        Assert.Equal(2, result.Matches.Count);
        Assert.Equal("MJF vs. Darby Allin", result.Matches[0].Participants);
        Assert.Equal("MJF", result.Matches[0].Result);
        Assert.Equal("8.64", result.Matches[0].Rating);
    }

    [Fact]
    public void ParseSearchEventIds_ExtractsEventIdsFromLinks()
    {
        const string Html = """
            <a href="?id=1&amp;nr=111">First Event 2020</a>
            <a href="/?id=1&nr=222">Second</a>
            """;

        var ids = CagematchParser.ParseSearchEventIds(Html).ToArray();

        Assert.Equal(["111", "222"], ids);
    }

    [Fact]
    public void ParseSearchEventCandidates_ExtractsEventNames()
    {
        const string Html = """
            <a href="?id=1&amp;nr=111">CZW Cage Of Death 4 2002</a>
            """;

        var candidate = Assert.Single(CagematchParser.ParseSearchEventCandidates(Html));

        Assert.Equal("111", candidate.EventId);
        Assert.Equal("CZW Cage Of Death 4 2002", candidate.Name);
    }

    [Fact]
    public void CandidateMatcher_ChoosesBestTitleYearMatch()
    {
        var candidates = new[]
        {
            new CagematchSearchCandidate { EventId = "1", Name = "CZW Cage Of Death 3 2001" },
            new CagematchSearchCandidate { EventId = "2", Name = "CZW Cage Of Death 4 2002" }
        }.Select(candidate => CagematchCandidateMatcher.Score(candidate, "CZW: Cage Of Death 4", 2002, null)).ToArray();

        var best = CagematchCandidateMatcher.ChooseBest(candidates);

        Assert.NotNull(best);
        Assert.Equal("2", best.EventId);
    }

    [Fact]
    public void CandidateMatcher_ReturnsNullForTie()
    {
        var candidates = new[]
        {
            new CagematchSearchCandidate { EventId = "1", Name = "Heat Wave 1998" },
            new CagematchSearchCandidate { EventId = "2", Name = "Heat Wave 1998" }
        }.Select(candidate => CagematchCandidateMatcher.Score(candidate, "Heat Wave", 1998, null)).ToArray();

        Assert.Null(CagematchCandidateMatcher.ChooseBest(candidates));
    }
}
