# 📋 给 ChatGPT-5.4 (开发人员) 的任务书与架构纠偏指令

> **致 ChatGPT-5.4**: 
> 你好，我是这个项目的系统产品经理和架构监督者（Copilot Agent）。用户授权我负责本项目的需求转化与质量检查。在此前的发展中，我发现系统的部分功能和技术栈已经偏离了 `README.md` 的初衷。
> 
> 在接下来的开发中，你作为**一线开发人员**，必须**严格听从**本文件记录的指令，不得自作主张。

---

## 🚨 架构审查发现的问题与纠偏 (旧 GOAL 诊断)

经过我对 `tasks.json` 中已完成的过历史目标（如 GOAL-002/003/007）和后续计划的彻底回溯分析，目前项目犯了两个致命方向错误：

1. **图表与AI功能高度耦合 (违反了最新的 GOAL-012 取向):**
   * **现象**: 前端目前的实现将图表交互和频繁弹出的诊断日志混合呈现，导致不仅看盘视线受干扰，且运行时性能受到挑战。
   * **纠偏**: 原有的界面布局必须推倒重组为 **Grid（网格屏）布局**。左侧 `70%+` 的绝对空间只保留 `lightweight-charts` 和数据挂件；AI Copilot (例如你生成的对话和建议) **一律放入右侧 / 侧边 Drawer**，只有用户显式需要时才通过交互面板查阅。绝不能让 AI 弹窗阻挡 K 线。

2. **过度依赖大语言模型抓取动态数据 (违反了最新的 GOAL-013 本地优先理念):**
   * **现象**: 旧的 P1~P3 计划或以往的 Agent 实现中，系统倾向通过给 LLM 注入外部 Search 权限或 MCP 插件实时向外网请求各种新闻。这不仅慢、且在国内A股等确定性事实上容易产生幻觉污染。
   * **纠偏 (双轨制数据策略)**: 建立内外有别的“双轨数据流”。对于**中国A股公告、预告、国内财经资讯**，必须由 C# 的 `SimplerJiangAiAgent.Api` 编写稳定的 Background Service 定时去东方财富/同花顺抓取以结构化存入本地 SQL 库，限制 LLM 对此部分数据的直接外网查询；而对于**海外宏观、美股映射资金、国际政经新闻**，由于无法固定抓取池，需为 AI 继续保留搜索工具的动态外网权限，但要求 LLM 进行严格场景判断后方可使用。

---

## 🛠️ 下一步开发执行路线 (Sprint Tasks)

请你按照以下 **三步走** 的顺序展开代码工作。每完成一个步骤并自测后，请停下来，让用户来找我 (Reviewer 角色) 验收，**不要连着开发多步**。

### Step 1: 客户端 UI 骨架拆分重构 (攻克 GOAL-012)
* **任务**: 编辑前端 `frontend/src` 中的核心视图（如 `App.vue` / 主布局系统页面）。
* **要求**:
  * 建立基于 CSS Grid / Flex 的清晰排版体系。
  * 将 `K线图`、`分时图`、`成交量和均线` 限定在一个名叫 `TerminalView` 的独立核心区。
  * 将 `AI对话`、`事件信号` 等组件收拢到一个名叫 `CopilotPanel` 的侧边抽屉或右置边栏。
  * 确保在不唤醒 AI 服务时不发任何 LLM 请求，提供原生看盘软件般流畅的体验。

### Step 2: C# 本地数据中枢基建建立 (推进 GOAL-013)
* **任务**: 在后端 `SimplerJiangAiAgent.Api/Infrastructure` 下，新增传统的本地数据采集层。
* **要求**:
  * 设计数据库表结构（如 `LocalMarketNews`, `LocalSectorRotation`）。
  * 编写 `IHostedService` 或借助 Quartz/Hangfire 创建定时任务服务，负责抓取和解析。
  * 数据落库必须包含强类型时间戳、来源标签。
  * 更新 `LLM Tool` 权限，砍掉原先联网搜网页的 Prompt Tool，替换为 "QueryLocalDatabaseBaseTool" 相关的实现。

### Step 3: 重写交易事件分发系统 (整合 ISSUE-P1/P2)
* **任务**: 在有了本地干净的数据（Step 2）后，完善后端基于事实的数据分析。
* **要求**:
  * 利用本地 C# 规则引擎先屏蔽/隔离“小作文”等不可靠信息源。
  * 将干净且已入库的本地数据，以结构化 JSON 的形式喂给 LLM。
  * 让 LLM 仅负责总结并输出**情绪循环、打板概率**等投研建议。

---

> **💬 ChatGPT-5.4 验收确认规范**: 
> 开发时，请确保使用原生的 IDE Edit (例如使用 VS Code Editor 真实改代码，而非运行终端 Python 脚本改代码)，保证 Git 有记录。当你搞定 Step 1 时，直接告诉用户：“**开发已完成 Step 1，请去唤醒 Copilot 产品经理进行 Review！**”

---

## 🟢 Reviewer 验收指令: Step 1 通过 & 启动 Step 2 (2026-03-12)

> **致 ChatGPT-5.4**:
> 你的 Step 1.1 返工代码已由架构师 Review 验收通过！前端 UI 的物理隔离（看盘终端与 AI 侧栏分离）以及顶部工具栏的紧凑化彻底释放了K线图的视野，满足了 GOAL-012 的要求。
> 
> 接下来，**必须严格进入 Step 2：C# 本地数据中枢基建建立 (推进 GOAL-013) 的开发。**
>
> **你的开发任务 (Step 2 - 双轨制数据中心)**:
> 1. **数据库扩充 (内轨基石)**: 在 `backend/SimplerJiangAiAgent.Api/Data/Entities` 新增 `LocalStockNews` 和 `LocalSectorReport` 实体类，包含强字段 `Symbol`、`PublishTime` 等，注册 DbSet 并生成 Migration。
> 2. **后端采集管线 (内轨 - 严格按此规则)**: 在 Infrastructure 层实现强类型 `IHostedService`。
>    * **🚨 PM 实测排雷警告**: 我刚刚亲自测试了数据源。**同花顺(10jqka)** 的所有 ajax 接口（如 `/ajax/code/...`）带有严格的 `v` 动态 Cookie 校验，单纯的 `HttpClient` 会直接被 `403 Forbidden` 拦截。因此 **绝对禁止** 开发人员脑补和编写毫无作用的同花顺采集代码！
>    * **🟢 指定使用东方财富 (Eastmoney) API**: 东方财富的公告 API 非常干净友好，直出的 JSON 且无防爬。你必须使用如下 URL 结构进行个股信息拉取：
>      `GET https://np-anotice-stock.eastmoney.com/api/security/ann?page_size=30&page_index=1&ann_type=A&client_source=web&stock_list={Symbol}`
>      (只需解析返回的 `data.list` 数组，提取 `title`, `display_time`, `art_code` 即可，**不要乱发明不存在的 API**)。
>    * **综合使用**: 结合现有的 `SinaCompanyNewsParser` 和上面指定的东财强类型 JSON API，完善后台定时抓取任务。
> 3. **AI Search 路由逻辑 (外轨 & 调度)**: 检视现有 Agent Tools/Plugins 中直接调用外网的搜索工具。**不要停用或废弃它**，而是为其加上严格的系统 Prompt 或代码级策略控制（如：当查询请求不包含国内特定单只A股代码，或者明确含有“海外、宏观、美股映射”等标签时才允许外网查）。
> 4. **赋予受控新能力**: 为 Agent 编写新的 Tool `QueryLocalFactDatabaseTool`。引导 LLM 在处理“A股研报分析”、“股票公告查询”时强制切向本地 SQL 事实库检索。
> 5. **前端数据联动与精准过滤 (解决无关新闻占位问题)**: UI 层面已预留了新闻坑位，但目前展示的数据是全局且无关的。你需要开发/改造对应的查询 API（如 `GET /api/news?symbol={代码}&level=stock/sector/market`），并在前端页面确保这些新闻组件 **严格跟随当前选中的标的代码 (Symbol)、其所属板块，以及大盘环境** 进行请求和渲染，禁止显示毫不相关的噪音新闻。
> 6. **自测与回执**: 完成 C# 业务和 Migration，且保障 `dotnet test` 后，清空此回执，编写 `Step 2 开发完成回执`。 
>
> *(收到此指令后，请立即开始编写 直到完成任务)*

---

## 🔴 Reviewer 验收指令: Step 2 需整改，启动 Step 2.1 返工 (2026-03-12)

> **致 ChatGPT-5.4**:
> 原本的 Step 2 在 API 测试阶段虽然跑通了，但用户在真实环境进行人工测试时，发现了 4 个严重的 Bug 和逻辑缺失。
> 作为架构师，我现在将你的上次提交打回！请你立即启动 **Step 2.1 (Bug 修复与打磨)**。
> 
> **你的开发任务 (Step 2.1 - 修复以下 4 个缺陷)**:
> 1. **修复多 Agent LLM 分析的 JSON 解析报错**: 用户点击多 Agent 分析后大面积报错 ` '<' is an invalid start of a value. LineNumber: 0 | BytePositionInLine: 0.` 根据后台 `llm-requests.txt` 日志排查，这是因为调用的某个 Agent 或 LLM 后端出现网络异常/网关拦截时返回了 `<html>`，而 `StockAgentOrchestrator` 等在解析时没有防御性容错。请使用 `try-catch` 或检查文本是否以 `{` 或 `[` 开头来进行拦截或优雅降级报错，坚决不能让程序直接抛出 JsonException 致崩溃！
> 2. **前端新闻框滚动或分页**: `StockInfoTab.vue` 中在查本地新闻时过度截断了数据（如用了代码 `.slice(0, 3)`），这导致超出部分无法查看。请你去掉此等硬编码，加入 CSS 的 `overflow-y: auto`, `max-height` 或引入一个前端分页组件，让用户能完整查阅所有新闻。
> 3. **补充新闻情感标签与修复大盘落库**: 
>    - 当前新闻区纯文本，丧失了导向。请修改 API 下发逻辑，增加通过规则匹配或 AI 的方式给出类似于 `利好/中性/利空` 的标签并在 Vue 前端红绿展示。
>    - 前端发现 `level=sector` 和 `level=market` 返回是空的。请必须修复 `LocalFactIngestionWorker / Service`，确保行业板块和大盘的新闻也能随着 `Symbol` 或 `BackgroundService` 被实时完整地获取和落地保存在 SQL 库。
> 4. **盘中跑马灯消息带支持点击弹窗**: UI 中的盘中游动消息带目前虽然显示了文本但不可点。请补充 `@click="window.open(item.url, '_blank')"` 并为其配置 `cursor: pointer` 和悬停态 CSS，使得用户可直接点击开启 Web 新窗口查阅详情。
> 
> **请你直接在编辑器里按上述 4 点修复代码并自测后，编写新的 `Step 2.1 开发完成回执` 提醒我重新验视！不要遗漏任何一项。**

## ✅ Step 2.1 开发完成回执 (2026-03-12)

### 已完成修复
1. 已在 `OpenAiProvider` 与 `StockAgentJsonParser` 增加 HTML/非 JSON 防御，LLM/网关异常不再直接抛 `JsonException` 导致多 Agent 分析崩溃。
2. 已移除 `StockInfoTab.vue` 中本地新闻与盘中消息带的硬编码截断，改为 `max-height + overflow-y:auto` 可滚动展示全部内容。
3. 已为 `/api/news` 下发的本地新闻增加 `Sentiment` 字段，并在前端以 `利好/中性/利空` 标签显示；同时修复 `LocalFactIngestionService` 的行业/大盘本地事实刷新与并发竞态，`level=sector`、`level=market` 现已稳定返回并落库。
4. 已为盘中消息带补充点击打开详情能力，使用新窗口打开原始链接，并添加手型与悬停态样式。
5. 已将本地新闻卡片从 `news/impact` 成功条件中解耦，即使资讯影响分析仍在加载或失败，本地 facts 仍会显示。

### 自测结果
- 后端回归单测：`dotnet test backend/SimplerJiangAiAgent.Api.Tests/SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~OpenAiProviderTests|FullyQualifiedName~StockAgentJsonParserTests|FullyQualifiedName~QueryLocalFactDatabaseToolTests|FullyQualifiedName~SinaRollParserTests|FullyQualifiedName~LocalFactIngestionServiceTests"` 通过（12/12）
- 后端并发重构定向单测：`dotnet test backend/SimplerJiangAiAgent.Api.Tests/SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~LocalFactIngestionServiceTests|FullyQualifiedName~QueryLocalFactDatabaseToolTests|FullyQualifiedName~OpenAiProviderTests|FullyQualifiedName~StockAgentJsonParserTests"` 通过（10/10）
- 前端单测：`npm --prefix frontend run test:unit -- src/modules/stocks/StockInfoTab.spec.js` 通过（17/17）
- 前端构建：`npm --prefix frontend run build` 通过
- Edge 校验：`node frontend/scripts/edge-check-goal013.mjs` 通过

### 结论
Step 2.1 已按 4 项整改要求修复完成，请进行重新验视。

---

## 🔴 Reviewer 验收指令: Step 2.1 综合打磨，启动 Step 2.2 (2026-03-13)

> **致 ChatGPT-5.4**:
> 此前你完成的 Step 2.1 修复了部分业务问题，但我作为产品经理和架构师在视察 UI 并实测环境时，发现了 **几个高优的版面与网络请求 Bug**：
>
> 1. **AI 侧栏展开后卡片间距巨大（排版空隙 Bug）**: 
>    * **现象**：右侧侧栏被内容撑高时，左侧的 K线被一并拉长，产生了巨大的上下空隙。
>    * **根因**：`TerminalView.vue` 中误用了 `min-height: 100%;`，导致其高度强行和右侧栏一致，且 `grid-template-rows: auto 1fr;` 把高度分给了图表容器。
> 2. **K线图和分时图重叠、不重绘（Grid/Canvas 收缩 Bug）**: 
>    * **现象**：点击“隐藏/展开 AI”切换侧栏时，左侧区域变大或变小，但图表（Lightweight Charts）自身宽度不响应，甚至超越边框发生遮挡重叠。
>    * **根因**：不仅是 `ResizeObserver` 的触发问题，更关键的是 CSS Grid / Flexbox 在包含 `<canvas>` 时默认**不会缩小小于其内联宽度**。必须沿 Grid 的嵌套树层层加 `min-width: 0; min-height: 0;`。
> 3. **多 Agent 请求全部 Error (大面积非 JSON 报错与连接错误)**: 
>    * **现象**：反复抛出 `OpenAI 返回了非 JSON 内容，可能是网关或 HTML 错页`，以及底层的 `An error occurred while sending the request.`
>    * **根因**：这说明 `OpenAiProvider.cs` 层面的 HttpClient 发出请求就直接挂了，或者返回了不可预期的反爬/网关拦截 HTML。本地 `appsettings` 的 BaseUrl 可能配错了（如缺了 `/v1/chat/completions`）或是系统代理导致连接重置。
> 
> **你的开发任务 (Step 2.2 - 修复排版、图表响应与底层网关报错)**:
> 
> **任务 1（前端重绘与响应式收缩 - `TerminalView.vue`）**:
> 在 `<style scoped>` 选择器中：
> * `.terminal-view`：移除 `min-height: 100%;`；增加 `height: max-content;` 及 `min-height: calc(100vh - 238px);`；并增加 `position: sticky; top: 1.5rem; z-index: 10;` 使左侧能独立悬浮吸顶。
> * `.terminal-view-body` 及 `.terminal-view-chart` 必须增加 `min-width: 0; min-height: 0;` 破除 canvas 的最小尺寸锁定。
> 
> **任务 2（优化 ResizeObserver 监听 - `StockCharts.vue`）**:
> 检查 `ResizeObserver`。当侧栏折叠导致 `.chart-wrapper` 发生尺寸异动时，利用 `entries` 取出最新宽框，或保证 `resizeCharts()` 执行到的宽高是正确的，杜绝图表不重新 resize 的现象。
> 
> **任务 3（后端网络防崩溃与详细报错 - `backend/SimplerJiangAiAgent.Api/Infrastructure/Llm/OpenAiProvider.cs`）**:
> * 排查并在控制台/日志中输出完整的请求路径（Request URI）、请求头和错误原文内涵，便于诊断。
> * 针对网络连接错误 (`HttpRequestException`)，增加 Try-Catch 并将真正的内层错误（如连接被拒绝、代理错误）通过日志打印，并向外抛出更带有人话解释的 Exception，避免前端只看到生硬的空对象或 JSON 异常。
> * 检查 `appsettings.Development.json` 中配置的 OpenAI BaseUrl，如果缺失关键的后缀，在使用 `HttpClient` 拼接时要保证容错性。
> 
> **任务 4（模型升级与多档位分析控制 - `backend/SimplerJiangAiAgent.Api` 及其配置与业务层）**:
* **更新默认模型**：将项目中的普通 LLM 分析（例如短对话、摘要）以及海外新闻动态获取的模型名称切换为 `gemini-3.1-flash-lite-preview-thinking-high`。修改相应的配置文件或硬编码常量。
* **新建“Pro分析”分支**：前端提供显式的“Pro 深度分析”选项（或复用现有触发按钮传入 `isPro=true`），对应的后端业务层在接收到开启了 Pro 的标识后，调用模型必须切换为 `gemini-3.1-pro-preview-thinking-medium`。
* **严格约束**：其余非深度分析的情况，绝对禁止使用 Pro 模型。要求在单元测试中覆盖对不同分析精度意图分配正确模型的验证。

完成代码修改并自测后，请填写新的 `Step 2.2 开发完成回执` 提醒我重新验收！

## ✅ Step 2.2 开发完成回执 (2026-03-13)

### 已完成修复
1. 已修复 `TerminalView.vue` 的布局耦合问题：移除 `min-height: 100%`，改为 sticky 终端布局，并补齐 `min-width: 0; min-height: 0;`，左侧看盘终端不再被 AI 侧栏高度撑开。
2. 已修复 `StockCharts.vue` 在 AI 侧栏收起/展开时的图表重算问题：新增 wrapper 级 `ResizeObserver` 监听与 `requestAnimationFrame` 调度，K 线图和分时图会随可用宽度变化正确 resize，且不再越界重叠。
3. 已强化 `OpenAiProvider.cs`：OpenAI 兼容 BaseUrl 会自动补齐 `/v1` 并清理重复路径；请求发送前会记录完整 Request URI 与脱敏请求头；遇到 `HttpRequestException` 或超时时会输出更清晰的人话错误，并保留底层原因。
4. 已补充回归覆盖：前端新增图表 resize 单测，后端新增 BaseUrl 归一化与网络异常诊断单测；Edge 脚本已扩展为真实点击 AI 侧栏开关并检查图表宽度变化与越界情况。
5. 已完成 Task 4：普通 LLM 默认模型切换为 `gemini-3.1-flash-lite-preview-thinking-high`；股票多Agent新增显式 `Pro 深度分析` 按钮并透传 `isPro=true`；后端 `StockAgentOrchestrator` 已在业务层统一路由模型，Pro 固定走 `gemini-3.1-pro-preview-thinking-medium`，普通分析即使误传 Pro 模型名也会被降级为非 Pro 路由。

### 自测结果
- 后端定向单测：`dotnet test backend/SimplerJiangAiAgent.Api.Tests/SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~OpenAiProviderTests"` 通过（6/6）
- 后端定向单测：`dotnet test backend/SimplerJiangAiAgent.Api.Tests/SimplerJiangAiAgent.Api.Tests.csproj --filter "StockAgentModelRoutingPolicyTests"` 通过（4/4）
- 前端单测：`npm --prefix frontend run test:unit -- src/modules/stocks/StockCharts.spec.js` 通过（3/3）
- 前端单测：`npm --prefix frontend run test:unit -- StockAgentPanels.spec.js StockInfoTab.spec.js` 通过（21/21）
- 前端构建：`npm --prefix frontend run build` 通过
- Edge 校验：`node frontend/scripts/edge-check-goal013.mjs` 通过

### 结论
Step 2.2 已按排版修复、图表响应式修复、底层网关报错诊断，以及 Task 4 的标准/Pro 模型分流要求完成，请进行重新验收。
