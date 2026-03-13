using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

internal static partial class SinaSectorNewsSearchParser
{
    private const string DefaultSource = "新浪财经搜索";
    private static readonly TimeZoneInfo ChinaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");

    public static IReadOnlyList<LocalSectorReportSeed> Parse(
        string symbol,
        string? sectorName,
        string html,
        DateTime crawledAt)
    {
        if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(sectorName) || string.IsNullOrWhiteSpace(html))
        {
            return Array.Empty<LocalSectorReportSeed>();
        }

        var document = new HtmlDocument();
        document.LoadHtml(html);

        var resultNodes = document.DocumentNode.SelectNodes("//div[contains(@class,'box-result')]")?.ToArray() ?? Array.Empty<HtmlNode>();
        var items = resultNodes
            .Select(node => BuildSeed(node, symbol, sectorName, crawledAt))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();

        if (items.Length == 0)
        {
            var titleNodes = document.DocumentNode.SelectNodes("//h2[a[@href]]")?.ToArray() ?? Array.Empty<HtmlNode>();
            items = titleNodes
                .Select(node => BuildFallbackSeed(node, symbol, sectorName, crawledAt))
                .Where(item => item is not null)
                .Select(item => item!)
                .ToArray();
        }

        return items
            .OrderByDescending(item => item.PublishTime)
            .DistinctBy(item => item.Url ?? item.ExternalId ?? item.Title)
            .Take(20)
            .ToArray();
    }

    private static LocalSectorReportSeed? BuildSeed(HtmlNode container, string symbol, string sectorName, DateTime crawledAt)
    {
        var linkNode = container.SelectSingleNode(".//h2/a[@href]") ?? container.SelectSingleNode(".//a[@href]");
        if (linkNode is null)
        {
            return null;
        }

        var metaNode = container.SelectSingleNode(".//*[contains(@class,'fgray_time')]");
        return BuildSeed(linkNode, metaNode, symbol, sectorName, crawledAt);
    }

    private static LocalSectorReportSeed? BuildFallbackSeed(HtmlNode titleNode, string symbol, string sectorName, DateTime crawledAt)
    {
        var linkNode = titleNode.SelectSingleNode(".//a[@href]");
        if (linkNode is null)
        {
            return null;
        }

        var metaNode = titleNode.SelectSingleNode("following-sibling::h2[1]//*[contains(@class,'fgray_time')]")
            ?? titleNode.ParentNode?.SelectSingleNode(".//*[contains(@class,'fgray_time')]");

        return BuildSeed(linkNode, metaNode, symbol, sectorName, crawledAt);
    }

    private static LocalSectorReportSeed? BuildSeed(
        HtmlNode linkNode,
        HtmlNode? metaNode,
        string symbol,
        string sectorName,
        DateTime crawledAt)
    {
        var title = NormalizeText(linkNode.InnerText);
        var url = linkNode.GetAttributeValue("href", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var metaText = NormalizeText(metaNode?.InnerText);
        var (source, publishTime) = ParseMeta(metaText, crawledAt);

        return new LocalSectorReportSeed(
            symbol,
            sectorName,
            "sector",
            title,
            source,
            "sina-sector-search",
            url,
            publishTime,
            crawledAt,
            url);
    }

    private static (string Source, DateTime PublishTime) ParseMeta(string? raw, DateTime crawledAt)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (DefaultSource, crawledAt);
        }

        var normalized = NormalizeText(raw);
        var timeMatch = PublishTimePattern().Match(normalized);
        if (!timeMatch.Success)
        {
            return (normalized, crawledAt);
        }

        var source = normalized[..timeMatch.Index].Trim();
        var publishTime = ParsePublishTime(timeMatch.Value, crawledAt);
        return (string.IsNullOrWhiteSpace(source) ? DefaultSource : source, publishTime);
    }

    private static DateTime ParsePublishTime(string raw, DateTime crawledAt)
    {
        var anchor = TimeZoneInfo.ConvertTimeFromUtc(
            crawledAt.Kind == DateTimeKind.Utc ? crawledAt : DateTime.SpecifyKind(crawledAt, DateTimeKind.Utc),
            ChinaTimeZone);
        var normalized = NormalizeText(raw);

        var minutesMatch = RelativeMinutesPattern().Match(normalized);
        if (minutesMatch.Success && int.TryParse(minutesMatch.Groups["value"].Value, out var minutes))
        {
            return anchor.AddMinutes(-minutes).ToUniversalTime();
        }

        var hoursMatch = RelativeHoursPattern().Match(normalized);
        if (hoursMatch.Success && int.TryParse(hoursMatch.Groups["value"].Value, out var hours))
        {
            return anchor.AddHours(-hours).ToUniversalTime();
        }

        var todayMatch = TodayTimePattern().Match(normalized);
        if (todayMatch.Success)
        {
            return ParseChinaLocalTime(anchor, anchor.Year, anchor.Month, anchor.Day, todayMatch.Groups["time"].Value);
        }

        var yesterdayMatch = YesterdayTimePattern().Match(normalized);
        if (yesterdayMatch.Success)
        {
            var day = anchor.AddDays(-1);
            return ParseChinaLocalTime(day, day.Year, day.Month, day.Day, yesterdayMatch.Groups["time"].Value);
        }

        var monthDayMatch = MonthDayTimePattern().Match(normalized);
        if (monthDayMatch.Success
            && int.TryParse(monthDayMatch.Groups["month"].Value, out var month)
            && int.TryParse(monthDayMatch.Groups["day"].Value, out var dayOfMonth))
        {
            return ParseChinaLocalTime(anchor, anchor.Year, month, dayOfMonth, monthDayMatch.Groups["time"].Value);
        }

        var fullDateText = normalized
            .Replace("年", "-")
            .Replace("月", "-")
            .Replace("日", " ");

        if (DateTime.TryParse(fullDateText, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), ChinaTimeZone);
        }

        return crawledAt;
    }

    private static DateTime ParseChinaLocalTime(DateTime anchor, int year, int month, int day, string timeText)
    {
        if (!TimeOnly.TryParse(timeText, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var time))
        {
            return anchor.ToUniversalTime();
        }

        var local = new DateTime(year, month, day, time.Hour, time.Minute, time.Second, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, ChinaTimeZone);
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return WhitespacePattern().Replace(HtmlEntity.DeEntitize(value), " ").Trim();
    }

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"(?<value>\d+)\s*分钟前", RegexOptions.Compiled)]
    private static partial Regex RelativeMinutesPattern();

    [GeneratedRegex(@"(?<value>\d+)\s*小时前", RegexOptions.Compiled)]
    private static partial Regex RelativeHoursPattern();

    [GeneratedRegex(@"今天\s*(?<time>\d{1,2}:\d{2}(?::\d{2})?)", RegexOptions.Compiled)]
    private static partial Regex TodayTimePattern();

    [GeneratedRegex(@"(?:昨天|昨日)\s*(?<time>\d{1,2}:\d{2}(?::\d{2})?)", RegexOptions.Compiled)]
    private static partial Regex YesterdayTimePattern();

    [GeneratedRegex(@"(?<month>\d{1,2})月(?<day>\d{1,2})日\s*(?<time>\d{1,2}:\d{2}(?::\d{2})?)", RegexOptions.Compiled)]
    private static partial Regex MonthDayTimePattern();

    [GeneratedRegex(@"(\d+\s*分钟前|\d+\s*小时前|今天\s*\d{1,2}:\d{2}(?::\d{2})?|(?:昨天|昨日)\s*\d{1,2}:\d{2}(?::\d{2})?|\d{1,2}月\d{1,2}日\s*\d{1,2}:\d{2}(?::\d{2})?|\d{4}[-/.年]\d{1,2}[-/.月]\d{1,2}[日\s]\d{1,2}:\d{2}(?::\d{2})?)", RegexOptions.Compiled)]
    private static partial Regex PublishTimePattern();
}