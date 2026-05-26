using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Wrestling.Configuration;
using Jellyfin.Plugin.Wrestling.Models;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Wrestling.Services;

/// <summary>
/// Applies manually configured match cards directly to Jellyfin movie overviews.
/// </summary>
public interface IMatchCardApplyService
{
    /// <summary>
    /// Applies match cards to movies in the configured library.
    /// </summary>
    Task<MatchCardApplyResult> ApplyAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Direct match-card apply service.
/// </summary>
public class MatchCardApplyService : IMatchCardApplyService
{
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly ILogger<MatchCardApplyService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MatchCardApplyService"/> class.
    /// </summary>
    public MatchCardApplyService(
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        ILogger<MatchCardApplyService> logger)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MatchCardApplyResult> ApplyAsync(CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var mappings = config.ManualMappings
            .Concat(ManualMappingTextParser.Parse(config.ManualMappingsText))
            .ToList();

        var result = new MatchCardApplyResult
        {
            LibraryName = string.IsNullOrWhiteSpace(config.LibraryName) ? "Wrestling PPVs" : config.LibraryName,
            MappingCount = mappings.Count
        };

        if (mappings.Count == 0)
        {
            result.Message = "No manual mappings configured.";
            SaveResult(config, result);
            return result;
        }

        var movies = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Movie],
                Recursive = true
            })
            .OfType<Movie>()
            .ToList();

        foreach (var movie in movies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsInConfiguredLibrary(movie, result.LibraryName))
            {
                continue;
            }

            result.Scanned++;
            var mapping = ManualMappingService.FindMapping(mappings, movie.Name, movie.ProductionYear, movie.PremiereDate);
            if (mapping is null)
            {
                result.Skipped++;
                continue;
            }

            if (!TryApplyManualMapping(movie, mapping, config.IncludeRatingsInOverview, out var message))
            {
                result.Failed++;
                result.Failures.Add(string.Create(CultureInfo.InvariantCulture, $"{movie.Name}: {message}"));
                continue;
            }

            try
            {
                await _providerManager.SaveMetadataAsync(movie, ItemUpdateType.MetadataEdit).ConfigureAwait(false);
                result.Matched++;
                result.UpdatedItems.Add(movie.Name ?? movie.Id.ToString("N"));
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Failures.Add(string.Create(CultureInfo.InvariantCulture, $"{movie.Name}: {ex.Message}"));
                _logger.LogWarning(ex, "Failed to save wrestling match card for {MovieName}", movie.Name);
            }
        }

        result.Message = string.Create(
            CultureInfo.InvariantCulture,
            $"Scanned {result.Scanned}, updated {result.Matched}, skipped {result.Skipped}, failed {result.Failed}.");
        SaveResult(config, result);
        return result;
    }

    private bool IsInConfiguredLibrary(BaseItem item, string libraryName)
    {
        var folders = _libraryManager.GetCollectionFolders(item);
        return folders.Any(folder => string.Equals(folder.Name, libraryName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Applies one manual mapping to one movie in memory.
    /// </summary>
    public static bool TryApplyManualMapping(Movie movie, ManualPpvMapping mapping, bool includeRatings, out string message)
    {
        ArgumentNullException.ThrowIfNull(movie);
        ArgumentNullException.ThrowIfNull(mapping);

        var lookupKey = LookupKey.Build(movie.Name, movie.ProductionYear, movie.PremiereDate);
        var wrestlingEvent = ManualMappingService.BuildEventFromMapping(mapping, lookupKey);
        if (wrestlingEvent is null)
        {
            message = "Mapping has no pasted matches.";
            return false;
        }

        if (!wrestlingEvent.EventDate.HasValue)
        {
            wrestlingEvent.EventDate = movie.PremiereDate;
        }

        var updatedOverview = MatchCardFormatter.AppendOrReplace(movie.Overview, wrestlingEvent, includeRatings);
        if (string.Equals(movie.Overview, updatedOverview, StringComparison.Ordinal))
        {
            message = "Already up to date.";
            return true;
        }

        movie.Overview = updatedOverview;
        message = "Updated.";
        return true;
    }

    private static void SaveResult(PluginConfiguration config, MatchCardApplyResult result)
    {
        config.LastApplyResult = result.ToSummary();
        Plugin.Instance?.SaveConfiguration();
    }
}

/// <summary>
/// Result returned from direct match-card apply.
/// </summary>
public class MatchCardApplyResult
{
    /// <summary>
    /// Gets or sets the configured library name.
    /// </summary>
    public string LibraryName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of manual mappings.
    /// </summary>
    public int MappingCount { get; set; }

    /// <summary>
    /// Gets or sets the number of movies scanned in the configured library.
    /// </summary>
    public int Scanned { get; set; }

    /// <summary>
    /// Gets or sets the number of updated movies.
    /// </summary>
    public int Matched { get; set; }

    /// <summary>
    /// Gets or sets the number of skipped movies.
    /// </summary>
    public int Skipped { get; set; }

    /// <summary>
    /// Gets or sets the number of failed movies.
    /// </summary>
    public int Failed { get; set; }

    /// <summary>
    /// Gets or sets a summary message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets updated item names.
    /// </summary>
    public List<string> UpdatedItems { get; set; } = [];

    /// <summary>
    /// Gets or sets failure messages.
    /// </summary>
    public List<string> Failures { get; set; } = [];

    /// <summary>
    /// Formats a short diagnostic summary.
    /// </summary>
    public string ToSummary()
    {
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"Library: {LibraryName}"),
            string.Create(CultureInfo.InvariantCulture, $"Mappings: {MappingCount}"),
            Message
        };

        if (UpdatedItems.Count > 0)
        {
            lines.Add(string.Concat("Updated: ", string.Join(", ", UpdatedItems)));
        }

        if (Failures.Count > 0)
        {
            lines.Add(string.Concat("Failures: ", string.Join("; ", Failures)));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
