namespace Payment.Api.Dtos;

public record ChargeRequest(Guid BookingId, decimal Amount, string IdempotencyKey);
public record ChargeResponse(Guid TransactionId, string Status);

public record RefundRequest(string Reason);
public record RefundResponse(string Status);

/// <summary>Admin/demo endpoint contract for injecting synthetic failures/latency.</summary>
public record ChaosSettingsRequest(int FailureRatePct, int LatencyMs);
