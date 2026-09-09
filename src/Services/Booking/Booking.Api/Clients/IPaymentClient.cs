namespace Booking.Api.Clients;

public record ChargeResult(bool Success, string? TransactionId, string? ErrorReason);

public interface IPaymentClient
{
    Task<ChargeResult> ChargeAsync(Guid bookingId, decimal amount, string idempotencyKey, CancellationToken ct);
    Task RefundAsync(string transactionId, CancellationToken ct);
}
