using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Wrestling.Cagematch;
using Jellyfin.Plugin.Wrestling.Configuration;
using Jellyfin.Plugin.Wrestling.Models;
using Jellyfin.Plugin.Wrestling.Providers;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Wrestling.Services;

#pragma warning disable CS1591

/// <summary>
/// Scans selected Jellyfin libraries and auto-populates match cards from CageMatch.
/// </summary>
public interface IWrestlingAutoScanService
{
    /// <summary>
    /// Gets selectable Jellyfin libraries.
    /// </summary>
    IReadOnlyList<WrestlingLibraryInfo> GetLibraries();

    /// <summary>
    /// Queues a scan in the background.
    /// </summary>
    Task<WrestlingScanStatus> QueueScanAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Cancels the current scan.
    /// </summary>
    WrestlingScanStatus CancelScan();

    /// <summary>
    /// Clears scan status.
    /// </summary>
    WrestlingScanStatus ClearStatus();

    /// <summary>
    /// Runs a scan and waits for completion.
    /// </summary>
    Task<WrestlingScanStatus> RunScanAsync(IProgress<double>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Gets current scan status.
    /// </summary>
    WrestlingScanStatus GetStatus();

    /// <summary>
    /// Gets movie metadata for selected scan libraries.
    /// </summary>
    IReadOnlyList<WrestlingScanQueueItem> GetQueueItems();
}

/// <summary>
/// Auto scan implementation.
/// </summary>
public sealed class WrestlingAutoScanService : IWrestlingAutoScanService, IDisposable
{
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private readonly ILibraryManager _libraryManager;
    private readonly ICagematchClient _cagematchClient;
    private readonly IWrestlingMatchCache _cache;
    private readonly IImportedMatchCacheService _importedCache;
    private readonly ILogger<WrestlingAutoScanService> _logger;
    private WrestlingScanStatus _status = WrestlingScanStatus.Idle();
    private CancellationTokenSource? _scanCancellation;

    /// <summary>
    /// Initializes a new instance of the <see cref="WrestlingAutoScanService"/> class.
    /// </summary>
    public WrestlingAutoScanService(
        ILibraryManager libraryManager,
        ICagematchClient cagematchClient,
        IWrestlingMatchCache cache,
        IImportedMatchCacheService importedCache,
        ILogger<WrestlingAutoScanService> logger)
    {
        _libraryManager = libraryManager;
        _cagematchClient = cagematchClient;
        _cache = cache;
        _importedCache = importedCache;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<WrestlingLibraryInfo> GetLibraries()
    {
        var selected = GetSelectedLibraryNames();
        return _libraryManager.GetVirtualFolders()
            .Select(folder => new WrestlingLibraryInfo
            {
                Id = folder.Name ?? string.Empty,
                Name = folder.Name ?? string.Empty,
                CollectionType = folder.CollectionType?.ToString() ?? string.Empty,
                IsSelected = selected.Contains(folder.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            })
            .Where(folder => !string.IsNullOrWhiteSpace(folder.Name))
            .OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc />
    public Task<WrestlingScanStatus> QueueScanAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_scanLock.CurrentCount == 0)
        {
            return Task.FromResult(GetStatus());
        }

        if (string.Equals(_status.ScanState, WrestlingScanState.Blocked, StringComparison.OrdinalIgnoreCase)
            && IsLiveCagematchMode())
        {
            _status.Message = "Scan is blocked by CageMatch. Clear status before starting a new live scan.";
            return Task.FromResult(GetStatus());
        }

        _status = WrestlingScanStatus.QueuedStatus(GetSelectedLibraryNames());
        _ = Task.Run(async () =>
        {
            try
            {
                await RunScanAsync(null, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Wrestling CageMatch scan failed.");
                _status.IsRunning = false;
                _status.Message = ex.Message;
                SaveSummary(_status);
            }
        }, CancellationToken.None);

        return Task.FromResult(GetStatus());
    }

    /// <inheritdoc />
    public WrestlingScanStatus CancelScan()
    {
        _scanCancellation?.Cancel();
        if (_status.IsRunning || _status.IsQueued)
        {
            _status.ScanState = WrestlingScanState.Cancelled;
            _status.IsRunning = false;
            _status.IsQueued = false;
            _status.Message = "Scan cancelled.";
            SaveSummary(_status);
        }

        return GetStatus();
    }

    /// <inheritdoc />
    public WrestlingScanStatus ClearStatus()
    {
        _scanCancellation?.Cancel();
        _status = WrestlingScanStatus.Idle();
        SaveSummary(_status);
        return GetStatus();
    }

    /// <inheritdoc />
    public async Task<WrestlingScanStatus> RunScanAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (!await _scanLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return GetStatus();
        }

        try
        {
            _scanCancellation?.Dispose();
            _scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var scanToken = _scanCancellation.Token;
            var selectedLibraries = GetSelectedLibraryNames();
            var movies = GetEligibleMovies(selectedLibraries).ToList();
            _status = WrestlingScanStatus.Running(selectedLibraries, movies.Count);

            if (selectedLibraries.Count == 0)
            {
                _status.IsRunning = false;
                _status.Message = "No libraries selected.";
                SaveSummary(_status);
                return GetStatus();
            }

            for (var index = 0; index < movies.Count; index++)
            {
                scanToken.ThrowIfCancellationRequested();

                var movie = movies[index];
                _status.CurrentItem = movie.Name ?? movie.Id.ToString("N");
                _status.LastAttemptedItem = _status.CurrentItem;
                _status.Queued = Math.Max(0, movies.Count - index - 1);

                var itemResult = await ProcessMovieAsync(movie, scanToken).ConfigureAwait(false);
                _status.Items.Add(itemResult);
                ApplyCounters(itemResult);
                progress?.Report(movies.Count == 0 ? 100 : 100.0 * (index + 1) / movies.Count);
                SaveSummary(_status);

                if (itemResult.Status == WrestlingScanItemStatus.Blocked)
                {
                    _status.ScanState = WrestlingScanState.Blocked;
                    _status.IsRunning = false;
                    _status.IsQueued = false;
                    _status.BlockedReason = itemResult.Reason;
                    _status.BlockedAtUtc = DateTime.UtcNow;
                    _status.Message = string.Concat("Blocked by CageMatch: ", itemResult.Reason);
                    SaveSummary(_status);
                    return GetStatus();
                }
            }

            _status.IsRunning = false;
            _status.ScanState = WrestlingScanState.Completed;
            _status.CurrentItem = string.Empty;
            _status.Message = string.Format(
                CultureInfo.InvariantCulture,
                "Completed scan. Updated {0}, skipped {1}, failed {2}, blocked {3}.",
                _status.Updated,
                _status.Skipped,
                _status.Failed,
                _status.Blocked);
            SaveSummary(_status);
            return GetStatus();
        }
        catch (OperationCanceledException)
        {
            _status.ScanState = WrestlingScanState.Cancelled;
            _status.IsRunning = false;
            _status.IsQueued = false;
            _status.Message = "Scan cancelled.";
            SaveSummary(_status);
            return GetStatus();
        }
        finally
        {
            _scanLock.Release();
        }
    }

    /// <inheritdoc />
    public WrestlingScanStatus GetStatus()
    {
        var config = GetConfiguration();
        var copy = _status.Clone();
        copy.LastCagematchUrl = config.LastCagematchUrl;
        copy.LastCagematchStatus = config.LastCagematchStatus;
        copy.LastResult = config.LastScanResult;
        copy.SelectedLibraries = GetSelectedLibraryNames();
        return copy;
    }

    /// <inheritdoc />
    public IReadOnlyList<WrestlingScanQueueItem> GetQueueItems()
    {
        var selectedLibraries = GetSelectedLibraryNames();
        return GetEligibleMovies(selectedLibraries)
            .Select(movie => new WrestlingScanQueueItem
            {
                ItemId = movie.Id,
                Name = movie.Name ?? string.Empty,
                Year = movie.ProductionYear,
                PremiereDate = movie.PremiereDate,
                LookupKey = LookupKey.Build(movie.Name, movie.ProductionYear, movie.PremiereDate)
            })
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<WrestlingScanItemResult> ProcessMovieAsync(Movie movie, CancellationToken cancellationToken)
    {
        var result = new WrestlingScanItemResult
        {
            ItemId = movie.Id,
            Name = movie.Name ?? string.Empty,
            Year = movie.ProductionYear,
            PremiereDate = movie.PremiereDate
        };

        try
        {
            var lookupKey = LookupKey.Build(movie.Name, movie.ProductionYear, movie.PremiereDate);
            var config = GetConfiguration();
            var mode = NormalizeDataSourceMode(config.DataSourceMode);
            WrestlingEvent? wrestlingEvent = null;

            if (mode == WrestlingDataSourceMode.LiveCagematch)
            {
                wrestlingEvent = _cache.GetByLookupKey(lookupKey);
            }

            if (wrestlingEvent is null)
            {
                wrestlingEvent = _importedCache.FindEvent(movie.Name, movie.ProductionYear, movie.PremiereDate);
                if (wrestlingEvent is not null)
                {
                    result.SearchMessage = mode == WrestlingDataSourceMode.WorkerCache ? "Used browser worker cache." : "Used imported cache.";
                }
            }

            if (wrestlingEvent is null)
            {
                if (mode != WrestlingDataSourceMode.LiveCagematch)
                {
                    result.Status = WrestlingScanItemStatus.Skipped;
                    result.SearchMessage = mode == WrestlingDataSourceMode.WorkerCache ? "Worker cache mode." : "CSV import mode.";
                    result.Reason = mode == WrestlingDataSourceMode.WorkerCache
                        ? "No worker cache found yet. Run the browser worker first."
                        : "No imported cache match found. Import CSV cache first.";
                    return result;
                }

                wrestlingEvent = await LookupLiveCagematchAsync(movie, lookupKey, result, cancellationToken).ConfigureAwait(false);
                if (wrestlingEvent is null)
                {
                    return result;
                }
            }

            result.CagematchEventId = wrestlingEvent.CagematchEventId;
            var updatedOverview = MatchCardFormatter.AppendOrReplace(movie.Overview, wrestlingEvent, config.IncludeRatingsInOverview);
            if (!string.IsNullOrWhiteSpace(wrestlingEvent.CagematchEventId))
            {
                movie.ProviderIds[WrestlingMovieMetadataProvider.ProviderId] = wrestlingEvent.CagematchEventId;
            }

            if (!string.Equals(movie.Overview, updatedOverview, StringComparison.Ordinal))
            {
                movie.Overview = updatedOverview;
                await movie.UpdateToRepositoryAsync(MediaBrowser.Controller.Library.ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                result.Status = WrestlingScanItemStatus.Updated;
                result.Reason = "Overview updated.";
            }
            else
            {
                await movie.UpdateToRepositoryAsync(MediaBrowser.Controller.Library.ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                result.Status = WrestlingScanItemStatus.Skipped;
                result.Reason = "Already up to date.";
            }
        }
        catch (CagematchBlockedException ex)
        {
            result.Status = WrestlingScanItemStatus.Blocked;
            result.Reason = ex.Message;
            _cache.RecordManualLookup(movie.Name ?? string.Empty, movie.ProductionYear, movie.PremiereDate, ex.Message);
        }
        catch (Exception ex)
        {
            result.Status = WrestlingScanItemStatus.Failed;
            result.Reason = ex.Message;
            _logger.LogWarning(ex, "Failed to scan wrestling movie {MovieName}", movie.Name);
        }

        return result;
    }

    private async Task<WrestlingEvent?> LookupLiveCagematchAsync(Movie movie, string lookupKey, WrestlingScanItemResult result, CancellationToken cancellationToken)
    {
        var search = await _cagematchClient.SearchEventAsync(movie.Name ?? string.Empty, movie.ProductionYear, movie.PremiereDate, cancellationToken).ConfigureAwait(false);
        result.SearchMessage = search.Message;
        result.CandidateCount = search.Candidates.Count;
        result.CagematchEventId = search.EventId;

        if (search.IsBlocked)
        {
            result.Status = WrestlingScanItemStatus.Blocked;
            result.Reason = string.IsNullOrWhiteSpace(search.Message) ? "CageMatch lookup was blocked." : search.Message;
            _cache.RecordManualLookup(movie.Name ?? string.Empty, movie.ProductionYear, movie.PremiereDate, result.Reason);
            return null;
        }

        if (string.IsNullOrWhiteSpace(search.EventId))
        {
            result.Status = WrestlingScanItemStatus.Skipped;
            result.Reason = string.IsNullOrWhiteSpace(search.Message) ? "No CageMatch candidate found." : search.Message;
            _cache.RecordManualLookup(movie.Name ?? string.Empty, movie.ProductionYear, movie.PremiereDate, result.Reason);
            return null;
        }

        var wrestlingEvent = await _cagematchClient.GetEventByIdAsync(search.EventId, lookupKey, cancellationToken).ConfigureAwait(false);
        if (wrestlingEvent is null)
        {
            result.Status = WrestlingScanItemStatus.Skipped;
            result.Reason = "CageMatch event did not yield match rows.";
            return null;
        }

        _cache.Save(wrestlingEvent);
        return wrestlingEvent;
    }

    private static bool IsLiveCagematchMode()
    {
        return NormalizeDataSourceMode(GetConfiguration().DataSourceMode) == WrestlingDataSourceMode.LiveCagematch;
    }

    private static string NormalizeDataSourceMode(string? mode)
    {
        if (string.Equals(mode, WrestlingDataSourceMode.LiveCagematch, StringComparison.OrdinalIgnoreCase))
        {
            return WrestlingDataSourceMode.LiveCagematch;
        }

        if (string.Equals(mode, WrestlingDataSourceMode.CsvImport, StringComparison.OrdinalIgnoreCase))
        {
            return WrestlingDataSourceMode.CsvImport;
        }

        return WrestlingDataSourceMode.WorkerCache;
    }

    private IEnumerable<Movie> GetEligibleMovies(IReadOnlyCollection<string> selectedLibraries)
    {
        if (selectedLibraries.Count == 0)
        {
            yield break;
        }

        var movies = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Movie],
                Recursive = true
            })
            .OfType<Movie>();

        foreach (var movie in movies)
        {
            var folders = _libraryManager.GetCollectionFolders(movie).Select(folder => folder.Name ?? string.Empty);
            if (folders.Any(folder => selectedLibraries.Contains(folder, StringComparer.OrdinalIgnoreCase)))
            {
                yield return movie;
            }
        }
    }

    private void ApplyCounters(WrestlingScanItemResult result)
    {
        if (result.Status == WrestlingScanItemStatus.Updated)
        {
            _status.Updated++;
        }
        else if (result.Status == WrestlingScanItemStatus.Failed)
        {
            _status.Failed++;
        }
        else if (result.Status == WrestlingScanItemStatus.Blocked)
        {
            _status.Blocked++;
        }
        else
        {
            _status.Skipped++;
        }
    }

    private static IReadOnlyList<string> GetSelectedLibraryNames()
    {
        var config = GetConfiguration();
        if (config.SelectedLibraryNames is { Count: > 0 })
        {
            return config.SelectedLibraryNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return string.IsNullOrWhiteSpace(config.LibraryName) ? [] : [config.LibraryName];
    }

    private static PluginConfiguration GetConfiguration()
    {
        return Plugin.Instance?.Configuration ?? new PluginConfiguration();
    }

    private static void SaveSummary(WrestlingScanStatus status)
    {
        var config = GetConfiguration();
        config.LastScanResult = status.ToSummary();
        Plugin.Instance?.SaveConfiguration();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _scanCancellation?.Dispose();
        _scanLock.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// A selectable Jellyfin library.
/// </summary>
public class WrestlingLibraryInfo
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string CollectionType { get; set; } = string.Empty;

    public bool IsSelected { get; set; }
}

/// <summary>
/// Current automatic scan status.
/// </summary>
public class WrestlingScanStatus
{
    public string ScanState { get; set; } = WrestlingScanState.Idle;

    public bool IsRunning { get; set; }

    public bool IsQueued { get; set; }

    public int Total { get; set; }

    public int Queued { get; set; }

    public int Updated { get; set; }

    public int Skipped { get; set; }

    public int Failed { get; set; }

    public int Blocked { get; set; }

    public string CurrentItem { get; set; } = string.Empty;

    public string LastAttemptedItem { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string BlockedReason { get; set; } = string.Empty;

    public DateTime? BlockedAtUtc { get; set; }

    public string LastCagematchUrl { get; set; } = string.Empty;

    public string LastCagematchStatus { get; set; } = string.Empty;

    public string LastResult { get; set; } = string.Empty;

    public IReadOnlyList<string> SelectedLibraries { get; set; } = [];

    public List<WrestlingScanItemResult> Items { get; set; } = [];

    public static WrestlingScanStatus Idle()
    {
        return new WrestlingScanStatus { ScanState = WrestlingScanState.Idle, Message = "Idle." };
    }

    public static WrestlingScanStatus QueuedStatus(IReadOnlyList<string> selectedLibraries)
    {
        return new WrestlingScanStatus { ScanState = WrestlingScanState.Queued, IsQueued = true, Message = "Scan queued.", SelectedLibraries = selectedLibraries };
    }

    public static WrestlingScanStatus Running(IReadOnlyList<string> selectedLibraries, int total)
    {
        return new WrestlingScanStatus { ScanState = WrestlingScanState.Running, IsRunning = true, Total = total, Queued = total, Message = "Scan running.", SelectedLibraries = selectedLibraries };
    }

    public WrestlingScanStatus Clone()
    {
        return new WrestlingScanStatus
        {
            IsRunning = IsRunning,
            IsQueued = IsQueued,
            Total = Total,
            Queued = Queued,
            Updated = Updated,
            Skipped = Skipped,
            Failed = Failed,
            Blocked = Blocked,
            CurrentItem = CurrentItem,
            LastAttemptedItem = LastAttemptedItem,
            Message = Message,
            ScanState = ScanState,
            BlockedReason = BlockedReason,
            BlockedAtUtc = BlockedAtUtc,
            LastCagematchUrl = LastCagematchUrl,
            LastCagematchStatus = LastCagematchStatus,
            LastResult = LastResult,
            SelectedLibraries = SelectedLibraries.ToList(),
            Items = Items.ToList()
        };
    }

    public string ToSummary()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "State: {0}{1}Libraries: {2}{1}Total: {3}{1}Updated: {4}{1}Skipped: {5}{1}Failed: {6}{1}Blocked: {7}{1}{8}",
            ScanState,
            Environment.NewLine,
            string.Join(", ", SelectedLibraries),
            Total,
            Updated,
            Skipped,
            Failed,
            Blocked,
            Message);
    }
}

/// <summary>
/// Scan status for one movie.
/// </summary>
public class WrestlingScanItemResult
{
    public Guid ItemId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int? Year { get; set; }

    public DateTime? PremiereDate { get; set; }

    public string CagematchEventId { get; set; } = string.Empty;

    public int CandidateCount { get; set; }

    public string SearchMessage { get; set; } = string.Empty;

    public string Status { get; set; } = WrestlingScanItemStatus.Skipped;

    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Queue item metadata used by the external cache worker.
/// </summary>
public class WrestlingScanQueueItem
{
    /// <summary>
    /// Gets or sets Jellyfin item id.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets movie title.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets production year.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets premiere date.
    /// </summary>
    public DateTime? PremiereDate { get; set; }

    /// <summary>
    /// Gets or sets normalized lookup key.
    /// </summary>
    public string LookupKey { get; set; } = string.Empty;
}

/// <summary>
/// String constants for item scan statuses.
/// </summary>
public static class WrestlingScanItemStatus
{
    public const string Updated = "updated";

    public const string Skipped = "skipped";

    public const string Failed = "failed";

    public const string Blocked = "blocked";
}

public static class WrestlingScanState
{
    public const string Idle = "Idle";

    public const string Queued = "Queued";

    public const string Running = "Running";

    public const string Blocked = "Blocked";

    public const string Cancelled = "Cancelled";

    public const string Completed = "Completed";
}

#pragma warning restore CS1591
