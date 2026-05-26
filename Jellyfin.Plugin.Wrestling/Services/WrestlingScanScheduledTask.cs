using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Wrestling.Services;

/// <summary>
/// Scheduled task for automatic CageMatch scans.
/// </summary>
public class WrestlingScanScheduledTask : IScheduledTask
{
    private readonly IWrestlingAutoScanService _scanService;

    /// <summary>
    /// Initializes a new instance of the <see cref="WrestlingScanScheduledTask"/> class.
    /// </summary>
    public WrestlingScanScheduledTask(IWrestlingAutoScanService scanService)
    {
        _scanService = scanService;
    }

    /// <inheritdoc />
    public string Name => "Scan wrestling match cards";

    /// <inheritdoc />
    public string Key => "WrestlingMatchCardScan";

    /// <inheritdoc />
    public string Description => "Searches CageMatch for selected wrestling libraries and writes match cards into movie overviews.";

    /// <inheritdoc />
    public string Category => "Wrestling";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (Plugin.Instance?.Configuration.EnableScheduledScan != true)
        {
            return;
        }

        if (string.Equals(_scanService.GetStatus().ScanState, WrestlingScanState.Blocked, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await _scanService.RunScanAsync(progress, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.DailyTrigger,
                TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
            }
        ];
    }
}
