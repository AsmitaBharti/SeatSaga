using Booking.Api.Clients;
using Booking.Api.Data;
using Booking.Api.Dtos;
using Booking.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Booking.Api.Services;

/// <summary>
/// Orchestrated saga (as opposed to choreography): this single method drives every
/// step of a booking explicitly and decides what happens on failure, rather than each
/// service reacting to the others' events independently. See the LLD's "Saga: Choreography
/// vs Orchestration" discussion for the reasoning.
/// </summary>
public class BookingOrchestrator : IBookingOrchestrator
{
    private readonly BookingDbContext _db;
    private readonly IInventoryClient _inventoryClient;
    private readonly IPaymentClient _paymentClient;
    private readonly ILogger<BookingOrchestrator> _logger;

    public BookingOrchestrator(
        BookingDbContext db,
        IInventoryClient inventoryClient,
        IPaymentClient paymentClient,
        ILogger<BookingOrchestrator> logger)
    {
        _db = db;
        _inventoryClient = inventoryClient;
        _paymentClient = paymentClient;
        _logger = logger;
    }

    public async Task<BookingResponse?> GetBookingAsync(Guid bookingId, CancellationToken ct)
    {
        var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        return booking is null ? null : ToResponse(booking);
    }

    public async Task<BookingResponse> PlaceBookingAsync(PlaceBookingRequest request, CancellationToken ct)
    {
        // ---- Idempotency check: a retried/double-clicked request returns the
        // original result instead of running the saga twice. ----
        var existing = await _db.Bookings.FirstOrDefaultAsync(b => b.IdempotencyKey == request.IdempotencyKey, ct);
        if (existing is not null)
        {
            _logger.LogInformation("Idempotent replay for key {IdempotencyKey}, returning existing booking {BookingId}",
                request.IdempotencyKey, existing.Id);
            return ToResponse(existing);
        }

        var booking = new BookingEntity
        {
            EventId = request.EventId,
            SeatId = request.SeatId,
            UserId = request.UserId,
            Amount = request.Amount,
            IdempotencyKey = request.IdempotencyKey,
            Status = BookingStatus.Pending
        };
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync(ct);

        // ---- Step 1: hold the seat ----
        var holdOutcome = await _inventoryClient.HoldSeatAsync(booking.SeatId, booking.Id, booking.UserId, ct);
        if (holdOutcome != InventoryHoldOutcome.Success)
        {
            return await FailAsync(booking, $"Seat unavailable: {holdOutcome}", ct);
        }

        await TransitionAsync(booking, BookingStatus.AwaitingPayment, ct);

        // ---- Step 2: charge payment (wrapped in the resilience pipeline configured
        // on the named HttpClient in Program.cs — retry, timeout, circuit breaker) ----
        var chargeResult = await _paymentClient.ChargeAsync(booking.Id, booking.Amount, booking.IdempotencyKey, ct);
        if (!chargeResult.Success)
        {
            // ---- Compensation: release the seat we already hold ----
            await CompensateAsync(booking, chargeResult.ErrorReason ?? "Payment failed", ct);
            return ToResponse(booking);
        }

        booking.PaymentTransactionId = chargeResult.TransactionId;

        // ---- Step 3: confirm the seat as sold ----
        try
        {
            await _inventoryClient.ConfirmSeatAsync(booking.SeatId, booking.Id, ct);
        }
        catch (Exception ex)
        {
            // Payment succeeded but confirmation failed — in a full build this triggers
            // a refund as well, since we can't leave a charged customer without a seat.
            // Flagged here rather than silently swallowed.
            _logger.LogError(ex, "Seat confirmation failed after successful payment for booking {BookingId} — refunding", booking.Id);
            await _paymentClient.RefundAsync(chargeResult.TransactionId!, ct);
            await CompensateAsync(booking, "Seat confirmation failed after payment", ct);
            return ToResponse(booking);
        }

        await TransitionAsync(booking, BookingStatus.Confirmed, ct);
        return ToResponse(booking);
    }

    private async Task<BookingResponse> FailAsync(BookingEntity booking, string reason, CancellationToken ct)
    {
        booking.Status = BookingStatus.Failed;
        booking.FailureReason = reason;
        booking.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Booking {BookingId} failed: {Reason}", booking.Id, reason);
        return ToResponse(booking);
    }

    private async Task CompensateAsync(BookingEntity booking, string reason, CancellationToken ct)
    {
        booking.Status = BookingStatus.Compensating;
        booking.FailureReason = reason;
        booking.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _inventoryClient.ReleaseSeatAsync(booking.SeatId, booking.Id, ct);

        booking.Status = BookingStatus.Cancelled;
        booking.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Booking {BookingId} compensated and cancelled: {Reason}", booking.Id, reason);
    }

    private async Task TransitionAsync(BookingEntity booking, BookingStatus status, CancellationToken ct)
    {
        booking.Status = status;
        booking.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private static BookingResponse ToResponse(BookingEntity b) =>
        new(b.Id, b.Status, b.SeatId, b.FailureReason);
}
