using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Wrestling.CacheWorker;

public static partial class Program
{
    public static async Task<int> Main(string[] args)
    {
        var options = WorkerOptions.Parse(args);
        if (options is null)
        {
            WorkerOptions.WriteHelp();
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        using var jellyfin = new JellyfinCacheClient(options);
        await using var browser = new VisibleBrowser(options);

        Console.WriteLine("Starting visible browser worker. Press Ctrl+C to stop.");
        await browser.StartAsync(cancellation.Token).ConfigureAwait(false);

        var queue = await jellyfin.GetQueueAsync(cancellation.Token).ConfigureAwait(false);
        if (options.Limit > 0)
        {
            queue = queue.Take(options.Limit).ToList();
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Loaded {queue.Count} Jellyfin queue items."));
        var processed = 0;
        foreach (var item in queue)
        {
            cancellation.Token.ThrowIfCancellationRequested();
            processed++;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[{processed}/{queue.Count}] {item.Name} {item.Year?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}"));

            var query = string.Join(' ', new[] { item.Name, item.Year?.ToString(CultureInfo.InvariantCulture) }.Where(value => !string.IsNullOrWhiteSpace(value)));
            var searchUrl = string.Create(CultureInfo.InvariantCulture, $"https://www.cagematch.net/?id=1&view=search&s={Uri.EscapeDataString(query)}");
            var searchHtml = await browser.GetCagematchHtmlAsync(searchUrl, cancellation.Token).ConfigureAwait(false);
            if (CagematchPageParser.IsBlockedGate(searchHtml))
            {
                Console.WriteLine("Stopped: CageMatch returned a blocked/gated page in the visible browser.");
                return 1;
            }

            var candidate = CagematchPageParser.ChooseBestCandidate(searchHtml, item.Name, item.Year, item.PremiereDate);
            if (candidate is null)
            {
                Console.WriteLine("No unambiguous CageMatch candidate.");
                continue;
            }

            var eventUrl = string.Create(CultureInfo.InvariantCulture, $"https://www.cagematch.net/?id=1&nr={candidate.EventId}");
            var eventHtml = await browser.GetCagematchHtmlAsync(eventUrl, cancellation.Token).ConfigureAwait(false);
            if (CagematchPageParser.IsBlockedGate(eventHtml))
            {
                Console.WriteLine("Stopped: CageMatch returned a blocked/gated event page in the visible browser.");
                return 1;
            }

            var parsedEvent = CagematchPageParser.ParseEventPage(eventHtml, candidate.EventId, eventUrl);
            if (parsedEvent.Matches.Count == 0)
            {
                Console.WriteLine("Event parsed, but no match rows were found.");
                continue;
            }

            await jellyfin.SyncAsync(parsedEvent, cancellation.Token).ConfigureAwait(false);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Synced {parsedEvent.Matches.Count} matches from {parsedEvent.Name}."));
        }

        Console.WriteLine("Worker completed.");
        return 0;
    }
}

public sealed class WorkerOptions
{
    public Uri JellyfinUrl { get; init; } = new("http://localhost:8096");

    public string ApiKey { get; init; } = string.Empty;

    public string BrowserPath { get; init; } = string.Empty;

    public int RemoteDebuggingPort { get; init; } = 9222;

    public int CrawlDelaySeconds { get; init; } = 527;

    public int Limit { get; init; }

    public static WorkerOptions? Parse(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Count; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = args[index][2..];
            var value = index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal) ? args[++index] : "true";
            values[key] = value;
        }

        if (!values.TryGetValue("jellyfin-url", out var urlText)
            || !Uri.TryCreate(urlText.TrimEnd('/') + "/", UriKind.Absolute, out var jellyfinUrl)
            || !values.TryGetValue("api-key", out var apiKey)
            || string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        return new WorkerOptions
        {
            JellyfinUrl = jellyfinUrl,
            ApiKey = apiKey,
            BrowserPath = values.GetValueOrDefault("browser-path", string.Empty),
            RemoteDebuggingPort = TryParsePositive(values.GetValueOrDefault("remote-debugging-port"), 9222),
            CrawlDelaySeconds = TryParsePositive(values.GetValueOrDefault("crawl-delay-seconds"), 527),
            Limit = TryParsePositive(values.GetValueOrDefault("limit"), 0)
        };
    }

    public static void WriteHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  Wrestling.CacheWorker --jellyfin-url http://server:8096 --api-key YOUR_KEY [--limit 3]");
        Console.WriteLine();
        Console.WriteLine("Optional:");
        Console.WriteLine("  --browser-path PATH              Path to msedge.exe or chrome.exe");
        Console.WriteLine("  --remote-debugging-port 9222     Local browser DevTools port");
        Console.WriteLine("  --crawl-delay-seconds 527        CageMatch crawl delay");
    }

    private static int TryParsePositive(string? value, int fallback)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0 ? parsed : fallback;
    }
}

public sealed class JellyfinCacheClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public JellyfinCacheClient(WorkerOptions options)
    {
        _httpClient = new HttpClient { BaseAddress = options.JellyfinUrl };
        _httpClient.DefaultRequestHeaders.Add("X-Emby-Token", options.ApiKey);
    }

    public async Task<List<QueueItem>> GetQueueAsync(CancellationToken cancellationToken)
    {
        var items = await _httpClient.GetFromJsonAsync<List<QueueItem>>("Wrestling/Cache/Queue", JsonOptions, cancellationToken).ConfigureAwait(false);
        return items ?? [];
    }

    public async Task SyncAsync(SyncedEvent syncedEvent, CancellationToken cancellationToken)
    {
        var request = new CacheSyncRequest { Events = [syncedEvent] };
        using var response = await _httpClient.PostAsJsonAsync("Wrestling/Cache/Sync", request, JsonOptions, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

public sealed class VisibleBrowser : IAsyncDisposable
{
    private readonly WorkerOptions _options;
    private readonly HttpClient _httpClient = new();
    private readonly ClientWebSocket _socket = new();
    private Process? _process;
    private DateTime _lastCagematchRequestUtc;
    private int _messageId;

    public VisibleBrowser(WorkerOptions options)
    {
        _options = options;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!await CanReachDevToolsAsync(cancellationToken).ConfigureAwait(false))
        {
            var browserPath = ResolveBrowserPath(_options.BrowserPath);
            var userDataDir = Path.Combine(AppContext.BaseDirectory, "browser-profile");
            Directory.CreateDirectory(userDataDir);
            _process = Process.Start(new ProcessStartInfo
            {
                FileName = browserPath,
                Arguments = string.Create(CultureInfo.InvariantCulture, $"--remote-debugging-port={_options.RemoteDebuggingPort} --user-data-dir=\"{userDataDir}\" --new-window about:blank"),
                UseShellExecute = false
            });
        }

        await WaitForDevToolsAsync(cancellationToken).ConfigureAwait(false);
        var tab = await OpenTabAsync(cancellationToken).ConfigureAwait(false);
        await _socket.ConnectAsync(new Uri(tab.WebSocketDebuggerUrl), cancellationToken).ConfigureAwait(false);
        await SendCommandAsync("Page.enable", null, cancellationToken).ConfigureAwait(false);
        await SendCommandAsync("Runtime.enable", null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetCagematchHtmlAsync(string url, CancellationToken cancellationToken)
    {
        await WaitForCrawlDelayAsync(cancellationToken).ConfigureAwait(false);
        _lastCagematchRequestUtc = DateTime.UtcNow;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Opening {url}"));
        await SendCommandAsync("Page.navigate", new { url }, cancellationToken).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        var result = await SendCommandAsync(
            "Runtime.evaluate",
            new { expression = "document.documentElement.outerHTML", returnByValue = true },
            cancellationToken).ConfigureAwait(false);
        return result.RootElement.GetProperty("result").GetProperty("result").GetProperty("value").GetString() ?? string.Empty;
    }

    public async ValueTask DisposeAsync()
    {
        _httpClient.Dispose();
        if (_socket.State == WebSocketState.Open)
        {
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        }

        _socket.Dispose();
        _process?.Dispose();
    }

    private async Task WaitForCrawlDelayAsync(CancellationToken cancellationToken)
    {
        if (_lastCagematchRequestUtc == default)
        {
            return;
        }

        var remaining = TimeSpan.FromSeconds(_options.CrawlDelaySeconds) - (DateTime.UtcNow - _lastCagematchRequestUtc);
        if (remaining <= TimeSpan.Zero)
        {
            return;
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Waiting {remaining.TotalSeconds:F0}s for CageMatch crawl delay."));
        await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonDocument> SendCommandAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _messageId);
        var payload = JsonSerializer.Serialize(new { id, method, @params = parameters }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var bytes = Encoding.UTF8.GetBytes(payload);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);

        while (true)
        {
            var message = await ReceiveMessageAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(message);
            if (document.RootElement.TryGetProperty("id", out var responseId) && responseId.GetInt32() == id)
            {
                return JsonDocument.Parse(message);
            }
        }
    }

    private async Task<string> ReceiveMessageAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException("Browser DevTools socket closed.");
            }

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }

    private async Task<DevToolsTab> OpenTabAsync(CancellationToken cancellationToken)
    {
        var url = string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{_options.RemoteDebuggingPort}/json/new?about:blank");
        using var response = await OpenTabResponseAsync(url, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<DevToolsTab>(cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Browser did not return a DevTools tab.");
    }

    private async Task<HttpResponseMessage> OpenTabResponseAsync(string url, CancellationToken cancellationToken)
    {
        using var putRequest = new HttpRequestMessage(HttpMethod.Put, url);
        var response = await _httpClient.SendAsync(putRequest, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        response.Dispose();
        response = await _httpClient.GetAsync(new Uri(url), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return response;
    }

    private async Task WaitForDevToolsAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (await CanReachDevToolsAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("Browser DevTools endpoint did not start.");
    }

    private async Task<bool> CanReachDevToolsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{_options.RemoteDebuggingPort}/json/version"), cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private static string ResolveBrowserPath(string configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        string[] candidates =
        [
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
        ];

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Could not find Edge or Chrome. Pass --browser-path.");
    }
}

public static partial class CagematchPageParser
{
    public static bool IsBlockedGate(string html)
    {
        return html.Contains("Forbidden", StringComparison.OrdinalIgnoreCase)
            || html.Contains("Access Denied", StringComparison.OrdinalIgnoreCase)
            || html.Contains("Javascript is required", StringComparison.OrdinalIgnoreCase)
            || html.Contains("enable javascript", StringComparison.OrdinalIgnoreCase)
            || html.Contains("cloudflare", StringComparison.OrdinalIgnoreCase)
            || html.Contains("captcha", StringComparison.OrdinalIgnoreCase);
    }

    public static CagematchCandidate? ChooseBestCandidate(string html, string title, int? year, DateTime? premiereDate)
    {
        var candidates = ParseSearchCandidates(html)
            .GroupBy(candidate => candidate.EventId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(candidate => candidate with { Score = Score(candidate.Name, title, year, premiereDate) })
            .OrderByDescending(candidate => candidate.Score)
            .ToList();

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        var best = candidates.FirstOrDefault();
        return best is not null && best.Score >= 75 && candidates.Count(item => item.Score == best.Score) == 1 ? best : null;
    }

    public static SyncedEvent ParseEventPage(string html, string eventId, string sourceUrl)
    {
        var title = Decode(TitleRegex().Match(html).Groups["title"].Value);
        var name = title.Split(['-'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            ?? string.Concat("CageMatch Event ", eventId);

        return new SyncedEvent
        {
            CagematchEventId = eventId,
            Name = name,
            EventDate = TryParseDate(html),
            SourceUrl = sourceUrl,
            Matches = ParseMatches(html).ToList()
        };
    }

    private static IEnumerable<CagematchCandidate> ParseSearchCandidates(string html)
    {
        foreach (Match match in EventLinkRegex().Matches(html))
        {
            yield return new CagematchCandidate(match.Groups["id"].Value, CleanCell(match.Groups["text"].Value), 0);
        }
    }

    private static IEnumerable<SyncedMatch> ParseMatches(string html)
    {
        var order = 1;
        foreach (Match rowMatch in RowRegex().Matches(html))
        {
            var cells = CellRegex().Matches(rowMatch.Value)
                .Select(match => CleanCell(match.Groups["cell"].Value))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();

            if (!cells.Any(LooksLikeMatchText))
            {
                continue;
            }

            var participantCell = cells.FirstOrDefault(LooksLikeMatchText) ?? cells[0];
            yield return new SyncedMatch
            {
                Order = order++,
                Participants = RemoveResultLanguage(participantCell),
                Stipulation = cells.FirstOrDefault(IsLikelyStipulation) ?? string.Empty,
                Rating = cells.LastOrDefault(IsLikelyRating) ?? string.Empty,
                Result = ExtractResult(participantCell)
            };
        }
    }

    private static int Score(string candidateName, string title, int? year, DateTime? premiereDate)
    {
        var target = Normalize(title);
        var candidate = Normalize(candidateName);
        if (target.Length == 0 || candidate.Length == 0)
        {
            return 0;
        }

        var score = candidate.Contains(target, StringComparison.Ordinal) || target.Contains(candidate, StringComparison.Ordinal) ? 80 : TokenOverlapScore(target, candidate);
        var expectedYear = premiereDate?.Year ?? year;
        if (expectedYear.HasValue && candidate.Contains(expectedYear.Value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            score += 30;
        }

        return score;
    }

    private static int TokenOverlapScore(string target, string candidate)
    {
        var targetTokens = target.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (targetTokens.Count == 0)
        {
            return 0;
        }

        var candidateTokens = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matched = targetTokens.Count(token => candidateTokens.Contains(token, StringComparer.OrdinalIgnoreCase));
        return (int)Math.Round(60.0 * matched / targetTokens.Count, MidpointRounding.AwayFromZero);
    }

    private static string Normalize(string value)
    {
        return WhitespaceRegex().Replace(PunctuationRegex().Replace(value, " "), " ").Trim().ToUpperInvariant();
    }

    private static bool LooksLikeMatchText(string value)
    {
        return value.Contains(" vs. ", StringComparison.OrdinalIgnoreCase)
            || value.Contains(" def. ", StringComparison.OrdinalIgnoreCase)
            || value.Contains(" defeats ", StringComparison.OrdinalIgnoreCase)
            || value.Contains("draw", StringComparison.OrdinalIgnoreCase);
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
        return match.Success ? match.Groups["winner"].Value.Trim() : string.Empty;
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

        string[] formats = ["dd.MM.yyyy", "yyyy-MM-dd", "MM/dd/yyyy"];
        return DateTime.TryParseExact(match.Groups["date"].Value, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.Date
            : null;
    }

    private static string CleanCell(string html)
    {
        return WhitespaceRegex().Replace(Decode(TagRegex().Replace(html, " ")), " ").Trim();
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

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex PunctuationRegex();

    [GeneratedRegex(@"<a[^>]+href\s*=\s*[""'][^""']*(?:[?&]|&amp;)id=1(?:&amp;|&)nr=(?<id>\d+)[^""']*[""'][^>]*>(?<text>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex EventLinkRegex();

    [GeneratedRegex(@"(?<date>\d{2}\.\d{2}\.\d{4}|\d{4}-\d{2}-\d{2}|\d{1,2}/\d{1,2}/\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"^\d+(?:\.\d+)?(?:\s*/\s*10)?(?:\s*\(\d+\))?$")]
    private static partial Regex RatingRegex();

    [GeneratedRegex(@"(?<winner>.+?)\s+(?:def\.|defeats|defeated)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex ResultRegex();
}

public sealed record DevToolsTab(string Id, string WebSocketDebuggerUrl);

public sealed record CagematchCandidate(string EventId, string Name, int Score);

public sealed class QueueItem
{
    public Guid ItemId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int? Year { get; set; }

    public DateTime? PremiereDate { get; set; }
}

public sealed class CacheSyncRequest
{
    public string Source { get; set; } = "Browser worker";

    public List<SyncedEvent> Events { get; set; } = [];
}

public sealed class SyncedEvent
{
    public string CagematchEventId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DateTime? EventDate { get; set; }

    public string SourceUrl { get; set; } = string.Empty;

    public List<SyncedMatch> Matches { get; set; } = [];
}

public sealed class SyncedMatch
{
    public int Order { get; set; }

    public string Participants { get; set; } = string.Empty;

    public string Stipulation { get; set; } = string.Empty;

    public string Rating { get; set; } = string.Empty;

    public string Result { get; set; } = string.Empty;
}
