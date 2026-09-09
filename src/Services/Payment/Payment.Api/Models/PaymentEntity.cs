namespace Payment.Api.Models;

public class PaymentEntity
{
    public Guid TransactionId { get; set; } = Guid.NewGuid();
    public Guid BookingId { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; }

    /// <summary>Unique constraint enforces charge idempotency at the DB level.</summary>
    public string IdempotencyKey { get; set; } = default!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
