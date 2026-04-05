# 上市公司财报信息采集功能
** 这是一篇人写的计划书，后续要Agent重构完整需求 **

我上个星期从别的地方（别人的需求）获得了启发，我们目前对于上市公司的数据主要来源于，东方财富网的接口实时获取，这非常受限，因为等于二道贩子消息，并且接口稳定性难以保证。
所以我希望我们可以单独做一个独立的Job定时工具，负责爬取上市公司数据（包括但不限于：季度年度财报，公告，增发配股，股票基本信息，高管及相关人员持股变动，股票分红信息等等）这样我们的信息更加全面。
别人的需求原文：
```
1、第一数据源：
巨潮资讯网和https://www.sec.gov/edgar/search/
（所有公司的官方公告数据采集到数据库，不同报告数据有交叉重叠的以最新一期报告的为准，同一报告出现更正修订的以更正后的为准，官方公告缺失的数据从以下数据源补齐）

2、第二数据源：
萝卜投研——券商研报——一致预期——最新预测
萝卜投研——股票行情（最新价、主营构成）
同花顺——个股——价值分析——业绩预测详表
https://robo.datayes.com/v1.5/datacenter/datareport
沪深-市场行情-沪深股票日行情：今收盘、成交金额(元)、流通市值(元)、总市值(元)
沪深-市场行情-交易市场日度成交概况：市价总值、成交金额——股票（上海、深圳、北京）
沪深-市场行情-行业估值信息：全部
沪深-市场行情-AH股比价：AH溢价
一级市场-增发配股-增发：增发类型、股权登记日、每股发行价、发行量、募集资金总额、募集资金用途简述、新增股份预计流通日期
一级市场-增发配股-股票配股信息：是否配股成功、每股配股比例、配股价、股权登记日、配股数量、配股前总股本、配股后总股
二级市场：大宗交易-沪深大宗交易：全部（只采集1千万以上的）
公司资料-基本信息-股票基本信息：全部
公司资料-股东股本-公司股本变动：总股本、A股（最新时间的）
公司资料-股东股本-A股公司股权质押冻结：涉及股份数量、股份质押冻结起始日期、股份质押冻结结束日期（只采集有效期内的）
公司资料-人员信息-高管及相关人员持股变动：全部
公司资料-投融资与分红-股票分红信息：全部
港股-市场行情-港股日行情：今收盘、成交金额(元)、总市值、流通市值
港股-公司资料-港股基本信息：全部
港股-公司资料-港股公司行为：全部

3、功能界面和第三数据源：
理杏仁——公司——基本面——估值——主要指标
理杏仁——公司——基本面——自定义图表
理杏仁——公司——（自定义财报、制图，包括所有指标）
理杏仁——筛选器——我的公司筛选、开始公司筛选

4、其实就相当于定制一个精简版的理杏仁，它有的核心功能和财务数据都要有，缺少的必要功能我们优化完善，错漏的数据我们更正补充，去掉其他多余的功能模块，放在普通电脑上用，数据既支持自动采集也支持手动选择文档，数据采集支持增量同步(已存在的数据不用重复采集)。
```
他们主要是参照了理杏仁这个插件软件，你可以爬取一下网上消息，看看他有什么功能，获取我们都能用得上。


** 但是目前我觉得有几个难题需要我跟你（Copilot Agent）讨论之后我们得出结论，一道一道讨论，你用askQuestion工具与我讨论，待我确认结论之后你负责补充进文件中。 **

1. 数据库问题，目前使用的SQlite虽然符合我们免安装的情况，但是考虑到它性能可能不好（你觉得呢？），并且如果我们还要下载公司财报等大文件大数据，可能SQLite的性能不足以支撑我们，你认为哪一种数据库适合开盒即用（跟随安装包直接安装，不需要配置运行环境），并且有好的大数据大文件效率。
2. 财报pdf,excel解析，转做可利用，可快速读取（可供LLM读取分析），方便记录的。企业财报大部分都是pdf和excel，我们需要设计一个高效，稳定的转化工具，并且支持复用。
3. 财报数据暴露给mcp和llm使用，要结合现有代码结构，我们已经将个股AI分析，股票板块推荐已经写好了，新加入这些数据要同时暴露以上功能使用，需要计划一下怎么样最小代价更新给他们，并丰富LLM能获取到的数据，以此达到更好的AI分析效果。

---

# Agent 整理：完整需求与开发计划

> 基于 2026-04-04 讨论确认，以下为 Agent 整理的正式开发计划。

## 1. 系统架构

### 1.1 总体架构图

```
┌─────────────────────────────────────────────────┐
│                  Desktop (Electron)              │
│  ┌─────────────────────────────────────────────┐ │
│  │            Frontend (Vue 3)                  │ │
│  │  ┌──────────┐  ┌──────────┐  ┌───────────┐ │ │
│  │  │ 财报浏览  │  │ 公告列表  │  │ 采集设置   │ │ │
│  │  └──────────┘  └──────────┘  └───────────┘ │ │
│  └─────────────────────────────────────────────┘ │
└────────────────────┬────────────────────────────┘
                     │ HTTP
┌────────────────────▼────────────────────────────┐
│           Backend API (ASP.NET Core)             │
│  ┌─────────────────────────────────────────────┐ │
│  │     CompanyData Module (新增)                │ │
│  │  ┌──────────┐  ┌──────────┐  ┌───────────┐ │ │
│  │  │ 查询 API  │  │ MCP 工具  │  │ 采集配置   │ │ │
│  │  └──────────┘  └──────────┘  └───────────┘ │ │
│  └─────────────────────────────────────────────┘ │
│  ┌─────────────────────────────────────────────┐ │
│  │     既有 Modules (Stocks/Market/Llm)         │ │
│  └─────────────────────────────────────────────┘ │
└────────────────────┬────────────────────────────┘
                     │ 共享 LiteDB + 文件系统
┌────────────────────▼────────────────────────────┐
│     FinancialDataWorker (独立 .NET Worker)       │
│  ┌──────────┐  ┌──────────┐  ┌───────────────┐ │
│  │ 巨潮爬取  │  │ PDF解析   │  │ LLM结构化     │ │
│  │  Worker   │  │  Pipeline │  │  Enrichment   │ │
│  └──────────┘  └──────────┘  └───────────────┘ │
└─────────────────────────────────────────────────┘
                     │
        ┌────────────┼────────────┐
        ▼            ▼            ▼
   ┌─────────┐ ┌─────────┐ ┌──────────┐
   │ LiteDB  │ │ 文件系统 │ │  SQLite  │
   │(财报数据)│ │(原始PDF) │ │(现有数据) │
   └─────────┘ └─────────┘ └──────────┘
```

### 1.2 新增项目结构

```
backend/
├── SimplerJiangAiAgent.Api/
│   └── Modules/
│       └── CompanyData/              ← 新增 Module
│           ├── CompanyDataModule.cs   ← 模块注册、路由映射
│           ├── Models/
│           │   ├── FinancialReport.cs     ← LiteDB 文档模型
│           │   ├── CompanyAnnouncement.cs
│           │   ├── CompanyBasicInfo.cs
│           │   ├── ExecutiveHolding.cs
│           │   └── DataCollectionConfig.cs ← 用户采集配置
│           ├── Services/
│           │   ├── IFinancialDataStore.cs      ← LiteDB 存取抽象
│           │   ├── LiteDbFinancialDataStore.cs ← LiteDB 实现
│           │   ├── FinancialReportQueryService.cs
│           │   └── Mcp/
│           │       └── CompanyDataMcpProvider.cs ← MCP 工具提供者
│           └── Endpoints/
│               ├── FinancialReportEndpoints.cs
│               ├── AnnouncementEndpoints.cs
│               └── CollectionConfigEndpoints.cs
│
├── SimplerJiangAiAgent.FinancialWorker/   ← 新增独立项目
│   ├── Program.cs
│   ├── appsettings.json
│   ├── Workers/
│   │   ├── CninfoReportCrawler.cs        ← 巨潮网报告爬取
│   │   ├── EastmoneyCrawler.cs           ← 东方财富补充数据
│   │   └── DataCollectionOrchestrator.cs ← 采集调度器
│   ├── Parsers/
│   │   ├── PdfExtractionPipeline.cs      ← 三路 PDF 解析管线
│   │   ├── PdfPigExtractor.cs
│   │   ├── DocnetExtractor.cs
│   │   ├── ITextExtractor.cs
│   │   ├── ExcelParser.cs                ← EPPlus Excel 解析
│   │   ├── FinancialTableParser.cs       ← 三张主表规则引擎
│   │   └── NarrativeLlmSummarizer.cs     ← 叙述性内容 LLM 精简
│   └── Models/
│       └── CrawlTask.cs
```

### 1.3 文件存储结构

```
App_Data/
├── financial-reports/
│   ├── {symbol}/              例: 600519/
│   │   ├── {year}/            例: 2025/
│   │   │   ├── annual-report.pdf
│   │   │   ├── q1-report.pdf
│   │   │   ├── q2-report.pdf (半年报)
│   │   │   ├── q3-report.pdf
│   │   │   └── announcements/
│   │   │       ├── 2025-03-15_增发公告.pdf
│   │   │       └── ...
│   │   └── ...
│   └── ...
├── financial-data.litedb        ← LiteDB 数据文件
├── financial-data-log.litedb    ← LiteDB 日志
└── app.db                       ← 现有 SQLite（不动）
```

## 2. 数据模型设计（LiteDB 文档）

### 2.1 核心文档模型

```csharp
// 财务报告文档（按报告期存储）
public class FinancialReport
{
    public ObjectId Id { get; set; }
    public string Symbol { get; set; }           // 股票代码 如 "600519"
    public string CompanyName { get; set; }       // 公司简称
    public string ReportType { get; set; }        // "annual" | "q1" | "semi-annual" | "q3"
    public int FiscalYear { get; set; }           // 财年 如 2025
    public string ReportPeriod { get; set; }      // "2025-Q3" | "2025-Annual"
    public DateTime PublishDate { get; set; }     // 发布日期

    // 原始文件引用
    public string PdfFilePath { get; set; }       // 文件系统中的相对路径
    public long PdfFileSize { get; set; }         // 文件大小(bytes)
    public string PdfHash { get; set; }           // SHA256 用于增量同步

    // 结构化财务数据（代码提取）
    public BalanceSheet BalanceSheet { get; set; }         // 资产负债表
    public IncomeStatement IncomeStatement { get; set; }   // 利润表
    public CashFlowStatement CashFlow { get; set; }        // 现金流量表

    // LLM 精简的叙述内容
    public string ManagementAnalysis { get; set; }    // 管理层分析摘要
    public string RiskWarnings { get; set; }          // 风险提示摘要
    public string BusinessHighlights { get; set; }    // 经营亮点摘要

    // 解析元数据
    public string ParseStatus { get; set; }       // "pending" | "parsed" | "failed" | "llm-enriched"
    public DateTime? ParsedAt { get; set; }
    public string ParseEngine { get; set; }       // 实际使用的解析引擎
    public List<string> ParseWarnings { get; set; } // 解析警告

    // 数据源追踪
    public string SourceUrl { get; set; }         // 下载来源 URL
    public string DataSource { get; set; }        // "cninfo" | "eastmoney"
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// 资产负债表
public class BalanceSheet
{
    public decimal? TotalAssets { get; set; }              // 总资产
    public decimal? TotalLiabilities { get; set; }         // 总负债
    public decimal? TotalEquity { get; set; }              // 股东权益
    public decimal? CurrentAssets { get; set; }            // 流动资产
    public decimal? NonCurrentAssets { get; set; }         // 非流动资产
    public decimal? CurrentLiabilities { get; set; }       // 流动负债
    public decimal? NonCurrentLiabilities { get; set; }    // 非流动负债
    public decimal? CashAndEquivalents { get; set; }       // 货币资金
    public decimal? AccountsReceivable { get; set; }       // 应收账款
    public decimal? Inventory { get; set; }                // 存货
    public decimal? ShortTermBorrowings { get; set; }      // 短期借款
    public decimal? LongTermBorrowings { get; set; }       // 长期借款
    public Dictionary<string, decimal> RawItems { get; set; } // 所有原始科目
}

// 利润表
public class IncomeStatement
{
    public decimal? Revenue { get; set; }                  // 营业收入
    public decimal? OperatingCost { get; set; }            // 营业成本
    public decimal? GrossProfit { get; set; }              // 毛利润
    public decimal? OperatingProfit { get; set; }          // 营业利润
    public decimal? NetProfit { get; set; }                // 净利润
    public decimal? NetProfitExcluding { get; set; }       // 扣非净利润
    public decimal? EarningsPerShare { get; set; }         // 每股收益
    public decimal? SellingExpenses { get; set; }          // 销售费用
    public decimal? AdminExpenses { get; set; }            // 管理费用
    public decimal? RdExpenses { get; set; }               // 研发费用
    public decimal? FinancialExpenses { get; set; }        // 财务费用
    public Dictionary<string, decimal> RawItems { get; set; }
}

// 现金流量表
public class CashFlowStatement
{
    public decimal? OperatingCashFlow { get; set; }        // 经营活动现金流净额
    public decimal? InvestingCashFlow { get; set; }        // 投资活动现金流净额
    public decimal? FinancingCashFlow { get; set; }        // 筹资活动现金流净额
    public decimal? NetCashFlow { get; set; }              // 现金净增加额
    public decimal? FreeCashFlow { get; set; }             // 自由现金流
    public Dictionary<string, decimal> RawItems { get; set; }
}

// 公司公告（第二期）
public class CompanyAnnouncement
{
    public ObjectId Id { get; set; }
    public string Symbol { get; set; }
    public string Title { get; set; }
    public string AnnouncementType { get; set; }  // "增发" | "配股" | "分红" | "股权质押" | "其他"
    public DateTime PublishDate { get; set; }
    public string Summary { get; set; }            // LLM 精简摘要
    public string PdfFilePath { get; set; }
    public string SourceUrl { get; set; }
    public Dictionary<string, object> StructuredData { get; set; } // 类型特定的结构化数据
    public DateTime CreatedAt { get; set; }
}

// 股票基本信息（第三期）
public class CompanyBasicInfo
{
    public ObjectId Id { get; set; }
    public string Symbol { get; set; }
    public string FullName { get; set; }
    public string Industry { get; set; }
    public decimal? TotalShares { get; set; }      // 总股本
    public decimal? FloatShares { get; set; }      // 流通股本
    public decimal? TotalMarketCap { get; set; }
    public decimal? FloatMarketCap { get; set; }
    public List<ShareCapitalChange> CapitalChanges { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// 高管持股变动（第四期）
public class ExecutiveHolding
{
    public ObjectId Id { get; set; }
    public string Symbol { get; set; }
    public string PersonName { get; set; }
    public string Position { get; set; }
    public string ChangeType { get; set; }         // "增持" | "减持"
    public decimal ChangeAmount { get; set; }
    public decimal ChangePrice { get; set; }
    public DateTime ChangeDate { get; set; }
    public decimal? HoldingAfter { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

### 2.2 采集配置模型

```csharp
public class DataCollectionConfig
{
    public ObjectId Id { get; set; }
    public string Scope { get; set; }             // "all" | "watchlist"
    public List<string> SpecificSymbols { get; set; } // scope="specific" 时使用
    public DateTime StartFrom { get; set; }        // 采集起始日期
    public bool AutoSync { get; set; }             // 是否自动定时同步
    public int SyncIntervalHours { get; set; }     // 同步间隔(小时)
    public string CninfoApiKey { get; set; }       // 巨潮网 API 密钥（加密存储）
    public DateTime? LastSyncAt { get; set; }
    public string LastSyncStatus { get; set; }
    public long EstimatedStorageBytes { get; set; } // 预估存储空间
}
```

## 3. PDF 解析管线设计

### 3.1 三路验证流水线

```
PDF文件 → ┌─ PdfPig   提取文本+表格 ─┐
          ├─ Docnet    提取文本+表格 ─┼→ 交叉验证 → 选最佳结果
          └─ iText7    提取文本+表格 ─┘
                                         │
                    ┌────────────────────┘
                    ▼
         FinancialTableParser (规则引擎)
            ├─ 识别资产负债表区域 → 提取科目和数值 → BalanceSheet
            ├─ 识别利润表区域 → 提取科目和数值 → IncomeStatement
            └─ 识别现金流量表区域 → 提取科目和数值 → CashFlowStatement
                    │
                    ▼
         NarrativeLlmSummarizer
            ├─ 提取管理层讨论与分析章节
            ├─ 提取风险因素章节
            └─ 调用 LLM Provider 精简为结构化摘要
                    │
                    ▼
         LiteDB 写入完整文档
```

### 3.2 三路验证策略

```csharp
public class PdfExtractionPipeline
{
    // 三个提取器并行运行
    // 比较策略：
    // 1. 如果三路结果一致 → 直接使用
    // 2. 如果两路一致一路不同 → 使用多数结果，标记警告
    // 3. 如果三路都不同 → 使用 PdfPig 为主（表格能力最强），标记需人工复核
    // 4. 数值偏差在 0.01% 以内视为一致（浮点精度）
}
```

### 3.3 财务三主表规则引擎

```
识别逻辑：
1. 关键词定位：搜索"资产负债表"、"利润表"/"损益表"、"现金流量表"
2. 表头识别：找到"项目"、"本期金额"、"上期金额"等列头
3. 科目映射：将 PDF 中的科目名称映射到标准字段名
   - "货币资金" → CashAndEquivalents
   - "应收账款" → AccountsReceivable
   - "营业收入" → Revenue
   - ... (维护完整映射表)
4. 数值提取：处理千分位、万元/亿元单位、括号表示负数等格式
5. 平衡验证：总资产 = 总负债 + 股东权益（容差 0.01%）
```

## 4. 数据采集流程

### 4.1 巨潮网 OpenAPI 集成

```
注册 → 获取 API Key → 配置到 DataCollectionConfig

API 调用流程：
1. /api/stock/p_stock2301 → 获取公告列表（按类型筛选：定期报告、增发、分红等）
2. 根据公告列表获取 PDF 下载链接
3. 下载 PDF 到本地文件系统
4. 记录爬取进度，支持断点续传
```

### 4.2 增量同步策略

```
1. 每次同步前检查已有数据的最新日期
2. 只拉取新增/更新的报告
3. 用 SHA256 哈希判断文件是否变化（更正报告覆盖旧版）
4. 同步状态记录在 LiteDB 的 CollectionProgress 集合中
```

### 4.3 存储空间预估

```
单股票年均数据量估算：
- 年报 PDF: ~2-10 MB
- 半年报: ~1-5 MB
- 一季报/三季报: ~0.5-2 MB 各
- 公告类: ~0.1-1 MB 每份, 年均约 10-30 份
- 结构化数据: ~10-50 KB/年

单股票每年约: 10-30 MB (PDF) + ~50 KB (结构化)
Watchlist 30 只股票 × 3 年: ~1-3 GB
全 A 股 5000+ × 3 年: ~150-450 GB (需明确提示用户)

用户配置界面需显示预估存储量，并在采集前确认。
```

## 5. MCP 集成方案

### 5.1 复用与新建策略

| 数据类型 | 复用现有 MCP | 新建 MCP | 说明 |
|---------|-----------|---------|------|
| 财务三主表 | 扩展 `/mcp/fundamentals` | - | 在 FundamentalsData DTO 中添加财报字段 |
| 管理层分析/风险 | - | `/mcp/financial-report` | 新建，返回指定报告期的完整解析数据 |
| 公司公告 | 扩展 `/mcp/news` | - | news 的 announcement source 已有基础 |
| 基本信息 | 扩展 `/mcp/company-overview` | - | 补充股本、市值等字段 |
| 高管持股 | 扩展 `/mcp/shareholder` | - | 已有 shareholder 端点 |
| 跨期财务对比 | - | `/mcp/financial-trend` | 新建，支持多报告期对比 |

### 5.2 新增 MCP 工具定义

```csharp
// /mcp/financial-report
// 返回指定股票指定报告期的完整财报解析数据
GetFinancialReportAsync(string symbol, string period, string? taskId, ...)
→ StockCopilotMcpEnvelopeDto<FinancialReportDataDto>

// /mcp/financial-trend
// 返回多个报告期的关键指标趋势
GetFinancialTrendAsync(string symbol, int periods, string? taskId, ...)
→ StockCopilotMcpEnvelopeDto<FinancialTrendDataDto>
```

### 5.3 AI 分析流程集成

```
现有个股 AI 分析流程增强：
Commander/Researcher 在分析时可调用：
1. GetFinancialReportAsync → 获取最新财报的三张主表 + 管理层摘要
2. GetFinancialTrendAsync → 获取近 N 季度关键指标趋势
3. 这些数据将作为 evidence 注入到分析 prompt 中

Prompt 注入模板：
## 财务数据（来自公司财报）
- 最新报告期：{period}
- 营收：{revenue}（同比 {revenueYoY}%）
- 净利润：{netProfit}（同比 {netProfitYoY}%）
- 毛利率：{grossMarginRate}%
- 资产负债率：{debtRatio}%
- 经营现金流：{operatingCashFlow}
- 管理层分析摘要：{managementSummary}
- 主要风险：{riskSummary}
```

## 6. 四期开发计划

### 第一期：财报采集与解析 MVP（核心链路）

---

## 9. API 可用性实测报告（2025-04-04）

> 以下接口均已通过实际 HTTP 请求验证，确认返回数据正常。

### 9.1 东方财富（主要结构化数据源）

**所有接口均免认证，仅需标准 User-Agent 头。**

#### 接口 1: 主要财务指标 ★★★★★

```
URL: https://emweb.securities.eastmoney.com/PC_HSF10/NewFinanceAnalysis/ZYZBAjaxNew
参数: type=0&code=SH{code}
方式: GET
认证: 否
```

返回 100+ 个财务指标字段，按报告期分页（每页约8个季度）：
- EPS（`EPSJB`）、每股净资产（`BPS`）
- 营收（`TOTALOPERATEREVE`）、净利润（`PARENTNETPROFIT`）
- ROE（`ROEJQ`）、资产负债率（`ZCFZL`）
- 毛利率（`XSMLL`）、净利率（`XSJLL`）
- 同比增长率、环比增长率等衍生指标
- 已验证：茅台 600519 返回 2023Q3~2025Q3 共8个季度完整数据

#### 接口 2: 资产负债表 ★★★★★

```
URL: https://emweb.securities.eastmoney.com/PC_HSF10/NewFinanceAnalysis/zcfzbAjaxNew
参数: companyType=4&reportDateType=0&reportType=1&dates={YYYY-MM-DD}&code=SH{code}
方式: GET
认证: 否
```

返回 200+ 个资产负债表科目 + 同比变化（`_YOY` 后缀）：
- 货币资金（`MONETARYFUNDS`）、存货（`INVENTORY`）
- 应收账款（`ACCOUNTS_RECE`）、应付账款（`ACCOUNTS_PAYABLE`）
- 总资产（`TOTAL_ASSETS`）、总负债（`TOTAL_LIABILITIES`）
- 股东权益（`TOTAL_EQUITY`）、少数股东权益（`MINORITY_EQUITY`）
- 已验证：茅台 2025Q3 总资产 3047.38亿

#### 接口 3: 利润表 ★★★★★

```
URL: https://emweb.securities.eastmoney.com/PC_HSF10/NewFinanceAnalysis/lrbAjaxNew
参数: companyType=4&reportDateType=0&reportType=1&dates={YYYY-MM-DD}&code=SH{code}
方式: GET
认证: 否
```

返回 100+ 个利润表科目 + 同比变化：
- 营业收入（`TOTAL_OPERATE_INCOME`/`OPERATE_INCOME`）
- 营业成本（`OPERATE_COST`）、营业税金（`OPERATE_TAX_ADD`）
- 四费：销售（`SALE_EXPENSE`）、管理（`MANAGE_EXPENSE`）、研发（`RESEARCH_EXPENSE`）、财务（`FINANCE_EXPENSE`）
- 营业利润（`OPERATE_PROFIT`）、净利润（`NETPROFIT`）
- 已验证：茅台 2025Q3 净利润 668.99亿

#### 接口 4: 现金流量表 ★★★★★

```
URL: https://emweb.securities.eastmoney.com/PC_HSF10/NewFinanceAnalysis/xjllbAjaxNew
参数: companyType=4&reportDateType=0&reportType=1&dates={YYYY-MM-DD}&code=SH{code}
方式: GET
认证: 否
```

返回 100+ 个现金流量表科目 + 同比变化：
- 经营活动现金流入（`TOTAL_OPERATE_INFLOW`）/流出（`TOTAL_OPERATE_OUTFLOW`）
- 经营活动净现金流（`NETCASH_OPERATE`）
- 投资活动净现金流（`NETCASH_INVEST`）
- 筹资活动净现金流（`NETCASH_FINANCE`）
- 已验证：茅台 2025Q3 经营净现金流 381.97亿

#### 接口 5: datacenter 通用查询 API ★★★★☆

```
URL: https://datacenter.eastmoney.com/securities/api/data/v1/get
参数: reportName={表名}&columns={字段列表|ALL}&filter=({条件})&sortColumns={排序}&sortTypes=-1&pageSize={N}
方式: GET
认证: 否
```

已验证的报表名：

| 报表名 | 数据内容 | 验证状态 |
|--------|---------|---------|
| `RPT_DMSK_FN_BALANCE` | 简化资产负债表 | ✅ 返回总资产/负债/权益 |
| `RPT_EXECUTIVE_HOLD_DETAILS` | 高管持股变动 | ✅ 含人名、变动数量、价格、日期 |
| `RPT_SHAREBONUS_DET` | 分红数据 | ✅ 26条历史记录，含分配方案、除权日 |
| `RPTA_WEB_RZRQ_GGMX` | 融资融券余额 | ✅ 3870+日度记录 |
| `RPT_F10_FN_MAINOP` | 主营业务构成 | ✅ 已在项目中使用 |

**注意事项：**
- 高管持股：字段名用 `SECURITY_NAME`（非 `SECURITY_NAME_ABBR`），建议用 `columns=ALL`
- 融资融券：报表名为 `RPTA_WEB_RZRQ_GGMX`，过滤列为 `SCODE`（非 `SECURITY_CODE`）

#### 接口 6: 公司概况 ★★★★★

```
URL: https://emweb.securities.eastmoney.com/PC_HSF10/CompanySurvey/CompanySurveyAjax
参数: code={SH/SZ}{code}
方式: GET
认证: 否
已在项目中使用: EastmoneyCompanyProfileParser.cs
```

### 9.2 巨潮资讯网（PDF 原始文件源）

**免认证，需 Referer 头。**

#### 接口 1: 公告查询 ★★★★☆

```
URL: http://www.cninfo.com.cn/new/hisAnnouncement/query
方式: POST
Content-Type: application/x-www-form-urlencoded
Referer: http://www.cninfo.com.cn/
```

参数：
```
stock={code},{orgId}    // 重要：必须包含 orgId，如 600519,gssh0600519
tabName=fulltext
pageSize=30
pageNum=1
column=szse             // szse=深/沪/北，sse=上交所
category=category_ndbg_szsh  // 年报分类代码
plate=sh                // sh/sz/bj
seDate=                 // 日期范围
```

**公告类别代码：**
```
category_ndbg_szsh  - 年报
category_bndbg_szsh - 半年报
category_yjdbg_szsh - 一季报
category_sjdbg_szsh - 三季报
category_gddh_szsh  - 股东大会
category_rcjy_szsh  - 日常经营
```

**orgId 规则：**
- 上交所：`gssh0{code}`（如 `gssh0600519`）
- 深交所：`gssz0{code}`（如 `gssz0000001`）

返回数据含：
- `announcementId` - 公告唯一ID
- `adjunctUrl` - PDF 相对路径（如 `finalpage/2025-04-03/1222993920.PDF`）
- `announcementTitle` - 公告标题
- `announcementTime` - 发布时间戳(毫秒)
- 已验证：茅台年报 total=56 条

#### 接口 2: PDF 下载 ★★★★★

```
URL: http://static.cninfo.com.cn/{adjunctUrl}
方式: GET
认证: 否
示例: http://static.cninfo.com.cn/finalpage/2025-04-03/1222993920.PDF
```

#### 接口 3: 全文搜索 ★★★★☆

```
URL: https://www.cninfo.com.cn/new/fulltextSearch/full
参数: searchkey={关键词}&pageNum=1&isfulltext=false&sortName=nothing&sortType=desc
方式: GET
认证: 否
```

返回与关键词匹配的所有公告，含 PDF 下载链接。已验证：搜索"贵州茅台"返回 1230 条。

### 9.3 额外发现的有用数据

以下数据对股票分析有价值，建议纳入后续计划：

| 数据类型 | 来源 | 接口 | 分析价值 |
|---------|------|------|---------|
| **融资融券余额** | 东方财富 | `RPTA_WEB_RZRQ_GGMX` | 市场杠杆情绪指标 |
| **分红历史** | 东方财富 | `RPT_SHAREBONUS_DET` | 股息率计算、分红稳定性 |
| **主营业务构成** | 东方财富 | `RPT_F10_FN_MAINOP` | 收入结构分析 |
| **大宗交易** | 东方财富 | datacenter API | 机构动向信号 |
| **股权质押** | 巨潮 | 公告分类筛选 | 大股东风险预警 |
| **机构持仓** | 东方财富 | datacenter API | 机构认可度 |

### 9.4 架构影响：重大简化

**关键发现：东方财富直接提供完整结构化三主表数据（JSON格式），无需解析 PDF。**

原始计划的 PDF 三路解析管线（PdfPig + Docnet + iText7）仅需用于：
1. 叙述性内容提取（管理层分析、风险提示）
2. 原始文件归档备查
3. 数据交叉验证（可选）

**第一期 MVP 可大幅简化：**
- 从东方财富 API 直接获取结构化三主表 + 指标数据
- 从巨潮网下载 PDF 归档
- PDF 解析管线降级为第二期功能（用于叙述性内容）
- 第一期即可跑通：采集 → 存储 → MCP 暴露 → AI 分析

### 9.5 推荐数据源组合

| 数据类型 | 主数据源 | 备选数据源 |
|---------|---------|-----------|
| 资产负债表 | 东方财富 zcfzbAjaxNew | 东方财富 datacenter RPT_DMSK_FN_BALANCE |
| 利润表 | 东方财富 lrbAjaxNew | 东方财富 datacenter API |
| 现金流量表 | 东方财富 xjllbAjaxNew | 东方财富 datacenter API |
| 主要指标 | 东方财富 ZYZBAjaxNew | 计算自三主表 |
| 年报PDF | 巨潮 hisAnnouncement | 东方财富 PDF 链接 |
| 高管持股 | 东方财富 RPT_EXECUTIVE_HOLD_DETAILS | 巨潮公告筛选 |
| 分红数据 | 东方财富 RPT_SHAREBONUS_DET | 巨潮公告筛选 |
| 公司基本信息 | 东方财富 CompanySurveyAjax | 巨潮基本信息 |
| 融资融券 | 东方财富 RPTA_WEB_RZRQ_GGMX | - |

**所有测试数据均为2026年4月4日实测，接口状态正常。**

**目标**：能自动下载指定股票的财报 PDF，解析出三张主表数据，存入 LiteDB，通过 MCP 暴露给 AI 分析。

| 步骤 | 内容 | 预估复杂度 |
|-----|------|-----------|
| 1.1 | 新建 `SimplerJiangAiAgent.FinancialWorker` 项目，配置 LiteDB | 低 |
| 1.2 | 新建 `CompanyData` Module 框架，注册 LiteDB 服务 | 低 |
| 1.3 | 实现巨潮网 OpenAPI 客户端（报告列表查询+PDF 下载） | 中 |
| 1.4 | 实现 PDF 三路解析管线（PdfPig + Docnet + iText7） | 高 |
| 1.5 | 实现财务三主表规则引擎（科目映射+数值提取+平衡验证） | 高 |
| 1.6 | 实现叙述性内容 LLM 精简（复用现有 LLM Provider） | 中 |
| 1.7 | 实现 LiteDB 存取服务 + 查询 API | 中 |
| 1.8 | 新建 `/mcp/financial-report` 和 `/mcp/financial-trend` MCP 工具 | 中 |
| 1.9 | 扩展现有 `/mcp/fundamentals` 注入财报数据 | 低 |
| 1.10 | 前端采集配置界面（范围、起始时间、存储预估） | 中 |
| 1.11 | 集成测试：选一只股票跑通全链路 | 中 |

**验收标准**：
- 能通过配置界面设置采集范围和起始时间
- Worker 能自动下载财报 PDF 并解析出三张主表
- AI 分析时能自动引用最新财报数据
- 显示预估存储空间

### 第二期：公司公告采集

| 步骤 | 内容 |
|-----|------|
| 2.1 | 巨潮网公告类型筛选（增发、配股、分红、股权质押等） |
| 2.2 | 公告 PDF 下载 + 类型特定的结构化提取 |
| 2.3 | 公告摘要 LLM 精简 |
| 2.4 | 扩展 `/mcp/news` 支持 announcement 源切换到本地数据 |
| 2.5 | 公告事件时间线前端展示 |

### 第三期：股票基本信息 + 股本结构

| 步骤 | 内容 |
|-----|------|
| 3.1 | 东方财富/巨潮 基本信息 API 对接 |
| 3.2 | 股本变动历史采集和存储 |
| 3.3 | 扩展 `/mcp/company-overview` 注入详细基本面 |
| 3.4 | 前端基本信息展示面板 |

### 第四期：高管持股变动

| 步骤 | 内容 |
|-----|------|
| 4.1 | 高管持股变动数据采集（巨潮网 API） |
| 4.2 | 持股变动信号分析（大额减持预警等） |
| 4.3 | 扩展 `/mcp/shareholder` 注入高管变动数据 |
| 4.4 | 前端高管持股变动时间线 |

## 7. 技术依赖清单

### NuGet 包（FinancialWorker 项目）

| 包名 | 用途 |
|------|------|
| `LiteDB` (v5.x) | 嵌入式文档数据库 |
| `UglyToad.PdfPig` | PDF 解析路线 1 |
| `Docnet.Core` | PDF 解析路线 2 |
| `itext7` | PDF 解析路线 3（注意 AGPL 协议） |
| `EPPlus` | Excel 解析 |
| `Microsoft.Extensions.Hosting` | Worker Service 框架 |

### NuGet 包（API 项目新增）

| 包名 | 用途 |
|------|------|
| `LiteDB` (v5.x) | 读取财报数据 |

## 8. 风险与注意事项

1. **巨潮网 API 限流**：需实现请求限速器（建议 1-2 QPS），避免被封禁
2. **PDF 格式多样性**：不同公司财报排版差异大，规则引擎需要持续迭代适配
3. **iText7 AGPL 协议**：如果项目闭源发布需要购买商业授权或剔除 iText7
4. **存储增长**：全 A 股模式下存储增长快，需要向用户明确提示并支持清理旧数据
5. **LLM 成本**：大量财报的叙述性内容 LLM 精简会产生 API 费用，需要做批量控制和失败重试
6. **巨潮网 API Key 安全**：加密存储在 LiteDB 中，不进入 Git 仓库

### 9.6 备选数据源测试（2026-04-04）

| 数据源 | 接口/入口 | 状态 | 评分 | 备注 |
|--------|----------|------|------|------|
| 新浪财经 | 资产负债表 HTML | ❌ 500错误 | ★☆☆☆☆ | 接口疑似下线 |
| 腾讯财经 | K线行情 JSON | ✅ | ★★★★☆ | 仅行情数据，无财务报表 |
| 同花顺10jqka | stockph API | ❌ 503 | ★☆☆☆☆ | 服务不可用 |
| 网易163 | 三主表 CSV/HTML | ❌ TLS失败 | ★☆☆☆☆ | 连接被拒 |
| 雪球 | 财务报表 | ❌ 400需认证 | ★★☆☆☆ | 需登录token |
| **东方财富 datacenter** | RPT_DMSK_FN_* | ✅ | ★★★★★ | **有效备选** |

**结论：**

- **免费第三方财务数据源几乎全灭**（新浪/网易/同花顺/雪球均不可直接调用）
- **可用方案**：东方财富双入口冗余（emweb + datacenter），加上巨潮PDF作为归档和验证
- **入口冗余策略**：如 emweb API 不可用，降级到 datacenter API（字段较少但核心数据完整）
- **长期建议**：如需真正异源冗余，考虑 AKShare Python 库或 Tushare Pro 付费接口

### 9.7 最终确定的数据源架构（三通道 + cninfo PDF）

```
数据采集三通道降级架构：

入口 A (主力): emweb.securities.eastmoney.com
  ├─ zcfzbAjaxNew    → 资产负债表 (200+ 字段 + YoY)
  ├─ lrbAjaxNew      → 利润表 (100+ 字段 + YoY)
  ├─ xjllbAjaxNew    → 现金流量表 (100+ 字段 + YoY)
  └─ ZYZBAjaxNew     → 主要财务指标
      │ 降级触发: HTTP 超时(15s)/5xx/数据为空
      ▼
入口 B (备用): datacenter.eastmoney.com
  ├─ RPT_DMSK_FN_BALANCE   → 资产负债表 (关键字段)
  ├─ RPT_DMSK_FN_INCOME    → 利润表 (关键字段)
  └─ RPT_DMSK_FN_CASHFLOW  → 现金流量表 (关键字段)
      │ 降级触发: 入口B也失败
      ▼
入口 C (第三备份): basic.10jqka.com.cn
  ├─ /api/stock/finance/{code}_debt.json   → 资产负债表
  ├─ /api/stock/finance/{code}_benefit.json → 利润表
  └─ /api/stock/finance/{code}_cash.json    → 现金流量表
      │ 并行（不受 A/B/C 状态影响）
      ▼
入口 D (PDF归档): cninfo.com.cn
  ├─ hisAnnouncement/query → 公告列表 (POST)
  └─ static.cninfo.com.cn  → PDF 下载

额外数据（第一期一并纳入）：
  ├─ RPT_SHAREBONUS_DET         → 分红历史
  ├─ RPTA_WEB_RZRQ_GGMX        → 融资融券余额
  └─ RPT_EXECUTIVE_HOLD_DETAILS → 高管持股变动
```

**降级逻辑：**
1. 每次采集先请求入口A，设 15s 超时
2. 如入口A返回错误/超时/数据为空，自动降级到入口B
3. 如入口B也失败，降级到入口C（同花顺 basic API）
4. 巨潮PDF始终并行下载（不受入口A/B/C状态影响）
5. 降级事件记录到 LiteDB 的 `CollectionLog` 集合，供后续分析

### 9.8 开源项目数据源研究（2026-04-04）

对主流 A 股开源数据项目进行了深度源码分析，目的是寻找非 Eastmoney 的独立数据源。

| 项目 | Stars | 底层数据源 | 财务报表支持 | .NET可调用 | 评估 |
|------|-------|-----------|------------|-----------|------|
| **AKShare** | 18k | Eastmoney API (emweb + datacenter) | ✅ 三张报表全覆盖 | ✅ 相同HTTP端点 | 底层与我们方案完全一致 |
| **efinance** | 10 | Eastmoney push2 API | ❌ 仅行情/资金流 | ✅ | 无财务报表，不可用 |
| **BaoStock** | ~2k | 自有TCP Socket服务器 | ✅ 指标级（非逐行） | ❌ 需移植TCP协议 | 移植成本高，数据粒度不足 |
| **Tushare** | ~13k | Sina + 自有服务器 | ✅ | ❌ 需token，免费额度极低 | 免费不可用，付费才行 |

**核心发现：**
- **所有主流开源项目底层都依赖 Eastmoney**，不存在真正独立的免费结构化财报数据源
- AKShare 的三张报表代码（`stock_three_report_em.py`）使用的端点与我们验证通过的完全相同
- AKShare 为退市股额外实现了 `datacenter.eastmoney.com/securities/api/data/get` 的 `RPT_F10_FINANCE_G*` 系列
- BaoStock 使用自定义二进制 TCP 协议，无法通过 HTTP 直接调用

**AKShare 财报端点源码确认（与我们一致）：**
- 资产负债表: `emweb.securities.eastmoney.com/.../zcfzbAjaxNew`
- 利润表: `emweb.securities.eastmoney.com/.../lrbAjaxNew`
- 现金流量表: `emweb.securities.eastmoney.com/.../xjllbAjaxNew`
- 需先从 HTML 页面提取 `companyType` (4=一般企业, 1=银行, 2=保险, 3=券商)
- 每次请求最多传 5 个日期

### 9.9 同花顺数据接口研究（2026-04-04）

#### 接口可用性

| 域名 | 认证要求 | 数据类型 | 可用性 |
|------|---------|---------|--------|
| `basic.10jqka.com.cn/api/stock/finance/` | **仅 User-Agent** | 三张报表 JSON | ✅ |
| `basic.10jqka.com.cn/basicapi/finance/` | **仅 User-Agent** | 财务指标 | ✅ |
| `data.10jqka.com.cn/` | hexin-v 令牌 | 资金流向等 | ⚠️ 需JS执行 |
| `d.10jqka.com.cn/` | hexin-v + Referer | K线数据 | ⚠️ 需JS执行 |
| `stockpage.10jqka.com.cn/` | 完整浏览器渲染 | 个股页面 | ❌ |

#### 同花顺 basic API 端点（无需令牌，Phase 1 纳入）

```
GET https://basic.10jqka.com.cn/api/stock/finance/{code}_debt.json
→ 资产负债表结构化 JSON

GET https://basic.10jqka.com.cn/api/stock/finance/{code}_benefit.json
→ 利润表结构化 JSON

GET https://basic.10jqka.com.cn/api/stock/finance/{code}_cash.json
→ 现金流量表结构化 JSON
```

**请求头要求：**
```
User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/114.0.0.0 Safari/537.36
```

#### hexin-v 令牌机制（了解但暂不实现）

- 同花顺使用自定义 JS 挑战（非 Cloudflare），名为 `ths.js`
- AKShare 通过 `py_mini_racer`（V8引擎）执行此 JS 生成令牌
- 令牌需同时设为 Cookie (`v={token}`) 和 HTTP Header (`hexin-v: {token}`)
- `data.10jqka.com.cn` 和 `d.10jqka.com.cn` 域需要此令牌
- **Phase 1 仅使用免令牌的 basic API**，如未来需要扩展再考虑移植

#### 风险评估

| 因素 | 风险等级 | 说明 |
|------|---------|------|
| IP封禁 | 中高 | 同花顺有激进的反爬策略，建议限速 1-2 req/s |
| 法律/ToS | 中 | 自动化采集可能违反服务条款，仅作为故障降级使用 |
| 接口稳定性 | 中 | 无 SLA，JSON 结构可能变更 |
| 生产可靠性 | 中高 | 作为第三备份可接受，不作为主力 |

**采用策略：最小化使用，仅在 Eastmoney 双通道都失败时触发，降低被检测风险。**

### 9.10 Phase 1 最终范围确认（2026-04-04）

基于以上所有研究和讨论，Phase 1 最终范围如下：

**数据采集：**
- [x] 三张财务报表（资产负债表、利润表、现金流量表）
- [x] 主要财务指标（100+ 项）
- [x] 分红送配历史
- [x] 融资融券余额
- [x] 三通道降级：Eastmoney emweb → datacenter → 同花顺 basic
- [x] 巨潮 PDF 并行下载与解析

**系统架构：**
- [x] 独立 .NET Worker 进程
- [x] LiteDB 文档数据库
- [x] PDF 三路验证（Docnet + PdfPig + iText7）
- [x] LLM 叙述内容摘要

**集成：**
- [x] MCP 工具（financial-report, financial-trend）
- [x] AI 分析流程集成
- [x] 用户配置（起始时间、全量/自选、存储预估）

**已确认技术决策：**
| 决策项 | 选择 |
|--------|------|
| 架构模式 | Hybrid: API Module + 独立 Worker |
| 新增数据库 | LiteDB (alongside SQLite) |
| 主力数据源 | Eastmoney emweb API |
| 第一备用 | Eastmoney datacenter API |
| 第二备用 | 同花顺 basic API (无令牌) |
| PDF 源 | cninfo.com.cn (并行) |
| PDF 解析 | Docnet + PdfPig + iText7 三路验证 |
| 叙述解析 | LLM 摘要（复用现有 Provider） |

### 9.11 理杏仁（LiXinger）功能参考研究（2026-04-04）

对理杏仁（www.lixinger.com）进行了功能研究，作为前端公司详情页的设计参考。

#### 理杏仁平台功能清单

理杏仁是面向量化分析师和价值投资者的 A 股/港股金融数据平台，提供 7 大数据域、60+ API 端点：

**1. 估值数据（Fundamental/Valuation）**
- PE(TTM)、PB、市值、股息率
- **百分位排名**：当前 PE 在 3年/5年/10年历史中的位置（`pe_ttm.y3.cvpos`）
- 按公司类型区分：一般企业 / 银行 / 券商 / 保险 / 其他金融

**2. 财务三表（Financial Statements）**
- 命名体系：`q.profitStatement.np.ttm`（净利润TTM）、`q.balanceSheet.ta.t`（总资产）
- 后缀语义：`.t` = 当期值、`.ttm` = 滚动12月、`.ttm_y2y` = TTM同比
- 单股最多可查 128 个财务指标，10年跨度

**3. 营收构成**
- 按业务线分解营收占比
- 前五大客户/供应商占比

**4. 热门指标**
- 换手率、股东人数变化、质押比例、限售解禁
- 每股分红与再投资收益率

**5. 股东数据**
- 前十大流通股东、股东人数变化趋势
- 基金持仓、基金公司合计持仓
- 高管变动

**6. 资金流向**
- 沪港通/港股通持仓
- 融资融券余额

**7. 宏观数据**
- 国债利率（中国/美国）、基准利率、汇率

#### 对我们前端设计的借鉴

| 理杏仁功能 | 借鉴方向 | 优先级 | 目标阶段 |
|------------|---------|--------|----------|
| 估值百分位排名 | 在指标卡片中显示当前 PE/PB 在历史中的位置 | ⭐⭐⭐ | Phase 1 |
| TTM + YoY 双维度 | 每个关键指标同时显示 TTM 值和同比变化 | ⭐⭐⭐ | Phase 1 |
| 公司类型区分 | 银行/保险/券商用不同指标集，复用 Eastmoney companyType | ⭐⭐ | Phase 1 |
| 趋势迷你图 | 关键指标的 8 季度折线图 | ⭐⭐ | Phase 1 |
| 营收构成饼图 | 业务线营收占比可视化 | ⭐ | Phase 2 |
| 股东人数变化 | 股东趋势图，判断筹码集中度 | ⭐ | Phase 2 |
| 宏观利率参考线 | 估值图中叠加无风险利率线 | ⭐ | Phase 3 |

#### Phase 1 前端设计参考要点

基于理杏仁的设计模式，我们的财务报表 Tab 应包含：

1. **估值概览卡片**：PE(TTM) + 百分位 | PB + 百分位 | 市值 | 股息率
2. **盈利指标卡片**：营收(TTM) + YoY | 净利(TTM) + YoY | ROE | 毛利率
3. **趋势图区**：营收/净利/ROE 最近 8 季度折线图
4. **三表切换浏览**：资产负债表 / 利润表 / 现金流量表，表格+关键行高亮
5. **数据源+采集状态**：底部显示数据来源通道和最后更新时间

## 10. 前端交互方案

### 10.1 数据消费：双模式设计

**模式一：独立财务报表 Tab**
- 在右侧边栏（`SidebarTabs.vue`）新增第5个Tab：**「财务报表」**
- Tab 内容包含（参照理杏仁设计模式）：
  - **估值概览卡片**：PE(TTM) + 历史百分位 | PB + 百分位 | 市值 | 股息率
  - **盈利指标卡片**：营收(TTM) + YoY | 净利(TTM) + YoY | ROE | 毛利率
  - **趋势图区**：营收/净利/ROE 最近 8 季度折线图（迷你图）
  - **三表切换浏览**：资产负债表 / 利润表 / 现金流量表，表格+关键行高亮
  - **报告期选择**：下拉选择季度/年度
  - **数据源标记**：显示本次数据来自哪个通道（emweb/datacenter/同花顺）
  - **采集状态**：最后采集时间、是否有最新数据
  - 按公司类型（一般企业/银行/保险/券商）自动适配指标集

**模式二：AI 分析引用**
- MCP 工具 `financial-report` / `financial-trend` 供 AI Agent 查询
- AI 分析 Tab 中的分析结论自动引用财报数据
- 用户在 AI 分析中可点击引用跳转到财报 Tab 对应数据

### 10.2 Worker 配置入口

在现有设置页面中增加**「财报采集」**配置区：

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| 启用自动采集 | 开关 | 关 | 控制 Worker 定时任务 |
| 采集范围 | 单选 | 自选股 | 自选股列表 / 全部A股 |
| 起始时间 | 日期选择 | 3年前 | 历史数据回溯起点 |
| 采集频率 | 单选 | 每日收盘后 | 每日/每周/手动 |
| 存储占用 | 只读显示 | - | 实时显示 LiteDB + PDF 占用空间 |
| 估算总量 | 只读显示 | - | 根据当前配置估算最终存储量 |

#### 10.2.1 采集测试面板

设置页的财报采集配置区内嵌**测试面板**，让用户可以直观观察 Worker 采集全流程：

**测试面板功能：**

| 功能 | 说明 |
|------|------|
| 单股测试 | 输入股票代码，点击「测试采集」，实时显示采集流程 |
| 通道选择 | 可指定测试哪个通道（主力/备用/同花顺/全部降级链） |
| 实时日志流 | SSE 或轮询方式展示采集过程的逐步日志 |
| 结果预览 | 采集完成后直接展示结构化数据和 PDF（如有） |
| 降级测试 | 模拟主力通道超时，观察是否正确降级到备用 |

**实时日志显示内容：**
```
[10:32:01] 开始采集 600519 (贵州茅台)
[10:32:01] 检测 companyType → 4 (一般企业)
[10:32:02] 通道A: emweb zcfzbAjaxNew → 200 OK, 5期数据
[10:32:02] 通道A: emweb lrbAjaxNew → 200 OK, 5期数据
[10:32:03] 通道A: emweb xjllbAjaxNew → 200 OK, 5期数据
[10:32:03] 通道A: emweb ZYZBAjaxNew → 200 OK, 20期指标
[10:32:04] 分红数据: RPT_SHAREBONUS_DET → 200 OK, 26条
[10:32:05] 融资融券: RPTA_WEB_RZRQ_GGMX → 200 OK, 30条
[10:32:05] cninfo PDF: 找到 3 份年报公告
[10:32:06] PDF #1: 下载中... 2.3MB → 完成
[10:32:08] PDF #1: Docnet提取 → 128页, PdfPig提取 → 128页, iText7提取 → 128页
[10:32:09] PDF #1: 三路一致性 → 通过
[10:32:10] LLM摘要生成中...
[10:32:12] 完成！总耗时 11s, 数据已存入 LiteDB
```

**结果预览区：**
- 三报表关键字段表格（可折叠查看完整字段）
- 分红记录列表
- 融资融券最近30天
- PDF 解析结果（原文摘要 + LLM摘要）
- 日志详情（可导出）

**对应 API：**
- `POST /api/system/finance-worker/test-collect` → 触发测试采集，返回 task ID
- `GET /api/system/finance-worker/test-collect/{taskId}/stream` → SSE 实时日志流
- `GET /api/system/finance-worker/test-collect/{taskId}/result` → 采集结果数据

### 10.3 采集触发机制

**定时自动：**
- Worker 在每个交易日收盘后（15:30 CST）自动执行批量采集
- 采集范围由用户配置决定（自选股 or 全量）
- 增量同步：只采集比本地最新报告期更新的数据

**按需触发：**
- 用户打开个股时，前端检查该股本地财报数据
- 如果无数据或数据过期（>1天未更新），通过 API 触发单股即时采集
- 单股采集走同样的三通道降级逻辑
- 采集完成后前端自动刷新财报 Tab

### 10.4 前端组件设计

```
SidebarTabs.vue              → 新增 Tab 5: 财务报表
  └─ StockFinancePanel.vue   → 财报 Tab 容器
       ├─ FinanceSummaryCard.vue    → 关键指标摘要
       ├─ FinanceTrendChart.vue     → 趋势迷你图
       ├─ FinanceStatementTable.vue → 报表明细表格
       └─ FinanceCollectionStatus.vue → 采集状态/来源
```

**数据请求路径：**
- `GET /api/stocks/{symbol}/finance/summary` → 关键指标摘要
- `GET /api/stocks/{symbol}/finance/statement?type=balance&period=2024Q3` → 报表明细
- `GET /api/stocks/{symbol}/finance/trend?indicators=revenue,netProfit,roe` → 趋势数据
- `POST /api/stocks/{symbol}/finance/collect` → 触发单股按需采集
- `GET /api/system/finance-worker/status` → Worker 状态和配置
- `PUT /api/system/finance-worker/config` → 更新 Worker 配置

## 11. Phase 1 开发任务拆解

### Phase 1 总览

Phase 1 目标：完成财报采集全链路 MVP，包含后端三通道采集 + LiteDB 存储 + PDF 解析 + MCP 工具 + 前端基础展示。

预计开发子任务 12 个，按依赖关系排序：

### Step 1: Worker 项目搭建与 LiteDB 集成

**输入**：无
**输出**：可编译运行的 Worker 项目 + LiteDB 数据库初始化

- [ ] 创建 `SimplerJiangAiAgent.FinancialWorker` .NET Worker Service 项目
- [ ] 添加到 `SimplerJiangAiAgent.sln`
- [ ] 引入 NuGet 包：LiteDB, Microsoft.Extensions.Hosting
- [ ] 实现 LiteDB 数据库服务（`FinancialDbContext`）
- [ ] 定义核心文档模型：`FinancialReport`, `FinancialIndicator`, `CollectionLog`
- [ ] 定义配置模型：`FinancialCollectionConfig`
- [ ] Worker 启动时初始化 LiteDB，创建索引
- [ ] 单元测试：LiteDB CRUD 操作验证

### Step 2: Eastmoney 主力通道实现

**输入**：Step 1 的 LiteDB 模型
**输出**：可从 Eastmoney emweb 抓取三表+指标并存 LiteDB

- [ ] 实现 `EastmoneyFinanceClient`（HttpClient 封装）
- [ ] 实现 companyType 检测逻辑（HTML 解析 `hidctype`）
- [ ] 实现三表数据抓取：zcfzbAjaxNew / lrbAjaxNew / xjllbAjaxNew
- [ ] 实现主要指标抓取：ZYZBAjaxNew
- [ ] JSON 响应解析 → `FinancialReport` 文档
- [ ] 存入 LiteDB，增量更新（按报告期去重）
- [ ] 错误处理：超时/5xx/空数据 → 降级标记
- [ ] 单元测试：模拟 JSON 响应 → 验证解析+存储

### Step 3: Eastmoney 备用通道实现

**输入**：Step 2 的客户端接口
**输出**：datacenter API 降级通道

- [ ] 实现 `EastmoneyDatacenterClient`
- [ ] 三表简化版：RPT_DMSK_FN_BALANCE / INCOME / CASHFLOW
- [ ] 字段映射到与主力通道相同的 `FinancialReport` 模型
- [ ] 降级逻辑：主力失败 → 自动切换备用
- [ ] 单元测试：降级场景验证

### Step 4: 同花顺第三通道实现

**输入**：Step 2/3 的客户端接口模式
**输出**：同花顺 basic API 第三降级通道

- [ ] 实现 `ThsFinanceClient`
- [ ] 三表端点：`basic.10jqka.com.cn/api/stock/finance/{code}_{type}.json`
- [ ] 嵌套 JSON 解析（flashData 结构）
- [ ] 中文数值解析（"2657.05亿" → decimal）
- [ ] TLS 1.2+ 强制配置
- [ ] 限速控制（1-2 req/s）
- [ ] 字段映射到统一 `FinancialReport` 模型
- [ ] 单元测试：模拟同花顺 JSON → 验证解析

### Step 5: 三通道编排与降级引擎

**输入**：Step 2/3/4 的三个客户端
**输出**：统一的 `FinancialDataCollector` 服务

- [ ] 实现 `FinancialDataCollector`（策略模式编排三通道）
- [ ] 降级逻辑：A(15s超时) → B → C
- [ ] 采集日志：每次记录通道选择、耗时、结果 → `CollectionLog`
- [ ] 并发控制：单股串行，多股可并行（限并发数）
- [ ] 增量策略：只拉取比本地最新更新的报告期
- [ ] 集成测试：模拟各通道失败组合

### Step 6: 额外数据源采集（分红/融资融券）

**输入**：Step 2 的 Eastmoney 客户端
**输出**：分红、融资融券数据入库

- [ ] 分红数据：RPT_SHAREBONUS_DET → `DividendRecord` 文档
- [ ] 融资融券：RPTA_WEB_RZRQ_GGMX → `MarginTradingRecord` 文档
- [ ] LiteDB 模型扩展
- [ ] 集成到 `FinancialDataCollector` 的采集流程
- [ ] 单元测试

### Step 7: cninfo PDF 下载与三路解析

**输入**：Step 1 的存储结构
**输出**：PDF 下载 + 三路文本提取

- [ ] 实现 cninfo 公告查询：`hisAnnouncement/query` POST
- [ ] 实现 PDF 下载：`static.cninfo.com.cn/{adjunctUrl}`
- [ ] 文件存储：`App_Data/financial-reports/{symbol}/{year}/`
- [ ] 三路 PDF 文本提取：Docnet / PdfPig / iText7
- [ ] 投票一致性验证
- [ ] LLM 叙述摘要（复用现有 LLM Provider）
- [ ] 存储解析结果到 LiteDB
- [ ] 单元测试：模拟 PDF 内容验证解析

### Step 8: 定时调度与按需采集 Worker

**输入**：Step 5 的 Collector + Step 7 的 PDF 管线
**输出**：完整的 Worker Service 可独立运行

- [ ] BackgroundService 定时调度（每日 15:30+）
- [ ] 中国A股交易日判断
- [ ] 配置读取：自选股列表 / 全量、起始时间
- [ ] 按需单股采集 HTTP 触发端点
- [ ] 采集进度报告（当前进度/总量/预计剩余）
- [ ] 健康检查端点
- [ ] 集成测试

### Step 9: API 层 - 财报查询端点

**输入**：Step 1-8 的 LiteDB 数据
**输出**：前端可调用的 REST API

- [ ] `GET /api/stocks/{symbol}/finance/summary` → 指标摘要
- [ ] `GET /api/stocks/{symbol}/finance/statement` → 报表明细
- [ ] `GET /api/stocks/{symbol}/finance/trend` → 趋势数据
- [ ] `POST /api/stocks/{symbol}/finance/collect` → 触发采集
- [ ] `GET /api/system/finance-worker/status` → Worker 状态
- [ ] `PUT /api/system/finance-worker/config` → 更新配置
- [ ] 请求 DTO 和响应 DTO 定义
- [ ] 路由注册到 StocksModule 或新建 FinanceModule

### Step 10: MCP 工具集成 + LLM 提示词更新

**输入**：Step 9 的 API 层 + LiteDB 数据
**输出**：所有 LLM Agent 可正确调用财报数据

> **关键发现**：现有 LLM 通过中文 Prompt 嵌入工具描述发现 MCP 工具（非 OpenAI function schema）。
> 仅注册端点不够，必须同步更新角色合约和提示词模板，LLM 才能"知道"新工具存在。

#### Step 10a: 新建 MCP 端点

- [ ] `StockMcpToolNames.cs` → 新增常量 `FinancialReportMcp`、`FinancialTrendMcp`
- [ ] `StocksModule.cs` → 注册 `/api/stocks/mcp/financial-report`、`/api/stocks/mcp/financial-trend`
- [ ] `IMcpToolGateway` / `McpToolGateway.cs` → 新增 `GetFinancialReportAsync`、`GetFinancialTrendAsync`
- [ ] `StockCopilotMcpService.cs` → 实现从 LiteDB 查询财报数据逻辑
- [ ] DTO 定义：`FinancialReportDataDto`、`FinancialTrendDataDto`（遵循 `StockCopilotMcpEnvelopeDto<T>` 模式）
- [ ] `/mcp/financial-report`：返回指定股票指定报告期的三表 + 关键指标
- [ ] `/mcp/financial-trend`：返回多报告期对比趋势（营收/净利/ROE/毛利率等）

#### Step 10b: 扩展现有 MCP 端点

- [ ] `/mcp/fundamentals` → 扩展 `StockCopilotFundamentalsDataDto`，添加来自 LiteDB 的深度财报字段
- [ ] `/mcp/company-overview` → 补充股本结构、市值等字段（如 LiteDB 有数据）
- [ ] `/mcp/shareholder` → 补充高管持股变动数据
- [ ] `/mcp/news` → 补充公司公告来源（来自 cninfo PDF 元数据）

#### Step 10c: Research 流程 LLM 提示词更新

> Research Workbench 使用 15 个角色（CompanyOverviewAnalyst, FundamentalsAnalyst 等），
> 工具通过 `StockAgentRoleContractRegistry` 绑定角色，通过 `TradingWorkbenchPromptTemplates` 在 Prompt 中描述。
> Research 流程是**预分派模式**（工具并行调用后结果注入 Prompt，LLM 不自主选择工具）。

- [ ] `StockAgentRoleContractRegistry.cs` → 在 `FundamentalsAnalyst` 角色的 `PreferredMcpSequence` 中追加 `FinancialReportMcp`、`FinancialTrendMcp`
- [ ] `TradingWorkbenchPromptTemplates.cs` → 更新 `FundamentalsAnalyst` 的系统提示词：
  - 描述 `FinancialReportMcp` 返回的数据结构（三表明细 + 关键指标）
  - 描述 `FinancialTrendMcp` 返回的数据结构（多期趋势对比）
  - 指导 LLM 如何使用新数据进行深度财务分析
  - 新增分析维度：盈利质量、资产结构、现金流健康度、偿债能力
- [ ] 确保现有 `Fundamentals` 数据与新 `FinancialReport` 数据不重复、互补展示

#### Step 10d: Recommend 流程 LLM 提示词更新

> Recommend 系统使用 13 个角色（MacroAnalyst, LeaderPicker, GrowthPicker 等），
> 工具通过 `RecommendRoleContractRegistry` 的 `ToolHints` 列表指定，
> 通过 `RecommendToolDispatcher` 路由执行。
> Recommend 流程是**工具调用循环模式**（LLM 自主选择调用哪个工具）。

- [ ] `RecommendRoleContractRegistry.cs` → 给以下角色添加 ToolHints：
  - `LeaderPicker` → 添加 `financial_report`、`financial_trend`
  - `GrowthPicker` → 添加 `financial_trend`（关注成长趋势）
  - `ChartValidator` → 添加 `financial_report`（基本面交叉验证）
  - `StockBull` / `StockBear` → 添加 `financial_report`、`financial_trend`
  - `RiskReviewer` → 添加 `financial_report`（偿债/流动性风险）
- [ ] `RecommendToolDispatcher.cs` → 添加 dispatch case：
  - `"financial_report"` → 调用 `McpToolGateway.GetFinancialReportAsync`
  - `"financial_trend"` → 调用 `McpToolGateway.GetFinancialTrendAsync`
- [ ] `RecommendPromptTemplates.cs` → 更新相关角色的 Prompt：
  - 描述新工具名称、用途、返回数据格式
  - LeaderPicker：用财报确认行业龙头的财务优势
  - GrowthPicker：用趋势数据验证营收/利润增长
  - RiskReviewer：用财报评估偿债能力和现金流风险

#### Step 10e: 访问控制与质量

- [ ] `RoleToolPolicyService.cs` → 为新工具设置 `local_required` 访问模式
- [ ] 新工具的 `evidence` 字段：标注数据来源（Eastmoney / datacenter / 同花顺 / LiteDB 缓存）
- [ ] `freshnessTag` 根据数据时效性标记（real-time / cached / stale）
- [ ] 单元测试：验证 MCP 端点返回格式、角色筛选、Prompt 注入

#### 需修改的完整文件清单

| 文件 | 修改类型 |
|------|---------|
| `StockMcpToolNames.cs` | 新增常量 |
| `StocksModule.cs` | 新增2个端点注册 |
| `McpToolGateway.cs` + `IMcpToolGateway` | 新增2个方法 |
| `StockCopilotMcpService.cs` + 接口 | 新增2个数据查询实现 |
| `StockAgentRoleContractRegistry.cs` | FundamentalsAnalyst 扩展 |
| `TradingWorkbenchPromptTemplates.cs` | FundamentalsAnalyst Prompt 更新 |
| `RecommendRoleContractRegistry.cs` | 5个角色 ToolHints 扩展 |
| `RecommendToolDispatcher.cs` | 2个新 dispatch case |
| `RecommendPromptTemplates.cs` | 5个角色 Prompt 更新 |
| `RoleToolPolicyService.cs` | 新工具访问策略 |
| 新增 DTO 文件 | FinancialReportDataDto, FinancialTrendDataDto |

### Step 11: 前端 - 财务报表 Tab

**输入**：Step 9 的 API
**输出**：右侧边栏第5个Tab可展示财报数据

- [ ] `StockFinancePanel.vue` 容器组件
- [ ] `FinanceSummaryCard.vue` 关键指标卡片
- [ ] `FinanceTrendChart.vue` 趋势迷你图
- [ ] `FinanceStatementTable.vue` 报表明细
- [ ] `FinanceCollectionStatus.vue` 采集状态
- [ ] 注册到 `SidebarTabs.vue` 的 Tab 数组
- [ ] 数据请求接入 Step 9 的 API
- [ ] 按需采集触发（无数据时自动调用 collect）
- [ ] 前端单元测试

### Step 12: 前端 - 设置页 Worker 配置

**输入**：Step 9 的配置 API
**输出**：设置页中可配置 Worker

- [ ] 财报采集配置卡片组件
- [ ] 启停开关、范围选择、起始时间
- [ ] 存储占用实时显示
- [ ] 估算存储量显示
- [ ] 保存配置调用 `PUT /api/system/finance-worker/config`
- [ ] 前端单元测试

### Step 12 补充: 采集测试面板

**输入**：Step 9 的 API + Step 8 的 Worker
**输出**：设置页内的采集测试面板

- [ ] `POST /api/system/finance-worker/test-collect` API（触发单股测试采集）
- [ ] SSE 实时日志流端点（`/test-collect/{taskId}/stream`）
- [ ] 采集结果查询端点（`/test-collect/{taskId}/result`）
- [ ] Worker 端实现：测试采集模式（单股、指定通道、日志回调）
- [ ] `FinanceTestPanel.vue` 前端组件
  - [ ] 股票代码输入 + 通道选择
  - [ ] 实时日志流展示（滚动日志框）
  - [ ] 结果预览（三表摘要 + 分红 + PDF状态）
  - [ ] 日志导出功能
- [ ] 嵌入设置页的配置区
- [ ] 前端单元测试

### 依赖关系图

```
Step 1 (项目搭建+LiteDB)
  ├─→ Step 2 (Eastmoney主力)
  │     ├─→ Step 3 (datacenter备用)
  │     ├─→ Step 6 (分红/融资融券)
  │     └─→ Step 5 (三通道编排) ←── Step 3, Step 4
  ├─→ Step 4 (同花顺)
  └─→ Step 7 (cninfo PDF)
        │
Step 5 + Step 7
  └─→ Step 8 (Worker完整调度)
        └─→ Step 9 (API层)
              ├─→ Step 10 (MCP工具)
              ├─→ Step 11 (前端Tab)
              └─→ Step 12 (前端配置+测试面板)
```

---

## 10. 实现完成总结 (2026-04-05)

### 全部 12 步已完成

| Step | 内容 | 关键产出 |
|------|------|----------|
| 1 | Worker 项目 + LiteDB 存储 | `FinancialWorker/`, `App_Data/financial-data.db` |
| 2 | 东方财富 emweb 客户端 | `EastmoneyFinanceClient.cs` |
| 3 | 东方财富 datacenter 客户端 | `EastmoneyDatacenterClient.cs` |
| 4 | 同花顺客户端 | `ThsFinanceClient.cs` |
| 5 | 三级降级编排器 | `FinancialDataOrchestrator.cs` |
| 6 | 分红 + 融资融券 | `DividendCollector.cs`, `MarginTradingCollector.cs` |
| 7 | PDF 采集管线 | `CninfoClient.cs`, `PdfVotingEngine.cs`, `FinancialTableParser.cs`, `PdfProcessingPipeline.cs` |
| 8 | Worker 调度 + HTTP API | `FinancialCollectionScheduler.cs`, port 5120 |
| 9 | 主 API 集成 | `FinancialDataReadService.cs`, `StocksModule.cs` 扩展 |
| 10 | MCP + LLM 集成 | `FinancialReportMcp`, `FinancialTrendMcp`, 角色 prompt 注入 |
| 11 | 前端财务报表 Tab | `FinancialReportTab.vue`, 8 个单元测试 |
| 12 | 管理员测试面板 | `FinancialDataTestPanel.vue`, App.vue 集成 |

### 架构决策

| 决策项 | 选择 |
|--------|------|
| 财务数据存储 | LiteDB 5.0.21（与 SQLite 并行） |
| 主 API 访问方式 | 直接 LiteDB ReadOnly=true |
| Worker 端口 | 5120（主 API 5119） |
| PDF 解析 | Docnet + PdfPig + iText7 三引擎投票 |
| 数据源优先级 | emweb(3) → datacenter(2) → 同花顺(1) → cninfo PDF(0) |

### 测试结果

- 后端：455 passed / 0 failed（含 10 个 PDF 管线专项测试）
- 前端：180 passed / 0 failed（含 8 个 FinancialReportTab 测试）
- 零容忍验收：5 项阻塞修复后二轮通过
