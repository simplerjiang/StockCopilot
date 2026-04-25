using Microsoft.Extensions.Options;
using SimplerJiangAiAgent.Api.Modules.Stocks.Models;
using SimplerJiangAiAgent.Api.Modules.Stocks.Services;

namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs;

public sealed class HighFrequencyQuoteService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HighFrequencyQuoteService> _logger;
    private readonly HighFrequencyQuoteOptions _options;

    public HighFrequencyQuoteService(
        IServiceProvider serviceProvider,
        ILogger<HighFrequencyQuoteService> logger,
        IOptions<HighFrequencyQuoteOptions> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncOnceAsync(DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "高频白名单行情同步失败");
            }

            var delay = TimeSpan.FromSeconds(Math.Max(30, _options.IntervalSeconds));
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task<int> SyncOnceAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || !ChinaAStockMarketClock.IsTradingSession(now))
        {
            return 0;
        }

        using var scope = _serviceProvider.CreateScope();
        var watchlistService = scope.ServiceProvider.GetRequiredService<IActiveWatchlistService>();
        var items = await watchlistService.GetEnabledAsync(_options.MaxSymbols, cancellationToken);
        if (items.Count == 0)
        {
            return 0;
        }

        var processed = 0;
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(1, _options.MaxConcurrentSymbols)
        };

        await Parallel.ForEachAsync(items, parallelOptions, async (item, token) =>
        {
            try
            {
                using var itemScope = _serviceProvider.CreateScope();
                var crawler = itemScope.ServiceProvider.GetRequiredService<IStockCrawler>();
                var syncService = itemScope.ServiceProvider.GetRequiredService<IStockSyncService>();
                var itemWatchlistService = itemScope.ServiceProvider.GetRequiredService<IActiveWatchlistService>();

                var symbol = StockSymbolNormalizer.Normalize(item.Symbol);
                var quoteTask = crawler.GetQuoteAsync(symbol, token);
                var minuteTask = crawler.GetMinuteLineAsync(symbol, token);
                var messagesTask = StartMessagesTask(crawler, symbol, token);
                await Task.WhenAll(quoteTask, minuteTask);

                var quote = await quoteTask;
                if (quote is null)
                {
                    return;
                }

                var messages = await GetMessagesSafelyAsync(messagesTask, symbol, token);
                var detail = new StockDetailDto(quote, Array.Empty<KLinePointDto>(), await minuteTask, messages);
                await syncService.SaveDetailAsync(detail, "day", token);
                await itemWatchlistService.MarkSyncedAsync(symbol, quote.Name, now.UtcDateTime, token);
                Interlocked.Increment(ref processed);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "高频白名单同步单只股票失败: {Symbol}", item.Symbol);
            }
        });

        return processed;
    }

    private async Task<IReadOnlyList<IntradayMessageDto>> GetMessagesSafelyAsync(Task<IReadOnlyList<IntradayMessageDto>> messagesTask, string symbol, CancellationToken cancellationToken)
    {
        try
        {
            return await messagesTask;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "高频白名单盘中消息抓取失败，已降级为空结果: {Symbol}", symbol);
            return Array.Empty<IntradayMessageDto>();
        }
    }

    private Task<IReadOnlyList<IntradayMessageDto>> StartMessagesTask(IStockCrawler crawler, string symbol, CancellationToken cancellationToken)
    {
        try
        {
            return crawler.GetIntradayMessagesAsync(symbol, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "高频白名单盘中消息抓取启动失败，已降级为空结果: {Symbol}", symbol);
            return Task.FromResult<IReadOnlyList<IntradayMessageDto>>(Array.Empty<IntradayMessageDto>());
        }
    }
}