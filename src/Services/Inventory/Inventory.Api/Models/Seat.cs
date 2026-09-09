using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Models;

/// <summary>
/// A single seat for an event. <see cref="RowVersion"/> is the optimistic-concurrency
/// token: EF Core includes it in the WHERE clause of every UPDATE. If two requests read
/// the same seat and both try to hold it, only the first UPDATE wins — the second throws
/// DbUpdateConcurrencyException, which SeatsController translates into a clean "LostRace"
/// result instead of corrupting state or double-booking the seat.
/// </summary>
public class Seat
{
    public int Id { get; set; }

    public int EventId { get; set; }
    public EventEntity Event { get; set; } = default!;

    public short Row { get; set; }
    public short Col { get; set; }

    public SeatStatus Status { get; set; } = SeatStatus.Available;

    public Guid? HeldByBookingId { get; set; }
    public DateTime? HoldExpiresAt { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = default!;
}
