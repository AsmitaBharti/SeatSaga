using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inventory.Api.Llm;

public class AnthropicLlmClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly ILogger<AnthropicLlmClient> _logger;
    private readonly string _model;

    public AnthropicLlmClient(HttpClient http, ILogger<AnthropicLlmClient> logger, IConfiguration config)
    {
        _http = http;
        _logger = logger;
        _model = config["Llm:Model"] ?? "claude-sonnet-5";
    }

    public async Task<string?> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct)
    {
        try
        {
            var request = new AnthropicRequest
            {
                Model = _model,
                MaxTokens = 512,
                System = systemPrompt,
                Messages = new[] { new AnthropicMessage { Role = "user", Content = userMessage } }
            };

            var response = await _http.PostAsJsonAsync("v1/messages", request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("LLM call failed with {StatusCode}", response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<AnthropicResponse>(cancellationToken: ct);
            var text = body?.Content?.FirstOrDefault(c => c.Type == "text")?.Text;
            return text;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Same philosophy as PaymentClient: treat "LLM unreachable" as a normal,
            // handled outcome — the caller (SeatCriteriaParser) falls back to defaults
            // rather than failing the whole request.
            _logger.LogError(ex, "LLM Service unreachable");
            return null;
        }
    }

    private class AnthropicRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = default!;
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
        [JsonPropertyName("system")] public string System { get; set; } = default!;
        [JsonPropertyName("messages")] public AnthropicMessage[] Messages { get; set; } = default!;
    }

    private class AnthropicMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = default!;
        [JsonPropertyName("content")] public string Content { get; set; } = default!;
    }

    private class AnthropicResponse
    {
        [JsonPropertyName("content")] public AnthropicContentBlock[]? Content { get; set; }
    }

    private class AnthropicContentBlock
    {
        [JsonPropertyName("type")] public string Type { get; set; } = default!;
        [JsonPropertyName("text")] public string? Text { get; set; }
    }
}
