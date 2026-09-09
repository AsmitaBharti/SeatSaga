namespace Inventory.Api.Llm;

/// <summary>
/// Minimal abstraction over "send a system+user prompt, get text back." Kept
/// deliberately narrow (not a full SDK wrapper) so SeatCriteriaParser doesn't
/// depend on any one vendor's API shape, and so it can be mocked in tests
/// without hitting a real LLM.
/// </summary>
public interface ILlmClient
{
    Task<string?> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct);
}
