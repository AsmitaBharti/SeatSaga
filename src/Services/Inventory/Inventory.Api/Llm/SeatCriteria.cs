using System.Text.Json.Serialization;

namespace Inventory.Api.Llm;

/// <summary>
/// The ONLY thing the LLM is allowed to produce. Deliberately narrow: no seat IDs,
/// no free-form text fields that flow into a query — just bounded, validated criteria
/// that SeatRecommendationService uses to filter/rank seats in plain C#.
/// </summary>
public class SeatCriteria
{
    [JsonPropertyName("maxPrice")]
    public decimal? MaxPrice { get; set; }

    [JsonPropertyName("partySize")]
    public int PartySize { get; set; } = 1;

    [JsonPropertyName("preferAisle")]
    public bool PreferAisle { get; set; }

    /// <summary>front | middle | back | any</summary>
    [JsonPropertyName("areaPreference")]
    public string AreaPreference { get; set; } = "any";

    public static SeatCriteria Default() => new();

    /// <summary>Clamps LLM output into safe, sane bounds regardless of what the model returned.</summary>
    public SeatCriteria Sanitize()
    {
        PartySize = Math.Clamp(PartySize, 1, 10);
        if (MaxPrice is < 0) MaxPrice = null;
        AreaPreference = AreaPreference?.ToLowerInvariant() switch
        {
            "front" or "middle" or "back" => AreaPreference.ToLowerInvariant(),
            _ => "any"
        };
        return this;
    }
}
