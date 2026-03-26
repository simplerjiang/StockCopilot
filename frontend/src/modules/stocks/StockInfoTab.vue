<script setup>
import { computed, nextTick, onMounted, onUnmounted, reactive, ref, watch } from 'vue'
import StockCharts from './StockCharts.vue'
import StockMarketNewsPanel from './StockMarketNewsPanel.vue'
import StockNewsImpactPanel from './StockNewsImpactPanel.vue'
import StockSearchToolbar from './StockSearchToolbar.vue'
import StockTerminalSummary from './StockTerminalSummary.vue'
import StockTopMarketOverview from './StockTopMarketOverview.vue'
import StockTradingPlanBoard from './StockTradingPlanBoard.vue'
import StockTradingPlanModal from './StockTradingPlanModal.vue'
import StockTradingPlanSection from './StockTradingPlanSection.vue'
import TerminalView from './TerminalView.vue'
import { createStockInfoTabDataRequests } from './stockInfoTabDataRequests'
import { createStockInfoTabQuoteRuntime } from './stockInfoTabQuoteRuntime'
import {
  createStockLoadStages,
  createStockWorkspace,
  DOMESTIC_REALTIME_CONTEXT_SYMBOLS,
  GLOBAL_REALTIME_CONTEXT_SYMBOLS,
  STOCK_LOAD_STAGE_DEFINITIONS
} from './stockInfoTabWorkspace'
import {
  formatDate,
  formatImpactScore,
  formatPlanPrice,
  formatPlanScale,
  formatRealtimeMoney,
  formatSignedNumber,
  formatSignedPercent,
  formatSignedRealtimeAmount,
  getChangeClass,
  getHeadlineNewsImpactEvents,
  getImpactCategoryValue,
  getImpactClass,
  getLocalNewsHeadline,
  isDirectStockSymbol,
  normalizePlanNumber,
  normalizeStockSymbol
} from './stockInfoTabFormatting'
import {
  fetchBackendGet,
  isAbortError,
  parseResponseMessage,
  replaceAbortController
} from './stockInfoTabRequestUtils'
import {
  buildRealtimeContextSymbols,
  canEditTradingPlan,
  canResumeTradingPlan,
  createTradingPlanForm,
  formatPlanAlertSummary,
  getLatestPlanAlert,
  getPlanAlertClass,
  getPlanReviewHeadline,
  getPlanReviewText,
  normalizeMarketContext,
  normalizeTradingPlan,
  normalizeTradingPlanAlert
} from './stockInfoTabTradingPlans'
import {
  buildStockContext,
  formatPercent,
  getHighClass,
  getLowClass,
  getPriceClass,
  getSortValue,
  normalizeNewsBucket,
  normalizeOptionalText,
  normalizeRealtimeOverview,
} from './stockInfoTabViewHelpers'
import {
  formatTradingPlanStatus,
  getTradingPlanStatusClass,
} from './tradingPlanReview'

const symbol = ref('')
const interval = ref(localStorage.getItem('stock_interval') || 'day')
const refreshSeconds = ref(Number(localStorage.getItem('stock_refresh_seconds') || 30))
const autoRefresh = ref(localStorage.getItem('stock_auto_refresh') === 'true')
const sources = ref([])
const selectedSource = ref(localStorage.getItem('stock_source') || '')
let refreshTimer = null
let planRefreshTimer = null
let minuteTdSequentialRefreshTimer = null
const selectedSymbol = ref('')
const searchResults = ref([])
const searchOpen = ref(false)
const searchLoading = ref(false)
const searchError = ref('')
let searchTimer = null
let isSelecting = false
const historyList = ref([])
const historyLoading = ref(false)
const historyError = ref('')
const historyRefreshSeconds = ref(Number(localStorage.getItem('stock_history_refresh_seconds') || 30))
const historyAutoRefresh = ref(localStorage.getItem('stock_history_auto_refresh') === 'true')
let historyTimer = null
const contextMenu = ref({ visible: false, x: 0, y: 0, item: null })
const sortKey = ref('id')
const sortAsc = ref(true)
const monochromeMode = ref(localStorage.getItem('stock_monochrome_mode') === 'true')
const marketNewsModalOpen = ref(false)
const stockRealtimeOverviewEnabled = ref(localStorage.getItem('stock_realtime_context_enabled') !== 'false')
const stockRealtimeOverview = ref(null)
const stockRealtimeLoading = ref(false)
const stockRealtimeError = ref('')
const stockRealtimeSymbol = ref('')
let stockRealtimeAbortController = null
const chartActiveView = ref('day')
const minuteTdSequentialEnabled = ref(false)

const stockWorkspaces = reactive({})
const rootWorkspace = createStockWorkspace('__root__')
const currentStockKey = ref('')

const getWorkspace = symbolKey => {
  const normalized = normalizeStockSymbol(symbolKey)
  if (!normalized) {
    return null
  }
  if (!stockWorkspaces[normalized]) {
    stockWorkspaces[normalized] = createStockWorkspace(normalized)
  }
  return stockWorkspaces[normalized]
}

const currentWorkspace = computed(() => getWorkspace(currentStockKey.value) ?? rootWorkspace)
const sidebarWorkspaces = computed(() =>
  Object.values(stockWorkspaces).filter(workspace => Boolean(workspace?.detail?.quote?.symbol))
)
const activePlanModalWorkspace = computed(() =>
  sidebarWorkspaces.value.find(workspace => workspace?.planModalOpen && workspace?.planForm)
  ?? (rootWorkspace.planModalOpen && rootWorkspace.planForm ? rootWorkspace : null)
)

const bindWorkspaceField = (key, fallbackValue) => computed({
  get: () => (currentWorkspace.value ? currentWorkspace.value[key] : fallbackValue),
  set: value => {
    if (currentWorkspace.value) {
      currentWorkspace.value[key] = value
    }
  }
})

const loading = bindWorkspaceField('loading', false)
const error = bindWorkspaceField('error', '')
const sourceLoadStages = bindWorkspaceField('sourceLoadStages', createStockLoadStages())
const detail = computed({
  get: () => currentWorkspace.value?.detail ?? null,
  set: value => {
    const symbolKey = normalizeStockSymbol(value?.quote?.symbol)
    if (symbolKey) {
      const workspace = getWorkspace(symbolKey)
      if (!workspace) {
        return
      }
      workspace.detail = value
      currentStockKey.value = symbolKey
      selectedSymbol.value = symbolKey
      return
    }

    if (currentWorkspace.value) {
      currentWorkspace.value.detail = value
    }
  }
})
const chatSessions = bindWorkspaceField('chatSessions', [])
const chatSessionsLoading = bindWorkspaceField('chatSessionsLoading', false)
const chatSessionsError = bindWorkspaceField('chatSessionsError', '')
const selectedChatSession = bindWorkspaceField('selectedChatSession', '')
const newsImpact = bindWorkspaceField('newsImpact', null)
const newsImpactLoading = bindWorkspaceField('newsImpactLoading', false)
const newsImpactError = bindWorkspaceField('newsImpactError', '')
const localNewsBuckets = bindWorkspaceField('localNewsBuckets', { stock: null, sector: null, market: null })
const localNewsLoading = bindWorkspaceField('localNewsLoading', false)
const localNewsError = bindWorkspaceField('localNewsError', '')
const planDraftLoading = bindWorkspaceField('planDraftLoading', false)
const planSaving = bindWorkspaceField('planSaving', false)
const planError = bindWorkspaceField('planError', '')
const planList = bindWorkspaceField('planList', [])
const planAlerts = bindWorkspaceField('planAlerts', [])
const planListLoading = bindWorkspaceField('planListLoading', false)
const planAlertsLoading = bindWorkspaceField('planAlertsLoading', false)
const marketNewsBucket = computed(() => rootWorkspace.localNewsBuckets.market ?? null)
const marketNewsLoading = computed(() => rootWorkspace.localNewsLoading)
const marketNewsError = computed(() => rootWorkspace.localNewsError)
const deletingPlanId = ref('')
const resumingPlanId = ref('')
const stockRealtimeQuotes = computed(() => stockRealtimeOverview.value?.indices ?? [])
const currentStockRealtimeQuote = computed(() => {
  const currentSymbol = normalizeStockSymbol(detail.value?.quote?.symbol)
  if (!currentSymbol) {
    return null
  }

  return stockRealtimeQuotes.value.find(item => normalizeStockSymbol(item.symbol) === currentSymbol) ?? null
})
const stockRealtimeDomesticIndices = computed(() =>
  stockRealtimeQuotes.value.filter(item => DOMESTIC_REALTIME_CONTEXT_SYMBOLS.includes(normalizeStockSymbol(item.symbol)))
)
const stockRealtimeGlobalIndices = computed(() =>
  stockRealtimeQuotes.value.filter(item => GLOBAL_REALTIME_CONTEXT_SYMBOLS.includes(normalizeStockSymbol(item.symbol)))
)
const stockRealtimeRelativeStrength = computed(() => {
  const focusQuote = currentStockRealtimeQuote.value
  const shanghaiQuote = stockRealtimeDomesticIndices.value.find(item => normalizeStockSymbol(item.symbol) === 'sh000001')
  if (!focusQuote || !shanghaiQuote) {
    return null
  }

  const spread = Number(focusQuote.changePercent) - Number(shanghaiQuote.changePercent)
  return {
    spread,
    label: spread >= 0 ? '强于沪指' : '弱于沪指'
  }
})
const stockRealtimeBreadthBias = computed(() => {
  const breadth = stockRealtimeOverview.value?.breadth
  if (!breadth) {
    return null
  }

  const advancers = Number(breadth.advancers ?? 0)
  const decliners = Number(breadth.decliners ?? 0)
  const delta = advancers - decliners
  let label = '多空拉锯'
  if (delta >= 300) {
    label = '普涨扩散'
  } else if (delta <= -300) {
    label = '普跌扩散'
  }

  return {
    delta,
    label
  }
})

const isBlockingQuoteLoad = computed(() => loading.value && !detail.value)
const isBackgroundQuoteRefresh = computed(() => loading.value && !!detail.value)
const visibleSourceLoadStages = computed(() =>
  STOCK_LOAD_STAGE_DEFINITIONS
    .map(stage => sourceLoadStages.value?.[stage.key])
    .filter(stage => stage && stage.status !== 'idle')
)
const showSourceLoadProgress = computed(() => loading.value && visibleSourceLoadStages.value.length > 0)
const sourceLoadProgressPercent = computed(() => {
  const stages = visibleSourceLoadStages.value
  if (!stages.length) {
    return 0
  }

  const progressMap = {
    pending: 0.2,
    success: 1,
    error: 1
  }

  const total = stages.reduce((sum, stage) => sum + (progressMap[stage.status] ?? 0), 0)
  return Math.round((total / stages.length) * 100)
})
const sourceLoadProgressTitle = computed(() => (isBackgroundQuoteRefresh.value ? '后台刷新进度' : '实时加载进度'))

const sidebarNewsSections = [
  { key: 'stock', title: '个股事实' },
  { key: 'sector', title: '板块上下文' }
]

const getStockLoadStageDefinition = key => STOCK_LOAD_STAGE_DEFINITIONS.find(stage => stage.key === key)

const resetStockLoadStages = workspace => {
  if (!workspace?.sourceLoadStages) {
    return
  }

  STOCK_LOAD_STAGE_DEFINITIONS.forEach(stage => {
    const target = workspace.sourceLoadStages[stage.key]
    if (!target) {
      workspace.sourceLoadStages[stage.key] = {
        key: stage.key,
        label: stage.label,
        status: 'idle',
        message: stage.messages.idle
      }
      return
    }

    target.label = stage.label
    target.status = 'idle'
    target.message = stage.messages.idle
  })
}

const setStockLoadStage = (workspace, requestToken, key, status, message = '') => {
  if (!workspace || requestToken !== workspace.quoteRequestToken) {
    return
  }

  const definition = getStockLoadStageDefinition(key)
  const stage = workspace.sourceLoadStages?.[key]
  if (!definition || !stage) {
    return
  }

  stage.label = definition.label
  stage.status = status
  stage.message = message || definition.messages[status] || definition.messages.idle
}

const applyHistorySymbol = item => {
  const rawSymbol = item.symbol || item.Symbol || item.code || item.Code || ''
  const normalizedSymbol = normalizeStockSymbol(rawSymbol)
  selectedSymbol.value = normalizedSymbol
  symbol.value = normalizedSymbol || String(rawSymbol || '').trim()
  currentStockKey.value = normalizedSymbol
  if (symbol.value.trim()) {
    fetchQuote()
  }
}

const buildDetailQuery = targetSymbol => {
  const params = new URLSearchParams({
    symbol: targetSymbol
  })
  if (selectedSource.value) {
    params.set('source', selectedSource.value)
  }
  return params
}

const buildChartQuery = (targetSymbol, options = {}) => {
  const params = new URLSearchParams({
    symbol: targetSymbol,
    interval: interval.value
  })
  const shouldIncludeMinute = options.includeMinute ?? (interval.value === 'day' || chartActiveView.value === 'minute')
  params.set('includeQuote', options.includeQuote ? 'true' : 'false')
  params.set('includeMinute', shouldIncludeMinute ? 'true' : 'false')
  if (selectedSource.value) {
    params.set('source', selectedSource.value)
  }
  return params
}

const applyLatestDetail = (workspace, requestToken, payload) => {
  if (!workspace || requestToken !== workspace.quoteRequestToken || !payload) {
    return false
  }

  const nextQuote = payload.quote ?? workspace.detail?.quote ?? null
  if (!nextQuote) {
    return false
  }

  workspace.detail = {
    ...(workspace.detail ?? {}),
    ...payload,
    quote: nextQuote,
    kLines: payload.kLines ?? workspace.detail?.kLines ?? [],
    minuteLines: payload.minuteLines ?? workspace.detail?.minuteLines ?? [],
    messages: payload.messages ?? workspace.detail?.messages ?? [],
    fundamentalSnapshot: payload.fundamentalSnapshot ?? workspace.pendingFundamentalSnapshot ?? workspace.detail?.fundamentalSnapshot ?? null
  }
  workspace.pendingFundamentalSnapshot = undefined
  return true
}

const applyLatestMessages = (workspace, requestToken, messages) => {
  if (!workspace || requestToken !== workspace.quoteRequestToken || !workspace.detail) {
    return false
  }

  workspace.detail = {
    ...workspace.detail,
    messages: Array.isArray(messages) ? messages : []
  }
  return true
}

const applyFundamentalSnapshot = (workspace, requestToken, snapshot) => {
  if (!workspace || requestToken !== workspace.quoteRequestToken) {
    return false
  }

  workspace.pendingFundamentalSnapshot = snapshot ?? null
  if (!workspace.detail) {
    return true
  }

  workspace.detail = {
    ...workspace.detail,
    fundamentalSnapshot: snapshot ?? null
  }
  return true
}

const openMarketNewsModal = () => {
  marketNewsModalOpen.value = true
}

const closeMarketNewsModal = () => {
  marketNewsModalOpen.value = false
}

const openExternal = url => {
  if (!url) return
  window.open(url, '_blank', 'noopener,noreferrer')
}

const closeTradingPlanModal = symbolKey => {
  const workspace = getWorkspace(symbolKey)
  if (!workspace) {
    return
  }
  workspace.planModalOpen = false
}

const editTradingPlan = (symbolKey, item) => {
  const workspace = getWorkspace(symbolKey)
  if (!workspace || !item?.id) {
    return
  }

  workspace.planError = ''
  workspace.planForm = createTradingPlanForm(item)
  workspace.planModalOpen = true
}

const saveTradingPlan = async (symbolKey = currentStockKey.value) => {
  const workspace = getWorkspace(symbolKey) ?? currentWorkspace.value
  if (!workspace?.planForm) {
    return
  }

  workspace.planSaving = true
  workspace.planError = ''
  try {
    const form = workspace.planForm
    const isEditing = Boolean(form.id)
    const response = await fetch(isEditing ? `/api/stocks/plans/${form.id}` : '/api/stocks/plans', {
      method: isEditing ? 'PUT' : 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(isEditing
        ? {
            name: form.name,
            direction: form.direction,
            triggerPrice: normalizePlanNumber(form.triggerPrice),
            invalidPrice: normalizePlanNumber(form.invalidPrice),
            stopLossPrice: normalizePlanNumber(form.stopLossPrice),
            takeProfitPrice: normalizePlanNumber(form.takeProfitPrice),
            targetPrice: normalizePlanNumber(form.targetPrice),
            expectedCatalyst: normalizeOptionalText(form.expectedCatalyst),
            invalidConditions: normalizeOptionalText(form.invalidConditions),
            riskLimits: normalizeOptionalText(form.riskLimits),
            analysisSummary: normalizeOptionalText(form.analysisSummary),
            sourceAgent: form.sourceAgent || 'commander',
            userNote: normalizeOptionalText(form.userNote)
          }
        : {
            symbol: form.symbol,
            name: form.name,
            direction: form.direction,
            triggerPrice: normalizePlanNumber(form.triggerPrice),
            invalidPrice: normalizePlanNumber(form.invalidPrice),
            stopLossPrice: normalizePlanNumber(form.stopLossPrice),
            takeProfitPrice: normalizePlanNumber(form.takeProfitPrice),
            targetPrice: normalizePlanNumber(form.targetPrice),
            expectedCatalyst: normalizeOptionalText(form.expectedCatalyst),
            invalidConditions: normalizeOptionalText(form.invalidConditions),
            riskLimits: normalizeOptionalText(form.riskLimits),
            analysisSummary: normalizeOptionalText(form.analysisSummary),
            analysisHistoryId: Number(form.analysisHistoryId),
            sourceAgent: form.sourceAgent || 'commander',
            userNote: normalizeOptionalText(form.userNote)
          })
    })

    if (!response.ok) {
      throw new Error(await parseResponseMessage(response, '交易计划保存失败'))
    }

    const saved = normalizeTradingPlan(await response.json())
    workspace.planModalOpen = false
    workspace.planForm = createTradingPlanForm({
      symbol: workspace.detail?.quote?.symbol ?? '',
      name: workspace.detail?.quote?.name ?? ''
    })
    await refreshTradingPlanSection(symbolKey, true)
    await refreshTradingPlanBoard(true)
    if (saved?.id) {
      workspace.planList = [saved, ...workspace.planList.filter(item => item.id !== saved.id)]
    }
  } catch (err) {
    workspace.planError = err.message || '交易计划保存失败'
  } finally {
    workspace.planSaving = false
  }
}

const deleteTradingPlan = async (symbolKey, item) => {
  const workspace = getWorkspace(symbolKey)
  if (!workspace || !item?.id) {
    return
  }

  if (typeof window !== 'undefined' && typeof window.confirm === 'function' && !window.confirm(`确认删除交易计划「${item.name}」？`)) {
    return
  }

  deletingPlanId.value = String(item.id)
  workspace.planError = ''
  try {
    const response = await fetch(`/api/stocks/plans/${item.id}`, { method: 'DELETE' })
    if (!response.ok) {
      throw new Error(await parseResponseMessage(response, '交易计划删除失败'))
    }

    workspace.planList = workspace.planList.filter(plan => String(plan.id) !== String(item.id))
    workspace.planAlerts = workspace.planAlerts.filter(plan => String(plan.planId) !== String(item.id))
    await refreshTradingPlanSection(symbolKey, true)
    await refreshTradingPlanBoard(true)
  } catch (err) {
    workspace.planError = err.message || '交易计划删除失败'
  } finally {
    deletingPlanId.value = ''
  }
}

const resumeTradingPlan = async (symbolKey, item) => {
  const workspace = getWorkspace(symbolKey)
  if (!workspace || !item?.id) {
    return
  }

  resumingPlanId.value = String(item.id)
  workspace.planError = ''
  try {
    const response = await fetch(`/api/stocks/plans/${item.id}/resume`, { method: 'POST' })
    if (!response.ok) {
      throw new Error(await parseResponseMessage(response, '交易计划恢复观察失败'))
    }

    const saved = normalizeTradingPlan(await response.json())
    workspace.planList = workspace.planList.map(plan => (String(plan.id) === String(saved.id) ? saved : plan))
    await refreshTradingPlanSection(symbolKey, true)
    await refreshTradingPlanBoard(true)
  } catch (err) {
    workspace.planError = err.message || '交易计划恢复观察失败'
  } finally {
    resumingPlanId.value = ''
  }
}

const jumpToPlanSymbol = symbolKey => {
  const normalizedSymbol = normalizeStockSymbol(symbolKey)
  if (!normalizedSymbol) {
    return
  }

  selectedSymbol.value = normalizedSymbol
  symbol.value = normalizedSymbol
  currentStockKey.value = normalizedSymbol
  fetchQuote()
}

const marketNewsItems = computed(() => marketNewsBucket.value?.items ?? [])
const marketNewsPreviewItems = computed(() => marketNewsItems.value.slice(0, 3))

const sortedHistoryList = computed(() => {
  const list = [...historyList.value]
  const key = sortKey.value
  const direction = sortAsc.value ? 1 : -1
  return list.sort((a, b) => {
    const va = getSortValue(a, key)
    const vb = getSortValue(b, key)
    if (va === vb) return 0
    return va > vb ? direction : -direction
  })
})

const toggleSort = key => {
  if (sortKey.value === key) {
    sortAsc.value = !sortAsc.value
  } else {
    sortKey.value = key
    sortAsc.value = true
  }
}

const openContextMenu = (event, item) => {
  event.preventDefault()
  contextMenu.value = {
    visible: true,
    x: event.clientX,
    y: event.clientY,
    item
  }
}

const closeContextMenu = () => {
  contextMenu.value = { visible: false, x: 0, y: 0, item: null }
}

const deleteHistoryItem = async () => {
  const target = contextMenu.value.item
  const id = target?.id ?? target?.Id
  if (!id) {
    closeContextMenu()
    return
  }

  try {
    const response = await fetch(`/api/stocks/history/${id}`, { method: 'DELETE' })
    if (response.ok || response.status === 204) {
      historyList.value = historyList.value.filter(item => (item.id ?? item.Id) !== id)
    }
  } finally {
    closeContextMenu()
  }
}

const appendRecordedHistory = async quote => {
  if (!quote?.symbol && !quote?.Symbol) {
    return
  }

  const payload = {
    symbol: quote.symbol ?? quote.Symbol ?? '',
    name: quote.name ?? quote.Name ?? '',
    price: Number(quote.price ?? quote.Price ?? 0),
    changePercent: Number(quote.changePercent ?? quote.ChangePercent ?? 0),
    turnoverRate: Number(quote.turnoverRate ?? quote.TurnoverRate ?? 0),
    peRatio: Number(quote.peRatio ?? quote.PeRatio ?? 0),
    high: Number(quote.high ?? quote.High ?? 0),
    low: Number(quote.low ?? quote.Low ?? 0),
    speed: Number(quote.speed ?? quote.Speed ?? 0)
  }

  const response = await fetch('/api/stocks/history', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload)
  })

  if (!response.ok) {
    throw new Error(await parseResponseMessage(response, '历史记录保存失败'))
  }

  const saved = await response.json()
  const savedId = String(saved?.id ?? saved?.Id ?? '')
  const savedSymbol = String(saved?.symbol ?? saved?.Symbol ?? '').toLowerCase()
  const currentList = Array.isArray(historyList.value) ? historyList.value : []

  historyList.value = [
    saved,
    ...currentList.filter(item => {
      const itemId = String(item?.id ?? item?.Id ?? '')
      const itemSymbol = String(item?.symbol ?? item?.Symbol ?? '').toLowerCase()
      if (savedId && itemId) {
        return itemId !== savedId
      }
      return !savedSymbol || itemSymbol !== savedSymbol
    })
  ]
}

const historySaveFailed = err => {
  historyError.value = err?.message || '历史记录保存失败'
}

const {
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
} = createStockInfoTabDataRequests({
  buildRealtimeContextSymbols,
  currentStockKey,
  detail,
  DOMESTIC_REALTIME_CONTEXT_SYMBOLS,
  fetchBackendGet,
  getStockRealtimeAbortController: () => stockRealtimeAbortController,
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
  setStockRealtimeAbortController: value => {
    stockRealtimeAbortController = value
  },
  sidebarNewsSections,
  sources,
  stockRealtimeError,
  stockRealtimeLoading,
  stockRealtimeOverview,
  stockRealtimeOverviewEnabled,
  stockRealtimeSymbol
})

const searchStocks = async query => {
  searchLoading.value = true
  searchError.value = ''
  try {
    const params = new URLSearchParams({ q: query })
    const response = await fetchBackendGet(`/api/stocks/search?${params.toString()}`)
    if (!response.ok) {
      throw new Error('搜索失败')
    }
    searchResults.value = await response.json()
    searchOpen.value = true
  } catch (err) {
    searchError.value = err.message || '搜索失败'
    searchResults.value = []
    searchOpen.value = true
  } finally {
    searchLoading.value = false
  }
}

const onSymbolInput = () => {
  if (isSelecting) {
    return
  }

  if (searchTimer) {
    clearTimeout(searchTimer)
  }

  searchResults.value = []
  searchOpen.value = false
  searchError.value = ''
  selectedSymbol.value = ''
}

const onSymbolEnter = event => {
  if (event?.isComposing) {
    return
  }
  fetchQuote()
}

const selectSearchResult = item => {
  isSelecting = true
  const rawSymbol = item.symbol || item.Symbol || item.code || item.Code || ''
  const normalizedSymbol = normalizeStockSymbol(rawSymbol)
  symbol.value = normalizedSymbol || item.code || item.Code || item.symbol || item.Symbol || ''
  selectedSymbol.value = normalizedSymbol
  searchOpen.value = false
  searchResults.value = []
  setTimeout(() => {
    isSelecting = false
    if (symbol.value.trim()) {
      fetchQuote()
    }
  }, 0)
}

const closeSearch = () => {
  searchOpen.value = false
}

const {
  fetchQuote,
  refreshChartData
} = createStockInfoTabQuoteRuntime({
  applyFundamentalSnapshot,
  appendRecordedHistory,
  currentStockKey,
  fetchBackendGet,
  fetchStockRealtimeOverview,
  getSelectedSource: () => selectedSource.value,
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
})

const setupHistoryRefresh = () => {
  if (historyTimer) {
    clearInterval(historyTimer)
    historyTimer = null
  }

  if (historyAutoRefresh.value && historyRefreshSeconds.value > 0) {
    historyTimer = setInterval(() => {
      if (!historyLoading.value) {
        refreshHistory()
      }
    }, historyRefreshSeconds.value * 1000)
  }
}

const setupRefresh = () => {
  if (refreshTimer) {
    clearInterval(refreshTimer)
    refreshTimer = null
  }

  if (autoRefresh.value && refreshSeconds.value > 0) {
    refreshTimer = setInterval(() => {
      if (!loading.value && symbol.value.trim()) {
        fetchQuote()
      }
    }, refreshSeconds.value * 1000)
  }
}

const setupPlanRefresh = () => {
  if (planRefreshTimer) {
    clearInterval(planRefreshTimer)
    planRefreshTimer = null
  }

  if (refreshSeconds.value > 0) {
    planRefreshTimer = setInterval(() => {
      if (!rootWorkspace.planListLoading && !rootWorkspace.planAlertsLoading) {
        refreshTradingPlanBoard(true)
      }

      if (!currentStockKey.value) {
        return
      }

      const workspace = getWorkspace(currentStockKey.value)
      if (workspace && !workspace.planListLoading && !workspace.planAlertsLoading) {
        refreshTradingPlanSection(currentStockKey.value, true)
      }
    }, Math.max(15, refreshSeconds.value) * 1000)
  }
}

const resolveActiveChartSymbolKey = () => normalizeStockSymbol(
  currentStockKey.value || detail.value?.quote?.symbol || selectedSymbol.value || symbol.value
)

const minuteTdSequentialRefreshEligible = computed(() => (
  chartActiveView.value === 'minute'
  && minuteTdSequentialEnabled.value
  && Boolean(resolveActiveChartSymbolKey())
))

const setupMinuteTdSequentialRefresh = () => {
  if (minuteTdSequentialRefreshTimer) {
    clearInterval(minuteTdSequentialRefreshTimer)
    minuteTdSequentialRefreshTimer = null
  }

  if (!minuteTdSequentialRefreshEligible.value) {
    return
  }

  minuteTdSequentialRefreshTimer = setInterval(() => {
    const symbolKey = resolveActiveChartSymbolKey()
    if (!symbolKey || loading.value || currentWorkspace.value?.detailAbortController) {
      return
    }
    refreshChartData(symbolKey)
  }, 1000)
}

const handleChartViewChange = viewId => {
  chartActiveView.value = viewId || 'day'
}

const handleChartStrategyVisibilityChange = payload => {
  if (payload?.viewId !== 'minute' || payload?.strategyId !== 'minuteTdSequential') {
    return
  }
  minuteTdSequentialEnabled.value = payload.active === true
}

watch(interval, value => {
  localStorage.setItem('stock_interval', value)
  if (symbol.value.trim()) {
    refreshChartData(currentStockKey.value || selectedSymbol.value || symbol.value)
  }
})

watch(refreshSeconds, value => {
  localStorage.setItem('stock_refresh_seconds', String(value))
  setupRefresh()
  setupPlanRefresh()
})

watch(autoRefresh, value => {
  localStorage.setItem('stock_auto_refresh', String(value))
  setupRefresh()
})

watch(selectedSource, value => {
  localStorage.setItem('stock_source', value)
  if (symbol.value.trim()) {
    fetchQuote()
  }
  if (historyList.value.length) {
    refreshHistory()
  }
  if (detail.value?.quote?.symbol) {
    fetchNewsImpact(undefined, { force: true })
  }
})

watch(historyRefreshSeconds, value => {
  localStorage.setItem('stock_history_refresh_seconds', String(value))
  setupHistoryRefresh()
})

watch(historyAutoRefresh, value => {
  localStorage.setItem('stock_history_auto_refresh', String(value))
  setupHistoryRefresh()
})

watch(minuteTdSequentialRefreshEligible, () => {
  setupMinuteTdSequentialRefresh()
})

onMounted(() => {
  fetchSources()
  setupRefresh()
  fetchHistory()
  fetchMarketNews()
  refreshTradingPlanBoard()
  fetchStockRealtimeOverview('', { force: true })
  setupHistoryRefresh()
  setupPlanRefresh()
  window.addEventListener('click', closeContextMenu)
})

onUnmounted(() => {
  if (refreshTimer) {
    clearInterval(refreshTimer)
  }
  if (planRefreshTimer) {
    clearInterval(planRefreshTimer)
  }
  if (minuteTdSequentialRefreshTimer) {
    clearInterval(minuteTdSequentialRefreshTimer)
  }
  if (historyTimer) {
    clearInterval(historyTimer)
  }
  Object.values(stockWorkspaces).forEach(workspace => {
    workspace.detailAbortController?.abort()
    workspace.newsImpactAbortController?.abort()
    workspace.localNewsAbortController?.abort()
    workspace.chatSessionsAbortController?.abort()
    workspace.agentHistoryAbortController?.abort()
    workspace.planListAbortController?.abort()
    workspace.planAlertsAbortController?.abort()
    workspace.copilotDraftAbortController?.abort()
    workspace.copilotToolAbortController?.abort()
  })
  rootWorkspace.detailAbortController?.abort()
  rootWorkspace.newsImpactAbortController?.abort()
  rootWorkspace.localNewsAbortController?.abort()
  rootWorkspace.chatSessionsAbortController?.abort()
  rootWorkspace.agentHistoryAbortController?.abort()
  rootWorkspace.planListAbortController?.abort()
  rootWorkspace.planAlertsAbortController?.abort()
  stockRealtimeAbortController?.abort()
  window.removeEventListener('click', closeContextMenu)
})

watch(monochromeMode, value => {
  localStorage.setItem('stock_monochrome_mode', String(value))
})

watch(stockRealtimeOverviewEnabled, value => {
  localStorage.setItem('stock_realtime_context_enabled', String(value))
  if (value) {
    fetchStockRealtimeOverview(currentStockKey.value || '', { force: true })
  }
})

watch(
  () => detail.value?.quote?.symbol,
  symbolKey => {
    if (!symbolKey) {
      return
    }
    fetchNewsImpact(symbolKey)
    fetchLocalNews(symbolKey)
    refreshTradingPlanSection(symbolKey)
    fetchStockRealtimeOverview(symbolKey)
  }
)

watch(currentStockKey, () => {
  setupPlanRefresh()
})
</script>

<template>
  <section class="panel" :class="{ monochrome: monochromeMode }">
    <div class="panel-header">
      <div>
        <p class="panel-kicker">GOAL-012</p>
        <h2>股票信息终端</h2>
        <p class="muted panel-subtitle">左侧聚焦行情与图表，右侧预留后续扩展区域。</p>
      </div>
      <div class="panel-actions">
        <button class="mode-toggle" @click="monochromeMode = !monochromeMode">
          {{ monochromeMode ? '彩色模式' : '黑白模式' }}
        </button>
      </div>
    </div>
    <StockSearchToolbar
      :symbol="symbol"
      :selected-source="selectedSource"
      :refresh-seconds="refreshSeconds"
      :auto-refresh="autoRefresh"
      :sources="sources"
      :search-open="searchOpen"
      :search-results="searchResults"
      :search-loading="searchLoading"
      :search-error="searchError"
      :is-blocking-quote-load="isBlockingQuoteLoad"
      :is-background-quote-refresh="isBackgroundQuoteRefresh"
      :error="error"
      :history-auto-refresh="historyAutoRefresh"
      :history-refresh-seconds="historyRefreshSeconds"
      :history-loading="historyLoading"
      :history-error="historyError"
      :history-list="historyList"
      :sorted-history-list="sortedHistoryList"
      :context-menu="contextMenu"
      :get-change-class="getChangeClass"
      :format-percent="formatPercent"
      @update:symbol="symbol = $event"
      @update:selected-source="selectedSource = $event"
      @update:refresh-seconds="refreshSeconds = $event"
      @update:auto-refresh="autoRefresh = $event"
      @update:history-refresh-seconds="historyRefreshSeconds = $event"
      @update:history-auto-refresh="historyAutoRefresh = $event"
      @symbol-input="onSymbolInput"
      @symbol-enter="onSymbolEnter"
      @fetch-quote="fetchQuote"
      @close-search="closeSearch"
      @select-search-result="selectSearchResult($event)"
      @refresh-history="refreshHistory"
      @apply-history-symbol="applyHistorySymbol($event)"
      @open-context-menu="openContextMenu"
      @delete-history-item="deleteHistoryItem"
    />

    <StockTopMarketOverview
      :enabled="stockRealtimeOverviewEnabled"
      :loading="stockRealtimeLoading"
      :error="stockRealtimeError"
      :overview="stockRealtimeOverview"
      :detail="detail"
      :current-stock-realtime-quote="currentStockRealtimeQuote"
      :stock-realtime-domestic-indices="stockRealtimeDomesticIndices"
      :stock-realtime-global-indices="stockRealtimeGlobalIndices"
      :stock-realtime-relative-strength="stockRealtimeRelativeStrength"
      :stock-realtime-breadth-bias="stockRealtimeBreadthBias"
      :format-date="formatDate"
      :get-change-class="getChangeClass"
      :format-signed-number="formatSignedNumber"
      :format-signed-percent="formatSignedPercent"
      :format-signed-realtime-amount="formatSignedRealtimeAmount"
      :format-realtime-money="formatRealtimeMoney"
      @refresh="fetchStockRealtimeOverview(currentStockKey || '', { force: true })"
      @toggle="stockRealtimeOverviewEnabled = !stockRealtimeOverviewEnabled"
    />

    <StockMarketNewsPanel
      :detail="detail"
      :loading="marketNewsLoading"
      :error="marketNewsError"
      :items="marketNewsItems"
      :preview-items="marketNewsPreviewItems"
      :modal-open="marketNewsModalOpen"
      :get-impact-class="getImpactClass"
      :get-local-news-headline="getLocalNewsHeadline"
      :format-date="formatDate"
      @refresh="fetchMarketNews({ force: true })"
      @open-modal="openMarketNewsModal"
      @close-modal="closeMarketNewsModal"
    />

    <div class="workspace-grid">
      <TerminalView :quote="detail?.quote ?? null" :monochrome="monochromeMode">
        <template #summary>
          <StockTerminalSummary
            :detail="detail"
            :show-source-load-progress="showSourceLoadProgress"
            :source-load-progress-title="sourceLoadProgressTitle"
            :source-load-progress-percent="sourceLoadProgressPercent"
            :visible-source-load-stages="visibleSourceLoadStages"
            :format-date="formatDate"
            @open-external="openExternal($event)"
          />
        </template>

        <template #chart>
          <div class="stock-chart-section">
            <StockCharts
              v-if="detail"
              :k-lines="detail.kLines"
              :minute-lines="detail.minuteLines"
              :base-price="Number(detail.quote.price) - Number(detail.quote.change)"
              :interval="interval"
              @update:interval="interval = $event"
              @view-change="handleChartViewChange"
              @strategy-visibility-change="handleChartStrategyVisibilityChange"
            />
            <div v-else class="chart-placeholder">
              <p>查询股票后，这里会以终端视图显示 K 线、分时、成交量和均线。</p>
            </div>
          </div>
        </template>
      </TerminalView>

      <div class="sidebar-workspace">
        <StockTradingPlanBoard
          :workspace="rootWorkspace"
          :format-trading-plan-status="formatTradingPlanStatus"
          :get-trading-plan-status-class="getTradingPlanStatusClass"
          :format-plan-scale="formatPlanScale"
          :format-plan-price="formatPlanPrice"
          :get-latest-plan-alert="getLatestPlanAlert"
          :get-plan-alert-class="getPlanAlertClass"
          :format-plan-alert-summary="formatPlanAlertSummary"
          :get-plan-review-headline="getPlanReviewHeadline"
          :get-plan-review-text="getPlanReviewText"
          @refresh="refreshTradingPlanBoard(true)"
          @jump="jumpToPlanSymbol($event)"
        />

        <template v-if="sidebarWorkspaces.length">
          <div
            v-for="workspace in sidebarWorkspaces"
            :key="`sidebar-${workspace.symbolKey}`"
            v-show="workspace.symbolKey === currentStockKey"
            class="sidebar-workspace"
          >
            <StockNewsImpactPanel
              :workspace="workspace"
              :sidebar-news-sections="sidebarNewsSections"
              :get-impact-class="getImpactClass"
              :get-local-news-headline="getLocalNewsHeadline"
              :format-date="formatDate"
              :get-headline-news-impact-events="getHeadlineNewsImpactEvents"
              :get-impact-category-value="getImpactCategoryValue"
              :format-impact-score="formatImpactScore"
              @refresh="fetchNewsImpact(workspace.symbolKey, { force: true })"
            />

            <StockTradingPlanSection
              :workspace="workspace"
              :deleting-plan-id="deletingPlanId"
              :resuming-plan-id="resumingPlanId"
              :format-trading-plan-status="formatTradingPlanStatus"
              :format-date="formatDate"
              :get-trading-plan-status-class="getTradingPlanStatusClass"
              :format-plan-scale="formatPlanScale"
              :format-plan-price="formatPlanPrice"
              :get-latest-plan-alert="getLatestPlanAlert"
              :get-plan-alert-class="getPlanAlertClass"
              :format-plan-alert-summary="formatPlanAlertSummary"
              :get-plan-review-headline="getPlanReviewHeadline"
              :get-plan-review-text="getPlanReviewText"
              :can-edit-trading-plan="canEditTradingPlan"
              :can-resume-trading-plan="canResumeTradingPlan"
              @refresh="refreshTradingPlanSection(workspace.symbolKey, true)"
              @edit="editTradingPlan(workspace.symbolKey, $event)"
              @resume="resumeTradingPlan(workspace.symbolKey, $event)"
              @delete="deleteTradingPlan(workspace.symbolKey, $event)"
            />
          </div>
        </template>

        <div v-else class="copilot-card ai-placeholder-card">
          <div class="terminal-empty compact-empty">
            <h4>扩展区已留空</h4>
            <p>股票助手、会话化协驾和多 Agent 分析模块已移除，后续可在这里重新接入新方案。</p>
          </div>
        </div>

        <StockTradingPlanModal
          :workspace="activePlanModalWorkspace"
          :format-plan-scale="formatPlanScale"
          @close="closeTradingPlanModal(activePlanModalWorkspace.symbolKey)"
          @save="saveTradingPlan(activePlanModalWorkspace.symbolKey)"
        />
      </div>
    </div>
  </section>
</template>

<style scoped>
.panel {
  background: linear-gradient(135deg, rgba(255, 255, 255, 0.75), rgba(248, 250, 252, 0.85));
  backdrop-filter: blur(10px);
  border: 1px solid rgba(148, 163, 184, 0.2);
  box-shadow: 0 10px 30px rgba(15, 23, 42, 0.08);
  border-radius: 16px;
  padding: 1.5rem;
  display: grid;
  gap: 1rem;
}

.stock-chart-section {
  border-radius: 18px;
}

.panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}

.panel h2 {
  margin-bottom: 0;
  color: #0f172a;
}

.panel-kicker {
  margin: 0 0 0.3rem;
  font-size: 0.72rem;
  letter-spacing: 0.18em;
  text-transform: uppercase;
  color: #2563eb;
}

.panel-subtitle {
  margin: 0.3rem 0 0;
}

.panel-actions {
  display: flex;
  gap: 0.75rem;
  flex-wrap: wrap;
  justify-content: flex-end;
}

.sidebar-workspace {
  display: grid;
  gap: 1rem;
}

.mode-toggle {
  border-radius: 999px;
  border: none;
  padding: 0.45rem 0.9rem;
  background: #e2e8f0;
  color: #0f172a;
  cursor: pointer;
}

.error-text {
  color: #b91c1c;
}

.workspace-grid {
  display: grid;
  gap: 1rem;
  grid-template-columns: minmax(0, 1.75fr) minmax(320px, 0.95fr);
  align-items: start;
  min-height: calc(100vh - 238px);
}

.terminal-empty p {
  margin: 0.2rem 0;
}

.terminal-empty {
  display: grid;
  gap: 0.35rem;
  min-height: 140px;
  align-content: center;
}

.terminal-empty h4 {
  margin: 0;
  color: #f8fafc;
}

.compact-empty h4 {
  color: #0f172a;
}

.chart-placeholder {
  display: grid;
  place-items: center;
  min-height: 100%;
  border-radius: 18px;
  border: 1px dashed rgba(148, 163, 184, 0.35);
  color: #cbd5e1;
}

.copilot-card {
  padding: 1rem;
  border-radius: 16px;
  background: rgba(255, 255, 255, 0.72);
  border: 1px solid rgba(148, 163, 184, 0.16);
}

.ai-placeholder-card {
  min-height: 180px;
}

.text-rise {
  color: #ef4444;
  font-weight: 600;
}

.text-fall {
  color: #22c55e;
  font-weight: 600;
}

.panel.monochrome {
  background: #ffffff;
  color: #000000;
  box-shadow: none;
}

.panel.monochrome .mode-toggle,
.panel.monochrome .context-menu button {
  background: #6b7280;
  color: #ffffff;
}

.panel.monochrome .history,
.panel.monochrome .history-table {
  background: #ffffff;
  color: #000000;
}
.panel.monochrome .text-rise,
.panel.monochrome .text-fall {
  color: #000000;
  font-weight: 600;
}

.impact-tag {
  padding: 0.2rem 0.45rem;
  border-radius: 999px;
  font-size: 0.78rem;
  font-weight: 600;
}

.impact-positive {
  background: rgba(239, 68, 68, 0.12);
  color: #b91c1c;
}

.impact-negative {
  background: rgba(34, 197, 94, 0.14);
  color: #166534;
}

.impact-neutral {
  background: rgba(148, 163, 184, 0.18);
  color: #475569;
}

.impact-score {
  font-weight: 600;
}

.chat-session {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.75rem;
}

.chat-session p {
  margin: 0;
}

.history-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.9rem;
}

.history-table th,
.history-table td {
  padding: 0.6rem 0.55rem;
  border-bottom: 1px solid rgba(226, 232, 240, 0.9);
  text-align: left;
}

.history-table th {
  color: #475569;
  cursor: pointer;
  white-space: nowrap;
}

.history-table tbody tr {
  cursor: pointer;
}

.history-table tbody tr:hover {
  background: rgba(241, 245, 249, 0.9);
}

.text-rise {
  color: #dc2626;
}

.text-fall {
  color: #16a34a;
}

@media (max-width: 1180px) {
  .workspace-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 720px) {
  .panel-header,
  .deck-card-header {
    flex-direction: column;
  }

  .panel-actions {
    width: 100%;
    justify-content: flex-start;
  }
}
</style>
