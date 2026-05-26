using Jellyfin.Plugin.Wrestling.Services;
using Xunit;

namespace Jellyfin.Plugin.Wrestling.Tests;

public class ScanStatusTests
{
    [Fact]
    public void ToSummary_IncludesCountersAndLibraries()
    {
        var status = new WrestlingScanStatus
        {
            SelectedLibraries = ["Wrestling PPVs"],
            Total = 3,
            Updated = 1,
            Skipped = 1,
            Failed = 1,
            Message = "Completed scan."
        };

        var summary = status.ToSummary();

        Assert.Contains("Wrestling PPVs", summary, StringComparison.Ordinal);
        Assert.Contains("Updated: 1", summary, StringComparison.Ordinal);
        Assert.Contains("Skipped: 1", summary, StringComparison.Ordinal);
        Assert.Contains("Failed: 1", summary, StringComparison.Ordinal);
    }
}
