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
        EnableScheduledScan = false;
        CrawlDelaySeconds = 527;
        IncludeRatingsInOverview = true;
        LibraryName = "Wrestling PPVs";
        SelectedLibraryNames = [];
        UserAgent = "Jellyfin.Plugin.Wrestling/1.0";
        CachedEvents = [];
        PendingManualLookups = [];
        ManualMappings = [];
        ManualMappingsText = string.Empty;
        LastApplyResult = string.Empty;
        LastScanResult = string.Empty;
        LastCagematchUrl = string.Empty;
        LastCagematchStatus = string.Empty;
    }

    /// <summary>
    /// Gets or sets a value indicating whether title/date searches are enabled when a CageMatch id is missing.
    /// </summary>
    public bool EnableAutomaticLookup { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the scheduled scan task should process selected libraries.
    /// </summary>
    public bool EnableScheduledScan { get; set; }

    /// <summary>
    /// Gets or sets the minimum delay between CageMatch HTTP requests.
    /// </summary>
    public int CrawlDelaySeconds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether ratings are shown in the spoiler-safe overview.
    /// </summary>
    public bool IncludeRatingsInOverview { get; set; }

    /// <summary>
    /// Gets or sets the library name to scan when applying match cards directly.
    /// </summary>
    public string LibraryName { get; set; }

    /// <summary>
    /// Gets or sets selected Jellyfin library names to scan.
    /// </summary>
    public List<string> SelectedLibraryNames { get; set; }

    /// <summary>
    /// Gets or sets the HTTP user agent.
    /// </summary>
    public string UserAgent { get; set; }

    /// <summary>
    /// Gets or sets the last CageMatch request time in UTC.
    /// </summary>
    public DateTime LastCagematchRequestUtc { get; set; }

    /// <summary>
    /// Gets or sets the latest CageMatch URL requested by the scanner.
    /// </summary>
    public string LastCagematchUrl { get; set; }

    /// <summary>
    /// Gets or sets the latest CageMatch request status.
    /// </summary>
    public string LastCagematchStatus { get; set; }

    /// <summary>
    /// Gets or sets cached event metadata.
    /// </summary>
    public List<CachedWrestlingEvent> CachedEvents { get; set; }

    /// <summary>
    /// Gets or sets items that need a manual CageMatch event id.
    /// </summary>
    public List<ManualLookupRequest> PendingManualLookups { get; set; }

    /// <summary>
    /// Gets or sets manually configured PPV mappings.
    /// </summary>
    public List<ManualPpvMapping> ManualMappings { get; set; }

    /// <summary>
    /// Gets or sets manually configured PPV mappings as editable text.
    /// </summary>
    public string ManualMappingsText { get; set; }

    /// <summary>
    /// Gets or sets the latest direct apply summary.
    /// </summary>
    public string LastApplyResult { get; set; }

    /// <summary>
    /// Gets or sets the latest automatic scan summary.
    /// </summary>
    public string LastScanResult { get; set; }
}

/// <summary>
/// Manual mapping between a Jellyfin PPV movie and wrestling event metadata.
/// </summary>
public class ManualPpvMapping
{
    /// <summary>
    /// Gets or sets the movie title to match.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the production year to match.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets the premiere date to match.
    /// </summary>
    public DateTime? PremiereDate { get; set; }

    /// <summary>
    /// Gets or sets the CageMatch event id or URL.
    /// </summary>
    public string CagematchEventId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets pasted match-card text.
    /// </summary>
    public string MatchCardText { get; set; } = string.Empty;
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
