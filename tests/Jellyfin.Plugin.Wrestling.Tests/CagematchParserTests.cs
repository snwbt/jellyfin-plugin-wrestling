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
            <a href="?id=1&amp;nr=111">First</a>
            <a href="/?id=1&nr=222">Second</a>
            """;

        var ids = CagematchParser.ParseSearchEventIds(Html).ToArray();

        Assert.Equal(["111", "222"], ids);
    }
}
