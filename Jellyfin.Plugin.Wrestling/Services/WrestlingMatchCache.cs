using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Wrestling.Configuration;
using Jellyfin.Plugin.Wrestling.Models;

namespace Jellyfin.Plugin.Wrestling.Services;

/// <summary>
/// Stores and retrieves cached wrestling events from plugin configuration.
/// </summary>
public interface IWrestlingMatchCache
{
    /// <summary>
    /// Gets a cached event by CageMatch id.
    /// </summary>
    WrestlingEvent? GetByCagematchId(string cagematchEventId);

    /// <summary>
    /// Gets a cached event by lookup key.
    /// </summary>
    WrestlingEvent? GetByLookupKey(string lookupKey);

    /// <summary>
    /// Saves a wrestling event.
    /// </summary>
    void Save(WrestlingEvent wrestlingEvent);

    /// <summary>
    /// Records a failed lookup for admin review.
    /// </summary>
    void RecordManualLookup(string name, int? year, DateTime? premiereDate, string reason);
}

/// <summary>
/// Configuration-backed match cache.
/// </summary>
public class WrestlingMatchCache : IWrestlingMatchCache
{
    /// <inheritdoc />
    public WrestlingEvent? GetByCagematchId(string cagematchEventId)
    {
        if (string.IsNullOrWhiteSpace(cagematchEventId))
        {
            return null;
        }

        var config = GetConfiguration();
        var cached = config.CachedEvents.FirstOrDefault(item =>
            string.Equals(item.CagematchEventId, cagematchEventId, StringComparison.OrdinalIgnoreCase));

        return cached is null ? null : FromCached(cached);
    }

    /// <inheritdoc />
    public WrestlingEvent? GetByLookupKey(string lookupKey)
    {
        if (string.IsNullOrWhiteSpace(lookupKey))
        {
            return null;
        }

        var config = GetConfiguration();
        var cached = config.CachedEvents.FirstOrDefault(item =>
            string.Equals(item.LookupKey, lookupKey, StringComparison.OrdinalIgnoreCase));

        return cached is null ? null : FromCached(cached);
    }

    /// <inheritdoc />
    public void Save(WrestlingEvent wrestlingEvent)
    {
        ArgumentNullException.ThrowIfNull(wrestlingEvent);

        var config = GetConfiguration();
        config.CachedEvents.RemoveAll(item =>
            string.Equals(item.CagematchEventId, wrestlingEvent.CagematchEventId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.LookupKey, wrestlingEvent.LookupKey, StringComparison.OrdinalIgnoreCase));

        config.CachedEvents.Add(ToCached(wrestlingEvent));
        Plugin.Instance?.SaveConfiguration();
    }

    /// <inheritdoc />
    public void RecordManualLookup(string name, int? year, DateTime? premiereDate, string reason)
    {
        var config = GetConfiguration();
        var exists = config.PendingManualLookups.Any(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)
            && item.Year == year
            && item.PremiereDate == premiereDate);

        if (exists)
        {
            return;
        }

        config.PendingManualLookups.Add(new ManualLookupRequest
        {
            Name = name,
            Year = year,
            PremiereDate = premiereDate,
            Reason = reason,
            RecordedAtUtc = DateTime.UtcNow
        });

        Plugin.Instance?.SaveConfiguration();
    }

    private static PluginConfiguration GetConfiguration()
    {
        return Plugin.Instance?.Configuration ?? new PluginConfiguration();
    }

    private static CachedWrestlingEvent ToCached(WrestlingEvent wrestlingEvent)
    {
        return new CachedWrestlingEvent
        {
            CagematchEventId = wrestlingEvent.CagematchEventId,
            LookupKey = wrestlingEvent.LookupKey,
            Name = wrestlingEvent.Name,
            EventDate = wrestlingEvent.EventDate,
            SourceUrl = wrestlingEvent.SourceUrl,
            CachedAtUtc = DateTime.UtcNow,
            Matches = wrestlingEvent.Matches.Select(match => new CachedWrestlingMatch
            {
                Order = match.Order,
                Participants = match.Participants,
                Stipulation = match.Stipulation,
                Rating = match.Rating,
                Result = match.Result
            }).ToList()
        };
    }

    private static WrestlingEvent FromCached(CachedWrestlingEvent cached)
    {
        return new WrestlingEvent
        {
            CagematchEventId = cached.CagematchEventId,
            LookupKey = cached.LookupKey,
            Name = cached.Name,
            EventDate = cached.EventDate,
            SourceUrl = cached.SourceUrl,
            Matches = cached.Matches.Select(match => new WrestlingMatch
            {
                Order = match.Order,
                Participants = match.Participants,
                Stipulation = match.Stipulation,
                Rating = match.Rating,
                Result = match.Result
            }).ToList()
        };
    }
}
