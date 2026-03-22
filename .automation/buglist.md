# 2026-03-22 测试记录

## Bug 模板

### Bug X: 标题

- 严重级别：高 / 中 / 低
- 复现步骤：
	1. 步骤 1
	2. 步骤 2
- 实际结果：
	- 现象 1
- 预期结果：
	- 预期 1
- 用户指导意见（来自人）：
	- 待补充
- 修复结果：
	- 待修复
- 复测结果：
	- 待复测

## 测试 Agent 操作准则

- 测试目标：不仅检查是否报错，还要检查功能是否真的产生了预期效果；“有前端显示、无真实后端效果”按 bug 处理。
- 覆盖方式：命令行直测 API + Browser MCP 实测 UI，二者必须互相校验，不接受只测其中一侧。
- 先后顺序：先跑单元测试，再跑命令行 API，再跑浏览器交互；浏览器验证前尽量确认后端健康。
- 写操作规则：所有保存、创建、编辑、删除、发送类动作，都要做“UI 操作 -> 接口回读/数据回读 -> 必要时二次刷新确认”。
- 读操作规则：所有列表、图表、卡片、日志页，都要确认“显示正确 + 数据来源正确 + 关键接口真实返回”。
- 假成功判定：出现成功提示但接口未持久化、刷新后丢失、网络层存在 4xx/5xx、或输出内容不符合产品约束，均计入 bug。
- 占位功能判定：可点击但只有占位文案、无实际能力、无后端联动的入口，按 bug 处理，不视为“已完成页面”。
- LLM 结果判定：面向用户的页面不应暴露推理过程、脏 Markdown、调试信息、链路标记；Developer Mode 可展示诊断，但也要检查是否越界泄露原始内容。
- 浏览器检查规则：不只看静态渲染，必须点击关键按钮、切换筛选、输入内容、观察状态变化，同时检查 console 和 network。
- 接口检查规则：不只看 HTTP 200；要关注响应正文是否为空、字段是否异常、是否存在稳定 500、是否存在间歇性失败与重试掩盖。
- 环境保护：写操作测试尽量使用可恢复字段和可删除测试数据；若修改了运行时配置，结束前恢复原状态。
- 记录要求：每个 bug 都要写清复现步骤、实际结果、预期结果，并预留“用户指导意见（来自人）”“修复结果”“复测结果”。
- 复测标准：Dev 修复后，必须按同一路径重跑，不接受只看代码或只看单测通过就关闭 bug。

## 已执行验证

- 后端单测：`dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --no-restore`
	- 结果：191/191 通过。
- 前端单测：`npm --prefix .\frontend run test:unit`
	- 结果：108/109 通过，`src/modules/admin/SourceGovernanceDeveloperMode.spec.js > shows paired request and response content with prettified json` 失败；期望出现 `请求内容`，实际仍是 `请求摘要...`。
- 命令行 API 实测：`/api/stocks/*`、`/api/news*`、`/api/market/*`、`/api/stocks/mcp/*`、`/api/admin/*`。
- Browser MCP 实测：`http://localhost:5119/`，覆盖 `股票信息`、`情绪轮动`、`股票推荐`、`治理开发者模式`；本轮顶部导航中已不再出现 `社媒优化`、`社媒爬虫`。
- 写操作专项已实测：
	- `LLM 设置`：`Project` 本轮通过 API 可写入、清空并正确读回；`激活通道切换`、`Provider 启停保存` 本轮通过且已恢复原状态。
	- `交易计划`：通过 API 和前端两条路径完成创建、编辑、删除，结果一致，测试数据已清理。
	- `股票助手`：`新建对话 + 发送消息` 本轮未再出现 `500`，但回答正文仍泄露推理式标题。
	- `治理开发者模式`：登录、开启 Developer Mode、日志刷新本轮可执行，但日志列表首条仍直接显示 `Considering the Request` 等原始 LLM 输出。

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
- 复测结果：
	- 2026-03-22 Browser MCP 仍可直接复现，当前未通过。
	- 返回文本首段继续出现 `Considering the Request`、`Analyzing the Scenario`、`Refining the Strategy` 等推理式标题。
	- 输出仍以全球市场/AI 算力/生物医药等泛化叙述为主，并明确写出“2026年3月22日（星期日），全球主要证券交易所均处于休市状态”，不符合本项目 A 股、本地事实优先的受控推荐预期。
	- 2026-03-22 本轮代码与单测已更新，但尚未在稳定浏览器会话中完成同路径复测；后续应在 bug 8 运行态掉线问题稳定后再次走 `股票推荐 -> 当日股票推荐` 路径确认真实 UI 输出。

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
- 复测结果：
	- 2026-03-22 本轮部分改善，但仍未通过。
	- 界面文案已经改成“原始 prompt 与推理文本不在界面直接展示”，多数条目也会显示“返回内容包含中间推理，已脱敏。”。
	- 但日志列表首条仍直接显示：`返回：**Considering the Request** Okay, I'm now zeroing in on the core of this request...`，说明审计列表摘要仍在泄露原始模型输出。
	- 前端单测同步出现回归：`SourceGovernanceDeveloperMode.spec.js` 期望 `请求内容/返回内容`，实际仍为 `请求摘要...`。
	- 2026-03-22 本轮代码级复测通过：`dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~SourceGovernanceReadServiceTests"` 12/12 通过，新增覆盖 `**Considering the Request** 最终建议...` 这类标题式泄露；前端 `npm --prefix .\frontend run test:unit -- src/modules/admin/SourceGovernanceDeveloperMode.spec.js` 7/7 通过。Browser MCP 尚未在稳定运行态下重走 Developer Mode 页面，因此本 bug 暂不直接关闭。

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
- 复测结果：
	- 2026-03-22 本轮在“盘中消息带”主列表里看到的 20 条都为浦发银行公告，未再复现原描述中的泛新闻混入。
	- 但右侧 `资讯影响 -> 板块上下文` 仍补入大量与银行/浦发银行弱相关的市场新闻，例如 `米其林：下行风险尚未充分反映`、`Unity面临高SBC成本与人工智能威胁，建议减仓`。
	- 当前判断：原“盘中消息带混入泛新闻”现象本轮未复现，但“个股页上下文区混入弱相关泛新闻”的同类问题仍存在，需要继续作为信息相关性问题跟踪。

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
- 复测结果：
	- 2026-03-22 本轮在 `sh600000` 的 `盘中消息带` 与右侧 `资讯影响` 中未再看到原记录里的错字/失真标题样例。
	- 当前样本下暂未复现，但仅覆盖了 `sh600000` 单一标的，建议后续在 `全量资讯库` 再抽样复核，不在本轮直接关闭。

## Bug 8: 首次查股后的后端存在疑似不稳定崩溃风险

- 严重级别：高
- 复现现象：
	- 在首次 Browser MCP 查股后，切换图表按钮时浏览器开始报 `ERR_CONNECTION_REFUSED`。
	- 随后命令行探测 `http://localhost:5119/api/health` 失败，5119 端口不再监听。
	- 重新执行 `start-all.bat` 后服务恢复。
- 当前状态：
	- 本轮重启后未再次稳定复现，因此先记录为“疑似高优先级稳定性问题”。
- 预期结果：
	- 查股与图表切换不应把后端服务打挂。
- 用户指导意见（来自人）：
	- 作为 `GOAL-AGENT-002-P0` 运行态稳定性复核项关闭。
- 修复结果：
	- 本轮复核未再复现“查股后后端挂掉”；同时补上 `/api/stocks/mcp/*` 请求取消统一映射为 499，避免客户端超时/取消被误记成服务端 500，缩小运行态误报面。
- 复测结果：
	- 2026-03-22 本轮已再次复现，不应继续标记为关闭。
	- 现象一：命令行连续探测图表接口期间，随后对 `http://localhost:5119/api/stocks/chart?...` 的请求开始报“无法连接到远程服务器”，紧接着 `http://localhost:5119/api/health` 返回 `__HEALTH_DOWN__`，5119 端口也一度不再监听。
	- 现象二：重新执行 `start-all.bat` 后脚本报 `Packaged desktop backend did not become healthy in time.`，启动链路恢复异常。
	- 现象三：稍后 5119 又被 `SimplerJiangAiAgent.Api.exe` 占用并恢复健康，表现出不稳定的掉线/恢复过程。
	- 2026-03-22 本轮追加缓冲：`frontend/src/modules/stocks/StockInfoTab.vue` 已对股票页内部 GET 请求加入有界重试，仅在可重放的连接类错误下自动补试，避免短暂监听空窗直接放大成整页图表/计划板失败态；新增前端回归测试覆盖图表与交易计划总览两类短时失败恢复。
	- 2026-03-22 打包复测：重新执行 `start-all.bat` 后，Browser MCP 走 `股票信息 -> 浦发银行 sh600000`，期间命令行两次探测 `http://localhost:5119/api/health` 均返回 `{"status":"ok"}`，本轮未再复现“首次查股后后端立刻掉线”。
	- 当前判断：Bug 8 的“前端被短时掉线放大为持续失败态”已先做缓冲修复，但后端是否仍存在更深层的偶发监听中断，暂无法在本轮打包复测中稳定复现；保留为高优先级稳定性观察项更稳妥。

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
- 复测结果：
	- 2026-03-22 Browser MCP 仍可稳定复现，当前未通过。
	- 新会话回答首段直接出现 `Defining the Scope`、`Interpreting the Data`、`Assessing Risk Elements`、`Synthesizing Risk Insights`，随后才进入中文答案。
	- 当前判断：Bug 11 仍然存在，且与 Bug 4、Bug 5 属于同一类输出收口失效。

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
	- 待修复
- 复测结果：
	- 2026-03-22 首次发现，待开发修复后按同路径重测。

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
