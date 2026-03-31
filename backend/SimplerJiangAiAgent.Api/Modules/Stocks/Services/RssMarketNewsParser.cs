using System.Globalization;
using System.Xml.Linq;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

internal static class RssMarketNewsParser
{
    private const int MaxAcceptedAgeDays = 3;

    public static IReadOnlyList<LocalSectorReportSeed> Parse(
        string xml,
        string source,
        string sourceTag,
        DateTime crawledAt)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return Array.Empty<LocalSectorReportSeed>();
        }

        try
        {
            var utcCrawledAt = EnsureUtc(crawledAt);
            var minPublishTime = utcCrawledAt.AddDays(-MaxAcceptedAgeDays);
            var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            var items = document
                .Descendants()
                .Where(node => node.Name.LocalName == "item" || node.Name.LocalName == "entry")
                .Select(item => BuildSeed(item, source, sourceTag, crawledAt))
                .Where(item => item is not null)
                .Select(item => item!)
                .Where(item => item.PublishTime >= minPublishTime && item.PublishTime <= utcCrawledAt.AddMinutes(5))
                .OrderByDescending(item => item.PublishTime)
                .DistinctBy(item => item.Url ?? item.ExternalId ?? item.Title)
                .Take(12)
                .ToArray();

            return items;
        }
        catch
        {
            return Array.Empty<LocalSectorReportSeed>();
        }
    }

    private static LocalSectorReportSeed? BuildSeed(XElement item, string source, string sourceTag, DateTime crawledAt)
    {
        var title = ReadElementValue(item, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var link = ReadLink(item);
        var externalId = ReadElementValue(item, "guid")
            ?? ReadElementValue(item, "id")
            ?? link;
        var published = ParseDate(
            ReadElementValue(item, "pubDate")
            ?? ReadElementValue(item, "published")
            ?? ReadElementValue(item, "updated"));

        return new LocalSectorReportSeed(
            null,
            "大盘环境",
            "market",
            title.Trim(),
            source,
            sourceTag,
            externalId,
            published,
            crawledAt,
            link);
    }

    private static string? ReadElementValue(XElement item, string localName)
    {
        return item.Elements().FirstOrDefault(node => node.Name.LocalName == localName)?.Value;
    }

    private static string? ReadLink(XElement item)
    {
        var directLink = item.Elements().FirstOrDefault(node => node.Name.LocalName == "link");
        if (directLink is null)
        {
            return null;
        }

        var href = directLink.Attribute("href")?.Value;
        if (!string.IsNullOrWhiteSpace(href))
        {
            return href;
        }

        return string.IsNullOrWhiteSpace(directLink.Value) ? null : directLink.Value.Trim();
    }

    private static DateTime ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DateTime.MinValue;
        }

        if (DateTime.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed;
        }

        return DateTime.MinValue;
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}