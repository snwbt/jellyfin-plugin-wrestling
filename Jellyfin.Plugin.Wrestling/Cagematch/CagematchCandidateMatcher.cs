using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Wrestling.Cagematch;

/// <summary>
/// Scores CageMatch search candidates against Jellyfin movie metadata.
/// </summary>
public static partial class CagematchCandidateMatcher
{
    /// <summary>
    /// Adds a match score to a candidate.
    /// </summary>
    public static CagematchSearchCandidate Score(CagematchSearchCandidate candidate, string? title, int? year, DateTime? premiereDate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var targetTitle = Normalize(title);
        var candidateText = Normalize(string.Join(' ', candidate.Name, candidate.RawText));
        var score = 0;

        if (!string.IsNullOrWhiteSpace(targetTitle) && !string.IsNullOrWhiteSpace(candidateText))
        {
            if (candidateText.Equals(targetTitle, StringComparison.OrdinalIgnoreCase))
            {
                score += 100;
            }
            else if (candidateText.Contains(targetTitle, StringComparison.OrdinalIgnoreCase))
            {
                score += 80;
            }
            else
            {
                score += TokenOverlapScore(targetTitle, candidateText);
            }
        }

        var candidateYear = ExtractYear(candidateText);
        if (year.HasValue && candidateYear == year.Value)
        {
            score += 30;
        }

        if (premiereDate.HasValue && candidateText.Contains(premiereDate.Value.ToString("dd MM yyyy", CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            score += 20;
        }

        candidate.Score = score;
        return candidate;
    }

    /// <summary>
    /// Chooses one best candidate, or returns null for no usable candidate or a tie.
    /// </summary>
    public static CagematchSearchCandidate? ChooseBest(IReadOnlyCollection<CagematchSearchCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var ordered = candidates.OrderByDescending(candidate => candidate.Score).ToList();
        var best = ordered[0];
        if (best.Score < 35)
        {
            return null;
        }

        if (ordered.Count > 1 && ordered[1].Score == best.Score)
        {
            return null;
        }

        return best;
    }

    private static int TokenOverlapScore(string targetTitle, string candidateText)
    {
        var targetTokens = targetTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (targetTokens.Count == 0)
        {
            return 0;
        }

        var matched = targetTokens.Count(token => candidateText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(token, StringComparer.OrdinalIgnoreCase));
        return (int)Math.Round(60.0 * matched / targetTokens.Count, MidpointRounding.AwayFromZero);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = PunctuationRegex().Replace(value, " ");
        return WhitespaceRegex().Replace(cleaned, " ").Trim().ToUpperInvariant();
    }

    private static int? ExtractYear(string text)
    {
        var match = YearRegex().Match(text);
        return match.Success && int.TryParse(match.Groups["year"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            ? year
            : null;
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex PunctuationRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"\b(?<year>19\d{2}|20\d{2})\b")]
    private static partial Regex YearRegex();
}
