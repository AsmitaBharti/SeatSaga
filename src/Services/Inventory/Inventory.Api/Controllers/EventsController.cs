using Inventory.Api.Data;
using Inventory.Api.Dtos;
using Inventory.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/events")]
public class EventsController : ControllerBase
{
    private readonly InventoryDbContext _db;

    public EventsController(InventoryDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Full seat map for an event. In the finished system this is served from the
    /// Redis read model (CQRS query side) — this endpoint is the authoritative
    /// SQL-backed fallback and what the read-model projector calls to rebuild state.
    /// </summary>
    [HttpGet("{eventId:int}/seats")]
    [ProducesResponseType(typeof(SeatMapResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SeatMapResponse>> GetSeatMap(int eventId, CancellationToken ct)
    {
        var exists = await _db.Events.AnyAsync(e => e.Id == eventId, ct);
        if (!exists)
            return NotFound();

        var seats = await _db.Seats
            .Where(s => s.EventId == eventId)
            .OrderBy(s => s.Row).ThenBy(s => s.Col)
            .Select(s => new SeatDto(s.Id, s.Row, s.Col, s.Status))
            .ToListAsync(ct);

        return Ok(new SeatMapResponse(eventId, seats));
    }

    /// <summary>
    /// Demo/admin-only endpoint: generates a seat grid for an event so you can
    /// immediately load-test the hold endpoint without hand-inserting rows.
    /// Not exposed through the Gateway in the real routing table.
    /// </summary>
    [HttpPost("seed")]
    [ProducesResponseType(typeof(SeedEventResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<SeedEventResponse>> Seed([FromBody] SeedEventRequest request, CancellationToken ct)
    {
        var evt = new EventEntity
        {
            Name = request.Name,
            VenueName = request.VenueName,
            StartsAt = request.StartsAt,
            BasePrice = request.BasePrice
        };

        for (short r = 0; r < request.Rows; r++)
        {
            for (short c = 0; c < request.Cols; c++)
            {
                evt.Seats.Add(new Seat { Row = r, Col = c, Status = SeatStatus.Available });
            }
        }

        _db.Events.Add(evt);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetSeatMap), new { eventId = evt.Id },
            new SeedEventResponse(evt.Id, evt.Seats.Count));
    }
}
