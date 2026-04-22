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

## 7.1 Lite RAG 和 Kernel Memory，最终效果会差别很大吗？

### 先说结论

**在本仓库当前财报场景下，二者最终效果未必会差很大；真正拉开差距的，通常不是“名字更高级”，而是 chunk 质量、metadata 设计、citation 设计、失败回退和与你们现有系统的整合质量。**

也就是说：

- 如果只是做 `财报正文检索 + 原文引用 + PDF 对照`
- 文档规模还不算海量
- 检索条件本身就很强（`symbol + reportDate + reportType + section`）

那么一个做得好的 **Lite RAG**，最终用户体感很可能已经接近一个做得普通的 Kernel Memory 集成。

### 哪些情况下差距不会太大

当满足以下条件时，Lite RAG 和 Kernel Memory 的最终用户效果可能比较接近：

1. **问题域足够垂直**
   - 当前只做财报正文，不是做通用企业知识库。

2. **候选文档天然可缩小范围**
   - 股票代码、报告期、报告类型本身就能把候选集压到很小。

3. **核心诉求是“找得到原文 + 给出引用”**
   - 不是做跨文档海量知识发现。

4. **现有系统已有稳定业务链路**
   - 你们已经有 Worker、主 API、PDF 解析、详情页和日志链路。

### 哪些情况下差距会慢慢拉大

当后续出现这些诉求时，Kernel Memory 的优势会逐渐体现：

1. 文档规模持续扩大
2. 文档来源持续增多（PDF、网页、公告、Word、Markdown 等）
3. 对 citation、文档版本、入库管线要求更高
4. 需要更标准化的文档 ingestion / indexing / query 生命周期
5. 需要把文档 RAG 变成跨模块复用的基础设施，而不是财报线单点功能

### 实际判断

> **当前阶段：Lite RAG 更可能在“更低成本”下拿到“足够接近”的效果。**

如果后面发现：

- 文档种类开始爆炸
- 入库管线越来越复杂
- citation 和 memory 生命周期越来越难自己维护

那再考虑 Kernel Memory，才是更稳妥的节奏。

---

## 7.2 Lite RAG 的核心优点：它真的更适合按你们系统特点定制

你这个判断是对的：

> **Lite RAG 的最大价值，不只是“更轻”，而是“更容易围绕你们现有股票系统做定制”。**

### Lite RAG 对你们的主要优势

1. **可以强绑定现有业务主键**
   - 例如：`symbol`、`reportDate`、`reportType`、`pdfFileId`、`section`

2. **可以直接复用现有链路**
   - `FinancialWorker` 做抽取 / 切块
   - 主 API 做检索
   - 前端详情页做 citation 和 PDF 对照

3. **可以做领域专用裁剪**
   - 对“管理层讨论”“风险提示”“分红方案”等章节做特殊 chunk 规则
   - 对“纯数值表格”直接跳过，不混入正文 RAG

4. **可以做非常贴近业务的 metadata filter**
   - 先按股票与报告期过滤，再做检索
   - 这会让检索空间天然更干净

5. **可以保持与现有日志 / DTO / MCP / 审计体系一致**

### Lite RAG 的代价

1. 你们自己要维护 chunk / index / query / citation 生命周期
2. 文档类型一旦增加，自己维护成本会逐步上涨
3. 以后想抽成通用 memory 层时，需要二次整理

所以 Lite RAG 不是“低配版”，而是：

> **当前阶段最贴业务、最贴现有工程结构的方案。**

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

## 8.1 现在要不要重构现有编排层？

### 结论先行

**不建议在 v0.4.2 之前整体重构现有多 Agent / 多 MCP 编排层。**

原因不是“现有实现完美”，而是：

1. **现有编排层已经非常厚**
2. **强耦合到状态机、数据库、MCP、Prompt 压缩、超时与重试策略**
3. **整体替换的工程量很大，且最容易引入隐蔽回归**

### 当前编排层为什么不适合大换血

从现有代码看：

- `ResearchRunner.cs`
  - 不是简单 orchestrator，而是完整研究状态机与 pipeline 执行器
- `ResearchRoleExecutor.cs`
  - 不只是调 LLM，而是集成了 MCP 调度、tool retry、token 压缩、timeout、prompt governance
- `McpToolGateway.cs`
  - 不只是 registry，而是大量业务 MCP 的统一入口与治理层

这些都说明：

> **当前系统不是“拿几个 agent 跑起来”，而是一个已经强业务化的研究引擎。**

### 整体重构的主要风险

1. **状态机回归**
   - turn / stage / role 状态推进容易出错

2. **Prompt / token 回归**
   - 通用框架接管后，现有 JSON 压缩、tool slimming、prompt 裁剪可能失效

3. **工具调度回归**
   - timeout、retry、并发门控行为变化

4. **历史数据兼容风险**
   - `ResearchTurn` / `ResearchStageSnapshot` / `ResearchRoleState` 等既有资产会受影响

5. **MCP 契约回归**
   - 角色权限、必选工具、最小证据数等业务规则容易被抽象层稀释

### 那是不是完全不能动？

不是。

更合理的方式是：

> **不要整体重构“主编排引擎”，而是优先重构“边界层”。**

例如优先抽离：

1. retrieval provider
2. chunking service
3. citation builder
4. prompt builder
5. tool scheduler

这样收益更高，风险更低。

### 收益判断

#### 整体换编排框架

- 短期收益：低
- 风险：高
- 是否建议现在做：不建议

#### 边界式重构

- 短期收益：中到高
- 风险：可控
- 是否建议现在做：建议

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

## 9.1 Kernel Memory 和 GraphRag.Net 有什么区别？

这两个名字都和 RAG 有关，但它们解决的问题并不在同一层级。

### Kernel Memory 更像什么

Kernel Memory 更像：

> **文档型 RAG / Memory 系统**

它更擅长：

- 文档 ingestion
- chunking
- indexing
- semantic retrieval
- citation
- 面向文档问答

对你们财报场景来说，Kernel Memory 的强项是：

- “帮我找某份财报里提到某件事的原文段落”
- “把相关段落取回来并附引用”

### GraphRag.Net 更像什么

GraphRag.Net 更像：

> **GraphRAG 思路的 .NET 社区实现 / 图谱型检索实验库**

它更偏向：

- 从文档提取实体与关系
- 构建图与社区摘要
- 做多跳推理与结构化关系检索

它擅长的问题不是简单“找段落”，而是：

- 多文档之间有哪些关系
- 多个实体之间的因果 / 关联链是什么
- 哪些主题在整个语料里形成了社区结构

### 哪个更适合当前财报线

如果聚焦你们当前 v0.4.x 财报路线：

- 重点是 PDF 原文、章节、citation、对照验证
- 重点不是复杂图关系推理

那么：

> **当前阶段更接近 Kernel Memory / Lite RAG 的问题域，而不是 GraphRAG 的问题域。**

### 二者简化对比

| 方案 | 更适合的问题 | 当前财报线适配度 | 复杂度 |
|---|---|---:|---:|
| Kernel Memory | 文档检索、citation、文档记忆 | 高 | 中高 |
| GraphRag.Net | 图谱关系推理、多跳问题 | 低到中 | 高 |

### 当前建议

1. **先不要把 GraphRag.Net 当成 v0.4.2 主路线**
2. 如果后面要做：
   - 跨公司关系
   - 股东/产业链/事件图谱
   - 多文档因果推理
   再重新评估 GraphRAG 才更合理

---

## 9.2 Kernel Memory 是不是“官方 RAG”？

更准确的说法是：

> **Kernel Memory 是微软维护的 RAG / Memory 开源方案，但它更像“完整方案 / 参考实现”，不是那种轻量、基础设施级别的通用标准库。**

所以它既不是“纯社区野路子”，也不是像 `Microsoft.Extensions.*` 那种非常薄的基础抽象。

对你们来说，要把它看成：

- 微软路线下的正式候选方案
- 但仍然需要认真评估部署方式与整合成本

---

## 9.3 Kernel Memory 如果走独立 Web Service，会不会很麻烦？

### 结论

**会，尤其对当前 Desktop EXE + 主 API + FinancialWorker 的架构来说，额外再挂一个长期运行的独立 KM service，维护复杂度会明显上升。**

### 麻烦主要来自哪里

1. **启动链更长**
   - EXE 之外，还要决定谁来拉起 KM service

2. **进程治理更复杂**
   - 端口、健康检查、日志、重启、升级都要多一层

3. **桌面交付更重**
   - 安装包、目录结构、用户环境诊断都会复杂化

4. **现有架构已经有两个主要运行单元**
   - 主 API
   - FinancialWorker
   - 再加一个长期 memory service，不划算

### 所以怎么处理更合理

如果未来要认真试 Kernel Memory，建议优先按下面顺序考虑：

1. **先不引入，优先做自研 Lite RAG**
2. **如果试 KM，优先试“嵌入式 / 局部使用 / 实验性”模式**
3. **不要一上来就把它做成 Desktop 正式常驻依赖**

### 当前结论

> **对当前仓库来说，Kernel Memory 不是不能用，而是“现在作为独立 Web Service 正式接入”性价比不高。**

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

社区库可以认真研究，也可以拿来做“子系统级试点”，但不建议一上来就拿它们整体替换当前主编排引擎。

### 原因

1. 维护连续性和版本兼容要额外评估
2. 对这个仓库来说，主编排层过厚，直接整体替换风险过高
3. 更适合先用于：
   - 财报文档 RAG 子系统
   - citation / retrieval 试点
   - GraphRAG 预研模块

### 适用结论

> **可以作为“叶子层 / 子系统”候选，但不建议直接接管主研究引擎。**

---

## 12. 多方案对比总表

| 方案 | 技术栈匹配度 | 部署复杂度 | 文档RAG能力 | Agent编排能力 | 学习成本 | 推荐度 |
|---|---:|---:|---:|---:|---:|---:|
| 纯 .NET 自研 Lite RAG | 5 | 1 | 3 | 2 | 2 | 5 |
| Semantic Kernel | 5 | 2 | 3 | 5 | 4 | 3 |
| Kernel Memory | 4 | 3 | 5 | 2 | 3 | 3 |
| Microsoft.Extensions.AI + 自研 | 5 | 1 | 2 | 2 | 2 | 5 |
| SK + KM 组合 | 4 | 4 | 5 | 5 | 5 | 3 |
| Python LangChain sidecar | 2 | 5 | 5 | 5 | 3 | 2 |
| 社区 .NET 库 | 3 | 2 | 3 | 3 | 3 | 3 |

---

## 13. 对本仓库的最终推荐路线

### 推荐路线 A（最推荐）

**v0.4.2 正式主线：**

- 自研 Lite RAG（FTS/BM25 + metadata filter）
- 不重构现有主编排层
- 在 RAG 子系统边界保持可替换
- 不引入 Python sidecar

适合原因：

- 与当前仓库最兼容
- 打包最轻
- 可以快速落地
- 可控性高

### 推荐路线 B（增强型）

**v0.4.2 ~ v0.4.3 渐进增强：**

- v0.4.2：先自研 Lite RAG
- v0.4.3：优先在 RAG 子系统试点社区 .NET 库或更强 retrieval 方案
- 若文档规模与 citation 要求明显升高，再评估 Kernel Memory
- 若未来确实要做复杂图谱关系推理，再评估 GraphRag.Net / GraphRAG 方向

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

> **本仓库当前最合适的路线是：保留现有多 Agent / 多 MCP 主编排层不大改，在财报线先做可替换的自研 Lite RAG；社区 .NET 库优先在 RAG 子系统试点，Kernel Memory 暂不作为 Desktop 常驻服务主线，GraphRag.Net 留给后续复杂图谱检索阶段再评估。**

---

## 16. 最终决策落档（2026-04-22）

经 PM 与用户在 v0.4.x 路线评审后确认，v0.4.2 的所有关键技术选型按以下结论执行。本节为权威决策来源；上文 §1–§15 为分析与候选讨论，遇到冲突以本节为准。

### 16.1 存储引擎：SQLite + FTS5（新建 `financial-rag.db`）

- 与现有 LiteDB（结构化财报库）**物理隔离**，新建独立 SQLite 文件 `financial-rag.db`，置于运行时 `LocalAppData` 数据根目录下。
- 使用 `Microsoft.Data.Sqlite` + 启用 FTS5 扩展。
- BM25 排序使用 FTS5 原生 `bm25()` 函数。
- 中文分词：v0.4.2 首版采用**入库前 jieba.NET 预切词 + 空格 join 后写入 FTS5**（FTS5 默认 unicode61 tokenizer）；不引入自定义 C tokenizer，避免桌面打包复杂度。
- 预留 `sqlite-vec` 扩展位（v0.4.3 启用向量召回时再加载，不强制 v0.4.2 加载）。

**理由**：LiteDB 全文索引无 BM25、中文召回弱；SQLite + FTS5 原生 BM25，未来加 `sqlite-vec` 即可平滑演进到 hybrid。

### 16.2 切块策略：三层结构（写入 schema 强约束）

```
Layer 1: 标题切（H1/H2/H3）
   ↓ 单段超过 800 字时
Layer 2: 段落兜底切（512–800 字，相邻 chunk 重叠 80 字）
Layer 3: 表格独立通道
   - 不进 prose 索引（FTS5 表）
   - 单独存为 structured_table（JSON）
   - chunk 中只保留 "见表 X-Y" 的指针引用
```

**chunk 表必备字段**：

| 字段 | 类型 | 说明 |
|---|---|---|
| `chunk_id` | TEXT PK | UUID |
| `source_type` | TEXT | 枚举：`financial_report` / `news` / `research_turn` / `trading_plan`（v0.4.2 只写入 `financial_report`） |
| `source_id` | TEXT | 来源主键（如 `pdf_file_id`） |
| `symbol` | TEXT | 股票代码 |
| `report_date` | TEXT | 报告期（如 `2024Q3`） |
| `report_type` | TEXT | 报告类型 |
| `section` | TEXT | 章节路径（如 `经营情况讨论与分析 > 行业格局`） |
| `block_kind` | TEXT | 枚举：`narrative` / `table_pointer` |
| `page_start` | INT | PDF 起始页（来自 v0.4.1） |
| `page_end` | INT | PDF 结束页 |
| `text` | TEXT | 切块文本 |
| `created_at` | INT | unix ts |

**理由**：标题切保留章节语义；800 字上限避免相关性稀释；表格独立保存防止数值碎片化；page 字段为 v0.4.3 citation 跳转铺路。

### 16.3 v0.4.2 范围：只做财报，但 schema 通用

- v0.4.2 只索引 `financial_report` 一种来源。
- chunk 表 `source_type` 字段保留枚举位，v0.4.3 之后视情况扩到 news / research_turn / trading_plan。
- 检索时**强制按 `source_type` 过滤**，prompt 注入时显式标注来源类型（如 `【证据·财报·2024Q3】...`），LLM 不会混淆。
- 未来扩源时每类独立 namespace + 独立 prompt 模板，**不做混合检索**。

**理由**：用户明确担忧"LLM 是否能分清 plans / research / 财报"，强类型隔离 + 显式来源标注是唯一可靠方案。

### 16.4 编排框架：不引入 SK / KM，自研三接口

v0.4.2 内自研以下三个接口，约 800–1500 行，完全可控：

```csharp
public interface IChunker {
    IReadOnlyList<FinancialChunk> Chunk(PdfParsedDocument doc);
}
public interface IRetriever {
    Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(RetrievalQuery q, CancellationToken ct);
}
public interface IEmbedder {  // v0.4.2 占位空实现，v0.4.3 启用
    Task<float[]> EmbedAsync(string text, CancellationToken ct);
}
```

- v0.4.2 **不引入** Microsoft Semantic Kernel，**不引入** Microsoft Kernel Memory。
- 未来若需迁移 KM/SK，三接口可做适配器套上去，不锁死。

**理由**：当前已有 MCP + 多 Agent 编排，SK 重叠度高；KM 对桌面打包过重；自研三接口范围明确、与现有日志/DTO/审计体系一致。

### 16.5 v0.4.2 验收硬约束（替换原 §14）

1. `financial-rag.db` 在运行时数据目录下自动创建，含 `chunks` 表 + FTS5 虚表。
2. `FinancialWorker` 在 PDF 解析完成后自动调用 `IChunker` 入库。
3. 提供 REST 接口 `POST /api/financial/rag/search`，入参 `{ query, symbol?, reportDate?, reportType?, topK }`，返回带 `page_start/page_end/section/score` 的 chunk 列表。
4. 中文查询召回率人工抽测 ≥ 50%（30 条手工 query 的命中率，详见 v0.4.2.1 评估集子任务，见 v0.4.3 §8）。
5. 不引入 Python 运行时、不引入 SK、不引入 KM。
