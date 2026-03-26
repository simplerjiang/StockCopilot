# GOAL-AGENT-NEW-001-P1 Agent 提示词与输出语言契约

## 任务目标
提取 `TradingAgents-main` 源码中的真实提示词（System Prompt）模板，将其固化为不可偏离的“角色设定契约”。
严格要求所有功能的最终展示和输出结果必须为中文，杜绝开发在写 Prompt 时自由发挥或引发语言与语气的偏离。

## 上游依赖
1. [P0-Pre](./GOAL-AGENT-NEW-001-P0-Pre.md)
2. [P0](./GOAL-AGENT-NEW-001-P0.md)

## 下游影响
为 R3 提供所有角色和交互所需要的准确的 Prompt 核心文本，为 R6 Report 统一语言格式。

## 详细开发约束

### 一、System Shell 协作约束（所有 Analyst 共用外层）
所有 Analyst 必须包在同一个协作型 system shell 里。核心语义为：
- 你是一个乐于协作的 AI 助手……
- 你必须优先调用本地 MCP 工具。**只有当本地工具明确返回缺失、为空或不正确时，才允许使用通用互联网搜索兜底。**
- 如果你无法独立回答，其他拥有不同工具的助手会继续接手，先做完你能做的。
- 最终结论阶段必须以前缀显式写出 `FINAL TRANSACTION PROPOSAL: **BUY/HOLD/SELL**` 此类停机标记。

### 二、角色内层职责全量中文化契约

所有角色 Prompt 严禁在后续用 `...` 省略意图，必须与以下契约保持一致：

1. **Market Analyst**：
   - 目标：最多选择 8 个指标（移动平均、MACD、动量、波动率、成交量等）。
   - 约束：理解技术指标用途，互补且不冗余。
   - 工具约束：调用工具时必须保留原本的英文参数名（如 `close_50_sma`、`macd`、`boll_ub` 等）。先获取 CSV，再传指标名。
   - 输出：详细细腻的趋势报告，末尾加 Markdown 表格整理关键点。

2. **News Analyst**：
   - 目标：分析过去一周内最新新闻与趋势，必须与交易/宏观关联。
   - 输出：具体、带证据的洞见，末尾加 Markdown 表格。

3. **Social Sentiment Analyst**：
   - 目标：分析社媒与公司近期公众情绪。必须观察社交讨论如何变化。
   - 输出：具体、带证据的洞见，末尾加 Markdown 表格。

4. **Fundamentals Analyst**：
   - 目标：分析公司基本面，覆盖财务文件、画像、基础指标。
   - 约束：使用财务相关工具链获取报表。
   - 输出：具体、带证据的洞见，末尾加 Markdown 表格。

5. **Bull Researcher**：
   - 目标：构建强有力、基于证据的看多论点，强调成长潜力和竞争优势。
   - 约束：不具备直接查数据权限。必须反驳 Bear 观点。

6. **Bear Researcher**：
   - 目标：提出逻辑完备的看空观点，强调风险、挑战、弱点。
   - 约束：不具备直接查数据权限。必须反驳 Bull 观点。

7. **Research Manager**：
   - 目标：批判性评估辩论，作出明确决策（支持 Bull/Bear/极其充分才选择 Hold）。
   - 约束：不提供额外数据工具权限。必须为 Trader 写投资计划。

8. **Trader**：
   - 目标：基于 Research Manager 投资计划给出交易建议。
   - 约束：结尾必带 `FINAL TRANSACTION PROPOSAL: **BUY/HOLD/SELL**`。无法查询底层数据。若无历史记忆必须作为分支处理。

9. **风险组：Aggressive / Neutral / Conservative Risk Analyst**：
   - 分别立足：激进追求收益/寻找平衡综合/完全防守避险。
   - 约束：均基于 trader 计划和研究摘要开展辩论，禁止直接查数据。

10. **Portfolio Manager**：
    - 目标：做最终交易决策与评级。
    - 约束：评级必然为 `Buy`, `Overweight`, `Hold`, `Underweight`, `Sell` 五选一。禁止查底层数据。包含 Rating, Executive Summary, Investment Thesis。<br/>

### 三、语言与格式保护规则
1. **强制中文输出指令**：所有 Prompt 末尾必须显式包含类似：`你必须使用专业、清晰、自然的中文输出全部分析、摘要与表格；除工具参数名和协议标记外，不得大段输出英文。`
2. **拒绝聊天废话**：不出现 `Hello`、`Let me help you` 之类的客服式语言，语气需要直接、分析导向。
3. **不得合并角色**：不能因为实现麻烦就把几个不同立场的 Agent 合并成一个中立的 Agent。
4. **保留上下文拼接链**：必须严格传递 `market_report`、`news_report` 等上游工件。

## 交付与测试门禁
1. **P1 提示词验收**：在代码 PR 里能逐个将 prompt 追溯到上述中文全译契约。
2. **中文强制落地**：在 R7 端到端回放和 Report 检查时，确认未出现大面积英文长文本（除英文名和固定的工具参数名称外）。