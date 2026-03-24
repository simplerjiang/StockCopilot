import {
  getCopilotApprovedToolCalls,
  getCopilotExecutedToolCallIds,
  getCopilotFinalAnswer
} from './stockInfoTabCopilot'

export const REQUIRED_PLAN_AGENT_IDS = ['stock_news', 'sector_news', 'financial_analysis', 'trend_analysis', 'commander']

export const PLAN_AGENT_LABELS = {
  stock_news: '个股资讯Agent',
  sector_news: '板块资讯Agent',
  financial_analysis: '个股分析Agent',
  trend_analysis: '走势分析Agent',
  commander: '指挥Agent'
}

export const formatPlanAgentLabels = agentIds => agentIds
  .map(agentId => PLAN_AGENT_LABELS[agentId] || agentId)
  .join('、')

export const inspectCommanderHistoryAgentResults = agentResults => {
  const list = Array.isArray(agentResults) ? agentResults : []
  const agentMap = new Map()
  list.forEach(item => {
    const agentId = String(item?.agentId ?? item?.AgentId ?? '').trim()
    if (agentId) {
      agentMap.set(agentId, item)
    }
  })

  const missingAgents = REQUIRED_PLAN_AGENT_IDS.filter(agentId => !agentMap.has(agentId))
  const failedAgents = REQUIRED_PLAN_AGENT_IDS.filter(agentId => {
    const item = agentMap.get(agentId)
    if (!item) {
      return false
    }
    const success = item?.success ?? item?.Success
    return success === false
  })
  const commander = agentMap.get('commander')
  const commanderData = commander?.data ?? commander?.Data ?? null

  if (!commander) {
    return {
      isComplete: false,
      blockedReason: '缺少指挥Agent结果，当前还不是完整的 commander 历史。'
    }
  }

  if ((commander?.success ?? commander?.Success) === false) {
    return {
      isComplete: false,
      blockedReason: '指挥Agent尚未成功完成，当前还不能生成交易计划。'
    }
  }

  if (!commanderData || typeof commanderData !== 'object') {
    return {
      isComplete: false,
      blockedReason: '指挥Agent未返回有效 data，当前还不能生成交易计划。'
    }
  }

  if (missingAgents.length) {
    return {
      isComplete: false,
      blockedReason: `缺少 ${formatPlanAgentLabels(missingAgents)}，当前多Agent历史尚未完整。`
    }
  }

  if (failedAgents.length) {
    return {
      isComplete: false,
      blockedReason: `${formatPlanAgentLabels(failedAgents)} 尚未成功完成，请先补齐完整 commander 历史。`
    }
  }

  return {
    isComplete: true,
    blockedReason: ''
  }
}

export const getSelectedCommanderHistoryAvailability = workspace => {
  const historyId = String(workspace?.selectedAgentHistoryId ?? '').trim()
  if (!historyId) {
    return {
      ready: false,
      blockedReason: '尚未生成完整的 commander 多Agent 历史。'
    }
  }

  const historyList = Array.isArray(workspace?.agentHistoryList) ? workspace.agentHistoryList : []
  const selectedItem = historyList.find(item => String(item?.id ?? item?.Id ?? '') === historyId)
  const apiReady = selectedItem?.isCommanderComplete ?? selectedItem?.IsCommanderComplete
  const apiBlockedReason = selectedItem?.commanderBlockedReason ?? selectedItem?.CommanderBlockedReason ?? ''

  if (apiReady === true) {
    return {
      ready: true,
      blockedReason: ''
    }
  }

  if (apiReady === false) {
    return {
      ready: false,
      blockedReason: apiBlockedReason || '当前已选 history 未达到 commander 完整性要求。'
    }
  }

  const resultAvailability = inspectCommanderHistoryAgentResults(workspace?.agentResults)
  return {
    ready: resultAvailability.isComplete,
    blockedReason: resultAvailability.isComplete
      ? ''
      : (apiBlockedReason || resultAvailability.blockedReason)
  }
}

export const isGroundedCopilotFinalAnswerReady = turn => {
  const finalAnswer = getCopilotFinalAnswer(turn)
  if (!finalAnswer) {
    return false
  }

  const status = String(finalAnswer.status ?? finalAnswer.Status ?? '').trim().toLowerCase()
  const needsToolExecution = Boolean(finalAnswer.needsToolExecution ?? finalAnswer.NeedsToolExecution)
  return status === 'done' && !needsToolExecution
}

export const getCopilotDraftPlanAvailability = (workspace, turn) => {
  const approvedToolCalls = getCopilotApprovedToolCalls(turn)
  const executedCallIds = getCopilotExecutedToolCallIds(turn)
  const executedApprovedCount = approvedToolCalls.filter(item => executedCallIds.has(item.callId ?? item.CallId ?? '')).length
  const pendingApprovedCount = approvedToolCalls.length - executedApprovedCount

  if (!approvedToolCalls.length) {
    return {
      enabled: false,
      blockedReason: '当前草案没有可承接到交易计划的工具结果。'
    }
  }

  if (executedApprovedCount <= 0) {
    return {
      enabled: false,
      blockedReason: '至少先执行一张已批准的 Copilot 工具卡。'
    }
  }

  if (pendingApprovedCount > 0) {
    return {
      enabled: false,
      blockedReason: `还有 ${pendingApprovedCount} 张已批准工具卡未执行，先补齐再起草交易计划。`
    }
  }

  if (!isGroundedCopilotFinalAnswerReady(turn)) {
    return {
      enabled: false,
      blockedReason: '先让 Copilot 收口到 grounded final answer，再进入交易计划。'
    }
  }

  if (!workspace?.detail?.quote?.symbol) {
    return {
      enabled: false,
      blockedReason: '当前没有绑定股票上下文。'
    }
  }

  const historyAvailability = getSelectedCommanderHistoryAvailability(workspace)
  if (workspace?.selectedAgentHistoryId && !historyAvailability.ready) {
    return {
      enabled: false,
      blockedReason: historyAvailability.blockedReason
    }
  }

  return {
    enabled: true,
    blockedReason: ''
  }
}