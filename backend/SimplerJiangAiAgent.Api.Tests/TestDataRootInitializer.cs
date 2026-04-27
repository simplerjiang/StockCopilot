using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SimplerJiangAiAgent.Api.Data;
using SimplerJiangAiAgent.Api.Infrastructure.Storage;

namespace SimplerJiangAiAgent.Api.Tests;

internal static class ApiTestDatabaseIsolation
{
    public static void UseIsolatedSqlite(IServiceCollection services, string dataRoot)
    {
        Directory.CreateDirectory(Path.Combine(dataRoot, "data"));

        foreach (var descriptor in services
            .Where(descriptor => descriptor.ServiceType == typeof(AppDbContext) ||
                                 descriptor.ServiceType == typeof(DbContextOptions<AppDbContext>))
            .ToList())
        {
            services.Remove(descriptor);
        }

        var databasePath = Path.Combine(dataRoot, "data", "SimplerJiangAiAgent.db");
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath};Cache=Shared")
                .AddInterceptors(new SqliteBusyTimeoutInterceptor(15000))
                .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));
    }
}

internal static class TestDataRootInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var root = Path.Combine(Path.GetTempPath(), "sjai-api-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var appData = Path.Combine(root, "App_Data");
        Directory.CreateDirectory(appData);

        var writableSettings = Path.Combine(appData, "llm-settings.json");
        var bundledSettings = Path.Combine(AppContext.BaseDirectory, "App_Data", "llm-settings.json");
        if (File.Exists(bundledSettings))
        {
            File.Copy(bundledSettings, writableSettings, overwrite: true);
        }
        else
        {
            File.WriteAllText(writableSettings, "{\"activeProviderKey\":\"default\",\"providers\":{}}");
        }

        Environment.SetEnvironmentVariable("SJAI_DATA_ROOT", root);
    }
}