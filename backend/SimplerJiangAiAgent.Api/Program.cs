using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.EventLog;
using System.Reflection;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Infrastructure.Config;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;
using SimplerJiangAiAgent.Api.Infrastructure.Logging;
using SimplerJiangAiAgent.Api.Infrastructure.Security;
using SimplerJiangAiAgent.Api.Infrastructure.Serialization;
using SimplerJiangAiAgent.Api.Infrastructure.Storage;
using SimplerJiangAiAgent.Api.Modules;
using SimplerJiangAiAgent.Api.Modules.Macro;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services.Recommend;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services.Recommend.WebSearch;
using SimplerJiangAiAgent.Api.Services;

var builder = WebApplication.CreateBuilder(args);

System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
if (OperatingSystem.IsWindows())
{
    builder.Logging.AddFilter<EventLogLoggerProvider>(null, LogLevel.None);
}

// 服务注册
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    // 桌面端嵌入与本地开发使用的宽松策略（生产环境请收敛）
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new ChinaDateTimeJsonConverter());
    options.SerializerOptions.Converters.Add(new ChinaNullableDateTimeJsonConverter());
    options.SerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
});
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<StockSyncOptions>(builder.Configuration.GetSection(StockSyncOptions.SectionName));
builder.Services.Configure<SourceGovernanceOptions>(builder.Configuration.GetSection(SourceGovernanceOptions.SectionName));
builder.Services.Configure<ConfigCenterOptions>(builder.Configuration.GetSection(ConfigCenterOptions.SectionName));
builder.Services.Configure<PermissionOptions>(builder.Configuration.GetSection(PermissionOptions.SectionName));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));
builder.Services.AddSingleton<IPermissionService, PermissionService>();
builder.Services.AddScoped<IStockSyncService, StockSyncService>();
builder.Services.AddScoped<ISourceGovernanceService, SourceGovernanceService>();
builder.Services.AddScoped<ISourceGovernanceReadService>(serviceProvider =>
    new SourceGovernanceReadService(
        serviceProvider.GetRequiredService<AppDbContext>(),
        serviceProvider.GetRequiredService<AppRuntimePaths>()));
builder.Services.AddSingleton<ICommandRunner, ProcessCommandRunner>();
builder.Services.AddSingleton<IBaostockClientFactory, BaostockClientFactory>();
builder.Services.AddSingleton<ITradingCalendarService, TradingCalendarService>();
builder.Services.AddHostedService<TradingCalendarWorker>();

var runtimePaths = new AppRuntimePaths(builder.Environment, builder.Configuration);
runtimePaths.EnsureWritableDirectories();
runtimePaths.EnsureBundledDefaultsCopied();
builder.Services.AddSingleton(runtimePaths);
builder.Services.AddSingleton<IFileLogWriter, FileLogWriter>();

var databaseOptions = builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
var (provider, connectionString, databaseStartupWarning) = ResolveDatabaseStartupConfiguration(databaseOptions, builder.Configuration, runtimePaths, builder.Environment);

if (!string.IsNullOrWhiteSpace(databaseStartupWarning))
{
    Console.WriteLine($"[DatabaseStartup] {databaseStartupWarning}");
}

if (provider.Equals("MySql", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));
}
else if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase) || provider.Equals("SQLite", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlite(connectionString)
               .AddInterceptors(new SqliteBusyTimeoutInterceptor(15000)));
}
else
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlServer(connectionString));
}

builder.Services.AddHostedService<StockSyncWorker>();
builder.Services.AddHostedService<HighFrequencyQuoteService>();
builder.Services.AddHostedService<LocalFactIngestionWorker>();
builder.Services.AddHostedService<SourceGovernanceWorker>();
builder.Services.AddHostedService<BaostockDataWorker>();
builder.Services.AddHostedService<MacroDataWorker>();
builder.Services.AddScoped<IMacroEnvironmentService, MacroEnvironmentService>();
builder.Services.AddScoped<ResearchZombieCleanupService>();
builder.Services.AddHostedService<ResearchZombieCleanupWorker>();
builder.Services.AddHostedService<RecommendZombieCleanupWorker>();

builder.Services.AddScoped<ITradeAccountingService, TradeAccountingService>();
builder.Services.AddScoped<ITradeComplianceService, TradeComplianceService>();
builder.Services.AddScoped<ITradeReviewService, TradeReviewService>();
builder.Services.AddScoped<IPortfolioSnapshotService, PortfolioSnapshotService>();
builder.Services.AddScoped<ITradingBehaviorService, TradingBehaviorService>();

builder.Services.AddModules(builder.Configuration);

var app = builder.Build();

// 自动创建数据库（最小骨架，生产环境建议使用迁移）
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.EnsureCreated();

    // Enable WAL mode for SQLite to improve concurrent read/write performance
    if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase) || provider.Equals("SQLite", StringComparison.OrdinalIgnoreCase))
    {
        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    }

    await StockMarketDataSchemaInitializer.EnsureAsync(dbContext);
    await LocalFactSchemaInitializer.EnsureAsync(dbContext);
    await SourceGovernanceSchemaInitializer.EnsureAsync(dbContext);
    await TradingPlanSchemaInitializer.EnsureAsync(dbContext);
    await MarketSentimentSchemaInitializer.EnsureAsync(dbContext);
    await ResearchSessionSchemaInitializer.EnsureAsync(dbContext);
    await RecommendSessionSchemaInitializer.EnsureAsync(dbContext);
    await TradeExecutionSchemaInitializer.EnsureAsync(dbContext);
    await ForumPostCountSchemaInitializer.EnsureAsync(dbContext);

    // B29/B34: Idempotent data cleanup – remove "示例名称" and extra spaces in short stock names
    await DataCleanupHelper.CleanStockNamesAsync(dbContext);
}

// 中间件管道
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exceptionFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
        if (exceptionFeature?.Error is UnsupportedStockSourceException unsupportedSource)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "unsupported_source", message = $"不支持的数据源: {unsupportedSource.SourceName}" });
            return;
        }

        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new { error = "Internal server error" });
    });
});

app.UseHttpsRedirection();
app.UseCors();
app.UseMiddleware<RequestLoggingMiddleware>();

// 静态前端（若已构建）
var distPath = runtimePaths.ResolveFrontendDistPath();
if (!string.IsNullOrWhiteSpace(distPath) && Directory.Exists(distPath))
{
    var fileProvider = new PhysicalFileProvider(distPath);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = fileProvider,
        OnPrepareResponse = ctx =>
        {
            if (ctx.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                ctx.Context.Response.Headers["Pragma"] = "no-cache";
            }
            else
            {
                ctx.Context.Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
            }
        }
    });
}

// 基础健康检查
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
    .WithName("Health")
    .WithOpenApi();

app.MapGet("/api/health/websearch", (IWebSearchService webSearchService) =>
{
    var status = webSearchService.GetHealthStatus();
    return Results.Ok(status);
})
    .WithName("WebSearchHealth")
    .WithOpenApi();

app.MapGet("/api/app/version", () => Results.Ok(new
{
    version = GetAppVersion(),
    repositoryUrl = "https://github.com/simplerjiang/StockCopilot",
    releaseUrl = "https://github.com/simplerjiang/StockCopilot/releases/latest"
}))
    .WithName("AppVersion")
    .WithOpenApi();

app.MapModules();

// V048-S2 #71: /api/* 未命中应进入标准 404，而不是被 SPA fallback 吞成 index.html
// MapFallback 路由特异性：/api/{**path} 比 {**path} 更具体，会优先匹配
app.MapFallback("/api/{**path}", () => Results.NotFound(new
{
    error = "api_endpoint_not_found",
    message = "请求的 API 路径不存在"
}));

// 前端路由兜底
if (!string.IsNullOrWhiteSpace(distPath) && Directory.Exists(distPath))
{
    app.MapFallbackToFile("index.html", new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(distPath),
        OnPrepareResponse = ctx =>
        {
            ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            ctx.Context.Response.Headers["Pragma"] = "no-cache";
        }
    });
}

app.Run();

static string ResolveDefaultConnectionString(string provider, IConfiguration configuration, AppRuntimePaths runtimePaths)
{
    if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase) || provider.Equals("SQLite", StringComparison.OrdinalIgnoreCase))
    {
        return runtimePaths.GetDefaultSqliteConnectionString();
    }

    return configuration.GetConnectionString("Default") ?? string.Empty;
}

static (string Provider, string ConnectionString, string? Warning) ResolveDatabaseStartupConfiguration(DatabaseOptions databaseOptions, IConfiguration configuration, AppRuntimePaths runtimePaths, IHostEnvironment environment)
{
    var provider = string.IsNullOrWhiteSpace(databaseOptions.Provider)
        ? "Sqlite"
        : databaseOptions.Provider.Trim();
    var connectionString = string.IsNullOrWhiteSpace(databaseOptions.ConnectionString)
        ? ResolveDefaultConnectionString(provider, configuration, runtimePaths)
        : databaseOptions.ConnectionString;

    if (!environment.IsDevelopment())
    {
        return (provider, connectionString, null);
    }

    if (!provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
    {
        return (provider, connectionString, null);
    }

    if (string.Equals(configuration["SJAI_DISABLE_DEV_DB_FALLBACK"], "1", StringComparison.OrdinalIgnoreCase))
    {
        return (provider, connectionString, null);
    }

    if (CanOpenSqlServerConnection(connectionString))
    {
        return (provider, connectionString, null);
    }

    var sqliteConnectionString = runtimePaths.GetDefaultSqliteConnectionString();
    return (
        "Sqlite",
        sqliteConnectionString,
        $"Development detected unreachable SQL Server, fallback to SQLite at '{runtimePaths.DatabaseFilePath}'. Set SJAI_DISABLE_DEV_DB_FALLBACK=1 to disable this behavior.");
}

static bool CanOpenSqlServerConnection(string connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return false;
    }

    try
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            ConnectTimeout = 3
        };

        using var connection = new SqlConnection(builder.ConnectionString);
        connection.Open();
        return true;
    }
    catch
    {
        return false;
    }
}

static string GetAppVersion()
{
    var assembly = typeof(Program).Assembly;
    var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    if (!string.IsNullOrWhiteSpace(informationalVersion))
    {
        var normalized = informationalVersion.Trim();
        var separatorIndex = normalized.IndexOfAny(['-', '+']);
        return separatorIndex >= 0 ? normalized[..separatorIndex] : normalized;
    }

    var version = assembly.GetName().Version;
    return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
}

// Exposed as public partial so integration tests can use WebApplicationFactory<Program>.
public partial class Program { }
