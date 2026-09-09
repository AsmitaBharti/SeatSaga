using Inventory.Api.Dtos;

namespace Inventory.Api.Services;

/// <summary>
/// Seat-hold business logic, kept independent of ASP.NET Core so it can be
/// unit-tested directly and reused by other callers later (e.g. the Booking
/// Service's saga compensation step, once services talk over the bus instead
/// of just HTTP).
/// </summary>
public enum SeatOperationResult
{
    Success,
    NotFound,
    NotHeldByThisBooking
}

public interface ISeatService
{
    Task<HoldSeatResponse> HoldAsync(int seatId, HoldSeatRequest request, CancellationToken ct);
    Task<SeatOperationResult> ReleaseAsync(int seatId, Guid bookingId, CancellationToken ct);
    Task<SeatOperationResult> ConfirmAsync(int seatId, Guid bookingId, CancellationToken ct);
}
