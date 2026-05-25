using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Wrestling.Cagematch;
using Jellyfin.Plugin.Wrestling.Configuration;
using Jellyfin.Plugin.Wrestling.Models;

namespace Jellyfin.Plugin.Wrestling.Services;

/// <summary>
/// Resolves manually configured PPV mappings.
/// </summary>
public static partial class ManualMappingService
{
    /// <summary>
    /// Finds a manual mapping for an item.
    /// </summary>
    public static ManualPpvMapping? FindMapping(IEnumerable<ManualPpvMapping> mappings, string? title, int? year, DateTime? premiereDate)
    {
        ArgumentNullException.ThrowIfNull(mappings);

        var exactKey = LookupKey.Build(title, year, premiereDate);
        var yearKey = LookupKey.Build(title, year, null);
        var titleOnlyKey = LookupKey.Build(title, null, null);

        return mappings.FirstOrDefault(mapping =>
        {
            if (string.IsNullOrWhiteSpace(mapping.Title))
            {
                return false;
            }

            var mappingKey = LookupKey.Build(mapping.Title, mapping.Year, mapping.PremiereDate);
            var mappingYearKey = LookupKey.Build(mapping.Title, mapping.Year, null);
            var mappingTitleOnlyKey = LookupKey.Build(mapping.Title, null, null);

            return string.Equals(mappingKey, exactKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(mappingYearKey, yearKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(mappingTitleOnlyKey, titleOnlyKey, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// Builds a normalized wrestling event from pasted match-card text.
    /// </summary>
    public static WrestlingEvent? BuildEventFromMapping(ManualPpvMapping mapping, string lookupKey)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        var matches = PastedMatchCardParser.Parse(mapping.MatchCardText).ToList();
        if (matches.Count == 0)
        {
            return null;
        }

        CagematchIds.TryNormalizeEventId(mapping.CagematchEventId, out var eventId);

        return new WrestlingEvent
        {
            CagematchEventId = eventId,
            LookupKey = lookupKey,
            Name = mapping.Title,
            EventDate = mapping.PremiereDate,
            SourceUrl = string.IsNullOrWhiteSpace(eventId) ? string.Empty : CagematchIds.BuildEventUrl(eventId),
            Matches = matches
        };
    }
}

/// <summary>
/// Parses simple pasted match-card text.
/// </summary>
public static partial class PastedMatchCardParser
{
    /// <summary>
    /// Parses pasted match-card text into normalized matches.
    /// </summary>
    /// <remarks>
    /// Supported line format:
    /// <c>1. Participants | Stipulation/title info | Rating | Result</c>.
    /// Stipulation, rating, and result are optional.
    /// </remarks>
    public static IEnumerable<WrestlingMatch> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        var order = 1;
        foreach (var rawLine in LineSplitRegex().Split(text))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var fields = line.Split('|').Select(field => field.Trim()).ToList();
            if (fields.Count == 0 || string.IsNullOrWhiteSpace(fields[0]))
            {
                continue;
            }

            var firstField = LeadingOrderRegex().Replace(fields[0], string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(firstField))
            {
                continue;
            }

            yield return new WrestlingMatch
            {
                Order = order++,
                Participants = firstField,
                Stipulation = fields.ElementAtOrDefault(1) ?? string.Empty,
                Rating = fields.ElementAtOrDefault(2) ?? string.Empty,
                Result = fields.ElementAtOrDefault(3) ?? string.Empty
            };
        }
    }

    [GeneratedRegex(@"\r?\n")]
    private static partial Regex LineSplitRegex();

    [GeneratedRegex(@"^\s*\d+[\.)-]?\s*")]
    private static partial Regex LeadingOrderRegex();
}

/// <summary>
/// Parses manual mapping text from the configuration page.
/// </summary>
public static partial class ManualMappingTextParser
{
    /// <summary>
    /// Parses one or more mapping blocks.
    /// </summary>
    public static List<ManualPpvMapping> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return BlockSplitRegex().Split(text)
            .Select(ParseBlock)
            .Where(mapping => mapping is not null)
            .Cast<ManualPpvMapping>()
            .ToList();
    }

    /// <summary>
    /// Formats mappings for editing.
    /// </summary>
    public static string Format(IEnumerable<ManualPpvMapping>? mappings)
    {
        if (mappings is null)
        {
            return string.Empty;
        }

        return string.Join(
            Environment.NewLine + Environment.NewLine + "---" + Environment.NewLine,
            mappings.Select(mapping =>
            {
                var lines = new List<string>
                {
                    string.Concat("Title: ", mapping.Title),
                    string.Concat("Year: ", mapping.Year?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                    string.Concat("Date: ", mapping.PremiereDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty),
                    string.Concat("CageMatch: ", mapping.CagematchEventId),
                    "Matches:"
                };

                if (!string.IsNullOrWhiteSpace(mapping.MatchCardText))
                {
                    lines.Add(mapping.MatchCardText.Trim());
                }

                return string.Join(Environment.NewLine, lines);
            }));
    }

    private static ManualPpvMapping? ParseBlock(string block)
    {
        var mapping = new ManualPpvMapping();
        var matchLines = new List<string>();
        var inMatches = false;

        foreach (var rawLine in LineSplitRegex().Split(block))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (inMatches)
            {
                matchLines.Add(line);
                continue;
            }

            var separatorIndex = line.IndexOf(':', StringComparison.Ordinal);
            if (separatorIndex < 0)
            {
                matchLines.Add(line);
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();

            if (key.Equals("Title", StringComparison.OrdinalIgnoreCase))
            {
                mapping.Title = value;
            }
            else if (key.Equals("Year", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
            {
                mapping.Year = year;
            }
            else if (key.Equals("Date", StringComparison.OrdinalIgnoreCase) && DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date))
            {
                mapping.PremiereDate = date.Date;
            }
            else if (key.Equals("CageMatch", StringComparison.OrdinalIgnoreCase))
            {
                mapping.CagematchEventId = value;
            }
            else if (key.Equals("Matches", StringComparison.OrdinalIgnoreCase))
            {
                inMatches = true;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    matchLines.Add(value);
                }
            }
        }

        mapping.MatchCardText = string.Join(Environment.NewLine, matchLines);
        return string.IsNullOrWhiteSpace(mapping.Title) ? null : mapping;
    }

    [GeneratedRegex(@"(?m)^\s*---\s*$")]
    private static partial Regex BlockSplitRegex();

    [GeneratedRegex(@"\r?\n")]
    private static partial Regex LineSplitRegex();
}
