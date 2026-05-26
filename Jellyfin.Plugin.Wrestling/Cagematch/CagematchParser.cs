using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Wrestling.Models;

namespace Jellyfin.Plugin.Wrestling.Cagematch;

/// <summary>
/// Parses CageMatch HTML into normalized metadata.
/// </summary>
public static partial class CagematchParser
{
    /// <summary>
    /// Parses an event page.
    /// </summary>
    public static WrestlingEvent ParseEventPage(string html, string eventId, string lookupKey, string sourceUrl)
    {
        ArgumentNullException.ThrowIfNull(html);

        var title = Decode(TitleRegex().Match(html).Groups["title"].Value);
        var eventName = title.Split(['-'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            ?? string.Concat("CageMatch Event ", eventId);

        return new WrestlingEvent
        {
            CagematchEventId = eventId,
            LookupKey = lookupKey,
            Name = eventName,
            EventDate = TryParseDate(html),
            SourceUrl = sourceUrl,
            Matches = ParseMatches(html).ToList()
        };
    }

    /// <summary>
    /// Parses event ids from a CageMatch search response.
    /// </summary>
    public static IEnumerable<string> ParseSearchEventIds(string html)
    {
        return ParseSearchEventCandidates(html).Select(candidate => candidate.EventId);
    }

    /// <summary>
    /// Parses event candidates from a CageMatch search response.
    /// </summary>
    public static IEnumerable<CagematchSearchCandidate> ParseSearchEventCandidates(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        foreach (Match match in EventLinkRegex().Matches(html))
        {
            yield return new CagematchSearchCandidate
            {
                EventId = match.Groups["id"].Value,
                Name = CleanCell(match.Groups["text"].Value),
                RawText = CleanCell(match.Value)
            };
        }
    }

    private static IEnumerable<WrestlingMatch> ParseMatches(string html)
    {
        var order = 1;
        foreach (Match rowMatch in RowRegex().Matches(html))
        {
            var cells = CellRegex().Matches(rowMatch.Value)
                .Select(match => CleanCell(match.Groups["cell"].Value))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();

            if (cells.Count < 2 || !LooksLikeMatchRow(cells))
            {
                continue;
            }

            yield return BuildMatch(order++, cells);
        }
    }

    private static WrestlingMatch BuildMatch(int order, IReadOnlyList<string> cells)
    {
        var participantCell = cells.FirstOrDefault(cell =>
            cell.Contains(" vs. ", StringComparison.OrdinalIgnoreCase)
            || cell.Contains(" defeats ", StringComparison.OrdinalIgnoreCase)
            || cell.Contains(" def. ", StringComparison.OrdinalIgnoreCase))
            ?? cells.ElementAtOrDefault(1)
            ?? cells[0];

        var result = ExtractResult(participantCell);

        return new WrestlingMatch
        {
            Order = order,
            Participants = RemoveResultLanguage(participantCell),
            Stipulation = cells.FirstOrDefault(IsLikelyStipulation) ?? string.Empty,
            Rating = cells.LastOrDefault(IsLikelyRating) ?? string.Empty,
            Result = result
        };
    }

    private static bool LooksLikeMatchRow(IReadOnlyList<string> cells)
    {
        return cells.Any(cell =>
            cell.Contains(" vs. ", StringComparison.OrdinalIgnoreCase)
            || cell.Contains(" def. ", StringComparison.OrdinalIgnoreCase)
            || cell.Contains(" defeats ", StringComparison.OrdinalIgnoreCase)
            || cell.Contains("draw", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLikelyStipulation(string value)
    {
        return value.Contains("match", StringComparison.OrdinalIgnoreCase)
            || value.Contains("title", StringComparison.OrdinalIgnoreCase)
            || value.Contains("championship", StringComparison.OrdinalIgnoreCase)
            || value.Contains("battle royal", StringComparison.OrdinalIgnoreCase)
            || value.Contains("tournament", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyRating(string value)
    {
        return RatingRegex().IsMatch(value);
    }

    private static string ExtractResult(string value)
    {
        var match = ResultRegex().Match(value);
        if (!match.Success)
        {
            return string.Empty;
        }

        return match.Groups["winner"].Value.Trim();
    }

    private static string RemoveResultLanguage(string value)
    {
        return ResultRegex().Replace(value, match => string.Concat(match.Groups["winner"].Value.Trim(), " vs. ")).Trim();
    }

    private static DateTime? TryParseDate(string html)
    {
        var match = DateRegex().Match(CleanCell(html));
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["date"].Value;
        string[] formats = ["dd.MM.yyyy", "yyyy-MM-dd", "MM/dd/yyyy"];
        return DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.Date
            : null;
    }

    private static string CleanCell(string html)
    {
        var withoutTags = TagRegex().Replace(html, " ");
        return WhitespaceRegex().Replace(Decode(withoutTags), " ").Trim();
    }

    private static string Decode(string value)
    {
        return WebUtility.HtmlDecode(value).Trim();
    }

    [GeneratedRegex("<title>(?<title>.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRegex();

    [GeneratedRegex("<tr[^>]*>(?<row>.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex RowRegex();

    [GeneratedRegex("<t[dh][^>]*>(?<cell>.*?)</t[dh]>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CellRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"<a[^>]+href\s*=\s*[""'][^""']*(?:[?&]|&amp;)id=1(?:&amp;|&)nr=(?<id>\d+)[^""']*[""'][^>]*>(?<text>.*?)</a>|[?&]id=1&amp;nr=(?<id>\d+)|[?&]id=1&nr=(?<id>\d+)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex EventLinkRegex();

    [GeneratedRegex(@"(?<date>\d{2}\.\d{2}\.\d{4}|\d{4}-\d{2}-\d{2}|\d{1,2}/\d{1,2}/\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"^\d+(?:\.\d+)?(?:\s*/\s*10)?(?:\s*\(\d+\))?$")]
    private static partial Regex RatingRegex();

    [GeneratedRegex(@"(?<winner>.+?)\s+(?:def\.|defeats|defeated)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex ResultRegex();
}
