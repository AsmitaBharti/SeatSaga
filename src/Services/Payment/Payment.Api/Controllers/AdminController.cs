using Payment.Api.Dtos;
using Payment.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Payment.Api.Controllers;

[ApiController]
[Route("admin")]
public class AdminController : ControllerBase
{
    private readonly ChaosSettings _chaos;

    public AdminController(ChaosSettings chaos)
    {
        _chaos = chaos;
    }

    /// <summary>
    /// Demo-only control surface: set a failure rate and/or latency so Booking
    /// Service's retry -> circuit breaker -> compensation path can be observed live.
    /// Not exposed through the Gateway's routing table in a real deployment.
    /// </summary>
    [HttpPost("chaos")]
    public IActionResult SetChaos([FromBody] ChaosSettingsRequest request)
    {
        _chaos.FailureRatePct = Math.Clamp(request.FailureRatePct, 0, 100);
        _chaos.LatencyMs = Math.Max(request.LatencyMs, 0);
        return NoContent();
    }

    [HttpGet("chaos")]
    public IActionResult GetChaos() => Ok(new { _chaos.FailureRatePct, _chaos.LatencyMs });
}
