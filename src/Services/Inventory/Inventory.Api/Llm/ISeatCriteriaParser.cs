namespace Inventory.Api.Llm;

public interface ISeatCriteriaParser
{
    /// <summary>
    /// Interprets free-text seat preferences into structured criteria. Never throws
    /// on LLM failure/malformed output — always returns something usable, falling
    /// back to SeatCriteria.Default() so the recommendation flow degrades gracefully
    /// instead of failing the request.
    /// </summary>
    Task<(SeatCriteria Criteria, bool UsedFallback)> ParseAsync(string preferenceText, CancellationToken ct);
}
