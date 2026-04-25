using System.Globalization;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public sealed class SinaStockCrawler : IStockCrawlerSource
{
    private readonly HttpClient _httpClient;

    public SinaStockCrawler(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public string SourceName => "新浪";

    public async Task<StockQuoteDto?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var normalized = StockSymbolNormalizer.Normalize(symbol);
        var url = $"https://hq.sinajs.cn/list={normalized}";
        var raw = await _httpClient.GetStringAsync(url, cancellationToken);

        var payload = ExtractPayload(raw);
        var fields = payload.Split(',');
        if (fields.Length < 4)
        {
            return BuildEmptyQuote(normalized);
        }

        var name = fields[0].Replace(" ", "");  // 中文股票名不应有空格
        var open = ParseDecimal(fields.ElementAtOrDefault(1));
        var prevClose = ParseDecimal(fields.ElementAtOrDefault(2));
        var price = ParseDecimal(fields.ElementAtOrDefault(3));
        var high = ParseDecimal(fields.ElementAtOrDefault(4));
        var low = ParseDecimal(fields.ElementAtOrDefault(5));
        var change = price - prevClose;
        var changePercent = prevClose == 0 ? 0 : Math.Round(change / prevClose * 100, 2);

        return new StockQuoteDto(
            normalized,
            name,
            price,
            change,
            changePercent,
            0m,
            0m,
            high,
            low,
            0m,
            DateTime.UtcNow,
            Array.Empty<StockNewsDto>(),
            Array.Empty<StockIndicatorDto>()
        );
    }

    public Task<MarketIndexDto> GetMarketIndexAsync(string symbol, CancellationToken cancellationToken = default)
    {
        return GetQuoteAsync(symbol, cancellationToken)
            .ContinueWith(task =>
            {
                var quote = task.Result;
                return new MarketIndexDto(
                    quote.Symbol,
                    quote.Name,
                    quote.Price,
                    quote.Change,
                    quote.ChangePercent,
                    quote.Timestamp
                );
            }, cancellationToken);
    }

    public Task<IReadOnlyList<KLinePointDto>> GetKLineAsync(string symbol, string interval, int count, CancellationToken cancellationToken = default)
    {
        // TODO: 新浪K线接口待接入
        IReadOnlyList<KLinePointDto> result = Array.Empty<KLinePointDto>();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<MinuteLinePointDto>> GetMinuteLineAsync(string symbol, CancellationToken cancellationToken = default)
    {
        // TODO: 新浪分时接口待接入
        IReadOnlyList<MinuteLinePointDto> result = Array.Empty<MinuteLinePointDto>();
        return Task.FromResult(result);
    }

    public async Task<IReadOnlyList<IntradayMessageDto>> GetIntradayMessagesAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var normalized = StockSymbolNormalizer.Normalize(symbol);
        var code = normalized.Replace("sh", string.Empty).Replace("sz", string.Empty);

        var companyUrl = $"https://finance.sina.com.cn/realstock/company/{normalized}/nc.shtml";
        var companyHtml = await _httpClient.GetStringAsync(companyUrl, cancellationToken);
        var companyMessages = SinaCompanyNewsParser.ParseCompanyNews(companyHtml);
        if (companyMessages.Count > 0)
        {
            return companyMessages
                .OrderByDescending(x => x.PublishedAt)
                .Take(20)
                .ToArray();
        }

        var rollUrl = "https://feed.mix.sina.com.cn/api/roll/get?pageid=155&lid=1686&num=20&versionNumber=1.2.8.1";
        var rollJson = await _httpClient.GetStringAsync(rollUrl, cancellationToken);
        var rollMessages = SinaRollParser.ParseRollMessages(rollJson, code);

        return rollMessages
            .OrderByDescending(x => x.PublishedAt)
            .Take(20)
            .ToArray();
    }

    private static StockQuoteDto BuildEmptyQuote(string symbol)
    {
        return new StockQuoteDto(
            symbol,
            symbol,
            0m,
            0m,
            0m,
            0m,
            0m,
            0m,
            0m,
            0m,
            DateTime.UtcNow,
            Array.Empty<StockNewsDto>(),
            Array.Empty<StockIndicatorDto>()
        );
    }

    private static string ExtractPayload(string raw)
    {
        var start = raw.IndexOf('"');
        var end = raw.LastIndexOf('"');
        if (start >= 0 && end > start)
        {
            return raw.Substring(start + 1, end - start - 1);
        }

        return raw;
    }

    private static decimal ParseDecimal(string? input)
    {
        if (decimal.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }
        return 0m;
    }

}
