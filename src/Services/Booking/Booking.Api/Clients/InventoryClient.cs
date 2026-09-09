using System.Net;
using System.Net.Http.Json;

namespace Booking.Api.Clients;

public class InventoryClient : IInventoryClient
{
    private readonly HttpClient _http;
    private readonly ILogger<InventoryClient> _logger;

    public InventoryClient(HttpClient http, ILogger<InventoryClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<InventoryHoldOutcome> HoldSeatAsync(int seatId, Guid bookingId, Guid userId, CancellationToken ct)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"api/seats/{seatId}/hold", new { bookingId, userId }, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Inventory hold call failed with {StatusCode} for seat {SeatId}", response.StatusCode, seatId);
                return InventoryHoldOutcome.Unreachable;
            }

            var body = await response.Content.ReadFromJsonAsync<HoldResponseBody>(cancellationToken: ct);
            return body?.Result switch
            {
                "Success" => InventoryHoldOutcome.Success,
                "LostRace" => InventoryHoldOutcome.LostRace,
                "Unavailable" => InventoryHoldOutcome.Unavailable,
                "NotFound" => InventoryHoldOutcome.NotFound,
                _ => InventoryHoldOutcome.Unreachable
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Resilience pipeline (see Program.cs) already retried/circuit-broke before
            // this exception surfaces — at this point Inventory is genuinely unreachable.
            _logger.LogError(ex, "Inventory Service unreachable while holding seat {SeatId}", seatId);
            return InventoryHoldOutcome.Unreachable;
        }
    }

    public async Task ReleaseSeatAsync(int seatId, Guid bookingId, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync($"api/seats/{seatId}/release", new { bookingId }, ct);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            _logger.LogError("Failed to release seat {SeatId} for booking {BookingId}: {StatusCode}", seatId, bookingId, response.StatusCode);
        }
    }

    public async Task ConfirmSeatAsync(int seatId, Guid bookingId, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync($"api/seats/{seatId}/confirm", new { bookingId }, ct);
        response.EnsureSuccessStatusCode();
    }

    private record HoldResponseBody(string Result, int SeatId, DateTime? HoldExpiresAt);
}
