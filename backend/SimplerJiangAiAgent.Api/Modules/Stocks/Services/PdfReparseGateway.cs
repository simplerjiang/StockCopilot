using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SimplerJiangAiAgent.Api.Infrastructure;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

/// <summary>
/// v0.4.1 §S2：reparse 调用网关。
/// FinancialWorker 是独立进程，Api 通过 HTTP 触发其 ReparseAsync；测试可注入 Stub 替换。
/// </summary>
public interface IPdfReparseGateway
{
    Task<PdfReparseGatewayResult> ReparseAsync(string id, CancellationToken ct = default);
}

public sealed class PdfReparseGatewayResult
{
    public bool DocumentFound { get; set; }
    public bool PhysicalFileMissing { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}

public sealed class HttpPdfReparseGateway : IPdfReparseGateway
{
    private static readonly TimeSpan ReparseTimeout = TimeSpan.FromMinutes(5);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<HttpPdfReparseGateway> _logger;

    public HttpPdfReparseGateway(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<HttpPdfReparseGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<PdfReparseGatewayResult> ReparseAsync(string id, CancellationToken ct = default)
    {
        var baseUrl = _configuration["FinancialWorker:BaseUrl"] ?? "http://localhost:5120";
        var url = $"{baseUrl.TrimEnd('/')}/api/pdf-reparse/{Uri.EscapeDataString(id)}";

        using var client = _httpClientFactory.CreateClient();
        client.Timeout = ReparseTimeout;

        try
        {
            using var resp = await client.PostAsync(url, content: null, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await SafeReadAsync(resp.Content, ct);
                _logger.LogWarning("[Pdf-Reparse] FinancialWorker returned {Code}: {Body}", (int)resp.StatusCode, body);
                return new PdfReparseGatewayResult
                {
                    DocumentFound = true,
                    Success = false,
                    Error = $"FinancialWorker 返回 {(int)resp.StatusCode}: {ErrorSanitizer.SanitizeErrorMessage(body)}"
                };
            }

            var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            return new PdfReparseGatewayResult
            {
                DocumentFound = ReadBool(json, "documentFound", true),
                PhysicalFileMissing = ReadBool(json, "physicalFileMissing", false),
                Success = ReadBool(json, "success", false),
                Error = ErrorSanitizer.SanitizeErrorMessage(ReadString(json, "error")),
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Pdf-Reparse] FinancialWorker 调用失败: {Url}", url);
            return new PdfReparseGatewayResult
            {
                DocumentFound = true,
                Success = false,
                Error = $"FinancialWorker 不可达: {ErrorSanitizer.SanitizeErrorMessage(ex.Message)}"
            };
        }
    }

    private static bool ReadBool(JsonElement el, string name, bool fallback)
    {
        foreach (var prop in el.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return prop.Value.GetBoolean();
        }
        return fallback;
    }

    private static string? ReadString(JsonElement el, string name)
    {
        foreach (var prop in el.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
                return prop.Value.GetString();
        }
        return null;
    }

    private static async Task<string> SafeReadAsync(HttpContent content, CancellationToken ct)
    {
        try { return await content.ReadAsStringAsync(ct); }
        catch { return ""; }
    }
}
