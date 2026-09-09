using Inventory.Api.Data;
using Inventory.Api.Dtos;
using Inventory.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

public class SeatService : ISeatService
{
    private static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(5);

    private readonly InventoryDbContext _db;
    private readonly ILogger<SeatService> _logger;

    public SeatService(InventoryDbContext db, ILogger<SeatService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<HoldSeatResponse> HoldAsync(int seatId, HoldSeatRequest request, CancellationToken ct)
    {
        var seat = await _db.Seats.FirstOrDefaultAsync(s => s.Id == seatId, ct);
        if (seat is null)
            return new HoldSeatResponse(HoldOutcome.NotFound, seatId, null);

        if (seat.Status != SeatStatus.Available)
            return new HoldSeatResponse(HoldOutcome.Unavailable, seatId, null);

        seat.Status = SeatStatus.Held;
        seat.HeldByBookingId = request.BookingId;
        seat.HoldExpiresAt = DateTime.UtcNow.Add(HoldDuration);

        try
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Seat {SeatId} held by booking {BookingId} until {ExpiresAt}",
                seatId, request.BookingId, seat.HoldExpiresAt);

            return new HoldSeatResponse(HoldOutcome.Success, seatId, seat.HoldExpiresAt);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone else's write committed between our read and our SaveChanges.
            // This is the expected, healthy outcome of a race — not a system error.
            _logger.LogInformation(
                "Seat {SeatId} lost race: booking {BookingId} was beaten to it", seatId, request.BookingId);

            return new HoldSeatResponse(HoldOutcome.LostRace, seatId, null);
        }
    }

    public async Task<SeatOperationResult> ReleaseAsync(int seatId, Guid bookingId, CancellationToken ct)
    {
        var seat = await _db.Seats.FirstOrDefaultAsync(s => s.Id == seatId, ct);
        if (seat is null)
            return SeatOperationResult.NotFound;

        if (seat.HeldByBookingId != bookingId)
            return SeatOperationResult.NotHeldByThisBooking;

        seat.Status = SeatStatus.Available;
        seat.HeldByBookingId = null;
        seat.HoldExpiresAt = null;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seat {SeatId} released by booking {BookingId}", seatId, bookingId);

        return SeatOperationResult.Success;
    }

    public async Task<SeatOperationResult> ConfirmAsync(int seatId, Guid bookingId, CancellationToken ct)
    {
        var seat = await _db.Seats.FirstOrDefaultAsync(s => s.Id == seatId, ct);
        if (seat is null)
            return SeatOperationResult.NotFound;

        if (seat.HeldByBookingId != bookingId)
            return SeatOperationResult.NotHeldByThisBooking;

        seat.Status = SeatStatus.Sold;
        seat.HoldExpiresAt = null;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seat {SeatId} confirmed sold for booking {BookingId}", seatId, bookingId);

        return SeatOperationResult.Success;
    }
}
