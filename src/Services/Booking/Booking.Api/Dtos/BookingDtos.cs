using Booking.Api.Models;

namespace Booking.Api.Dtos;

public record PlaceBookingRequest(int EventId, int SeatId, Guid UserId, decimal Amount, string IdempotencyKey);

public record BookingResponse(Guid BookingId, BookingStatus Status, int SeatId, string? FailureReason);

public record CancelBookingRequest(string Reason);
