# P0-Pre MCP 补强计划

> **创建时间**: 2026-03-28
> **PM**: PM Agent
> **来源**: P0-Pre MCP 审计报告中的 5 项待补强内容
> **优先级**: 按开发难度和价值排序执行

---

## 执行优先级

| # | 补强项 | 难度 | 价值 | 优先级 |
|---|-------|------|------|--------|
| E1 | mainBusiness/stockSectorName 为 null | 低 | 高 | P0 |
| E2 | MarketContextMcp 接入市场指数/资金/广度 | 中 | 高 | P0 |
| E3 | StockProductMcp 补充主营构成数据 | 中 | 中 | P1 |
| E4 | SocialSentimentMcp 真实社媒源评估 | — | 低(短期) | P2(仅评估) |

---

## E1: mainBusiness / stockSectorName 修复

### 问题分析
1. **mainBusiness 为 null**: `EastmoneyCompanyProfileParser` 已有 `zyyw → 主营业务` 映射（line 13），但实测返回 null。
   - 可能原因：上游 Eastmoney CompanySurvey API 的 `zyyw` 字段对某些股票返回空值或格式异常。
   - 需要先诊断：直接调 CompanySurvey API 看 sh600519 的 zyyw 实际返回值。
   
2. **stockSectorName 为 null**: `StockMarketContextService` line 38-48 查询 `StockQuoteSnapshots.SectorName` 再 fallback 到 `StockCompanyProfiles.SectorName`。
   - 可能原因：Quote API f10 字段未返回（行业名需 f100）；Profile SectorName 未正确持久化。
   - `EastmoneyStockParser.ParseQuote` 用 `f10` 获取 SectorName，但 Eastmoney quote API 的 f10 可能是换手率而非行业。
   - `EastmoneyCompanyProfileParser.Parse` 正确从 `sshy` 字段提取 SectorName。

### 开发任务
- [ ] **E1-D1**: 诊断 - 直接调用 Eastmoney CompanySurvey API，检查 `zyyw` 字段返回值
- [ ] **E1-D2**: 诊断 - 检查 `EastmoneyStockParser.ParseQuote` 的 f10 字段含义，确认是否为行业名
- [ ] **E1-D3**: 修复 - 如 f10 不是行业字段，改用正确的字段（如 f100）或确保 Profile SectorName 被正确传播到 QuoteSnapshot
- [ ] **E1-D4**: 修复 - 如 zyyw 为空，考虑从 jyfw(经营范围) 提取摘要作为 fallback
- [ ] **E1-T1**: 验证 - 复测 CompanyOverviewMcp 和 StockProductMcp，确认 mainBusiness 和 sectorName 有值

---

## E2: MarketContextMcp 接入市场实时概览

### 问题分析
当前 `MarketContextMcp` 仅从 `StockMarketContextService` 获取板块轮动信息（stage + mainline sector），缺少核心市场数据：
- 三大指数行情（上证/深证/创业板）
- 主力资金净流入
- 北向资金流入
- 涨跌分布/涨停跌停

而 `RealtimeMarketOverviewService.GetOverviewAsync()` 已经提供了这些数据，只是没有接入到 MCP 端点。

### 开发任务
- [ ] **E2-D1**: 扩展 `StockCopilotMarketContextDataDto` record，增加可选的 Overview 字段
- [ ] **E2-D2**: 在 `StockCopilotMcpService.GetMarketContextAsync` 中注入 `IRealtimeMarketOverviewService`，调用 GetOverviewAsync
- [ ] **E2-D3**: 将 overview 数据（指数、资金、广度）映射到 DTO 和 evidence
- [ ] **E2-D4**: 更新 features 和 evidence 构建方法
- [ ] **E2-T1**: 单元测试
- [ ] **E2-T2**: 实测验证 MarketContextMcp 返回完整市场数据

### 关键接口
```
IRealtimeMarketOverviewService.GetOverviewAsync() → MarketRealtimeOverviewDto
  ├── Indices: IReadOnlyList<BatchStockQuoteDto>  // 三大指数
  ├── MainCapitalFlow: MarketCapitalFlowSnapshotDto?  // 主力资金
  ├── NorthboundFlow: NorthboundFlowSnapshotDto?  // 北向资金
  └── Breadth: MarketBreadthDistributionDto?  // 涨跌分布
```

---

## E3: StockProductMcp 补充主营构成

### 问题分析
当前只有 4 条基础 fact（经营范围、行业、证监会行业、地区），缺少：
- 按产品线拆分的主营收入构成（如茅台酒 xx%、系列酒 xx%）
- 按地区拆分的收入结构

Eastmoney CompanySurvey API 可能在 `zyfw` 子节点包含主营构成数据，但目前未解析。

### 开发任务
- [ ] **E3-D1**: 诊断 - 检查 CompanySurvey response 中是否有 zyfw / zygc / zycp 等主营构成数据
- [ ] **E3-D2**: 如有 - 扩展 EastmoneyCompanyProfileParser 解析主营构成，生成新的 fact 条目
- [ ] **E3-D3**: 如无 - 考虑用 Eastmoney 另一个 API (如营收构成接口) 获取
- [ ] **E3-T1**: 实测验证 StockProductMcp 返回主营构成数据

---

## E4: SocialSentimentMcp 真实社媒源评估

### 评估结论
- Eastmoney 股吧(guba) API 需要新建 crawler + parser + 存储
- 工作量较大（约2-3天），属 P2 优先级
- 当前降级模式已合理运行（local news + market proxy），不阻断 R1-R7
- **决定**: 仅记录为 backlog，不在本轮执行

---

## 执行顺序
1. E1 (诊断 + 修复) → 最快见效的 bug fix
2. E2 (MarketContext 扩展) → 价值最高的功能增强
3. E3 (Product 主营构成) → 数据源可用性未确认，需先诊断
4. E4 → 仅评估，记录 backlog
