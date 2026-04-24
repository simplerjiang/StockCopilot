using System.Text;
using System.Text.RegularExpressions;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;

namespace SimplerJiangAiAgent.Api.Modules.Stocks.Services;

public sealed class StockSearchService : IStockSearchService
{
    private static readonly Regex QuoteRegex = new("\"(?<payload>.*)\"", RegexOptions.Compiled);
    private static readonly Regex CodeRegex = new(@"(?<code>\d{6})", RegexOptions.Compiled);
    private static readonly Regex SymbolRegex = new(@"(?<symbol>(sh|sz)\d{6})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // V048-S1 P0-4：支持带市场前缀的查询（sh/sz/bj/hk/us.），归一后再送上游
    private static readonly Regex PrefixedSymbolRegex = new(@"^(?<prefix>sh|sz|bj|hk)(?<code>\d{6})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex UsPrefixedRegex = new(@"^us\.(?<ticker>[A-Za-z0-9\.\-]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly HttpClient _httpClient;

    public StockSearchService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<StockSearchResultDto>> SearchAsync(string query, int limit = 20, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<StockSearchResultDto>();
        }

        // V048-S1 P0-4：归一带市场前缀的查询——上游 Tencent smartbox 不识别 sh/sz 前缀，需要 strip 后再送
        var trimmedQuery = query.Trim();
        var upstreamQuery = StripMarketPrefix(trimmedQuery);
        var preferredSymbol = trimmedQuery != upstreamQuery ? StockSymbolNormalizer.Normalize(trimmedQuery) : null;

        var url = $"https://smartbox.gtimg.cn/s3/?q={Uri.EscapeDataString(upstreamQuery)}&t=all";
        var bytes = await _httpClient.GetByteArrayAsync(url, cancellationToken);
        var raw = DecodeContent(bytes);
        var payload = ExtractPayload(raw);
        if (!string.IsNullOrWhiteSpace(payload))
        {
            payload = Regex.Unescape(payload);
        }
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Array.Empty<StockSearchResultDto>();
        }

        var results = new List<StockSearchResultDto>();
        var delimiter = payload.Contains('^') ? '^' : ';';
        var items = payload.Split(delimiter, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var item in items)
        {
            var parts = item.Split('~', StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var market = parts[0].Trim();
            var code = parts.Length >= 2 ? parts[1].Trim() : string.Empty;
            var name = parts.Length >= 3 ? parts[2].Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = parts.Length >= 2 ? parts[1].Trim() : parts[0].Trim();
            }

            if (string.IsNullOrWhiteSpace(code) || !CodeRegex.IsMatch(code))
            {
                var codeMatch = CodeRegex.Match(item);
                code = codeMatch.Success ? codeMatch.Groups["code"].Value : string.Empty;
            }

            var symbol = ResolveSymbol(parts, code, market);
            if (string.IsNullOrWhiteSpace(symbol))
            {
                continue;
            }

            results.Add(new StockSearchResultDto(symbol, name, code, market));
            if (results.Count >= limit)
            {
                break;
            }
        }

        // V048-S1 P0-4：当查询带市场前缀时，把完全匹配的 symbol 置顶
        if (preferredSymbol is not null && results.Count > 0)
        {
            var preferred = results
                .Where(r => string.Equals(r.Symbol, preferredSymbol, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (preferred.Count > 0)
            {
                var others = results.Where(r => !string.Equals(r.Symbol, preferredSymbol, StringComparison.OrdinalIgnoreCase));
                results = preferred.Concat(others).ToList();
            }
        }

        return results;
    }

    /// <summary>
    /// V048-S1 P0-4：strip 市场前缀（sh/sz/bj/hk/us.），这样 Tencent smartbox 才能匹配到个股。
    /// </summary>
    private static string StripMarketPrefix(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return query;
        }

        var m = PrefixedSymbolRegex.Match(query);
        if (m.Success)
        {
            return m.Groups["code"].Value;
        }

        var um = UsPrefixedRegex.Match(query);
        if (um.Success)
        {
            return um.Groups["ticker"].Value;
        }

        return query;
    }

    private static string ExtractPayload(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var match = QuoteRegex.Match(raw);
        if (match.Success)
        {
            return match.Groups["payload"].Value;
        }

        var start = raw.IndexOf('"');
        var end = raw.LastIndexOf('"');
        if (start >= 0 && end > start)
        {
            return raw.Substring(start + 1, end - start - 1);
        }

        return string.Empty;
    }

    private static string DecodeContent(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            return Encoding.GetEncoding("GB18030").GetString(bytes);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }

    private static string BuildSymbol(string code, string market)
    {
        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(market))
        {
            return string.Empty;
        }

        var normalizedMarket = market.Trim().ToLowerInvariant();
        if (normalizedMarket.StartsWith("sh") || normalizedMarket.StartsWith("sz"))
        {
            if (normalizedMarket.Length > 2)
            {
                return StockSymbolNormalizer.Normalize(normalizedMarket);
            }

            return StockSymbolNormalizer.Normalize($"{normalizedMarket}{code}");
        }

        return StockSymbolNormalizer.Normalize(code);
    }

    private static string ResolveSymbol(string[] parts, string code, string market)
    {
        foreach (var part in parts)
        {
            var match = SymbolRegex.Match(part);
            if (match.Success)
            {
                return StockSymbolNormalizer.Normalize(match.Groups["symbol"].Value);
            }
        }

        return BuildSymbol(code, market);
    }
}
