using Booking.Api.Dtos;

namespace Booking.Api.Services;

public interface IBookingOrchestrator
{
    Task<BookingResponse> PlaceBookingAsync(PlaceBookingRequest request, CancellationToken ct);
    Task<BookingResponse?> GetBookingAsync(Guid bookingId, CancellationToken ct);
}
