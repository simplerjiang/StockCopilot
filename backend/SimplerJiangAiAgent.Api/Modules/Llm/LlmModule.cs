using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Text;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using SimplerJiangAiAgent.Api.Infrastructure.Logging;
using SimplerJiangAiAgent.Api.Infrastructure.Security;
using SimplerJiangAiAgent.Api.Infrastructure.Storage;
using SimplerJiangAiAgent.Api.Modules.Llm.Models;

namespace SimplerJiangAiAgent.Api.Modules.Llm;

public sealed class LlmModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        var timeoutSeconds = configuration.GetValue<int?>("Llm:HttpClientTimeoutSeconds") ?? 180;
        if (timeoutSeconds < 30)
        {
            timeoutSeconds = 30;
        }

        services.AddSingleton<ILlmSettingsStore>(serviceProvider =>
            new JsonFileLlmSettingsStore(
                serviceProvider.GetRequiredService<AppRuntimePaths>(),
                serviceProvider.GetRequiredService<ILogger<JsonFileLlmSettingsStore>>()));
        services.AddSingleton<ILlmService, LlmService>();
        services.AddSingleton<IAdminAuthService, AdminAuthService>();
        services.AddHttpClient<OpenAiProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        });
        services.AddSingleton<ILlmProvider, OpenAiProvider>();

        services.AddSingleton<AntigravityOAuthService>();
        services.AddHttpClient<AntigravityProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        });
        services.AddSingleton<ILlmProvider, AntigravityProvider>();

        services.AddHttpClient<OllamaProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(660);
        });
        services.AddSingleton<ILlmProvider, OllamaProvider>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/llm/onboarding-status", async (ILlmSettingsStore store) =>
        {
            var settings = await store.GetAllAsync();
            var activeProviderKey = await store.GetActiveProviderKeyAsync();
            var hasAnyApiKey = settings.Any(item => item.Enabled && !string.IsNullOrWhiteSpace(item.ApiKey));

            return Results.Ok(new LlmOnboardingStatusResponse(
                hasAnyApiKey,
                !hasAnyApiKey,
                activeProviderKey,
                "admin-llm"));
        })
        .WithName("GetLlmOnboardingStatus")
        .WithOpenApi();

        var adminGroup = app.MapGroup("/api/admin");

        adminGroup.MapPost("/login", (AdminLoginRequest request, IAdminAuthService authService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new { message = "用户名或密码不能为空" });
            }

            if (!authService.ValidateCredentials(request.Username.Trim(), request.Password))
            {
                return Results.Unauthorized();
            }

            var token = authService.IssueToken();
            var expiresAt = authService.GetExpiry(token);
            return Results.Ok(new AdminLoginResponse(token, expiresAt));
        })
        .WithName("AdminLogin")
        .WithOpenApi();

        var secureAdminGroup = app.MapGroup("/api/admin").AddEndpointFilter<AdminAuthFilter>();

        secureAdminGroup.MapGet("/llm/settings", async (ILlmSettingsStore store) =>
        {
            var settings = await store.GetAllAsync();
            var result = settings.Select(ToResponse).ToArray();
            return Results.Ok(result);
        })
        .WithName("GetLlmSettings")
        .WithOpenApi();

        secureAdminGroup.MapGet("/llm/settings/active", async (ILlmSettingsStore store) =>
        {
            var activeProviderKey = await store.GetActiveProviderKeyAsync();
            var providers = await store.GetAllAsync();
            return Results.Ok(new ActiveLlmProviderResponse(
                activeProviderKey,
                providers.Select(item => item.Provider).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray()));
        })
        .WithName("GetActiveLlmProvider")
        .WithOpenApi();

        secureAdminGroup.MapPut("/llm/settings/active", async (ActiveLlmProviderRequest request, ILlmSettingsStore store) =>
        {
            if (string.IsNullOrWhiteSpace(request.ActiveProviderKey))
            {
                return Results.BadRequest(new { message = "ActiveProviderKey 不能为空" });
            }

            var activeProviderKey = await store.SetActiveProviderKeyAsync(request.ActiveProviderKey);
            var providers = await store.GetAllAsync();
            return Results.Ok(new ActiveLlmProviderResponse(
                activeProviderKey,
                providers.Select(item => item.Provider).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray()));
        })
        .WithName("SetActiveLlmProvider")
        .WithOpenApi();

        secureAdminGroup.MapGet("/llm/news-cleansing", async (ILlmSettingsStore store) =>
        {
            var (provider, model, batchSize) = await store.GetNewsCleansingSettingsAsync();
            return Results.Ok(new NewsCleansingSettingsResponse(provider, model, batchSize));
        })
        .WithName("GetNewsCleansingSettings")
        .WithOpenApi();

        secureAdminGroup.MapPut("/llm/news-cleansing", async (NewsCleansingSettingsRequest request, ILlmSettingsStore store) =>
        {
            await store.SetNewsCleansingSettingsAsync(
                request.Provider ?? "active",
                request.Model ?? "",
                request.BatchSize ?? 12);
            var (provider, model, batchSize) = await store.GetNewsCleansingSettingsAsync();
            return Results.Ok(new NewsCleansingSettingsResponse(provider, model, batchSize));
        })
        .WithName("SetNewsCleansingSettings")
        .WithOpenApi();

        secureAdminGroup.MapGet("/llm/settings/{provider}", async (string provider, ILlmSettingsStore store) =>
        {
            var settings = await store.GetProviderAsync(provider);
            if (settings is null)
            {
                return Results.NotFound();
            }

            // Tavily API Key is global: fill from any provider if current is empty
            if (string.IsNullOrWhiteSpace(settings.TavilyApiKey))
            {
                var globalTavily = await store.GetGlobalTavilyKeyAsync();
                if (!string.IsNullOrWhiteSpace(globalTavily))
                {
                    settings.TavilyApiKey = globalTavily;
                }
            }

            return Results.Ok(ToResponse(settings));
        })
        .WithName("GetLlmProviderSettings")
        .WithOpenApi();

        secureAdminGroup.MapPut("/llm/settings/{provider}", async (string provider, LlmSettingsRequest request, ILlmSettingsStore store) =>
        {
            var existing = await store.GetProviderAsync(provider);
            var updated = await store.UpsertAsync(new LlmProviderSettings
            {
                Provider = provider,
                ProviderType = !string.IsNullOrWhiteSpace(request.ProviderType)
                    ? request.ProviderType
                    : (existing?.ProviderType ?? "openai"),
                ApiKey = request.ApiKey ?? string.Empty,
                TavilyApiKey = request.TavilyApiKey ?? string.Empty,
                BaseUrl = request.BaseUrl ?? string.Empty,
                Model = request.Model ?? string.Empty,
                SystemPrompt = request.SystemPrompt ?? string.Empty,
                ForceChinese = request.ForceChinese,
                Organization = request.Organization ?? string.Empty,
                Project = request.Project ?? string.Empty,
                OllamaNumCtx = request.OllamaNumCtx,
                OllamaNumGpu = request.OllamaNumGpu,
                OllamaKeepAlive = request.OllamaKeepAlive ?? string.Empty,
                OllamaNumPredict = request.OllamaNumPredict,
                OllamaTemperature = request.OllamaTemperature,
                OllamaTopK = request.OllamaTopK,
                OllamaTopP = request.OllamaTopP,
                OllamaMinP = request.OllamaMinP,
                OllamaStop = request.OllamaStop ?? Array.Empty<string>(),
                OllamaThink = request.OllamaThink,
                Enabled = request.Enabled
            });

            return Results.Ok(ToResponse(updated));
        })
        .WithName("UpsertLlmProviderSettings")
        .WithOpenApi();

        secureAdminGroup.MapPost("/antigravity/auth-start", async (AntigravityOAuthService oauthService) =>
        {
            var result = await oauthService.StartAuthFlowAsync();
            return Results.Ok(new { authUrl = result.AuthUrl, port = result.Port });
        })
        .WithName("AntigravityAuthStart")
        .WithOpenApi();

        secureAdminGroup.MapGet("/antigravity/auth-status", (AntigravityOAuthService oauthService) =>
        {
            var status = oauthService.GetAuthStatus();
            return Results.Ok(new { status = status.Status, error = status.Error });
        })
        .WithName("AntigravityAuthStatus")
        .WithOpenApi();

        secureAdminGroup.MapPost("/antigravity/auth-complete", async (AntigravityOAuthService oauthService, HttpContext httpContext) =>
        {
            var body = await httpContext.Request.ReadFromJsonAsync<AntigravityAuthCompleteRequest>();
            if (body is null || body.Port <= 0)
            {
                return Results.BadRequest(new { message = "port is required" });
            }

            try
            {
                var result = await oauthService.WaitForAuthCompleteAsync(body.Port);
                return Results.Ok(new
                {
                    refreshToken = result.RefreshToken,
                    email = result.Email,
                    projectId = result.ProjectId
                });
            }
            catch (OperationCanceledException)
            {
                return Results.BadRequest(new { message = "OAuth 超时或已取消" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("AntigravityAuthComplete")
        .WithOpenApi();

        secureAdminGroup.MapGet("/antigravity/models", () =>
        {
            return Results.Ok(AntigravityConstants.AvailableModels);
        })
        .WithName("AntigravityModels")
        .WithOpenApi();

        secureAdminGroup.MapPost("/llm/test/{provider}", async (string provider, LlmChatRequestDto request, ILlmService llmService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Prompt))
            {
                return Results.BadRequest(new { message = "Prompt 不能为空" });
            }

            var result = await llmService.ChatAsync(provider, new LlmChatRequest(request.Prompt, request.Model, request.Temperature, request.UseInternet));
            return Results.Ok(new LlmChatResponseDto(result.Content));
        })
        .WithName("TestLlmProvider")
        .WithOpenApi();

        secureAdminGroup.MapGet("/ollama/status", async (IHttpClientFactory httpClientFactory) =>
        {
            using var httpClient = httpClientFactory.CreateClient();
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var resp = await httpClient.GetAsync("http://localhost:11434/", cts.Token);
                if (resp.IsSuccessStatusCode)
                {
                    var modelsResp = await httpClient.GetAsync("http://localhost:11434/api/tags", cts.Token);
                    object? models = null;
                    if (modelsResp.IsSuccessStatusCode)
                    {
                        var json = await modelsResp.Content.ReadAsStringAsync(cts.Token);
                        try { models = System.Text.Json.JsonSerializer.Deserialize<object>(json); }
                        catch { models = null; }
                    }
                    return Results.Ok(new { status = "running", installed = true, models });
                }
            }
            catch { /* not running, fall through */ }

            var installed = ResolveOllamaExecutablePath() is not null;

            return Results.Ok(new { status = "not_running", installed, models = (object?)null });
        })
        .WithName("GetOllamaStatus")
        .WithOpenApi();

        secureAdminGroup.MapPost("/ollama/start", async (IHttpClientFactory httpClientFactory) =>
        {
            using var httpClient = httpClientFactory.CreateClient();
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var resp = await httpClient.GetAsync("http://localhost:11434/", cts.Token);
                if (resp.IsSuccessStatusCode)
                    return Results.Ok(new { success = true, message = "Ollama 已在运行" });
            }
            catch { /* not running, proceed to start */ }

            var ollamaExecutablePath = ResolveOllamaExecutablePath();
            if (string.IsNullOrWhiteSpace(ollamaExecutablePath))
            {
                return Results.Ok(new { success = false, message = "未找到 Ollama 可执行文件，请先安装 Ollama（https://ollama.com）" });
            }

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ollamaExecutablePath,
                    Arguments = "serve",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(ollamaExecutablePath) ?? string.Empty,
                };
                var process = System.Diagnostics.Process.Start(psi);
                if (process == null)
                    return Results.Ok(new { success = false, message = "无法启动 Ollama 进程，请确认 Ollama 已安装" });

                for (int i = 0; i < 30; i++)
                {
                    await Task.Delay(500);
                    try
                    {
                        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        var check = await httpClient.GetAsync("http://localhost:11434/", cts2.Token);
                        if (check.IsSuccessStatusCode)
                            return Results.Ok(new { success = true, message = "Ollama 已启动" });
                    }
                    catch { /* still starting */ }
                }
                return Results.Ok(new { success = false, message = "Ollama 进程已启动但服务未就绪，请稍后重试" });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return Results.Ok(new { success = false, message = "未找到 ollama 命令，请先安装 Ollama（https://ollama.com）" });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { success = false, message = $"启动失败：{ex.Message}" });
            }
        })
        .WithName("StartOllama")
        .WithOpenApi();

        secureAdminGroup.MapPost("/ollama/stop", () =>
        {
            try
            {
                var processes = System.Diagnostics.Process.GetProcessesByName("ollama");
                if (processes.Length == 0)
                    return Results.Ok(new { success = true, message = "Ollama 未在运行" });

                foreach (var p in processes)
                {
                    try { p.Kill(entireProcessTree: true); p.WaitForExit(5000); }
                    catch { /* ignore individual process kill errors */ }
                    finally { p.Dispose(); }
                }
                return Results.Ok(new { success = true, message = "Ollama 已停止" });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { success = false, message = $"停止失败：{ex.Message}" });
            }
        })
        .WithName("StopOllama")
        .WithOpenApi();

        secureAdminGroup.MapPost("/ollama/pull", async (HttpContext httpContext, IHttpClientFactory httpClientFactory) =>
        {
            var body = await System.Text.Json.JsonSerializer.DeserializeAsync<Dictionary<string, string>>(httpContext.Request.Body);
            var model = body?.GetValueOrDefault("model")?.Trim();
            if (string.IsNullOrWhiteSpace(model))
                return Results.BadRequest(new { success = false, message = "请指定模型名称" });

            using var httpClient = httpClientFactory.CreateClient();

            // Check if Ollama is running
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await httpClient.GetAsync("http://localhost:11434/", cts.Token);
            }
            catch
            {
                return Results.Ok(new { success = false, message = "Ollama 未运行，请先启动" });
            }

            // Call Ollama pull API
            try
            {
                var pullRequest = new { name = model, stream = false };
                var json = System.Text.Json.JsonSerializer.Serialize(pullRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var cts2 = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                var resp = await httpClient.PostAsync("http://localhost:11434/api/pull", content, cts2.Token);
                var result = await resp.Content.ReadAsStringAsync(cts2.Token);

                if (resp.IsSuccessStatusCode)
                    return Results.Ok(new { success = true, message = $"模型 {model} 拉取完成" });
                else
                    return Results.Ok(new { success = false, message = $"拉取失败：{result}" });
            }
            catch (TaskCanceledException)
            {
                return Results.Ok(new { success = false, message = "拉取超时（>10分钟），请手动执行 ollama pull " + model });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { success = false, message = $"拉取异常：{ex.Message}" });
            }
        })
        .WithName("PullOllamaModel")
        .WithOpenApi();

        secureAdminGroup.MapGet("/source-governance/overview", async (ISourceGovernanceReadService readService) =>
        {
            var result = await readService.GetOverviewAsync();
            return Results.Ok(result);
        })
        .WithName("GetSourceGovernanceOverview")
        .WithOpenApi();

        secureAdminGroup.MapGet("/source-governance/sources", async (
            string? status,
            string? tier,
            int? page,
            int? pageSize,
            ISourceGovernanceReadService readService) =>
        {
            var normalizedPage = NormalizePage(page);
            var normalizedPageSize = NormalizePageSize(pageSize);
            var result = await readService.GetSourcesAsync(status, tier, normalizedPage, normalizedPageSize);
            return Results.Ok(result);
        })
        .WithName("GetSourceGovernanceSources")
        .WithOpenApi();

        secureAdminGroup.MapGet("/source-governance/candidates", async (
            string? status,
            int? page,
            int? pageSize,
            ISourceGovernanceReadService readService) =>
        {
            var normalizedPage = NormalizePage(page);
            var normalizedPageSize = NormalizePageSize(pageSize);
            var result = await readService.GetCandidatesAsync(status, normalizedPage, normalizedPageSize);
            return Results.Ok(result);
        })
        .WithName("GetSourceGovernanceCandidates")
        .WithOpenApi();

        secureAdminGroup.MapGet("/source-governance/changes", async (
            string? status,
            string? domain,
            int? page,
            int? pageSize,
            ISourceGovernanceReadService readService) =>
        {
            var normalizedPage = NormalizePage(page);
            var normalizedPageSize = NormalizePageSize(pageSize);
            var result = await readService.GetChangesAsync(status, domain, normalizedPage, normalizedPageSize);
            return Results.Ok(result);
        })
        .WithName("GetSourceGovernanceChanges")
        .WithOpenApi();

        secureAdminGroup.MapGet("/source-governance/changes/{id:long}", async (long id, ISourceGovernanceReadService readService) =>
        {
            var detail = await readService.GetChangeDetailAsync(id);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        })
        .WithName("GetSourceGovernanceChangeDetail")
        .WithOpenApi();

        secureAdminGroup.MapGet("/source-governance/errors", async (int? take, ISourceGovernanceReadService readService) =>
        {
            var normalizedTake = Math.Clamp(take ?? 30, 1, 100);
            var result = await readService.GetErrorSnapshotsAsync(normalizedTake);
            return Results.Ok(result);
        })
        .WithName("GetSourceGovernanceErrors")
        .WithOpenApi();

        secureAdminGroup.MapGet("/source-governance/trace/{traceId}", async (string traceId, int? take, ISourceGovernanceReadService readService) =>
        {
            var normalizedTake = Math.Clamp(take ?? 50, 1, 200);
            var result = await readService.SearchTraceAsync(traceId, normalizedTake);
            return Results.Ok(result);
        })
        .WithName("SearchSourceGovernanceTrace")
        .WithOpenApi();

        secureAdminGroup.MapGet("/source-governance/llm-logs", async (int? take, string? keyword, ISourceGovernanceReadService readService) =>
        {
            var normalizedTake = Math.Clamp(take ?? 200, 1, 1000);
            var result = await readService.GetLlmConversationLogsAsync(normalizedTake, keyword);
            return Results.Ok(new
            {
                total = result.Count,
                items = result
            });
        })
        .WithName("GetSourceGovernanceLlmLogs")
        .WithOpenApi();

        app.MapPost("/api/llm/chat/{provider}", async (string provider, LlmChatRequestDto request, ILlmService llmService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Prompt))
            {
                return Results.BadRequest(new { message = "Prompt 不能为空" });
            }

            try
            {
                var result = await llmService.ChatAsync(provider, new LlmChatRequest(request.Prompt, request.Model, request.Temperature, request.UseInternet));
                return Results.Ok(new LlmChatResponseDto(result.Content));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("ChatLlmProvider")
        .WithOpenApi();

        app.MapPost("/api/llm/chat/stream/{provider}", async (string provider, LlmChatRequestDto request, ILlmSettingsStore store, OpenAiProvider openAiProvider, AntigravityProvider antigravityProvider, IFileLogWriter fileLogWriter, HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(request.Prompt))
            {
                return Results.BadRequest(new { message = "Prompt 不能为空" });
            }

            var traceId = Guid.NewGuid().ToString("N");

            var settings = await store.GetProviderAsync(provider) ?? new LlmProviderSettings { Provider = provider, Enabled = true };
            if (!settings.Enabled)
            {
                return Results.BadRequest(new { message = $"Provider {provider} 未启用" });
            }

            fileLogWriter.Write("LLM-AUDIT", $"traceId={traceId} stage=request-stream provider={provider} model={request.Model ?? settings.Model ?? string.Empty} useInternet={request.UseInternet} prompt={EscapeForAudit(request.Prompt)}");

            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";
            context.Response.ContentType = "text/event-stream";

            try
            {
                var providerType = (settings.ProviderType ?? string.Empty).Trim().ToLowerInvariant();
                var chatRequest = new LlmChatRequest(request.Prompt, request.Model, request.Temperature, request.UseInternet, traceId);
                var streamSource = providerType == "antigravity"
                    ? antigravityProvider.StreamChatAsync(settings, chatRequest, context.RequestAborted)
                    : openAiProvider.StreamChatAsync(settings, chatRequest, context.RequestAborted);

                var fullContent = new StringBuilder();
                await foreach (var chunk in streamSource)
                {
                    fullContent.Append(chunk);
                    await context.Response.WriteAsync($"data: {chunk}\n\n", context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                }
                await context.Response.WriteAsync("data: [DONE]\n\n", context.RequestAborted);
                fileLogWriter.Write("LLM-AUDIT", $"traceId={traceId} stage=response-stream provider={provider} content={EscapeForAudit(fullContent.ToString())}");
            }
            catch (Exception ex)
            {
                fileLogWriter.Write("LLM-AUDIT", $"traceId={traceId} stage=error-stream provider={provider} type={ex.GetType().Name} message={EscapeForAudit(ex.Message)}");
                await context.Response.WriteAsync($"data: {ex.Message}\n\n", context.RequestAborted);
            }

            return Results.Empty;
        })
        .WithName("ChatLlmProviderStream")
        .WithOpenApi();
    }

    private static LlmSettingsResponse ToResponse(LlmProviderSettings settings)
    {
        var masked = MaskKey(settings.ApiKey);
        var tavilyMasked = MaskKey(settings.TavilyApiKey);
        var isOllamaProvider = OllamaRuntimeDefaults.IsOllamaProvider(settings);
        return new LlmSettingsResponse(
            settings.Provider,
            settings.ProviderType,
            settings.BaseUrl,
            settings.Model,
            settings.SystemPrompt,
            settings.ForceChinese,
            settings.Organization,
            settings.Project,
            settings.Enabled,
            !string.IsNullOrWhiteSpace(settings.ApiKey),
            masked,
            !string.IsNullOrWhiteSpace(settings.TavilyApiKey),
            tavilyMasked,
                isOllamaProvider ? OllamaRuntimeDefaults.ResolveNumCtx(settings.OllamaNumCtx) : settings.OllamaNumCtx,
                isOllamaProvider ? OllamaRuntimeDefaults.ResolveNumGpu(settings.OllamaNumGpu) : settings.OllamaNumGpu,
                isOllamaProvider ? OllamaRuntimeDefaults.ResolveKeepAlive(settings.OllamaKeepAlive) : settings.OllamaKeepAlive,
                isOllamaProvider ? OllamaRuntimeDefaults.ResolveNumPredict(settings.OllamaNumPredict) : settings.OllamaNumPredict,
                isOllamaProvider ? OllamaRuntimeDefaults.ResolveTemperature(settings.OllamaTemperature) : settings.OllamaTemperature,
                isOllamaProvider ? OllamaRuntimeDefaults.ResolveTopK(settings.OllamaTopK) : settings.OllamaTopK,
                isOllamaProvider ? OllamaRuntimeDefaults.ResolveTopP(settings.OllamaTopP) : settings.OllamaTopP,
                isOllamaProvider ? OllamaRuntimeDefaults.ResolveMinP(settings.OllamaMinP) : settings.OllamaMinP,
                OllamaRuntimeDefaults.ResolveStop(settings.OllamaStop),
                isOllamaProvider ? OllamaRuntimeDefaults.ResolveThink(settings.OllamaThink) : (settings.OllamaThink ?? false),
            settings.UpdatedAt);
    }

    private static string MaskKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var trimmed = key.Trim();
        if (trimmed.Length <= 8)
        {
            return "****";
        }

        return $"{trimmed[..4]}****{trimmed[^4..]}";
    }

    private static string EscapeForAudit(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        const int maxLength = 4000;
        var normalized = value
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + "...(truncated)";
    }

    private static string? ResolveOllamaExecutablePath()
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var seen = new HashSet<string>(comparer);

        foreach (var candidate in EnumerateOllamaExecutableCandidates())
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var normalized = candidate.Trim();
            if (!seen.Add(normalized))
            {
                continue;
            }

            if (File.Exists(normalized))
            {
                return normalized;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateOllamaExecutableCandidates()
    {
        foreach (var candidate in EnumerateOllamaPathCandidates(Environment.GetEnvironmentVariable("PATH")))
        {
            yield return candidate;
        }

        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        foreach (var candidate in EnumerateOllamaPathCandidates(Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User)))
        {
            yield return candidate;
        }

        foreach (var candidate in EnumerateOllamaPathCandidates(Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine)))
        {
            yield return candidate;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "Programs", "Ollama", "ollama.exe");
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "Ollama", "ollama.exe");
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "Ollama", "ollama.exe");
        }
    }

    private static IEnumerable<string> EnumerateOllamaPathCandidates(string? pathValue)
    {
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            yield break;
        }

        var executableName = OperatingSystem.IsWindows() ? "ollama.exe" : "ollama";

        foreach (var rawSegment in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var expandedSegment = Environment.ExpandEnvironmentVariables(rawSegment.Trim().Trim('"'));
            if (string.IsNullOrWhiteSpace(expandedSegment))
            {
                continue;
            }

            string candidate;
            try
            {
                candidate = Path.Combine(expandedSegment, executableName);
            }
            catch
            {
                continue;
            }

            yield return candidate;
        }
    }

    private static int NormalizePage(int? page)
    {
        var normalized = page.GetValueOrDefault(1);
        return normalized <= 0 ? 1 : normalized;
    }

    private static int NormalizePageSize(int? pageSize)
    {
        var size = pageSize.GetValueOrDefault(20);
        return Math.Clamp(size, 1, 100);
    }
}

internal sealed record AntigravityAuthCompleteRequest(int Port);
