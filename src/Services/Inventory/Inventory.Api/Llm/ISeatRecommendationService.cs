namespace Inventory.Api.Llm;

public interface ISeatRecommendationService
{
    Task<SeatConciergeResponse> RecommendAsync(int eventId, string preferenceText, CancellationToken ct);
}
