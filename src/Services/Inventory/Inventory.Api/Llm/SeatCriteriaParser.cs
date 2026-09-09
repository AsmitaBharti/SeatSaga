using System.Text.Json;

namespace Inventory.Api.Llm;

public class SeatCriteriaParser : ISeatCriteriaParser
{
    private const string SystemPrompt = """
        You extract seat-booking preferences from a user's free-text request.
        Respond with ONLY a JSON object, no other text, matching exactly this shape:
        {"maxPrice": number|null, "partySize": number, "preferAisle": boolean, "areaPreference": "front"|"middle"|"back"|"any"}
        If a field isn't mentioned, use sensible defaults (partySize=1, preferAisle=false, areaPreference="any", maxPrice=null).
        """;

    private readonly ILlmClient _llmClient;
    private readonly ILogger<SeatCriteriaParser> _logger;

    public SeatCriteriaParser(ILlmClient llmClient, ILogger<SeatCriteriaParser> logger)
    {
        _llmClient = llmClient;
        _logger = logger;
    }

    public async Task<(SeatCriteria Criteria, bool UsedFallback)> ParseAsync(string preferenceText, CancellationToken ct)
    {
        var raw = await _llmClient.CompleteAsync(SystemPrompt, preferenceText, ct);
        if (string.IsNullOrWhiteSpace(raw))
        {
            _logger.LogInformation("LLM returned no usable output; falling back to default seat criteria");
            return (SeatCriteria.Default(), true);
        }

        try
        {
            // Models occasionally wrap JSON in prose or code fences despite instructions —
            // extract the outermost {...} defensively rather than trusting raw output.
            var jsonStart = raw.IndexOf('{');
            var jsonEnd = raw.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd <= jsonStart)
                throw new JsonException("No JSON object found in LLM output");

            var json = raw.Substring(jsonStart, jsonEnd - jsonStart + 1);
            var criteria = JsonSerializer.Deserialize<SeatCriteria>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (criteria is null)
                throw new JsonException("Deserialized to null");

            return (criteria.Sanitize(), false);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM output as SeatCriteria: {RawOutput}", raw);
            return (SeatCriteria.Default(), true);
        }
    }
}
