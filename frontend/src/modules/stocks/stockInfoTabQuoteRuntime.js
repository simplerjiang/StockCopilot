export const createStockInfoTabQuoteRuntime = deps => {
  const {
    applyFundamentalSnapshot,
    appendRecordedHistory,
    currentStockKey,
    fetchBackendGet,
    fetchStockRealtimeOverview,
    getSelectedSource,
    getWorkspace,
    historySaveFailed,
    isAbortError,
    isDirectStockSymbol,
    normalizeStockSymbol,
    replaceAbortController,
    searchStocks,
    selectedSymbol,
    setStockLoadStage,
    resetStockLoadStages,
    symbol,
    buildDetailQuery,
    buildChartQuery,
    applyLatestDetail,
    applyLatestMessages,
    error
  } = deps

  const fetchQuote = async () => {
    const query = symbol.value.trim()
    if (!query) {
      error.value = '请输入股票代码'
      return
    }

    const normalizedQuery = normalizeStockSymbol(query)
    const targetSymbol = normalizeStockSymbol(selectedSymbol.value || normalizedQuery)

    if (!isDirectStockSymbol(targetSymbol)) {
      searchStocks(query)
      return
    }

    selectedSymbol.value = targetSymbol
    symbol.value = targetSymbol
    currentStockKey.value = targetSymbol

    const workspace = getWorkspace(targetSymbol)
    if (!workspace) {
      return
    }

    const requestToken = ++workspace.quoteRequestToken
    const controller = replaceAbortController(workspace.detailAbortController)
    workspace.detailAbortController = controller
    workspace.loading = true
    workspace.error = ''
    workspace.pendingFundamentalSnapshot = undefined
    resetStockLoadStages(workspace)
    setStockLoadStage(workspace, requestToken, 'cache', 'pending')
    setStockLoadStage(workspace, requestToken, 'detail', 'pending')
    setStockLoadStage(workspace, requestToken, 'tencent', 'pending')
    setStockLoadStage(workspace, requestToken, 'eastmoney', 'pending')

    const tencentQuoteProgressPromise = (async () => {
      try {
        const params = new URLSearchParams({ symbol: targetSymbol, source: '腾讯' })
        const response = await fetchBackendGet(`/api/stocks/quote?${params.toString()}`, { signal: controller.signal })
        if (!response.ok) {
          throw new Error('腾讯接口请求失败')
        }
        await response.json()
        setStockLoadStage(workspace, requestToken, 'tencent', 'success')
      } catch (err) {
        if (isAbortError(err)) {
          return
        }
        setStockLoadStage(workspace, requestToken, 'tencent', 'error', err.message || '腾讯接口请求失败')
      }
    })()

    const fundamentalSnapshotPromise = (async () => {
      try {
        const params = new URLSearchParams({ symbol: targetSymbol })
        const response = await fetchBackendGet(`/api/stocks/fundamental-snapshot?${params.toString()}`, { signal: controller.signal })
        if (response.status === 404) {
          applyFundamentalSnapshot(workspace, requestToken, null)
          setStockLoadStage(workspace, requestToken, 'eastmoney', 'success', '东财暂未返回可用基本面')
          return
        }
        if (!response.ok) {
          throw new Error('东财接口请求失败')
        }
        const snapshot = await response.json()
        applyFundamentalSnapshot(workspace, requestToken, snapshot)
        const facts = snapshot?.facts ?? snapshot?.Facts
        const factCount = Array.isArray(facts) ? facts.length : 0
        setStockLoadStage(
          workspace,
          requestToken,
          'eastmoney',
          'success',
          factCount ? `东财已返回 ${factCount} 条基本面字段` : '东财基本面已返回'
        )
      } catch (err) {
        if (isAbortError(err)) {
          return
        }
        setStockLoadStage(workspace, requestToken, 'eastmoney', 'error', err.message || '东财接口请求失败')
      }
    })()

    try {
      const stockRealtimePromise = fetchStockRealtimeOverview(targetSymbol, { force: true })
      const params = buildDetailQuery(targetSymbol)
      const chartParams = buildChartQuery(targetSymbol, {
        includeQuote: !workspace.detail?.quote
      })

      const cachePromise = (async () => {
        try {
          const cacheResponse = await fetchBackendGet(`/api/stocks/detail/cache?${params.toString()}`, { signal: controller.signal })
          if (!cacheResponse.ok) {
            setStockLoadStage(workspace, requestToken, 'cache', 'success', '未命中缓存，继续实时加载')
            return
          }

          const cacheDetail = await cacheResponse.json()
          if (workspace.sourceLoadStages?.detail?.status !== 'success') {
            applyLatestDetail(workspace, requestToken, cacheDetail)
            setStockLoadStage(workspace, requestToken, 'cache', 'success', '已显示缓存快照')
            return
          }

          setStockLoadStage(workspace, requestToken, 'cache', 'success', '缓存返回较晚，已忽略')
        } catch (err) {
          if (isAbortError(err)) {
            return
          }
          setStockLoadStage(workspace, requestToken, 'cache', 'error', err.message || '缓存读取失败')
        }
      })()

      const liveChartPromise = (async () => {
        const response = await fetchBackendGet(`/api/stocks/chart?${chartParams.toString()}`, { signal: controller.signal })
        if (!response.ok) {
          throw new Error('接口请求失败')
        }

        return response.json()
      })()

      const liveMessagesPromise = (async () => {
        try {
          const messageParams = new URLSearchParams({ symbol: targetSymbol })
          const selectedSourceValue = getSelectedSource()
          if (selectedSourceValue) {
            messageParams.set('source', selectedSourceValue)
          }
          const response = await fetchBackendGet(`/api/stocks/messages?${messageParams.toString()}`, { signal: controller.signal })
          if (!response.ok) {
            throw new Error('盘中消息加载失败')
          }

          const messages = await response.json()
          applyLatestMessages(workspace, requestToken, messages)
        } catch (err) {
          if (isAbortError(err)) {
            return
          }
        }
      })()

      const liveChart = await liveChartPromise
      applyLatestDetail(workspace, requestToken, liveChart)
      const kLines = liveChart?.kLines ?? liveChart?.KLines
      const minuteLines = liveChart?.minuteLines ?? liveChart?.MinuteLines
      const quote = liveChart?.quote ?? liveChart?.Quote
      const klineCount = Array.isArray(kLines) ? kLines.length : 0
      const minuteCount = Array.isArray(minuteLines) ? minuteLines.length : 0
      setStockLoadStage(
        workspace,
        requestToken,
        'detail',
        'success',
        `已返回 ${klineCount} 根K线 / ${minuteCount} 条分时`
      )

      if (requestToken === workspace.quoteRequestToken) {
        workspace.loading = false
        if (workspace.detailAbortController === controller) {
          workspace.detailAbortController = null
        }
      }

      if (requestToken === workspace.quoteRequestToken && quote) {
        try {
          await appendRecordedHistory(quote)
        } catch (err) {
          historySaveFailed(err)
        }
      }

      await Promise.allSettled([
        cachePromise,
        tencentQuoteProgressPromise,
        fundamentalSnapshotPromise,
        stockRealtimePromise,
        liveMessagesPromise
      ])
    } catch (err) {
      if (isAbortError(err)) {
        return
      }
      if (requestToken === workspace.quoteRequestToken) {
        workspace.error = err.message || '请求失败'
        setStockLoadStage(workspace, requestToken, 'detail', 'error', err.message || '图表数据请求失败')
      }
    } finally {
      if (requestToken === workspace.quoteRequestToken && workspace.loading) {
        workspace.loading = false
        if (workspace.detailAbortController === controller) {
          workspace.detailAbortController = null
        }
      }
    }
  }

  const refreshChartData = async (symbolKey = currentStockKey.value) => {
    const workspace = getWorkspace(symbolKey)
    const targetSymbol = normalizeStockSymbol(symbolKey || workspace?.detail?.quote?.symbol)
    if (!workspace || !targetSymbol || !workspace.detail) {
      return
    }

    const requestToken = ++workspace.quoteRequestToken
    const controller = replaceAbortController(workspace.detailAbortController)
    workspace.detailAbortController = controller
    workspace.loading = true
    workspace.error = ''
    resetStockLoadStages(workspace)
    setStockLoadStage(workspace, requestToken, 'cache', 'success', '沿用当前详情')
    setStockLoadStage(workspace, requestToken, 'detail', 'pending')
    const chartParams = buildChartQuery(targetSymbol, {
      includeQuote: false
    })

    try {
      const chartResponse = await fetchBackendGet(`/api/stocks/chart?${chartParams.toString()}`, { signal: controller.signal })
      if (!chartResponse.ok) {
        throw new Error('图表数据请求失败')
      }

      const chartPayload = await chartResponse.json()
      applyLatestDetail(workspace, requestToken, chartPayload)
      const kLines = chartPayload?.kLines ?? chartPayload?.KLines
      const minuteLines = chartPayload?.minuteLines ?? chartPayload?.MinuteLines
      const klineCount = Array.isArray(kLines) ? kLines.length : 0
      const minuteCount = Array.isArray(minuteLines) ? minuteLines.length : 0
      setStockLoadStage(workspace, requestToken, 'detail', 'success', `已返回 ${klineCount} 根K线 / ${minuteCount} 条分时`)

      if (requestToken === workspace.quoteRequestToken) {
        workspace.loading = false
        if (workspace.detailAbortController === controller) {
          workspace.detailAbortController = null
        }
      }
    } catch (err) {
      if (isAbortError(err)) {
        return
      }
      if (requestToken === workspace.quoteRequestToken) {
        workspace.error = err.message || '图表数据请求失败'
        setStockLoadStage(workspace, requestToken, 'detail', 'error', err.message || '图表数据请求失败')
      }
    } finally {
      if (requestToken === workspace.quoteRequestToken && workspace.loading) {
        workspace.loading = false
        if (workspace.detailAbortController === controller) {
          workspace.detailAbortController = null
        }
      }
    }
  }

  return {
    fetchQuote,
    refreshChartData
  }
}