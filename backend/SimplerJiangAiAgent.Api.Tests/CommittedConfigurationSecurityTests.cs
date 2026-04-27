using System.Text.Json;
using System.Text.RegularExpressions;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class CommittedConfigurationSecurityTests
{
    private static readonly Regex SensitiveDatabaseCredentialPattern = new(
        @"(?i)(^|;|\s)(password|pwd|user\s+id|uid)\s*=\s*[^;\s]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void CommittedAppsettingsDoNotContainPlaintextDatabaseCredentials()
    {
        var root = FindRepositoryRoot();
        var appsettingsFiles = Directory.GetFiles(root, "appsettings*.json", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}backend{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !Path.GetFileName(path).Contains("Local", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(appsettingsFiles);

        foreach (var path in appsettingsFiles)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var connectionString in ReadDatabaseConnectionStrings(document.RootElement))
            {
                Assert.DoesNotMatch(SensitiveDatabaseCredentialPattern, connectionString);
            }
        }
    }

    private static IEnumerable<string> ReadDatabaseConnectionStrings(JsonElement root)
    {
        if (root.TryGetProperty("ConnectionStrings", out var connectionStrings) && connectionStrings.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in connectionStrings.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    yield return property.Value.GetString() ?? string.Empty;
                }
            }
        }

        if (root.TryGetProperty("Database", out var database)
            && database.ValueKind == JsonValueKind.Object
            && database.TryGetProperty("ConnectionString", out var databaseConnectionString)
            && databaseConnectionString.ValueKind == JsonValueKind.String)
        {
            yield return databaseConnectionString.GetString() ?? string.Empty;
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, ".gitignore"))
                && Directory.Exists(Path.Combine(directory.FullName, "backend")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}