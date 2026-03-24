export const createStockInfoTabDataRequests = deps => {
  const {
    buildRealtimeContextSymbols,
    currentStockKey,
    detail,
    DOMESTIC_REALTIME_CONTEXT_SYMBOLS,
    fetchBackendGet,
    getStockRealtimeAbortController,
    getWorkspace,
    GLOBAL_REALTIME_CONTEXT_SYMBOLS,
    historyError,
    historyList,
    historyLoading,
    isAbortError,
    normalizeNewsBucket,
    normalizeRealtimeOverview,
    normalizeStockSymbol,
    normalizeTradingPlan,
    normalizeTradingPlanAlert,
    parseResponseMessage,
    replaceAbortController,
    rootWorkspace,
    selectedSource,
    setStockRealtimeAbortController,
    sidebarNewsSections,
    sources,
    stockRealtimeError,
    stockRealtimeLoading,
    stockRealtimeOverview,
    stockRealtimeOverviewEnabled,
    stockRealtimeSymbol
  } = deps

  const fetchTradingPlans = async (symbolKey = currentStockKey.value, options = {}) => {
    const isBoard = Boolean(options.global)
    const workspace = isBoard ? rootWorkspace : getWorkspace(symbolKey)
    const symbolValue = isBoard ? '' : (workspace?.detail?.quote?.symbol ?? symbolKey)
    if (!workspace || (!isBoard && !symbolValue)) {
      return
    }

    const force = Boolean(options.force)
    if (!force && (workspace.planListLoaded || workspace.planListLoading)) {
      return
    }

    const requestToken = ++workspace.planListRequestToken
    const controller = replaceAbortController(workspace.planListAbortController)
    workspace.planListAbortController = controller
    workspace.planListLoading = true
    workspace.planError = ''
    try {
      const params = new URLSearchParams()
      if (symbolValue) {
        params.set('symbol', symbolValue)
      }
      if (options.take) {
        params.set('take', String(options.take))
      }
      const response = await fetchBackendGet(`/api/stocks/plans?${params.toString()}`, { signal: controller.signal })
      if (!response.ok) {
        throw new Error(await parseResponseMessage(response, '交易计划加载失败'))
      }
      const list = await response.json()
      if (requestToken !== workspace.planListRequestToken) {
        return
      }
      workspace.planList = Array.isArray(list) ? list.map(normalizeTradingPlan) : []
      workspace.planListLoaded = true
    } catch (err) {
      if (isAbortError(err)) {
        return
      }
      workspace.planError = err.message || '交易计划加载失败'
      workspace.planList = []
      workspace.planListLoaded = false
    } finally {
      if (requestToken === workspace.planListRequestToken) {
        workspace.planListLoading = false
        if (workspace.planListAbortController === controller) {
          workspace.planListAbortController = null
        }
      }
    }
  }

  const fetchTradingPlanAlerts = async (symbolKey = currentStockKey.value, options = {}) => {
    const isBoard = Boolean(options.global)
    const workspace = isBoard ? rootWorkspace : getWorkspace(symbolKey)
    const symbolValue = isBoard ? '' : (workspace?.detail?.quote?.symbol ?? symbolKey)
    if (!workspace || (!isBoard && !symbolValue)) {
      return
    }

    const force = Boolean(options.force)
    if (!force && (workspace.planAlertsLoaded || workspace.planAlertsLoading)) {
      return
    }

    const requestToken = ++workspace.planAlertsRequestToken
    const controller = replaceAbortController(workspace.planAlertsAbortController)
    workspace.planAlertsAbortController = controller
    workspace.planAlertsLoading = true
    try {
      const params = new URLSearchParams()
      if (symbolValue) {
        params.set('symbol', symbolValue)
      }
      if (options.planId) {
        params.set('planId', String(options.planId))
      }
      if (options.take) {
        params.set('take', String(options.take))
      }
      const response = await fetchBackendGet(`/api/stocks/plans/alerts?${params.toString()}`, { signal: controller.signal })
      if (!response.ok) {
        throw new Error(await parseResponseMessage(response, '交易计划告警加载失败'))
      }
      const list = await response.json()
      if (requestToken !== workspace.planAlertsRequestToken) {
        return
      }
      workspace.planAlerts = Array.isArray(list) ? list.map(normalizeTradingPlanAlert) : []
      workspace.planAlertsLoaded = true
    } catch (err) {
      if (isAbortError(err)) {
        return
      }
      workspace.planError = err.message || '交易计划告警加载失败'
      workspace.planAlerts = []
      workspace.planAlertsLoaded = false
    } finally {
      if (requestToken === workspace.planAlertsRequestToken) {
        workspace.planAlertsLoading = false
        if (workspace.planAlertsAbortController === controller) {
          workspace.planAlertsAbortController = null
        }
      }
    }
  }

  const refreshTradingPlanBoard = async (force = false) => {
    await fetchTradingPlans('', { force, global: true, take: 20 })
    await fetchTradingPlanAlerts('', { force, global: true, take: 20 })
  }

  const refreshTradingPlanSection = async (symbolKey = currentStockKey.value, force = false) => {
    await fetchTradingPlans(symbolKey, { force })
    await fetchTradingPlanAlerts(symbolKey, { force, take: 20 })
  }

  const fetchNewsImpact = async (symbolKey = currentStockKey.value, options = {}) => {
    const workspace = getWorkspace(symbolKey)
    const symbolValue = workspace?.detail?.quote?.symbol
    if (!workspace || !symbolValue) {
      return
    }
    const force = Boolean(options.force)
    if (!force && (workspace.newsImpactLoaded || workspace.newsImpactLoading)) {
      return
    }

    const requestToken = ++workspace.newsImpactRequestToken
    const controller = replaceAbortController(workspace.newsImpactAbortController)
    workspace.newsImpactAbortController = controller
    workspace.newsImpactLoading = true
    workspace.newsImpactError = ''
    try {
      const params = new URLSearchParams({ symbol: symbolValue })
      if (selectedSource.value) {
        params.set('source', selectedSource.value)
      }
      const response = await fetchBackendGet(`/api/stocks/news/impact?${params.toString()}`, { signal: controller.signal })
      if (!response.ok) {
        throw new Error('资讯影响加载失败')
      }
      const payload = await response.json()
      if (requestToken !== workspace.newsImpactRequestToken) {
        return
      }
      workspace.newsImpact = payload
      workspace.newsImpactLoaded = true
    } catch (err) {
      if (isAbortError(err)) {
        return
      }
      workspace.newsImpactError = err.message || '资讯影响加载失败'
      workspace.newsImpact = null
      workspace.newsImpactLoaded = false
    } finally {
      if (requestToken === workspace.newsImpactRequestToken) {
        workspace.newsImpactLoading = false
        if (workspace.newsImpactAbortController === controller) {
          workspace.newsImpactAbortController = null
        }
      }
    }
  }

  const fetchMarketNews = async (options = {}) => {
    const force = Boolean(options.force)
    if (!force && (rootWorkspace.localNewsBuckets.market || rootWorkspace.localNewsLoading)) {
      return
    }
    const requestToken = ++rootWorkspace.localNewsRequestToken
    const controller = replaceAbortController(rootWorkspace.localNewsAbortController)
    rootWorkspace.localNewsAbortController = controller
    rootWorkspace.localNewsLoading = true
    rootWorkspace.localNewsError = ''

    try {
      const response = await fetchBackendGet('/api/news?level=market', { signal: controller.signal })
      if (!response.ok) {
        throw new Error('大盘资讯加载失败')
      }

      const payload = await response.json()
      if (requestToken !== rootWorkspace.localNewsRequestToken) {
        return
      }

      rootWorkspace.localNewsBuckets = {
        ...rootWorkspace.localNewsBuckets,
        market: normalizeNewsBucket('market', payload)
      }
    } catch (err) {
      if (isAbortError(err)) {
        return
      }
      rootWorkspace.localNewsError = err.message || '大盘资讯加载失败'
    } finally {
      if (requestToken === rootWorkspace.localNewsRequestToken) {
        rootWorkspace.localNewsLoading = false
        if (rootWorkspace.localNewsAbortController === controller) {
          rootWorkspace.localNewsAbortController = null
        }
      }
    }
  }

  const fetchStockRealtimeOverview = async (symbolKey = currentStockKey.value, options = {}) => {
    if (typeof fetch !== 'function') {
      return
    }

    const normalizedSymbol = normalizeStockSymbol(symbolKey || detail.value?.quote?.symbol)
    const effectiveSymbol = normalizedSymbol || ''

    const force = Boolean(options.force)
    if (!stockRealtimeOverviewEnabled.value && !force) {
      return
    }

    if (!force && stockRealtimeLoading.value) {
      return
    }

    if (!force && stockRealtimeOverview.value && stockRealtimeSymbol.value === effectiveSymbol) {
      return
    }

    const controller = replaceAbortController(getStockRealtimeAbortController())
    setStockRealtimeAbortController(controller)
    stockRealtimeLoading.value = true
    stockRealtimeError.value = ''

    try {
      const params = new URLSearchParams({
        symbols: buildRealtimeContextSymbols(
          effectiveSymbol,
          DOMESTIC_REALTIME_CONTEXT_SYMBOLS,
          GLOBAL_REALTIME_CONTEXT_SYMBOLS
        ).join(',')
      })
      const response = await fetchBackendGet(`/api/market/realtime/overview?${params.toString()}`, { signal: controller.signal })
      if (!response.ok) {
        throw new Error('市场实时总览加载失败')
      }

      const payload = await response.json()
      stockRealtimeOverview.value = normalizeRealtimeOverview(payload)
      stockRealtimeSymbol.value = effectiveSymbol
    } catch (err) {
      if (isAbortError(err)) {
        return
      }
      stockRealtimeError.value = err.message || '市场实时总览加载失败'
    } finally {
      if (getStockRealtimeAbortController() === controller) {
        setStockRealtimeAbortController(null)
      }
      stockRealtimeLoading.value = false
    }
  }

  const fetchLocalNews = async (symbolKey = currentStockKey.value, options = {}) => {
    const workspace = symbolKey ? getWorkspace(symbolKey) : null
    const symbolValue = workspace?.detail?.quote?.symbol
    if (!workspace || !symbolValue) {
      return
    }
    const force = Boolean(options.force)
    if (!force && (workspace.localNewsLoaded || workspace.localNewsLoading)) {
      return
    }

    const requestToken = ++workspace.localNewsRequestToken
    const controller = replaceAbortController(workspace.localNewsAbortController)
    workspace.localNewsAbortController = controller
    workspace.localNewsLoading = true
    workspace.localNewsError = ''
    try {
      const buckets = await Promise.all(
        sidebarNewsSections.map(async section => {
          const params = new URLSearchParams({ level: section.key })
          params.set('symbol', symbolValue)
          const response = await fetchBackendGet(`/api/news?${params.toString()}`, { signal: controller.signal })
          if (!response.ok) {
            throw new Error('本地新闻加载失败')
          }
          const payload = await response.json()
          return [section.key, normalizeNewsBucket(section.key, payload)]
        })
      )

      if (requestToken !== workspace.localNewsRequestToken) {
        return
      }

      const bucketMap = Object.fromEntries(buckets)
      workspace.localNewsBuckets = {
        ...workspace.localNewsBuckets,
        stock: bucketMap.stock ?? null,
        sector: bucketMap.sector ?? null
      }
      workspace.localNewsLoaded = true
    } catch (err) {
      if (isAbortError(err)) {
        return
      }
      workspace.localNewsError = err.message || '本地新闻加载失败'
      workspace.localNewsBuckets = {
        ...workspace.localNewsBuckets,
        stock: null,
        sector: null
      }
      workspace.localNewsLoaded = false
    } finally {
      if (requestToken === workspace.localNewsRequestToken) {
        workspace.localNewsLoading = false
        if (workspace.localNewsAbortController === controller) {
          workspace.localNewsAbortController = null
        }
      }
    }
  }

  const fetchSources = async () => {
    try {
      const response = await fetchBackendGet('/api/stocks/sources')
      if (response.ok) {
        sources.value = await response.json()
      }
    } catch {
      // 忽略来源加载失败
    }
  }

  const fetchHistory = async () => {
    historyLoading.value = true
    historyError.value = ''
    try {
      const response = await fetchBackendGet('/api/stocks/history')
      if (!response.ok) {
        throw new Error('历史记录请求失败')
      }
      historyList.value = await response.json()
    } catch (err) {
      historyError.value = err.message || '历史记录请求失败'
    } finally {
      historyLoading.value = false
    }
  }

  const refreshHistory = async () => {
    historyLoading.value = true
    historyError.value = ''
    try {
      const params = new URLSearchParams()
      if (selectedSource.value) {
        params.set('source', selectedSource.value)
      }
      const url = params.toString() ? `/api/stocks/history/refresh?${params.toString()}` : '/api/stocks/history/refresh'
      const response = await fetch(url, { method: 'POST' })
      if (!response.ok) {
        throw new Error('历史记录刷新失败')
      }
      historyList.value = await response.json()
    } catch (err) {
      historyError.value = err.message || '历史记录刷新失败'
    } finally {
      historyLoading.value = false
    }
  }

  return {
    fetchHistory,
    fetchLocalNews,
    fetchMarketNews,
    fetchNewsImpact,
    fetchSources,
    fetchStockRealtimeOverview,
    fetchTradingPlanAlerts,
    fetchTradingPlans,
    refreshHistory,
    refreshTradingPlanBoard,
    refreshTradingPlanSection
  }
}