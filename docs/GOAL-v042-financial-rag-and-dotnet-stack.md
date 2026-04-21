# v0.4.2 计划书：财报 RAG Lite 与 .NET 技术栈深度分析

> 版本：v0.4.2  
> 目标类型：财报文本可检索化 / .NET AI 技术路线定型  
> 说明：本文件重点回答“RAG 是什么、LangChain 是什么、.NET 有什么替代方案、各自优缺点是什么”。

---

## 1. 先用大白话讲清楚：RAG 到底是什么

RAG（Retrieval-Augmented Generation，检索增强生成）可以理解成：

> **先去你自己的资料库里找相关内容，再把找到的内容连同问题一起发给 LLM 回答。**

它不是让模型“凭记忆乱答”，而是让模型：

1. 先检索
2. 再阅读检索结果
3. 再输出答案
4. 最好附带证据来源

### 对财报场景来说，RAG 适合干什么？

适合：

- 找财报里的“管理层讨论与分析”
- 找“风险提示”
- 找“募资用途”
- 找“会计政策变化”
- 找“这一段原文到底怎么写的”

不适合直接让 RAG 负责：

- 净利润同比计算
- 资产负债率精确数值判断
- 三表结构化字段的严格比较

这些仍然应由结构化数据层负责。

### 一句话总结

> **RAG 更像“帮你找文档和证据”，不是替代结构化财务计算。**

---

## 2. LangChain 到底是什么，它和 RAG 不是一回事

LangChain 不是 RAG 本身，而是一个 **LLM 应用编排框架**。

它解决的是：

- 怎么接不同模型
- 怎么做 prompt chain
- 怎么接 tool calling
- 怎么做 retrieval
- 怎么把检索、工具、代理流程串起来

### LangChain ≠ RAG

关系更准确地说是：

- **RAG**：一种能力模式
- **LangChain**：一种实现这类能力的框架

就像：

- “做网页”是一件事
- React/Vue 是做网页的框架

所以不能把“要做 RAG”自动理解为“必须上 LangChain”。

---

## 3. 这个仓库为什么不能直接默认走 Python + LangChain

当前仓库是：

- 后端：.NET
- 前端：Vue
- 桌面：Desktop EXE
- 已有 AI 能力：自研服务编排 + MCP + 多 Agent 业务链路

如果默认引入 Python + LangChain，会带来这些问题：

1. 增加额外运行时
   - 用户机器要多一套 Python 环境或 sidecar

2. 增加打包复杂度
   - Windows 安装包、升级、依赖、环境变量、日志、异常定位都会更复杂

3. 增加跨进程排障难度
   - 现在本来就有 API + Worker，若再加 Python sidecar，排障链会更长

4. 不能充分利用现有 .NET 业务服务
   - 当前数据、模块、API 都在 .NET 主链路里，强行跨到 Python 会把很多事做成“桥接”而不是“原生扩展”

所以：

> **对于这个仓库，LangChain 最多适合作为研究样机，不适合 v0.4.2 作为默认正式运行时。**

---

## 4. .NET 是否有类似 LangChain 的组件？答案是：有，但不是一比一复制

### 4.1 能力上可替代，不一定要名字上等价

在 .NET 世界里，没有一个“唯一、绝对对应 LangChain”的官方单体框架，但已经有几条成熟路线可以组合出同类能力：

1. **Microsoft Semantic Kernel**
2. **Microsoft Kernel Memory**
3. **Microsoft.Extensions.AI + 自研 RAG / 工具编排**
4. **Semantic Kernel + Kernel Memory 组合**
5. **Python LangChain sidecar（跨语言方案）**
6. **社区型 .NET 库（如 LangChain.NET / GraphRag.Net 等）**

其中，真正适合这个仓库长期走的，主要是前 4 条。

---

## 5. 方案一：纯 .NET 自研 Lite RAG（推荐作为 v0.4.2 正式主线）

### 核心思路

延续当前仓库风格，不强引新的“重框架”，而是直接在现有 .NET 服务里补齐：

1. 文本切块
2. 本地索引
3. 检索服务
4. Prompt 组装
5. 引用回传

### 适合本仓库的原因

当前仓库已经有：

- 独立财报 Worker
- 主 API
- 现成本地数据目录
- 现成 AI 服务和 MCP 路由
- 现成日志审计链路

因此完全可以走：

- `FinancialWorker` 做抽取和切块
- 主 API 做检索
- 现有 AI 路径做回答与展示

### 推荐技术形态

- 存储：SQLite / LiteDB + 文本块表
- 检索：FTS / BM25 / metadata filter
- 模型调用：沿用当前仓库 LLM 通路
- 证据返回：自己定义 citation DTO

### 优点

1. 与当前工程最匹配
2. 打包最轻
3. 可控性最高
4. 方便逐步演进
5. 便于与现有 API、DTO、日志体系整合

### 缺点

1. 前期要自己补一些基础能力
2. 没有“现成框架帮你全包”
3. 若以后要做复杂 Agent graph，要自己继续往上搭

### 适用结论

> **最适合作为 v0.4.2 的正式生产主线。**

---

## 6. 方案二：Microsoft Semantic Kernel（推荐作为 .NET 官方编排候选）

### 它是什么

Semantic Kernel（SK）是微软主推的 AI 编排 SDK，核心偏向：

- Prompt orchestration
- Tool / plugin 调用
- Memory / retrieval 扩展
- Agent / function 调用
- .NET / Python / Java 多语言支持

从生态与企业适配角度看，它是 .NET 世界里最接近“官方 LangChain 替代思路”的方案之一。

### 它适合干什么

对于本仓库，SK 更适合用于：

1. 管理 AI 插件 / tool 调用
2. 封装 RAG 查询为 plugin
3. 把财报检索结果注入 AI 工作流
4. 为未来 Recommend / Research / Workbench 提供更统一的 tool abstraction

### 它不最适合干什么

如果只是做最小财报 Lite RAG，直接上 SK 可能有点“框架先行”。

### 优点

1. 微软生态，.NET 集成友好
2. 比 Python LangChain 更贴合当前仓库
3. 更利于未来工具化与 Agent 化扩展
4. 可以和现有 .NET 服务直接整合
5. 便于后续接入 Azure / Ollama / 本地模型 / 插件体系

### 缺点

1. 比纯自研 Lite RAG 更重
2. 若只做最小检索，会显得有些超配
3. 要理解插件、planner、agent 抽象，学习成本高于纯 CRUD + retrieval

### 适用结论

> **适合作为 v0.4.2 的“上层编排增强候选”，但不应强制成为第一步。**

更合理的方式是：

- 先做纯 .NET Lite RAG
- 再评估是否用 SK 接管上层编排与工具调用

---

## 7. 方案三：Microsoft Kernel Memory（推荐作为文档 RAG 服务候选）

### 它是什么

Kernel Memory（KM）可以理解为：

> **微软路线下更偏“文档 ingestion + chunking + indexing + citation + query”的 Memory / RAG 服务层。**

相比 Semantic Kernel，Kernel Memory更偏“文档入库和检索管线”，而不是通用 Agent 编排。

### 它特别适合什么

对于财报场景，它天然适合：

- 上传 PDF
- 文本抽取
- chunking
- memory indexing
- 带 citation 的问答

也就是说，如果把财报 PDF 当成“文档资产”，Kernel Memory 的心智模型是很顺的。

### 优点

1. 对文档 RAG 场景更直接
2. 比起自己从零补 chunking / memory service，更省时间
3. 对 citation、文档索引、检索链路更友好
4. 比 LangChain sidecar 更符合 .NET 主栈

### 缺点

1. 对当前仓库来说，它仍然属于“额外引入一个较完整的 memory 层”
2. 若当前只是 Lite RAG，可能还是偏重
3. 若以 service 模式跑，部署复杂度会高于纯本地读库检索
4. 当前仓库需要仔细评估它与 `FinancialWorker` 的边界，避免重复建设

### 适用结论

> **适合作为 v0.4.2 的深度备选，或 v0.4.3 之后的增强路线。**

若后续确认财报 PDF 文档量会持续增长、需要更标准化文档 memory 体系，那么 Kernel Memory 很值得认真评估。

---

## 8. 方案四：Microsoft.Extensions.AI + 自研 RAG（很适合本仓库的中间路线）

### 它是什么

`Microsoft.Extensions.AI` 更像一层 **AI 能力抽象与 building blocks**，不是完整的“LangChain 克隆框架”。

它适合做：

- `IChatClient`
- `IEmbeddingGenerator`
- 基础模型能力抽象
- 与自研 retrieval / orchestration 组合

### 为什么它很适合这个仓库

这个仓库已经有很强的业务编排和 API 结构了，缺的不是“所有能力都没有”，而是：

- 统一 AI 抽象
- 统一模型接口
- 统一 retrieval / generation 接口边界

因此很适合走：

- 业务流程继续自研
- 检索继续自研
- AI 能力抽象逐步收敛到 `Microsoft.Extensions.AI`

### 优点

1. 很轻
2. 很贴近 .NET 原生生态
3. 不会强行改变当前架构
4. 便于后续接 Semantic Kernel 或其他组件

### 缺点

1. 它不是“开箱即用全家桶”
2. 文档 RAG 的很多上层能力还得自己搭
3. 如果团队希望一个框架帮你把 agent / retrieval / tool 全包，它不够完整

### 适用结论

> **非常适合作为本仓库 v0.4.2 的底层 AI 抽象层。**

如果让我给这个仓库选“最稳妥”的演进方式：

- retrieval 自研
- 上层可选 SK
- 底层 AI 能力抽象尽量向 `Microsoft.Extensions.AI` 靠拢

这是比较均衡的路线。

---

## 9. 方案五：Semantic Kernel + Kernel Memory 组合（中长期强方案）

### 核心思路

- Kernel Memory 负责文档 ingestion / chunk / retrieve / citation
- Semantic Kernel 负责工具调用 / 编排 / agent workflow

这是 .NET 世界里比较像“LangChain + LangGraph + 文档记忆层”的组合打法。

### 优点

1. 文档能力和编排能力分工明确
2. 长期演进空间大
3. 更容易做正式 AI 工作台与企业级治理

### 缺点

1. 对当前版本来说太重
2. 会明显扩大 v0.4.2 scope
3. 引入成本、学习成本、调试成本都更高

### 适用结论

> **不建议作为 v0.4.2 首版主线，但非常适合作为 v0.4.3+ 的增强方案。**

---

## 10. 方案六：Python LangChain sidecar（可研究，不建议默认正式化）

### 做法

在 .NET 主应用外，再跑一个 Python 服务，负责：

- PDF ingestion
- chunking
- embeddings
- retrieval
- agent orchestration

主应用通过 HTTP/MCP 调它。

### 优点

1. 社区生态丰富
2. 教程多、示例多
3. 在实验阶段能快速搭起来

### 缺点

1. 脱离当前主技术栈
2. Windows 打包和部署复杂度上升
3. 运行时依赖更多
4. 排障跨语言、跨进程
5. 长期容易形成“双后端”问题

### 适用结论

> **适合实验，不适合 v0.4.2 默认正式上线方案。**

---

## 11. 方案七：社区 .NET 库（LangChain.NET / GraphRag.Net 等）

### 看法

社区库可以研究，但不建议把路线建立在这些库之上。

### 原因

1. 成熟度不如微软主线组件稳定
2. 维护连续性和版本兼容要额外评估
3. 对这个仓库来说，财报线是正式业务能力，不适合把主命脉压在较弱生态上

### 适用结论

> **可做参考，不建议作为正式主路线。**

---

## 12. 多方案对比总表

| 方案 | 技术栈匹配度 | 部署复杂度 | 文档RAG能力 | Agent编排能力 | 学习成本 | 推荐度 |
|---|---:|---:|---:|---:|---:|---:|
| 纯 .NET 自研 Lite RAG | 5 | 1 | 3 | 2 | 2 | 5 |
| Semantic Kernel | 5 | 2 | 3 | 5 | 4 | 4 |
| Kernel Memory | 4 | 3 | 5 | 2 | 3 | 4 |
| Microsoft.Extensions.AI + 自研 | 5 | 1 | 2 | 2 | 2 | 5 |
| SK + KM 组合 | 4 | 4 | 5 | 5 | 5 | 3 |
| Python LangChain sidecar | 2 | 5 | 5 | 5 | 3 | 2 |
| 社区 .NET 库 | 3 | 2 | 3 | 3 | 3 | 2 |

---

## 13. 对本仓库的最终推荐路线

### 推荐路线 A（最推荐）

**v0.4.2 正式主线：**

- `Microsoft.Extensions.AI` 作为底层抽象方向
- 自研 Lite RAG（FTS/BM25 + metadata filter）
- 不强依赖 Semantic Kernel
- 不引入 Python sidecar

适合原因：

- 与当前仓库最兼容
- 打包最轻
- 可以快速落地
- 可控性高

### 推荐路线 B（增强型）

**v0.4.2 ~ v0.4.3 渐进增强：**

- v0.4.2：先自研 Lite RAG
- v0.4.3：再引入 Semantic Kernel 接管上层编排
- 若文档规模与 citation 要求明显升高，再评估 Kernel Memory

这是我认为最稳的“先做对、再做强”的路径。

---

## 14. v0.4.2 建议交付内容

1. 财报 PDF 叙述文本切块模型
2. 本地文本块存储
3. Lite RAG 查询接口
4. 最小引用返回格式
5. `.NET 路线决策结论` 固化到文档

### 不建议在 v0.4.2 一次做完的内容

1. 独立向量数据库
2. 多 Agent 图编排
3. Python sidecar 正式接入
4. 复杂 GraphRAG

---

## 15. 最终结论

### 关于 RAG

RAG 不是替代财务结构化数据，而是补足财报正文检索与证据引用。

### 关于 LangChain

LangChain 是框架，不是 RAG 本身；对于这个 .NET 桌面仓库，不应默认把它当正式主线。

### 关于 .NET 方案

.NET 完全有能力做这条线，而且路线不止一条。

### 最终推荐

> **本仓库最合适的路线是：`自研 Lite RAG + Microsoft.Extensions.AI 抽象优先`，`Semantic Kernel` 作为上层增强候选，`Kernel Memory` 作为文档 memory 强化候选，`Python LangChain` 仅作研究备选。**
