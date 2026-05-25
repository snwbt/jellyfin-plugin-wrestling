using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Wrestling.Models;

/// <summary>
/// Normalized wrestling event metadata.
/// </summary>
public class WrestlingEvent
{
    /// <summary>
    /// Gets or sets the CageMatch event id.
    /// </summary>
    public string CagematchEventId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the normalized lookup key.
    /// </summary>
    public string LookupKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the event name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the event date.
    /// </summary>
    public DateTime? EventDate { get; set; }

    /// <summary>
    /// Gets or sets the source URL.
    /// </summary>
    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the match card.
    /// </summary>
    public List<WrestlingMatch> Matches { get; set; } = [];
}

/// <summary>
/// Normalized wrestling match metadata.
/// </summary>
public class WrestlingMatch
{
    /// <summary>
    /// Gets or sets match order.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Gets or sets participants.
    /// </summary>
    public string Participants { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets stipulation or title information.
    /// </summary>
    public string Stipulation { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets match rating text.
    /// </summary>
    public string Rating { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets result text.
    /// </summary>
    public string Result { get; set; } = string.Empty;
}
