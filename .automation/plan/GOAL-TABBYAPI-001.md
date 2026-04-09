# GOAL-TABBYAPI-001: ExLlamaV3 + TabbyAPI 本地推理加速集成

> **状态：已完成预研，结论为 NOT VIABLE（8GB 显存下）**  
> **最终决策：继续使用 Ollama (gemma4:e2b, 57 tok/s) 作为主引擎**

## 概述

在保留现有 Ollama 推理引擎的基础上，新增 ExLlamaV3 + TabbyAPI 作为可选的本地推理后端，利用 ExLlamaV3 更高效的 NVIDIA GPU 推理能力提升本地模型速度。

**上次更新**：2026-04-09

## 背景

| 指标 | 当前 (Ollama/llama.cpp) | 目标 (ExLlamaV3) |
|------|------------------------|------------------|
| 推理速度 | 55-61 tok/s | 80-120+ tok/s |
| 引擎 | llama.cpp | ExLlamaV3 |
| 模型格式 | GGUF (Q4_K_M) | EXL3 (QTIP) |
| GPU | RTX 5060 8GB | 同 |

## 调研发现（2026-04-09）

### 1. ExLlamaV2 已存档，替代方案是 ExLlamaV3

- ExLlamaV2 仓库已被作者 turboderp **archived**，不再维护。
- **ExLlamaV3** 是活跃项目，当前版本 **v0.0.28**（2026 年 4 月初发布）。
- ExLlamaV3 使用 **EXL3 格式**（基于 QTIP 算法），不再使用 EXL2。
- ExLlamaV3 提供 **cu128 Windows wheels**，兼容 RTX 5060 的 CUDA 13.2。

### 2. Gemma4 在 ExLlamaV3 中的支持状态

- Gemma4 支持已在 ExLlamaV3 **dev 分支**实现，**尚未进入正式 release**。
- GitHub Issue #186 显示 Gemma4 MoE 版本有**量化精度问题**。
- HuggingFace 上有 `turboderp/gemma-4-26B-A4B-exl3`（26B，太大，不适合 8GB 显存）。
- **没有 gemma4:e2b 的 EXL3 格式模型**。

### 3. Gemma3 替代方案可行

Gemma3 系列在 ExLlamaV3 中有稳定支持，且有已转换的 EXL3 模型可用。详见下方"模型选型"章节。

### 4. TabbyAPI 确认支持 ExLlamaV3

TabbyAPI 已更新支持 ExLlamaV3 后端，可直接配置使用。

## 模型选型

### RTX 5060 8GB 下的 Gemma3 候选模型

| 模型 | 参数量 | 4bpw 显存 | 3bpw 显存 | 预估速度 | 推荐度 |
|------|--------|----------|----------|---------|--------|
| gemma-3-4b-it | 4B | ~2.5 GB | ~2 GB | ~120 tok/s | ⭐⭐⭐ 速度优先 |
| gemma-3-12b-it | 12B | ~6.5 GB | ~5 GB | ~40 tok/s | ⭐⭐⭐⭐ 质量优先 |

### 与当前 gemma4:e2b (Ollama) 的对比

| 对比维度 | gemma4:e2b (Ollama) | gemma3-4b (ExLlamaV3) | gemma3-12b @ 3bpw (ExLlamaV3) |
|---------|---------------------|----------------------|------------------------------|
| 推理速度 | ~57 tok/s | ~120 tok/s | ~40 tok/s |
| 质量 | 基线 | 接近基线 | 明显优于基线 |
| 显存占用 | ~4 GB | ~2.5 GB | ~5 GB |
| 格式 | GGUF | EXL3 | EXL3 |

**结论**：
- **gemma3-4b**：质量接近 gemma4:e2b，速度翻倍，显存富余，适合速度敏感场景。
- **gemma3-12b @ 3bpw**：质量明显更好，但速度比当前 Ollama 慢，适合质量敏感场景。

## 架构设计

### 现有 Provider 体系

```
LlmService
  ├── OpenAiProvider (ProviderType: "openai") ← TabbyAPI 可复用
  ├── OllamaProvider (ProviderType: "ollama")
  └── AntigravityProvider (ProviderType: "antigravity")
```

### TabbyAPI 接入方式

**Phase 1（零代码改动）**：利用现有 `OpenAiProvider`，配置新 Provider：
```json
{
  "Provider": "tabbyapi",
  "ProviderType": "openai",
  "BaseUrl": "http://localhost:5000",
  "Model": "gemma-3-4b-it-exl3",
  "Enabled": true
}
```

**Phase 2（可选优化）**：如需 TabbyAPI 特有功能（如自定义采样参数、连续批处理控制），创建专用 `TabbyApiProvider`。

### 部署架构

```
┌─────────────────────────────┐
│       应用后端 (ASP.NET)      │
│  LlmService                 │
│    ├─ OllamaProvider ───────┼──→ Ollama (localhost:11434)
│    └─ OpenAiProvider ───────┼──→ TabbyAPI (localhost:5000)
└─────────────────────────────┘
                                    ↓
                              ExLlamaV3 引擎
                                    ↓
                              EXL3 模型文件
```

## 实施计划

### Story 1: 环境验证（预研）
**验收标准**：确认 ExLlamaV3 + TabbyAPI 可在当前硬件上运行  
**状态**：DONE

- [ ] 1.1 安装 ExLlamaV3 cu128 Windows wheel（兼容 RTX 5060 CUDA 13.2）
- [ ] 1.2 检查 HuggingFace 上 gemma3-4b 或 gemma3-12b 的 EXL3 量化模型可用性
- [ ] 1.3 如无现成 EXL3 模型，使用 ExLlamaV3 convert 工具自行转换（需原始 HF 权重 + Python 环境）
- [ ] 1.4 下载选定的 EXL3 模型到本地
- [ ] 1.5 安装 TabbyAPI，配置 ExLlamaV3 后端
- [ ] 1.6 启动 TabbyAPI，手动测试 `/v1/chat/completions` 端点
- [ ] 1.7 运行速度基准测试，与 Ollama 57 tok/s 对比
- [ ] 1.8 验证模型输出质量（与 gemma4:e2b 在典型新闻研报任务上对比）

**阻塞条件**：
- 如果 ExLlamaV3 cu128 wheel 安装失败或不兼容 RTX 5060 → 等待 ExLlamaV3 修复或回退 Ollama
- 如果 EXL3 模型转换失败或质量不达标 → 尝试其他模型（如 Qwen2.5）或等待社区提供转换好的模型

### Story 2: 应用集成
**验收标准**：用户可通过 Admin UI 在 Ollama 和 TabbyAPI 之间切换  
**状态**：CANCELLED  
**依赖**：Story 1 通过

- [ ] 2.1 通过 Admin API 添加 TabbyAPI Provider 配置（ProviderType=openai）
- [ ] 2.2 验证 LlmService 正确路由到 OpenAiProvider
- [ ] 2.3 端到端测试：通过应用发送实际研究任务到 TabbyAPI
- [ ] 2.4 对比 Ollama 和 TabbyAPI 的输出质量
- [ ] 2.5 验证 Provider 切换功能（Active Provider 从 ollama 切到 tabbyapi）
- [ ] 2.6 错误处理验证：TabbyAPI 未启动时的降级行为

### Story 3: 运维与文档
**验收标准**：有完整的启动/维护文档  
**状态**：CANCELLED  
**依赖**：Story 2 通过

- [ ] 3.1 编写 TabbyAPI 启动脚本（Windows .bat 或 .ps1）
- [ ] 3.2 更新 README.md 添加 TabbyAPI + ExLlamaV3 配置说明
- [ ] 3.3 更新 start-all.bat（可选：自动启动 TabbyAPI）
- [ ] 3.4 记录性能对比数据到文档

## 风险与缓解

| 风险 | 概率 | 影响 | 缓解措施 |
|------|------|------|---------|
| ExLlamaV3 仍在 0.0.x 早期阶段，API 可能不稳定 | 中 | 延迟 | 锁定已验证版本（v0.0.28），不追最新；保留 Ollama 回退 |
| Gemma4 MoE 模型量化精度有已知问题 | 高 | 阻塞 Gemma4 路线 | 改用 Gemma3 系列（已有稳定 EXL3 支持） |
| 小模型（4B）可能需要自行转换 EXL3 格式 | 中 | 延迟（数小时） | 预留转换时间，或优先选用 HF 上已有的 EXL3 模型 |
| RTX 50 系列 + ExLlamaV3 兼容性未经生产验证 | 中 | 阻塞 | Story 1 预研阶段即可发现，快速回退到 Ollama |
| TabbyAPI 稳定性不足 | 低 | 可控 | 保留 Ollama 作为回退选项 |
| 两套服务维护成本高 | 确定 | 持续 | 日常只启动一个，另一个作备选 |

## 决策待定

### ~~待定~~ 已决策 1：是否等待 ExLlamaV3 正式支持 Gemma4 再行动？

- **等待的理由**：Gemma4 是当前生产模型，保持模型一致性可减少质量回归风险。
- **不等待的理由**：Gemma4 MoE 量化精度问题未知何时解决；Gemma3-12b 质量可能已经足够好；dev 分支支持不等于稳定可用。
- **决策结论**：**不等。先用 Gemma3 模型验证流程。** Gemma4 支持成熟后再切换模型。

### ~~待定~~ 已决策 2：优先尝试 Gemma3-4b（速度优先）还是 Gemma3-12b（质量优先）？

- **Gemma3-4b 优势**：~120 tok/s，显存仅 2.5 GB，速度是 Ollama 的 2 倍。
- **Gemma3-12b 优势**：质量明显好于 gemma4:e2b，但速度 ~40 tok/s，比 Ollama 慢。
- **决策结论**：**Gemma3-4b（速度优先）。RTX 5060 8GB 下预估 ~120 tok/s。** Story 1 中如质量不达标再评估 12b。

### ~~待定~~ 已决策 3：是否在 ExLlamaV3 达到 1.0 前就投入集成？

- **投入的理由**：v0.0.28 已可用，cu128 wheels 已发布，TabbyAPI 已支持；等 1.0 可能遥遥无期。
- **不投入的理由**：0.0.x 阶段可能有破坏性变更，集成后维护成本高。
- **决策结论**：**接受早期风险，先做 Story 1 预研。如不可行回退 Ollama。**

## 决策记录

- **保留 Ollama**：Ollama 作为默认推理引擎，稳定可靠
- **TabbyAPI 作为可选加速**：仅在需要更快速度时启用
- **Phase 1 先行**：利用现有 OpenAiProvider，零代码改动验证可行性
- **不做强制迁移**：用户可根据需要自由切换
- **ExLlamaV3 替代 ExLlamaV2**（2026-04-09）：ExLlamaV2 已存档，全部计划改为基于 ExLlamaV3
- **模型路线调整为 Gemma3**（2026-04-09）：因 Gemma4 EXL3 格式不可用，改用 Gemma3 系列作为首选

## 任务分级

**L 级**（新功能，涉及外部依赖）  
流程：Dev → Test → UI Designer → User Rep + 写报告

## 附录：当前 Ollama 优化已完成

| 优化项 | 状态 |
|--------|------|
| `OLLAMA_FLASH_ATTENTION=1` | ✅ 已设置 |
| `OLLAMA_KV_CACHE_TYPE=q8_0` | ✅ 已设置 |
| `num_gpu=99` 默认参数 | ✅ 已加入代码 |
| 速度 37.9 → 57.5 tok/s | ✅ 已验证 |
| Ollama batch size 配置化 | ✅ 已完成 |

## 预研结果（2026-04-09 完成）

### 测试矩阵

| 方案 | 引擎 | 模型 | 速度 | 中文质量 | 可行性 |
|------|------|------|------|---------|--------|
| Ollama (当前) | llama.cpp | gemma4:e2b (MoE 5.1B/2B) | **57 tok/s** | ✅ 好 | ✅ 最优 |
| ExLlamaV3 + TabbyAPI | ExLlamaV3 0.0.28 | gemma3-4b EXL3 4.5bpw | 38 tok/s | ❌ 差 | ❌ 慢于 Ollama |
| ExLlamaV2 + TabbyAPI | ExLlamaV2 0.3.2 | gemma3-4b EXL2 4.65bpw | **89 tok/s** | ❌ 差 | ⚠️ 速度快质量差 |
| ExLlamaV2 + TabbyAPI | ExLlamaV2 0.3.2 | gemma3-12b EXL2 3.0bpw | 4.4 tok/s | ❌ 乱码 | ❌ 不可用 |

### 关键发现

1. **ExLlamaV3 在 Windows 上比 Ollama 慢**：因为 `flash_attn` 无 Windows 预构建，SDPA shim 性能不足
2. **ExLlamaV2 自带 attention 内核，速度领先**：89 tok/s 是所有方案中最快的
3. **速度与质量不可兼得（8GB 显存）**：
   - gemma3-4b 速度快但中文 prompt 遵循差
   - gemma3-12b @ 3bpw 中文输出完全崩塌（重复单字乱码）
   - gemma4:e2b MoE 架构在速度/质量平衡上最优
4. **基础设施验证通过**：TabbyAPI + ExLlamaV2 端到端集成无代码改动，配置已留存

### 保留资产

- TabbyAPI 安装：`E:\tabbyapi\tabbyAPI`（已停止）
- ExLlamaV2 + ExLlamaV3：已安装在 Python 环境
- EXL2 模型：`E:\tabbyapi\models\gemma-3-4b-it-exl2-4.65bpw`
- EXL3 模型：`E:\tabbyapi\models\gemma-3-4b-it-exl3-4.5bpw-h8`
- 12b 模型：`E:\tabbyapi\models\gemma-3-12b-it-exl2-3bpw`
- Provider 配置：`tabbyapi` 已在应用中注册（disabled）

### 重新评估条件

- 升级到 >12GB 显存 GPU → 可用更高 bpw 的大模型
- ExLlamaV3 发布 Blackwell 原生 flash_attn 支持 → 重新测速
- 出现更强的中文小模型 (<= 8B) → 在 ExLlamaV2 上重新测试

## 附录：关键外部链接

| 资源 | 说明 |
|------|------|
| ExLlamaV3 GitHub | turboderp/exllamav3，活跃开发中 |
| ExLlamaV3 Issue #186 | Gemma4 MoE 量化精度问题跟踪 |
| TabbyAPI GitHub | theroyallab/tabbyAPI |
| HF: turboderp/gemma-4-26B-A4B-exl3 | 26B 模型，不适合 8GB 显存 |
