using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace SimplerJiangAiAgent.FinancialWorker.Services.Rag;

public class OllamaEmbedder : IEmbedder
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaEmbedder> _logger;
    private readonly string _model;
    private bool? _available;

    public OllamaEmbedder(HttpClient httpClient, ILogger<OllamaEmbedder> logger, string model = "bge-m3")
    {
        _httpClient = httpClient;
        _logger = logger;
        _model = model;
    }

    public bool IsAvailable
    {
        get
        {
            if (_available == null)
            {
                try
                {
                    var response = _httpClient.GetAsync("/api/tags").GetAwaiter().GetResult();
                    if (!response.IsSuccessStatusCode)
                    {
                        _available = false;
                    }
                    else
                    {
                        var tags = response.Content.ReadFromJsonAsync<OllamaTagsResponse>().GetAwaiter().GetResult();
                        _available = tags?.Models?.Any(m =>
                            m.Name != null && m.Name.StartsWith(_model, StringComparison.OrdinalIgnoreCase)) == true;
                        if (_available == false)
                            _logger.LogWarning("[Embedding] Ollama is reachable but model '{Model}' is not installed", _model);
                    }
                }
                catch
                {
                    _available = false;
                }
            }
            return _available.Value;
        }
    }

    public async Task<float[]?> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            var request = new OllamaEmbeddingRequest
            {
                Model = _model,
                Prompt = text
            };

            var response = await _httpClient.PostAsJsonAsync("/api/embeddings", request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Embedding] Ollama returned {Status} for model {Model}",
                    response.StatusCode, _model);
                _available = false;
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(cancellationToken: ct);
            if (result?.Embedding == null || result.Embedding.Length == 0)
            {
                _logger.LogWarning("[Embedding] Ollama returned empty embedding for model {Model}", _model);
                return null;
            }

            _available = true;
            return result.Embedding;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Embedding] Ollama embedding failed for model {Model}", _model);
            _available = false;
            return null;
        }
    }

    /// <summary>Reset availability check (e.g., after user changes model).</summary>
    public void ResetAvailability() => _available = null;
}

internal class OllamaEmbeddingRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = "";
}

internal class OllamaEmbeddingResponse
{
    [JsonPropertyName("embedding")]
    public float[]? Embedding { get; set; }
}

internal class OllamaTagsResponse
{
    [JsonPropertyName("models")]
    public OllamaModelInfo[]? Models { get; set; }
}

internal class OllamaModelInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
