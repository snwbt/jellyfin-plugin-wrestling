using Jellyfin.Plugin.Wrestling.Configuration;
using Jellyfin.Plugin.Wrestling.Services;
using Xunit;

namespace Jellyfin.Plugin.Wrestling.Tests;

public class ManualMappingTests
{
    [Fact]
    public void ParseMappingsBuildsManualPpvMappings()
    {
        const string Text = """
            Title: ECW Heat Wave 1998
            Year: 1998
            Date:
            CageMatch: https://www.cagematch.net/?id=1&nr=12345
            Matches:
            1. Tommy Dreamer, Sandman & Spike Dudley vs. The Dudley Boyz | Dudleyville Street Fight | 8.1 |
            2. Taz vs. Bam Bam Bigelow | FTW Championship Match | 7.4 | Taz
            """;

        var mappings = ManualMappingTextParser.Parse(Text);

        Assert.Single(mappings);
        Assert.Equal("ECW Heat Wave 1998", mappings[0].Title);
        Assert.Equal(1998, mappings[0].Year);
        Assert.Contains("Tommy Dreamer", mappings[0].MatchCardText, StringComparison.Ordinal);
    }

    [Fact]
    public void FindMappingMatchesByTitleAndYear()
    {
        var mappings = new[]
        {
            new ManualPpvMapping
            {
                Title = "ECW Heat Wave 1998",
                Year = 1998
            }
        };

        var mapping = ManualMappingService.FindMapping(mappings, "ECW Heat Wave 1998", 1998, null);

        Assert.NotNull(mapping);
    }

    [Fact]
    public void BuildEventFromMappingParsesPastedMatches()
    {
        var mapping = new ManualPpvMapping
        {
            Title = "ECW Heat Wave 1998",
            Year = 1998,
            CagematchEventId = "https://www.cagematch.net/?id=1&nr=12345",
            MatchCardText = """
                1. Tommy Dreamer, Sandman & Spike Dudley vs. The Dudley Boyz | Dudleyville Street Fight | 8.1 |
                2. Taz vs. Bam Bam Bigelow | FTW Championship Match | 7.4 | Taz
                """
        };

        var wrestlingEvent = ManualMappingService.BuildEventFromMapping(mapping, "ecw heat wave 1998|1998");

        Assert.NotNull(wrestlingEvent);
        Assert.Equal("12345", wrestlingEvent.CagematchEventId);
        Assert.Equal(2, wrestlingEvent.Matches.Count);
        Assert.Equal("Tommy Dreamer, Sandman & Spike Dudley vs. The Dudley Boyz", wrestlingEvent.Matches[0].Participants);
        Assert.Equal("Dudleyville Street Fight", wrestlingEvent.Matches[0].Stipulation);
        Assert.Equal("8.1", wrestlingEvent.Matches[0].Rating);
        Assert.Equal("Taz", wrestlingEvent.Matches[1].Result);
    }
}
