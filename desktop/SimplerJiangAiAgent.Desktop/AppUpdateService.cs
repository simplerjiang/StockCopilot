using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimplerJiangAiAgent.Desktop;

internal sealed record AppReleaseInfo(
    Version Version,
    string VersionLabel,
    string AssetName,
    string DownloadUrl,
    string ReleasePageUrl,
    string ReleaseNotes);

internal static partial class AppUpdateService
{
    private const string RepositoryOwner = "simplerjiang";
    private const string RepositoryName = "StockCopilot";
    public static Version CurrentVersion { get; } = ReadCurrentVersion();

    private static readonly HttpClient UpdateClient = CreateClient();

    public static string CurrentVersionLabel => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    public static string RepositoryUrl => $"https://github.com/{RepositoryOwner}/{RepositoryName}";

    public static async Task<AppReleaseInfo?> GetAvailableReleaseAsync(CancellationToken cancellationToken = default)
    {
        using var response = await UpdateClient.GetAsync(
            $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest",
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubReleaseResponse>(stream, JsonOptions.Default.GitHubReleaseResponse, cancellationToken);
        if (release is null)
        {
            return null;
        }

        var latestVersion = ParseVersion(release.TagName);
        if (latestVersion is null || latestVersion <= CurrentVersion)
        {
            return null;
        }

        var installerAsset = release.Assets.FirstOrDefault(asset =>
            asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            && asset.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase));

        if (installerAsset is null || string.IsNullOrWhiteSpace(installerAsset.BrowserDownloadUrl))
        {
            return null;
        }

        return new AppReleaseInfo(
            latestVersion,
            ToVersionLabel(latestVersion),
            installerAsset.Name,
            installerAsset.BrowserDownloadUrl,
            release.HtmlUrl ?? RepositoryUrl,
            release.Body ?? string.Empty);
    }

    public static async Task<string> DownloadInstallerAsync(AppReleaseInfo release, CancellationToken cancellationToken = default)
    {
        var targetDirectory = Path.Combine(Path.GetTempPath(), "SimplerJiangAiAgent", "updates", release.VersionLabel);
        Directory.CreateDirectory(targetDirectory);

        var filePath = Path.Combine(targetDirectory, release.AssetName);
        using var response = await UpdateClient.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(filePath);
        await source.CopyToAsync(destination, cancellationToken);

        return filePath;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SimplerJiangAiAgent", CurrentVersionLabel));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static Version ReadCurrentVersion()
    {
        var assembly = typeof(AppUpdateService).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var parsed = ParseVersion(informationalVersion);
        if (parsed is not null)
        {
            return parsed;
        }

        return assembly.GetName().Version ?? new Version(0, 0, 0);
    }

    private static Version? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

        var separatorIndex = normalized.IndexOfAny(['-', '+']);
        if (separatorIndex >= 0)
        {
            normalized = normalized[..separatorIndex];
        }

        return Version.TryParse(normalized, out var version)
            ? version
            : null;
    }

    private static string ToVersionLabel(Version version)
    {
        return $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private sealed record GitHubReleaseResponse(
        string TagName,
        string? HtmlUrl,
        string? Body,
        GitHubReleaseAssetResponse[] Assets);

    private sealed record GitHubReleaseAssetResponse(
        string Name,
        string BrowserDownloadUrl);

    [JsonSerializable(typeof(GitHubReleaseResponse))]
    private sealed partial class JsonOptions : JsonSerializerContext;
}