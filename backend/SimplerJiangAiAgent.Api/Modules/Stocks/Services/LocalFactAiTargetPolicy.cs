using System.Text.Json;
using SimplerJiangAiAgent.Api.Data.Entities;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

internal static class LocalFactAiTargetPolicy
{
    private static readonly string[] TargetPrefixes =
    [
        "个股:", "个股：", "股票:", "股票：", "公司:", "公司：",
        "板块:", "板块：", "行业:", "行业：", "标的:", "标的：", "靶点:", "靶点："
    ];

    private static readonly string[] CompanySuffixes =
    [
        "股份有限公司", "有限责任公司", "有限公司", "集团股份", "集团", "公司", "A股", "股票"
    ];

    private static readonly HashSet<string> GenericAiTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "紧急消息", "突发事件", "宏观货币", "地缘政治", "行业周期", "行业预期", "政策红利", "财报业绩", "经营数据",
        "资金面", "监管政策", "海外映射", "商品价格", "风险预警", "公告", "分红", "回购", "并购重组", "股权激励",
        "订单合同", "投资扩产", "市场环境", "板块轮动", "主线板块", "板块上下文兜底"
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string? SanitizeForStock(LocalStockNews row, string? aiTarget, string? fallbackName = null, string? fallbackSectorName = null)
    {
        var associatedName = FirstNonEmpty(
            StockNameNormalizer.NormalizeDisplayName(fallbackName),
            StockNameNormalizer.NormalizeDisplayName(row.Name));
        return Sanitize(
            aiTarget,
            "stock",
            row.Symbol,
            associatedName,
            FirstNonEmpty(fallbackSectorName, row.SectorName),
            row.Title,
            row.ArticleExcerpt,
            row.ArticleSummary);
    }

    public static string? SanitizeForSectorReport(LocalSectorReport row, string? aiTarget)
    {
        return Sanitize(
            aiTarget,
            row.Level,
            row.Symbol,
            null,
            row.SectorName,
            row.Title,
            row.ArticleExcerpt,
            row.ArticleSummary);
    }

    public static IReadOnlyList<string> SanitizeTagsForStock(LocalStockNews row, IReadOnlyList<string> aiTags, string? fallbackName = null, string? fallbackSectorName = null)
    {
        var associatedName = FirstNonEmpty(
            StockNameNormalizer.NormalizeDisplayName(fallbackName),
            StockNameNormalizer.NormalizeDisplayName(row.Name));
        return SanitizeTags(aiTags, row.Symbol, associatedName, FirstNonEmpty(fallbackSectorName, row.SectorName));
    }

    public static IReadOnlyList<string> SanitizeTagsForSectorReport(LocalSectorReport row, IReadOnlyList<string> aiTags)
    {
        return SanitizeTags(aiTags, row.Symbol, null, row.SectorName);
    }

    public static bool Repair(LocalStockNews row, string? fallbackName = null, string? fallbackSectorName = null)
    {
        var changed = false;
        var sanitized = SanitizeForStock(row, row.AiTarget, fallbackName, fallbackSectorName);
        if (!string.Equals(row.AiTarget, sanitized, StringComparison.Ordinal))
        {
            row.AiTarget = sanitized;
            changed = true;
        }

        var sanitizedTags = SanitizeTagsForStock(row, ParseTags(row.AiTags), fallbackName, fallbackSectorName);
        var serializedTags = SerializeTagsOrNull(sanitizedTags);
        if (!string.Equals(row.AiTags, serializedTags, StringComparison.Ordinal))
        {
            row.AiTags = serializedTags;
            changed = true;
        }

        return changed;
    }

    public static bool Repair(LocalSectorReport row)
    {
        var changed = false;
        var sanitized = SanitizeForSectorReport(row, row.AiTarget);
        if (!string.Equals(row.AiTarget, sanitized, StringComparison.Ordinal))
        {
            row.AiTarget = sanitized;
            changed = true;
        }

        var sanitizedTags = SanitizeTagsForSectorReport(row, ParseTags(row.AiTags));
        var serializedTags = SerializeTagsOrNull(sanitizedTags);
        if (!string.Equals(row.AiTags, serializedTags, StringComparison.Ordinal))
        {
            row.AiTags = serializedTags;
            changed = true;
        }

        return changed;
    }

    private static IReadOnlyList<string> SanitizeTags(
        IReadOnlyList<string> tags,
        string? symbol,
        string? associatedName,
        string? sectorName)
    {
        if (tags.Count == 0)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags)
        {
            var normalized = NormalizeTag(tag);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            var canonical = TryCanonicalizeTag(normalized, symbol, associatedName, sectorName);
            if (canonical is null || !seen.Add(canonical))
            {
                continue;
            }

            result.Add(canonical);
        }

        return result;
    }

    private static string? TryCanonicalizeTag(string tag, string? symbol, string? associatedName, string? sectorName)
    {
        if (IsGarbageTag(tag))
        {
            return null;
        }

        var core = NormalizeTargetCore(tag);
        if (GenericAiTags.Contains(tag) || GenericAiTags.Contains(core))
        {
            return GenericAiTags.Contains(tag) ? tag : core;
        }

        if (MatchesSymbol(tag, symbol))
        {
            return StockSymbolNormalizer.Normalize(symbol!);
        }

        if (MatchesName(tag, associatedName))
        {
            return StockNameNormalizer.NormalizeDisplayNameOrNull(associatedName);
        }

        if (MatchesName(tag, sectorName))
        {
            return NormalizeTarget(sectorName);
        }

        return null;
    }

    private static string? Sanitize(
        string? aiTarget,
        string scope,
        string? symbol,
        string? associatedName,
        string? sectorName,
        string title,
        string? articleExcerpt,
        string? articleSummary)
    {
        var normalizedTarget = NormalizeTarget(aiTarget);
        var hasAssociatedCompany = !string.IsNullOrWhiteSpace(associatedName);
        var sectorFallback = IsSectorScope(scope) && !IsGenericMarketSector(sectorName) ? NormalizeTarget(sectorName) : null;
        var fallbackTarget = hasAssociatedCompany ? StockNameNormalizer.NormalizeDisplayName(associatedName) : sectorFallback;

        if (string.IsNullOrWhiteSpace(normalizedTarget) || IsNoClearTarget(normalizedTarget))
        {
            return fallbackTarget;
        }

        if (IsGenericMarketTarget(normalizedTarget) && IsMarketScope(scope))
        {
            return normalizedTarget;
        }

        if (MatchesSymbol(normalizedTarget, symbol)
            || MatchesName(normalizedTarget, associatedName)
            || MatchesName(normalizedTarget, sectorName)
            || AppearsInEvidence(normalizedTarget, title, articleExcerpt, articleSummary))
        {
            return normalizedTarget;
        }

        return fallbackTarget;
    }

    private static bool AppearsInEvidence(string target, params string?[] evidence)
    {
        var targetCore = NormalizeTargetCore(target);
        if (string.IsNullOrWhiteSpace(targetCore))
        {
            return false;
        }

        foreach (var value in evidence)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var compactEvidence = Compact(value);
            if (compactEvidence.Contains(targetCore, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var variant in BuildNameVariants(targetCore))
            {
                if (variant.Length >= 2 && compactEvidence.Contains(variant, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool MatchesName(string target, string? name)
    {
        var targetCore = NormalizeTargetCore(target);
        var nameCore = NormalizeTargetCore(name);
        if (string.IsNullOrWhiteSpace(targetCore) || string.IsNullOrWhiteSpace(nameCore))
        {
            return false;
        }

        if (targetCore.Contains(nameCore, StringComparison.OrdinalIgnoreCase)
            || nameCore.Contains(targetCore, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var targetVariants = BuildNameVariants(targetCore);
        var nameVariants = BuildNameVariants(nameCore);
        return targetVariants.Any(targetVariant =>
            nameVariants.Any(nameVariant =>
                targetVariant.Length >= 2
                && nameVariant.Length >= 2
                && (targetVariant.Contains(nameVariant, StringComparison.OrdinalIgnoreCase)
                    || nameVariant.Contains(targetVariant, StringComparison.OrdinalIgnoreCase))));
    }

    private static bool MatchesSymbol(string target, string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        var compactTarget = Compact(target);
        var compactSymbol = Compact(symbol);
        var bareSymbol = compactSymbol.Length > 2 && (compactSymbol.StartsWith("sh", StringComparison.OrdinalIgnoreCase) || compactSymbol.StartsWith("sz", StringComparison.OrdinalIgnoreCase))
            ? compactSymbol[2..]
            : compactSymbol;

        return compactTarget.Contains(compactSymbol, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(bareSymbol) && compactTarget.Contains(bareSymbol, StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizeTarget(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        foreach (var prefix in TargetPrefixes)
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var targetName = StockNameNormalizer.NormalizeDisplayName(trimmed[prefix.Length..]);
                return string.IsNullOrWhiteSpace(targetName) ? prefix.Trim() : prefix + targetName;
            }
        }

        return StockNameNormalizer.NormalizeDisplayName(trimmed);
    }

    private static string? NormalizeTag(string? value)
    {
        var normalized = NormalizeTarget(value);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string NormalizeTargetCore(string? value)
    {
        var compact = Compact(value);
        if (string.IsNullOrWhiteSpace(compact))
        {
            return string.Empty;
        }

        foreach (var prefix in TargetPrefixes)
        {
            var compactPrefix = Compact(prefix);
            if (compact.StartsWith(compactPrefix, StringComparison.OrdinalIgnoreCase))
            {
                compact = compact[compactPrefix.Length..];
                break;
            }
        }

        return compact.Trim('(', ')', '（', '）', '[', ']', '【', '】');
    }

    private static IReadOnlyList<string> BuildNameVariants(string value)
    {
        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            value
        };

        foreach (var suffix in CompanySuffixes)
        {
            if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                variants.Add(value[..^suffix.Length]);
            }
        }

        return variants.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
    }

    private static string Compact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value.Where(ch => !char.IsWhiteSpace(ch) && ch is not ':' and not '：' and not '-' and not '_' and not '－' and not '—').ToArray());
    }

    private static IReadOnlyList<string> ParseTags(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        try
        {
            var tags = JsonSerializer.Deserialize<List<string>>(value, JsonOptions);
            return tags?.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

    private static string? SerializeTagsOrNull(IReadOnlyList<string> tags)
    {
        return tags.Count == 0 ? null : SerializeTags(tags);
    }

    private static string SerializeTags(IReadOnlyList<string> tags)
    {
        return JsonSerializer.Serialize(tags.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), JsonOptions);
    }

    private static bool IsNoClearTarget(string value)
    {
        var compact = Compact(value);
        return compact.Contains("无明确", StringComparison.Ordinal)
            || compact.Contains("不明确", StringComparison.Ordinal)
            || compact.Contains("无标的", StringComparison.Ordinal)
            || compact.Contains("无靶点", StringComparison.Ordinal)
            || (compact.Contains("无", StringComparison.Ordinal) && (compact.Contains("标的", StringComparison.Ordinal) || compact.Contains("靶点", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Detects LLM-hallucinated garbage tags: nonsense coined words, overly long tags,
    /// or known blacklisted patterns that carry no semantic value.
    /// </summary>
    internal static bool IsGarbageTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return true;
        }

        var trimmed = tag.Trim();

        // Length guard: valid Chinese tags are typically 2-12 characters
        if (trimmed.Length > 16)
        {
            return true;
        }

        if (trimmed.Length < 2)
        {
            return true;
        }

        // Known garbage tag blacklist (LLM hallucinations observed in production)
        if (GarbageTagBlacklist.Contains(trimmed))
        {
            return true;
        }

        // Partial match: tag contains a known garbage fragment
        var compactTag = Compact(trimmed);
        foreach (var fragment in GarbageTagFragments)
        {
            if (compactTag.Contains(fragment, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Known exact garbage tags produced by LLM hallucination.
    /// This list is intentionally extensible — add new entries as they appear in production logs.
    /// </summary>
    private static readonly HashSet<string> GarbageTagBlacklist = new(StringComparer.OrdinalIgnoreCase)
    {
        "无荒隔靶点", "无明确靶点", "无明确标的", "不明确", "未知",
        "无标的", "无靶点", "暂无", "其他", "N/A", "null", "none",
        "undefined", "无", "空", "未分类", "未知标签", "默认",
        "无关", "无意义", "测试", "test", "TODO"
    };

    /// <summary>
    /// Substring fragments that indicate a garbage/hallucinated tag.
    /// </summary>
    private static readonly string[] GarbageTagFragments =
    [
        "无荒隔", "无明确", "不明确", "无标的", "无靶点"
    ];

    private static bool IsMarketScope(string? scope)
    {
        return string.Equals(scope, "market", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSectorScope(string? scope)
    {
        return string.Equals(scope, "sector", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGenericMarketTarget(string target)
    {
        var core = NormalizeTargetCore(target);
        return core is "大盘" or "市场" or "A股" or "A股市场";
    }

    private static bool IsGenericMarketSector(string? sectorName)
    {
        var core = NormalizeTargetCore(sectorName);
        return string.IsNullOrWhiteSpace(core)
            || core is "大盘" or "大盘环境" or "市场" or "市场环境";
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}