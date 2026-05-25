using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Wrestling.Services;

/// <summary>
/// Creates stable cache lookup keys from item metadata.
/// </summary>
public static partial class LookupKey
{
    /// <summary>
    /// Builds a normalized key from title, year, and date.
    /// </summary>
    /// <param name="name">Event name.</param>
    /// <param name="year">Production year.</param>
    /// <param name="premiereDate">Premiere date.</param>
    /// <returns>Normalized key.</returns>
    public static string Build(string? name, int? year, DateTime? premiereDate)
    {
        var normalizedName = NormalizeName(name);
        var datePart = premiereDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            ?? year?.ToString(CultureInfo.InvariantCulture)
            ?? "unknown";

        return string.Concat(normalizedName, "|", datePart);
    }

    private static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "unknown";
        }

        var builder = new StringBuilder(name.Length);
        foreach (var character in name.Normalize(NormalizationForm.FormD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append(' ');
            }
        }

        return WhitespaceRegex().Replace(builder.ToString(), " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
