using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace SimplerJiangAiAgent.Api.Infrastructure.Storage;

public sealed class AppRuntimePaths
{
    public const string DataRootEnvironmentVariable = "SJAI_DATA_ROOT";
    private const string ApplicationFolderName = "SimplerJiangAiAgent";

    public AppRuntimePaths(IHostEnvironment environment, IConfiguration configuration)
    {
        ContentRootPath = environment.ContentRootPath;
        DataRootPath = ResolveDataRoot(configuration);
        AppDataPath = Path.Combine(DataRootPath, "App_Data");
        var logOverride = Environment.GetEnvironmentVariable("SJAI_LOG_ROOT");
        LogsPath = !string.IsNullOrWhiteSpace(logOverride) ? logOverride : Path.Combine(AppDataPath, "logs");
        DatabaseDirectoryPath = Path.Combine(DataRootPath, "data");
        DatabaseFilePath = Path.Combine(DatabaseDirectoryPath, "SimplerJiangAiAgent.db");
        WritableLlmSettingsFilePath = Path.Combine(AppDataPath, "llm-settings.json");
        WritableLocalLlmSecretsFilePath = Path.Combine(AppDataPath, "llm-settings.local.json");
        BundledLlmSettingsFilePath = Path.Combine(ContentRootPath, "App_Data", "llm-settings.json");
        BundledFrontendDistPath = Path.Combine(ContentRootPath, "frontend", "dist");
        DevelopmentFrontendDistPath = Path.GetFullPath(Path.Combine(ContentRootPath, "..", "..", "frontend", "dist"));
    }

    public string ContentRootPath { get; }
    public string DataRootPath { get; }
    public string AppDataPath { get; }
    public string LogsPath { get; }
    public string DatabaseDirectoryPath { get; }
    public string DatabaseFilePath { get; }
    public string WritableLlmSettingsFilePath { get; }
    public string WritableLocalLlmSecretsFilePath { get; }
    public string BundledLlmSettingsFilePath { get; }
    public string BundledFrontendDistPath { get; }
    public string DevelopmentFrontendDistPath { get; }

    public void EnsureWritableDirectories()
    {
        Directory.CreateDirectory(DataRootPath);
        Directory.CreateDirectory(AppDataPath);
        Directory.CreateDirectory(LogsPath);
        Directory.CreateDirectory(DatabaseDirectoryPath);
    }

    public void EnsureBundledDefaultsCopied()
    {
        if (File.Exists(WritableLlmSettingsFilePath) || !File.Exists(BundledLlmSettingsFilePath))
        {
            return;
        }

        var parent = Path.GetDirectoryName(WritableLlmSettingsFilePath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.Copy(BundledLlmSettingsFilePath, WritableLlmSettingsFilePath, overwrite: false);
    }

    public string GetDefaultSqliteConnectionString()
    {
        return $"Data Source={DatabaseFilePath};Cache=Shared";
    }

    public string? ResolveFrontendDistPath()
    {
        if (Directory.Exists(BundledFrontendDistPath))
        {
            return BundledFrontendDistPath;
        }

        if (Directory.Exists(DevelopmentFrontendDistPath))
        {
            return DevelopmentFrontendDistPath;
        }

        return null;
    }

    private static string ResolveDataRoot(IConfiguration configuration)
    {
        var configured = configuration["Database:DataRootPath"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var environmentOverride = Environment.GetEnvironmentVariable(DataRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentOverride))
        {
            return Path.GetFullPath(environmentOverride);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = AppContext.BaseDirectory;
        }

        return Path.Combine(localAppData, ApplicationFolderName);
    }
}