using HtmlAgilityPack;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using System.Globalization;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

internal static class SinaCompanyNewsParser
{
    private static readonly TimeZoneInfo ChinaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
    public static IReadOnlyList<IntradayMessageDto> ParseCompanyNews(string html)
    {
        return ParseCompanyNews(html, DateTime.UtcNow);
    }

    internal static IReadOnlyList<IntradayMessageDto> ParseCompanyNews(string html, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return Array.Empty<IntradayMessageDto>();
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var nodes = doc.DocumentNode.SelectNodes("//div[contains(@class,'datelist')]//li");
        if (nodes == null || nodes.Count == 0)
        {
            return Array.Empty<IntradayMessageDto>();
        }

        var list = new List<IntradayMessageDto>();
        foreach (var node in nodes)
        {
            var link = node.SelectSingleNode(".//a");
            var timeNode = node.SelectSingleNode(".//span");
            if (link == null)
            {
                continue;
            }

            var title = link.InnerText?.Trim() ?? string.Empty;
            var url = link.GetAttributeValue("href", null);
            var timeText = timeNode?.InnerText?.Trim();

            var publishedAt = ParseTime(timeText, nowUtc);
            list.Add(new IntradayMessageDto(title, "新浪", publishedAt, url));
        }

        return list;
    }

    private static DateTime ParseTime(string? timeText, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(timeText))
        {
            return nowUtc;
        }

        if (TimeSpan.TryParse(timeText, CultureInfo.InvariantCulture, out var time))
        {
            var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), ChinaTimeZone).Date;
            var local = new DateTime(today.Year, today.Month, today.Day, time.Hours, time.Minutes, 0, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(local, ChinaTimeZone);
        }

        if (DateTime.TryParse(timeText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), ChinaTimeZone);
        }

        return nowUtc;
    }
}
