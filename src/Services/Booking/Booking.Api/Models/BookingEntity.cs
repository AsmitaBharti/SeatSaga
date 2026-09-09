namespace Booking.Api.Models;

/// <summary>
/// A single booking attempt and its saga progress. EventId/SeatId reference
/// Inventory Service's data by ID only (no FK across service boundaries —
/// each service owns its own database).
/// </summary>
public class BookingEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int EventId { get; set; }
    public int SeatId { get; set; }
    public Guid UserId { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public decimal Amount { get; set; }

    /// <summary>
    /// Client-supplied key. Unique constraint means a retried or double-clicked
    /// request returns the original booking instead of creating a second one.
    /// </summary>
    public string IdempotencyKey { get; set; } = default!;

    public string? PaymentTransactionId { get; set; }
    public string? FailureReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
