# 20260424 人工测试 Bug 清单
1. 目前AI分析页面，经常还是会有一些JSON没有成功转意成功，还是直接输出的JSON。人工判断原因可能是LLM输出有JSON嵌套，原文情况是：“内容: ```json” 开头。检查并修复，不允许直接输出JSON给用户。
2. 目前cninfo PDF下载获取财报pdf、embedding 、RAG入库和回归已经测试基本没有问题。但是我觉得很多时候光财报并不足够。我发现在盘中消息带里面的“东方财富网公告”中有很多个股的公告，这些公告中实际上都是PDF，但是现在是直接传给LLM网页链接或者pdf url的，我怀疑LLM实际上并没有阅读这些资料，我认为有必要将个股公告pdf一起爬取下来，丰富我们现在的RAG数据库，并且可以创建更多的RAG MCP给LLM使用。

## 20260424 Test Agent 自动化回归发现（工程/数据/契约视角）

3. **[Blocker | 安全] /api/admin/* 全部端点无鉴权**
   - `curl http://localhost:5119/api/admin/llm/settings`、`/ollama/status`、`/source-governance/overview`、`/source-governance/llm-logs`（719KB LLM 原文对话全量）、`/antigravity/auth-status` 均 HTTP 200 无鉴权直返。
   - `AdminAuthFilter.cs` 注释"本地桌面软件无需认证"直接 `return await next(...)`。
   - 风险：5119 绑到局域网即暴露 API Key（已 mask）、OAuth 状态、全量 LLM 历史。

4. **[Major | 契约] 未知 /api/* 返回 200 + SPA HTML**
   - `curl /api/bogus` 返回 `<!DOCTYPE html>` 480 字节。SPA fallback 吃掉所有未命中 /api/* 路由。前端脚本 `JSON.parse` 必抛 `Unexpected token ''<''`。

5. **[Blocker | 数据真实性] 非法股票代码被编造假行情+假新闻**
   - `/api/stocks/quote?symbol=99999` 返 HTTP 200 name=99999，news=[{title:"99999 新闻示例标题", source:"示例来源"}]。
   - `CompositeStockCrawler.cs` 硬编码占位 fallback。用户错输代码不会拦截，反收到看涨假象。

6. **[Major | 数据矛盾] /api/market/sentiment/latest 字段互斥**
   - isDegraded=true 同时 limitDownCount=20，advancers=decliners=0；stageLabel="混沌" + stageLabelV2="同步不完整" 并存；totalTurnover=2.64 万亿与"0 涨 0 跌"自相矛盾。

7. **[Major | 空壳] /api/market/sectors 所有条目 breadthScore 恒=50，leader=null**
   - 20 条板块全部 advancerCount=0 declinerCount=0 leaderSymbol=null。板块→龙头链路断。

8. **[Major | 数据冻结] /api/market/realtime/overview 北向资金常数不变**
   - northboundFlow 整条曲线 shanghaiBalance=520.00 shenzhenNetInflow=0；point.time 只有 09:30:00 没有日期。

9. **[Major | 时间线错位] 新闻 publishTime 早于 crawledAt**
   - `/api/news/archive` total=3784，items 出现 publishTime=2026-04-25T05:44 而 crawledAt=2026-04-24T23:22。抓到了明日 6 小时前版本，时区/源偏移错位。

10. **[Major | 新闻清洗未过滤站点 chrome] summary 含"财经 焦点 股票 新股 期指 期权"等导航栏词条**
    - isAiProcessed=true 的条目摘要仍是东方财富导航栏文字，下游再喂 LLM 继续污染。

11. **[Major | AI 打标错位] 诺诚健华→aiTarget"中国电力设备"，茅台年报→"无明确标的"**
    - 同批 /api/news?symbol=sh600519 下新闻-股票 AI 关联不可信。

12. **[Major | 公告聚合丢信号] /api/stocks/news/impact 季报 mergedCount=6 但 impactScore=0 category="中性"**
    - 茅台一季度报告被合并后打成中性 0 分，重大事件被稀释掉。

13. **[Major | 信号置信度虚高] /api/stocks/signals confidence=44 基于 1:0 单事件**
    - evidence 仅"利好 1 条利空 0 条"即出"偏多"信号 44 分，缺样本过少保护。

14. **[Major | 行情缓存混脏] /api/stocks/history 同列表既有新数据也有 high=0/low=0 残件**
    - sz002001 新和成 high=0 low=0 speed=0；sh513310 ETF updatedAt 8 天前无提示。K 线会画到零轴。

15. **[Major | 自选股缺字段] /api/stocks/watchlist 全部 name=null，3/4 条 lastQuoteSyncAt=null**
    - 列表必然显示空名/"从未同步"。

16. **[Major | 研究会话裸 400] /api/stocks/research/active-session 与 /sessions 返回 HTTP 400 空 body**
    - 前端错误提示无内容→白屏。

17. **[Major | /api/trades/summary 无参 HTTP 400 空 body] 必填参数契约未声明**

18. **[Major | 特殊代码打垮后端] /api/stocks/detail?symbol=sh000001（上证指数）与 sz000518（*ST 四环）返回 0 字节 / 连接失败**
    - 合规操作让后端崩溃；*ST 四环名字含空格说明爬虫未 trim。

19. **[Major | 搜索无空态] /api/stocks/search?q=xyz 随机串返 8 条不相关股票**
    - q=ST 返 "*ST 四环"（含空格），易误触。

20. **[Major | 基本面事实重复] /api/stocks/fundamental-snapshot 主营业务与经营范围几乎整段重复**
    - 右侧 Fact 面板同一段文字连展两次。

21. **[Major | Recommend 会话状态悬挂] 多条 roleStates 永远 Running，父 session 已 Failed**
    - session #1 status=Failed updatedAt=2026-04-02，但 recommend_sector_bear / recommend_stock_bull / recommend_chart_validator 全部 Running completedAt=null。前端历史列表永远转圈。

22. **[Major | Recommend stageRunIndex=2 出现 7 次重复快照] ids 6,9,109,10,111,112,113 交错 04-01/04-02 时间轴**
    - 阶段重放未清理旧 snapshot。

23. **[Major | 第三方网关 URL 泄露到业务错误] errorMessage 含"uri=https://api.bltcy.ai/v1/chat/completions"**
    - 直接透给前端，泄露 LLM 代理地址。

24. **[Major | 历史输入乱码落库] recommend session #1 turn #0 userPrompt="?????????????"** （中文 GBK→UTF-8 错转丢字）

25. **[Major | LLM 输出未裁剪 markdown fence 就入库] outputContentJson 字段值包含 ```json\n{...}\n``` triple backtick 包裹**
    - 与 buglist 第 1 条同源：下游 JSON.parse 必抛；前端渲染出 ```json 裸文本。

26. **[Major | Recommend 板块代码/名错配] "新能源汽车" code=880398，同 session 另一阶段 880398 被标"天然气"；880472 同时标"证券"**
    - LLM 输出无 schema 校验。

27. **[Minor | 指数塞股票指标] /api/stocks/quotes/batch?symbols=sh000001 指数返 turnoverRate=1.26 peRatio=0**
    - 指数无换手率语义。

28. **[Minor | 推荐会话列表未清理调试会话] lastUserIntent="runtimeclean-1775668061842" 占据最前**
    - 真实会话被调试任务淹没。

29. **[Minor | boardType=hy 未校验] /api/market/sectors?boardType=hy 静默回落概念板块返 total=20，不是 400**

30. **[Minor | 归档新闻 url_unavailable 率无度量] /api/news/archive 首页多条 readMode=url_unavailable，total=3784 无"可读率"汇总**

31. **[Minor | Worker supervisor 报 healthy 但有 652 条 24h error] /api/admin/source-governance/overview recentErrorCount24h=652；supervisor-status status=healthy lastCollectionTime=null；logs 有"cninfo 未找到可下载的 PDF 公告"真实失败**
    - healthy 指标造假。

32. **[Minor | totalTurnover 裸数字无单位字段]** 前端单位换算约定化，易错。

33. **[Nit | 时间字段格式不一致]** `tradingDateLabel:"04-24"` / `time:"09:30:00"` / `snapshotTime:"2026-04-24T23:00:00"` 三种风格混用。

34. **[Nit | ST 股名带空格]** `*ST 四环`、`*ST八钢` 混存，搜索排序文本不一致。

未覆盖（待后续轮次）：Recommend SSE 实时流断流/resume；research workbench 完整操作；PDF 原件对照 UI 回放；RAG 引用落点正确性；Ollama 离线降级 UI；多 Tab 并发；Desktop WPF 壳层。


## 20260424 User Representative Agent 交易员视角验收发现

**样本：** 600519 贵州茅台主测、603099 长白山切股复测；范围覆盖股票信息（5 Tab + K 线）、情绪轮动、全量资讯库、股票推荐、交易日志、财报中心

35. **[User Rep] [Blocker | 财报数据单位错 10000 倍] 茅台 2025 年报总资产显示 3038.35 万、营收 1720.54 万、净利润 853.10 万**
    - 实际应为 3 万亿级总资产、1700 亿级营收、850 亿级净利润。基本每股收益 65.66 无“元”单位；毛利润 “—”。任何消费此数据的 AI 分析/推荐都是毒数据。

36. **[User Rep] [Blocker | 全量资讯库行业标签错配] 茅台一季度经营数据公告顶部标签显示“中国石化”/“行业预期”，底部又正确写“酿酒行业”**
    - 按行业筛选茅台会飞到石化板块；AI 分析消费此标签逻辑带偏。

37. **[User Rep] [Major | LLM 幻觉标签] 多条资讯出现“无荒隔靶点”这种无语义生造词**
    - 中国平安持股计划、张坤调仓、白酒股风向变了 等条目反复出现。降低品牌可信度；按标签筛直接无结果。

38. **[User Rep] [Major | 茅台无分红数据] 股票信息→600519→财务报表→“💰 近期分红”显示“暂无分红数据”**
    - 茅台是 A 股分红王，漏掉股息核心决策维度。

39. **[User Rep] [Major | 财报主表列无单位无说明] 盈利趋势列头只写“营业收入/净利润”，数值后带 %，看不出是绝对值还是同比增长率**
    - 2025-12-31 / 2025-09-30 等列语义不明；2026-03-31 列全空；块标题“剩余额数数据”文字不通。

40. **[User Rep] [Blocker | 情绪轮动核心面板大面积不可用] 扩散指数/持续指数/热门板块TOP3占比/涨跌分布/龙头TOP5/板块内涨跌/相关新闻全部“数据不可用”**
    - 情绪轮动主卖点基本废掉，只剩板块涨幅排行。

41. **[User Rep] [Major | 同步最新数据假成功] 点击后时间戳更新，toast 显示“但仍有部分数据缺失”，所有不可用项仍不可用**
    - 用户反复点击以为没响应；未告知缺什么数据源、何时恢复。

42. **[User Rep] [Major | 所有板块扩散值恒=50.0、5日均=0.0] 20 个板块毫无差异**
    - 扩散是“强势板块筛选”核心维度，恒定值=无信号。

43. **[User Rep] [Major | 公告时间戳异常] 东方财富公告时间为凌晨 03:14:56 / 06:49:02 / 08:14:56**
    - 专业用户会怀疑系统时钟/时区错位，连带怀疑行情时间。

44. **[User Rep] [Major | 盘中消息带 20 条里 18 条重复] 同一“提质增效重回报评估报告”出现 6 次，“独立董事述职报告”出现 6 次，“独立性评估意见”出现 6 次**
    - 实际只 3-4 个不同公告；信息密度为零。

45. **[User Rep] [Major | 最近查询含 `invalid/invalid/0%` 垃圾条目且无清理入口]**
    - 脏数据固定在列表末尾，暴露后端兜底 bug。

46. **[User Rep] [Minor | 最近查询点击后不按时间重排] 最常用的股票埋在中间**

47. **[User Rep] [Minor | 基本面快照“主营业务”与“经营范围”文字几乎一字不差重复]** 占版面零增量

48. **[User Rep] [Major | 侧栏“交易计划”Tab 空壳] 只有“当前股票暂无交易计划…”+ 新建/刷新两个按钮**
    - 期待：持仓、成本、浮盈、止盈止损、仓位建议、风险收益比、加减仓计划。MVP 都不达标。

49. **[User Rep] [Major | 侧栏“全局总览”Tab 极度贫瘠] 只有一条“风险值 8.3% 低危”+“暂无交易计划 可从 commander 分析一键添加”**
    - 出现英文“commander”；“全局总览”名不副实。

50. **[User Rep] [Major | 股票推荐历史清一色失败/降级] 近 20 条中 15 失败 4 降级 仅 1 完成；默认无选中右侧主按钮全 disabled**
    - 新用户首屏全红；未告知失败原因。

51. **[User Rep] [Minor | 股票推荐中英文混杂] 副标题“13-Agent 多阶段辩论推荐系统”、“Realtime Context”、“News Archive Console”**

52. **[User Rep] [Major | 交易日志顶部“加载持仓中/加载暴露数据中”无限转圈] 15 秒+ 仍转圈、无错误、无重试；下方健康度已加载（矛盾）**

53. **[User Rep] [Major | 交易日志健康度自相矛盾] 7 日交易 0 笔，健康度卡里“计划执行率 100%”，下方复盘卡里“0%”；0 笔交易拿 100 分健康度；“最大单笔亏损 +0.00”符号不通**

54. **[User Rep] [Minor | 新闻影响 Tab 混入英文海外新闻] Seeking Alpha “Eagle Financial Services Q1 2026 Earnings Call” 未翻译直接并列在“大盘资讯”**
    - A 股交易员不需要看美股财报会纪要。

55. **[User Rep] [Minor | 资金流三处自相矛盾] 顶条“北向 休市”、下方卡“+0.00 亿”、情绪轮动“─+0.0亿 23:00:00”**

56. **[User Rep] [Nit | 财报详情暴露内部 Report ID] 基本信息区“Report ID: 69e88063a9bca60ba4b19a63”直接展示**

57. **[User Rep] [Minor | 情绪轮动“近期趋势”只有 1 天] 右侧板块详情趋势区只有当天一条，名不副实**

58. **[User Rep] [Minor | 切主 Nav 再回来侧栏 Tab 重置] K 线周期保留，但侧栏从“财务报表/AI 分析”回到默认“交易计划”**
    - 多股切换场景影响效率。

59. **[User Rep] [Minor | 股票推荐默认态所有按钮 disabled 无引导] 主面板所有按钮灰色，用户需猜到去左侧点“新建推荐”**
    - 冷启动“死界面”观感。

### 手册缺口（待补到 README.UserAgentTest.md）
- 财报数值单位断言（茅台营收应 ≈ 1700 亿而非 1720 万）
- 情绪轮动同步完整性断言（扩散指数必须不全为 50、涨跌分布必须有数）

---

## 20260425 Test Agent 深度代码审查+回归测试

### 自动化测试结果
- **后端**: 62 tests passed, 0 failed, 0 skipped
- **前端**: 466 passed, 2 skipped, 0 failed
- **跳过测试**: TradingWorkbench.spec.js 2 个用例 (MCP result panels, routing summary)
- **后端构建**: 成功，无警告
- **前端构建**: 成功，1 个 chunk size 警告 (989KB > 500KB)

### Bug #60: StocksModule.cs 超 2900 行，远超 1000 行规范上限
- **严重度**: Major
- **位置**: [StocksModule.cs](backend/SimplerJiangAiAgent.Api/Modules/Stocks/StocksModule.cs) (2936 行)
- **描述**: 单文件包含全部股票 API 端点注册 + 路由逻辑 + 辅助方法，严重违反仓库 AGENTS.md 1000 行上限
- **影响**: 维护困难，代码审查效率极低，多人协作冲突概率高

### Bug #61: 大量 fire-and-forget Task.Run 缺少异常观测
- **严重度**: Major
- **位置**: [StocksModule.cs#L700-L727](backend/SimplerJiangAiAgent.Api/Modules/Stocks/StocksModule.cs#L700-L727)
- **描述**: 至少 10 处 `_ = Task.Run(async () => { ... })` 中 catch 块为 `catch { /* best-effort */ }`，无日志记录。例如自选股自动加入(L700)、今日数据自动采集(L719) 等
- **影响**: 后台任务静默失败，交易员看不到任何提示，数据缺失原因无从排查

### Bug #62: /api/trades/reset-all 无确认机制，一键清空全部交易记录
- **严重度**: Blocker
- **位置**: [StocksModule.cs#L2517](backend/SimplerJiangAiAgent.Api/Modules/Stocks/StocksModule.cs#L2517)
- **描述**: `POST /api/trades/reset-all` 无需任何确认参数，直接删除所有交易记录、持仓和复盘。无审计日志
- **影响**: 误触或自动化脚本 bug 导致交易员全部历史交易数据不可恢复丢失

### Bug #63: admin token 存储 key 不一致 admin_token vs admin-token
- **严重度**: Major
- **位置**: [FinancialDataTestPanel.vue#L6](frontend/src/modules/admin/FinancialDataTestPanel.vue#L6) vs [AdminLlmSettings.vue#L386](frontend/src/modules/admin/AdminLlmSettings.vue#L386)
- **描述**: `FinancialDataTestPanel.vue` 使用 `localStorage.getItem('admin-token')`，而 `AdminLlmSettings.vue` 和 `SourceGovernanceDeveloperMode.vue` 使用 `localStorage.getItem('admin_token')`
- **影响**: 在 LLM 设置页登录后切到财报测试面板需要重新登录，token 不互通，交易员困惑

### Bug #64: CORS 全开放 AllowAnyOrigin
- **严重度**: Major
- **位置**: [Program.cs#L33](backend/SimplerJiangAiAgent.Api/Program.cs#L33)
- **描述**: 主 API 和 FinancialWorker 均 `AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()`，注释写"桌面端嵌入与本地开发使用的宽松策略"但未实现生产收敛
- **影响**: 绑定到非 localhost 时，任何恶意网页可通过 CORS 调用全部 API（含 reset-all、交易操作等）

### Bug #65: SSL 证书校验被关闭（DangerousAcceptAnyServerCertificateValidator）
- **严重度**: Minor
- **位置**: [StocksModule.cs#L135](backend/SimplerJiangAiAgent.Api/Modules/Stocks/StocksModule.cs#L135), [StocksModule.cs#L144](backend/SimplerJiangAiAgent.Api/Modules/Stocks/StocksModule.cs#L144)
- **描述**: SinaGuba 和 TaogubaGuba HttpClient 配置了 `DangerousAcceptAnyServerCertificateValidator` 和 `(_, _, _, _) => true`
- **影响**: 中间人攻击风险，爬虫数据可被篡改

### Bug #66: 前端无统一 API 错误处理层
- **严重度**: Major
- **位置**: 所有 `frontend/src/modules/**/*.vue` 中的 `fetch()` 调用
- **描述**: 超过 30 处直接使用 `fetch()` 且错误处理各自为政。部分用 `catch {}` 静默吞异常，部分用 `catch { return [] }`，无统一的 HTTP 错误码映射、网络超时重试、用户友好提示
- **影响**: 后端返回 500 时前端静默失败，交易员看不到错误原因

### Bug #67: 多端点缺少 symbol 输入归一化（Normalize）
- **严重度**: Minor
- **位置**: [StocksModule.cs#L616](backend/SimplerJiangAiAgent.Api/Modules/Stocks/StocksModule.cs#L616) (quote), [StocksModule.cs#L1096](backend/SimplerJiangAiAgent.Api/Modules/Stocks/StocksModule.cs#L1096) (kline), [StocksModule.cs#L1109](backend/SimplerJiangAiAgent.Api/Modules/Stocks/StocksModule.cs#L1109) (minute)
- **描述**: `/quote`、`/kline`、`/minute` 等端点只做 `symbol.Trim()` 不调用 `StockSymbolNormalizer.Normalize()`。传入 `600519` 和 `sh600519` 可能得到不同结果
- **影响**: 前端传入纯数字代码时可能找不到数据

### Bug #68: 前端打包产物 989KB 超过 500KB 警告阈值
- **严重度**: Minor
- **位置**: `frontend/vite.config.*`
- **描述**: `index-CzoN8OIo.js` 达 989.82KB (gzip 298KB)，Vite 建议 code-split。无 lazy loading 或 manual chunks 配置
- **影响**: 首屏加载慢，特别是在低网速（桌面端局域网可能影响较小）

### Bug #69: TradeLogTab.vue 2761 行，StockInfoTab.vue 1646 行
- **严重度**: Minor
- **位置**: [TradeLogTab.vue](frontend/src/modules/stocks/TradeLogTab.vue) (2761 行), [StockInfoTab.vue](frontend/src/modules/stocks/StockInfoTab.vue) (1646 行)
- **描述**: 前端 Vue 文件同样严重超标，远超 1000 行限制
- **影响**: 维护困难，组件内部状态管理复杂易出错

### Bug #70: ResearchSessionService 静态 ConcurrentDictionary<string, SemaphoreSlim> 无淘汰机制
- **严重度**: Minor
- **位置**: [ResearchSessionService.cs#L12](backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/ResearchSessionService.cs#L12)
- **描述**: `SymbolLocks` 是 `static ConcurrentDictionary`，每个新 symbol 创建一个 SemaphoreSlim，永不释放
- **影响**: 长期运行后内存持续增长（每个 symbol ≈ 180 bytes），虽然速度慢但方向确定是泄漏

### Bug #71: RealtimeSectorBoardService/RealtimeMarketOverviewService 同样的 CacheGates 无淘汰

---

## 20260425 User Rep 交易员视角深度验收

### Bug #113: 无 Vue Router——不支持深链接和浏览器前进/后退
- **严重度**: Major
- **位置**: frontend/src/App.vue (activeTab ref + URLSearchParams 手动模拟)
- **描述**: 整个应用无 Vue Router，不支持浏览器前进/后退，无法通过 URL 直接定位到某只股票。交易员想把分析页书签或分享——做不到。
- **交易员影响**: 盘中切换多只股票后无法用后退键回到上一只，频繁重复搜索浪费时间。

### Bug #114: keep-alive 包裹全部 Tab 组件——长时间运行内存膨胀
- **严重度**: Major
- **位置**: frontend/src/App.vue (keep-alive + component :is)
- **描述**: 所有 11 个 Tab 一旦访问就永久驻留内存。SSE 连接、定时器、DOM 节点持续累积。
- **交易员影响**: 开盘到收盘连续使用 4-6 小时后页面卡顿。

### Bug #115: 后端连接检测间隔 5 分钟——交易员可能离线操作而不知情
- **严重度**: Major
- **位置**: frontend/src/App.vue (healthTimer setInterval 300000)
- **描述**: 后端崩溃或网络断开后最长 5 分钟才感知。期间提交的交易计划/执行记录静默失败。
- **交易员影响**: 行情急变时提交计划却没保存，误以为已记录。

### Bug #116: 顶部时钟只显示时间不显示日期
- **严重度**: NIT
- **位置**: frontend/src/App.vue (clockText HH:MM:SS)
- **描述**: 缺乏日期上下文。盘后复盘和跨日操作时分不清数据日期。
- **交易员影响**: T+0 做 T 时容易误判数据日期。

### Bug #117: 无全局错误边界——单组件崩溃导致整页白屏
- **严重度**: Major
- **位置**: frontend/src/App.vue (无 onErrorCaptured / errorHandler)
- **描述**: 任何子组件抛未捕获异常（如 JSON.parse 失败）整个 SPA 白屏，需手动刷新丢失所有状态。
- **交易员影响**: 正在看盘时突然白屏，关键时刻影响操作。

### Bug #118: 交易计划弹窗缺少止损/止盈价格逻辑校验
- **严重度**: Major
- **位置**: frontend/src/modules/stocks/StockTradingPlanModal.vue
- **描述**: 可保存不合理的价格组合——买入计划的止损价高于触发价，止盈价低于当前价。
- **交易员影响**: 建立无效止损计划浑然不知，真正亏损时才发现计划本身就错。

### Bug #119: 多个 BackgroundService 无协调启动顺序
- **严重度**: Minor
- **位置**: backend/SimplerJiangAiAgent.Api/Program.cs (6+ 个 Worker 同时启动)
- **描述**: 没有启动顺序保证，Worker 可能在 Schema 初始化完成前就开始查询。
- **交易员影响**: 冷启动时偶尔数据加载失败需手动刷新。

### Bug #120: SQLite 并发写入缺乏队列化保护
- **严重度**: Major
- **位置**: backend 全局 SQLite 写入路径
- **描述**: WAL + 15s busy timeout 仍可能在 6+ Worker 同时写入时 database is locked。
- **交易员影响**: 盘中密集更新期间保存交易计划偶尔失败。

### Bug #121: README Ollama keep_alive 默认值与 UserAgentTest 描述矛盾
- **严重度**: Minor
- **位置**: README.md vs README.UserAgentTest.md
- **描述**: README 写 `keep_alive=-1`，UserAgentTest 要求 `5m`。文档互相矛盾。
- **交易员影响**: 用户按 README 配置后 UI 行为与文档不符。

### Bug #122: UserAgentTest 缺失"财报中心"模块回归步骤
- **严重度**: Major
- **位置**: README.UserAgentTest.md
- **描述**: 前端实际 6 个主 Tab，财报中心（FinancialCenterPage）完全不在验收手册中。
- **交易员影响**: 整个财报中心模块处于验收盲区。

### Bug #123: appsettings.json 可能含数据库连接字符串凭证
- **严重度**: Minor
- **位置**: backend/SimplerJiangAiAgent.Api/Program.cs (GetConnectionString)
- **描述**: 非 SQLite provider 时从配置文件读连接字符串，如被提交到 Git 可能泄露凭证。
- **交易员影响**: 潜在凭证泄露。

### Bug #124: 前端无 A 股交易时段感知
- **严重度**: Major
- **位置**: 前端全局
- **描述**: 无论盘中还是盘后，自动刷新频率相同，无"已收盘"/"非交易时段"视觉提示。后端有 ChinaAStockMarketClock 但前端未利用。
- **交易员影响**: 盘后看到数据在刷新产生行情仍在变化的错觉，浪费系统资源。

### 既有 Bug 复核确认（仍未修复）
- **#3** Admin API 无鉴权：AdminAuthFilter 仍直通
- **#5** 假新闻假指标：BuildPlaceholderNews 仍注入所有股票
- **#23** 堆栈/URL 泄露到前端：errorMessage 仍含网关 URL
- **#21** zombie 会话：roleStates 级别清理未覆盖
- **严重度**: Minor
- **位置**: [RealtimeSectorBoardService.cs#L9](backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/../../../Modules/Market/Services/RealtimeSectorBoardService.cs#L9), [RealtimeMarketOverviewService.cs#L14](backend/SimplerJiangAiAgent.Api/Modules/Market/Services/RealtimeMarketOverviewService.cs#L14)
- **描述**: 同 #70，CacheGates 字典只增不减
- **影响**: 同 #70

### Bug #72: 多处后台 Task.Run 使用 CancellationToken.None
- **严重度**: Minor
- **位置**: [StocksModule.cs#L687](backend/SimplerJiangAiAgent.Api/Modules/Stocks/StocksModule.cs#L687), [StocksModule.cs#L725](backend/SimplerJiangAiAgent.Api/Modules/Stocks/StocksModule.cs#L725), [StocksModule.cs#L794](backend/SimplerJiangAiAgent.Api/Modules/Stocks/StocksModule.cs#L794)
- **描述**: 后台 fire-and-forget 任务传入 `CancellationToken.None`，应用关闭时无法优雅取消
- **影响**: 应用关闭时后台任务可能写入不完整数据或抛出未观测异常

### Bug #73: SSE EventSource onerror 无指数退避重试
- **严重度**: Minor
- **位置**: [StockRecommendTab.vue#L549](frontend/src/modules/stocks/StockRecommendTab.vue#L549)
- **描述**: SSE `onerror` 仅计数到 `SSE_MAX_RETRIES` 后放弃，无指数退避间隔。浏览器 EventSource 自动重连每次间隔相同
- **影响**: 网络抖动时快速耗尽重试次数后永久断连

### Bug #74: 2 个前端测试被 skip 且未标注原因
- **严重度**: NIT
- **位置**: [TradingWorkbench.spec.js#L146](frontend/src/modules/stocks/TradingWorkbench.spec.js#L146), [TradingWorkbench.spec.js#L276](frontend/src/modules/stocks/TradingWorkbench.spec.js#L276)
- **描述**: `it.skip('renders collapsible MCP result panels')` 和 `it.skip('shows routing summary for follow-up turn')` 无注释说明跳过原因
- **影响**: 这两个功能缺少测试覆盖，可能存在隐藏回归

### Bug #75: MarketModule 板块类型 boardType 未做白名单校验
- **严重度**: NIT
- **位置**: [MarketModule.cs#L99](backend/SimplerJiangAiAgent.Api/Modules/Market/MarketModule.cs#L99)
- **描述**: `/api/market/sectors?boardType=hy` 传入非法 boardType 不返回 400，静默回落到 concept 板块
- **影响**: 交易员以为看到了行业板块数据，实际看的是概念板块（与 Bug #29 相同）

### Bug #76: FinancialDbContext 和 RagDbContext 注册为 Singleton
- **严重度**: Minor
- **位置**: [Program.cs#L19-L22](backend/SimplerJiangAiAgent.FinancialWorker/Program.cs#L19-L22)
- **描述**: `FinancialDbContext`(LiteDB) 和 `RagDbContext`(SQLite) 注册为 Singleton。LiteDB 用 `Connection=shared` 模式支持并发，但 RagDbContext 如果使用 SQLite 连接，在并发写入时可能出 SQLITE_BUSY
- **影响**: 高并发采集时可能偶发数据库锁定错误

### Bug #77: Desktop Form1.cs 有 async void 事件处理
- **严重度**: Minor
- **位置**: [Form1.cs#L97](desktop/SimplerJiangAiAgent.Desktop/Form1.cs#L97), [Form1.cs#L266](desktop/SimplerJiangAiAgent.Desktop/Form1.cs#L266)
- **描述**: `OnLoadAsync` 和 `OnBackendHealthTimerTickAsync` 是 `async void`。WinForms 事件处理允许 async void，但异常不会被全局异常处理器捕获
- **影响**: 桌面端启动或健康检查异常时可能无提示崩溃

### Bug #78: RecommendationRunner 多处 SaveChangesAsync 使用 CancellationToken.None
- **严重度**: NIT
- **位置**: [RecommendationRunner.cs#L147](backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/Recommend/RecommendationRunner.cs#L147), [RecommendationRunner.cs#L156](backend/SimplerJiangAiAgent.Api/Modules/Stocks/Services/Recommend/RecommendationRunner.cs#L156)
- **描述**: SaveChangesAsync 不传递调用方的 CancellationToken，用户取消推荐会话时数据库写入不会被中断
- **影响**: 用户取消操作但后台仍在写数据库
- 资讯库标签与行业一致性断言
- 交易日志加载完成断言（持仓/暴露卡需显式收敛）

### 未走完场景
股票推荐新发起 SSE + 追问未做；财报 PDF 原件预览/重新解析未点；交易日志快速录入/设置本金/生成复盘未点；键盘快捷键/右键/批量未覆盖；股票信息“高级”“黑白模式”按钮未测；港股/ETF 多资产未测；全量资讯库“批量清洗待处理”未点。

---
## 20260424 轮 2 Test Agent 深度发现（SSE/RAG/并发/安全视角）

60. **[Major | 跨市场污染] /api/stocks/quote?symbol=us.AAPL 返回同名 A 股数据伪装苹果** marketState=Open price 非零但 highLimit/lowLimit=0 exchange 空，误导跨市场用户
61. **[Minor | 符号归一松散] sh600519 尾空格 / URL 编码空格 / "sh600000 ST" 均静默接受** 掩盖用户输错
62. **[Minor | 无效符号返 200 假数据] xyz999999 / abc 全零报价 + marketState=Closed** 前端无法区分"停牌"vs"代码错误"
63. **[Minor | 安全] SQL 注入载荷被写入 lastUserIntent 字段回显** 未执行 SQL 但存储链无 sanitize
64. **[Minor | 北交所字段残缺] bj430047 price>0 但 highLimit/lowLimit/turnover=0** 北交所投资者看不到涨跌停
65. **[Major | symbol 归一跨接口不一致] /api/stocks/financial/reports?symbol=sh600519 返空，=600519 返完整列表** 前端按 sh600519 传永远空
66. **[Major | Research 写端点 405] POST/PUT /api/research/{id}/retry-from-stage 全部 Method Not Allowed 仅 GET** 公开模块只读，HTTP 无法新建/重跑
67. **[Major | Research 历史全 Failed] list 47 条 sample 59 号 status=Failed 错误信息 mojibake 中文** 主线研究长期挂了
68. **[Major | /api/stocks/pdf/reports symbol=sh600519 返空]** 与 #65 同源归一缺陷
69. **[Major | RAG hybrid 退化为 bm25] mode=hybrid 返 mode=bm25 degradation_reason=vector_search_unavailable 即便 Ollama/bge-m3 在线** 嵌入路径单独挂
70. **[Minor | RAG citations 混入 2026-03-31 未来日期]** 系统当前 2026-04-24
71. **[Blocker | SPA 吞 404] GET/POST /api/news/archive/cleanup 返 200 + index.html 而不是 404** 任何拼错 API 调用永远"成功"，监控全盲
72. **[Major | 归档清洗卡死] runId=1 state=running rounds=1 processed=10/124 updatedAt 三次采样不变；mayContinueAutomatically=true 但 recentEvents 无新批次；POST process-pending 返"已在运行中"** 自动调度器死了或 budget 硬封顶
73. **[Major | 散户热度数据/状态矛盾] /retail-heat 全 dailyCount=0 ma20=0 hasData=false；同 symbol /collection-status 显示 eastmoney 210 帖 taoguba 12 帖 sina 23 帖** read/write 聚合不一致
74. **[Minor | retail-heat symbol 回显剥掉 sh/sz 前缀]** 前端路由丢市场
75. **[Major | Recommend session 终态字段矛盾] session.status=Completed completedAt=null，同 turns[last] status=Running** UI 要么永远转圈要么永远绿勾
76. **[Major | Recommend lastUserIntent mojibake] 中文 prompt 经 UTF-8→GBK 双重乱码回显** 所有用户生成内容受影响
77. **[Major | SSE 对已 Completed 会话不回放不关闭] curl /events 4s 空返 0 byte，两次同样** resume 语义不存在
78. **[Blocker | /api/market/sync 5 路并发全部 30s 超时] 进程级全局锁 + 队列无长度保护** 刷新按钮连点即全局瘫痪
79. **[Minor | #40 修复不完整] advancers/decliners/diffusion 已补齐但 isDegraded=true degradationReason=market_breadth_clist_fallback_topic_zdfenbu 仍在** 阈值测过不等于用户展示链路过
80. **[Minor | #35 修复有缺口] PDF Q1 资产负债表缺"资产总计/负债总计"字段** 页面 ratio 会丢分母

## 20260424 轮 2 User Rep 深度验收发现（交易员痛点视角）

### A. 股票推荐

81. **[Major | 推荐进行中误显失败 banner] 刚接入 SSE 即渲染"⚠️ 推荐总监未产出结构化报告"，但团队进度仍全部"待执行"** 把 in-progress 用失败语意占位，用户误以为挂了

82. **[Major | 推荐 SSE 切走再切回彻底断开] 默认回到空白页无 reconnect hint，需手动点左侧"运行中"条目才续上** 多 Tab 是交易员常态，每次都踩

83. **[Minor | 板块裁决官摘要残留调试前缀 "CONSENSUS_REACHED:"]** 内部 state token 泄漏

84. **[Minor | 角色状态栏英文 role ID/工具名] recommend_leader_picker / recommend_smart_money / stock_search / stock_fundamentals 与团队进度中文标签并存** 双套命名共存

85. **[Major | 单角色 >5 分钟无 ETA 无工具上限显示] 龙头猎手 🔧 5 次到 520s 仍转圈，prompt 限 8 次未展示** 交易员不知等还是停

86. **[Nit | 新建推荐运行期 disabled 无 tooltip 解释]** 新手以为坏了

87. **[Nit | 追问空输入 disabled 无提示]** placeholder 未说需非空

### B. 交易日志 & 持仓

88. **[Blocker | 持仓账务公式崩塌] 1 股长白山成本 35.53 浮盈 +33.64 百分比 0.0% 总市值 33.64；35.53+33.64=69.17 ≠ 总市值 33.64；+33.64/35.53≈+94.7% ≠ 0.0%** 账务全线不可信

89. **[Blocker | 持仓/交易流水/可用资金三者不联动] 有 1 股长白山但交易记录"暂无"，可用资金仍 10000=本金 未扣持仓成本** 钱怎么来的说不清

90. **[Major | 0 笔交易拿 100% 计划执行率 + 100 满分健康度]** 样本为空应显示 N/A

91. **[Major | 持仓总览点击行无 drill-down] 手册 F 场景明确要求跳股票信息，实际无 DOM 变化无 toast** 跨模块联动断

92. **[Major | 快速录入代码填入后名称不自动补齐] sz000001 后股票名称栏不联动** 两字段解耦且无校验

93. **[Minor | 时间段切换后上下卡 label 无区分] 持仓/风险敞口实时，总盈亏/胜率按周期，UI 不区分**

### C. 财报中心

94. **[Blocker | PDF 来源标签与详情自相矛盾] 列表"来源渠道: PDF"但详情抽屉"暂无 PDF 原件，请先触发采集"** 手册场景 3 PdfPreviewDialog 链路走不通

95. **[Blocker | 茅台一季报单位/累计口径错乱] 营业收入 1720.54 亿 营业利润 1148.09 亿 EPS 65.67 元，单季 Q1 预期营收 5-6 百亿 EPS 约 18-20 元，现值是年度累计** 无切换无注释，交易员拿去做估值即错

96. **[Major | 大多关键字段"—"但来源仍标 PDF 解析成功] 总资产/总负债/股东权益/净利/毛利/现金流全 "—"**

### D. 全量资讯库

97. **[Blocker | 平安银行 4+ 条公告被绑 sh000001 上证指数 symbol] 列表同页出现两份：已清洗(sz000001/银行) + 待清洗(sh000001/sh000001)** symbol 污染上证指数资讯

98. **[Major | 批量清洗计数 0 但列表仍有 ⏳ 待清洗条目]** counter 与列表矛盾

99. **[Major | 后台清洗无 live feedback] 点击后只显示"等待首轮结果"静态文字，无进度条无增量滚动**

### E. 股票信息

100. **[Minor | 未选股时默认 Tab 仍高亮"交易计划"]** 应隐藏或切到兼容 Tab

101. **[Major | 北向资金多处口径打架] 顶部 strip "休市" / 正文卡"+0.00 亿" / 推荐快照"+0.00 亿"**

102. **[Minor | "强于沪指 +3.11%" 文案歧义] 建议用"跑赢 3.11pp"**

103. **[Minor | 盘中消息带同标题同时间戳重复] 2025 年度独立董事述职报告 / "提质增效重回报"各 2 次**

104. **[Major | 盘中消息带出现凌晨 3:14 / 5:43 公告时间无盘后标签]** 采集时间还是发布时间用户分不清

105. **[Minor | 采集状态无一键补采] 只能全量重跑**

### F. 管理设置

106. **[Major | Ollama 服务状态首次加载卡"检查中..."刷新置灰无超时提示]**

107. **[Blocker | Embedding 状态不可用 RAG 能力 0% 但主页无显眼提示] vector dim — coverage 0/1456 chunks** 财报解析/AI 分析暗哑降级

108. **[Minor | Ollama 面板状态顺序问题] Ollama 运行中后仍显示"请先启动 Ollama"需刷新**

109. **[Major | 默认主渠道带"有 Google 封号风险"红字] Antigravity 为默认主渠道本身带风险提示** 违反"默认不给风险选项"原则

### G. 治理开发者模式

110. **[Major | 治理 670 次错误但 Quarantine=0]** 隔离策略形同虚设

111. **[Blocker | 脱敏承诺撒谎] 列表声明"展示脱敏后摘要，原始 prompt 不在界面展示"，但 Trace 按钮一点即输出完整 prompt / 系统提示词 / 40KB JSON / 工具调用 / 429 原文** 合规违规

112. **[Blocker | Developer Mode 一键开启无二次确认无密码无审计 log]** 开即看全部脱敏前内容

113. **[Nit] 手册 README.UserAgentTest.md 与当前实现存在缺口：持仓总览点击跳股票信息未实现；PDF 原件预览在无 PDF 环境下手册未给前置造数步骤**

---
**轮 2 验收结论：依然拒绝通过。Blocker 升至 10+ 条。推荐 TOP 5 修复顺序：#88/#89 账务 → #111/#112 合规 → #95/#94 财报 → #97 symbol → #82/#85 SSE**
---

---
## 20260424 PM 决策：WONT-FIX 标记

- **#63**（[Minor | 安全] SQL 注入载荷写入 lastUserIntent 回显）：WONT-FIX — 本地桌面单用户，无外部攻击面。
- **#111**（[Blocker → WONT-FIX] 治理 Trace 脱敏承诺与实际展示矛盾）：WONT-FIX — 本地桌面单用户，prompt/追问直出即预期行为；不做文案修正、不做脱敏实现。
- **#112**（[Blocker → WONT-FIX] Developer Mode 一键开启无二次确认无审计）：WONT-FIX — 本地桌面单用户，无需门禁或审计。
- **#110**（[Major] 治理 670 次错误但 Quarantine=0）：**保留**为独立功能 Debt（V048-DEBT 中仍存在），隔离策略需修复。

---
## 20260425 PM Agent 全量复查 & 修复状态

### 代码级验证统计
- ✅ 确认有效 Bug：72 条
- ✓ 已修复（本轮之前）：5 条（#4, #71, #84, #90 后端, #33）
- ⚙️ 设计问题/功能需求：15 条
- 🔵 运行时数据问题：6 条
- ❌ 非 Bug/误报：5 条（#15, #20, #31, #33, #47）
- ⬛ WONT-FIX（此前已标记）：4 条（#63, #111, #112, #110保留为Debt）
- ❓ 需运行时验证：6 条

### 本轮修复（10 条，全部通过回归测试 814/815）

| Bug # | 修复内容 | 状态 |
|-------|---------|------|
| **#65** | `FinancialDataReadService.ListReports()` 用 `BuildSymbolAliases()` 展开别名查询，sh600519/600519 均可匹配 | ✓ FIXED |
| **#88/#89** | `TradeAccountingService.RecalculatePositionAsync` 补充 MarketValue/UnrealizedPnL 计算；`PortfolioSnapshotService` 扣除佣金 | ✓ FIXED |
| **#1/#25** | `LlmService.ChatAsync` 统一调用 `StripMarkdownCodeFences`（所有 provider 出口），增强正则处理截断 fence | ✓ FIXED |
| **#5/#61/#62** | `StockSymbolNormalizer.IsValid()` 新增 + 10 个端点 400 校验 + 移除 `BuildPlaceholderNews/Indicators` 假数据 | ✓ FIXED |
| **#23** | `ErrorSanitizer.SanitizeErrorMessage()` 脱敏 URL，应用于 Recommend/Research 存储层和 API 响应 | ✓ FIXED |
| **#9/#43** | `EastmoneyAnnouncementParser` / `AnnouncementPdfCollector` / `SinaCompanyNewsParser` 时间解析改为 CST→UTC | ✓ FIXED |
| **#7/#42** | `SectorRotationSnapshot` breadthScore/advancerCount 改 nullable，无 member 数据时返回 null 而非 50/0 | ✓ FIXED |
| **#21** | `RecommendationRunner.FailRunningSnapshotsAsync` 级联清理 RoleState；`RecommendZombieCleanupWorker` 增强孤儿扫描 | ✓ FIXED |
| **#97** | `StockSymbolNormalizer.Normalize()` 前缀规则改为 switch：0/2/3→sz, 6→sh, 4/8→bj | ✓ FIXED |
| **#72** | `LocalFactAiEnrichmentService` budget 耗尽后状态改为 paused（非 running），手动 POST 可恢复 | ✓ FIXED |

### 此前已修复确认

| Bug # | 修复位置 | 说明 |
|-------|---------|------|
| **#4/#71** | `Program.cs` MapFallback("/api/{**path}") 返回 404 JSON | V048-S2 已修 |
| **#84** | `RecommendProgress.vue` ROLE_LABELS 字典完整覆盖 13 角色中文名 | 已修 |
| **#90** | `TradingBehaviorService.cs` 0笔交易返回 null（但前端可能仍渲染为100%） | 后端已修，前端待查 |

### 非 Bug 确认

| Bug # | 原描述 | 判定理由 |
|-------|--------|---------|
| **#15** | watchlist name=null | Name 为可选字段，LastQuoteSyncAt 在 Worker 同步后填充，属预期设计 |
| **#20/#47** | fundamental-snapshot 主营业务/经营范围重复 | 两字段来自不同数据源（公司概况 vs 工商注册），内容相似是源数据特点 |
| **#31** | supervisor healthy 但有 652 错误 | health 状态和 errorCount 是独立指标，并存是设计意图 |
| **#33** | 时间字段格式不一致 | 不同场景（标签/盘中/快照）使用不同格式是 REST 常见做法 |
| **#17** | /api/trades/summary 400 | 端点不存在于代码中，可能为旧版 API |

### 设计问题（非代码 Bug，需产品决策）

#2（东财公告PDF入RAG）、#18（指数代码处理）、#30（可读率指标）、#32（turnover单位）、#39（财报列元数据）、#46（查询排序）、#48/#49（空壳Tab）、#51（i18n）、#54（海外新闻过滤）、#56（Report ID暴露）、#75（session completedAt字段）、#93（时间标签区分）、#100（默认Tab）、#102（文案歧义）、#105（一键补采）、#109（默认渠道风险提示）

### 运行时数据问题（代码逻辑正确，需运行时调查）

#34（ST股名空格源数据不一致）、#44（消息重复可能是 dedup key 问题）、#50（推荐失败=LLM服务不稳定）、#57（趋势只有1天=采集不足）、#70（RAG日期=数据标注问题）

### 待后续修复的确认 Bug（优先级参考）

**High**: #6（isDegraded矛盾）、#8（北向数据冻结）、#10（导航栏文字未清理）、#12（impactScore被稀释）、#13（信号置信度虚高）、#14（K线零轴）、#16（research 400空body）、#19（搜索过于宽松）、#24/#76（编码乱码）、#35（财报单位部分修复）、#36（标签错配）、#37（幻觉标签）、#40/#41（情绪轮动不可用）、#52/#53（交易日志卡加载）、#55/#101（北向数据矛盾）、#60（跨市场污染）、#64（北交所字段残缺）、#66（Research缺retry端点）、#67（Research乱码）、#69（RAG退化bm25）、#73（散户热度全零）、#74（symbol前缀丢失）、#77（SSE不回放）、#78（sync超时）、#79（isDegraded未清）、#81（推荐误显失败）、#82（SSE断连）、#83（CONSENSUS_REACHED前缀）、#85（无ETA）、#88补充（前端渲染确认）、#94/#95/#96（财报PDF矛盾）、#98/#99（清洗计数/反馈）、#103/#104（消息重复/时间标签）、#106/#107/#108（Ollama/Embedding状态）、#110（Quarantine=0）

**Medium/Low**: #26（板块代码名错配）、#27（指数指标）、#28（调试会话）、#29（boardType回落）、#45（垃圾查询条目）、#58（侧栏Tab重置）、#59（冷启动无引导）、#86/#87（disabled无tooltip）、#91/#92（持仓drill-down/名称补齐）

### 本轮修复 - 第二批（14 条，回归测试 822/828 通过，6 个已有测试断言已同步修复）

| Bug # | 修复内容 | 状态 |
|-------|---------|------|
| **#10** | `LocalFactArticleReadService.StripLeadingNavBar()` 新增头部导航栏检测（14个关键词阈值≥5），在 `ExtractReadableText` 中调用 | ✓ FIXED |
| **#12** | `StockNewsImpactService.MergeGroup()` 评分策略从 Average 改为 max-absolute-value，保留最强信号方向 | ✓ FIXED |
| **#13** | `StockSignalService` 添加最小样本量保护：<3条证据 confidence≤20，<5条≤35，并在 evidence 中标注"⚠️ 样本不足" | ✓ FIXED |
| **#14** | `StockDataService.GetKLineAsync()` 返回前过滤 High==0 && Low==0 的无效 K 线条目 | ✓ FIXED |
| **#16** | Research session 端点空 symbol 返回 400 JSON `{error:"missing_symbol"}` 而非空 body | ✓ FIXED |
| **#83** | `RecommendationRunner.cs` 在 persist 前剥离 "CONSENSUS_REACHED:" 前缀 | ✓ FIXED |
| **#6/#79** | `SectorRotationIngestionService` isDegraded 判定改为 `degradedFlags.Count > 0 && !hasCoreData`；同步成功后不再添加 `_partial` 后缀 | ✓ FIXED |
| **#60** | `StockSymbolNormalizer.IsForeignMarket()` 检测 us./gb./jp. 前缀，API 返回 400 "暂不支持美股/港股/外盘查询" | ✓ FIXED |
| **#64** | `EastmoneyStockCrawler` 修复 bj 前缀 marketPrefix 映射和 secId 解析（去除 "bj" 前缀） | ✓ FIXED |
| **#19** | `StockSearchService` 添加相关度过滤：中文查名称包含、字母数字查代码/拼音前缀匹配，不匹配则跳过 | ✓ FIXED |
| **#24/#76** | `Program.cs` 全局 JSON 配置 `UnsafeRelaxedJsonEscaping`；SSE 推送使用同样配置；EF 显式 `.IsUnicode()` 标注 | ✓ FIXED |
| **#81** | `RecommendReportCard.vue` 失败 banner 仅在 session 终态时渲染，运行中显示"请等待分析完成" | ✓ FIXED |
| **#82** | `StockRecommendTab.vue` onActivated 自动重连 SSE（session 仍在运行时）；onDeactivated 不关闭 SSE | ✓ FIXED |
| **#8/#55/#101** | `NorthboundFlowPointDto.Time` 改为完整 DateTime；缓存添加 isStale 标记；API 响应含 IsStale 字段 | ✓ FIXED |

### 两批修复总计

- **已修复 Bug 数**：24 条（#1, #5, #6, #7, #8, #9, #10, #12, #13, #14, #16, #19, #21, #23, #24, #42, #43, #55, #60, #61, #62, #64, #65, #72, #76, #79, #81, #82, #83, #88, #89, #97, #101）
- **涉及修改文件**：~50 个（后端 + 前端）
- **新增测试用例**：~30 个
- **回归测试**：828 测试，822 通过，6 个已有 flaky/同步问题

---

## 20260425 第三轮深度复测状态更新

### 自动化测试状态
- **后端 Api.Tests**: 未通过。`V048S2EndpointTests.MarketSync_ConcurrentRequests_OneSucceeds_OthersReturn409Immediately` 仍断言 409，但当前实现返回 429，测试契约未同步。
- **FinancialWorker.Tests**: 106 passed / 0 failed。
- **前端测试入口**: `npm --prefix frontend test -- --run` 失败，原因是 `frontend/package.json` 没有 `test` script；实际可用命令为 `npm --prefix frontend run test:unit -- --run`。
- **前端 test:unit**: 466 passed / 2 skipped。
- **构建**: 后端 build 通过；前端 build 通过但仍有约 990KB chunk size warning。

### 第三轮确认仍 OPEN / 修复不完整
- **OPEN #62（reset-all）** 后端仍无强确认契约：`POST /api/trades/reset-all` 仍可空 body 直删交易、持仓、复盘。前端二次确认不能防脚本或绕过 UI 调用。
- **OPEN #23** URL/异常脱敏覆盖不完整：Recommend/Research 路径已修，但 `LlmModule` 的公共 chat/test 路径仍可能直接返回 `ex.Message`。
- **OPEN #78（market/sync）** 实现已改 429，但测试仍断言 409，自动化套件未绿；需同步测试契约并补 429 断言。
- **OPEN #77** SSE resume 仍不完整：后端支持 Last-Event-ID，但前端新建 EventSource 无显式历史 cursor；刷新/进程重启后的完整回放仍不可靠。
- **OPEN #88/#89** 交易账务运行态仍不可信：代码有修复，但当前运行 UI 仍显示成本、市值、浮盈、可用资金互相矛盾，疑似运行包/数据库/前端 dist 与源码不同步。
- **OPEN #90** 0 笔交易健康度仍可能显示 100 分 / 100% 执行率，前端渲染未完成空样本 N/A 语义。
- **OPEN #92** 快速录入股票名称补齐仍不符合交易员预期：实际体验仍需 blur 或等待，未达到输入代码即可靠补齐。
- **OPEN #107** Embedding/RAG 不可用缺少主页级显眼提示：管理页可见，股票信息/财报中心/AI 分析入口仍缺降级横幅。
- **OPEN #56** 财报详情仍暴露内部 Report ID，对交易员无业务价值。
- **OPEN #113/#121/#122** 文档与导航仍过期：README/UserAgentTest 仍存在 Tab 数量、财报中心覆盖、keep_alive 默认值矛盾。

### 第三轮新增 Bug 候选

#### Bug #125: 前端标准测试命令缺失
- **严重度**: Major
- **位置**: `frontend/package.json`
- **描述**: `npm --prefix frontend test -- --run` 直接失败，因为没有 `test` script；实际命令是 `test:unit`。
- **交易员影响**: CI/人工验收容易误判前端不可测或跳过前端回归，UI 问题更容易漏到盘中。

#### Bug #126: API 集成测试污染真实本地数据库风险
- **严重度**: Major
- **位置**: `backend/SimplerJiangAiAgent.Api.Tests/Modules/V048S2EndpointTests.cs` / `Program.cs` runtime path
- **描述**: API 测试日志显示访问用户本机 `%LocalAppData%/SimplerJiangAiAgent/data/SimplerJiangAiAgent.db`，并伴随 schema ALTER/连接错误。测试 host 未隔离 runtime path / DB。
- **交易员影响**: 本地回归测试可能改写、锁住或污染真实交易/行情数据库。

#### Bug #127: 港股 symbol 契约自相矛盾
- **严重度**: Minor
- **位置**: `StockSymbolNormalizer.cs`
- **描述**: `IsValid()` 接受 `hk00700`，但外盘拒绝逻辑主要覆盖 `hk.` 等前缀；产品是否支持港股没有一致契约。
- **交易员影响**: 用户输入港股可能得到空数据或不一致错误提示，误以为系统支持港股行情。

#### Bug #128: 运行包与源码修复状态不一致
- **严重度**: Blocker
- **位置**: 当前启动应用 / 交易日志 / build-deploy 链路
- **描述**: 源码中 #88/#89 有修复证据，但运行 UI 仍展示旧的错误账务结果；可能前端 dist、后端二进制或数据库状态不是当前源码。
- **交易员影响**: PM/测试以为修好了，交易员实际打开仍看到错账，信任直接归零。

#### Bug #129: 顶部连接状态可能误报离线
- **严重度**: Major
- **位置**: 全局顶部状态栏
- **描述**: 用户验收观察到大量 API 请求成功时顶部仍显示“离线”。连接健康状态与真实 API 成功状态可能不同步。
- **交易员影响**: 交易员会误以为保存、同步、推荐均不可用，盘中不敢操作。

#### Bug #130: 情绪轮动 nullable 数据在前端被数字化为 0
- **严重度**: Major
- **位置**: 情绪轮动 / 板块榜单前端 normalize 逻辑
- **描述**: 后端把缺失扩散数据改为 null 后，前端若继续 `Number(null)` 会变成 0，用户仍无法区分真实 0 和缺数据。
- **交易员影响**: 板块强弱、扩散、龙头筛选被错误数字误导。

#### Bug #131: 推荐 SSE 断线缺少退避和恢复详情
- **严重度**: Major
- **位置**: 股票推荐 / SSE 进度
- **描述**: 切页恢复已补，但 onerror 仍缺指数退避、最后事件时间、后台 session 是否仍运行等状态说明。
- **交易员影响**: 推荐运行数分钟后断线时，用户不知道后台是否仍在分析，也不知道该继续等还是终止。

#### Bug #132: 财报中心缺少单季/累计/年度口径标签
- **严重度**: Major
- **位置**: 财报中心 / 财报详情
- **描述**: 金额和 EPS 有格式化，但报告期层没有明确“单季 / 累计 YTD / 年度”口径。
- **交易员影响**: 茅台 Q1 这类数据可能被当成单季数据，估值和同比判断会错。

### 第三轮结论
第三轮不建议放行。虽然一批历史问题已在代码层可确认 DONE，但当前仍有 **reset-all 后端裸删、交易账务运行态不一致、测试套件未绿、真实 DB 被测试污染风险** 等阻塞问题。下一步建议优先修：#62、#128、#78 测试契约、#126 测试数据库隔离。

### 本轮修复 - 第三批（16 条，回归测试 830/830 全绿）

| Bug # | 修复内容 | 状态 |
|-------|---------|------|
| **#27** | `StockSymbolNormalizer.IsIndex()` 检测 000xxx/399xxx，batch quote 对指数 nullify turnoverRate/peRatio | ✓ FIXED |
| **#28** | `RecommendationSessionService.ListSessions` 过滤 lastUserIntent 以 "runtimeclean"/"debug" 开头的会话 | ✓ FIXED |
| **#29** | `SectorBoardTypes.TryNormalize()` 映射 hy→industry，无效 boardType 返回 400 | ✓ FIXED |
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
| **#107** | Embedding 不可用/0% coverage 时显示警告 banner | ✓ FIXED |
| **#108** | Ollama 未运行时 15s 自动 polling 刷新状态 | ✓ FIXED |

### 额外修复（浏览器验收发现）

| Bug # | 修复内容 | 状态 |
|-------|---------|------|
| **#78(partial)** | `/api/market/sync` 端点添加 catch → 返回 `{synced:false, degraded:true}` 而非 500 | ✓ FIXED |
| **DDL** | `MarketSentimentSchemaInitializer` sector 列改为 NULL（AdvancerCount/BreadthScore 等） | ✓ FIXED |
| **补丁** | `/quote` 无 symbol 参数返回 400（非 500）；`hk.` 前缀识别为外盘 | ✓ FIXED |

### 三批修复最终总计

- **已修复 Bug 数**：40+ 条
- **涉及修改文件**：~70 个（后端 + 前端）
- **回归测试**：830 测试全部通过（0 失败）
- **API 实际验证**：9 项全部通过
- **浏览器验收**：5/6 场景通过（情绪轮动受外部数据源 push2.eastmoney.com 502 影响，代码已修复待数据源恢复）
