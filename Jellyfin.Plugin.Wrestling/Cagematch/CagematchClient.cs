using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Wrestling.Configuration;
using Jellyfin.Plugin.Wrestling.Models;
using Jellyfin.Plugin.Wrestling.Services;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Wrestling.Cagematch;

/// <summary>
/// CageMatch client abstraction.
/// </summary>
public interface ICagematchClient
{
    /// <summary>
    /// Gets an event by explicit CageMatch event id.
    /// </summary>
    Task<WrestlingEvent?> GetEventByIdAsync(string eventId, string lookupKey, CancellationToken cancellationToken);

    /// <summary>
    /// Searches for one unambiguous event by title and date metadata.
    /// </summary>
    Task<CagematchSearchResult> SearchEventAsync(string name, int? year, DateTime? premiereDate, CancellationToken cancellationToken);
}

/// <summary>
/// Search result wrapper.
/// </summary>
public class CagematchSearchResult
{
    /// <summary>
    /// Gets an empty result.
    /// </summary>
    public static CagematchSearchResult None { get; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether lookup was blocked.
    /// </summary>
    public bool IsBlocked { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether lookup returned ambiguous candidates.
    /// </summary>
    public bool IsAmbiguous { get; set; }

    /// <summary>
    /// Gets or sets the resolved event id.
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a status message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets candidates considered for the lookup.
    /// </summary>
    public List<CagematchSearchCandidate> Candidates { get; set; } = [];
}

/// <summary>
/// One CageMatch event candidate from search results.
/// </summary>
public class CagematchSearchCandidate
{
    /// <summary>
    /// Gets or sets the CageMatch event id.
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the candidate display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets raw searchable text from the result.
    /// </summary>
    public string RawText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the score assigned by the matcher.
    /// </summary>
    public int Score { get; set; }
}

/// <summary>
/// Respectful CageMatch HTTP client.
/// </summary>
public class CagematchClient : ICagematchClient
{
    private static readonly SemaphoreSlim RequestLock = new(1, 1);
    private readonly HttpClient _httpClient;
    private readonly ILogger<CagematchClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CagematchClient"/> class.
    /// </summary>
    public CagematchClient(HttpClient httpClient, ILogger<CagematchClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<WrestlingEvent?> GetEventByIdAsync(string eventId, string lookupKey, CancellationToken cancellationToken)
    {
        if (!CagematchIds.TryNormalizeEventId(eventId, out var normalizedId))
        {
            return null;
        }

        var url = CagematchIds.BuildEventUrl(normalizedId);
        var html = await GetStringRespectfullyAsync(url, cancellationToken).ConfigureAwait(false);
        var wrestlingEvent = CagematchParser.ParseEventPage(html, normalizedId, lookupKey, url);

        if (wrestlingEvent.Matches.Count == 0)
        {
            _logger.LogWarning("CageMatch event {EventId} did not yield any matches.", normalizedId);
            return null;
        }

        return wrestlingEvent;
    }

    /// <inheritdoc />
    public async Task<CagematchSearchResult> SearchEventAsync(string name, int? year, DateTime? premiereDate, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        if (!config.EnableAutomaticLookup || string.IsNullOrWhiteSpace(name))
        {
            return CagematchSearchResult.None;
        }

        var query = Uri.EscapeDataString(string.Join(' ', new[] { name, year?.ToString(CultureInfo.InvariantCulture) }.Where(value => !string.IsNullOrWhiteSpace(value))));
        var url = string.Create(CultureInfo.InvariantCulture, $"https://www.cagematch.net/?id=1&view=search&s={query}");

        try
        {
            var html = await GetStringRespectfullyAsync(url, cancellationToken).ConfigureAwait(false);
            var candidates = CagematchParser.ParseSearchEventCandidates(html)
                .GroupBy(candidate => candidate.EventId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Select(candidate => CagematchCandidateMatcher.Score(candidate, name, year, premiereDate))
                .OrderByDescending(candidate => candidate.Score)
                .ToList();

            if (candidates.Count == 1)
            {
                return new CagematchSearchResult
                {
                    EventId = candidates[0].EventId,
                    Candidates = candidates,
                    Message = "Found one CageMatch event candidate."
                };
            }

            var best = CagematchCandidateMatcher.ChooseBest(candidates);
            if (best is not null)
            {
                return new CagematchSearchResult
                {
                    EventId = best.EventId,
                    Candidates = candidates,
                    Message = string.Create(CultureInfo.InvariantCulture, $"Selected best CageMatch candidate {best.EventId} with score {best.Score}.")
                };
            }

            return new CagematchSearchResult
            {
                IsAmbiguous = candidates.Count > 1,
                Candidates = candidates,
                Message = candidates.Count == 0 ? "No CageMatch event candidates found." : "Multiple CageMatch event candidates found."
            };
        }
        catch (CagematchBlockedException ex)
        {
            return new CagematchSearchResult
            {
                IsBlocked = true,
                Message = ex.Message
            };
        }
    }

    private async Task<string> GetStringRespectfullyAsync(string url, CancellationToken cancellationToken)
    {
        await RequestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WaitForThrottleAsync(cancellationToken).ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(GetConfiguration().UserAgent);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var config = GetConfiguration();
            config.LastCagematchRequestUtc = DateTime.UtcNow;
            config.LastCagematchUrl = url;
            config.LastCagematchStatus = string.Format(CultureInfo.InvariantCulture, "HTTP {0}", (int)response.StatusCode);
            Plugin.Instance?.SaveConfiguration();

            var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || IsBlockedGate(html))
            {
                config.LastCagematchStatus = string.Format(CultureInfo.InvariantCulture, "Blocked or gated: HTTP {0}", (int)response.StatusCode);
                Plugin.Instance?.SaveConfiguration();
                throw new CagematchBlockedException(
                    string.Format(CultureInfo.InvariantCulture, "CageMatch lookup was blocked or gated. HTTP status: {0}.", response.StatusCode));
            }

            return html;
        }
        finally
        {
            RequestLock.Release();
        }
    }

    private static async Task WaitForThrottleAsync(CancellationToken cancellationToken)
    {
        var config = GetConfiguration();
        if (config.LastCagematchRequestUtc == default)
        {
            return;
        }

        var delay = TimeSpan.FromSeconds(Math.Max(1, config.CrawlDelaySeconds));
        var elapsed = DateTime.UtcNow - config.LastCagematchRequestUtc;
        var remaining = delay - elapsed;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
        }
    }

    private static PluginConfiguration GetConfiguration()
    {
        return Plugin.Instance?.Configuration ?? new PluginConfiguration();
    }

    private static bool IsBlockedGate(string html)
    {
        return html.Contains("Javascript is required", StringComparison.OrdinalIgnoreCase)
            || html.Contains("enable javascript", StringComparison.OrdinalIgnoreCase)
            || html.Contains("cloudflare", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// CageMatch blocked/gated response.
/// </summary>
public class CagematchBlockedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CagematchBlockedException"/> class.
    /// </summary>
    public CagematchBlockedException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// CageMatch id helpers.
/// </summary>
public static partial class CagematchIds
{
    /// <summary>
    /// Tries to normalize a CageMatch event id or URL.
    /// </summary>
    public static bool TryNormalizeEventId(string? input, out string eventId)
    {
        eventId = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var decoded = WebUtility.HtmlDecode(input);
        var nrMatch = EventNumberRegex().Match(decoded);
        if (nrMatch.Success)
        {
            eventId = nrMatch.Groups["id"].Value;
            return true;
        }

        var match = StandaloneEventIdRegex().Match(decoded);
        if (!match.Success)
        {
            return false;
        }

        eventId = match.Groups["id"].Value;
        return true;
    }

    /// <summary>
    /// Builds a CageMatch event URL.
    /// </summary>
    public static string BuildEventUrl(string eventId)
    {
        return string.Create(CultureInfo.InvariantCulture, $"https://www.cagematch.net/?id=1&nr={eventId}");
    }

    [GeneratedRegex(@"(?:[?&]|^)nr=(?<id>\d{1,12})", RegexOptions.IgnoreCase)]
    private static partial Regex EventNumberRegex();

    [GeneratedRegex(@"^\s*(?<id>\d{1,12})\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex StandaloneEventIdRegex();
}
