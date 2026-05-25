using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Wrestling.Models;
using Jellyfin.Plugin.Wrestling.Providers;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Wrestling.Services;

/// <summary>
/// Reads cached match cards for API consumers.
/// </summary>
public interface IWrestlingMatchService
{
    /// <summary>
    /// Gets cached matches for a Jellyfin item id.
    /// </summary>
    Task<WrestlingMatchesResponse?> GetMatchesAsync(Guid itemId, bool includeResults, CancellationToken cancellationToken);
}

/// <summary>
/// Match service backed by Jellyfin library metadata and plugin cache.
/// </summary>
public class WrestlingMatchService : IWrestlingMatchService
{
    private readonly ILibraryManager _libraryManager;
    private readonly IWrestlingMatchCache _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="WrestlingMatchService"/> class.
    /// </summary>
    public WrestlingMatchService(ILibraryManager libraryManager, IWrestlingMatchCache cache)
    {
        _libraryManager = libraryManager;
        _cache = cache;
    }

    /// <inheritdoc />
    public Task<WrestlingMatchesResponse?> GetMatchesAsync(Guid itemId, bool includeResults, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var movie = _libraryManager.GetItemById<Movie>(itemId);
        if (movie is null)
        {
            return Task.FromResult<WrestlingMatchesResponse?>(null);
        }

        var wrestlingEvent = TryGetEvent(movie);
        if (wrestlingEvent is null)
        {
            return Task.FromResult<WrestlingMatchesResponse?>(null);
        }

        var response = new WrestlingMatchesResponse
        {
            ItemId = itemId,
            EventName = wrestlingEvent.Name,
            EventDate = wrestlingEvent.EventDate,
            SourceUrl = wrestlingEvent.SourceUrl,
            Matches = wrestlingEvent.Matches
                .OrderBy(match => match.Order)
                .Select(match => new WrestlingMatchResponse
                {
                    Order = match.Order,
                    Participants = match.Participants,
                    Stipulation = match.Stipulation,
                    Rating = match.Rating,
                    Result = includeResults ? match.Result : null
                })
                .ToList()
        };

        return Task.FromResult<WrestlingMatchesResponse?>(response);
    }

    private WrestlingEvent? TryGetEvent(Movie movie)
    {
        if (movie.ProviderIds.TryGetValue(WrestlingMovieMetadataProvider.ProviderId, out var providerId)
            && !string.IsNullOrWhiteSpace(providerId))
        {
            var cachedById = _cache.GetByCagematchId(providerId);
            if (cachedById is not null)
            {
                return cachedById;
            }
        }

        var lookupKey = LookupKey.Build(movie.Name, movie.ProductionYear, movie.PremiereDate);
        return _cache.GetByLookupKey(lookupKey);
    }
}

/// <summary>
/// API response for cached matches.
/// </summary>
public class WrestlingMatchesResponse
{
    /// <summary>
    /// Gets or sets the Jellyfin item id.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets the event name.
    /// </summary>
    public string EventName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the event date.
    /// </summary>
    public DateTime? EventDate { get; set; }

    /// <summary>
    /// Gets or sets the source URL.
    /// </summary>
    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets matches.
    /// </summary>
    public List<WrestlingMatchResponse> Matches { get; set; } = [];
}

/// <summary>
/// API response for one match.
/// </summary>
public class WrestlingMatchResponse
{
    /// <summary>
    /// Gets or sets the match order.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Gets or sets participants.
    /// </summary>
    public string Participants { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets stipulation or title info.
    /// </summary>
    public string Stipulation { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets rating text.
    /// </summary>
    public string Rating { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets result text when explicitly requested.
    /// </summary>
    public string? Result { get; set; }
}
