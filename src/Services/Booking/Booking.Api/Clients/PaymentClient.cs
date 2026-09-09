using System.Net.Http.Json;
using Polly.CircuitBreaker;

namespace Booking.Api.Clients;

public class PaymentClient : IPaymentClient
{
    private readonly HttpClient _http;
    private readonly ILogger<PaymentClient> _logger;

    public PaymentClient(HttpClient http, ILogger<PaymentClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<ChargeResult> ChargeAsync(Guid bookingId, decimal amount, string idempotencyKey, CancellationToken ct)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/payments/charge", new { bookingId, amount, idempotencyKey }, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Payment charge failed with {StatusCode} for booking {BookingId}", response.StatusCode, bookingId);
                return new ChargeResult(false, null, $"Payment service returned {response.StatusCode}");
            }

            var body = await response.Content.ReadFromJsonAsync<ChargeResponseBody>(cancellationToken: ct);
            return new ChargeResult(true, body?.TransactionId, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or BrokenCircuitException)
        {
            // Expected today: Payment Service doesn't exist yet, so every call exhausts
            // retries and (once enough failures accumulate) trips the circuit breaker,
            // landing here. The saga treats this exactly like a real declined payment —
            // it compensates by releasing the seat. This is intentional: it's how you'll
            // demo the circuit breaker live once Payment Service exists and its /admin/chaos
            // endpoint is used to simulate flakiness instead of total absence.
            _logger.LogError(ex, "Payment Service unreachable for booking {BookingId}", bookingId);
            return new ChargeResult(false, null, "Payment service unreachable");
        }
    }

    public async Task RefundAsync(string transactionId, CancellationToken ct)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"api/payments/{transactionId}/refund", new { reason = "Booking compensation" }, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Refund failed for transaction {TransactionId}: {StatusCode}", transactionId, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refund call failed for transaction {TransactionId}", transactionId);
        }
    }

    private record ChargeResponseBody(string TransactionId, string Status);
}
