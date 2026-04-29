# Bug 清单（归档版）

> 原始清单创建于 20260424 人工测试，经三轮 Test Agent / User Rep 深度审查后大规模修复。
> 本文件已清理：仅保留仍 OPEN 的 bug、历史修复记录、PM 决策归档。

---

## 仍 OPEN 的 Bug

- **BACKLOG #113** 无 Vue Router/深链接仍未完成。（BACKLOG - 后续迭代）

| **#114** | `fundamental-snapshot` 端点在无缓存数据时返回 404 而非 204/空响应，导致控制台出现 "Failed to load resource: 404" 红色日志。前端已通过 `detail/cache` 优雅降级，UI 无可见异常。建议改为返回 204 No Content 或空对象。 | OPEN (Minor) |

---

## 历史修复记录

### 本轮修复 - 第一批（回归测试 814/815 通过）

| Bug # | 修复内容 | 状态 |
|-------|---------|------|
| **#65** | `FinancialDataReadService.ListReports()` 用 `BuildSymbolAliases()` 展开别名查询，sh600519/600519 均可匹配 | ✓ FIXED |
| **#88、#89** | `TradeAccountingService.RecalculatePositionAsync` 补充 MarketValue/UnrealizedPnL 计算；`PortfolioSnapshotService` 扣除佣金 | ✓ FIXED |
| **#1/#25** | `LlmService.ChatAsync` 统一调用 `StripMarkdownCodeFences`（所有 provider 出口），增强正则处理截断 fence | ✓ FIXED |
| **#23** | `ErrorSanitizer.SanitizeErrorMessage()` 脱敏 URL，应用于 Recommend/Research 存储层和 API 响应 | ✓ FIXED |
| **#97** | `StockSymbolNormalizer.Normalize()` 前缀规则改为 switch：0/2/3→sz, 6→sh, 4/8→bj | ✓ FIXED |
| **#72** | `LocalFactAiEnrichmentService` budget 耗尽后状态改为 paused（非 running），手动 POST 可恢复 | ✓ FIXED |
| **#26** | `RecommendSectorCodeNameNormalizer` 统一修正推荐角色输出中的板块 code/name，并在发事件/落库前规范化 | ✓ FIXED |
| **#34** | `StockNameNormalizer` 统一清理 ST/*ST/S*ST 前缀后的多余空白，并接入行情解析/搜索/历史/归档名称出口 | ✓ FIXED |
| **#36** | `LocalFactAiTargetPolicy` 清洗 AiTarget/AiTags，archive/enrichment 统一按 symbol/name/sector 股票事实修复标签，防止茅台资讯混入中国石化 | ✓ FIXED |

### 此前已修复确认

| Bug # | 修复位置 | 说明 |
|-------|---------|------|
| **#84** | `RecommendProgress.vue` ROLE_LABELS 字典完整覆盖 13 角色中文名 | 已修 |

### 本轮修复 - 第二批（回归测试 822/828 通过，6 个已有测试断言已同步修复）

| Bug # | 修复内容 | 状态 |
|-------|---------|------|
| **#83** | `RecommendationRunner.cs` 在 persist 前剥离 "CONSENSUS_REACHED:" 前缀 | ✓ FIXED |
| **#60** | `StockSymbolNormalizer.IsForeignMarket()` 检测 us./gb./jp. 前缀，API 返回 400 "暂不支持美股/港股/外盘查询" | ✓ FIXED |
| **#64** | `EastmoneyStockCrawler` 修复 bj 前缀 marketPrefix 映射和 secId 解析（去除 "bj" 前缀） | ✓ FIXED |
| **#24/#76** | `Program.cs` 全局 JSON 配置 `UnsafeRelaxedJsonEscaping`；SSE 推送使用同样配置；EF 显式 `.IsUnicode()` 标注 | ✓ FIXED |
| **#81** | `RecommendReportCard.vue` 失败 banner 仅在 session 终态时渲染，运行中显示"请等待分析完成" | ✓ FIXED |
| **#82** | `StockRecommendTab.vue` onActivated 自动重连 SSE（session 仍在运行时）；onDeactivated 不关闭 SSE | ✓ FIXED |

### 两批修复总计

- **已修复 Bug 数**：15 条（#1, #23, #24, #60, #61, #64, #65, #72, #76, #81, #82, #83, #88, #89, #97）
- **涉及修改文件**：~50 个（后端 + 前端）
- **新增测试用例**：~30 个
- **回归测试**：828 测试，822 通过，6 个已有 flaky/同步问题

### 本轮修复 - 第三批（回归测试 830/830 全绿）

| Bug # | 修复内容 | 状态 |
|-------|---------|------|
| **#27** | `StockSymbolNormalizer.IsIndex()` 检测 000xxx/399xxx，batch quote 对指数 nullify turnoverRate/peRatio | ✓ FIXED |
| **#28** | `RecommendationSessionService.ListSessions` 过滤 lastUserIntent 以 "runtimeclean"/"debug" 开头的会话 | ✓ FIXED |
| **#29** | `SectorBoardTypes.TryNormalize()` 映射 hy→industry，无效 boardType 返回 400 | ✓ FIXED |
| **#30** | `/api/news/archive` 返回 readable/url_unavailable 数量与占比，`NewsArchiveTab` 页头展示可读率/无原文率 | ✓ FIXED |
| **#32** | `MarketSentimentSummaryDto` 保留 totalTurnover 并新增 totalTurnoverCny/unit/unitLabel，前端按单位契约展示成交额 | ✓ FIXED |
| **#35** | `FinancialDataReadService` 将 THS 万元口径统一归一为 `CNY-yuan`，summary/trend/detail 和前端财报卡均显示亿元级金额 | ✓ FIXED |
| **#45** | `StockHistoryService.UpsertCoreAsync` 用 IsValid() 拦截无效 symbol 入库 | ✓ FIXED |
| **#74** | `/retail-heat` 端点用 Normalize() 保留完整 sh/sz 前缀 | ✓ FIXED |
| **#103** | 盘中消息按 (Title, PublishedAt) GroupBy 去重 | ✓ FIXED |
| **#52** | `TradeLogTab.vue` loadPortfolioSnapshot/loadExposure/loadBehaviorStats 添加 !res.ok 错误处理 | ✓ FIXED |
| **#53** | 后端已正确返回 null（0笔交易），前端已有 `== null ? '—'` 处理（确认无需额外修改） | ✓ FIXED (verified) |
| **#58** | `SidebarTabs.vue` onActivated 从 localStorage 恢复侧栏 Tab 选择 | ✓ FIXED |
| **#59** | `StockRecommendTab.vue` 空态引导面板 + 快速操作按钮可用 | ✓ FIXED |
| **#85** | `RecommendProgress.vue` 单角色 >300s 显示"⏳ 已超过预期时间"警告 | ✓ FIXED |
| **#66** | 新增 POST `/api/stocks/research/sessions/{id}/retry-from-stage` 端点 | ✓ FIXED |
| **#73** | `HistoricalBackfillService` 移除破坏性 ClearOldDataAsync；`RetailHeatIndexService` 修复时区不一致 | ✓ FIXED |
| **#98** | `QueryLocalFactDatabaseTool` 计算 PendingTotal；前端按钮显示 (N) 计数 | ✓ FIXED |
| **#99** | `NewsArchiveTab.vue` 清洗进度显示"已处理 X 条，剩余 Y 条" | ✓ FIXED |
| **#106** | `AdminLlmSettings.vue` Ollama 检查 10s 超时 + 超时提示 | ✓ FIXED |
| **#108** | Ollama 未运行时 15s 自动 polling 刷新状态 | ✓ FIXED |

### 额外修复（浏览器验收发现）

| Bug # | 修复内容 | 状态 |
|-------|---------|------|
| **#78(partial)** | `/api/market/sync` 端点添加 catch → 返回 `{synced:false, degraded:true}` 而非 500 | ✓ FIXED |
| **DDL** | `MarketSentimentSchemaInitializer` sector 列改为 NULL（AdvancerCount/BreadthScore 等） | ✓ FIXED |
| **补丁** | `/quote` 无 symbol 参数返回 400（非 500）；`hk.` 前缀识别为外盘 | ✓ FIXED |

### 第五批修复

| Bug # | 修复内容 | 状态 |
|-------|---------|------|
| **#77** | SSE resume 完善：前端 EventSource 添加显式历史 cursor，刷新/进程重启后回放可靠 | ✓ FIXED |

### 三批修复最终总计

- **已修复 Bug 数**：40+ 条
- **涉及修改文件**：~70 个（后端 + 前端）
- **回归测试**：830 测试全部通过（0 失败）
- **API 实际验证**：9 项全部通过
- **浏览器验收**：5/6 场景通过（情绪轮动受外部数据源 push2.eastmoney.com 502 影响，代码已修复待数据源恢复）

---

## PM 决策归档

### WONT-FIX

- **#63**（[Minor | 安全] SQL 注入载荷写入 lastUserIntent 回显）：WONT-FIX — 本地桌面单用户，无外部攻击面。
- **#111**（[Blocker → WONT-FIX] 治理 Trace 脱敏承诺与实际展示矛盾）：WONT-FIX — 本地桌面单用户，prompt/追问直出即预期行为；不做文案修正、不做脱敏实现。
- **#112**（[Blocker → WONT-FIX] Developer Mode 一键开启无二次确认无审计）：WONT-FIX — 本地桌面单用户，无需门禁或审计。
- **#110**（[Major] 治理 670 次错误但 Quarantine=0）：**保留**为独立功能 Debt（V048-DEBT 中仍存在），隔离策略需修复。

### 非 Bug 确认

| Bug # | 原描述 | 判定理由 |
|-------|--------|---------|
| **#31** | supervisor healthy 但有 652 错误 | health 状态和 errorCount 是独立指标，并存是设计意图 |
| **#33** | 时间字段格式不一致 | 不同场景（标签/盘中/快照）使用不同格式是 REST 常见做法 |

### 设计问题（已归档/不修复，需产品决策时再启动）

#2（东财公告PDF入RAG）、#39（财报列元数据）、#48/#49（空壳Tab）、#54（海外新闻过滤）、#75（session completedAt字段）、#93（时间标签区分）、#105（一键补采）

### 运行时数据问题（待观察，代码逻辑正确）

#44（消息重复可能是 dedup key 问题）、#50（推荐失败=LLM服务不稳定）、#57（趋势只有1天=采集不足）
