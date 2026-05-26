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

    /// <summary>
    /// Initializes a new instance of the <see cref="WrestlingController"/> class.
    /// </summary>
    public WrestlingController(IWrestlingMatchService matchService, IMatchCardApplyService applyService)
    {
        _matchService = matchService;
        _applyService = applyService;
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
