using Inventory.Api.Dtos;

namespace Inventory.Api.Llm;

public record SeatConciergeRequest(string PreferenceText);

public record SeatConciergeResponse(
    IReadOnlyList<SeatDto> RecommendedSeats,
    string Explanation,
    bool UsedFallbackCriteria);
