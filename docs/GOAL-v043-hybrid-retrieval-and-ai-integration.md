# v0.4.3 计划书：Hybrid Retrieval 与 AI 分析集成增强

> 版本：v0.4.3  
> 目标类型：增强检索质量 / 接入现有 AI 分析路径

---

## 1. 版本目标

当 `v0.4.0` 和 `v0.4.1` 已经把财报中心与 PDF 白盒能力做好，`v0.4.2` 已经具备财报文本 Lite RAG 后，`v0.4.3` 再解决“检索质量”和“AI 集成”两个增强问题。

---

## 2. 核心范围

### 2.1 Hybrid Retrieval

在 Lite RAG 基础上，增强为：

- 关键词召回
- 向量召回（如后续启用 embedding）
- 结果合并与简单重排

### 2.2 财报证据接入 AI 分析

将财报引用能力逐步接到：

- `股票信息` 页 AI 分析
- Research / Recommend 路径
- 未来工作台类分析功能

### 2.3 证据引用回传

回答中显示：

- 引用段落
- 报告期
- 章节
- 文件来源

---

## 3. 本版本前提

本版本只有在以下前提满足后才应启动：

1. 财报中心页面稳定可用
2. PDF 原件与解析结果已可对照
3. 财报文本切块与 Lite RAG 已跑通

---

## 4. 技术重点

1. 召回质量评估
2. chunk 质量评估
3. 引用可信度评估
4. 与现有 AI 工作流的接口边界

---

## 5. 风险

1. 过早做 embedding 可能会掩盖 chunk 质量问题
2. 若 citation 设计不好，AI 引用会看起来像“有来源但不可核验”
3. 若没有前面几个版本的白盒化基础，v0.4.3 很容易又回到黑盒状态

---

## 6. 完成定义

v0.4.3 的目标不是“更聪明”这句空话，而是：

- 检索质量明显优于 Lite RAG
- 回答可以附财报证据引用
- 用户能从引用回到 PDF 与解析详情

---

## 7. 最终决策落档（2026-04-22）

经 PM 与用户在 v0.4.x 路线评审后确认。本节为权威决策来源；上文 §1–§6 为初版骨架，遇冲突以本节为准。

### 7.1 Recency 处理：保持原方案（时间衰减打分）

- Hybrid 排序保留时间衰减作为打分维度之一，权重不超过 0.2。
- 同时提供按报告期/年度的**显式筛选项**，让用户可强制锁定时间窗口。
- 衰减函数：`exp(-Δdays / 365)`，可配置半衰期。

### 7.2 Embedding：本地 Ollama，UI 可选模型

- 默认走本地 Ollama（HTTP `http://localhost:11434/api/embeddings`）。
- LLM 设置页面新增 **Embedding 模型管理**子面板：
  - 检测 Ollama 服务是否在线
  - 列出已安装 embedding 模型
  - 一键 `ollama pull <model>` 安装
  - 切换模型时**强制提示重建索引**（embedding 维度不一致会崩）
  - 维度记录到 chunk 表 metadata，查询时校验
- 不引入 ONNX Runtime、不引入云端 OpenAI embedding（避免 API key 配置复杂度）。

#### 推荐模型清单（按 CN 财报场景排序）

| 模型 | 大小 | 维度 | 中文 | 速度 | 推荐场景 |
|---|---|---|---|---|---|
| `bge-m3` | ~2.2GB | 1024 | ★★★★★ | 中 | **首推默认值**，多语言+长文本+稠密/稀疏混合 |
| `bge-large-zh-v1.5` | ~1.3GB | 1024 | ★★★★★ | 中 | 纯中文场景略快于 bge-m3，不支持长文本 |
| `nomic-embed-text` | ~274MB | 768 | ★★ | 快 | 低配机/英文为主时备用，**财报场景不推荐** |
| `mxbai-embed-large` | ~670MB | 1024 | ★★★ | 中 | 英文强，中文一般 |
| `snowflake-arctic-embed` | 110MB–670MB | 384–1024 | ★★★ | 快 | 多尺寸可选，资源敏感场景 |

### 7.3 Citation 链路：依赖 v0.4.1 page 字段

- v0.4.3 的 citation DTO 必须包含 `page_start` / `page_end`（来自 v0.4.1 §9.1 决策）。
- 前端引用 chip 点击后直接打开 PDF Viewer 并跳转到 `page_start`。
- 若 chunk 缺失 page 字段（历史数据），UI 显示"原文页码不可用"提示。

### 7.4 不引入 SK / KM（沿用 v0.4.2 决策）

- v0.4.3 仍走自研三接口（`IChunker` / `IRetriever` / `IEmbedder`）。
- Hybrid 实现路径：
  - `IRetriever` 内部组合 BM25（FTS5）+ 向量（sqlite-vec）+ 简单 RRF（Reciprocal Rank Fusion）合并
  - 不引入独立 reranker 服务（CPU reranker 延迟在桌面端不可接受），用 RRF 替代

### 7.5 完成定义补充（替换原 §6）

1. `financial-rag.db` 启用 sqlite-vec 扩展，新增 `chunk_embeddings` 表。
2. `FinancialWorker` 在 chunk 入库后调用 `IEmbedder` 异步生成向量。
3. `POST /api/financial/rag/search` 支持 `mode: "bm25" | "vector" | "hybrid"` 参数。
4. AI 分析路径（股票信息页、Research、Recommend）在调用 LLM 前注入 hybrid 检索结果，并在响应中保留 citation 数组。
5. 前端引用 chip 可跳转 PDF Viewer 并定位到 `page_start`。
6. **v0.4.2.1 子任务**（见 §8）必须先完成：30 条人工标注财报 Q&A 评估集，nDCG@5 作为 Hybrid 上线门禁，Hybrid 必须显著优于 Lite RAG baseline 才允许默认启用。

---

## 8. v0.4.2.1 评估集子任务（v0.4.3 前置门禁）

### 8.1 范围

- 人工标注 **30 条**财报 Q&A，覆盖：
  - 经营情况讨论（10 条）
  - 风险提示（5 条）
  - 募资用途（5 条）
  - 会计政策变化（5 条）
  - 分红方案（5 条）
- 每条 Q&A 标注 1–3 个"正确 chunk"作为 ground truth。

### 8.2 评估指标

- **nDCG@5**（主指标）
- **Recall@10**（辅指标）
- **MRR**（辅指标）

### 8.3 离线评估脚本

- 提供 CLI：`dotnet run --project tools/RagEval -- --mode bm25|vector|hybrid`
- 输出 markdown 报告到 `docs/RAG-eval-<date>.md`

### 8.4 上线门禁

- Hybrid 的 nDCG@5 必须比 Lite RAG baseline **相对提升 ≥ 15%**，否则 v0.4.3 默认仍走 Lite RAG，Hybrid 仅作为"实验性开关"开放。

### 8.5 时间安排

- v0.4.2 完成后立即启动 v0.4.2.1，独立成 1–2 天的评估集冲刺。
- v0.4.3 开发期间评估脚本作为回归门禁。
