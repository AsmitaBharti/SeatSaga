using Inventory.Api.Models;

namespace Inventory.Api.Dtos;

public record SeatDto(int SeatId, short Row, short Col, SeatStatus Status);

public record SeatMapResponse(int EventId, IReadOnlyList<SeatDto> Seats);

public record HoldSeatRequest(Guid BookingId, Guid UserId);

public enum HoldOutcome
{
    Success,
    LostRace,
    Unavailable,
    NotFound
}

public record HoldSeatResponse(HoldOutcome Result, int SeatId, DateTime? HoldExpiresAt);

public record ReleaseSeatRequest(Guid BookingId);

public record ConfirmSeatRequest(Guid BookingId);

public record SeedEventRequest(string Name, string VenueName, DateTime StartsAt, decimal BasePrice, short Rows, short Cols);

public record SeedEventResponse(int EventId, int SeatCount);
