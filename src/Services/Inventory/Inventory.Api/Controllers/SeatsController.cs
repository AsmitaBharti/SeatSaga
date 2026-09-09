using Inventory.Api.Dtos;
using Inventory.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/seats")]
public class SeatsController : ControllerBase
{
    private readonly ISeatService _seatService;

    public SeatsController(ISeatService seatService)
    {
        _seatService = seatService;
    }

    /// <summary>
    /// Attempts to place a temporary hold on a seat. Under a race, exactly one
    /// caller receives Success and the other(s) receive LostRace — the actual
    /// concurrency handling lives in SeatService, not here.
    /// </summary>
    [HttpPost("{seatId:int}/hold")]
    [ProducesResponseType(typeof(HoldSeatResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<HoldSeatResponse>> Hold(int seatId, [FromBody] HoldSeatRequest request, CancellationToken ct)
    {
        var result = await _seatService.HoldAsync(seatId, request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Releases a hold (or a sold seat) back to Available. Used by the Booking
    /// Service's saga compensation path when payment fails, and by the
    /// background expiry sweep's underlying service method.
    /// </summary>
    [HttpPost("{seatId:int}/release")]
    public async Task<IActionResult> Release(int seatId, [FromBody] ReleaseSeatRequest request, CancellationToken ct)
    {
        var result = await _seatService.ReleaseAsync(seatId, request.BookingId, ct);

        return result switch
        {
            SeatOperationResult.Success => Ok(new { status = "Released" }),
            SeatOperationResult.NotFound => NotFound(),
            SeatOperationResult.NotHeldByThisBooking => Conflict(new { message = "Seat is not held by this booking." }),
            _ => Problem()
        };
    }

    /// <summary>
    /// Converts a Held seat to Sold once payment has completed.
    /// </summary>
    [HttpPost("{seatId:int}/confirm")]
    public async Task<IActionResult> Confirm(int seatId, [FromBody] ConfirmSeatRequest request, CancellationToken ct)
    {
        var result = await _seatService.ConfirmAsync(seatId, request.BookingId, ct);

        return result switch
        {
            SeatOperationResult.Success => Ok(new { status = "Sold" }),
            SeatOperationResult.NotFound => NotFound(),
            SeatOperationResult.NotHeldByThisBooking => Conflict(new { message = "Seat is not held by this booking." }),
            _ => Problem()
        };
    }
}
