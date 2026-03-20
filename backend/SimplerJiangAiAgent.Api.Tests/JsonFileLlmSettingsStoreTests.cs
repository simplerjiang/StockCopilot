using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SimplerJiangAiAgent.Api.Infrastructure.Llm;
using SimplerJiangAiAgent.Api.Infrastructure.Storage;
using Xunit;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class JsonFileLlmSettingsStoreTests
{
    [Fact]
    public async Task GetProviderAsync_ShouldPreferLocalSecretFile()
    {
    using var envScope = ApiKeyEnvironmentScope.Clear();
        var rootPath = CreateTempRoot();
        var appDataPath = Path.Combine(rootPath, "App_Data");
        Directory.CreateDirectory(appDataPath);

        await File.WriteAllTextAsync(
            Path.Combine(appDataPath, "llm-settings.json"),
            """
            {
              "activeProviderKey": "default",
              "providers": {
                "default": {
                  "provider": "default",
                  "providerType": "openai",
                  "apiKey": "tracked-key",
                  "baseUrl": "https://api.bltcy.ai",
                  "model": "gemini-test"
                }
              }
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(appDataPath, "llm-settings.local.json"),
            """
            {
              "providers": {
                "default": {
                  "provider": "default",
                  "providerType": "openai",
                  "apiKey": "local-key"
                }
              }
            }
            """);

        var store = new JsonFileLlmSettingsStore(new FakeWebHostEnvironment(rootPath));

        var settings = await store.GetProviderAsync("default");

        Assert.NotNull(settings);
        Assert.Equal("local-key", settings!.ApiKey);
        Assert.Equal("https://api.bltcy.ai", settings.BaseUrl);
        Assert.Equal("gemini-test", settings.Model);
    }

    [Fact]
    public async Task UpsertAsync_ShouldWriteApiKeyToIgnoredLocalFileOnly()
    {
      using var envScope = ApiKeyEnvironmentScope.Clear();
        var rootPath = CreateTempRoot();
        var store = new JsonFileLlmSettingsStore(new FakeWebHostEnvironment(rootPath));

        var result = await store.UpsertAsync(new LlmProviderSettings
        {
          Provider = "default",
          ProviderType = "openai",
            ApiKey = "local-secret-key",
            BaseUrl = "https://api.bltcy.ai",
            Model = "gemini-test",
            Enabled = true
        });

        var defaultsJson = await File.ReadAllTextAsync(Path.Combine(rootPath, "App_Data", "llm-settings.json"));
        var localJson = await File.ReadAllTextAsync(Path.Combine(rootPath, "App_Data", "llm-settings.local.json"));
        var defaultsDocument = JsonSerializer.Deserialize<LlmSettingsDocument>(defaultsJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.Equal("local-secret-key", result.ApiKey);
        Assert.DoesNotContain("local-secret-key", defaultsJson, StringComparison.Ordinal);
        Assert.Contains("local-secret-key", localJson, StringComparison.Ordinal);
        Assert.NotNull(defaultsDocument);
        Assert.Equal("default", defaultsDocument!.ActiveProviderKey);
        Assert.True(defaultsDocument.Providers.TryGetValue("default", out var defaultsSettings));
        Assert.Equal(string.Empty, defaultsSettings!.ApiKey);
    }

    [Fact]
    public async Task ResolveProviderKeyAsync_ShouldMigrateLegacyOpenAiToDefaultAndPersistActiveProvider()
    {
      using var envScope = ApiKeyEnvironmentScope.Clear();
        var rootPath = CreateTempRoot();
        var appDataPath = Path.Combine(rootPath, "App_Data");
        Directory.CreateDirectory(appDataPath);

        await File.WriteAllTextAsync(
            Path.Combine(appDataPath, "llm-settings.json"),
            """
            {
              "providers": {
                "openai": {
                  "provider": "openai",
                  "baseUrl": "https://api.example.com",
                  "model": "gpt-test"
                },
                "gemini_official": {
                  "provider": "gemini_official",
                  "providerType": "openai",
                  "baseUrl": "https://generativelanguage.googleapis.com/v1beta/openai/",
                  "model": "gemini-test"
                }
              }
            }
            """);

        var store = new JsonFileLlmSettingsStore(new FakeWebHostEnvironment(rootPath));

        var resolvedDefault = await store.ResolveProviderKeyAsync("openai");
        var active = await store.SetActiveProviderKeyAsync("gemini_official");
        var activeRead = await store.GetActiveProviderKeyAsync();

        Assert.Equal("default", resolvedDefault);
        Assert.Equal("gemini_official", active);
        Assert.Equal("gemini_official", activeRead);
    }

      [Fact]
      public void ServiceCollection_ShouldResolveStoreFromRuntimePaths()
      {
        using var envScope = ApiKeyEnvironmentScope.Clear();
        var rootPath = CreateTempRoot();
        var configuration = new ConfigurationBuilder()
          .AddInMemoryCollection(new Dictionary<string, string?>
          {
            ["Database:DataRootPath"] = rootPath
          })
          .Build();
        var environment = new FakeWebHostEnvironment(rootPath);
        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(environment);
        services.AddSingleton<IWebHostEnvironment>(environment);
        services.AddSingleton<AppRuntimePaths>();
        services.AddSingleton<ILlmSettingsStore>(serviceProvider =>
          new JsonFileLlmSettingsStore(serviceProvider.GetRequiredService<AppRuntimePaths>()));

        using var serviceProvider = services.BuildServiceProvider();

        var store = serviceProvider.GetRequiredService<ILlmSettingsStore>();

        Assert.IsType<JsonFileLlmSettingsStore>(store);
      }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "SimplerJiangAiAgent.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class ApiKeyEnvironmentScope : IDisposable
    {
      private static readonly string[] VariableNames =
      [
        "OPENAI_API_KEY",
        "GEMINI_API_KEY",
        "GOOGLE_API_KEY",
        "LLM__DEFAULT__APIKEY",
        "LLM__GEMINI_OFFICIAL__APIKEY",
        "LLM__OPENAI__APIKEY"
      ];

      private readonly Dictionary<string, string?> _originalValues;

      private ApiKeyEnvironmentScope(Dictionary<string, string?> originalValues)
      {
        _originalValues = originalValues;
      }

      public static ApiKeyEnvironmentScope Clear()
      {
        var originalValues = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var variableName in VariableNames)
        {
          originalValues[variableName] = Environment.GetEnvironmentVariable(variableName);
          Environment.SetEnvironmentVariable(variableName, null);
        }

        return new ApiKeyEnvironmentScope(originalValues);
      }

      public void Dispose()
      {
        foreach (var (variableName, originalValue) in _originalValues)
        {
          Environment.SetEnvironmentVariable(variableName, originalValue);
        }
      }
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public FakeWebHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
          ContentRootFileProvider = new NullFileProvider();
          WebRootFileProvider = new NullFileProvider();
        }

        public string ApplicationName { get; set; } = "SimplerJiangAiAgent.Api.Tests";
        public IFileProvider WebRootFileProvider { get; set; }
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
    }
}