using System.Diagnostics;
using Baostock.NET.Client;

Console.WriteLine("=== Baostock.NET Long-Term Stability Test ===");
Console.WriteLine($"Start: {DateTime.Now:HH:mm:ss}");
Console.WriteLine("Running for 3 minutes with varied queries...");
Console.WriteLine();

var totalRequests = 0;
var totalErrors = 0;
var latencies = new List<long>();
var errorDetails = new List<string>();
var sw = new Stopwatch();
var testDuration = TimeSpan.FromMinutes(3);
var startTime = DateTime.UtcNow;

// Track connection health
var reconnections = 0;
BaostockClient? client = null;

async Task<BaostockClient> EnsureConnected()
{
    if (client == null)
    {
        client = await BaostockClient.CreateAndLoginAsync();
        Console.WriteLine($"  [Connected at {DateTime.Now:HH:mm:ss}]");
    }
    return client;
}

// API rotation (skip KLine due to known bug)
var queries = new (string name, Func<BaostockClient, Task<int>> action)[]
{
    ("TradeDates", async c => { int n = 0; await foreach (var r in c.QueryTradeDatesAsync("2024-01-01", "2024-12-31")) { n++; if (n > 5) break; } return n; }),
    ("ProfitData", async c => { int n = 0; await foreach (var r in c.QueryProfitDataAsync("sh.600000", 2023, 4)) { n++; } return n; }),
    ("StockBasic", async c => { int n = 0; await foreach (var r in c.QueryStockBasicAsync("sh.600000", null)) { n++; } return n; }),
    ("Industry", async c => { int n = 0; await foreach (var r in c.QueryStockIndustryAsync("sh.600000", "2024")) { n++; } return n; }),
    ("GrowthData", async c => { int n = 0; await foreach (var r in c.QueryGrowthDataAsync("sh.600000", 2023, 4)) { n++; } return n; }),
    ("OperationData", async c => { int n = 0; await foreach (var r in c.QueryOperationDataAsync("sh.600000", 2023, 4)) { n++; } return n; }),
    ("BalanceData", async c => { int n = 0; await foreach (var r in c.QueryBalanceDataAsync("sh.600000", 2023, 4)) { n++; } return n; }),
    ("CashFlowData", async c => { int n = 0; await foreach (var r in c.QueryCashFlowDataAsync("sh.600000", 2023, 4)) { n++; } return n; }),
    ("DupontData", async c => { int n = 0; await foreach (var r in c.QueryDupontDataAsync("sh.600000", 2023, 4)) { n++; } return n; }),
    ("DividendData", async c => { int n = 0; await foreach (var r in c.QueryDividendDataAsync("sh.600000", "2024", "report")) { n++; } return n; }),
    ("DepositRate", async c => { int n = 0; await foreach (var r in c.QueryDepositRateDataAsync("2020", "2024")) { n++; if (n > 10) break; } return n; }),
    ("LoanRate", async c => { int n = 0; await foreach (var r in c.QueryLoanRateDataAsync("2020", "2024")) { n++; if (n > 10) break; } return n; }),
    ("ReserveRatio", async c => { int n = 0; await foreach (var r in c.QueryRequiredReserveRatioDataAsync("2020", "2024", "0")) { n++; if (n > 10) break; } return n; }),
    ("MoneySupplyMonth", async c => { int n = 0; await foreach (var r in c.QueryMoneySupplyDataMonthAsync("2020", "2024")) { n++; if (n > 10) break; } return n; }),
    ("AdjustFactor", async c => { int n = 0; await foreach (var r in c.QueryAdjustFactorAsync("sh.600000", "2024-01-01", "2024-12-31")) { n++; if (n > 10) break; } return n; }),
};

int queryIndex = 0;
int roundNumber = 0;

try
{
    var c = await EnsureConnected();

    while (DateTime.UtcNow - startTime < testDuration)
    {
        var query = queries[queryIndex % queries.Length];
        queryIndex++;

        if (queryIndex % queries.Length == 0)
        {
            roundNumber++;
            Console.WriteLine($"\n  --- Round {roundNumber} complete ({totalRequests} requests, {totalErrors} errors, avg {(latencies.Count > 0 ? latencies.Average() : 0):F0}ms) ---");
        }

        sw.Restart();
        try
        {
            var rows = await query.action(c);
            sw.Stop();
            totalRequests++;
            latencies.Add(sw.ElapsedMilliseconds);

            // Print every 5th request to avoid flooding
            if (totalRequests % 5 == 0)
            {
                var elapsed = DateTime.UtcNow - startTime;
                Console.WriteLine($"  [{elapsed.Minutes:D2}:{elapsed.Seconds:D2}] {totalRequests} requests OK, last: {query.name}={sw.ElapsedMilliseconds}ms");
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            totalErrors++;
            var msg = $"{query.name}: {ex.GetType().Name}: {ex.Message}";
            errorDetails.Add(msg);
            Console.WriteLine($"  ERROR: {msg}");

            // Try to reconnect if connection might be broken
            if (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                Console.WriteLine($"  [Reconnecting...]");
                try { if (client != null) await client.DisposeAsync(); } catch { }
                client = null;
                reconnections++;
                c = await EnsureConnected();
            }
        }

        // Small delay (200ms) to simulate realistic usage, not pure burst
        await Task.Delay(200);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Fatal: {ex.GetType().Name}: {ex.Message}");
}
finally
{
    if (client != null) await client.DisposeAsync();
}

// Results
Console.WriteLine();
Console.WriteLine("=== STABILITY TEST RESULTS ===");
Console.WriteLine($"Duration: {(DateTime.UtcNow - startTime).TotalMinutes:F1} minutes");
Console.WriteLine($"Total requests: {totalRequests}");
Console.WriteLine($"Total errors: {totalErrors}");
Console.WriteLine($"Reconnections: {reconnections}");
Console.WriteLine($"Success rate: {(totalRequests > 0 ? (double)totalRequests / (totalRequests + totalErrors) * 100 : 0):F1}%");

if (latencies.Count > 0)
{
    var sorted = latencies.OrderBy(x => x).ToList();
    Console.WriteLine($"Latency - Avg: {sorted.Average():F0}ms, Min: {sorted.Min()}ms, Max: {sorted.Max()}ms");
    Console.WriteLine($"Latency - P50: {sorted[sorted.Count / 2]}ms, P90: {sorted[(int)(sorted.Count * 0.9)]}ms, P99: {sorted[(int)(sorted.Count * 0.99)]}ms");
    
    // Check for degradation over time
    var firstQuarter = sorted.Take(sorted.Count / 4).Average();
    var lastQuarter = sorted.Skip(sorted.Count * 3 / 4).Average();
    Console.WriteLine($"Degradation check - First 25%: {firstQuarter:F0}ms, Last 25%: {lastQuarter:F0}ms");
}

if (errorDetails.Count > 0)
{
    Console.WriteLine($"\nError details:");
    foreach (var group in errorDetails.GroupBy(e => e.Split(':')[0]))
    {
        Console.WriteLine($"  {group.Key}: {group.Count()} errors");
        Console.WriteLine($"    Sample: {group.First()}");
    }
}

Console.WriteLine($"\nEnd: {DateTime.Now:HH:mm:ss}");
