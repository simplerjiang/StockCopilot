# GOAL-AGENT-NEW-001 P0-Pre Phase F R1 开发报告

## 本轮目标

1. 不走旧 `StockAgentOrchestrator` 主路径，在新的 stock copilot / MCP contract 路径上补一个真实 live gate runner。
2. 让该 runner 直接调用 `ILlmService`，留下真实 `LLM-AUDIT` trace，而不是继续停留在启发式 draft。
3. 把 role contract / local-first / external-gated / stop/fallback / 禁止 direct query 规则写进 LLM 可见 prompt。
4. 对 LLM 产出的 tool 计划做后端强校验，拒绝越权、越界、未注册、未先走 local 的 external 搜索，并把被批准 MCP 的 tool traceId 与 LLM traceId 一起回传。

## 已完成实现

1. 在后端新增 `IStockCopilotLiveGateService` / `StockCopilotLiveGateService`。
2. 新增 `POST /api/stocks/copilot/live-gate`，作为窄的 live gate / acceptance runner，不伪装成完整多轮产品能力。
3. 新增运行时 DTO：
   - `StockCopilotLiveGateRequestDto`
   - `StockCopilotLiveGateResultDto`
   - `StockCopilotRejectedToolCallDto`
   - `StockCopilotTurnDto.LlmTraceId`
4. live gate 现在会：
   - 调用 `ILlmService.ChatAsync(...)`
   - 用真实返回的 `TraceId` 作为 LLM 审计主键
   - 解析模型输出 JSON 计划
   - 依据 `IStockAgentRoleContractRegistry`、`IRoleToolPolicyService`、`IMcpServiceRegistry` 做强校验
   - 只执行被批准的 `IMcpToolGateway` 调用
   - 把 `LLM traceId + tool traceIds + rejected tool calls + acceptance baseline` 一起回传
5. prompt 中已经显式写入：
   - tool registry 与 `policyClass`
   - 15 个角色 contract
   - `local_required / local_preferred / external_gated / disabled`
   - local-first 顺序约束
   - stop/fallback 规则
   - disabled 角色禁止 direct query
6. 后端强校验当前覆盖以下拦截面：
   - 未注册 role
   - 未注册 tool
   - 不允许 direct query 的角色
   - role 未授权 tool
   - 未开启 `allowExternalSearch` 的 external 工具
   - 未先走 local 的 external 搜索
   - 超预算工具计划
   - 跨 symbol 查询
   - 重复 tool plan

## 定向验证

### 后端单测命令

```powershell
dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~StockCopilotLiveGateServiceTests|FullyQualifiedName~StockCopilotAcceptanceServiceTests|FullyQualifiedName~StockAgentRoleContractRegistryTests|FullyQualifiedName~StockMcpGatewayPhaseATests|FullyQualifiedName~StockCopilotSessionServiceTests"
```

### 单测结果

- `21/21` 全部通过。
- 中途首次执行因运行中的 `SimplerJiangAiAgent.Api.exe` 锁住 build 输出而失败；已按仓库规则先停止该 API 进程后重跑，第二次通过。

### 本轮新增回归点

1. prompt 必须包含 contract / policy / disabled role / external-gated 规则。
2. `portfolio_manager` 直接规划 `StockNewsMcp` 会被拒绝。
3. 没有先走 local 的 `StockSearchMcp` 会被按 local-first 拦截。
4. 成功执行后的 `LLM traceId` 与 `tool traceId` 都会出现在 runtime 返回中。

## 2026-03-26 更正说明（以已验证事实为准）

1. 上述“tracked `llm-settings.json` 中 `apiKey` 为空”这一点本身属实，但它只说明 tracked 配置文件继续保持无 secret；它不再代表当前机器整体 provider 不可用。
2. 当前机器已确认 `backend/SimplerJiangAiAgent.Api/App_Data/llm-settings.local.json` 存在，active provider 为 `default`，有效 key 来自 local secret，而不是 tracked `llm-settings.json` 或环境变量。
3. 当前机器已复用现有 `http://localhost:5119` backend 实例完成真实 smoke：
   - `GET /api/health` 返回 `200 {"status":"ok"}`
   - `POST /api/stocks/copilot/live-gate` 以 `symbol=sh600000`、`allowExternalSearch=false`、问题“看下浦发银行日线结构和本地新闻证据”成功返回 `LlmTraceId = 7c254306ff89470d8ca971c08aab3090`、`FinalAnswerStatus = done`、`RejectedToolCallCount = 0`、`Acceptance.ExecutedToolCallCount = 3`
   - runtime 中至少 3 个非空 tool trace ids，对应 `MarketContextMcp`、`StockKlineMcp`、`StockNewsMcp`
4. 对应 `LLM-AUDIT` 日志与 admin trace 聚合均可命中该 `traceId` 的真实 `request/response` 与 `requestRaw/responseRaw` 记录。
5. 因此，“本轮无法在本机留下真实 provider smoke 轨迹；这是环境 blocker”只应保留为本报告生成当时的阶段性观察，不再代表当前结论。

## 跨机器复现命令

其他机器若尚未配置本地 secret 或环境变量，可在补齐 provider 配置后执行：

```powershell
Invoke-RestMethod -Method Post http://localhost:5119/api/stocks/copilot/live-gate -ContentType 'application/json' -Body '{"symbol":"sh600000","question":"看下浦发银行日线结构和本地新闻证据","sessionKey":null,"sessionTitle":"live-gate-smoke","taskId":"phase-f-r1-live","allowExternalSearch":false,"provider":"active","model":null,"temperature":0.1}'
```

复现时的最小验收点：

1. 返回 `Session.Turns[0].LlmTraceId` 非空。
2. 至少一个 `ToolResults[*].TraceId` 非空。
3. 若模型提了越权工具，`RejectedToolCalls` 中能看到明确拒绝原因。
4. `Acceptance` 已生成且 `ExecutedToolCallCount > 0`。

## 当前结论

1. `live LLM key / tool trace gate` 的代码实现已补齐并收口到新 stock copilot 路径。
2. 当前环境下 `live LLM key / tool trace gate` 已通过，不再是环境 blocker。需要继续保持的是 tracked `llm-settings.json` 不存 secret；其他机器若没有 `llm-settings.local.json` 或环境变量，仍需自行完成 provider 配置后再复现相同 smoke。