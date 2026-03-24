export const createStockInfoTabAgentRuntime = deps => {
  const {
    chatSymbolKey,
    currentStockKey,
    currentWorkspace,
    formatDate,
    getWorkspace,
    interval,
    inspectCommanderHistoryAgentResults,
    replaceAbortController,
    saveAgentHistoryRequest,
    fetchBackendGet,
    isAbortError,
    selectedSource,
    upsertAgentResult
  } = deps

  const fetchAgentHistory = async (symbolKey = chatSymbolKey.value, options = {}) => {
    const workspace = getWorkspace(symbolKey)
    if (!workspace || !symbolKey) {
      return
    }
    const force = Boolean(options.force)
    if (!force && (workspace.agentHistoryLoaded || workspace.agentHistoryLoading)) {
      return
    }
    const requestToken = ++workspace.agentHistoryRequestToken
    const controller = replaceAbortController(workspace.agentHistoryAbortController)
    workspace.agentHistoryAbortController = controller
    workspace.agentHistoryLoading = true
    workspace.agentHistoryError = ''
    try {
      const params = new URLSearchParams({ symbol: symbolKey })
      const response = await fetchBackendGet(`/api/stocks/agents/history?${params.toString()}`, { signal: controller.signal })
      if (!response.ok) {
        throw new Error('多Agent历史加载失败')
      }
      const list = await response.json()
      if (requestToken !== workspace.agentHistoryRequestToken) {
        return
      }
      workspace.agentHistoryList = Array.isArray(list) ? list : []
      workspace.agentHistoryLoaded = true
    } catch (err) {
      if (isAbortError(err)) {
        return
      }
      workspace.agentHistoryError = err.message || '多Agent历史加载失败'
      workspace.agentHistoryList = []
      workspace.agentHistoryLoaded = false
    } finally {
      if (requestToken === workspace.agentHistoryRequestToken) {
        workspace.agentHistoryLoading = false
        if (workspace.agentHistoryAbortController === controller) {
          workspace.agentHistoryAbortController = null
        }
      }
    }
  }

  const loadAgentHistoryDetail = async (historyId, symbolKey = currentStockKey.value) => {
    if (!historyId) return
    const workspace = getWorkspace(symbolKey)
    if (!workspace) return
    workspace.agentHistoryLoading = true
    workspace.agentHistoryError = ''
    try {
      const response = await fetch(`/api/stocks/agents/history/${historyId}`)
      if (!response.ok) {
        throw new Error('历史详情加载失败')
      }
      const historyDetail = await response.json()
      const result = historyDetail.result ?? historyDetail.Result
      workspace.agentResults = result?.agents ?? result?.Agents ?? []
      workspace.agentUpdatedAt = formatDate(historyDetail.createdAt ?? historyDetail.CreatedAt)
    } catch (err) {
      workspace.agentHistoryError = err.message || '历史详情加载失败'
    } finally {
      workspace.agentHistoryLoading = false
    }
  }

  const saveAgentHistory = async (symbolKey = currentStockKey.value) => {
    const workspace = getWorkspace(symbolKey) ?? currentWorkspace.value
    if (!workspace?.detail?.quote?.symbol) return
    const saved = await saveAgentHistoryRequest({
      symbol: workspace.detail.quote.symbol,
      name: workspace.detail.quote.name,
      interval: interval.value,
      source: selectedSource.value || null,
      provider: null,
      model: null,
      useInternet: true,
      result: {
        symbol: workspace.detail.quote.symbol,
        name: workspace.detail.quote.name,
        timestamp: new Date().toISOString(),
        agents: workspace.agentResults
      }
    })
    const savedId = saved.id ?? saved.Id ?? ''
    workspace.selectedAgentHistoryId = savedId
    if (savedId) {
      const historyList = Array.isArray(workspace.agentHistoryList) ? workspace.agentHistoryList : []
      workspace.agentHistoryList = [
        saved,
        ...historyList.filter(item => String(item?.id ?? item?.Id ?? '') !== String(savedId))
      ]
    }
    workspace.agentHistoryLoaded = false
  }

  const runAgents = async (symbolKey = currentStockKey.value, isPro = false) => {
    const workspace = getWorkspace(symbolKey) ?? currentWorkspace.value
    if (!workspace?.detail?.quote?.symbol) {
      if (workspace) {
        workspace.agentError = '请先选择股票'
      }
      return
    }

    workspace.agentLoading = true
    workspace.agentError = ''
    try {
      workspace.agentResults = []
      workspace.selectedAgentHistoryId = ''
      const order = ['stock_news', 'sector_news', 'financial_analysis', 'trend_analysis', 'commander']
      for (const agentId of order) {
        const payload = {
          symbol: workspace.detail.quote.symbol,
          agentId,
          interval: interval.value,
          count: workspace.detail.kLines?.length || 60,
          source: selectedSource.value || null,
          provider: null,
          useInternet: true,
          dependencyResults: agentId === 'commander' ? workspace.agentResults : [],
          isPro
        }

        try {
          const response = await fetch('/api/stocks/agents/single', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
          })
          if (!response.ok) {
            const message = await response.text()
            throw new Error(message || `${agentId} 请求失败`)
          }
          const result = await response.json()
          if (currentWorkspace.value?.symbolKey === workspace.symbolKey) {
            upsertAgentResult(result)
          } else {
            const agentIdValue = result?.agentId ?? result?.AgentId ?? ''
            const list = [...workspace.agentResults]
            const index = list.findIndex(item => (item.agentId ?? item.AgentId) === agentIdValue)
            if (index >= 0) {
              list[index] = result
            } else {
              list.push(result)
            }
            workspace.agentResults = list
          }
        } catch (err) {
          const failedResult = {
            agentId,
            agentName: agentId,
            success: false,
            error: err.message || `${agentId} 请求失败`,
            data: null,
            rawContent: null
          }
          if (currentWorkspace.value?.symbolKey === workspace.symbolKey) {
            upsertAgentResult(failedResult)
          } else {
            workspace.agentResults = [...workspace.agentResults, failedResult]
          }
        }

        workspace.agentUpdatedAt = formatDate(new Date().toISOString())
      }

      try {
        const historyAvailability = inspectCommanderHistoryAgentResults(workspace.agentResults)
        if (!historyAvailability.isComplete) {
          workspace.selectedAgentHistoryId = ''
          workspace.agentHistoryError = historyAvailability.blockedReason
          return
        }

        await saveAgentHistory(workspace.symbolKey)
        await fetchAgentHistory(workspace.symbolKey, { force: true })
      } catch (err) {
        workspace.agentHistoryError = err.message || '保存多Agent历史失败'
      }
    } catch (err) {
      workspace.agentError = err.message || '多Agent请求失败'
    } finally {
      workspace.agentLoading = false
    }
  }

  return {
    fetchAgentHistory,
    loadAgentHistoryDetail,
    runAgents,
    saveAgentHistory
  }
}