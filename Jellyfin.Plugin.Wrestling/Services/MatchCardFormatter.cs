using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Wrestling.Models;

namespace Jellyfin.Plugin.Wrestling.Services;

/// <summary>
/// Formats and replaces the plugin-managed overview section.
/// </summary>
public static partial class MatchCardFormatter
{
    /// <summary>
    /// Marker used at the start of the managed overview section.
    /// </summary>
    public const string StartMarker = "<!-- wrestling-match-card:start -->";

    /// <summary>
    /// Marker used at the end of the managed overview section.
    /// </summary>
    public const string EndMarker = "<!-- wrestling-match-card:end -->";

    /// <summary>
    /// Replaces the plugin-managed section while preserving surrounding overview text.
    /// </summary>
    /// <param name="overview">Current overview text.</param>
    /// <param name="wrestlingEvent">Wrestling event metadata.</param>
    /// <param name="includeRatings">Whether ratings should be included.</param>
    /// <returns>Updated overview.</returns>
    public static string AppendOrReplace(string? overview, WrestlingEvent wrestlingEvent, bool includeRatings)
    {
        var cleanOverview = RemoveManagedSection(overview);
        var card = FormatSpoilerSafeSection(wrestlingEvent, includeRatings);

        if (string.IsNullOrWhiteSpace(cleanOverview))
        {
            return card;
        }

        return string.Concat(cleanOverview.Trim(), Environment.NewLine, Environment.NewLine, card);
    }

    /// <summary>
    /// Removes the plugin-managed section from overview text.
    /// </summary>
    /// <param name="overview">Overview text.</param>
    /// <returns>Overview without plugin-managed content.</returns>
    public static string RemoveManagedSection(string? overview)
    {
        if (string.IsNullOrWhiteSpace(overview))
        {
            return string.Empty;
        }

        return ManagedSectionRegex().Replace(overview, string.Empty).Trim();
    }

    private static string FormatSpoilerSafeSection(WrestlingEvent wrestlingEvent, bool includeRatings)
    {
        var builder = new StringBuilder();
        builder.AppendLine(StartMarker);
        builder.AppendLine("Match Card");

        if (wrestlingEvent.EventDate.HasValue)
        {
            builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "Date: {0:yyyy-MM-dd}", wrestlingEvent.EventDate.Value));
        }

        foreach (var match in wrestlingEvent.Matches.OrderBy(match => match.Order))
        {
            var line = new StringBuilder();
            line.Append(CultureInfo.InvariantCulture, $"{match.Order}. {match.Participants}");

            if (!string.IsNullOrWhiteSpace(match.Stipulation))
            {
                line.Append(CultureInfo.InvariantCulture, $" - {match.Stipulation}");
            }

            if (includeRatings && !string.IsNullOrWhiteSpace(match.Rating))
            {
                line.Append(CultureInfo.InvariantCulture, $" (Rating: {match.Rating})");
            }

            builder.AppendLine(line.ToString());
        }

        builder.Append(EndMarker);
        return builder.ToString();
    }

    [GeneratedRegex("<!-- wrestling-match-card:start -->[\\s\\S]*?<!-- wrestling-match-card:end -->", RegexOptions.Multiline)]
    private static partial Regex ManagedSectionRegex();
}
