using Inventory.Api.Llm;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/events/{eventId:int}/concierge")]
public class SeatConciergeController : ControllerBase
{
    private readonly ISeatRecommendationService _recommendationService;

    public SeatConciergeController(ISeatRecommendationService recommendationService)
    {
        _recommendationService = recommendationService;
    }

    /// <summary>
    /// Accepts a free-text seat preference (e.g. "2 seats together, under $60, near the
    /// front, aisle if possible") and returns recommended seats. The LLM only interprets
    /// the text into structured criteria — all filtering/ranking happens deterministically
    /// in SeatRecommendationService. See Llm/SeatCriteriaParser.cs for the interpretation
    /// boundary.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(SeatConciergeResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SeatConciergeResponse>> Recommend(
        int eventId, [FromBody] SeatConciergeRequest request, CancellationToken ct)
    {
        var result = await _recommendationService.RecommendAsync(eventId, request.PreferenceText, ct);
        return Ok(result);
    }
}
