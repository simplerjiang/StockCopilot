using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SimplerJiangAiAgent.Api.Data;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services.Recommend;

public interface IRecommendSectorCodeNameResolver
{
    Task<IReadOnlyDictionary<string, string>> GetLatestCodeNameMapAsync(CancellationToken cancellationToken = default);
}

public sealed class RecommendSectorCodeNameResolver : IRecommendSectorCodeNameResolver
{
    private readonly DbContextOptions<AppDbContext> _dbContextOptions;

    public RecommendSectorCodeNameResolver(DbContextOptions<AppDbContext> dbContextOptions)
    {
        _dbContextOptions = dbContextOptions;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetLatestCodeNameMapAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = new AppDbContext(_dbContextOptions);
        var latestSnapshotTime = await dbContext.SectorRotationSnapshots
            .AsNoTracking()
            .MaxAsync(item => (DateTime?)item.SnapshotTime, cancellationToken);

        if (latestSnapshotTime is null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var rows = await dbContext.SectorRotationSnapshots
            .AsNoTracking()
            .Where(item => item.SnapshotTime == latestSnapshotTime.Value)
            .Select(item => new
            {
                item.SectorCode,
                item.SectorName,
                item.IsMainline,
                item.MainlineScore,
                item.RankNo
            })
            .ToListAsync(cancellationToken);

        return rows
            .Where(item => !string.IsNullOrWhiteSpace(item.SectorCode) && !string.IsNullOrWhiteSpace(item.SectorName))
            .GroupBy(item => item.SectorCode.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.IsMainline)
                    .ThenByDescending(item => item.MainlineScore)
                    .ThenBy(item => item.RankNo <= 0 ? int.MaxValue : item.RankNo)
                    .Select(item => item.SectorName.Trim())
                    .First(),
                StringComparer.OrdinalIgnoreCase);
    }
}

internal static class RecommendSectorCodeNameNormalizer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly string[] NameProperties = ["name", "sectorName"];
    private static readonly string[] CodeProperties = ["code", "sectorCode"];

    public static string NormalizeJson(string json, IReadOnlyDictionary<string, string> codeNameMap)
    {
        if (string.IsNullOrWhiteSpace(json) || codeNameMap.Count == 0)
        {
            return json;
        }

        var codeToName = BuildCodeToNameMap(codeNameMap);
        if (codeToName.Count == 0)
        {
            return json;
        }

        var nameToCode = BuildUniqueNameToCodeMap(codeToName);
        var root = JsonNode.Parse(json);
        if (root is null)
        {
            return json;
        }

        var changed = NormalizeNode(root, codeToName, nameToCode);
        return changed ? root.ToJsonString(JsonOptions) : json;
    }

    private static bool NormalizeNode(
        JsonNode node,
        IReadOnlyDictionary<string, string> codeToName,
        IReadOnlyDictionary<string, string> nameToCode)
    {
        var changed = false;

        if (node is JsonObject jsonObject)
        {
            changed |= NormalizeSectorObject(jsonObject, codeToName, nameToCode);

            foreach (var property in jsonObject.ToArray())
            {
                if (property.Value is not null)
                {
                    changed |= NormalizeNode(property.Value, codeToName, nameToCode);
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                if (item is not null)
                {
                    changed |= NormalizeNode(item, codeToName, nameToCode);
                }
            }
        }

        return changed;
    }

    private static bool NormalizeSectorObject(
        JsonObject jsonObject,
        IReadOnlyDictionary<string, string> codeToName,
        IReadOnlyDictionary<string, string> nameToCode)
    {
        var nameProperty = FindStringProperty(jsonObject, NameProperties, out var rawName);
        var codeProperty = FindStringProperty(jsonObject, CodeProperties, out var rawCode);

        if (nameProperty is null && codeProperty is null)
        {
            return false;
        }

        var normalizedName = NormalizeText(rawName);
        var normalizedCode = NormalizeText(rawCode);
        string? canonicalCode = null;
        string? canonicalName = null;

        if (!string.IsNullOrWhiteSpace(normalizedName)
            && nameToCode.TryGetValue(normalizedName, out var codeByName)
            && codeToName.TryGetValue(codeByName, out var nameByName))
        {
            canonicalCode = codeByName;
            canonicalName = nameByName;
        }
        else if (!string.IsNullOrWhiteSpace(normalizedCode)
            && codeToName.TryGetValue(normalizedCode, out var nameByCode))
        {
            canonicalCode = normalizedCode;
            canonicalName = nameByCode;
        }

        if (string.IsNullOrWhiteSpace(canonicalName) && string.IsNullOrWhiteSpace(canonicalCode))
        {
            return false;
        }

        var changed = false;
        if (nameProperty is not null && !string.Equals(rawName, canonicalName, StringComparison.Ordinal))
        {
            jsonObject[nameProperty] = canonicalName;
            changed = true;
        }

        if (codeProperty is not null && !string.Equals(rawCode, canonicalCode, StringComparison.OrdinalIgnoreCase))
        {
            jsonObject[codeProperty] = canonicalCode;
            changed = true;
        }

        return changed;
    }

    private static Dictionary<string, string> BuildCodeToNameMap(IReadOnlyDictionary<string, string> codeNameMap)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in codeNameMap)
        {
            var code = NormalizeText(pair.Key);
            var name = NormalizeText(pair.Value);
            if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(name))
            {
                map[code] = name;
            }
        }

        return map;
    }

    private static Dictionary<string, string> BuildUniqueNameToCodeMap(IReadOnlyDictionary<string, string> codeToName)
    {
        var nameToCode = new Dictionary<string, string>(StringComparer.Ordinal);
        var duplicatedNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var pair in codeToName)
        {
            var name = NormalizeText(pair.Value);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (nameToCode.TryGetValue(name, out var existingCode)
                && !string.Equals(existingCode, pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                duplicatedNames.Add(name);
                continue;
            }

            nameToCode[name] = pair.Key;
        }

        foreach (var name in duplicatedNames)
        {
            nameToCode.Remove(name);
        }

        return nameToCode;
    }

    private static string? FindStringProperty(JsonObject jsonObject, IReadOnlyList<string> propertyNames, out string? value)
    {
        foreach (var propertyName in propertyNames)
        {
            if (jsonObject.TryGetPropertyValue(propertyName, out var node)
                && node is JsonValue jsonValue
                && jsonValue.TryGetValue<string>(out var stringValue))
            {
                value = stringValue;
                return propertyName;
            }
        }

        value = null;
        return null;
    }

    private static string NormalizeText(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}