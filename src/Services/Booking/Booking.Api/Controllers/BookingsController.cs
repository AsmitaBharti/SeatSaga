using Booking.Api.Dtos;
using Booking.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Booking.Api.Controllers;

[ApiController]
[Route("api/bookings")]
public class BookingsController : ControllerBase
{
    private readonly IBookingOrchestrator _orchestrator;

    public BookingsController(IBookingOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    /// <summary>
    /// Starts the booking saga: hold seat -> charge payment -> confirm seat,
    /// with automatic compensation if any step fails. All orchestration logic
    /// lives in BookingOrchestrator — this action only translates HTTP in and out.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(BookingResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<BookingResponse>> Place([FromBody] PlaceBookingRequest request, CancellationToken ct)
    {
        var result = await _orchestrator.PlaceBookingAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.BookingId }, result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BookingResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<BookingResponse>> GetById(Guid id, CancellationToken ct)
    {
        var result = await _orchestrator.GetBookingAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
