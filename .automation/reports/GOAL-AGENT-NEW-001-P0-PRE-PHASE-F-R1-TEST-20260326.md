# GOAL-AGENT-NEW-001 P0-Pre Phase F-R1 Test / Acceptance Report (2026-03-26)

This report records the verified acceptance facts for the current machine and current environment only. It does not claim that other machines are already configured the same way.

## English

### Scope

1. Record the verified Phase F-R1 acceptance facts for the live LLM key / tool trace gate.
2. Clarify that tracked `llm-settings.json` remains secret-free by design.
3. Clarify that cross-machine reproduction still requires a local secret or environment-based provider configuration.

### Provider Configuration Evidence

1. `backend/SimplerJiangAiAgent.Api/App_Data/llm-settings.local.json` exists.
2. The current active provider is `default`.
3. A valid provider key exists and comes from local secret material, not from tracked `llm-settings.json`, and not from environment variables.

### Targeted Backend Unit Tests

Command:

```powershell
dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~StockCopilotLiveGateServiceTests|FullyQualifiedName~StockCopilotAcceptanceServiceTests|FullyQualifiedName~StockAgentRoleContractRegistryTests|FullyQualifiedName~StockMcpGatewayPhaseATests|FullyQualifiedName~StockCopilotSessionServiceTests"
```

Result:

1. `21/21` passed.

### Runtime Smoke Verification

1. The backend reused the existing `http://localhost:5119` instance.
2. Health check succeeded:
   - `GET /api/health`
   - Response: `200 {"status":"ok"}`
3. Live smoke request succeeded with local-first settings:

```http
POST /api/stocks/copilot/live-gate
Content-Type: application/json

{
  "symbol": "sh600000",
  "question": "看下浦发银行日线结构和本地新闻证据",
  "allowExternalSearch": false
}
```

### Key Live Smoke Result Fields

1. `LlmTraceId = 7c254306ff89470d8ca971c08aab3090`
2. `FinalAnswerStatus = done`
3. `RejectedToolCallCount = 0`
4. `Acceptance.ExecutedToolCallCount = 3`
5. At least 3 non-empty tool trace ids were returned for:
   - `MarketContextMcp`
   - `StockKlineMcp`
   - `StockNewsMcp`

### Trace Evidence

1. The same `traceId` is present in `LLM-AUDIT` with real `request/response` records.
2. The admin trace aggregation also resolves the same trace with `requestRaw/responseRaw` evidence.

### Conclusion

1. The live LLM key / tool trace gate is passed in the current environment.
2. Keeping tracked `backend/SimplerJiangAiAgent.Api/App_Data/llm-settings.json` free of secrets is still the correct state.
3. Other machines are not blocked by code, but they still need a local secret or environment-based provider configuration before reproducing the same smoke.

## 中文

### 范围

1. 记录当前机器、当前环境下 Phase F-R1 的 live LLM key / tool trace gate 验收事实。
2. 说明 tracked `llm-settings.json` 继续保持无 secret 是正确状态。
3. 说明跨机器复现仍需本地 secret 或环境变量提供 provider 配置。

### Provider 配置证据

1. `backend/SimplerJiangAiAgent.Api/App_Data/llm-settings.local.json` 存在。
2. 当前 active provider 为 `default`。
3. 有效 provider key 真实存在，来源是 local secret，而不是 tracked `llm-settings.json`，也不是环境变量。

### 定向后端单测

命令：

```powershell
dotnet test .\backend\SimplerJiangAiAgent.Api.Tests\SimplerJiangAiAgent.Api.Tests.csproj --filter "FullyQualifiedName~StockCopilotLiveGateServiceTests|FullyQualifiedName~StockCopilotAcceptanceServiceTests|FullyQualifiedName~StockAgentRoleContractRegistryTests|FullyQualifiedName~StockMcpGatewayPhaseATests|FullyQualifiedName~StockCopilotSessionServiceTests"
```

结果：

1. `21/21` 通过。

### 运行态 Smoke 验证

1. backend 复用现有 `http://localhost:5119` 实例。
2. 健康检查成功：
   - `GET /api/health`
   - 返回 `200 {"status":"ok"}`
3. local-first live smoke 请求成功：

```http
POST /api/stocks/copilot/live-gate
Content-Type: application/json

{
  "symbol": "sh600000",
  "question": "看下浦发银行日线结构和本地新闻证据",
  "allowExternalSearch": false
}
```

### 关键返回字段

1. `LlmTraceId = 7c254306ff89470d8ca971c08aab3090`
2. `FinalAnswerStatus = done`
3. `RejectedToolCallCount = 0`
4. `Acceptance.ExecutedToolCallCount = 3`
5. 至少返回 3 个非空 tool trace ids，对应：
   - `MarketContextMcp`
   - `StockKlineMcp`
   - `StockNewsMcp`

### Trace 证据

1. 相同 `traceId` 已在 `LLM-AUDIT` 中确认存在真实 `request/response` 记录。
2. admin trace 聚合也能命中同一 trace 的 `requestRaw/responseRaw`。

### 最终结论

1. 当前环境下，live LLM key / tool trace gate 已通过。
2. tracked `backend/SimplerJiangAiAgent.Api/App_Data/llm-settings.json` 继续保持无 secret 是正确状态。
3. 其他机器并不存在代码 blocker，但仍需先通过本地 secret 或环境变量补齐 provider 配置，才能复现相同 smoke。