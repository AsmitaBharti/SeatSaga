using Payment.Api.Dtos;

namespace Payment.Api.Services;

public enum RefundOutcome { Success, NotFound }

public interface IPaymentProcessingService
{
    Task<ChargeResponse> ChargeAsync(ChargeRequest request, CancellationToken ct);
    Task<RefundOutcome> RefundAsync(Guid transactionId, string reason, CancellationToken ct);
}
