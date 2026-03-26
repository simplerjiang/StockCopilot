# GOAL-AGENT-NEW-001 TradingAgents Source Analysis (2026-03-25)

### 这份文件的作用
这是一份给后续实现阶段用的 TradingAgents 源码分析备忘。
目的就是防止后面开发时忘掉 TradingAgents 的真实结构，只剩截图印象，然后做偏。

### 本轮实际读过的关键文件
本轮重点读了这些文件：
1. `noupload/TradingAgents-main/README.md`
2. `noupload/TradingAgents-main/main.py`
3. `noupload/TradingAgents-main/cli/main.py`
4. `noupload/TradingAgents-main/cli/stats_handler.py`
5. `noupload/TradingAgents-main/tradingagents/default_config.py`
6. `noupload/TradingAgents-main/tradingagents/graph/trading_graph.py`
7. `noupload/TradingAgents-main/tradingagents/graph/setup.py`
8. `noupload/TradingAgents-main/tradingagents/graph/conditional_logic.py`
9. `noupload/TradingAgents-main/tradingagents/graph/propagation.py`
10. `noupload/TradingAgents-main/tradingagents/agents/utils/agent_states.py`
11. `noupload/TradingAgents-main/tradingagents/agents/utils/agent_utils.py`
12. `noupload/TradingAgents-main/tradingagents/agents/utils/memory.py`
13. `noupload/TradingAgents-main/tradingagents/agents/analysts/market_analyst.py`
14. `noupload/TradingAgents-main/tradingagents/agents/researchers/bull_researcher.py`
15. `noupload/TradingAgents-main/tradingagents/agents/managers/research_manager.py`
16. `noupload/TradingAgents-main/tradingagents/agents/trader/trader.py`
17. `noupload/TradingAgents-main/tradingagents/agents/risk_mgmt/aggressive_debator.py`
18. `noupload/TradingAgents-main/tradingagents/agents/managers/portfolio_manager.py`

### 源码级理解
#### 1. 它本质上是一个 staged graph
不是自由聊天 loop。

顺序就是：
1. analysts，
2. bull/bear debate，
3. research manager judgment，
4. trader proposal，
5. 三类 risk loop，
6. portfolio manager final decision。

这决定了我们后续产品设计必须把 stage 推进做成主叙事。

#### 2. analyst 的 tool scope 是被明确限定的
从 `trading_graph.py` 和 `agent_utils.py` 可以看到：
1. market 只拿股价和指标。
2. social 只拿新闻类数据。
3. news 拿新闻/全球新闻/insider。
4. fundamentals 拿报表族。

这意味着我们后续也要给角色做清晰的 tool 边界。

#### 3. debate 是真实结构，不是装饰
从 `conditional_logic.py` 能确认：
1. 多空辩论是 loop。
2. 风险三角色也是 loop。

所以新模块里 disagreement 必须能被持久化和展示，不是写个“正反观点”标题就算完。

#### 4. `Current Report` 是核心舞台
从 `cli/main.py` 的 `MessageBuffer` 和 `update_display()` 看得很清楚：
1. 它始终维护一个 `current_report`。
2. report section 和 agent completion 是绑定的。
3. 运行中用户主要看的是这块，而不是只盯消息流。

所以我们新模块必须保留中央 `Current Report`。

#### 5. 它故意做了 message clearing
从 `agent_utils.py` 的 `create_msg_delete()` 可以看到，它会在 analyst 之间清消息，避免上下文污染和无限膨胀。

我们不一定要照搬这个实现，但必须同样认真控制跨 stage 的上下文边界。

#### 6. memory 是轻量、按角色拆开的
从 `memory.py` 看：
1. 用的是 BM25。
2. 每个角色单独一个 memory。
3. 存的是 `情境 + 建议`，不是隐藏推理链。

这意味着后面如果引入 research memory，也应该保持轻量、可解释、可审计。

### 角色级别笔记
#### Market Analyst
1. 先拉 stock data。
2. 再选 indicator。
3. 再生成市场报告。

迁移提醒：
1. 在本仓库里最好把它规范成结构化 market-analysis block，而不是只存一段 markdown。

#### Bull Researcher
1. 读取 analyst reports。
2. 读取上一轮 bear 观点。
3. 读取相似场景 memory。
4. 输出一段牛方论证。

迁移提醒：
1. 牛方发言必须作为独立 debate artifact 落库。

#### Research Manager
1. 概括双方重点。
2. 强制给出结论倾向。
3. 产出 `investment_plan`。

迁移提醒：
1. 这是第一个关键 synthesis 边界，后面一定要输出结构化研究结论对象。

#### Trader
1. 吃 research plan。
2. 输出交易提案。

迁移提醒：
1. 它的输出应该直接能对接交易计划 draft 字段。

#### Aggressive Risk Analyst
1. 专门替高收益高风险立场辩护。

迁移提醒：
1. 风险三角色不能被压成一个“风险总结”段落，不然 TradingAgents 的核心结构就没了。

#### Portfolio Manager
1. 按固定 rating scale 拍板。
2. 产出 final decision。

迁移提醒：
1. 它必须成为后续动作交接的 authoritative block。

### CLI 布局里最值得保留的东西
从 `cli/main.py` 看，真正应该保留的是这些可见层次：
1. progress table。
2. messages/tools table。
3. current report panel。
4. footer stats。

不是要照着做终端样式，而是要保留“工作态可感知”的信息结构。

### 我们迁移时必须保留的
1. 明确的阶段执行顺序。
2. 明确的角色分工。
3. team/role 可见进度。
4. message/tool activity 可见。
5. 中央 `Current Report`。
6. debate 作为真实可持久化概念。
7. final decision 作为独立治理输出。

### 我们迁移时可以适配的
1. 内部 orchestrator 不必非得是 LangGraph。
2. 长文本输出可以转成结构化 DTO。
3. tool 层改成本仓库现有 MCP adapter。
4. memory 机制可以换，但要保持可审计。
5. final decision schema 可以更强，因为本仓库有图表/证据/交易计划交接。

### 如果迁移时偷懒，会出什么问题
1. 只学视觉，不学 staged runtime，会变成假多 Agent。
2. 只学长文本，不做结构化对象，会让 replay 和动作交接很脆。
3. 只学角色名，不做 role-bounded tools，会让 grounded 性崩掉。
4. 把 `Current Report` 再改回聊天主流，会失去 TradingAgents workbench 的核心识别度。