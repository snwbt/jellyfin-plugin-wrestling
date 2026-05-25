using System;
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Wrestling.Configuration;

/// <summary>
/// Plugin configuration persisted by Jellyfin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        EnableAutomaticLookup = true;
        CrawlDelaySeconds = 527;
        IncludeRatingsInOverview = true;
        UserAgent = "Jellyfin.Plugin.Wrestling/1.0";
        CachedEvents = [];
        PendingManualLookups = [];
    }

    /// <summary>
    /// Gets or sets a value indicating whether title/date searches are enabled when a CageMatch id is missing.
    /// </summary>
    public bool EnableAutomaticLookup { get; set; }

    /// <summary>
    /// Gets or sets the minimum delay between CageMatch HTTP requests.
    /// </summary>
    public int CrawlDelaySeconds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether ratings are shown in the spoiler-safe overview.
    /// </summary>
    public bool IncludeRatingsInOverview { get; set; }

    /// <summary>
    /// Gets or sets the HTTP user agent.
    /// </summary>
    public string UserAgent { get; set; }

    /// <summary>
    /// Gets or sets the last CageMatch request time in UTC.
    /// </summary>
    public DateTime LastCagematchRequestUtc { get; set; }

    /// <summary>
    /// Gets or sets cached event metadata.
    /// </summary>
    public List<CachedWrestlingEvent> CachedEvents { get; set; }

    /// <summary>
    /// Gets or sets items that need a manual CageMatch event id.
    /// </summary>
    public List<ManualLookupRequest> PendingManualLookups { get; set; }
}

/// <summary>
/// A cached normalized wrestling event.
/// </summary>
public class CachedWrestlingEvent
{
    /// <summary>
    /// Gets or sets the CageMatch event id.
    /// </summary>
    public string CagematchEventId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the normalized lookup key used when no event id is present.
    /// </summary>
    public string LookupKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the event name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the event date, when known.
    /// </summary>
    public DateTime? EventDate { get; set; }

    /// <summary>
    /// Gets or sets the source URL.
    /// </summary>
    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the date the cache entry was refreshed.
    /// </summary>
    public DateTime CachedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets cached matches.
    /// </summary>
    public List<CachedWrestlingMatch> Matches { get; set; } = [];
}

/// <summary>
/// A cached normalized wrestling match.
/// </summary>
public class CachedWrestlingMatch
{
    /// <summary>
    /// Gets or sets the match order.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Gets or sets the participants.
    /// </summary>
    public string Participants { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets stipulation or championship information.
    /// </summary>
    public string Stipulation { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets match rating text.
    /// </summary>
    public string Rating { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets result or winner text.
    /// </summary>
    public string Result { get; set; } = string.Empty;
}

/// <summary>
/// A lookup that could not be resolved automatically.
/// </summary>
public class ManualLookupRequest
{
    /// <summary>
    /// Gets or sets the attempted title.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the attempted production year.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets the attempted premiere date.
    /// </summary>
    public DateTime? PremiereDate { get; set; }

    /// <summary>
    /// Gets or sets why manual lookup is required.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when this request was recorded.
    /// </summary>
    public DateTime RecordedAtUtc { get; set; }
}
