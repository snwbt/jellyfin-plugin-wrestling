using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Wrestling.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Wrestling.Api;

/// <summary>
/// Wrestling plugin API.
/// </summary>
[ApiController]
[Authorize]
[Route("Wrestling")]
public class WrestlingController : ControllerBase
{
    private readonly IWrestlingMatchService _matchService;
    private readonly IMatchCardApplyService _applyService;
    private readonly IWrestlingAutoScanService _scanService;
    private readonly IImportedMatchCacheService _importedCacheService;

    /// <summary>
    /// Initializes a new instance of the <see cref="WrestlingController"/> class.
    /// </summary>
    public WrestlingController(
        IWrestlingMatchService matchService,
        IMatchCardApplyService applyService,
        IWrestlingAutoScanService scanService,
        IImportedMatchCacheService importedCacheService)
    {
        _matchService = matchService;
        _applyService = applyService;
        _scanService = scanService;
        _importedCacheService = importedCacheService;
    }

    /// <summary>
    /// Gets cached wrestling matches for an item.
    /// </summary>
    [HttpGet("Items/{itemId:guid}/Matches")]
    public async Task<ActionResult<WrestlingMatchesResponse>> GetMatches(
        [FromRoute] Guid itemId,
        [FromQuery] bool includeResults,
        CancellationToken cancellationToken)
    {
        var response = await _matchService.GetMatchesAsync(itemId, includeResults, cancellationToken).ConfigureAwait(false);
        return response is null ? NotFound() : Ok(response);
    }

    /// <summary>
    /// Applies manually configured match cards to the configured PPV library.
    /// </summary>
    [HttpPost("Apply")]
    public async Task<ActionResult<MatchCardApplyResult>> ApplyMatchCards(CancellationToken cancellationToken)
    {
        return Ok(await _applyService.ApplyAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Gets selectable Jellyfin libraries.
    /// </summary>
    [HttpGet("Libraries")]
    public ActionResult<IReadOnlyList<WrestlingLibraryInfo>> GetLibraries()
    {
        return Ok(_scanService.GetLibraries());
    }

    /// <summary>
    /// Gets current automatic scan status.
    /// </summary>
    [HttpGet("Status")]
    public ActionResult<WrestlingScanStatus> GetStatus()
    {
        return Ok(_scanService.GetStatus());
    }

    /// <summary>
    /// Queues an automatic CageMatch scan.
    /// </summary>
    [HttpPost("Scan")]
    public async Task<ActionResult<WrestlingScanStatus>> Scan(CancellationToken cancellationToken)
    {
        return Ok(await _scanService.QueueScanAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Cancels the current automatic scan.
    /// </summary>
    [HttpPost("CancelScan")]
    public ActionResult<WrestlingScanStatus> CancelScan()
    {
        return Ok(_scanService.CancelScan());
    }

    /// <summary>
    /// Clears automatic scan status.
    /// </summary>
    [HttpPost("ClearStatus")]
    public ActionResult<WrestlingScanStatus> ClearStatus()
    {
        return Ok(_scanService.ClearStatus());
    }

    /// <summary>
    /// Imports workbook-exported CSV cache rows.
    /// </summary>
    [HttpPost("Cache/Import")]
    public ActionResult<ImportedCacheImportResult> ImportCache([FromBody] ImportedCacheImportRequest? request)
    {
        return Ok(_importedCacheService.ImportCsv(request?.Csv ?? string.Empty));
    }

    /// <summary>
    /// Serves the optional Jellyfin Web enhancement script.
    /// </summary>
    [HttpGet("Web/wrestling-match-card.js")]
    [AllowAnonymous]
    public IActionResult GetWebEnhancement()
    {
        var assembly = Assembly.GetExecutingAssembly();
        const string resourceName = "Jellyfin.Plugin.Wrestling.Web.wrestling-match-card.js";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return NotFound();
        }

        using var reader = new StreamReader(stream);
        return Content(reader.ReadToEnd(), "application/javascript");
    }
}
