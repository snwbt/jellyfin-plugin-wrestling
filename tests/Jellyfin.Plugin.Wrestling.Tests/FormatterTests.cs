using Jellyfin.Plugin.Wrestling.Models;
using Jellyfin.Plugin.Wrestling.Services;
using Xunit;

namespace Jellyfin.Plugin.Wrestling.Tests;

public class FormatterTests
{
    [Fact]
    public void AppendOrReplace_PreservesOriginalOverviewAndReplacesManagedBlock()
    {
        var wrestlingEvent = new WrestlingEvent
        {
            Name = "Example PPV",
            EventDate = new DateTime(2026, 1, 1),
            Matches =
            [
                new WrestlingMatch
                {
                    Order = 1,
                    Participants = "Wrestler A vs. Wrestler B",
                    Stipulation = "World Title Match",
                    Rating = "9.00",
                    Result = "Wrestler A"
                }
            ]
        };

        var first = MatchCardFormatter.AppendOrReplace("Original plot.", wrestlingEvent, true);
        var second = MatchCardFormatter.AppendOrReplace(first, wrestlingEvent, false);

        Assert.StartsWith("Original plot.", second, StringComparison.Ordinal);
        Assert.Contains("Match Card", second, StringComparison.Ordinal);
        Assert.DoesNotContain("Rating:", second, StringComparison.Ordinal);
        Assert.Equal(1, Count(second, MatchCardFormatter.StartMarker));
        Assert.DoesNotContain("Wrestler A:", second, StringComparison.Ordinal);
    }

    private static int Count(string value, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }
}
