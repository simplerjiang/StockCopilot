using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.EventLog;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Infrastructure.Config;
using SimplerJiangAiAgent.Api.Infrastructure.Jobs;
using SimplerJiangAiAgent.Api.Infrastructure.Logging;
using SimplerJiangAiAgent.Api.Infrastructure.Security;
using SimplerJiangAiAgent.Api.Modules;

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
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<StockSyncOptions>(builder.Configuration.GetSection(StockSyncOptions.SectionName));
builder.Services.Configure<SourceGovernanceOptions>(builder.Configuration.GetSection(SourceGovernanceOptions.SectionName));
builder.Services.Configure<ConfigCenterOptions>(builder.Configuration.GetSection(ConfigCenterOptions.SectionName));
builder.Services.Configure<PermissionOptions>(builder.Configuration.GetSection(PermissionOptions.SectionName));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));
builder.Services.AddSingleton<IPermissionService, PermissionService>();
builder.Services.AddScoped<IStockSyncService, StockSyncService>();
builder.Services.AddScoped<ISourceGovernanceService, SourceGovernanceService>();
builder.Services.AddScoped<ISourceGovernanceReadService, SourceGovernanceReadService>();
builder.Services.AddSingleton<ICommandRunner, ProcessCommandRunner>();
builder.Services.AddSingleton<IFileLogWriter, FileLogWriter>();

var databaseOptions = builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
var connectionString = string.IsNullOrWhiteSpace(databaseOptions.ConnectionString)
    ? builder.Configuration.GetConnectionString("Default") ?? string.Empty
    : databaseOptions.ConnectionString;

if (databaseOptions.Provider.Equals("MySql", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));
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

builder.Services.AddModules(builder.Configuration);

var app = builder.Build();

// 自动创建数据库（最小骨架，生产环境建议使用迁移）
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.EnsureCreated();
    await StockMarketDataSchemaInitializer.EnsureAsync(dbContext);
    await LocalFactSchemaInitializer.EnsureAsync(dbContext);
    await SourceGovernanceSchemaInitializer.EnsureAsync(dbContext);
    await TradingPlanSchemaInitializer.EnsureAsync(dbContext);
}

// 中间件管道
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseMiddleware<RequestLoggingMiddleware>();

// 静态前端（若已构建）
var distPath = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "..", "frontend", "dist"));
if (Directory.Exists(distPath))
{
    var fileProvider = new PhysicalFileProvider(distPath);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
}

// 基础健康检查
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
    .WithName("Health")
    .WithOpenApi();

app.MapModules();

// 前端路由兜底
if (Directory.Exists(distPath))
{
    app.MapFallbackToFile("index.html", new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(distPath)
    });
}

app.Run();
