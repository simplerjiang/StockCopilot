export const getCopilotToolCalls = turn => (Array.isArray(turn?.toolCalls ?? turn?.ToolCalls) ? (turn.toolCalls ?? turn.ToolCalls) : [])

export const getCopilotToolResults = turn => (Array.isArray(turn?.toolResults ?? turn?.ToolResults) ? (turn.toolResults ?? turn.ToolResults) : [])

export const getCopilotFinalAnswer = turn => turn?.finalAnswer ?? turn?.FinalAnswer ?? null

export const getCopilotApprovedToolCalls = turn => getCopilotToolCalls(turn)
  .filter(item => (item?.approvalStatus ?? item?.ApprovalStatus) === 'approved')

export const getCopilotExecutedToolCallIds = turn => new Set(
  getCopilotToolResults(turn)
    .map(item => item?.callId ?? item?.CallId ?? '')
    .filter(Boolean)
)

export const parseCopilotInputSummary = summary => {
  return String(summary || '')
    .split(';')
    .map(item => item.trim())
    .filter(Boolean)
    .reduce((result, item) => {
      const index = item.indexOf('=')
      if (index < 0) {
        return result
      }

      const key = item.slice(0, index).trim()
      const value = item.slice(index + 1).trim()
      if (key) {
        result[key] = value
      }
      return result
    }, {})
}

export const summarizeCopilotToolPayload = (toolName, payload, formatDate = value => String(value || '')) => {
  if (!payload) {
    return '工具未返回结果。'
  }

  const data = payload.data ?? payload.Data ?? {}
  if (toolName === 'StockKlineMcp') {
    const bars = Array.isArray(data.bars ?? data.Bars) ? (data.bars ?? data.Bars) : []
    const levels = data.keyLevels ?? data.KeyLevels ?? {}
    const resistance = Array.isArray(levels.resistanceLevels ?? levels.ResistanceLevels) ? (levels.resistanceLevels ?? levels.ResistanceLevels) : []
    const support = Array.isArray(levels.supportLevels ?? levels.SupportLevels) ? (levels.supportLevels ?? levels.SupportLevels) : []
    return `K 线 ${bars.length} 根，压力位 ${resistance.length} 个，支撑位 ${support.length} 个。`
  }

  if (toolName === 'StockMinuteMcp') {
    const points = Array.isArray(data.points ?? data.Points) ? (data.points ?? data.Points) : []
    const sessionPhase = data.sessionPhase ?? data.SessionPhase ?? 'unknown'
    return `分时 ${points.length} 个点位，当前 session=${sessionPhase}。`
  }

  if (toolName === 'StockStrategyMcp') {
    const signals = Array.isArray(data.signals ?? data.Signals) ? (data.signals ?? data.Signals) : []
    const topSignals = signals.slice(0, 3).map(item => item.strategy ?? item.Strategy ?? '').filter(Boolean)
    return `策略信号 ${signals.length} 条${topSignals.length ? `，首批=${topSignals.join('/')}` : ''}。`
  }

  if (toolName === 'StockNewsMcp') {
    const itemCount = Number(data.itemCount ?? data.ItemCount ?? 0)
    const latestPublishedAt = data.latestPublishedAt ?? data.LatestPublishedAt ?? ''
    return `本地新闻 ${itemCount} 条${latestPublishedAt ? `，最近时间 ${formatDate(latestPublishedAt)}` : ''}。`
  }

  if (toolName === 'StockSearchMcp') {
    const resultCount = Number(data.resultCount ?? data.ResultCount ?? 0)
    const provider = data.provider ?? data.Provider ?? 'unknown'
    return `外部搜索 provider=${provider}，结果 ${resultCount} 条。`
  }

  return '工具已执行。'
}

export const buildCopilotToolResult = (callId, toolName, payload, formatDate) => ({
  callId,
  toolName,
  status: 'completed',
  traceId: payload?.traceId ?? payload?.TraceId ?? '',
  evidenceCount: Array.isArray(payload?.evidence ?? payload?.Evidence) ? (payload.evidence ?? payload.Evidence).length : 0,
  featureCount: Array.isArray(payload?.features ?? payload?.Features) ? (payload.features ?? payload.Features).length : 0,
  warnings: Array.isArray(payload?.warnings ?? payload?.Warnings) ? (payload.warnings ?? payload.Warnings) : [],
  degradedFlags: Array.isArray(payload?.degradedFlags ?? payload?.DegradedFlags) ? (payload.degradedFlags ?? payload.DegradedFlags) : [],
  summary: summarizeCopilotToolPayload(toolName, payload, formatDate)
})

export const buildCopilotAcceptanceExecutions = turn => {
  const toolCallsById = Object.fromEntries(getCopilotToolCalls(turn)
    .map(item => [item?.callId ?? item?.CallId ?? '', item])
    .filter(([callId]) => Boolean(callId)))

  return getCopilotToolResults(turn)
    .map(result => {
      const callId = result?.callId ?? result?.CallId ?? ''
      if (!callId) {
        return null
      }

      const toolCall = toolCallsById[callId] || {}
      const payload = turn?.toolPayloads?.[callId] || {}
      return {
        callId,
        toolName: result?.toolName ?? result?.ToolName ?? toolCall?.toolName ?? toolCall?.ToolName ?? '',
        policyClass: toolCall?.policyClass ?? toolCall?.PolicyClass ?? 'local_required',
        latencyMs: Number(payload?.latencyMs ?? payload?.LatencyMs ?? 0),
        evidenceCount: Number(result?.evidenceCount ?? result?.EvidenceCount ?? 0),
        featureCount: Number(result?.featureCount ?? result?.FeatureCount ?? 0),
        warnings: Array.isArray(result?.warnings ?? result?.Warnings) ? (result?.warnings ?? result?.Warnings) : [],
        degradedFlags: Array.isArray(result?.degradedFlags ?? result?.DegradedFlags) ? (result?.degradedFlags ?? result?.DegradedFlags) : []
      }
    })
    .filter(Boolean)
}