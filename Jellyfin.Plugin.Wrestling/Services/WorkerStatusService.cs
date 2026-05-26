using System;
using Jellyfin.Plugin.Wrestling.Configuration;

namespace Jellyfin.Plugin.Wrestling.Services;

#pragma warning disable CS1591

/// <summary>
/// Tracks external browser worker heartbeat/status.
/// </summary>
public interface IWorkerStatusService
{
    /// <summary>
    /// Records a worker heartbeat.
    /// </summary>
    WorkerStatus RecordHeartbeat(WorkerHeartbeatRequest request);

    /// <summary>
    /// Gets current worker status.
    /// </summary>
    WorkerStatus GetStatus();

    /// <summary>
    /// Gets worker setup command.
    /// </summary>
    WorkerCommandInfo GetCommand(string? serverUrl);
}

/// <summary>
/// In-memory worker status service.
/// </summary>
public class WorkerStatusService : IWorkerStatusService
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);
    private readonly object _sync = new();
    private WorkerStatus _status = WorkerStatus.Disconnected();

    /// <inheritdoc />
    public WorkerStatus RecordHeartbeat(WorkerHeartbeatRequest request)
    {
        lock (_sync)
        {
            _status = new WorkerStatus
            {
                IsConnected = true,
                State = string.IsNullOrWhiteSpace(request.State) ? "Running" : request.State,
                WorkerVersion = request.WorkerVersion,
                CurrentItem = request.CurrentItem,
                CurrentUrl = request.CurrentUrl,
                LastError = request.LastError,
                Processed = request.Processed,
                Skipped = request.Skipped,
                Failed = request.Failed,
                Total = request.Total,
                NextAllowedRequestUtc = request.NextAllowedRequestUtc,
                LastHeartbeatUtc = DateTime.UtcNow
            };

            return GetStatus();
        }
    }

    /// <inheritdoc />
    public WorkerStatus GetStatus()
    {
        lock (_sync)
        {
            if (_status.LastHeartbeatUtc is not null && DateTime.UtcNow - _status.LastHeartbeatUtc > StaleAfter)
            {
                return _status with { IsConnected = false, State = "Disconnected" };
            }

            return _status;
        }
    }

    /// <inheritdoc />
    public WorkerCommandInfo GetCommand(string? serverUrl)
    {
        var baseUrl = string.IsNullOrWhiteSpace(serverUrl) ? "http://localhost:8096" : serverUrl.TrimEnd('/');
        var version = Plugin.Instance?.Configuration.GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0.7";
        var workerUrl = $"https://raw.githubusercontent.com/snwbt/jellyfin-plugin-wrestling/main/release-assets/Wrestling.CacheWorker_{version}.zip";
        return new WorkerCommandInfo
        {
            WorkerDownloadUrl = workerUrl,
            Command = $".\\Wrestling.CacheWorker.exe --jellyfin-url {baseUrl} --api-key YOUR_JELLYFIN_API_KEY --limit 3"
        };
    }
}

/// <summary>
/// Browser worker heartbeat request.
/// </summary>
public class WorkerHeartbeatRequest
{
    public string WorkerVersion { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string CurrentItem { get; set; } = string.Empty;

    public string CurrentUrl { get; set; } = string.Empty;

    public string LastError { get; set; } = string.Empty;

    public int Processed { get; set; }

    public int Skipped { get; set; }

    public int Failed { get; set; }

    public int Total { get; set; }

    public DateTime? NextAllowedRequestUtc { get; set; }
}

/// <summary>
/// Browser worker status.
/// </summary>
public record WorkerStatus
{
    public bool IsConnected { get; init; }

    public string State { get; init; } = "Disconnected";

    public string WorkerVersion { get; init; } = string.Empty;

    public string CurrentItem { get; init; } = string.Empty;

    public string CurrentUrl { get; init; } = string.Empty;

    public string LastError { get; init; } = string.Empty;

    public int Processed { get; init; }

    public int Skipped { get; init; }

    public int Failed { get; init; }

    public int Total { get; init; }

    public DateTime? NextAllowedRequestUtc { get; init; }

    public DateTime? LastHeartbeatUtc { get; init; }

    public static WorkerStatus Disconnected()
    {
        return new WorkerStatus();
    }
}

/// <summary>
/// Worker setup command response.
/// </summary>
public class WorkerCommandInfo
{
    public string WorkerDownloadUrl { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;
}

#pragma warning restore CS1591
