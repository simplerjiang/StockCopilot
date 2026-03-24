# 2026-03-23 已解决 Bug 归档

- 说明：本文件归档 2026-03-23 从 `.automation/buglist.md` 迁出的已解决 Bug 历史记录。
- 当前主清单：见 `.automation/buglist.md`。

## Bug 1: 情绪轮动页不可用，后端板块接口稳定 500

- 严重级别：高
- 复现步骤：
	1. 打开首页。
	2. 点击顶部 `情绪轮动`。
	3. 页面立即显示 `情绪轮动数据加载失败`。
- 实际结果：
	- 页面顶部快照全部是 `0` / `暂无快照`。
	- Browser console 报错：`GET /api/market/sectors?boardType=concept&page=1&pageSize=12&sort=strength => 500`。
	- 命令行复测 `GET http://localhost:5119/api/market/sectors?page=1&pageSize=3` 稳定返回 500。
- 预期结果：
	- `情绪轮动` 应能正常展示板块榜、阶段快照、比较窗口数据。
- 用户指导意见（来自人）：
	- 纳入 `GOAL-AGENT-002-P0` 运行态稳定性收口。
- 修复结果：
	- 已在 `MarketSentimentSchemaInitializer` 增补 SQLite 幂等补列与索引补齐，开发/本地 SQLite 回退场景下不再因旧表缺列导致 `/api/market/sectors` 500。
	- 2026-03-22 二次修复：定位到 `SectorRotationQueryService` 在 SQLite 上对 `decimal` 字段执行 `ORDER BY / THEN BY` 会触发 `System.NotSupportedException`；现已改为“先取最新快照，再在内存排序后分页/截断”，并新增 SQLite 回归测试锁定 `/api/market/sectors` 与 `/api/market/mainline`。
- 复测结果：
	- 2026-03-22 重新打开后仍可稳定复现，当前未通过。
	- Browser MCP：页面仍显示 `情绪轮动数据加载失败`，顶部快照回落为 `0 / 暂无快照`，实时总览区域停在 `加载中...`。
	- 命令行/API：`/api/market/sentiment/latest`、`/api/market/sentiment/history?days=10`、`/api/market/realtime/overview` 为 200，但 `/api/market/sectors?boardType=concept&page=1&pageSize=3&sort=strength` 仍为 500；`/api/market/mainline?boardType=concept&window=10d&take=6` 也返回 500；`/api/market/sectors/realtime?...` 为 200 但 `items=[]`。
	- 当前判断：Bug 1 持续存在，而且已扩大为“板块分页/主线接口异常 + 页面整屏失败态”。
	- 2026-03-22 本轮二次修复后已通过：命令行复测 `GET /api/market/sectors?boardType=concept&page=1&pageSize=3&sort=strength` 与 `GET /api/market/mainline?boardType=concept&window=10d&take=3` 均返回 200 且正文包含板块数据；Browser MCP 刷新进入 `情绪轮动` 后已看到板块榜、详情侧栏和顶部快照，不再出现 `情绪轮动数据加载失败`。

## Bug 2: 股票图表终端空白，切换周期也没有真正走轻量图表接口

- 严重级别：高
- 复现步骤：
	1. 打开 `股票信息`。
	2. 查询或点击最近查询中的 `浦发银行 sh600000`。
	3. 观察 `专业图表终端`，再切换 `日K图 / 月K图 / 年K图`。
- 实际结果：
	- 图表区域持续显示 `暂无 K 线数据`。
	- Browser network 中没有看到 `/api/stocks/chart?...` 请求。
	- 页面改为重复请求 `/api/stocks/detail/cache?...interval=day|month` 和 `/api/stocks/detail?...interval=day|month`。
	- 后端命令行直测 `GET /api/stocks/chart?symbol=sh600000&interval=day&count=60` 是 200 且返回正文，说明不是源数据缺失，而是前端链路没有把图表接口真正用起来。
- 预期结果：
	- 选股后应展示实际分时/K线图。
	- 周期切换应走 README 声明的 `/api/stocks/chart` 轻量链路，而不是详情聚合链路。
- 用户指导意见（来自人）：
	- 作为股票终端轻链路回归项持续复核。
- 修复结果：
	- 已由 `MANUAL-20260319-CHART-PERF` 收口：前端切换图表周期只请求 `/api/stocks/chart`，不再退回 `/api/stocks/detail` 聚合链路。
- 复测结果：
	- 2026-03-22 本轮仍未通过，但当前形态与最初记录不同。
	- Browser MCP：`sh600000` 的 `专业图表终端` 仍显示 `暂无 K 线数据`；切到 `月K图` 后仍为空白。
	- Browser network：本轮已经真实请求 `/api/stocks/chart?symbol=sh600000&interval=day&includeQuote=true&includeMinute=true` 和 `/api/stocks/chart?symbol=sh600000&interval=month&includeQuote=false&includeMinute=false`，说明“没走轻量链路”的旧问题已不成立。
	- 命令行/API：`/api/stocks/chart?symbol=sh600000&interval=day&includeQuote=true&includeMinute=true` 返回 200，且 payload 含 `kLines=60`、`minuteLines=256`；前端仍显示无数据，当前更像是图表渲染/字段消费不一致，而不是接口未请求。
	- 2026-03-22 本轮追加修复后，`StockInfoTab.vue` 已对股票页关键 GET 请求补上短时重试，避免瞬时 `Failed to fetch / ERR_CONNECTION_REFUSED` 直接把图表区打成永久失败态；新增前端回归测试覆盖“首轮图表请求短暂失败后自动重试成功”。
	- 2026-03-22 打包复测：重新执行 `start-all.bat` 后，Browser MCP 进入 `股票信息 -> 浦发银行 sh600000`，页面已显示 `浦发银行（sh600000）` 与 `专业图表终端`，且页面内不再出现 `暂无 K 线数据` / `暂无分时数据`。
	- 当前判断：Bug 2 以“运行态短时连接波动导致图表空白”的形态已被本轮前端重试修复并通过打包复测。

## Bug 3: 顶部导航暴露了两个纯占位模块，没有任何实际功能

- 严重级别：中
- 复现步骤：
	1. 打开首页。
	2. 点击 `社媒优化`。
	3. 点击 `社媒爬虫`。
- 实际结果：
	- `社媒优化` 页面只显示：`占位模块：后续提供文案改写、标题优化、风格适配等功能。`
	- `社媒爬虫` 页面只显示：`占位模块：后续接入爬虫任务、账号池与采集调度。`
	- 没有任何可执行能力或后端联动。
- 预期结果：
	- 未完成模块不应作为正式导航入口暴露；或者至少应有明确的禁用态/开发中标识，而不是可点击后进入空壳页。
- 用户指导意见（来自人）：
	- 纳入 `GOAL-AGENT-002-P0` 输出安全与占位功能清理范围。
- 修复结果：
	- 已从 `frontend/src/App.vue` 顶部导航移除 `社媒优化` 与 `社媒爬虫` 两个占位模块入口。
- 复测结果：
	- 2026-03-22 Browser MCP 已确认顶部导航不再出现这两个页签；当前代码复核同样已移除。

## Bug 4: 股票推荐输出格式失控，直接把模型推理过程暴露到最终界面

- 严重级别：高
- 复现步骤：
	1. 打开 `股票推荐`。
	2. 点击 `当日股票推荐`。
	3. 等待返回。
- 实际结果：
	- 返回内容直接出现 `Analyzing the Request`、`Refining the Approach`、`Simulating the Search` 等推理式文本。
	- 输出混入英文、Markdown 粗体和长篇自由发挥，不是面向用户的受控结果。
	- 内容主体偏向全球市场/美股风险叙事，不像本项目的 A 股本地优先推荐流。
- 预期结果：
	- 结果应是面向用户的结构化推荐，不应暴露原始推理过程。
	- 推荐内容应遵守本项目的 A 股、本地事实优先和前端展示约束。
- 用户指导意见（来自人）：
	- 纳入 `GOAL-AGENT-002-P0` 用户面向结果脱敏收口。
- 修复结果：
	- 已在前端聊天渲染与保存链路增加 `<think>` / reasoning scaffold 清洗，并补齐 `StockRecommendTab` 定向测试，阻断原始推理式标题直接落到最终界面。
	- 2026-03-22 本轮继续扩展 reasoning scaffold 识别，新增 `Considering the Request`、`Analyzing the Scenario`、`Refining the Strategy`、`before answering` 等标题式脚手架清洗，前端流式推荐单测已覆盖该类标题前缀。
	- 2026-03-23 再次补强前端共享清洗器：`ChatWindow.vue` 与推荐页已统一复用 `frontend/src/utils/reasoningSanitizer.js`，新增覆盖 `Simulating Information Retrieval`、`Interpreting the Data`、`Formulating the Response` 等本轮真实泄露标题，避免推荐流式输出只清掉旧词表、漏掉新标题。
	- 2026-03-23 本轮后续又把共享清洗器从“固定标题词表”提升为“英文元叙事前缀”启发式，新增处理 `I'm currently dissecting...`、`The task is clear...` 一类非标题式推理开场；`StockRecommendTab.spec.js` 已补入对应流式回归样本。
- 复测结果：
	- 2026-03-22 Browser MCP 仍可直接复现，当前未通过。
	- 返回文本首段继续出现 `Considering the Request`、`Analyzing the Scenario`、`Refining the Strategy` 等推理式标题。
	- 输出仍以全球市场/AI 算力/生物医药等泛化叙述为主，并明确写出“2026年3月22日（星期日），全球主要证券交易所均处于休市状态”，不符合本项目 A 股、本地事实优先的受控推荐预期。
	- 2026-03-22 本轮代码与单测已更新，但尚未在稳定浏览器会话中完成同路径复测；后续应在 bug 8 运行态掉线问题稳定后再次走 `股票推荐 -> 当日股票推荐` 路径确认真实 UI 输出。
	- 2026-03-23 代码级复测通过：`npm --prefix .\frontend run test:unit -- src/modules/stocks/StockRecommendTab.spec.js` 已确认流式返回中即使连续出现 `Considering the Request -> Simulating Information Retrieval -> Interpreting the Data -> Formulating the Response`，最终助手内容仍只保留 `你好世界`。
	- 2026-03-23 Browser MCP 新鲜会话复测通过：使用带时间戳的新页面 `?tab=stock-recommend&ts=...` 重新进入推荐页、创建新会话并触发真实请求后，回答已直接从中文正文开始，未再出现 `Initiating Market Analysis` / `Refining Search Strategies` / `Analyzing Current Context` 等英文推理标题。
	- 当前判断：就“股票推荐最终界面泄露推理式标题/脚手架”这一主症状，本轮 Browser MCP 已通过；推荐内容的题材选择与 A 股本地优先度仍值得继续单独跟踪，但不再作为本 bug 的继续阻塞项。

## Bug 5: Developer Mode 直接展示原始 LLM 推理/脏输出，审计日志未做安全收口

- 严重级别：中
- 复现步骤：
	1. 打开 `LLM 设置`，用 `admin / admin123` 登录。
	2. 切到 `治理开发者模式`。
	3. 勾选 `开启 Developer Mode（只读诊断）`。
	4. 查看 `LLM 对话过程日志` 列表。
- 实际结果：
	- 日志列表直接显示类似 `**Analyzing the Request**` 的原始模型推理式输出。
	- 还可见本应只返回 JSON 的任务返回了非 JSON 文案。
	- 当前展示没有对这类脏输出做聚合后的安全收口。
- 预期结果：
	- 开发者日志可以保留审计信息，但不应直接把原始推理文本作为主展示内容泄露到 UI。
	- 应优先展示结构化 request/response/error 摘要，而不是未收口的模型思维流。
- 用户指导意见（来自人）：
	- 纳入 `GOAL-AGENT-002-P0` 开发者模式安全收口。
- 修复结果：
	- 已在 `SourceGovernanceReadService` 做请求脱敏与 reasoning 输出收口；开发者模式界面改为展示请求摘要、返回摘要和原始日志摘要，不再直接外露原始推理文本。
	- 2026-03-22 本轮继续补强：后端新增标题式 reasoning 脚手架识别与标点残留兜底，前端 `SourceGovernanceDeveloperMode.vue` 的日志摘要也同步二次清洗 `Considering the Request` 等标题，避免摘要列表直接透传原始推理开场白。
	- 2026-03-23 再次补强前后端词表：前端开发者模式摘要与后端治理日志摘要现已同步覆盖 `Interpreting the Data`、`Formulating the Response`、`Simulating Information Retrieval` 等新增标题式脚手架，且前端列表摘要改为复用共享清洗器，避免聊天与治理页再次各自漂移。
	- 2026-03-23 本轮后续继续把治理日志摘要从“标题词表”升级为“英文元叙事前缀”收口：后端 `SourceGovernanceReadService` 与前端共享清洗器都新增对 `I'm currently dissecting...`、`Here's how I'm approaching this...`、`The user ... needs a JSON array ...` 这类英文自述前缀的识别与脱敏，后端新增对应回归测试。
	- 2026-03-23 本轮继续补上 Developer Mode 的展示层安全摘要策略：`SourceGovernanceDeveloperMode.vue` 现在会对“非 JSON 且中英混杂的历史脏输出”直接显示 `返回内容不是结构化 JSON，已按安全摘要收口。`，同时若能从原文中提取到合法 JSON，仍保留 JSON 美化视图供审计查看。
- 复测结果：
	- 2026-03-22 本轮部分改善，但仍未通过。
	- 界面文案已经改成“原始 prompt 与推理文本不在界面直接展示”，多数条目也会显示“返回内容包含中间推理，已脱敏。”。
	- 但日志列表首条仍直接显示：`返回：**Considering the Request** Okay, I'm now zeroing in on the core of this request...`，说明审计列表摘要仍在泄露原始模型输出。
	- 前端单测同步出现回归：`SourceGovernanceDeveloperMode.spec.js` 期望 `请求内容/返回内容`，实际仍为 `请求摘要...`。
	- 2026-03-22 本轮代码级复测通过：`dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~SourceGovernanceReadServiceTests"` 12/12 通过，新增覆盖 `**Considering the Request** 最终建议...` 这类标题式泄露；前端 `npm --prefix .\frontend run test:unit -- src/modules/admin/SourceGovernanceDeveloperMode.spec.js` 7/7 通过。Browser MCP 尚未在稳定运行态下重走 Developer Mode 页面，因此本 bug 暂不直接关闭。
	- 2026-03-23 代码级复测继续通过：前端 `SourceGovernanceDeveloperMode.spec.js` 已新增 `Interpreting the Data` / `Formulating the Response` 样本，确认列表摘要不再显示这些标题；后端隔离输出目录下的 `SourceGovernanceReadServiceTests` 也通过新增 `Simulating Information Retrieval -> Interpreting the Data -> Formulating the Response` 回归测试。
	- 2026-03-23 Browser MCP 新鲜会话复测部分通过：重新登录 `治理开发者模式` 并开启 Developer Mode 后，最新日志条目已显示 `返回内容包含中间推理，已脱敏。`，说明新产生的英文元推理输出不再直接暴露到 UI。
	- 2026-03-23 本轮追加前端安全摘要后，Browser MCP 再次复测：历史中英混杂非 JSON 条目在列表中已显示为 `返回内容不是结构化 JSON，已按安全摘要收口。`；点击进入详情弹层时，`返回摘要` 也保持该安全摘要，而合法 JSON 仍通过 `返回 JSON 美化视图` 展示。
	- 当前判断：Bug 5 本轮已通过，可关闭。

## Bug 9: LLM 设置可写但不可清空，空值保存没有实际生效

- 严重级别：高
- 复现步骤：
	1. 打开 `LLM 设置`，用 `admin / admin123` 登录。
	2. 在 `default` provider 中把 `Project` 改成任意非空值，例如 `write-test-20260322`，点击 `保存设置`。
	3. 再把同一字段清空，继续点击 `保存设置`。
	4. 命令行复测 `GET /api/admin/llm/settings/default`，或直接调用 `PUT /api/admin/llm/settings/default` 传入 `"project":""`。
- 实际结果：
	- UI 会提示 `已保存`，看起来像成功了。
	- 但后端读回仍然保留旧值 `write-test-20260322`，空值没有真正落库。
	- 直接走 admin `PUT` 接口传空字符串也同样无效，不是单纯前端问题。
	- 本轮只能通过手工修改运行时文件 `%LOCALAPPDATA%\SimplerJiangAiAgent\App_Data\llm-settings.json` 才把该值恢复为空。
- 预期结果：
	- 可选字段应支持被用户清空；UI 保存成功必须与后端持久化结果一致。
- 用户指导意见（来自人）：
	- 纳入 `GOAL-AGENT-002-P0` 设置持久化修复范围。
- 修复结果：
	- 已修复 `JsonFileLlmSettingsStore`，显式空字符串会真正覆盖旧值，`Project` / `Organization` / `BaseUrl` / `Model` / `SystemPrompt` 均可被清空。
- 复测结果：
	- 2026-03-22 本轮命令行复测未复现。
	- 通过 `/api/admin/login` 登录后，先将 `default` provider 的 `project` 写入 `retest-clear-20260322`，再用 `PUT /api/admin/llm/settings/default` 提交空字符串，读回结果已为空；最后已恢复原值。
	- 当前判断：Bug 9 本轮通过，可维持关闭状态。

## Bug 10: 股票助手发送消息时，消息保存接口间歇性 500，但前端表面仍显示成功

- 严重级别：高
- 复现步骤：
	1. 打开 `股票信息`，选择 `sh600000`。
	2. 在右侧 `股票助手` 点击 `新建对话`。
	3. 输入 `请用一句话说明浦发银行当前最大的风险点。` 并点击 `发送`。
	4. 观察 browser network / console 中的 `/api/stocks/chat/sessions/{sessionKey}/messages` 请求。
- 实际结果：
	- 页面可以看到用户提问和助手回答，似乎发送成功。
	- 但同一次发送过程中，`PUT /api/stocks/chat/sessions/{sessionKey}/messages` 会出现多个请求，且其中部分稳定返回 `500 Internal Server Error`。
	- 这意味着聊天写入链路并不干净，属于“前端有显示，但底层保存过程在报错”的假成功。
	- 本轮实测中，最新会话 `sh600000-1774152043985` 已出现至少两次该 500。
- 预期结果：
	- 一次聊天发送不应伴随保存接口 500。
	- 聊天历史保存应稳定、幂等，不能靠后续重试把表面现象掩盖过去。
- 用户指导意见（来自人）：
	- 纳入 `GOAL-AGENT-002-P0` 聊天假成功修复范围。
- 修复结果：
	- 已将 `ChatWindow.vue` 历史保存改为串行保存队列，停止流式输出期间的重复 PUT 风暴，避免前端表面成功但底层保存过程报错。
- 复测结果：
	- 2026-03-22 本轮 Browser MCP 未再复现 500。
	- 新会话 `sh600000-1774167817597` 创建、回读与多次 `PUT /api/stocks/chat/sessions/{sessionKey}/messages` 均返回 200，未见 `500 Internal Server Error`。
	- 当前判断：Bug 10 本轮通过，但仍可观察到一次发送触发多次 PUT；虽然目前都成功，不再按高优先级故障计。

## Bug 11: 股票助手返回内容仍泄露原始推理式标题，不是收口后的用户答案

- 严重级别：中
- 复现步骤：
	1. 打开 `股票信息`，选择 `sh600000`。
	2. 在 `股票助手` 输入任意简短问题并点击 `发送`。
	3. 观察助手返回文本首段。
- 实际结果：
	- 返回内容前缀直接出现 `Defining the Scope****Analyzing the Data` 这类原始推理式标题。
	- 这是面向用户的聊天面板，不是开发日志或调试面板，但依然暴露了模型中间表达痕迹。
	- 该问题与 `股票推荐`、`Developer Mode` 中发现的同类问题一致，说明收口规则没有覆盖聊天发送链路。
- 预期结果：
	- `股票助手` 应只输出整理后的最终答案，不应出现推理标题、链路标记或脏 Markdown。
- 用户指导意见（来自人）：
	- 纳入 `GOAL-AGENT-002-P0` 用户聊天输出脱敏收口。
- 修复结果：
	- 已在 `ChatWindow.vue` 对流式与最终助手内容统一做 reasoning scaffold 清洗，阻断 `Defining the Scope`、`Analyzing the Data` 等原始推理式标题进入用户聊天面板。
	- 2026-03-23 已把股票助手与股票推荐复用的清洗逻辑抽到 `frontend/src/utils/reasoningSanitizer.js`，并补齐 `Interpreting the Data`、`Assessing Risk Elements`、`Synthesizing Risk Insights` 等仍在真实输出里出现的标题，避免股票助手和推荐页再次各自漏词。
- 复测结果：
	- 2026-03-22 Browser MCP 仍可稳定复现，当前未通过。
	- 新会话回答首段直接出现 `Defining the Scope`、`Interpreting the Data`、`Assessing Risk Elements`、`Synthesizing Risk Insights`，随后才进入中文答案。
	- 当前判断：Bug 11 仍然存在，且与 Bug 4、Bug 5 属于同一类输出收口失效。
	- 2026-03-23 代码级复测通过：`npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js` 已确认股票助手流式返回即使包含 `Defining the Scope****Interpreting the Data**` 与 `Assessing Risk Elements` / `Synthesizing Risk Insights`，保存与恢复后的助手内容仍只保留 `风险提示保持仓位纪律`。
	- 2026-03-23 Browser MCP 新鲜会话复测通过：在 `股票信息 -> sh600000 -> 股票助手` 新建对话并发送 `今天这只股票的风险点是什么？` 后，最新助手回答直接从中文正文开始，未再出现 `Defining the Scope`、`Interpreting the Data`、`Assessing Risk Elements` 等推理式标题。
	- 当前判断：就“股票助手对用户直接暴露推理式标题”这一主症状，本轮 Browser MCP 已通过，可从同类泄露问题中移出。

## Bug 12: `start-all.bat` 启动链误判失败，桌面打包链未按 5119 健康起来

- 严重级别：高
- 复现步骤：
	1. 在运行态异常后执行 `c:\Users\kong\AiAgent\start-all.bat`。
	2. 等待脚本完成打包、启动 packaged desktop 并做健康检查。
- 实际结果：
	- 脚本输出 `Packaged desktop backend did not become healthy in time.` 并以非零退出。
	- 现有日志 `.automation/tmp/backend-run.log` 中出现 `Now listening on: http://localhost:5000`，与脚本固定等待的 `http://localhost:5119/api/health` 不一致。
	- 这会导致启动链即使后端进程已启动，也被误判为失败。
- 预期结果：
	- `start-all.bat` 应与实际 packaged backend 端口保持一致，或从真实监听地址探测健康，不应产生假失败。
- 用户指导意见（来自人）：
	- 待补充
- 修复结果：
	- 2026-03-23 已对桌面打包启动链做端口与超时对齐：`desktop/SimplerJiangAiAgent.Desktop/Form1.cs` 不再在 packaged backend 启动时静默切换到 `5119-5139` 其他端口，而是固定使用 `http://localhost:5119`；桌面内部等待后端健康的超时也从 20 秒提升到 90 秒。
	- 2026-03-23 `start-all.bat` 对 `http://localhost:5119/api/health` 的等待窗口同步从 60 秒提升到 90 秒，避免脚本和桌面宿主对“启动完成”的判断不一致。
- 复测结果：
	- 2026-03-23 已多次重走 `start-all.bat` 打包启动链，frontend build、backend publish、desktop publish 均成功，脚本最终稳定输出 `Packaged desktop started successfully.`。
	- 2026-03-23 当前判断：Bug 12 的“5119 健康检查与真实 packaged backend 端口/超时不一致导致误判失败”已修复，本轮可关闭。

## Bug 13: 治理开发者模式前端单测回归，日志展示文案与用例不一致

- 严重级别：中
- 复现步骤：
	1. 运行 `npm --prefix .\frontend run test:unit`。
	2. 观察 `SourceGovernanceDeveloperMode.spec.js` 结果。
- 实际结果：
	- 用例 `shows paired request and response content with prettified json` 失败。
	- 断言期望首段包含 `请求内容`，实际拿到的是 `请求摘要{"symbol":"600000",...}`。
- 预期结果：
	- 单测断言和页面实际展示应一致；若页面已改成“摘要”模式，用例应同步更新；若页面仍要求展示“内容”，实现应修正。
- 用户指导意见（来自人）：
	- 待补充
- 修复结果：
	- 已将 `SourceGovernanceDeveloperMode.spec.js` 中该用例的期望文案从 `请求内容 / 返回内容` 同步为页面真实展示的 `请求摘要 / 返回摘要`。
- 复测结果：
	- 2026-03-22 已通过 `npm --prefix .\frontend run test:unit -- src/modules/admin/SourceGovernanceDeveloperMode.spec.js` 复测，7/7 通过；该回归项已消失。

## Bug 9: Copilot 工具全部执行后，状态机未推进到 grounded final answer，`起草交易计划` 链路仍阻塞

- 严重级别：高
- 复现步骤：
	1. 打开 `股票信息`，使用 fresh runtime 进入 `http://localhost:5121/?tab=stock-info`。
	2. 查询 `sh600000`。
	3. 提问：`先看这只股票 60 日结构，再核对本地公告有没有新的风险点。`
	4. 依次执行 Copilot 草案中的 `StockKlineMcp` 与 `StockNewsMcp` 两张已批准工具卡。
	5. 点击 `起草交易计划`，等待多 Agent 分析完成后再次点击。
- 实际结果：
	- 两张已批准工具卡都执行完成后，`Copilot 质量基线` 已显示 `工具效率 100%`、`动作卡就绪度 100%`，`起草交易计划` 按钮也已解锁。
	- 但 `受控回答状态` 仍停留在 `needs_tool_execution`，没有进入 grounded final answer。
	- 第一次点击 `起草交易计划` 只会把右侧 Agent 区域切到 `分析中...`，15 秒后仍没有出现 `交易计划草稿` 弹窗。
	- 第二次点击后，浏览器事件明确出现 `http://localhost:5121/api/stocks/plans/draft` 请求失败，弹窗仍未出现。
- 预期结果：
	- 当全部已批准 Copilot 工具执行完毕后，状态机应推进到 grounded final answer，或者至少在 UI 上明确提示还缺少哪一步。
	- `起草交易计划` 应该单击即可完成“必要分析 -> 生成草稿 -> 打开弹窗”链路，不能出现按钮已解锁但实际无法打开草稿的假成功。
- 用户指导意见（来自人）：
	- 纳入 `GOAL-AGENT-002` 收尾复核，按核心链路阻塞处理。
- 修复结果：
	- 已在后端新增 `StockAgentHistoryValidation`，统一校验 `stock_news / sector_news / financial_analysis / trend_analysis / commander` 五类结果是否齐备，且 `commander` 必须成功返回有效 `data`；不完整 history 不再允许被保存成可用于交易计划的 `analysisHistoryId`。
	- `TradingPlanDraftService.BuildDraftAsync(...)` 已接入相同完整性校验，在真正生成交易计划前显式拒绝缺少 commander 的历史，避免按钮看似可用但最终草稿失败。
	- 前端 `StockInfoTab.vue` 已把 `draft_trading_plan` gating 收紧到 grounded final answer `done`、完整 commander history、完整本地 agentResults 三个条件同时满足；`runAgents()` 与 `openTradingPlanDraft()` 都不再把部分结果直接送进 `/api/stocks/plans/draft`。
- 复测结果：
	- 2026-03-24 已运行 `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~TradingPlanServicesTests"`，19/19 通过。
	- 2026-03-24 已运行 `npm --prefix .\frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js`，59/59 通过。
	- 2026-03-24 本轮代码与任务核对确认：`GOAL-AGENT-002-R5-B` 已完成，原 bug 复现场景的根因是“不完整 history 被误当成可起草交易计划的 history”；该路径现已被前后端同时封堵。

## Bug 10: Copilot `Evidence / Source` 面板展示抓取残留导航噪音，证据摘要不可读

- 严重级别：中
- 复现步骤：
	1. 打开 `股票信息`，查询 `sh600000`。
	2. 提交 Copilot 问题并执行 `StockNewsMcp`。
	3. 查看右侧 Copilot 区域中的 `Evidence / Source` 面板。
- 实际结果：
	- 证据列表标题和来源是对的，但正文摘要大量显示网站导航和站点杂质，例如连续出现 `财经 焦点 股票 新股 期指 期权 行情 数据 全球 美股...` 等长串残留文本。
	- 多条证据项都重复这种抓取残留，用户无法从该面板直接读到有效证据摘要。
	- 同一轮页面中的 `个股资讯Agent -> 证据来源` 区块已经能展示较干净的标题与事实条目，说明当前 Copilot 证据卡与更下游的事实展示质量不一致。
- 预期结果：
	- `Evidence / Source` 面板应展示结构化摘要、事实摘录或清洗后的正文片段，而不是原站导航、页头噪音或抓取残留。
	- 面向用户的 Copilot 证据面板应达到至少与 `证据来源` 区块同等级别的可读性。
- 用户指导意见（来自人）：
	- 纳入 `GOAL-AGENT-002` 信息可读性与证据可信度收口。
- 修复结果：
	- 已在 `LocalFactDisplayPolicy` 新增可复用的证据摘要清洗逻辑，优先从 `summary/excerpt/title` 中抽取可读内容，过滤 `财经 焦点 股票 新股 期指 期权 行情 数据 全球 美股 港股 基金` 这类导航噪音，并将摘要裁切为 1-2 句可读片段。
	- `StockCopilotMcpService` 生成本地新闻与外部搜索 evidence 时，已统一改为输出清洗后的 `Excerpt/Summary`，而不是把原始抓取残留直接透传给 Copilot 面板。
	- `StockCopilotSessionPanel.vue` 也补了前端兜底清洗，Evidence 面板默认渲染 `snippet` 而不是原始 `excerpt`，即使后续有旧数据进入浏览器，页面也会优先展示更可读的摘要。
- 复测结果：
	- 2026-03-24 已运行 `dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "StockCopilotMcpServiceTests"`，5/5 通过，新增回归锁定导航噪音会被清理后再进入 Evidence DTO。
	- 2026-03-24 已运行 `npm --prefix .\frontend run test:unit -- .\src\modules\stocks\StockInfoTab.spec.js`，61/61 通过，新增回归锁定 Copilot Evidence 面板显示 clean summary 且不再展示导航脏文本。
	- 当前判断：Bug 10 已修复，后续若继续做“展开全文/查看原文”体验，应作为 R5-C 的增强项，而不是当前 bug 的继续阻塞项。

## Bug 6: 股票页“盘中消息带”混入大量与个股无关的泛新闻

- 严重级别：中
- 复现步骤：
	1. 打开 `股票信息`。
	2. 选择 `sh600000`。
	3. 查看 `盘中消息带` 列表后半段内容。
- 实际结果：
	- 前半段是逐笔成交与公告，后半段混入了与浦发银行无关的泛新闻，例如地方文旅、山火、公交、乡村振兴等。
	- 这会让用户误以为这些消息与当前股票相关。
- 预期结果：
	- `盘中消息带` 应只展示当前标的强相关消息，至少要在来源或筛选上隔离泛新闻。
- 用户指导意见（来自人）：
	- 纳入 `GOAL-AGENT-002-P0` 本地事实展示质量收口。
- 修复结果：
	- 已新增 `LocalFactDisplayPolicy`，个股资讯只保留与 symbol/name/aiTarget 强相关的本地事实，过滤泛板块和大盘噪音。
	- 2026-03-24 追加复核中，`sh600000` 股票页 `盘中消息带` 继续只看到浦发银行公告与强相关条目，未再出现原问题所述的泛新闻混入。
	- 右侧上下文区此前遗留的弱相关板块资讯，在当前 live 样本下也未再构成明显污染，不再单独保留为开放 bug。
- 复测结果：
	- 2026-03-24 Browser MCP fresh 复核：`sh600000` 页面已显示 20 条浦发银行相关公告/资讯，未再出现地方文旅、山火、公交等无关泛新闻。
	- 当前判断：Bug 6 已收口，后续若继续优化“板块上下文相关性阈值”，应作为信息质量增强项，而不是继续占用 buglist 开放位。

## Bug 7: 资讯清洗标题存在明显失真/错字，影响可读性与可信度

- 严重级别：中
- 复现步骤：
	1. 打开 `股票信息`，选择 `sh600000`，等待右侧 `资讯影响` 加载完成。
	2. 或打开 `全量资讯库` 查看列表。
- 实际结果：
	- 出现明显错误标题，例如：
		- `浦发银行：上海浦东发展银行股份有限公司关于诚002025年度业绩说明会的公告`
		- `优先股二期股权发行实践公告`
		- `长龙信誉基金董事长刘元瑞辩`
	- 标题被改写到事实失真，不只是简单翻译问题。
- 预期结果：
	- 清洗后的标题可以润色，但不能篡改原始含义，更不能出现乱码、错字和实体错配。
- 用户指导意见（来自人）：
	- 纳入 `GOAL-AGENT-002-P0` 标题失真抑制收口。
- 修复结果：
	- 已通过 `LocalFactDisplayPolicy.SanitizeTranslatedTitle(...)` 抑制已是清晰中文标题时的失真翻译副本，避免错误标题直接展示到股票页和资讯库。
	- 2026-03-24 追加复核中，股票页与资讯归档抽样均未再看到原记录里的错字/失真标题样例。
- 复测结果：
	- 2026-03-24 live 样本复核：`sh600000` 股票页、`/api/news/archive` 对应页面抽样未再复现原记录中的错字和事实失真标题。
	- 当前判断：Bug 7 已收口；后续若继续扩大全量资讯抽样面，应作为持续质量抽检，而不是当前未修复项。

## Bug 8: 首次查股后的后端存在疑似不稳定崩溃风险

- 严重级别：高
- 复现现象：
	- 在首次 Browser MCP 查股后，切换图表按钮时浏览器开始报 `ERR_CONNECTION_REFUSED`。
	- 随后命令行探测 `http://localhost:5119/api/health` 失败，5119 端口不再监听。
	- 重新执行 `start-all.bat` 后服务恢复。
- 预期结果：
	- 查股与图表切换不应把后端服务打挂。
- 用户指导意见（来自人）：
	- 作为 `GOAL-AGENT-002-P0` 运行态稳定性复核项关闭。
- 修复结果：
	- 2026-03-24 根因进一步收敛到桌面宿主自带的健康探针策略，而非后台自身崩溃：桌面宿主原本使用 `2s` 探测间隔 + `2s` 请求超时 + 连续 `2` 次失败即 `Kill` 并重启托管后台，导致短时抖动会被误判成“后端死亡”。
	- 已在 `desktop/SimplerJiangAiAgent.Desktop/Form1.cs` 修复该策略：
		- 健康探针改为 `5s` 间隔、`5s` 超时；
		- 连续失败阈值提升到 `3` 次；
		- 只有在持续失联达到 `20s` 观察窗后才触发自动恢复；
		- 防止健康检查重入，避免异步 tick 重叠放大失败计数；
		- 页面导航失败时先做一次真实健康检查，后端仍健康则不再直接触发恢复；
		- `StopOwnedBackendProcess()` 显式清理 owned 状态，避免宿主自杀式 stop 被继续当成“异常退出”。
- 复测结果：
	- 2026-03-24 已运行 `dotnet build .\desktop\SimplerJiangAiAgent.Desktop\SimplerJiangAiAgent.Desktop.csproj`，构建通过。
	- 2026-03-24 已运行 `.\scripts\publish-windows-package.ps1`，打包通过，产物 `artifacts\windows-package\SimplerJiangAiAgent.Desktop.exe` 已生成。
	- 2026-03-24 已拉起打包 EXE，并通过本地健康页 `http://localhost:5119/api/health` 回读到 `{"status":"ok"}`，确认 packaged desktop 链路可正常提供健康接口。
	- 当前判断：Bug 8 已从“疑似后台崩溃”落实为“桌面宿主误判短抖动并主动重启”的问题，且修复后 packaged startup smoke 通过，故本轮归档关闭。

## Bug 11: 情绪轮动页“情绪总览”与“实时总览”同时展示互相冲突的数据

- 严重级别：高
- 复现步骤：
	1. 执行 `start-all.bat`，打开 `http://localhost:5119/?tab=market-sentiment`。
	2. 观察首屏顶部 `情绪总览`、`资金与广度`、`涨跌分布桶` 三块区域。
	3. 同时回读接口：`/api/market/sentiment/latest` 与 `/api/market/realtime/overview`。
- 实际结果：
	- 页面同一屏同时展示了两套互相冲突的数据：`实时总览` 显示 `4992 / 299`、`涨停 153 / 跌停 3`、主力净流入 `+82.89 亿`，但 `情绪总览` 卡片却显示 `涨停 / 跌停 0 / 1`、`涨跌家数 0 / 0 / 平盘 0`、热门板块成交占比 `0.00%`。
	- 通过页面内直接 `fetch` 回读确认，这不是纯前端渲染问题，而是 `/api/market/sentiment/latest` 当前真实返回了 `limitUpCount=0`、`advancers=0`、`decliners=0`、`top3SectorTurnoverShare=0`，与 `/api/market/realtime/overview` 中的真实盘面数据同时并存。
	- 用户无法判断应该相信哪一块，页面给出了带有“正式指标”外观的假值。
- 预期结果：
	- 情绪轮动页的核心指标口径应一致，不能在同一屏并列展示互相冲突的总览结果。
	- 如果某一数据源缺失，应明确降级或标记“暂无数据”，而不是默认回填成 0 并伪装成真实行情。
- 用户指导意见（来自人）：
	- 待补充
- 修复结果：
	- 已在 `frontend/src/modules/market/MarketSentimentTab.vue` 增加显示层降级逻辑：当持久化 summary 的涨跌家数、涨跌停或成交占比明显缺失时，页面会自动回退到 `/api/market/realtime/overview` 的实时广度数据，而不是继续展示 0 值假数据。
	- `热门板块成交占比` 在持久化源未同步完成时改为 `待同步`，并新增明确提示文案，向用户说明当前口径是“实时补足 + 部分指标尚未同步”。
	- 同步新增前端回归测试，锁定“实时广度补足”和“占比待同步”行为。
- 复测结果：
	- 2026-03-24 已运行 `npm --prefix .\frontend run test:unit -- src/modules/market/MarketSentimentTab.spec.js`，5/5 通过。
	- 2026-03-24 已运行 `npm --prefix .\frontend run build`，构建通过，仅有既有 chunk-size warning。
	- 2026-03-24 Browser MCP 在 `http://localhost:5119/?tab=market-sentiment` 复测通过：首屏已显示 `153 / 3`、`4992 / 299 / 平盘 15`，`热门板块成交占比` 显示为 `待同步`，并有“已自动改用实时广度补足”的提示，不再出现与实时总览相互冲突的 0 值假指标。
	- 当前判断：作为用户可见页面缺陷，Bug 11 已修复；`/api/market/sentiment/latest` 持久化源自身仍需后续单独跟踪，但不再作为该页面 bug 继续阻塞。

## Bug 12: 情绪轮动页大量板块详情是假丰富状态，涨幅明显但成员拆解、龙头和新闻全空

- 严重级别：中
- 复现步骤：
	1. 打开 `http://localhost:5119/?tab=market-sentiment`。
	2. 保持默认 `概念轮动 / 综合强度 / 10日主线`。
	3. 翻到第 2 页，依次点击 `微盘精选`、`股权转让` 等板块。
	4. 回读对应接口，例如 `/api/market/sectors/BK0803?boardType=concept&window=10d`。
- 实际结果：
	- 多个板块卡片显示涨幅在 `+4%` 到 `+5%`，看起来像是有分析价值的热点，但点进详情后经常只剩一行 `当前没有龙头股快照` 和 `本地事实库暂无该板块新闻`。
	- 以 `股权转让` 为例，接口真实返回 `changePercent=4.42`，但 `advancerCount=0`、`declinerCount=0`、`flatMemberCount=0`、`limitUpMemberCount=0`，`leaders=[]`，`news=[]`。这类详情页对用户几乎没有分析价值。
	- 页面表面上给了完整的“板块详情”结构，实际很多条目只有空壳，占位感很重，属于假丰富信息架构。
- 预期结果：
	- 对于无可用细项数据的板块，不应把详情区伪装成可分析面板。
	- 至少应在列表层提前标明“仅有快照、无明细”，或者在详情区解释为何该板块涨幅存在但成员拆解与龙头数据为空。
- 用户指导意见（来自人）：
	- 待补充
- 修复结果：
	- 已在 `frontend/src/modules/market/MarketSentimentTab.vue` 为稀疏板块增加 `快照有限` 标签，并在详情区新增有限数据警示，不再把空 leaders / 空 news / 全 0 成员拆解伪装成完整详情。
	- 详情区龙头股与新闻空态文案已改为“当前只同步到板块快照，明细待补齐”，明确告诉用户当前是同步不完整，而不是正常意义上的“没有龙头/没有资讯”。
	- 同步新增前端回归测试，锁定稀疏快照的诚实展示行为。
- 复测结果：
	- 2026-03-24 已运行 `npm --prefix .\frontend run test:unit -- src/modules/market/MarketSentimentTab.spec.js`，5/5 通过。
	- 2026-03-24 Browser MCP 复测通过：在 `情绪轮动` 页面点击 `噪声防治` 等稀疏板块后，卡片已显示 `快照有限`，详情区出现“当前板块只有涨幅快照，龙头、成员拆解或资讯尚未同步完整”的警示，并将龙头/新闻空态解释为待补齐，而不是继续伪装成完整分析面板。
	- 当前判断：Bug 12 已修复。

## Bug 13: 情绪轮动页默认排序与卡片排名号口径不一致，列表语义混乱

- 严重级别：中
- 复现步骤：
	1. 打开 `http://localhost:5119/?tab=market-sentiment`。
	2. 保持默认 `排序方式 = 综合强度`，观察第一页列表顺序。
	3. 对照卡片开头的 `#rankNo` 与当前排序语义。
- 实际结果：
	- 默认第一页最靠前的卡片并不是 `#1`、`#2`，而是 `#11 绿色电力`、`#13 新型城镇化`、`#16 数字哨兵` 等。
	- 用户肉眼会天然把卡片左上角的 `#11` 理解成“当前第 11 名”，但该页此时实际是按 `综合强度` 排，不是按 `rankNo` 排。页面把两套排名信息塞进同一视觉主位，语义非常混乱。
	- 这个问题在接口层也能对应上：第一页 `/api/market/sectors?...sort=strength` 返回的是强度排序结果，但每项仍携带另一套 `rankNo`，前端没有做任何解释或区分。
- 预期结果：
	- 当页面按 `综合强度`、`涨幅优先`、`净流入优先` 等排序时，不应继续把另一套 `rankNo` 当作卡片主排名展示。
	- 如果必须展示多个口径，至少需要清楚标注“东财实时榜第 N 名”或“快照排名第 N 名”，避免误导。
- 用户指导意见（来自人）：
	- 待补充
- 修复结果：
	- 已修改 `frontend/src/modules/market/MarketSentimentTab.vue` 的实时板块 merge 逻辑：只有在 `涨幅优先` 或 `净流入优先` 这类实时排序模式下才按实时榜重排，默认 `综合强度` 等排序保持原有列表顺序，不再被 `rankNo` 隐式打乱。
	- 卡片主排序已改为 `当前第 N`，同时把外部参考排名显式标为 `东财#N` 或 `快照#N`，把“当前显示顺序”和“外部榜单顺位”分离展示。
	- 同步新增回归测试，锁定“实时榜 rankNo 不得破坏强度排序”的行为。
- 复测结果：
	- 2026-03-24 已运行 `npm --prefix .\frontend run test:unit -- src/modules/market/MarketSentimentTab.spec.js`，5/5 通过。
	- 2026-03-24 Browser MCP 复测通过：默认首屏卡片已显示为 `当前第1 东财#11 绿色电力` 这类格式，用户能明确区分“当前排序位置”和“东财参考排名”；列表也不再因为实时榜 rankNo 而破坏 `综合强度` 的当前排序语义。
	- 当前判断：Bug 13 已修复。

## Bug 14: 情绪轮动页首屏信息层级失衡，核心列表被大段概览和指标压到下方，页面丑且难用

- 严重级别：中
- 复现步骤：
	1. 打开 `http://localhost:5119/?tab=market-sentiment`。
	2. 不做任何交互，直接观察首屏与首屏以下的信息分布。
	3. 尝试快速找到“最强板块列表”和“当前选中板块详情”。
- 实际结果：
	- 首屏先堆叠了 hero、实时总览、资金与广度、涨跌分布桶、4 张指标卡、历史条，再往下才进入真正的轮动列表和详情。
	- 核心任务其实是“找当前最强板块并看详情”，但页面把大量次级指标放在更强的视觉优先级，导致真正有用的板块列表被压到首屏下方，滚动成本高。
	- 板块卡片本身也过度拥挤：一张按钮里堆了排名、涨幅、综合分、窗口分、扩散、排名变化、情绪、热点、龙头、主线分、龙头稳定分，阅读负担很重，观感也比较乱。
- 预期结果：
	- 页面应该优先让用户在首屏完成“看榜单 -> 选板块 -> 看详情”这一核心动作，其余大盘指标应降级、折叠或并入更紧凑的概览区。
	- 卡片信息层级应收敛，减少一次性灌输的指标数量，避免视觉上像调试面板而不是产品页面。
- 用户指导意见（来自人）：
	- 待补充
- 修复结果：
	- 已在 `frontend/src/modules/market/MarketSentimentTab.vue` 重排页面结构，把榜单工具条、板块列表和详情区整体上移到首屏主视区，将实时概览、历史趋势和其他次级指标降到后面。
	- 卡片文案与层级已收敛为更少的主信息，新增 `PRIORITY BOARD` / `综合强度榜单` 等标题，把页面主任务改回“先看榜单，再选详情”。
	- 页面样式同步调整，首屏不再像一组调试卡片堆叠在榜单前面。
- 复测结果：
	- 2026-03-24 已运行 `npm --prefix .\frontend run test:unit -- src/modules/market/MarketSentimentTab.spec.js`，5/5 通过。
	- 2026-03-24 Browser MCP 复测通过：首屏已先出现榜单工具条、`PRIORITY BOARD` 与板块列表/详情，实时总览和历史条被下移到主工作流之后；当前页面可在首屏直接完成“看榜单 -> 选板块 -> 看详情”的核心动作。
	- 当前判断：Bug 14 已修复。