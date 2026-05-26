using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Wrestling.Configuration;
using Jellyfin.Plugin.Wrestling.Models;

namespace Jellyfin.Plugin.Wrestling.Services;

/// <summary>
/// Imports and reads match-card data exported from external workbook/cache tools.
/// </summary>
public interface IImportedMatchCacheService
{
    /// <summary>
    /// Imports workbook-style CSV rows into plugin configuration.
    /// </summary>
    ImportedCacheImportResult ImportCsv(string csv);

    /// <summary>
    /// Finds an imported event for a Jellyfin movie.
    /// </summary>
    WrestlingEvent? FindEvent(string? title, int? year, DateTime? premiereDate);
}

/// <summary>
/// Configuration-backed imported match cache.
/// </summary>
public static partial class ImportedMatchCacheMatcher
{
    /// <summary>
    /// Scores an imported cache event against item metadata.
    /// </summary>
    public static int Score(string? eventName, DateTime? eventDate, string? title, int? year, DateTime? premiereDate)
    {
        var target = Normalize(title);
        var candidate = Normalize(eventName);
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(candidate))
        {
            return 0;
        }

        var score = 0;
        if (candidate.Equals(target, StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }
        else if (candidate.Contains(target, StringComparison.OrdinalIgnoreCase) || target.Contains(candidate, StringComparison.OrdinalIgnoreCase))
        {
            score += 80;
        }
        else
        {
            score += TokenOverlapScore(target, candidate);
        }

        var expectedYear = premiereDate?.Year ?? year;
        if (expectedYear.HasValue && eventDate?.Year == expectedYear.Value)
        {
            score += 30;
        }

        return score;
    }

    private static int TokenOverlapScore(string target, string candidate)
    {
        var targetTokens = target.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (targetTokens.Count == 0)
        {
            return 0;
        }

        var candidateTokens = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matched = targetTokens.Count(token => candidateTokens.Contains(token, StringComparer.OrdinalIgnoreCase));
        return (int)Math.Round(60.0 * matched / targetTokens.Count, MidpointRounding.AwayFromZero);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return WhitespaceRegex().Replace(PunctuationRegex().Replace(value, " "), " ").Trim().ToUpperInvariant();
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex PunctuationRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}

/// <summary>
/// Imported match cache service.
/// </summary>
public class ImportedMatchCacheService : IImportedMatchCacheService
{
    /// <inheritdoc />
    public ImportedCacheImportResult ImportCsv(string csv)
    {
        var result = new ImportedCacheImportResult();
        if (string.IsNullOrWhiteSpace(csv))
        {
            result.Message = "No CSV data provided.";
            SaveResult(result);
            return result;
        }

        var rows = ParseCsv(csv).ToList();
        if (rows.Count < 2)
        {
            result.Message = "CSV did not contain data rows.";
            SaveResult(result);
            return result;
        }

        var headers = rows[0].Select(NormalizeHeader).ToList();
        var dateIndex = FindHeader(headers, "DATE");
        var matchIndex = FindHeader(headers, "MATCHFIXTURE", "MATCH");
        var typeIndex = FindHeader(headers, "MATCHTYPE", "TYPE");
        var eventIndex = FindHeader(headers, "EVENT", "SHOW");
        var ratingIndex = FindHeader(headers, "RATINGONCAGEMATCH", "RATING", "CAGEMATCHRATING");
        var urlIndex = FindHeader(headers, "URL", "CAGEMATCHURL", "LINK");

        if (matchIndex < 0 || eventIndex < 0)
        {
            result.Message = "CSV must include Match Fixture and Event columns.";
            SaveResult(result);
            return result;
        }

        var groups = rows.Skip(1)
            .Select(row => ToImportedRow(row, dateIndex, matchIndex, typeIndex, eventIndex, ratingIndex, urlIndex))
            .Where(row => !string.IsNullOrWhiteSpace(row.Event) && !string.IsNullOrWhiteSpace(row.Match))
            .GroupBy(row => BuildGroupKey(row.Event, row.Date))
            .ToList();

        var imported = new List<CachedWrestlingEvent>();
        foreach (var group in groups)
        {
            var first = group.First();
            imported.Add(new CachedWrestlingEvent
            {
                CagematchEventId = string.Empty,
                LookupKey = LookupKey.Build(first.Event, first.Date?.Year, first.Date),
                Name = first.Event,
                EventDate = first.Date,
                SourceUrl = first.Url,
                CachedAtUtc = DateTime.UtcNow,
                Matches = group.Select((row, index) => new CachedWrestlingMatch
                {
                    Order = index + 1,
                    Participants = row.Match,
                    Stipulation = row.MatchType,
                    Rating = row.Rating,
                    Result = string.Empty
                }).ToList()
            });
        }

        var config = GetConfiguration();
        config.ImportedEvents = imported;
        result.ImportedEvents = imported.Count;
        result.ImportedMatches = imported.Sum(item => item.Matches.Count);
        result.Message = string.Format(CultureInfo.InvariantCulture, "Imported {0} events and {1} matches.", result.ImportedEvents, result.ImportedMatches);
        SaveResult(result);
        return result;
    }

    /// <inheritdoc />
    public WrestlingEvent? FindEvent(string? title, int? year, DateTime? premiereDate)
    {
        var config = GetConfiguration();
        config.ImportedEvents ??= [];
        var candidates = config.ImportedEvents
            .Select(item => new
            {
                Event = item,
                Score = ImportedMatchCacheMatcher.Score(item.Name, item.EventDate, title, year, premiereDate)
            })
            .OrderByDescending(item => item.Score)
            .ToList();

        var best = candidates.FirstOrDefault();
        if (best is null || best.Score < 35)
        {
            return null;
        }

        if (candidates.Count > 1 && candidates[1].Score == best.Score)
        {
            return null;
        }

        return FromCached(best.Event, LookupKey.Build(title, year, premiereDate));
    }

    private static ImportedCacheRow ToImportedRow(IReadOnlyList<string> row, int dateIndex, int matchIndex, int typeIndex, int eventIndex, int ratingIndex, int urlIndex)
    {
        var dateText = Get(row, dateIndex);
        return new ImportedCacheRow
        {
            Date = TryParseDate(dateText),
            Match = Get(row, matchIndex),
            MatchType = Get(row, typeIndex),
            Event = Get(row, eventIndex),
            Rating = Get(row, ratingIndex),
            Url = Get(row, urlIndex)
        };
    }

    private static string BuildGroupKey(string eventName, DateTime? date)
    {
        return LookupKey.Build(eventName, date?.Year, date);
    }

    private static int FindHeader(IReadOnlyList<string> headers, params string[] names)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (names.Contains(headers[i], StringComparer.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static string NormalizeHeader(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    }

    private static string Get(IReadOnlyList<string> row, int index)
    {
        return index >= 0 && index < row.Count ? row[index].Trim() : string.Empty;
    }

    private static DateTime? TryParseDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string[] formats = ["dd.MM.yyyy", "yyyy-MM-dd", "M/d/yyyy", "MM/dd/yyyy", "d/M/yyyy", "dd/MM/yyyy"];
        return DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed)
            ? parsed.Date
            : null;
    }

    private static IEnumerable<List<string>> ParseCsv(string csv)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < csv.Length; i++)
        {
            var ch = csv[i];
            if (inQuotes)
            {
                if (ch == '"' && i + 1 < csv.Length && csv[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    field.Append(ch);
                }
            }
            else if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (ch == '\r' || ch == '\n')
            {
                if (ch == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(field.ToString());
                field.Clear();
                if (row.Any(value => !string.IsNullOrWhiteSpace(value)))
                {
                    yield return row;
                }

                row = [];
            }
            else
            {
                field.Append(ch);
            }
        }

        row.Add(field.ToString());
        if (row.Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            yield return row;
        }
    }

    private static WrestlingEvent FromCached(CachedWrestlingEvent cached, string lookupKey)
    {
        return new WrestlingEvent
        {
            CagematchEventId = cached.CagematchEventId,
            LookupKey = lookupKey,
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

    private static PluginConfiguration GetConfiguration()
    {
        return Plugin.Instance?.Configuration ?? new PluginConfiguration();
    }

    private static void SaveResult(ImportedCacheImportResult result)
    {
        var config = GetConfiguration();
        config.LastImportResult = result.Message;
        Plugin.Instance?.SaveConfiguration();
    }
}

/// <summary>
/// Imported cache result.
/// </summary>
public class ImportedCacheImportResult
{
    /// <summary>
    /// Gets or sets imported event count.
    /// </summary>
    public int ImportedEvents { get; set; }

    /// <summary>
    /// Gets or sets imported match count.
    /// </summary>
    public int ImportedMatches { get; set; }

    /// <summary>
    /// Gets or sets the import summary.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Request body for cache import.
/// </summary>
public class ImportedCacheImportRequest
{
    /// <summary>
    /// Gets or sets CSV text.
    /// </summary>
    public string Csv { get; set; } = string.Empty;
}

internal sealed class ImportedCacheRow
{
    public DateTime? Date { get; set; }

    public string Match { get; set; } = string.Empty;

    public string MatchType { get; set; } = string.Empty;

    public string Event { get; set; } = string.Empty;

    public string Rating { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;
}
