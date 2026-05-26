using Jellyfin.Plugin.Wrestling.Services;
using Xunit;

namespace Jellyfin.Plugin.Wrestling.Tests;

public class WorkerStatusTests
{
    [Fact]
    public void Heartbeat_MarksWorkerConnected()
    {
        var service = new WorkerStatusService();

        var status = service.RecordHeartbeat(new WorkerHeartbeatRequest
        {
            WorkerVersion = "1.0.0.7",
            State = "Running",
            CurrentItem = "ECW Heat Wave 1998",
            Processed = 1,
            Total = 3
        });

        Assert.True(status.IsConnected);
        Assert.Equal("1.0.0.7", status.WorkerVersion);
        Assert.Equal("ECW Heat Wave 1998", status.CurrentItem);
        Assert.Equal(1, status.Processed);
        Assert.Equal(3, status.Total);
    }

    [Fact]
    public void Command_UsesProvidedServerUrl()
    {
        var service = new WorkerStatusService();

        var command = service.GetCommand("http://jellyfin.local:8096");

        Assert.Contains("http://jellyfin.local:8096", command.Command, StringComparison.Ordinal);
        Assert.Contains("Wrestling.CacheWorker_", command.WorkerDownloadUrl, StringComparison.Ordinal);
    }
}
