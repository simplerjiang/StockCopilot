using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public interface IStockNewsImpactService
{
    StockNewsImpactDto Evaluate(string symbol, string name, IReadOnlyList<IntradayMessageDto> messages);
}

public sealed class StockNewsImpactService : IStockNewsImpactService
{
    private static readonly string[] PositiveKeywords =
    {
        "上涨", "增持", "回购", "中标", "签约", "业绩增长", "盈利", "利好", "上调", "扩产", "突破", "创新高", "获批", "订单", "超预期"
    };

    private static readonly string[] NegativeKeywords =
    {
        "下跌", "减持", "被罚", "违规", "亏损", "利空", "下调", "诉讼", "停产", "裁员", "风险", "暴雷", "业绩下滑", "大跌", "降级"
    };

    private static readonly (string Keyword, string Theme)[] ThemeKeywords =
    {
        ("回购", "股份回购"),
        ("增持", "股东增持"),
        ("减持", "股东减持"),
        ("中标", "项目中标"),
        ("签约", "项目签约"),
        ("业绩", "业绩表现"),
        ("盈利", "业绩表现"),
        ("亏损", "业绩表现"),
        ("诉讼", "合规诉讼"),
        ("被罚", "合规诉讼"),
        ("停产", "经营风险"),
        ("裁员", "经营风险"),
        ("创新高", "价格突破"),
        ("突破", "价格突破"),
        ("订单", "订单进展"),
        ("获批", "监管审批")
    };

    public StockNewsImpactDto Evaluate(string symbol, string name, IReadOnlyList<IntradayMessageDto> messages)
    {
        var merged = messages
            .Select(BuildImpactItem)
            .GroupBy(item => BuildDedupeKey(item))
            .Select(group => MergeGroup(group.ToList()))
            .ToList();

        var items = merged
            .OrderByDescending(item => Math.Abs(item.ImpactScore))
            .Take(20)
            .ToList();

        var positive = items.Count(item => item.Category == "利好");
        var negative = items.Count(item => item.Category == "利空");
        var neutral = items.Count(item => item.Category == "中性");

        var overall = positive == negative
            ? "中性"
            : positive > negative
                ? "利好偏多"
                : "利空偏多";

        var maxPositive = items.Where(item => item.ImpactScore > 0).Select(item => item.ImpactScore).DefaultIfEmpty(0).Max();
        var maxNegative = items.Where(item => item.ImpactScore < 0).Select(item => Math.Abs(item.ImpactScore)).DefaultIfEmpty(0).Max();

        var summary = new StockNewsImpactSummaryDto(
            positive,
            neutral,
            negative,
            overall,
            maxPositive,
            maxNegative
        );

        return new StockNewsImpactDto(symbol, name, DateTime.Now, summary, items);
    }

    private static StockNewsImpactItemDto BuildImpactItem(IntradayMessageDto message)
    {
        var title = message.Title ?? string.Empty;
        var eventType = DetectEventType(title);
        var typeWeight = GetEventTypeWeight(eventType);
        var sourceCredibility = GetSourceCredibility(message.Source ?? string.Empty);
        var posHits = CountHits(title, PositiveKeywords);
        var negHits = CountHits(title, NegativeKeywords);
        var timeWeight = GetTimeWeight(message.PublishedAt);
        var weighted = (posHits - negHits) * 20m * typeWeight * sourceCredibility * timeWeight;
        var score = Math.Clamp((int)Math.Round(weighted), -100, 100);

        var category = score >= 20
            ? "利好"
            : score <= -20
                ? "利空"
                : "中性";

        var theme = DetectTheme(title);
        var reason = BuildReason(posHits, negHits, eventType, typeWeight, sourceCredibility, timeWeight, theme);

        return new StockNewsImpactItemDto(
            title,
            message.Source ?? "",
            message.PublishedAt,
            message.Url,
            eventType,
            typeWeight,
            sourceCredibility,
            theme,
            1,
            category,
            score,
            reason
        );
    }

    private static StockNewsImpactItemDto MergeGroup(IReadOnlyList<StockNewsImpactItemDto> group)
    {
        if (group.Count == 1)
        {
            return group[0];
        }

        var representative = group
            .OrderByDescending(item => item.PublishedAt)
            .ThenByDescending(item => Math.Abs(item.ImpactScore))
            .First();

        var maxScore = group.OrderByDescending(item => Math.Abs(item.ImpactScore)).First().ImpactScore;
        var category = maxScore >= 20
            ? "利好"
            : maxScore <= -20
                ? "利空"
                : "中性";

        var avgTypeWeight = Math.Round((decimal)group.Average(item => (double)item.TypeWeight), 2);
        var avgSourceCredibility = Math.Round((decimal)group.Average(item => (double)item.SourceCredibility), 2);
        var sources = string.Join("/", group.Select(item => item.Source).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().Take(3));
        var reason = $"同主题合并:{group.Count}条；最强信号:{maxScore}；来源:{sources}";

        return representative with
        {
            ImpactScore = maxScore,
            Category = category,
            MergedCount = group.Count,
            TypeWeight = avgTypeWeight,
            SourceCredibility = avgSourceCredibility,
            Reason = reason
        };
    }

    private static string BuildDedupeKey(StockNewsImpactItemDto item)
    {
        return $"{item.Theme}|{item.Category}";
    }

    private static string NormalizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var compact = new string(title
            .Where(ch => !char.IsWhiteSpace(ch) && ch != '，' && ch != '。' && ch != ',' && ch != '.')
            .ToArray());
        return compact.Length <= 18 ? compact : compact[..18];
    }

    private static int CountHits(string title, IReadOnlyList<string> keywords)
    {
        var hits = 0;
        foreach (var keyword in keywords)
        {
            if (title.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                hits += 1;
            }
        }
        return hits;
    }

    private static string DetectEventType(string title)
    {
        if (title.Contains("公告", StringComparison.OrdinalIgnoreCase) || title.Contains("快报", StringComparison.OrdinalIgnoreCase))
        {
            return "公告";
        }

        if (title.Contains("研报", StringComparison.OrdinalIgnoreCase) || title.Contains("评级", StringComparison.OrdinalIgnoreCase))
        {
            return "研报";
        }

        return "新闻";
    }

    private static decimal GetEventTypeWeight(string eventType)
    {
        return eventType switch
        {
            "公告" => 1.25m,
            "研报" => 1.1m,
            _ => 1.0m
        };
    }

    private static decimal GetSourceCredibility(string source)
    {
        if (source.Contains("交易所", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("证监", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("公告", StringComparison.OrdinalIgnoreCase))
        {
            return 1.2m;
        }

        if (source.Contains("新华社", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("证券", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("新浪", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("东方财富", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("腾讯", StringComparison.OrdinalIgnoreCase))
        {
            return 1.0m;
        }

        return 0.85m;
    }

    private static decimal GetTimeWeight(DateTime publishedAt)
    {
        var age = DateTime.Now - publishedAt;
        if (age.TotalHours <= 6)
        {
            return 1.1m;
        }

        if (age.TotalHours <= 24)
        {
            return 1.0m;
        }

        return 0.9m;
    }

    private static string DetectTheme(string title)
    {
        foreach (var (keyword, theme) in ThemeKeywords)
        {
            if (title.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return theme;
            }
        }

        return "其他";
    }

    private static string? BuildReason(int positiveHits, int negativeHits, string eventType, decimal typeWeight, decimal sourceCredibility, decimal timeWeight, string theme)
    {
        if (positiveHits == 0 && negativeHits == 0)
        {
            return null;
        }

        return $"主题:{theme} 类型:{eventType}({typeWeight:0.00}) 来源可信度:{sourceCredibility:0.00} 时效:{timeWeight:0.00} 正向:{positiveHits} 负向:{negativeHits}";
    }
}
