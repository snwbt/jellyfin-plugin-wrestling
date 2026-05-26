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
            Blocked = 1,
            Message = "Completed scan."
        };

        var summary = status.ToSummary();

        Assert.Contains("Wrestling PPVs", summary, StringComparison.Ordinal);
        Assert.Contains("Updated: 1", summary, StringComparison.Ordinal);
        Assert.Contains("Skipped: 1", summary, StringComparison.Ordinal);
        Assert.Contains("Failed: 1", summary, StringComparison.Ordinal);
        Assert.Contains("Blocked: 1", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void BlockedStatus_CarriesReasonAndAttemptedItem()
    {
        var status = WrestlingScanStatus.Running(["Wrestling PPVs"], 10);
        status.ScanState = WrestlingScanState.Blocked;
        status.Blocked = 1;
        status.BlockedReason = "Blocked by CageMatch: HTTP 403";
        status.LastAttemptedItem = "ECW Heat Wave 1998";

        Assert.Equal(WrestlingScanState.Blocked, status.ScanState);
        Assert.Equal("Blocked by CageMatch: HTTP 403", status.BlockedReason);
        Assert.Equal("ECW Heat Wave 1998", status.LastAttemptedItem);
    }
}
