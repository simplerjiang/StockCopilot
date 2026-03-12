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
   * **现象**: 旧的 P1~P3 计划或以往的 Agent 实现中，系统倾向通过给 LLM 注入外部 Search 权限或 MCP 插件实时向外网请求新闻和研报。这不仅慢、且产生严重的幻觉、时效污染。
   * **纠偏**: 全面褫夺 LLM 在运行时的公网直接抓取权限！以后的行情、新闻、研报获取，**全部改由 C# 的 `SimplerJiangAiAgent.Api` 编写稳定的 Background Service 爬虫定时去东方财富/同花顺获取，并在 SQL Server / Sqlite 库中落地**。LLM `StockAgent` 等角色，仅限查询 `AppDbContext` 里的表、输出分析，再反馈给客户端。

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
> **你的开发任务 (Step 2)**:
> 1. **数据库扩充**: 在 `backend/SimplerJiangAiAgent.Api/Data/Entities` 新增 `LocalStockNews` 和 `LocalSectorReport` 实体类，并在 `AppDbContext` 中注册 DbSet。完成后请使用 EF Core 生成 Migration 进行数据库结构的演进。
>    - 核心强字段要求包含：`Id`, `Symbol` (标的代码), `Title`, `Content` (降噪后的文本), `Source` (来源机构), `PublishTime` (新闻实际发布时间, 关键!), `CreatedAt` (入库时间)。
> 2. **后端采集底层**: 在 Infrastructure 层实现强类型的 `IHostedService` (`BackgroundService`)，比如 `MarketDataScraperWorker`：周期性通过原生 HttpClient/爬虫拉取新闻和研报，并`SaveChanges`存入本地。彻底取代由 LLM 请求直接向外网抓取的逻辑。
> 3. **清理旧 Agent 权限**: 检查现有的 Agent Tools/Plugins，如果存在直接调用外网搜索引擎或临时爬取特定网页内容的 Tool（会导致严重的幻觉且极不稳定），将其移出 Tool 清单或直接废弃！
> 4. **赋予受控新能力**: 为 Agent 编写一个且唯一的新 Tool，如 `QueryLocalFactDatabaseTool`。让 LLM 只能以受限查表/全文索引的方式检索本地 `LocalStockNews` 里爬虫已筛洗好的数据，进行投研推演。
> 5. **自测与回执**: 完成后端 C# 业务和 Migration 并确保 `dotnet build` 且 `dotnet test` 通过后，**清空下方旧的回执，编写新的 `Step 2 开发完成回执`** 提醒用户进行 Review。
>
> *(收到此指令后，请立即开始编写 C# 后端代码并排查 Agent 工具侧代码！切勿修改前端页面。)*
