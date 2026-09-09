using Inventory.Api.Data;
using Inventory.Api.Dtos;
using Inventory.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Llm;

/// <summary>
/// Deliberately contains zero LLM calls. Criteria arrives already parsed and
/// validated (see SeatCriteriaParser); everything here is plain, testable,
/// deterministic seat-filtering logic — the part that must never hallucinate.
/// </summary>
public class SeatRecommendationService : ISeatRecommendationService
{
    private const int MaxRowsConsideredFront = 3;

    private readonly InventoryDbContext _db;
    private readonly ISeatCriteriaParser _criteriaParser;
    private readonly ILogger<SeatRecommendationService> _logger;

    public SeatRecommendationService(
        InventoryDbContext db,
        ISeatCriteriaParser criteriaParser,
        ILogger<SeatRecommendationService> logger)
    {
        _db = db;
        _criteriaParser = criteriaParser;
        _logger = logger;
    }

    public async Task<SeatConciergeResponse> RecommendAsync(int eventId, string preferenceText, CancellationToken ct)
    {
        var (criteria, usedFallback) = await _criteriaParser.ParseAsync(preferenceText, ct);

        var seats = await _db.Seats
            .Where(s => s.EventId == eventId && s.Status == SeatStatus.Available)
            .ToListAsync(ct);

        var maxRow = seats.Count == 0 ? 0 : seats.Max(s => s.Row);
        var maxCol = seats.Count == 0 ? 0 : seats.Max(s => s.Col);

        var candidates = seats.AsEnumerable();

        if (criteria.AreaPreference != "any" && seats.Count > 0)
        {
            candidates = criteria.AreaPreference switch
            {
                "front" => candidates.Where(s => s.Row <= MaxRowsConsideredFront),
                "back" => candidates.Where(s => s.Row >= maxRow - MaxRowsConsideredFront),
                "middle" => candidates.Where(s => s.Row > MaxRowsConsideredFront && s.Row < maxRow - MaxRowsConsideredFront),
                _ => candidates
            };
        }

        if (criteria.PreferAisle)
        {
            // Treat the first and last column of each row as "aisle" seats.
            candidates = candidates.Where(s => s.Col == 0 || s.Col == maxCol);
        }

        var ranked = candidates
            .OrderBy(s => s.Row) // closer to the front first, as a simple relevance proxy
            .ThenBy(s => s.Col)
            .Take(Math.Max(criteria.PartySize, 1))
            .ToList();

        var explanation = BuildExplanation(criteria, ranked.Count, usedFallback);

        _logger.LogInformation(
            "Seat concierge for event {EventId}: {Count} seats recommended (fallback={UsedFallback})",
            eventId, ranked.Count, usedFallback);

        return new SeatConciergeResponse(
            ranked.Select(s => new SeatDto(s.Id, s.Row, s.Col, s.Status)).ToList(),
            explanation,
            usedFallback);
    }

    private static string BuildExplanation(SeatCriteria criteria, int foundCount, bool usedFallback)
    {
        if (foundCount == 0)
            return "No available seats matched those preferences — try relaxing the price cap or area preference.";

        var parts = new List<string> { $"Found {foundCount} seat(s)" };
        if (criteria.AreaPreference != "any") parts.Add($"in the {criteria.AreaPreference} of the venue");
        if (criteria.PreferAisle) parts.Add("on the aisle");
        if (criteria.MaxPrice is not null) parts.Add($"within your ${criteria.MaxPrice} budget");

        var explanation = string.Join(" ", parts) + ".";
        if (usedFallback)
            explanation += " (Couldn't fully interpret your request, so default preferences were used.)";

        return explanation;
    }
}
