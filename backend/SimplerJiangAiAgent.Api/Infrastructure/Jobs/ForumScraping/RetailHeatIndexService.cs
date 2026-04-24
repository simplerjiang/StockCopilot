using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SimplerJiangAiAgent.Api.Data;

namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs.ForumScraping;

public interface IRetailHeatIndexService
{
    Task<RetailHeatTimeSeriesDto> GetTimeSeriesAsync(string symbol, DateOnly? from, DateOnly? to, CancellationToken ct);
}

public sealed class RetailHeatIndexService : IRetailHeatIndexService
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<RetailHeatIndexService> _logger;

    public RetailHeatIndexService(AppDbContext dbContext, ILogger<RetailHeatIndexService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<RetailHeatTimeSeriesDto> GetTimeSeriesAsync(
        string symbol, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var normalizedSymbol = NormalizeSymbol(symbol);

        var endDate = to ?? DateOnly.FromDateTime(DateTime.Today);
        var startDate = from ?? endDate.AddDays(-90);

        // 扩展查询范围：需要额外前置数据来计算 delta 和 MA20
        var extendedStart = startDate.AddDays(-30);

        var rawRecords = await QueryRawPostCountsAsync(normalizedSymbol, extendedStart, endDate, ct);

        if (rawRecords.Count == 0)
        {
            return new RetailHeatTimeSeriesDto(
                normalizedSymbol,
                Array.Empty<RetailHeatDataPointDto>(),
                null,
                "暂无散户论坛数据");
        }

        // 按平台分组，每天取最新一条（按 CollectedAt DESC 已去重），计算相邻日增量
        var platformGroups = rawRecords
            .GroupBy(r => r.Platform)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.TradingDate).ToList());

        var dailyDeltas = new SortedDictionary<string, (int DeltaSum, int PlatformCount)>();

        foreach (var (platform, records) in platformGroups)
        {
            for (var i = 1; i < records.Count; i++)
            {
                var delta = records[i].PostCount - records[i - 1].PostCount;
                if (delta < 0) delta = 0; // 异常重置时取 0

                var date = records[i].TradingDate;
                if (dailyDeltas.TryGetValue(date, out var existing))
                    dailyDeltas[date] = (existing.DeltaSum + delta, existing.PlatformCount + 1);
                else
                    dailyDeltas[date] = (delta, 1);
            }
        }

        // 汇总各平台原始 PostCount 用于直接展示
        var datesWithData = new HashSet<string>();
        var dailyPostCounts = new Dictionary<string, int>();
        foreach (var row in rawRecords)
        {
            datesWithData.Add(row.TradingDate);
            if (dailyPostCounts.TryGetValue(row.TradingDate, out var existing))
                dailyPostCounts[row.TradingDate] = existing + row.PostCount;
            else
                dailyPostCounts[row.TradingDate] = row.PostCount;
        }

        // 补零：确保从 extendedStart 到 endDate 之间每个工作日都有数据点
        // 包含 extendedStart 范围以保证 MA20 窗口完整
        {
            var current = extendedStart;
            while (current <= endDate)
            {
                if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
                {
                    var dateStr = current.ToString("yyyy-MM-dd");
                    if (!dailyDeltas.ContainsKey(dateStr))
                    {
                        dailyDeltas[dateStr] = (0, 0);
                    }
                }
                current = current.AddDays(1);
            }
        }

        if (dailyDeltas.Count == 0)
        {
            return new RetailHeatTimeSeriesDto(
                normalizedSymbol,
                Array.Empty<RetailHeatDataPointDto>(),
                null,
                "暂无散户论坛数据");
        }

        // 计算 MA20 和 HeatRatio
        var allDates = dailyDeltas.Keys.ToList();
        var dataPoints = new List<RetailHeatDataPointDto>();
        var fromStr = startDate.ToString("yyyy-MM-dd");

        for (var i = 0; i < allDates.Count; i++)
        {
            var date = allDates[i];
            var (deltaSum, platformCount) = dailyDeltas[date];

            // MA20 窗口
            var windowStart = Math.Max(0, i - 19);
            var ma20Sum = 0.0;
            var windowCount = 0;
            for (var j = windowStart; j <= i; j++)
            {
                ma20Sum += dailyDeltas[allDates[j]].DeltaSum;
                windowCount++;
            }
            var ma20 = windowCount > 0 ? ma20Sum / windowCount : 0;

            var heatRatio = ma20 > 0 ? deltaSum / ma20 : 0;
            var signal = ClassifySignal(heatRatio);

            // 只输出请求范围内的数据
            if (string.Compare(date, fromStr, StringComparison.Ordinal) >= 0)
            {
                dataPoints.Add(new RetailHeatDataPointDto(
                    date, deltaSum, Math.Round(ma20, 1), Math.Round(heatRatio, 2), signal, platformCount,
                    dailyPostCounts.GetValueOrDefault(date, 0),
                    datesWithData.Contains(date)));
            }
        }

        var latest = dataPoints.Count > 0 ? dataPoints[^1] : null;
        var description = latest is not null
            ? GetSignalDescription(latest.Signal)
            : "暂无散户论坛数据";

        return new RetailHeatTimeSeriesDto(normalizedSymbol, dataPoints, latest, description);
    }

    /// <summary>
    /// 查询原始 PostCount 数据，每个 Platform+TradingDate 只取最新一条记录。
    /// </summary>
    private async Task<List<RawPostCountRow>> QueryRawPostCountsAsync(
        string symbol, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var fromStr = from.ToString("yyyy-MM-dd");
        var toStr = to.ToString("yyyy-MM-dd");

        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Platform, TradingDate, PostCount " +
            "FROM ForumPostCounts " +
            "WHERE Symbol = @symbol AND TradingDate >= @from AND TradingDate <= @to " +
            "ORDER BY Platform, TradingDate, CollectedAt DESC";

        AddParameter(command, "@symbol", symbol);
        AddParameter(command, "@from", fromStr);
        AddParameter(command, "@to", toStr);

        var rows = new List<RawPostCountRow>();
        var seenKeys = new HashSet<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var platform = reader.GetString(0);
            var tradingDate = reader.GetString(1);
            var key = $"{platform}|{tradingDate}";
            // 每个 Platform+TradingDate 只取第一条（CollectedAt DESC => 最新）
            if (seenKeys.Add(key))
            {
                rows.Add(new RawPostCountRow(platform, tradingDate, reader.GetInt32(2)));
            }
        }

        return rows;
    }

    private static void AddParameter(DbCommand command, string name, string value)
    {
        var param = command.CreateParameter();
        param.ParameterName = name;
        param.Value = value;
        command.Parameters.Add(param);
    }

    private static string NormalizeSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return symbol;

        var s = symbol.Trim();
        // 去掉 sh/sz/SH/SZ 前缀
        if (s.Length > 2 &&
            (s.StartsWith("sh", StringComparison.OrdinalIgnoreCase) ||
             s.StartsWith("sz", StringComparison.OrdinalIgnoreCase)))
        {
            var rest = s[2..];
            if (rest.All(char.IsDigit))
                return rest;
        }

        return s;
    }

    private static string ClassifySignal(double heatRatio) => heatRatio switch
    {
        >= 3.0 => "hot",
        >= 2.0 => "warm",
        >= 0.7 => "normal",
        >= 0.5 => "cool",
        _ => "cold"
    };

    private static string GetSignalDescription(string signal) => signal switch
    {
        "hot" => "散户极度过热，强卖出信号",
        "warm" => "散户热度偏高，注意追高风险",
        "normal" => "散户热度正常",
        "cool" => "散户热度偏低，可能是买入机会",
        "cold" => "散户极度冷清，强买入信号（需排除垃圾股）",
        _ => "暂无散户论坛数据"
    };

    private sealed record RawPostCountRow(string Platform, string TradingDate, int PostCount);
}
