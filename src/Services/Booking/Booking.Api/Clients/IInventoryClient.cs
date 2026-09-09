namespace Booking.Api.Clients;

public enum InventoryHoldOutcome { Success, LostRace, Unavailable, NotFound, Unreachable }

public interface IInventoryClient
{
    Task<InventoryHoldOutcome> HoldSeatAsync(int seatId, Guid bookingId, Guid userId, CancellationToken ct);
    Task ReleaseSeatAsync(int seatId, Guid bookingId, CancellationToken ct);
    Task ConfirmSeatAsync(int seatId, Guid bookingId, CancellationToken ct);
}
