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
    /// Runs a scan and waits for completion.
    /// </summary>
    Task<WrestlingScanStatus> RunScanAsync(IProgress<double>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Gets current scan status.
    /// </summary>
    WrestlingScanStatus GetStatus();
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
    private readonly ILogger<WrestlingAutoScanService> _logger;
    private WrestlingScanStatus _status = WrestlingScanStatus.Idle();

    /// <summary>
    /// Initializes a new instance of the <see cref="WrestlingAutoScanService"/> class.
    /// </summary>
    public WrestlingAutoScanService(
        ILibraryManager libraryManager,
        ICagematchClient cagematchClient,
        IWrestlingMatchCache cache,
        ILogger<WrestlingAutoScanService> logger)
    {
        _libraryManager = libraryManager;
        _cagematchClient = cagematchClient;
        _cache = cache;
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
                CollectionType = folder.CollectionType.ToString() ?? string.Empty,
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
    public async Task<WrestlingScanStatus> RunScanAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (!await _scanLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return GetStatus();
        }

        try
        {
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
                cancellationToken.ThrowIfCancellationRequested();

                var movie = movies[index];
                _status.CurrentItem = movie.Name ?? movie.Id.ToString("N");
                _status.Queued = Math.Max(0, movies.Count - index - 1);

                var itemResult = await ProcessMovieAsync(movie, cancellationToken).ConfigureAwait(false);
                _status.Items.Add(itemResult);
                ApplyCounters(itemResult);
                progress?.Report(movies.Count == 0 ? 100 : 100.0 * (index + 1) / movies.Count);
                SaveSummary(_status);
            }

            _status.IsRunning = false;
            _status.CurrentItem = string.Empty;
            _status.Message = string.Format(
                CultureInfo.InvariantCulture,
                "Completed scan. Updated {0}, skipped {1}, failed {2}.",
                _status.Updated,
                _status.Skipped,
                _status.Failed);
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
            var wrestlingEvent = _cache.GetByLookupKey(lookupKey);
            if (wrestlingEvent is null)
            {
                var search = await _cagematchClient.SearchEventAsync(movie.Name ?? string.Empty, movie.ProductionYear, movie.PremiereDate, cancellationToken).ConfigureAwait(false);
                result.SearchMessage = search.Message;
                result.CandidateCount = search.Candidates.Count;
                result.CagematchEventId = search.EventId;

                if (string.IsNullOrWhiteSpace(search.EventId))
                {
                    result.Status = WrestlingScanItemStatus.Skipped;
                    result.Reason = string.IsNullOrWhiteSpace(search.Message) ? "No CageMatch candidate found." : search.Message;
                    _cache.RecordManualLookup(movie.Name ?? string.Empty, movie.ProductionYear, movie.PremiereDate, result.Reason);
                    return result;
                }

                wrestlingEvent = await _cagematchClient.GetEventByIdAsync(search.EventId, lookupKey, cancellationToken).ConfigureAwait(false);
                if (wrestlingEvent is null)
                {
                    result.Status = WrestlingScanItemStatus.Skipped;
                    result.Reason = "CageMatch event did not yield match rows.";
                    return result;
                }

                _cache.Save(wrestlingEvent);
            }

            result.CagematchEventId = wrestlingEvent.CagematchEventId;
            var updatedOverview = MatchCardFormatter.AppendOrReplace(movie.Overview, wrestlingEvent, GetConfiguration().IncludeRatingsInOverview);
            movie.ProviderIds[WrestlingMovieMetadataProvider.ProviderId] = wrestlingEvent.CagematchEventId;

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
            result.Status = WrestlingScanItemStatus.Failed;
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
    public bool IsRunning { get; set; }

    public bool IsQueued { get; set; }

    public int Total { get; set; }

    public int Queued { get; set; }

    public int Updated { get; set; }

    public int Skipped { get; set; }

    public int Failed { get; set; }

    public string CurrentItem { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string LastCagematchUrl { get; set; } = string.Empty;

    public string LastCagematchStatus { get; set; } = string.Empty;

    public string LastResult { get; set; } = string.Empty;

    public IReadOnlyList<string> SelectedLibraries { get; set; } = [];

    public List<WrestlingScanItemResult> Items { get; set; } = [];

    public static WrestlingScanStatus Idle()
    {
        return new WrestlingScanStatus { Message = "Idle." };
    }

    public static WrestlingScanStatus QueuedStatus(IReadOnlyList<string> selectedLibraries)
    {
        return new WrestlingScanStatus { IsQueued = true, Message = "Scan queued.", SelectedLibraries = selectedLibraries };
    }

    public static WrestlingScanStatus Running(IReadOnlyList<string> selectedLibraries, int total)
    {
        return new WrestlingScanStatus { IsRunning = true, Total = total, Queued = total, Message = "Scan running.", SelectedLibraries = selectedLibraries };
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
            CurrentItem = CurrentItem,
            Message = Message,
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
            "Libraries: {0}{1}Total: {2}{1}Updated: {3}{1}Skipped: {4}{1}Failed: {5}{1}{6}",
            string.Join(", ", SelectedLibraries),
            Environment.NewLine,
            Total,
            Updated,
            Skipped,
            Failed,
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
/// String constants for item scan statuses.
/// </summary>
public static class WrestlingScanItemStatus
{
    public const string Updated = "updated";

    public const string Skipped = "skipped";

    public const string Failed = "failed";
}

#pragma warning restore CS1591
