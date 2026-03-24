import { computed } from 'vue'

export const createStockInfoTabCopilotRuntime = deps => {
  const {
    chatSymbolKey,
    chatWindowRefs,
    currentStockKey,
    currentWorkspace,
    fetchAgentHistory,
    fetchBackendGet,
    fetchLocalNews,
    fetchNewsImpact,
    formatDate,
    getCopilotApprovedToolCalls,
    getCopilotDraftPlanAvailability,
    getSelectedCommanderHistoryAvailability,
    getWorkspace,
    isAbortError,
    nextTick,
    openTradingPlanDraft,
    parseCopilotInputSummary,
    parseResponseMessage,
    refreshChartData,
    replaceAbortController,
    runAgents,
    buildCopilotAcceptanceExecutions,
    buildCopilotToolResult
  } = deps

  const getCurrentCopilotTurn = workspace => {
    if (!workspace) {
      return null
    }

    const turns = Array.isArray(workspace.copilotReplayTurns) ? workspace.copilotReplayTurns : []
    if (!turns.length) {
      return null
    }

    return turns.find(item => item.turnId === workspace.copilotCurrentTurnId) ?? turns[0] ?? null
  }

  const buildEffectiveCopilotAction = (workspace, turn, action) => {
    const actionType = action?.actionType ?? action?.ActionType ?? ''
    if (actionType !== 'draft_trading_plan') {
      return action
    }

    const availability = getCopilotDraftPlanAvailability(workspace, turn)
    return {
      ...action,
      enabled: availability.enabled,
      blockedReason: availability.blockedReason
    }
  }

  const buildEffectiveCopilotTurn = workspace => {
    const turn = getCurrentCopilotTurn(workspace)
    if (!turn) {
      return null
    }

    const followUpActions = Array.isArray(turn.followUpActions ?? turn.FollowUpActions)
      ? (turn.followUpActions ?? turn.FollowUpActions)
      : []

    return {
      ...turn,
      followUpActions: followUpActions.map(action => buildEffectiveCopilotAction(workspace, turn, action))
    }
  }

  const currentCopilotTurn = computed(() => buildEffectiveCopilotTurn(currentWorkspace.value))

  const buildCopilotToolUrl = (turn, toolCall) => {
    const toolName = toolCall?.toolName ?? toolCall?.ToolName ?? ''
    const inputSummary = parseCopilotInputSummary(toolCall?.inputSummary ?? toolCall?.InputSummary ?? '')
    const symbolKey = inputSummary.symbol || turn?.symbol || turn?.Symbol || currentStockKey.value
    const taskId = toolCall?.callId ?? toolCall?.CallId ?? `stock-copilot-${Date.now()}`

    if (toolName === 'StockKlineMcp') {
      const params = new URLSearchParams({
        symbol: symbolKey,
        interval: inputSummary.interval || 'day',
        limit: inputSummary.limit || '120',
        taskId
      })
      return `/api/stocks/mcp/kline?${params.toString()}`
    }

    if (toolName === 'StockMinuteMcp') {
      const params = new URLSearchParams({
        symbol: symbolKey,
        limit: inputSummary.limit || '240',
        taskId
      })
      return `/api/stocks/mcp/minute?${params.toString()}`
    }

    if (toolName === 'StockStrategyMcp') {
      const params = new URLSearchParams({
        symbol: symbolKey,
        interval: inputSummary.interval || 'day',
        strategies: inputSummary.strategies || '',
        taskId
      })
      return `/api/stocks/mcp/strategy?${params.toString()}`
    }

    if (toolName === 'StockNewsMcp') {
      const params = new URLSearchParams({
        symbol: symbolKey,
        lookbackHours: inputSummary.lookbackHours || '72',
        taskId
      })
      return `/api/stocks/mcp/news?${params.toString()}`
    }

    if (toolName === 'StockSearchMcp') {
      const params = new URLSearchParams({
        query: inputSummary.query || symbolKey,
        limit: inputSummary.limit || '5',
        taskId
      })
      return `/api/stocks/mcp/search?${params.toString()}`
    }

    return ''
  }

  const upsertCopilotTurn = (workspace, session, turn) => {
    if (!workspace || !session || !turn) {
      return
    }

    const normalizedTurn = {
      ...turn,
      toolPayloads: turn.toolPayloads ?? {},
      toolResults: Array.isArray(turn.toolResults ?? turn.ToolResults) ? [...(turn.toolResults ?? turn.ToolResults)] : []
    }

    const replayTurns = Array.isArray(workspace.copilotReplayTurns) ? workspace.copilotReplayTurns : []
    workspace.copilotSessionKey = session.sessionKey ?? session.SessionKey ?? workspace.copilotSessionKey
    workspace.copilotSessionTitle = session.title ?? session.Title ?? workspace.copilotSessionTitle
    workspace.copilotCurrentTurnId = normalizedTurn.turnId ?? normalizedTurn.TurnId ?? ''
    workspace.copilotReplayTurns = [
      normalizedTurn,
      ...replayTurns.filter(item => (item.turnId ?? item.TurnId) !== (normalizedTurn.turnId ?? normalizedTurn.TurnId))
    ].slice(0, 6)
  }

  const fetchCopilotAcceptanceBaseline = async (symbolKey = currentStockKey.value) => {
    const workspace = getWorkspace(symbolKey)
    const turn = buildEffectiveCopilotTurn(workspace)
    const symbol = workspace?.detail?.quote?.symbol || turn?.symbol || turn?.Symbol || ''

    if (!workspace || !turn || !symbol) {
      if (workspace) {
        workspace.copilotAcceptanceBaseline = null
        workspace.copilotAcceptanceError = ''
        workspace.copilotAcceptanceLoading = false
      }
      return
    }

    const controller = replaceAbortController(workspace.copilotAcceptanceAbortController)
    workspace.copilotAcceptanceAbortController = controller
    workspace.copilotAcceptanceLoading = true
    workspace.copilotAcceptanceError = ''
    const requestToken = (workspace.copilotAcceptanceRequestToken || 0) + 1
    workspace.copilotAcceptanceRequestToken = requestToken

    try {
      const response = await fetch('/api/stocks/copilot/acceptance/baseline', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        signal: controller.signal,
        body: JSON.stringify({
          symbol,
          turn,
          toolExecutions: buildCopilotAcceptanceExecutions(turn),
          replaySampleTake: 20
        })
      })
      if (!response.ok) {
        throw new Error(await parseResponseMessage(response, 'Copilot 质量基线生成失败'))
      }

      const baseline = await response.json()
      if (workspace.copilotAcceptanceRequestToken === requestToken) {
        workspace.copilotAcceptanceBaseline = baseline
        workspace.copilotAcceptanceError = ''
      }
    } catch (err) {
      if (isAbortError(err)) {
        return
      }
      if (workspace.copilotAcceptanceRequestToken === requestToken) {
        workspace.copilotAcceptanceError = err.message || 'Copilot 质量基线生成失败'
      }
    } finally {
      if (workspace.copilotAcceptanceRequestToken === requestToken) {
        workspace.copilotAcceptanceLoading = false
      }
      if (workspace.copilotAcceptanceAbortController === controller) {
        workspace.copilotAcceptanceAbortController = null
      }
    }
  }

  const submitCopilotDraft = async (symbolKey = currentStockKey.value) => {
    const workspace = getWorkspace(symbolKey)
    if (!workspace?.detail?.quote?.symbol) {
      if (workspace) {
        workspace.copilotError = '请先选择股票'
      }
      return
    }

    const question = String(workspace.copilotQuestion || '').trim()
    if (!question) {
      workspace.copilotError = '请输入问题'
      return
    }

    const controller = replaceAbortController(workspace.copilotDraftAbortController)
    workspace.copilotDraftAbortController = controller
    workspace.copilotLoading = true
    workspace.copilotError = ''
    try {
      const response = await fetch('/api/stocks/copilot/turns/draft', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        signal: controller.signal,
        body: JSON.stringify({
          symbol: workspace.detail.quote.symbol,
          question,
          sessionKey: workspace.copilotSessionKey || null,
          sessionTitle: workspace.copilotSessionTitle || `${workspace.detail.quote.name} Copilot`,
          taskId: `stock-copilot-draft-${workspace.symbolKey}`,
          allowExternalSearch: Boolean(workspace.copilotAllowExternalSearch)
        })
      })
      if (!response.ok) {
        throw new Error(await parseResponseMessage(response, 'Copilot 草案生成失败'))
      }

      const session = await response.json()
      const turn = Array.isArray(session?.turns ?? session?.Turns) ? (session.turns ?? session.Turns)[0] : null
      if (!turn) {
        throw new Error('Copilot 草案为空')
      }

      upsertCopilotTurn(workspace, session, turn)
      await fetchCopilotAcceptanceBaseline(symbolKey)
    } catch (err) {
      if (isAbortError(err)) {
        return
      }
      workspace.copilotError = err.message || 'Copilot 草案生成失败'
    } finally {
      workspace.copilotLoading = false
      if (workspace.copilotDraftAbortController === controller) {
        workspace.copilotDraftAbortController = null
      }
    }
  }

  const selectCopilotReplayTurn = async (turnId, symbolKey = currentStockKey.value) => {
    const workspace = getWorkspace(symbolKey)
    if (!workspace) {
      return
    }

    workspace.copilotCurrentTurnId = turnId
    await fetchCopilotAcceptanceBaseline(symbolKey)
  }

  const executeCopilotToolCall = async (callId, symbolKey = currentStockKey.value) => {
    const workspace = getWorkspace(symbolKey)
    const turn = getCurrentCopilotTurn(workspace)
    if (!workspace || !turn) {
      return
    }

    const toolCall = (turn.toolCalls ?? turn.ToolCalls ?? []).find(item => (item.callId ?? item.CallId) === callId)
    if (!toolCall) {
      return
    }

    if ((toolCall.approvalStatus ?? toolCall.ApprovalStatus) !== 'approved') {
      workspace.copilotError = toolCall.blockedReason ?? toolCall.BlockedReason ?? '该工具当前未获批执行。'
      return
    }

    const url = buildCopilotToolUrl(turn, toolCall)
    if (!url) {
      workspace.copilotError = '暂不支持该工具的前端执行。'
      return
    }

    const controller = replaceAbortController(workspace.copilotToolAbortController)
    workspace.copilotToolAbortController = controller
    workspace.copilotToolBusyCallId = callId
    workspace.copilotError = ''
    try {
      const response = await fetchBackendGet(url, { signal: controller.signal })
      if (!response.ok) {
        throw new Error(await parseResponseMessage(response, '工具执行失败'))
      }

      const payload = await response.json()
      turn.toolPayloads = {
        ...(turn.toolPayloads || {}),
        [callId]: payload
      }
      const nextResult = buildCopilotToolResult(callId, toolCall.toolName ?? toolCall.ToolName ?? '', payload, formatDate)
      const existing = Array.isArray(turn.toolResults ?? turn.ToolResults) ? (turn.toolResults ?? turn.ToolResults) : []
      turn.toolResults = [
        nextResult,
        ...existing.filter(item => (item.callId ?? item.CallId) !== callId)
      ]

      const toolName = toolCall.toolName ?? toolCall.ToolName ?? ''
      if (toolName === 'StockNewsMcp') {
        fetchNewsImpact(symbolKey, { force: true })
        fetchLocalNews(symbolKey, { force: true })
      }

      await fetchCopilotAcceptanceBaseline(symbolKey)
    } catch (err) {
      if (isAbortError(err)) {
        return
      }
      workspace.copilotError = err.message || '工具执行失败'
    } finally {
      workspace.copilotToolBusyCallId = ''
      if (workspace.copilotToolAbortController === controller) {
        workspace.copilotToolAbortController = null
      }
    }
  }

  const focusCopilotWorkspaceSection = async (symbolKey, section, options = {}) => {
    const workspace = getWorkspace(symbolKey)
    if (!workspace) {
      return
    }

    workspace.copilotFocusSection = section || ''
    if (options.chartView) {
      workspace.copilotChartFocusView = options.chartView
    }

    await nextTick()
    const selectorMap = {
      chart: '.stock-chart-section',
      news: '.stock-news-impact-section',
      strategy: '.stock-agent-section',
      plan: '.stock-plan-section'
    }
    const selector = selectorMap[section]
    if (!selector) {
      return
    }

    const element = document.querySelector(selector)
    element?.scrollIntoView?.({ behavior: 'smooth', block: 'nearest' })
  }

  const activateCopilotAction = async (actionId, symbolKey = currentStockKey.value) => {
    const workspace = getWorkspace(symbolKey)
    const turn = buildEffectiveCopilotTurn(workspace)
    if (!workspace || !turn) {
      return
    }

    const action = (turn.followUpActions ?? turn.FollowUpActions ?? []).find(item => (item.actionId ?? item.ActionId) === actionId)
    if (!action) {
      return
    }

    if (!(action.enabled ?? action.Enabled)) {
      workspace.copilotError = action.blockedReason ?? action.BlockedReason ?? '该动作当前不可执行。'
      return
    }

    const actionType = action.actionType ?? action.ActionType ?? ''
    const toolName = action.toolName ?? action.ToolName ?? ''
    const shouldExecuteApprovedTool = Boolean(toolName)

    if (actionType === 'draft_trading_plan') {
      await focusCopilotWorkspaceSection(symbolKey, 'plan')
      if (!getSelectedCommanderHistoryAvailability(workspace).ready) {
        if (workspace.agentLoading) {
          workspace.copilotError = '多Agent 分析进行中，请等待 commander 完整结果返回。'
          return
        }
        await runAgents(symbolKey)
      }
      await openTradingPlanDraft(symbolKey)
      return
    }

    const toolCall = getCopilotApprovedToolCalls(turn).find(item => (item.toolName ?? item.ToolName) === toolName)
    if (toolCall && shouldExecuteApprovedTool) {
      await executeCopilotToolCall(toolCall.callId ?? toolCall.CallId ?? '', symbolKey)
    }

    if (actionType === 'inspect_chart') {
      deps.interval.value = parseCopilotInputSummary(toolCall?.inputSummary ?? toolCall?.InputSummary ?? '').interval || 'day'
      await focusCopilotWorkspaceSection(symbolKey, 'chart', { chartView: deps.interval.value })
      await refreshChartData(symbolKey)
      return
    }

    if (actionType === 'inspect_intraday') {
      deps.interval.value = 'day'
      await focusCopilotWorkspaceSection(symbolKey, 'chart', { chartView: 'minute' })
      await refreshChartData(symbolKey)
      return
    }

    if (actionType === 'inspect_strategy') {
      await focusCopilotWorkspaceSection(symbolKey, 'strategy')
      if (!workspace.agentResults.length && !workspace.agentLoading) {
        await runAgents(symbolKey)
      } else if (!workspace.agentHistoryLoaded && !workspace.agentHistoryLoading) {
        await fetchAgentHistory(symbolKey, { force: true })
      }
      return
    }

    if (actionType === 'inspect_news') {
      await focusCopilotWorkspaceSection(symbolKey, 'news')
      fetchNewsImpact(symbolKey, { force: true })
      fetchLocalNews(symbolKey, { force: true })
    }
  }

  const setChatRef = symbolKey => instance => {
    if (!symbolKey) {
      return
    }
    if (instance) {
      chatWindowRefs.set(symbolKey, instance)
      return
    }
    chatWindowRefs.delete(symbolKey)
  }

  const createChatSession = async (symbolKey = chatSymbolKey.value) => {
    const workspace = getWorkspace(symbolKey)
    if (!workspace || !symbolKey) return
    const timestamp = new Date()
    const label = `${timestamp.getFullYear()}-${String(timestamp.getMonth() + 1).padStart(2, '0')}-${String(
      timestamp.getDate()
    ).padStart(2, '0')} ${String(timestamp.getHours()).padStart(2, '0')}:${String(timestamp.getMinutes()).padStart(2, '0')}`
    const response = await fetch('/api/stocks/chat/sessions', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ symbol: symbolKey, title: label })
    })
    if (!response.ok) {
      throw new Error('创建会话失败')
    }
    const session = await response.json()
    const entry = { key: session.sessionKey ?? session.SessionKey, label: session.title ?? session.Title }
    workspace.chatSessions = [entry, ...workspace.chatSessions]
    workspace.selectedChatSession = entry.key
    workspace.chatSessionsLoaded = true
  }

  const fetchChatSessions = async (symbolKey = chatSymbolKey.value, options = {}) => {
    const workspace = getWorkspace(symbolKey)
    if (!workspace || !symbolKey) {
      return
    }
    const force = Boolean(options.force)
    if (!force && (workspace.chatSessionsLoaded || workspace.chatSessionsLoading)) {
      return
    }

    const requestToken = ++workspace.chatSessionsRequestToken
    const controller = replaceAbortController(workspace.chatSessionsAbortController)
    workspace.chatSessionsAbortController = controller
    workspace.chatSessionsLoading = true
    workspace.chatSessionsError = ''
    try {
      const params = new URLSearchParams({ symbol: symbolKey })
      const response = await fetchBackendGet(`/api/stocks/chat/sessions?${params.toString()}`, { signal: controller.signal })
      if (!response.ok) {
        throw new Error('聊天历史加载失败')
      }
      const list = await response.json()
      if (requestToken !== workspace.chatSessionsRequestToken) {
        return
      }
      workspace.chatSessions = Array.isArray(list) ? list.map(item => ({
        key: item.sessionKey ?? item.SessionKey,
        label: item.title ?? item.Title
      })) : []
      if (!workspace.chatSessions.length) {
        await createChatSession(symbolKey)
        workspace.chatSessionsLoaded = true
        return
      }
      if (!workspace.chatSessions.some(item => item.key === workspace.selectedChatSession)) {
        workspace.selectedChatSession = workspace.chatSessions[0]?.key || ''
      }
      workspace.chatSessionsLoaded = true
    } catch (err) {
      if (isAbortError(err)) {
        return
      }
      workspace.chatSessionsError = err.message || '聊天历史加载失败'
      workspace.chatSessions = []
      workspace.chatSessionsLoaded = false
    } finally {
      if (requestToken === workspace.chatSessionsRequestToken) {
        workspace.chatSessionsLoading = false
        if (workspace.chatSessionsAbortController === controller) {
          workspace.chatSessionsAbortController = null
        }
      }
    }
  }

  const startNewChat = async (symbolKey = currentStockKey.value) => {
    try {
      await createChatSession(symbolKey)
    } catch (err) {
      const workspace = getWorkspace(symbolKey)
      if (workspace) {
        workspace.chatSessionsError = err.message || '创建会话失败'
      }
      return
    }
    await nextTick()
    chatWindowRefs.get(symbolKey)?.createNewChat()
  }

  const chatHistoryAdapter = {
    load: async key => {
      if (!key) return []
      const response = await fetch(`/api/stocks/chat/sessions/${encodeURIComponent(key)}/messages`)
      if (!response.ok) return []
      const list = await response.json()
      if (!Array.isArray(list)) return []
      return list.map(item => ({
        role: item.role ?? item.Role,
        content: item.content ?? item.Content,
        timestamp: item.timestamp ?? item.Timestamp
      }))
    },
    save: async (key, messages) => {
      if (!key) return
      await fetch(`/api/stocks/chat/sessions/${encodeURIComponent(key)}/messages`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ messages })
      })
    }
  }

  return {
    chatHistoryAdapter,
    currentCopilotTurn,
    executeCopilotToolCall,
    fetchChatSessions,
    fetchCopilotAcceptanceBaseline,
    selectCopilotReplayTurn,
    setChatRef,
    startNewChat,
    submitCopilotDraft,
    activateCopilotAction
  }
}