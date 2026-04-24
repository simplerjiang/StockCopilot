# Baostock.NET — v1.0.0 Bug 修复清单

> **目标仓库**：https://github.com/simplerjiang/baostock.NET
> **影响版本**：v1.0.0
> **发现时间**：2026-04-23，压测时发现

---

## Bug 复现

```csharp
await using var client = await BaostockClient.CreateAndLoginAsync();
await foreach (var row in client.QueryHistoryKDataPlusAsync(
    "sh.600000", startDate: "2024-01-01", endDate: "2024-01-31"))
{
    Console.WriteLine(row.Close);
}
// 抛出 System.IndexOutOfRangeException
```

## 根因分析

### 问题 1：`ParseKLineRow` 无边界检查（主因）

**文件**：`src/Baostock.NET/Queries/Client.History.cs`

`ParseKLineRow(string[] cols)` 硬编码访问 `cols[0]` 到 `cols[13]`（14 个字段），没有任何边界检查。当 baostock 服务器对某些交易日（如停牌日、数据缺失日）返回的 JSON 数组列数 < 14 时，直接抛出 `IndexOutOfRangeException`。

```csharp
// 当前代码 — 无防护
private static KLineRow ParseKLineRow(string[] cols)
{
    return new KLineRow(
        Date: DateOnly.ParseExact(cols[0], "yyyy-MM-dd", CultureInfo.InvariantCulture),
        Code: cols[1],
        Open: ParseNullableDecimal(cols[2]),
        High: ParseNullableDecimal(cols[3]),
        Low: ParseNullableDecimal(cols[4]),
        Close: ParseNullableDecimal(cols[5]),
        PreClose: ParseNullableDecimal(cols[6]),
        Volume: ParseNullableLong(cols[7]),
        Amount: ParseNullableDecimal(cols[8]),
        AdjustFlag: (AdjustFlag)int.Parse(cols[9], CultureInfo.InvariantCulture),
        Turn: ParseNullableDecimal(cols[10]),
        TradeStatus: (TradeStatus)int.Parse(cols[11], CultureInfo.InvariantCulture),
        PctChg: ParseNullableDecimal(cols[12]),
        IsST: cols[13] == "1");   // ← 如果只有 10 列就爆
}
```

### 问题 2：`isCompressed` 回调可能误判（次因）

**文件**：`src/Baostock.NET/Queries/Client.History.cs`

```csharp
var (header, respBody) = DecodeResponseFrame(responseFrame, header =>
    string.Equals(header.MessageType, MessageTypes.GetKDataPlusResponse, StringComparison.Ordinal));
```

这个回调把**所有**类型为 `GetKDataPlusResponse` 的响应都当压缩帧走 zlib 解压。

但根据 baostock 协议，只有 **MSG=96** 才是压缩帧。如果 `MessageTypes.GetKDataPlusResponse` 不等于 `"96"`，那么非压缩的 K 线响应会被错误地扔进 `FrameCodec.Decompress()`，产生乱码 JSON → 解析出的 `cols` 长度不足 → `IndexOutOfRangeException`。

**需要确认**：`MessageTypes.GetKDataPlusResponse` 的值是否就是 `"96"`。如果不是，这才是真正的根因。

---

## 修复方案

### Fix 1：`ParseKLineRow` 加防御性边界检查（必修）

**方案 A：逐字段检查**

```csharp
private static KLineRow ParseKLineRow(string[] cols)
{
    return new KLineRow(
        Date: DateOnly.ParseExact(cols[0], "yyyy-MM-dd", CultureInfo.InvariantCulture),
        Code: cols.Length > 1 ? cols[1] : string.Empty,
        Open: cols.Length > 2 ? ParseNullableDecimal(cols[2]) : null,
        High: cols.Length > 3 ? ParseNullableDecimal(cols[3]) : null,
        Low: cols.Length > 4 ? ParseNullableDecimal(cols[4]) : null,
        Close: cols.Length > 5 ? ParseNullableDecimal(cols[5]) : null,
        PreClose: cols.Length > 6 ? ParseNullableDecimal(cols[6]) : null,
        Volume: cols.Length > 7 ? ParseNullableLong(cols[7]) : null,
        Amount: cols.Length > 8 ? ParseNullableDecimal(cols[8]) : null,
        AdjustFlag: cols.Length > 9 ? (AdjustFlag)int.Parse(cols[9], CultureInfo.InvariantCulture) : AdjustFlag.NoAdjust,
        Turn: cols.Length > 10 ? ParseNullableDecimal(cols[10]) : null,
        TradeStatus: cols.Length > 11 ? (TradeStatus)int.Parse(cols[11], CultureInfo.InvariantCulture) : TradeStatus.Suspended,
        PctChg: cols.Length > 12 ? ParseNullableDecimal(cols[12]) : null,
        IsST: cols.Length > 13 && cols[13] == "1");
}
```

**方案 B：辅助方法（更简洁）**

```csharp
// 添加辅助方法
private static string SafeCol(string[] cols, int i)
    => i < cols.Length ? cols[i] : string.Empty;

private static KLineRow ParseKLineRow(string[] cols)
{
    return new KLineRow(
        Date: DateOnly.ParseExact(SafeCol(cols, 0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
        Code: SafeCol(cols, 1),
        Open: ParseNullableDecimal(SafeCol(cols, 2)),
        High: ParseNullableDecimal(SafeCol(cols, 3)),
        Low: ParseNullableDecimal(SafeCol(cols, 4)),
        Close: ParseNullableDecimal(SafeCol(cols, 5)),
        PreClose: ParseNullableDecimal(SafeCol(cols, 6)),
        Volume: ParseNullableLong(SafeCol(cols, 7)),
        Amount: ParseNullableDecimal(SafeCol(cols, 8)),
        AdjustFlag: SafeCol(cols, 9) is { Length: > 0 } af ? (AdjustFlag)int.Parse(af, CultureInfo.InvariantCulture) : AdjustFlag.NoAdjust,
        Turn: ParseNullableDecimal(SafeCol(cols, 10)),
        TradeStatus: SafeCol(cols, 11) is { Length: > 0 } ts ? (TradeStatus)int.Parse(ts, CultureInfo.InvariantCulture) : TradeStatus.Suspended,
        PctChg: ParseNullableDecimal(SafeCol(cols, 12)),
        IsST: SafeCol(cols, 13) == "1");
}
```

### Fix 2：`ParseMinuteKLineRow` 同样加边界检查（必修）

同文件的 `ParseMinuteKLineRow` 也直接访问 `cols[0]~cols[9]`，需要同样处理。

### Fix 3：确认 `isCompressed` 逻辑（建议排查）

**检查步骤**：

1. 打开 `src/Baostock.NET/Protocol/Framing.cs`（或 `MessageTypes` 所在文件）
2. 确认 `MessageTypes.GetKDataPlusResponse` 的值
3. 确认是否等于 `"96"`（baostock 压缩帧标识）

**如果不等于 "96"**，则需要修改 `isCompressed` 回调：

```csharp
// 修改前（可能有 bug）：
var (header, respBody) = DecodeResponseFrame(responseFrame, header =>
    string.Equals(header.MessageType, MessageTypes.GetKDataPlusResponse, StringComparison.Ordinal));

// 修改后：只有 MSG=96 才解压
var (header, respBody) = DecodeResponseFrame(responseFrame, header =>
    string.Equals(header.MessageType, MessageTypes.CompressedResponse, StringComparison.Ordinal));
```

**如果等于 "96"**，则说明 `isCompressed` 逻辑没问题，Bug 纯粹由问题 1（无边界检查）导致。

---

## 测试验证

修复后运行以下测试确认：

```csharp
// 1. 正常查询（应不再抛异常）
await foreach (var row in client.QueryHistoryKDataPlusAsync(
    "sh.600000", startDate: "2024-01-01", endDate: "2024-01-31"))
{
    Console.WriteLine($"{row.Date} {row.Close}");
}

// 2. 大范围查询（触发分页 + 压缩）
await foreach (var row in client.QueryHistoryKDataPlusAsync(
    "sh.600000", startDate: "2020-01-01", endDate: "2024-12-31"))
{
    // 验证所有行解析正常
}

// 3. 分钟级查询
await foreach (var row in client.QueryHistoryKDataPlusMinuteAsync(
    "sh.600000", startDate: "2024-01-15", endDate: "2024-01-15"))
{
    Console.WriteLine($"{row.Date} {row.Time} {row.Close}");
}

// 4. 停牌股查询（可能返回空行或不完整行）
await foreach (var row in client.QueryHistoryKDataPlusAsync(
    "sh.600000", startDate: "2019-01-01", endDate: "2019-01-31"))
{
    // 验证停牌日数据不崩溃
}
```

---

## Bug 2：宏观经济 API 日期格式不一致

### 复现

```csharp
// 以下调用抛出 BaostockException: "开始日期格式不正确"
await foreach (var r in client.QueryDepositRateDataAsync("2020", "2024")) { }
await foreach (var r in client.QueryLoanRateDataAsync("2020", "2024")) { }
await foreach (var r in client.QueryRequiredReserveRatioDataAsync("2020", "2024")) { }
await foreach (var r in client.QueryMoneySupplyDataMonthAsync("2020", "2024")) { }
```

### 问题

宏观经济 5 个 API 的日期参数需要 `yyyy-MM-dd` 格式（如 `"2020-01-01"`），但：
1. **API 签名没有明确标注格式要求**，用户容易传 `"2020"` 或 `"2020-01"` 导致服务端拒绝
2. Python 版 baostock 对这些 API 也是 `yyyy-MM-dd` 格式，但 Python 版文档有说明

### 影响 API

| 方法 | 参数 | 需要格式 |
|------|------|--------|
| `QueryDepositRateDataAsync(startDate, endDate)` | startDate, endDate | `yyyy-MM-dd` |
| `QueryLoanRateDataAsync(startDate, endDate)` | startDate, endDate | `yyyy-MM-dd` |
| `QueryRequiredReserveRatioDataAsync(startDate, endDate)` | startDate, endDate | `yyyy-MM-dd` |
| `QueryMoneySupplyDataMonthAsync(startDate, endDate)` | startDate, endDate | `yyyy-MM-dd` |
| `QueryMoneySupplyDataYearAsync(startDate, endDate)` | startDate, endDate | `yyyy-MM-dd` |

### 修复方案

**方案 A：参数验证 + 自动补全（推荐）**

在每个宏观 API 方法开头加日期格式校验和自动补全逻辑：

```csharp
private static string NormalizeDateParam(string? date, string defaultValue)
{
    if (string.IsNullOrEmpty(date)) return defaultValue;
    
    // 支持 "2020" → "2020-01-01"
    if (date.Length == 4 && int.TryParse(date, out _))
        return $"{date}-01-01";
    
    // 支持 "2020-01" → "2020-01-01"  
    if (date.Length == 7 && date[4] == '-')
        return $"{date}-01";
    
    // 已经是 yyyy-MM-dd 格式
    if (date.Length == 10 && date[4] == '-' && date[7] == '-')
        return date;
    
    throw new ArgumentException($"日期格式不正确：'{date}'，需要 yyyy-MM-dd 格式", nameof(date));
}
```

然后在各宏观 API 方法中调用：
```csharp
startDate = NormalizeDateParam(startDate, "2015-01-01");
endDate = NormalizeDateParam(endDate, DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
```

**方案 B：仅改 XML 文档注释（最小改动）**

给每个参数加明确格式说明：
```csharp
/// <param name="startDate">开始日期，格式必须为 <c>"yyyy-MM-dd"</c>，如 <c>"2020-01-01"</c>。</param>
/// <param name="endDate">结束日期，格式必须为 <c>"yyyy-MM-dd"</c>，如 <c>"2024-12-31"</c>。</param>
```

**建议**：方案 A + B 都做。自动补全提升易用性，文档注释提供 IntelliSense 提示。

---

## Bug 3：QueryTradeDatesAsync 日期格式拒绝

### 复现

```csharp
// 以下调用被服务端拒绝："开始日期格式不正确"
await foreach (var r in client.QueryTradeDatesAsync("2024")) { }
await foreach (var r in client.QueryTradeDatesAsync("2025")) { }
```

### 问题

`QueryTradeDatesAsync` 接收 `year` 参数（字符串），但服务端实际需要完整日期格式。需要确认 Python 版 baostock 的 `query_trade_dates(start_date, end_date)` 参数格式，然后对齐 .NET 版实现。

### 修复方案

确认 Python 版参数后，统一处理。可能需要修改方法签名或内部日期转换。

---

## 数据质量备注：3 个宏观 API 返回空数据

### 现象

以下 3 个 API 使用 `"2020-01-01"` ~ `"2024-12-31"` 调用成功（无异常），但返回 **0 条记录**：
- `QueryDepositRateDataAsync` — 存款利率
- `QueryLoanRateDataAsync` — 贷款利率  
- `QueryRequiredReserveRatioDataAsync` — 存款准备金率

而货币供应量 API 正常返回数据（月度 59 条，年度 4 条）。

### 可能原因

1. **baostock 服务端停更**：这 3 个宏观 API 可能在某个时间点后停止更新数据
2. **日期范围不匹配**：可能需要更早的日期范围（如 2010-2020）
3. **服务端维护中**：临时性问题

### 建议

1. 用更大的日期范围测试（如 `"2010-01-01"` ~ `"2024-12-31"`）
2. 对照 Python 版 baostock 测试相同 API 是否也返回空
3. 如果确认 baostock 不再提供这些数据，v0.5.1 的宏观数据需要寻找替代数据源（如央行官网 API、Wind 等）

---

## 数据质量测试全景（2026-04-23）

| API | 状态 | 数据量 | 备注 |
|-----|------|-------|------|
| QueryTradeDatesAsync | ❌ FAIL | — | 日期格式拒绝（Bug 3） |
| QueryAllStockAsync | ✅ | 5657 | 合理 |
| QueryStockBasicAsync | ✅ | 2 条 | 浦发银行/平安银行正确 |
| QueryStockIndustryAsync | ✅ | 1 条 | 证监会分类正确 |
| QueryHistoryKDataPlusAsync | ✅* | 22/19 行 | 本次通过，但有间歇性 Bug |
| QueryProfitDataAsync | ✅ | 2 条 | ROE/净利润正确 |
| QueryOperationDataAsync | ✅ | 1 条 | 正常 |
| QueryGrowthDataAsync | ✅ | 1 条 | 正常 |
| QueryDupontDataAsync | ✅ | 1 条 | ROE=0.0516 |
| QueryBalanceDataAsync | ✅ | 1 条 | 负债率=0.9186 |
| QueryCashFlowDataAsync | ✅ | 1 条 | 正常 |
| QueryDividendDataAsync | ✅ | 1-2 条 | 正常 |
| QueryAdjustFactorAsync | ✅ | 1 条 | 正常 |
| QueryPerformanceExpressReportAsync | ✅ | 4 条 | 正常 |
| QueryForecastReportAsync | ✅ | 0 条 | 浦发无预告（正常） |
| QueryDepositRateDataAsync | ⚠️ | 0 条 | 无异常但无数据 |
| QueryLoanRateDataAsync | ⚠️ | 0 条 | 无异常但无数据 |
| QueryRequiredReserveRatioDataAsync | ⚠️ | 0 条 | 无异常但无数据 |
| QueryMoneySupplyDataMonthAsync | ✅ | 59 条 | 正常 |
| QueryMoneySupplyDataYearAsync | ✅ | 4 条 | 正常 |
| QueryHs300StocksAsync | ✅ | 300 | 精确 |
| QuerySz50StocksAsync | ✅ | 50 | 精确 |
| QueryZz500StocksAsync | ✅ | 500 | 精确 |
| QueryStStocksAsync | ✅ | 81 | 合理 |
| QueryStarStStocksAsync | ✅ | 70 | 合理 |
| QuerySuspendedStocksAsync | ✅ | 0 | 该日期无暂停股（正常） |
| QueryTerminatedStocksAsync | ✅ | 246 | 合理 |

---

## 影响范围总结

| Bug | 方法 | 严重程度 | 修复优先级 |
|-----|------|---------|-----------|
| Bug 1 | `QueryHistoryKDataPlusAsync` | 🔴 间歇性崩溃 | P0 |
| Bug 1 | `QueryHistoryKDataPlusMinuteAsync` | 🟠 可能崩溃 | P0 |
| Bug 2 | 5 个宏观 API 日期格式 | 🟡 易用性 | P1 |
| Bug 3 | `QueryTradeDatesAsync` 日期格式 | 🔴 功能不可用 | P0 |
| 数据 | DepositRate/LoanRate/ReserveRatio | ⚠️ 返回空数据 | 需排查 |
| — | 其他 22 个 API | ✅ 正常 | — |
