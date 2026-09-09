using Payment.Api.Data;
using Payment.Api.Dtos;
using Payment.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Payment.Api.Services;

public class PaymentProcessingService : IPaymentProcessingService
{
    private readonly PaymentDbContext _db;
    private readonly ChaosSettings _chaos;
    private readonly ILogger<PaymentProcessingService> _logger;
    private static readonly Random Random = new();

    public PaymentProcessingService(PaymentDbContext db, ChaosSettings chaos, ILogger<PaymentProcessingService> logger)
    {
        _db = db;
        _chaos = chaos;
        _logger = logger;
    }

    public async Task<ChargeResponse> ChargeAsync(ChargeRequest request, CancellationToken ct)
    {
        // ---- Idempotency: replay of the same key returns the original transaction ----
        var existing = await _db.Payments.FirstOrDefaultAsync(p => p.IdempotencyKey == request.IdempotencyKey, ct);
        if (existing is not null)
        {
            _logger.LogInformation("Idempotent replay for key {IdempotencyKey}, returning existing transaction {TransactionId}",
                request.IdempotencyKey, existing.TransactionId);
            return new ChargeResponse(existing.TransactionId, existing.Status.ToString());
        }

        // ---- Chaos injection: simulate latency and/or failure on demand ----
        if (_chaos.LatencyMs > 0)
        {
            await Task.Delay(_chaos.LatencyMs, ct);
        }

        if (_chaos.FailureRatePct > 0 && Random.Next(100) < _chaos.FailureRatePct)
        {
            _logger.LogWarning("Chaos-injected failure for booking {BookingId}", request.BookingId);
            throw new PaymentDeclinedException("Simulated gateway failure (chaos injection active)");
        }

        var payment = new PaymentEntity
        {
            BookingId = request.BookingId,
            Amount = request.Amount,
            Status = PaymentStatus.Charged,
            IdempotencyKey = request.IdempotencyKey
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Charged {Amount} for booking {BookingId}, transaction {TransactionId}",
            request.Amount, request.BookingId, payment.TransactionId);

        return new ChargeResponse(payment.TransactionId, payment.Status.ToString());
    }

    public async Task<RefundOutcome> RefundAsync(Guid transactionId, string reason, CancellationToken ct)
    {
        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.TransactionId == transactionId, ct);
        if (payment is null)
            return RefundOutcome.NotFound;

        payment.Status = PaymentStatus.Refunded;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Refunded transaction {TransactionId}: {Reason}", transactionId, reason);
        return RefundOutcome.Success;
    }
}

/// <summary>Thrown to simulate a real gateway decline; the controller maps this to a 502/503-style response.</summary>
public class PaymentDeclinedException : Exception
{
    public PaymentDeclinedException(string message) : base(message) { }
}
