using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Wrestling.Cagematch;
using Jellyfin.Plugin.Wrestling.Models;
using Jellyfin.Plugin.Wrestling.Services;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Wrestling.Providers;

/// <summary>
/// Movie metadata provider that appends spoiler-safe wrestling match cards.
/// </summary>
public class WrestlingMovieMetadataProvider : IRemoteMetadataProvider<Movie, MovieInfo>
{
    /// <summary>
    /// CageMatch provider id key.
    /// </summary>
    public const string ProviderId = "CageMatch";

    private readonly ICagematchClient _cagematchClient;
    private readonly IWrestlingMatchCache _cache;
    private readonly ILogger<WrestlingMovieMetadataProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WrestlingMovieMetadataProvider"/> class.
    /// </summary>
    public WrestlingMovieMetadataProvider(
        ICagematchClient cagematchClient,
        IWrestlingMatchCache cache,
        ILogger<WrestlingMovieMetadataProvider> logger)
    {
        _cagematchClient = cagematchClient;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Wrestling Match Cards";

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
    {
        var providerId = TryGetCagematchId(searchInfo.ProviderIds);
        if (!string.IsNullOrWhiteSpace(providerId))
        {
            return
            [
                new RemoteSearchResult
                {
                    Name = searchInfo.Name,
                    ProductionYear = searchInfo.Year,
                    PremiereDate = searchInfo.PremiereDate,
                    ProviderIds = new Dictionary<string, string> { [ProviderId] = providerId }
                }
            ];
        }

        var result = await _cagematchClient.SearchEventAsync(
            searchInfo.Name,
            searchInfo.Year,
            searchInfo.PremiereDate,
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(result.EventId))
        {
            return [];
        }

        return
        [
            new RemoteSearchResult
            {
                Name = searchInfo.Name,
                ProductionYear = searchInfo.Year,
                PremiereDate = searchInfo.PremiereDate,
                ProviderIds = new Dictionary<string, string> { [ProviderId] = result.EventId }
            }
        ];
    }

    /// <inheritdoc />
    public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
    {
        var lookupKey = LookupKey.Build(info.Name, info.Year, info.PremiereDate);
        var providerId = TryGetCagematchId(info.ProviderIds);
        WrestlingEvent? wrestlingEvent = null;

        if (!string.IsNullOrWhiteSpace(providerId))
        {
            wrestlingEvent = _cache.GetByCagematchId(providerId)
                ?? await FetchAndCacheByIdAsync(providerId, lookupKey, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            wrestlingEvent = _cache.GetByLookupKey(lookupKey);
            if (wrestlingEvent is null)
            {
                var search = await _cagematchClient.SearchEventAsync(info.Name, info.Year, info.PremiereDate, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(search.EventId))
                {
                    wrestlingEvent = await FetchAndCacheByIdAsync(search.EventId, lookupKey, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    _cache.RecordManualLookup(info.Name, info.Year, info.PremiereDate, search.Message);
                }
            }
        }

        if (wrestlingEvent is null)
        {
            return new MetadataResult<Movie> { HasMetadata = false };
        }

        var movie = new Movie
        {
            Name = info.Name ?? wrestlingEvent.Name,
            Overview = MatchCardFormatter.AppendOrReplace(null, wrestlingEvent, Plugin.Instance?.Configuration.IncludeRatingsInOverview ?? true),
            PremiereDate = wrestlingEvent.EventDate ?? info.PremiereDate,
            ProductionYear = wrestlingEvent.EventDate?.Year ?? info.Year
        };

        movie.ProviderIds[ProviderId] = wrestlingEvent.CagematchEventId;

        return new MetadataResult<Movie>
        {
            HasMetadata = true,
            Item = movie
        };
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Wrestling match cards do not provide remote images.");
    }

    private static string? TryGetCagematchId(IReadOnlyDictionary<string, string>? providerIds)
    {
        if (providerIds is null)
        {
            return null;
        }

        foreach (var key in new[] { ProviderId, "Cagematch", "CAGEMATCH" })
        {
            if (providerIds.TryGetValue(key, out var value) && CagematchIds.TryNormalizeEventId(value, out var normalized))
            {
                return normalized;
            }
        }

        return null;
    }

    private async Task<WrestlingEvent?> FetchAndCacheByIdAsync(string providerId, string lookupKey, CancellationToken cancellationToken)
    {
        try
        {
            var wrestlingEvent = await _cagematchClient.GetEventByIdAsync(providerId, lookupKey, cancellationToken).ConfigureAwait(false);
            if (wrestlingEvent is not null)
            {
                _cache.Save(wrestlingEvent);
            }

            return wrestlingEvent;
        }
        catch (CagematchBlockedException ex)
        {
            _cache.RecordManualLookup(providerId, null, null, ex.Message);
            _logger.LogWarning(ex, "CageMatch event {EventId} could not be fetched.", providerId);
            return null;
        }
    }
}
