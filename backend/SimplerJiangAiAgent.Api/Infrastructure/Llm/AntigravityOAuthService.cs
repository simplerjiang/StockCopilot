using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SimplerJiangAiAgent.Api.Infrastructure.Logging;

namespace SimplerJiangAiAgent.Api.Infrastructure.Llm;

public sealed class AntigravityOAuthService
{
    private readonly HttpClient _httpClient;
    private readonly IFileLogWriter _logWriter;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    // 缓存的 token 和 project
    private string? _cachedAccessToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;
    private string? _cachedProjectId;
    private string _antigravityVersion = AntigravityConstants.FallbackVersion;

    // OAuth 流程状态
    private TaskCompletionSource<(string code, string state)>? _callbackTcs;
    private HttpListener? _activeListener;

    public AntigravityOAuthService(HttpClient httpClient, IFileLogWriter logWriter, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logWriter = logWriter;
        _clientId = configuration["Antigravity:ClientId"] ?? AntigravityConstants.DefaultClientId;
        _clientSecret = configuration["Antigravity:ClientSecret"] ?? AntigravityConstants.DefaultClientSecret;
    }

    // =========== 公开属性 ===========
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_clientId) && !string.IsNullOrWhiteSpace(_clientSecret);
    public string? CachedProjectId => _cachedProjectId;
    public string AntigravityVersion => _antigravityVersion;

    // =========== PKCE ===========

    private static (string verifier, string challenge) GeneratePkce()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var verifier = Base64UrlEncode(bytes);
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var challenge = Base64UrlEncode(hash);
        return (verifier, challenge);
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    // =========== 启动 OAuth 流程 ===========

    /// <summary>
    /// 启动 OAuth 认证流程。返回授权 URL 和回调端口。
    /// 调用方需要在浏览器中打开 authUrl，用户登录后会回调到 localhost:{port}。
    /// </summary>
    public Task<AntigravityAuthStartResult> StartAuthFlowAsync(CancellationToken ct = default)
    {
        // 清理上一次的 OAuth 流程，防止重复 auth-start 导致 listener 泄漏
        StopListener();
        _callbackTcs = null;

        // 找一个空闲端口
        var port = FindFreePort();
        var redirectUri = $"http://127.0.0.1:{port}/antigravity-callback";

        // 生成 PKCE
        var (verifier, challenge) = GeneratePkce();

        // 构造 state（含 verifier，用于回调时取回）
        var stateObj = new { verifier, projectId = _cachedProjectId ?? "" };
        var stateJson = JsonSerializer.Serialize(stateObj);
        var stateBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(stateJson));

        // 构造授权 URL
        var scopes = Uri.EscapeDataString(string.Join(" ", AntigravityConstants.Scopes));
        var authUrl = $"{AntigravityConstants.AuthorizationUrl}" +
            $"?client_id={Uri.EscapeDataString(_clientId)}" +
            $"&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&scope={scopes}" +
            $"&code_challenge={challenge}" +
            $"&code_challenge_method=S256" +
            $"&state={stateBase64}" +
            $"&access_type=offline" +
            $"&prompt=consent";

        // 启动本地回调监听
        _callbackTcs = new TaskCompletionSource<(string code, string state)>();
        _ = Task.Run(() => ListenForCallbackAsync(port, ct), ct);

        Log($"OAuth flow started, port={port}");
        return Task.FromResult(new AntigravityAuthStartResult(authUrl, port));
    }

    /// <summary>
    /// 等待 OAuth 回调完成，然后交换 token。
    /// 超时 5 分钟。返回 refresh_token、access_token、email、projectId。
    /// </summary>
    public async Task<AntigravityAuthResult> WaitForAuthCompleteAsync(int port, CancellationToken ct = default)
    {
        if (_callbackTcs is null)
            throw new InvalidOperationException("OAuth flow not started");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(5));

        try
        {
            var (code, stateBase64) = await _callbackTcs.Task.WaitAsync(cts.Token);

            // 从 state 中解析 verifier
            var stateJson = Encoding.UTF8.GetString(Base64UrlDecode(stateBase64));
            using var stateDoc = JsonDocument.Parse(stateJson);
            var verifier = stateDoc.RootElement.GetProperty("verifier").GetString()!;

            var redirectUri = $"http://127.0.0.1:{port}/antigravity-callback";

            // 交换 token
            var tokenResult = await ExchangeCodeForTokenAsync(code, verifier, redirectUri, ct);

            // 缓存 access_token
            _cachedAccessToken = tokenResult.accessToken;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(tokenResult.expiresIn - 60);

            // 获取用户邮箱（可选）
            string? email = null;
            try
            {
                email = await GetUserEmailAsync(tokenResult.accessToken, ct);
            }
            catch (Exception ex)
            {
                Log($"Failed to get user email: {ex.Message}");
            }

            // 获取 ProjectId（对中国账号直接用默认值）
            var projectId = AntigravityConstants.DefaultProjectId;
            _cachedProjectId = projectId;

            Log($"OAuth complete, email={email ?? "unknown"}, projectId={projectId}");
            return new AntigravityAuthResult(
                tokenResult.refreshToken,
                tokenResult.accessToken,
                email,
                projectId);
        }
        finally
        {
            StopListener();
        }
    }

    /// <summary>
    /// 获取当前 OAuth 流程状态
    /// </summary>
    public AntigravityAuthStatus GetAuthStatus()
    {
        if (_callbackTcs is null)
            return new AntigravityAuthStatus("idle", null);
        if (_callbackTcs.Task.IsFaulted)
            return new AntigravityAuthStatus("error", SanitizeAuthError(_callbackTcs.Task.Exception));
        if (_callbackTcs.Task.IsCompleted)
            return new AntigravityAuthStatus("completed", null);
        return new AntigravityAuthStatus("waiting", null);
    }

    private static string? SanitizeAuthError(AggregateException? exception)
    {
        var message = exception?.Flatten().InnerExceptions.FirstOrDefault()?.Message ?? exception?.Message;
        return ErrorSanitizer.SanitizeErrorMessage(message);
    }

    // =========== Token 管理 ===========

    /// <summary>
    /// 确保有有效的 access_token。如果过期则使用 refresh_token 刷新。
    /// </summary>
    public async Task<string> EnsureAccessTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(_cachedAccessToken) && _tokenExpiry > DateTimeOffset.UtcNow)
        {
            return _cachedAccessToken;
        }

        await _tokenLock.WaitAsync(ct);
        try
        {
            // 双重检查
            if (!string.IsNullOrWhiteSpace(_cachedAccessToken) && _tokenExpiry > DateTimeOffset.UtcNow)
            {
                return _cachedAccessToken;
            }

            var result = await RefreshAccessTokenAsync(refreshToken, ct);
            _cachedAccessToken = result.accessToken;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(result.expiresIn - 60);

            // 如果返回了新的 refresh_token，记录一下（调用方需要持久化）
            if (!string.IsNullOrWhiteSpace(result.newRefreshToken))
            {
                Log("Received new refresh_token during refresh");
            }

            return _cachedAccessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>
    /// 强制清除缓存的 token（用于 401 重试场景）
    /// </summary>
    public void InvalidateAccessToken()
    {
        _cachedAccessToken = null;
        _tokenExpiry = DateTimeOffset.MinValue;
    }

    // =========== 版本号 ===========

    /// <summary>
    /// 尝试获取最新 Antigravity 版本号
    /// </summary>
    public async Task UpdateVersionAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetStringAsync(AntigravityConstants.VersionUrl, ct);
            var version = response.Trim();
            if (System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+$"))
            {
                _antigravityVersion = version;
                Log($"Updated version to {version}");
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to update version: {ex.Message}");
        }
    }

    // =========== 私有方法 ===========

    private static int FindFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task ListenForCallbackAsync(int port, CancellationToken ct)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _activeListener = listener;

        try
        {
            listener.Start();
            Log($"Callback listener started on port {port}");

            while (!ct.IsCancellationRequested)
            {
                var contextTask = listener.GetContextAsync();
                var completed = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, ct));
                if (completed != contextTask) break;

                var context = await contextTask;
                var path = context.Request.Url?.AbsolutePath ?? "";

                if (path.Equals("/antigravity-callback", StringComparison.OrdinalIgnoreCase))
                {
                    var code = context.Request.QueryString["code"];
                    var state = context.Request.QueryString["state"];
                    var error = context.Request.QueryString["error"];

                    // 返回成功页面
                    var html = string.IsNullOrWhiteSpace(error)
                        ? "<html><body><h2>登录成功！</h2><p>请返回应用程序。此页面可以关闭。</p></body></html>"
                        : $"<html><body><h2>登录失败</h2><p>错误: {WebUtility.HtmlEncode(error)}</p></body></html>";
                    var buffer = Encoding.UTF8.GetBytes(html);
                    context.Response.ContentType = "text/html; charset=utf-8";
                    context.Response.ContentLength64 = buffer.Length;
                    await context.Response.OutputStream.WriteAsync(buffer, ct);
                    context.Response.Close();

                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        _callbackTcs?.TrySetException(new InvalidOperationException($"OAuth error: {error}"));
                    }
                    else if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(state))
                    {
                        _callbackTcs?.TrySetResult((code, state));
                    }
                    else
                    {
                        _callbackTcs?.TrySetException(new InvalidOperationException("Missing code or state in callback"));
                    }
                    break;
                }
                else
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                }
            }
        }
        catch (OperationCanceledException)
        {
            _callbackTcs?.TrySetCanceled(ct);
        }
        catch (Exception ex)
        {
            Log($"Callback listener error: {ex.Message}");
            _callbackTcs?.TrySetException(ex);
        }
        finally
        {
            StopListener();
        }
    }

    private void StopListener()
    {
        try { _activeListener?.Stop(); } catch { }
        try { _activeListener?.Close(); } catch { }
        _activeListener = null;
    }

    private async Task<(string accessToken, string refreshToken, int expiresIn)> ExchangeCodeForTokenAsync(
        string code, string verifier, string redirectUri, CancellationToken ct)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = verifier
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, AntigravityConstants.TokenUrl);
        request.Content = content;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            Log($"Token exchange failed: {response.StatusCode} {responseText}");
            throw new InvalidOperationException($"Token exchange failed: {response.StatusCode} {responseText}");
        }

        using var doc = JsonDocument.Parse(responseText);
        var root = doc.RootElement;
        var accessToken = root.GetProperty("access_token").GetString()!;
        var refreshToken = root.GetProperty("refresh_token").GetString()!;
        var expiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3599;

        return (accessToken, refreshToken, expiresIn);
    }

    private async Task<(string accessToken, int expiresIn, string? newRefreshToken)> RefreshAccessTokenAsync(
        string refreshToken, CancellationToken ct)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, AntigravityConstants.TokenUrl);
        request.Content = content;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            Log($"Token refresh failed: {response.StatusCode} {responseText}");

            // 检查是否 refresh_token 已被撤销
            if (responseText.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase))
            {
                InvalidateAccessToken();
                throw new InvalidOperationException("Antigravity refresh_token 已失效，请重新登录 Google 账号");
            }

            throw new InvalidOperationException($"Token refresh failed: {response.StatusCode} {responseText}");
        }

        using var doc = JsonDocument.Parse(responseText);
        var root = doc.RootElement;
        var accessToken = root.GetProperty("access_token").GetString()!;
        var expiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3599;
        string? newRefreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;

        return (accessToken, expiresIn, newRefreshToken);
    }

    private async Task<string?> GetUserEmailAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AntigravityConstants.UserInfoUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;

        var responseText = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseText);
        return doc.RootElement.TryGetProperty("email", out var email) ? email.GetString() : null;
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }

    private void Log(string message)
    {
        _logWriter.Write("ANTIGRAVITY-AUTH", message);
    }
}

// =========== 返回类型 ===========

public sealed record AntigravityAuthStartResult(string AuthUrl, int Port);

public sealed record AntigravityAuthResult(
    string RefreshToken,
    string AccessToken,
    string? Email,
    string ProjectId);

public sealed record AntigravityAuthStatus(string Status, string? Error);
