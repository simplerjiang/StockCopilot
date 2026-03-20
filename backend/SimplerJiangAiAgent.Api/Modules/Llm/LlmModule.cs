using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
            new JsonFileLlmSettingsStore(serviceProvider.GetRequiredService<AppRuntimePaths>()));
        services.AddSingleton<ILlmService, LlmService>();
        services.AddSingleton<IAdminAuthService, AdminAuthService>();
        services.AddHttpClient<OpenAiProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        });
        services.AddSingleton<ILlmProvider, OpenAiProvider>();
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

        secureAdminGroup.MapGet("/llm/settings/{provider}", async (string provider, ILlmSettingsStore store) =>
        {
            var settings = await store.GetProviderAsync(provider);
            if (settings is null)
            {
                return Results.NotFound();
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
                ProviderType = existing?.ProviderType ?? "openai",
                ApiKey = request.ApiKey ?? string.Empty,
                BaseUrl = request.BaseUrl ?? string.Empty,
                Model = request.Model ?? string.Empty,
                SystemPrompt = request.SystemPrompt ?? string.Empty,
                ForceChinese = request.ForceChinese,
                Organization = request.Organization ?? string.Empty,
                Project = request.Project ?? string.Empty,
                Enabled = request.Enabled
            });

            return Results.Ok(ToResponse(updated));
        })
        .WithName("UpsertLlmProviderSettings")
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

        app.MapPost("/api/llm/chat/stream/{provider}", async (string provider, LlmChatRequestDto request, ILlmSettingsStore store, OpenAiProvider providerImpl, IFileLogWriter fileLogWriter, HttpContext context) =>
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
                var fullContent = new StringBuilder();
                await foreach (var chunk in providerImpl.StreamChatAsync(settings, new LlmChatRequest(request.Prompt, request.Model, request.Temperature, request.UseInternet, traceId), context.RequestAborted))
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
