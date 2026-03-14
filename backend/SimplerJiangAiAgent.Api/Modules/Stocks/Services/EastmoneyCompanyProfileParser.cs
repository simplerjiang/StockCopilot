using System.Text.Json;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

internal static class EastmoneyCompanyProfileParser
{
    private static readonly (string JsonKey, string Label)[] FactMappings =
    [
        ("gsmc", "公司全称"),
        ("ywmc", "英文名称"),
        ("zqlb", "证券类别"),
        ("ssjys", "上市交易所"),
        ("sshy", "所属行业"),
        ("sszjhhy", "证监会行业"),
        ("qy", "所属地区"),
        ("zcdz", "注册地址"),
        ("bgdz", "办公地址"),
        ("gswz", "公司网站"),
        ("zjl", "总经理"),
        ("frdb", "法人代表"),
        ("dsz", "董事长"),
        ("zczb", "注册资本"),
        ("lxdh", "联系电话"),
        ("dzxx", "电子邮箱")
    ];

    public static EastmoneyCompanyProfileDto Parse(string symbol, string json, string? shareholderJson = null)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("jbzl", out var profileNode) || profileNode.ValueKind != JsonValueKind.Object)
        {
            return new EastmoneyCompanyProfileDto(symbol, symbol, null, ParseShareholderCount(shareholderJson), ParseShareholderFacts(shareholderJson));
        }

        var name = profileNode.TryGetProperty("agjc", out var nameNode)
            ? nameNode.GetString() ?? symbol
            : symbol;
        var sectorName = profileNode.TryGetProperty("sshy", out var sectorNode)
            ? sectorNode.GetString()
            : null;

        var shareholderCount = ParseShareholderCount(shareholderJson);
        var facts = BuildFacts(profileNode, shareholderJson, shareholderCount);

        return new EastmoneyCompanyProfileDto(
            symbol,
            name,
            string.IsNullOrWhiteSpace(sectorName) ? null : sectorName.Trim(),
            shareholderCount,
            facts);
    }

    private static IReadOnlyList<StockFundamentalFactDto> BuildFacts(JsonElement profileNode, string? shareholderJson, int? shareholderCount)
    {
        var facts = new List<StockFundamentalFactDto>();
        foreach (var (jsonKey, label) in FactMappings)
        {
            if (!profileNode.TryGetProperty(jsonKey, out var valueNode) || valueNode.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            var value = valueNode.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(value) || value == "--")
            {
                continue;
            }

            facts.Add(new StockFundamentalFactDto(label, value, "东方财富公司概况"));
        }

        if (shareholderCount.HasValue)
        {
            facts.Add(new StockFundamentalFactDto("股东户数", shareholderCount.Value.ToString(), "东方财富股东研究"));
        }

        foreach (var fact in ParseShareholderFacts(shareholderJson))
        {
            if (facts.Any(existing => existing.Label == fact.Label && existing.Value == fact.Value))
            {
                continue;
            }

            facts.Add(fact);
        }

        return facts;
    }

    private static IReadOnlyList<StockFundamentalFactDto> ParseShareholderFacts(string? shareholderJson)
    {
        if (string.IsNullOrWhiteSpace(shareholderJson))
        {
            return Array.Empty<StockFundamentalFactDto>();
        }

        using var document = JsonDocument.Parse(shareholderJson);
        var root = document.RootElement;
        if (!root.TryGetProperty("gdrs", out var gdrsNode) || gdrsNode.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<StockFundamentalFactDto>();
        }

        foreach (var item in gdrsNode.EnumerateArray())
        {
            var facts = new List<StockFundamentalFactDto>();

            if (item.TryGetProperty("END_DATE", out var endDateNode))
            {
                var endDate = endDateNode.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(endDate))
                {
                    facts.Add(new StockFundamentalFactDto("股东户数统计截止", endDate, "东方财富股东研究"));
                }
            }

            if (item.TryGetProperty("HOLD_FOCUS", out var focusNode))
            {
                var focus = focusNode.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(focus))
                {
                    facts.Add(new StockFundamentalFactDto("股权集中度", focus, "东方财富股东研究"));
                }
            }

            if (item.TryGetProperty("AVG_HOLD_AMT", out var avgHoldAmtNode))
            {
                var avgHoldAmt = avgHoldAmtNode.ValueKind == JsonValueKind.Number && avgHoldAmtNode.TryGetDecimal(out var avgHoldAmtValue)
                    ? Math.Round(avgHoldAmtValue, 2).ToString("0.##")
                    : avgHoldAmtNode.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(avgHoldAmt))
                {
                    facts.Add(new StockFundamentalFactDto("户均持股市值", avgHoldAmt, "东方财富股东研究"));
                }
            }

            if (item.TryGetProperty("AVG_FREE_SHARES", out var avgSharesNode))
            {
                var avgShares = avgSharesNode.ValueKind == JsonValueKind.Number && avgSharesNode.TryGetDecimal(out var avgSharesValue)
                    ? Math.Round(avgSharesValue, 2).ToString("0.##")
                    : avgSharesNode.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(avgShares))
                {
                    facts.Add(new StockFundamentalFactDto("户均流通股", avgShares, "东方财富股东研究"));
                }
            }

            return facts;
        }

        return Array.Empty<StockFundamentalFactDto>();
    }

    private static int? ParseShareholderCount(string? shareholderJson)
    {
        if (string.IsNullOrWhiteSpace(shareholderJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(shareholderJson);
        var root = document.RootElement;
        if (!root.TryGetProperty("gdrs", out var gdrsNode) || gdrsNode.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in gdrsNode.EnumerateArray())
        {
            if (!item.TryGetProperty("HOLDER_TOTAL_NUM", out var countNode))
            {
                continue;
            }

            if (countNode.ValueKind == JsonValueKind.Number && countNode.TryGetInt32(out var count))
            {
                return count;
            }

            if (countNode.ValueKind == JsonValueKind.String && int.TryParse(countNode.GetString(), out count))
            {
                return count;
            }
        }

        return null;
    }
}