# v0.3.2 散户热度反向指标 (Retail Heat Contrarian Index)

> **版本**: v0.3.2  
> **创建日期**: 2026-04-13  
> **任务级别**: L（新功能、跨模块改动）  
> **状态**: PLANNING  

## 1. 背景与动机

当前系统的 `SocialSentimentMcp` 运行在**降级模式**——仅聚合本地新闻（利好/利空/中性）计数，没有任何真实社交媒体数据采集。系统明确标注：

> "SocialSentimentMcp v1 是本地情绪相关证据聚合工具，仅汇总本地新闻与市场代理快照，不会自行给出社交情绪结论。"

这导致社交情绪数据不可靠，研究报告中的情绪判断缺乏真实数据支撑。

## 2. 核心思路

基于散户行为的经典反向指标理论：

- **论坛帖子量高** → 散户热情火热 → **卖出信号**（过热预警）
- **论坛帖子量低** → 散户参与度低 → **买入信号**（排除垃圾股前提下）

采集中国主流 A 股社区的每日帖子数量，计算相对于历史均值的偏离程度，生成量化反向指标。

## 3. 数据源

### 3.1 主力平台（必须）

| 平台 | URL 模式 | 数据特点 |
|------|----------|----------|
| 东方财富股吧 | `guba.eastmoney.com/list,{code}.html` | 每股独立股吧，帖子量结构化最好 |
| 雪球 | `xueqiu.com/S/{symbol}` | 用户质量较高，反爬较严 |
| 同花顺社区 | `t.10jqka.com.cn/guba_{code}/` | 系统已集成 10jqka 财务数据 |

### 3.2 补充平台（尽量加）

| 平台 | URL 模式 | 备注 |
|------|----------|------|
| 淘股吧 | `www.taoguba.com.cn` | 活跃的交易社区 |
| 新浪股吧 | `guba.sina.com.cn` | 老牌论坛 |
| 百度股吧 | `guba.baidu.com` | 流量大 |

### 3.3 数据源可行性验证（Phase 0 — 最高优先级）

**在正式开发前，必须逐一验证每个平台的爬虫可行性**：

1. 对每个平台，用 HttpClient 请求目标页面
2. 确认能否拿到帖子数量（直接 API 或 HTML 解析）
3. 评估反爬强度（验证码、IP 限制、频率限制）
4. 记录每个平台的可行性结论
5. 根据结果确定最终纳入的平台列表

## 4. 技术方案

### 4.1 采集规格

| 参数 | 值 |
|------|-----|
| 采集范围 | ActiveWatchlist 中的股票 |
| 采集粒度 | 每日帖子总数（不采集帖子内容） |
| 采集频率 | 每天 3 次：开盘前 ~9:00、午盘 ~12:00、收盘后 ~15:30 |
| 请求间隔 | 每次请求间隔 3-5 秒（反爬安全） |
| 架构 | 集成到现有 FinancialWorker（BackgroundService） |

### 4.2 数据模型

```csharp
// 新实体：论坛帖子计数
public class ForumPostCount
{
    public int Id { get; set; }
    public string Symbol { get; set; }         // 股票代码 e.g. "600519"
    public string Platform { get; set; }        // "eastmoney" | "xueqiu" | "10jqka" | ...
    public DateOnly TradingDate { get; set; }   // 交易日
    public string SessionPhase { get; set; }    // "pre_market" | "noon" | "post_market"
    public int PostCount { get; set; }          // 帖子数量
    public DateTime CollectedAt { get; set; }   // 采集时间 UTC
}

// 计算结果：散户热度指数
public class RetailHeatIndex
{
    public int Id { get; set; }
    public string Symbol { get; set; }
    public DateOnly TradingDate { get; set; }
    public double DailyPostCount { get; set; }   // 当日所有平台帖子总量
    public double Ma20PostCount { get; set; }     // 20日均值
    public double HeatRatio { get; set; }         // DailyPostCount / Ma20PostCount
    public string Signal { get; set; }            // "hot" | "warm" | "normal" | "cool" | "cold"
    public int PlatformCount { get; set; }        // 有数据的平台数量
    public DateTime CalculatedAt { get; set; }
}
```

### 4.3 指标计算逻辑

```
HeatRatio = 当日帖子总量 / 20日移动平均

信号映射：
  HeatRatio >= 3.0  → "hot"    (极度过热，强卖出信号)
  HeatRatio >= 2.0  → "warm"   (偏热，注意风险)
  HeatRatio >= 0.7  → "normal" (正常区间)
  HeatRatio >= 0.5  → "cool"   (偏冷，可能是买入机会)
  HeatRatio <  0.5  → "cold"   (极度冷清，强买入信号，需排除垃圾股)
```

### 4.4 爬虫架构

```
FinancialWorker
├── Services/
│   ├── ForumScraping/
│   │   ├── IForumPostCountScraper.cs        # 接口
│   │   ├── EastmoneyGubaScraper.cs          # 东方财富股吧
│   │   ├── XueqiuScraper.cs                # 雪球
│   │   ├── ThsGubaScraper.cs               # 同花顺社区
│   │   ├── TaogubaGubaScraper.cs           # 淘股吧
│   │   ├── SinaGubaScraper.cs              # 新浪股吧
│   │   ├── BaiduGubaScraper.cs             # 百度股吧
│   │   └── ForumScrapingOrchestrator.cs    # 编排器
│   └── RetailHeatIndexService.cs            # 指标计算服务
```

### 4.5 与现有系统集成

**A) 填充 SocialSentimentMcp 降级模式**

在 `StockCopilotMcpService.GetSocialSentimentAsync()` 中：
- 如果有 ForumPostCount 数据，从降级模式切换到正常模式
- 将 HeatRatio 和 Signal 注入 SocialSentimentDataDto
- 研究报告自动引用真实论坛数据

**B) K线图热度子图**

在 K 线图下方新增独立子图区域：
- X 轴与 K 线对齐（交易日）
- Y 轴为 HeatRatio 值
- 柱状图展示，颜色编码：红色=hot/warm，灰色=normal，绿色=cool/cold
- 叠加 20日均线参考线（HeatRatio = 1.0 处）

### 4.6 API 设计

```
GET /api/stocks/{symbol}/retail-heat
  ?from=2026-03-01&to=2026-04-13
  Response: {
    symbol: "600519",
    data: [
      { date: "2026-04-13", dailyCount: 1520, ma20: 800, heatRatio: 1.9, signal: "warm", platforms: 4 },
      ...
    ],
    latestSignal: "warm",
    description: "散户热度偏高，注意追高风险"
  }
```

## 5. Story 拆解与执行计划

### Phase 0: 爬虫可行性验证 (Spike)

| Story | 验收标准 |
|-------|---------|
| S0.1 东方财富股吧 Spike | 能成功获取任意一只股票的帖子列表页，解析出帖子数量 |
| S0.2 雪球 Spike | 同上，评估反爬强度 |
| S0.3 同花顺社区 Spike | 同上 |
| S0.4 淘股吧 Spike | 同上 |
| S0.5 新浪股吧 Spike | 同上 |
| S0.6 百度股吧 Spike | 同上 |
| S0.7 Spike 总结 | 出具可行性报告，确定最终平台列表 |

### Phase 1: 数据基础设施 (S1-S2)

| Story | 依赖 | 验收标准 |
|-------|------|---------|
| S1 数据模型 + 迁移 | Phase 0 | ForumPostCount + RetailHeatIndex 表创建成功，测试通过 |
| S2 爬虫引擎 | S1 | 所有可行平台 Scraper 集成到 FinancialWorker，定时采集 ActiveWatchlist 股票 |

### Phase 2: 指标计算 + 后端集成 (S3-S4)

| Story | 依赖 | 验收标准 |
|-------|------|---------|
| S3 指标计算服务 + API | S2 | RetailHeatIndexService 计算正确，API 返回时序数据 |
| S4 SocialSentiment 集成 | S3 | SocialSentimentMcp 从降级模式切换到使用真实论坛数据 |

### Phase 3: 前端展示 (S5)

| Story | 依赖 | 验收标准 |
|-------|------|---------|
| S5 K线图热度子图 | S3 | K线图下方新增热度柱状图，红/灰/绿编码，与日期联动 |

### Phase 4: 历史回填 + 收尾 (S6)

| Story | 依赖 | 验收标准 |
|-------|------|---------|
| S6 历史回填 | S2 | 支持一键回填 30-60 天数据，回填完成后指标立即可用 |

## 6. 执行流程 (L级)

```
Phase 0: Spike 验证
  └→ Dev Agent 逐个验证 → Test Agent 确认结果 → PM 出具总结
Phase 1-2: 后端闭环
  └→ Dev Agent 开发 → Test Agent 单元测试 + 数据验证
Phase 3: 前端
  └→ Dev Agent 开发 → Test Agent 验证 → UI Designer 走查
Phase 4: 回填 + 验收
  └→ Dev Agent 开发 → Test Agent 验证
       → User Rep 两轮验收
       → PM 写报告
```

## 7. 技术风险与应对

| 风险 | 概率 | 应对 |
|------|------|------|
| 反爬机制阻断 | 高 | User-Agent 轮换、请求限速 3-5s、失败降级不中断 |
| HTML 结构变化 | 中 | 多种解析策略 fallback、监控解析成功率 |
| 部分平台完全不可行 | 中 | Phase 0 提前排除，至少确保 1-2 个平台可用 |
| 雪球反爬特别严格 | 高 | 优先做东方财富（最开放），雪球作为 nice-to-have |
| 自选股过多导致采集超时 | 低 | 限制并发、按优先级采集、支持部分采集 |

## 8. 合规说明

- 仅供个人量化研究使用，不对外提供数据服务
- 严格控制请求频率（3-5 秒间隔），不给目标站点造成压力
- 只采集公开可见的帖子数量统计，不采集用户隐私数据
- 不绕过登录墙或验证码
