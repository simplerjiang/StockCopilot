<script setup>
import { computed, nextTick, onMounted, onUnmounted, reactive, ref, watch } from 'vue'
import StockCharts from './StockCharts.vue'
import StockAgentPanels from './StockAgentPanels.vue'
import TerminalView from './TerminalView.vue'
import CopilotPanel from './CopilotPanel.vue'
import ChatWindow from '../../components/ChatWindow.vue'

const symbol = ref('')
const interval = ref(localStorage.getItem('stock_interval') || 'day')
const refreshSeconds = ref(Number(localStorage.getItem('stock_refresh_seconds') || 30))
const autoRefresh = ref(localStorage.getItem('stock_auto_refresh') === 'true')
const sources = ref([])
const selectedSource = ref(localStorage.getItem('stock_source') || '')
let refreshTimer = null
let planRefreshTimer = null
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
const copilotPanelOpen = ref(localStorage.getItem('stock_copilot_panel_open') !== 'false')
const marketNewsModalOpen = ref(false)
const chatWindowRefs = new Map()

const createWorkspace = symbolKey => reactive({
  symbolKey,
  detail: null,
  loading: false,
  error: '',
  quoteRequestToken: 0,
  detailAbortController: null,
  chatSessions: [],
  chatSessionsLoading: false,
  chatSessionsError: '',
  selectedChatSession: '',
  chatSessionsLoaded: false,
  chatSessionsRequestToken: 0,
  chatSessionsAbortController: null,
  agentResults: [],
  agentLoading: false,
  agentError: '',
  agentUpdatedAt: '',
  agentHistoryList: [],
  agentHistoryLoading: false,
  agentHistoryError: '',
  selectedAgentHistoryId: '',
  agentHistoryLoaded: false,
  agentHistoryRequestToken: 0,
  agentHistoryAbortController: null,
  newsImpact: null,
  newsImpactLoading: false,
  newsImpactError: '',
  newsImpactLoaded: false,
  newsImpactRequestToken: 0,
  newsImpactAbortController: null,
  localNewsBuckets: { stock: null, sector: null, market: null },
  localNewsLoading: false,
  localNewsError: '',
  localNewsLoaded: false,
  localNewsRequestToken: 0,
  localNewsAbortController: null,
  planDraftLoading: false,
  planSaving: false,
  planError: '',
  planModalOpen: false,
  planForm: null,
  planList: [],
  planAlerts: [],
  planListLoading: false,
  planAlertsLoading: false,
  planListLoaded: false,
  planAlertsLoaded: false,
  planListRequestToken: 0,
  planAlertsRequestToken: 0,
  planListAbortController: null,
  planAlertsAbortController: null
})

const stockWorkspaces = reactive({})
const rootWorkspace = createWorkspace('__root__')
const currentStockKey = ref('')

const getWorkspace = symbolKey => {
  const normalized = normalizeStockSymbol(symbolKey)
  if (!normalized) {
    return null
  }
  if (!stockWorkspaces[normalized]) {
    stockWorkspaces[normalized] = createWorkspace(normalized)
  }
  return stockWorkspaces[normalized]
}

const currentWorkspace = computed(() => getWorkspace(currentStockKey.value) ?? rootWorkspace)
const sidebarWorkspaces = computed(() =>
  Object.values(stockWorkspaces).filter(workspace => Boolean(workspace?.detail?.quote?.symbol))
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
const agentResults = bindWorkspaceField('agentResults', [])
const agentLoading = bindWorkspaceField('agentLoading', false)
const agentError = bindWorkspaceField('agentError', '')
const agentUpdatedAt = bindWorkspaceField('agentUpdatedAt', '')
const agentHistoryList = bindWorkspaceField('agentHistoryList', [])
const agentHistoryLoading = bindWorkspaceField('agentHistoryLoading', false)
const agentHistoryError = bindWorkspaceField('agentHistoryError', '')
const selectedAgentHistoryId = bindWorkspaceField('selectedAgentHistoryId', '')
const newsImpact = bindWorkspaceField('newsImpact', null)
const newsImpactLoading = bindWorkspaceField('newsImpactLoading', false)
const newsImpactError = bindWorkspaceField('newsImpactError', '')
const localNewsBuckets = bindWorkspaceField('localNewsBuckets', { stock: null, sector: null, market: null })
const localNewsLoading = bindWorkspaceField('localNewsLoading', false)
const localNewsError = bindWorkspaceField('localNewsError', '')
const marketNewsBucket = computed(() => rootWorkspace.localNewsBuckets.market ?? null)
const marketNewsLoading = computed(() => rootWorkspace.localNewsLoading)
const marketNewsError = computed(() => rootWorkspace.localNewsError)
const deletingPlanId = ref('')

const isBlockingQuoteLoad = computed(() => loading.value && !detail.value)
const isBackgroundQuoteRefresh = computed(() => loading.value && !!detail.value)

const sidebarNewsSections = [
  { key: 'stock', title: '个股事实' },
  { key: 'sector', title: '板块上下文' }
]

const isAbortError = err => err?.name === 'AbortError'

const replaceAbortController = currentController => {
  currentController?.abort()
  return new AbortController()
}

const upsertAgentResult = result => {
  const agentId = result?.agentId ?? result?.AgentId ?? ''
  if (!agentId) return
  const list = [...agentResults.value]
  const index = list.findIndex(item => (item.agentId ?? item.AgentId) === agentId)
  if (index >= 0) {
    list[index] = result
  } else {
    list.push(result)
  }
  agentResults.value = list
}

const normalizeStockSymbol = value => {
  const trimmed = String(value || '').trim().toLowerCase()
  if (!trimmed) {
    return ''
  }
  if (/^(sh|sz)\d{6}$/.test(trimmed)) {
    return trimmed
  }
  if (/^\d{6}$/.test(trimmed)) {
    return trimmed.startsWith('6') ? `sh${trimmed}` : `sz${trimmed}`
  }
  return trimmed
}

const isDirectStockSymbol = value => /^(sh|sz)\d{6}$/.test(normalizeStockSymbol(value))

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
    symbol: targetSymbol,
    interval: interval.value
  })
  if (selectedSource.value) {
    params.set('source', selectedSource.value)
  }
  return params
}

const applyLatestDetail = (workspace, requestToken, payload) => {
  if (!workspace || requestToken !== workspace.quoteRequestToken || !payload?.quote) {
    return false
  }

  workspace.detail = payload
  return true
}


const cnDateTimeFormatter = new Intl.DateTimeFormat('zh-CN', {
  timeZone: 'Asia/Shanghai',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hour12: false
})

const formatDate = value => {
  if (!value) return ''
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''
  return cnDateTimeFormatter.format(date)
}

const formatImpactScore = value => {
  if (value == null || Number.isNaN(Number(value))) return ''
  const num = Number(value)
  return num > 0 ? `+${num}` : `${num}`
}

const getImpactClass = category => {
  if (category === '利好') return 'impact-positive'
  if (category === '利空') return 'impact-negative'
  return 'impact-neutral'
}

const normalizeLocalNewsItem = item => ({
  title: item?.title ?? item?.Title ?? '',
  translatedTitle: item?.translatedTitle ?? item?.TranslatedTitle ?? '',
  source: item?.source ?? item?.Source ?? '',
  sourceTag: item?.sourceTag ?? item?.SourceTag ?? '',
  category: item?.category ?? item?.Category ?? '',
  sentiment: item?.sentiment ?? item?.Sentiment ?? '中性',
  publishTime: item?.publishTime ?? item?.PublishTime ?? '',
  crawledAt: item?.crawledAt ?? item?.CrawledAt ?? '',
  url: item?.url ?? item?.Url ?? '',
  aiTarget: item?.aiTarget ?? item?.AiTarget ?? '',
  aiTags: Array.isArray(item?.aiTags ?? item?.AiTags) ? (item.aiTags ?? item.AiTags) : []
})

const getLocalNewsHeadline = item => item?.translatedTitle || item?.title || ''

const openMarketNewsModal = () => {
  marketNewsModalOpen.value = true
}

const closeMarketNewsModal = () => {
  marketNewsModalOpen.value = false
}

const normalizeNewsBucket = (level, payload) => ({
  level,
  symbol: payload?.symbol ?? payload?.Symbol ?? '',
  sectorName: payload?.sectorName ?? payload?.SectorName ?? '',
  items: Array.isArray(payload?.items ?? payload?.Items) ? (payload.items ?? payload.Items).map(normalizeLocalNewsItem) : []
})

const normalizePlanNumber = value => {
  if (value === '' || value == null) return null
  const number = Number(value)
  return Number.isFinite(number) ? number : null
}

const normalizePlanStatus = value => {
  if (value === 'Draft') {
    return 'Pending'
  }
  if (value === 'Archived') {
    return 'Cancelled'
  }
  return value || 'Pending'
}

const normalizeTradingPlan = item => ({
  id: item?.id ?? item?.Id ?? '',
  symbol: item?.symbol ?? item?.Symbol ?? '',
  name: item?.name ?? item?.Name ?? '',
  direction: item?.direction ?? item?.Direction ?? 'Long',
  status: normalizePlanStatus(item?.status ?? item?.Status),
  triggerPrice: normalizePlanNumber(item?.triggerPrice ?? item?.TriggerPrice),
  invalidPrice: normalizePlanNumber(item?.invalidPrice ?? item?.InvalidPrice),
  stopLossPrice: normalizePlanNumber(item?.stopLossPrice ?? item?.StopLossPrice),
  takeProfitPrice: normalizePlanNumber(item?.takeProfitPrice ?? item?.TakeProfitPrice),
  targetPrice: normalizePlanNumber(item?.targetPrice ?? item?.TargetPrice),
  expectedCatalyst: item?.expectedCatalyst ?? item?.ExpectedCatalyst ?? '',
  invalidConditions: item?.invalidConditions ?? item?.InvalidConditions ?? '',
  riskLimits: item?.riskLimits ?? item?.RiskLimits ?? '',
  analysisSummary: item?.analysisSummary ?? item?.AnalysisSummary ?? '',
  analysisHistoryId: item?.analysisHistoryId ?? item?.AnalysisHistoryId ?? '',
  sourceAgent: item?.sourceAgent ?? item?.SourceAgent ?? 'commander',
  userNote: item?.userNote ?? item?.UserNote ?? '',
  createdAt: item?.createdAt ?? item?.CreatedAt ?? '',
  updatedAt: item?.updatedAt ?? item?.UpdatedAt ?? '',
  watchlistEnsured: item?.watchlistEnsured ?? item?.WatchlistEnsured ?? null
})

const normalizeTradingPlanAlert = item => ({
  id: item?.id ?? item?.Id ?? '',
  planId: item?.planId ?? item?.PlanId ?? '',
  symbol: item?.symbol ?? item?.Symbol ?? '',
  eventType: item?.eventType ?? item?.EventType ?? '',
  severity: item?.severity ?? item?.Severity ?? 'Info',
  message: item?.message ?? item?.Message ?? '',
  snapshotPrice: normalizePlanNumber(item?.snapshotPrice ?? item?.SnapshotPrice),
  metadataJson: item?.metadataJson ?? item?.MetadataJson ?? '',
  occurredAt: item?.occurredAt ?? item?.OccurredAt ?? ''
})

const createTradingPlanForm = item => ({
  id: item?.id ?? '',
  symbol: item?.symbol ?? '',
  name: item?.name ?? '',
  direction: item?.direction ?? 'Long',
  triggerPrice: item?.triggerPrice ?? '',
  invalidPrice: item?.invalidPrice ?? '',
  stopLossPrice: item?.stopLossPrice ?? '',
  takeProfitPrice: item?.takeProfitPrice ?? '',
  targetPrice: item?.targetPrice ?? '',
  expectedCatalyst: item?.expectedCatalyst ?? '',
  invalidConditions: item?.invalidConditions ?? '',
  riskLimits: item?.riskLimits ?? '',
  analysisSummary: item?.analysisSummary ?? '',
  analysisHistoryId: item?.analysisHistoryId ?? '',
  sourceAgent: item?.sourceAgent ?? 'commander',
  userNote: item?.userNote ?? ''
})

const normalizeOptionalText = value => {
  const result = String(value ?? '').trim()
  return result || null
}

const formatPlanPrice = value => {
  const number = normalizePlanNumber(value)
  return Number.isFinite(number) ? Number(number).toFixed(2) : '待补录'
}

const getLatestPlanAlert = (workspace, planId) => {
  if (!workspace || !planId) return null
  return (Array.isArray(workspace.planAlerts) ? workspace.planAlerts : []).find(item => String(item.planId) === String(planId)) || null
}

const getPlanAlertClass = severity => {
  if (severity === 'Critical') return 'plan-alert-critical'
  if (severity === 'Warning') return 'plan-alert-warning'
  return 'plan-alert-info'
}

const formatPlanAlertSummary = alert => {
  if (!alert) return ''
  const occurredAt = formatDate(alert.occurredAt)
  return occurredAt ? `${alert.message} · ${occurredAt}` : alert.message
}

const parseResponseMessage = async (response, fallback) => {
  try {
    const text = await response.text()
    if (!text) {
      return fallback
    }
    const payload = JSON.parse(text)
    return payload?.message || fallback
  } catch {
    return fallback
  }
}

const openExternal = url => {
  if (!url) return
  window.open(url, '_blank', 'noopener,noreferrer')
}

const buildStockContext = currentDetail => {
  const quote = currentDetail?.quote
  if (!quote) return ''
  const name = quote.name ?? ''
  const symbol = quote.symbol ?? ''
  const price = quote.price ?? ''
  const change = quote.change ?? ''
  const changePercent = quote.changePercent ?? ''
  const high = quote.high ?? ''
  const low = quote.low ?? ''
  const peRatio = quote.peRatio ?? ''
  const floatMarketCap = quote.floatMarketCap ?? ''
  const volumeRatio = quote.volumeRatio ?? ''
  const shareholderCount = quote.shareholderCount ?? ''
  const sectorName = quote.sectorName ?? ''
  const timestamp = quote.timestamp ?? ''
  return `股票：${name}（${symbol})\n价格：${price}\n涨跌：${change}（${changePercent}%）\n高：${high} 低：${low}\n市盈率：${peRatio}\n流通市值：${floatMarketCap}\n量比：${volumeRatio}\n股东户数：${shareholderCount}\n所属板块：${sectorName}\n时间：${formatDate(timestamp)}`
}

const chatSymbolKey = computed(() => {
  const quote = detail.value?.quote
  const raw = quote?.symbol || selectedSymbol.value || symbol.value || ''
  return String(raw || '').trim().toLowerCase()
})

const getChatSessionOptions = workspace => (Array.isArray(workspace?.chatSessions) ? workspace.chatSessions : [])
const getChatHistoryKey = workspace => workspace?.selectedChatSession || ''

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
    const response = await fetch(`/api/stocks/chat/sessions?${params.toString()}`, { signal: controller.signal })
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
    const response = await fetch(`/api/stocks/agents/history?${params.toString()}`, { signal: controller.signal })
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

const agentHistoryOptions = computed(() => {
  return agentHistoryList.value.map(item => {
    const id = item.id ?? item.Id
    const symbol = item.symbol ?? item.Symbol
    const createdAt = item.createdAt ?? item.CreatedAt
    const label = `${symbol} - ${formatDate(createdAt)}`
    return { value: id, label }
  })
})

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
  const payload = {
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
  }
  const response = await fetch('/api/stocks/agents/history', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload)
  })
  if (!response.ok) {
    throw new Error('保存多Agent历史失败')
  }
  const saved = await response.json()
  workspace.selectedAgentHistoryId = saved.id ?? saved.Id ?? ''
  workspace.agentHistoryLoaded = false
}

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
    const response = await fetch(`/api/stocks/plans?${params.toString()}`, { signal: controller.signal })
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
    const response = await fetch(`/api/stocks/plans/alerts?${params.toString()}`, { signal: controller.signal })
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

const openTradingPlanDraft = async (symbolKey = currentStockKey.value) => {
  const workspace = getWorkspace(symbolKey) ?? currentWorkspace.value
  if (!workspace?.detail?.quote?.symbol) {
    return
  }

  workspace.planDraftLoading = true
  workspace.planError = ''
  try {
    let historyId = workspace.selectedAgentHistoryId
    if (!historyId) {
      if (!Array.isArray(workspace.agentResults) || !workspace.agentResults.length) {
        throw new Error('请先完成多Agent分析')
      }
      await saveAgentHistory(symbolKey)
      await fetchAgentHistory(symbolKey, { force: true })
      historyId = workspace.selectedAgentHistoryId
    }

    if (!historyId) {
      throw new Error('多Agent历史保存失败')
    }

    const response = await fetch('/api/stocks/plans/draft', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        symbol: workspace.detail.quote.symbol,
        analysisHistoryId: Number(historyId)
      })
    })

    if (!response.ok) {
      throw new Error(await parseResponseMessage(response, '交易计划草稿生成失败'))
    }

    const payload = normalizeTradingPlan(await response.json())
    workspace.planForm = createTradingPlanForm(payload)
    workspace.planModalOpen = true
  } catch (err) {
    workspace.planError = err.message || '交易计划草稿生成失败'
  } finally {
    workspace.planDraftLoading = false
  }
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

const selectAgentHistory = async (symbolKey, value) => {
  const workspace = getWorkspace(symbolKey)
  if (!workspace) {
    return
  }
  workspace.selectedAgentHistoryId = value || ''
  if (!workspace.selectedAgentHistoryId) {
    return
  }
  await loadAgentHistoryDetail(workspace.selectedAgentHistoryId, symbolKey)
}

const buildChatPrompt = content => {
  const context = buildStockContext(detail.value)
  const responseRule = '回答要求：只输出自然语言或Markdown，不要JSON，不要代码块。'
  return context
    ? `你是股票助手，请基于以下股票信息回答用户问题。\n${responseRule}\n\n${context}\n\n用户问题：${content}`
    : `${responseRule}\n${content}`
}

const currentStockLabel = computed(() => {
  const quote = detail.value?.quote
  if (!quote) return '未选择股票'
  return `${quote.name ?? ''}（${quote.symbol ?? ''}）`
})

const getAgentId = agent => agent?.agentId ?? agent?.AgentId ?? ''
const getAgentData = agent => agent?.data ?? agent?.Data ?? null

const parseLevelNumber = value => {
  const number = Number(value)
  return Number.isFinite(number) ? number : null
}

const extractTaggedPriceLevels = (value, patterns) => {
  const text = String(value || '')
  const results = []
  patterns.forEach(pattern => {
    const matches = text.matchAll(pattern)
    for (const match of matches) {
      const number = Number(match[1])
      if (Number.isFinite(number)) {
        results.push(number)
      }
    }
  })
  return results
}

const aiLevels = computed(() => {
  const list = Array.isArray(agentResults.value) ? agentResults.value : []
  const commanderData = getAgentData(list.find(item => getAgentId(item) === 'commander'))
  const trendData = getAgentData(list.find(item => getAgentId(item) === 'trend_analysis'))

  const resistancePatterns = [/突破\s*([0-9]+(?:\.[0-9]+)?)/g, /站上\s*([0-9]+(?:\.[0-9]+)?)/g, /目标\s*([0-9]+(?:\.[0-9]+)?)/g]
  const supportPatterns = [/跌破\s*([0-9]+(?:\.[0-9]+)?)/g, /失守\s*([0-9]+(?:\.[0-9]+)?)/g, /止损\s*([0-9]+(?:\.[0-9]+)?)/g, /支撑\s*([0-9]+(?:\.[0-9]+)?)/g]
  const triggerLevels = extractTaggedPriceLevels(commanderData?.trigger_conditions, resistancePatterns)
  const invalidLevels = extractTaggedPriceLevels(commanderData?.invalid_conditions, supportPatterns)
  const riskLevels = extractTaggedPriceLevels(commanderData?.risk_warning, supportPatterns)
  const analysisLevels = extractTaggedPriceLevels(commanderData?.analysis_opinion, [...resistancePatterns, ...supportPatterns])

  const resistanceFromRecommendation = triggerLevels[0] ?? analysisLevels[0] ?? null
  const supportFromRecommendation = invalidLevels[0] ?? riskLevels[0] ?? null

  const forecast = Array.isArray(trendData?.forecast) ? trendData.forecast : []
  const forecastPrices = forecast
    .map(item => parseLevelNumber(item?.price))
    .filter(price => Number.isFinite(price))

  const resistanceFromTrend = forecastPrices.length ? Math.max(...forecastPrices) : null
  const supportFromTrend = forecastPrices.length ? Math.min(...forecastPrices) : null

  const resistance = resistanceFromRecommendation ?? resistanceFromTrend
  const support = supportFromRecommendation ?? supportFromTrend

  if (!Number.isFinite(resistance) && !Number.isFinite(support)) {
    return null
  }

  return {
    resistance: Number.isFinite(resistance) ? resistance : null,
    support: Number.isFinite(support) ? support : null
  }
})

const marketNewsItems = computed(() => marketNewsBucket.value?.items ?? [])
const marketNewsPreviewItems = computed(() => marketNewsItems.value.slice(0, 3))

const getChangeClass = value => {
  const number = Number(value)
  if (Number.isNaN(number)) return ''
  if (number > 0) return 'text-rise'
  if (number < 0) return 'text-fall'
  return ''
}

const getPriceClass = item => {
  const change = item.changePercent ?? item.ChangePercent ?? 0
  return getChangeClass(change)
}

const getHighClass = item => {
  const price = Number(item.price ?? item.Price)
  const high = Number(item.high ?? item.High)
  if (Number.isNaN(price) || Number.isNaN(high)) return ''
  return high >= price ? 'text-rise' : 'text-fall'
}

const getLowClass = item => {
  const price = Number(item.price ?? item.Price)
  const low = Number(item.low ?? item.Low)
  if (Number.isNaN(price) || Number.isNaN(low)) return ''
  return low <= price ? 'text-fall' : 'text-rise'
}

const formatPercent = value => {
  if (value === null || value === undefined || value === '') return ''
  return `${value}%`
}

const getSortValue = (item, key) => {
  switch (key) {
    case 'id':
      return Number(item.id ?? item.Id ?? 0)
    case 'symbol':
      return String(item.symbol ?? item.Symbol ?? '')
    case 'name':
      return String(item.name ?? item.Name ?? '')
    case 'price':
      return Number(item.price ?? item.Price ?? 0)
    case 'changePercent':
      return Number(item.changePercent ?? item.ChangePercent ?? 0)
    case 'turnoverRate':
      return Number(item.turnoverRate ?? item.TurnoverRate ?? 0)
    case 'peRatio':
      return Number(item.peRatio ?? item.PeRatio ?? 0)
    case 'speed':
      return Number(item.speed ?? item.Speed ?? 0)
    case 'high':
      return Number(item.high ?? item.High ?? 0)
    case 'low':
      return Number(item.low ?? item.Low ?? 0)
    case 'updatedAt':
      return new Date(item.updatedAt ?? item.UpdatedAt ?? 0).getTime()
    default:
      return 0
  }
}

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
  // 保留已有数据，避免页面闪烁

  try {
    const params = buildDetailQuery(targetSymbol)

    const cacheResponse = await fetch(`/api/stocks/detail/cache?${params.toString()}`, { signal: controller.signal })
    if (cacheResponse.ok) {
      const cacheDetail = await cacheResponse.json()
      applyLatestDetail(workspace, requestToken, cacheDetail)
    }

    const response = await fetch(`/api/stocks/detail?${params.toString()}`, { signal: controller.signal })
    if (!response.ok) {
      throw new Error('接口请求失败')
    }
    const liveDetail = await response.json()
    applyLatestDetail(workspace, requestToken, liveDetail)
  } catch (err) {
    if (isAbortError(err)) {
      return
    }
    if (requestToken === workspace.quoteRequestToken) {
      workspace.error = err.message || '请求失败'
    }
  } finally {
    if (requestToken === workspace.quoteRequestToken) {
      workspace.loading = false
      if (workspace.detailAbortController === controller) {
        workspace.detailAbortController = null
      }
    }
  }
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
    const response = await fetch(`/api/stocks/news/impact?${params.toString()}`, { signal: controller.signal })
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
    const response = await fetch('/api/news?level=market', { signal: controller.signal })
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

  const targetWorkspace = workspace
  const requestToken = ++targetWorkspace.localNewsRequestToken
  const controller = replaceAbortController(targetWorkspace.localNewsAbortController)
  targetWorkspace.localNewsAbortController = controller
  targetWorkspace.localNewsLoading = true
  targetWorkspace.localNewsError = ''
  try {
    const buckets = await Promise.all(
      sidebarNewsSections.map(async section => {
        const params = new URLSearchParams({ level: section.key })
        params.set('symbol', symbolValue)
        const response = await fetch(`/api/news?${params.toString()}`, { signal: controller.signal })
        if (!response.ok) {
          throw new Error('本地新闻加载失败')
        }
        const payload = await response.json()
        return [section.key, normalizeNewsBucket(section.key, payload)]
      })
    )

    if (requestToken !== targetWorkspace.localNewsRequestToken) {
      return
    }

    targetWorkspace.localNewsBuckets = {
      ...targetWorkspace.localNewsBuckets,
      stock: Object.fromEntries(buckets).stock ?? null,
      sector: Object.fromEntries(buckets).sector ?? null
    }
    targetWorkspace.localNewsLoaded = true
  } catch (err) {
    if (isAbortError(err)) {
      return
    }
    targetWorkspace.localNewsError = err.message || '本地新闻加载失败'
    targetWorkspace.localNewsBuckets = {
      ...targetWorkspace.localNewsBuckets,
      stock: null,
      sector: null
    }
    targetWorkspace.localNewsLoaded = false
  } finally {
    if (requestToken === targetWorkspace.localNewsRequestToken) {
      targetWorkspace.localNewsLoading = false
      if (targetWorkspace.localNewsAbortController === controller) {
        targetWorkspace.localNewsAbortController = null
      }
    }
  }
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

const searchStocks = async query => {
  searchLoading.value = true
  searchError.value = ''
  try {
    const params = new URLSearchParams({ q: query })
    const response = await fetch(`/api/stocks/search?${params.toString()}`)
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

const fetchSources = async () => {
  try {
    const response = await fetch('/api/stocks/sources')
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
    const response = await fetch('/api/stocks/history')
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

watch(interval, value => {
  localStorage.setItem('stock_interval', value)
  if (symbol.value.trim()) {
    fetchQuote()
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

watch(chatSymbolKey, value => {
  if (!value) {
    return
  }
  fetchChatSessions(value)
  fetchAgentHistory(value)
})

watch(historyRefreshSeconds, value => {
  localStorage.setItem('stock_history_refresh_seconds', String(value))
  setupHistoryRefresh()
})

watch(historyAutoRefresh, value => {
  localStorage.setItem('stock_history_auto_refresh', String(value))
  setupHistoryRefresh()
})

onMounted(() => {
  fetchSources()
  setupRefresh()
  fetchHistory()
  fetchMarketNews()
  refreshTradingPlanBoard()
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
  })
  rootWorkspace.detailAbortController?.abort()
  rootWorkspace.newsImpactAbortController?.abort()
  rootWorkspace.localNewsAbortController?.abort()
  rootWorkspace.chatSessionsAbortController?.abort()
  rootWorkspace.agentHistoryAbortController?.abort()
  rootWorkspace.planListAbortController?.abort()
  rootWorkspace.planAlertsAbortController?.abort()
  window.removeEventListener('click', closeContextMenu)
})

watch(monochromeMode, value => {
  localStorage.setItem('stock_monochrome_mode', String(value))
})

watch(copilotPanelOpen, value => {
  localStorage.setItem('stock_copilot_panel_open', String(value))
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
        <p class="muted panel-subtitle">左侧聚焦行情与图表，右侧收纳 AI 对话和事件信号。</p>
      </div>
      <div class="panel-actions">
        <button class="mode-toggle" @click="monochromeMode = !monochromeMode">
          {{ monochromeMode ? '彩色模式' : '黑白模式' }}
        </button>
        <button class="mode-toggle focus-toggle" @click="copilotPanelOpen = !copilotPanelOpen">
          {{ copilotPanelOpen ? '专注模式' : '显示 AI 侧栏' }}
        </button>
      </div>
    </div>
    <div class="compact-toolbar">
      <section class="toolbar-shell sticky-toolbar">
        <div class="toolbar-main-row">
          <div class="toolbar-title">
            <strong>标的查询</strong>
            <span class="muted">顶部快速切换，不再占用图表纵向空间。</span>
          </div>

          <div class="field search-field toolbar-search-field">
            <input
              v-model="symbol"
              placeholder="输入股票代码/名称/拼音缩写"
              @input="onSymbolInput"
              @keydown.enter.prevent="onSymbolEnter"
            />
            <button @click="fetchQuote" :disabled="isBlockingQuoteLoad">查询</button>
            <div v-if="searchOpen" class="search-dropdown">
              <div class="search-modal-header">
                <span>搜索结果</span>
                <button class="close-btn" @click="closeSearch">关闭</button>
              </div>
              <p v-if="searchError" class="muted">{{ searchError }}</p>
              <p v-else-if="searchLoading" class="muted">搜索中...</p>
              <ul v-else class="search-list">
                <li
                  v-for="item in searchResults"
                  :key="item.symbol || item.Symbol"
                  @click="selectSearchResult(item)"
                >
                  <div class="result-name">{{ item.name ?? item.Name }}</div>
                  <div class="result-code">{{ item.symbol ?? item.Symbol }}</div>
                </li>
              </ul>
              <p v-if="!searchLoading && !searchError && !searchResults.length" class="muted">暂无匹配结果</p>
            </div>
          </div>

          <div class="toolbar-settings">
            <div class="toolbar-select-group">
              <label class="muted">来源</label>
              <select v-model="selectedSource">
                <option value="">自动</option>
                <option v-for="item in sources" :key="item" :value="item">{{ item }}</option>
              </select>
            </div>

            <div class="toolbar-select-group narrow-group">
              <label class="muted">刷新</label>
              <input type="number" min="5" v-model.number="refreshSeconds" />
            </div>

            <label class="muted inline-check toolbar-check">
              <input type="checkbox" v-model="autoRefresh" /> 自动
            </label>
          </div>
        </div>

        <div class="toolbar-sub-row">
          <p class="muted toolbar-status">
            数据刷新：{{ autoRefresh ? `每 ${refreshSeconds} 秒` : '手动刷新' }}
          </p>
          <p v-if="error" class="muted toolbar-status error-text">{{ error }}</p>
          <p v-else-if="isBlockingQuoteLoad" class="muted toolbar-status">查询中...</p>
          <p v-else-if="isBackgroundQuoteRefresh" class="muted toolbar-status">后台刷新中...</p>

          <div class="toolbar-history-actions">
            <span class="muted">历史：{{ historyAutoRefresh ? `每 ${historyRefreshSeconds} 秒` : '手动' }}</span>
            <select v-model.number="historyRefreshSeconds">
              <option :value="10">10 秒</option>
              <option :value="15">15 秒</option>
              <option :value="30">30 秒</option>
              <option :value="60">60 秒</option>
            </select>
            <label class="muted inline-check toolbar-check">
              <input type="checkbox" v-model="historyAutoRefresh" /> 自动更新
            </label>
            <button @click="refreshHistory" :disabled="historyLoading">刷新历史</button>
          </div>
        </div>

        <div class="history-ribbon">
          <p class="muted history-ribbon-title">最近查询</p>
          <div v-if="historyList.length" class="history-chip-row">
            <button
              v-for="item in sortedHistoryList"
              :key="item.id || item.Id"
              class="history-chip"
              @click="applyHistorySymbol(item)"
              @contextmenu="openContextMenu($event, item)"
            >
              <span>{{ item.name ?? item.Name }}</span>
              <strong>{{ item.symbol ?? item.Symbol }}</strong>
              <small :class="getChangeClass(item.changePercent ?? item.ChangePercent)">
                {{ formatPercent(item.changePercent ?? item.ChangePercent) || '0%' }}
              </small>
            </button>
          </div>
          <p v-else class="muted">暂无历史数据。</p>
          <p v-if="historyError" class="muted error-text">{{ historyError }}</p>
          <p v-if="historyLoading && !historyList.length" class="muted">历史数据刷新中...</p>
        </div>

        <div
          v-if="contextMenu.visible"
          class="context-menu"
          :style="{ left: `${contextMenu.x}px`, top: `${contextMenu.y}px` }"
        >
          <button @click="deleteHistoryItem">删除</button>
        </div>
      </section>
    </div>

    <section class="market-news-panel" :class="{ empty: !detail && !marketNewsLoading && !marketNewsError && !marketNewsItems.length }">
      <div class="market-news-header">
        <div>
          <p class="market-news-kicker">Market Wire</p>
          <h3>大盘资讯</h3>
        </div>
        <div class="market-news-actions">
          <button class="market-news-button" @click="fetchMarketNews({ force: true })" :disabled="marketNewsLoading">刷新</button>
          <button class="market-news-button" @click="openMarketNewsModal" :disabled="!marketNewsItems.length">展开阅读</button>
        </div>
      </div>

      <p v-if="marketNewsError" class="muted error-text">{{ marketNewsError }}</p>
      <p v-else-if="marketNewsLoading" class="muted">大盘资讯加载中...</p>
      <div v-else-if="marketNewsItems.length" class="market-news-preview-list">
        <article v-for="item in marketNewsPreviewItems" :key="`market-${item.title}-${item.publishTime}`" class="market-news-item">
          <span class="impact-tag" :class="getImpactClass(item.sentiment)">{{ item.sentiment }}</span>
          <a v-if="item.url" :href="item.url" target="_blank" rel="noreferrer">
            {{ getLocalNewsHeadline(item) }}
          </a>
          <span v-else>{{ getLocalNewsHeadline(item) }}</span>
          <small v-if="item.translatedTitle && item.translatedTitle !== item.title">原题：{{ item.title }}</small>
          <div v-if="item.aiTags?.length || item.aiTarget" class="local-news-meta-row">
            <span v-if="item.aiTarget" class="local-news-target">{{ item.aiTarget }}</span>
            <span v-for="tag in item.aiTags" :key="`market-tag-${item.title}-${tag}`" class="local-news-tag">{{ tag }}</span>
          </div>
          <small>{{ item.source }} · {{ formatDate(item.publishTime) }}</small>
        </article>
      </div>
      <p v-else class="muted">暂无可展示的大盘资讯。</p>
    </section>

    <div v-if="marketNewsModalOpen" class="market-news-modal-backdrop" @click.self="closeMarketNewsModal">
      <section class="market-news-modal" role="dialog" aria-modal="true" aria-label="大盘资讯详情">
        <div class="market-news-header">
          <div>
            <p class="market-news-kicker">Expanded Reader</p>
            <h3>大盘资讯详情</h3>
          </div>
          <button class="market-news-button" @click="closeMarketNewsModal">关闭</button>
        </div>
        <div class="market-news-modal-list">
          <article v-for="item in marketNewsItems" :key="`market-modal-${item.title}-${item.publishTime}`" class="market-news-item">
            <span class="impact-tag" :class="getImpactClass(item.sentiment)">{{ item.sentiment }}</span>
            <a v-if="item.url" :href="item.url" target="_blank" rel="noreferrer">
              {{ getLocalNewsHeadline(item) }}
            </a>
            <span v-else>{{ getLocalNewsHeadline(item) }}</span>
            <small v-if="item.translatedTitle && item.translatedTitle !== item.title">原题：{{ item.title }}</small>
            <div v-if="item.aiTags?.length || item.aiTarget" class="local-news-meta-row">
              <span v-if="item.aiTarget" class="local-news-target">{{ item.aiTarget }}</span>
              <span v-for="tag in item.aiTags" :key="`market-modal-tag-${item.title}-${tag}`" class="local-news-tag">{{ tag }}</span>
            </div>
            <small>{{ item.source }} · {{ formatDate(item.publishTime) }}</small>
          </article>
        </div>
      </section>
    </div>

    <div class="workspace-grid" :class="{ focused: !copilotPanelOpen }">
      <TerminalView :quote="detail?.quote ?? null" :monochrome="monochromeMode">
        <template #summary>
          <div v-if="detail" class="terminal-summary-grid">
            <div class="quote-card">
              <p><strong>{{ detail.quote.name }}</strong>（{{ detail.quote.symbol }}）</p>
              <p>当前价：{{ detail.quote.price }}</p>
              <p>涨跌：{{ detail.quote.change }}（{{ detail.quote.changePercent }}%）</p>
              <p class="muted">更新时间：{{ formatDate(detail.quote.timestamp) }}</p>
            </div>

            <div class="quote-card">
              <div class="quote-card-header">
                <h4>基本面快照</h4>
                <span class="muted">Step 3</span>
              </div>
              <p>流通市值：{{ detail.quote.floatMarketCap ? `${(Number(detail.quote.floatMarketCap) / 100000000).toFixed(2)} 亿` : '-' }}</p>
              <p>市盈率：{{ detail.quote.peRatio ?? '-' }}</p>
              <p>量比：{{ detail.quote.volumeRatio ?? '-' }}</p>
              <p>股东户数：{{ detail.quote.shareholderCount ? Number(detail.quote.shareholderCount).toLocaleString('zh-CN') : '-' }}</p>
              <p>所属板块：{{ detail.quote.sectorName || '-' }}</p>
              <p v-if="detail.fundamentalSnapshot?.updatedAt" class="muted">快照刷新：{{ formatDate(detail.fundamentalSnapshot.updatedAt) }}</p>
              <ul v-if="detail.fundamentalSnapshot?.facts?.length" class="fundamental-facts">
                <li v-for="fact in detail.fundamentalSnapshot.facts.slice(0, 8)" :key="`fundamental-${fact.label}-${fact.value}`">
                  <strong>{{ fact.label }}：</strong>{{ fact.value }}
                </li>
              </ul>
            </div>

            <div class="quote-card tape-card">
              <div class="quote-card-header">
                <h4>盘中消息带</h4>
                <span class="muted">{{ detail.messages.length }} 条</span>
              </div>
              <ul v-if="detail.messages.length" class="messages">
                <li
                  v-for="item in detail.messages"
                  :key="`${item.title}-${item.publishedAt ?? item.PublishedAt ?? ''}`"
                  :class="{ clickable: !!(item.url ?? item.Url) }"
                  @click="openExternal(item.url ?? item.Url)"
                >
                  <span>{{ item.title }}</span>
                  <small>{{ item.source }} · {{ formatDate(item.publishedAt ?? item.PublishedAt) }}</small>
                </li>
              </ul>
              <p v-else class="muted">暂无盘中消息。</p>
            </div>
          </div>

          <div v-else class="terminal-empty">
            <h4>等待加载股票</h4>
            <p>主视区只保留价格、分时、K 线、量价指标与消息带。</p>
            <p class="muted">数据来源：腾讯 / 新浪 / 百度（后端爬虫占位）</p>
          </div>
        </template>

        <template #chart>
          <StockCharts
            v-if="detail"
            :k-lines="detail.kLines"
            :minute-lines="detail.minuteLines"
            :base-price="Number(detail.quote.price) - Number(detail.quote.change)"
            :ai-levels="aiLevels"
            :interval="interval"
            @update:interval="interval = $event"
          />
          <div v-else class="chart-placeholder">
            <p>查询股票后，这里会以终端视图显示 K 线、分时、成交量和均线。</p>
          </div>
        </template>
      </TerminalView>

      <CopilotPanel :open="copilotPanelOpen" :current-stock-label="currentStockLabel" @toggle="copilotPanelOpen = !copilotPanelOpen">
        <section class="copilot-card trading-plan-card trading-plan-board-card">
          <div class="trading-plan-header">
            <div>
              <h3>交易计划总览</h3>
              <p class="muted">不选股也能直接查看最近交易计划，并快速跳转到对应标的。</p>
            </div>
            <button class="market-news-button plan-refresh-button" @click="refreshTradingPlanBoard(true)" :disabled="rootWorkspace.planListLoading || rootWorkspace.planAlertsLoading">
              刷新
            </button>
          </div>

          <p v-if="rootWorkspace.planError" class="muted error">{{ rootWorkspace.planError }}</p>
          <p v-else-if="rootWorkspace.planListLoading && !rootWorkspace.planList.length" class="muted">加载中...</p>
          <ul v-else-if="rootWorkspace.planList.length" class="plan-list plan-board-list">
            <li v-for="item in rootWorkspace.planList" :key="`plan-board-${item.id}`" class="plan-item plan-item-compact">
              <div class="plan-item-header">
                <div class="plan-item-title">
                  <strong>{{ item.name }} · {{ item.status }}</strong>
                  <small>{{ item.symbol }}</small>
                </div>
                <button class="plan-link-button" @click="jumpToPlanSymbol(item.symbol)">查看股票</button>
              </div>
              <p>{{ item.analysisSummary || item.expectedCatalyst || '等待补充计划摘要' }}</p>
              <div
                v-if="getLatestPlanAlert(rootWorkspace, item.id)"
                class="plan-alert"
                :class="getPlanAlertClass(getLatestPlanAlert(rootWorkspace, item.id).severity)"
              >
                <strong>{{ getLatestPlanAlert(rootWorkspace, item.id).eventType }}</strong>
                <span>{{ formatPlanAlertSummary(getLatestPlanAlert(rootWorkspace, item.id)) }}</span>
              </div>
              <div class="plan-pill-row">
                <span class="plan-pill">方向 {{ item.direction }}</span>
                <span class="plan-pill">触发 {{ formatPlanPrice(item.triggerPrice) }}</span>
                <span class="plan-pill">止损 {{ formatPlanPrice(item.stopLossPrice) }}</span>
                <span class="plan-pill">止盈 {{ formatPlanPrice(item.takeProfitPrice) }}</span>
                <span class="plan-pill">目标 {{ formatPlanPrice(item.targetPrice) }}</span>
              </div>
            </li>
          </ul>
          <p v-else class="muted">暂无交易计划，可从 commander 分析一键起草。</p>
        </section>

        <template v-if="sidebarWorkspaces.length">
          <div
            v-for="workspace in sidebarWorkspaces"
            :key="`sidebar-${workspace.symbolKey}`"
            v-show="workspace.symbolKey === currentStockKey"
            class="sidebar-workspace"
          >
            <section class="copilot-card news-impact">
              <div class="news-impact-header">
                <div>
                  <h3>资讯影响</h3>
                  <p class="muted">事件信号在右侧集中查看，不遮挡 K 线。</p>
                </div>
                <button @click="fetchNewsImpact(workspace.symbolKey, { force: true })" :disabled="workspace.newsImpactLoading || !workspace.detail">刷新</button>
              </div>

              <div v-if="workspace.detail" class="news-impact-content">
                <p v-if="workspace.newsImpactError" class="muted error">{{ workspace.newsImpactError }}</p>
                <p v-else-if="workspace.newsImpactLoading" class="muted">分析中...</p>
                <div v-else-if="workspace.newsImpact" class="news-impact-summary">
                  <span>利好 {{ workspace.newsImpact.summary.positive }}</span>
                  <span>中性 {{ workspace.newsImpact.summary.neutral }}</span>
                  <span>利空 {{ workspace.newsImpact.summary.negative }}</span>
                  <span class="overall">总体：{{ workspace.newsImpact.summary.overall }}</span>
                </div>
                <p v-else class="muted">资讯影响待生成。</p>

                <div class="news-buckets">
                  <article v-for="section in sidebarNewsSections" :key="section.key" class="news-bucket-card">
                    <div class="news-bucket-header">
                      <strong>{{ section.title }}</strong>
                      <span v-if="section.key === 'sector' && workspace.localNewsBuckets[section.key]?.sectorName" class="muted">
                        {{ workspace.localNewsBuckets[section.key]?.sectorName }}
                      </span>
                    </div>

                    <p v-if="workspace.localNewsLoading" class="muted">加载中...</p>
                    <ul v-else-if="workspace.localNewsBuckets[section.key]?.items?.length" class="news-bucket-list">
                      <li v-for="item in workspace.localNewsBuckets[section.key].items" :key="`${section.key}-${item.title}-${item.publishTime}`">
                        <span class="impact-tag" :class="getImpactClass(item.sentiment)">{{ item.sentiment }}</span>
                        <a v-if="item.url ?? item.Url" :href="item.url ?? item.Url" target="_blank" rel="noreferrer">
                          {{ getLocalNewsHeadline(item) }}
                        </a>
                        <span v-else>{{ getLocalNewsHeadline(item) }}</span>
                        <small v-if="item.translatedTitle && item.translatedTitle !== item.title">原题：{{ item.title }}</small>
                        <div v-if="item.aiTags?.length || item.aiTarget" class="local-news-meta-row">
                          <span v-if="item.aiTarget" class="local-news-target">{{ item.aiTarget }}</span>
                          <span v-for="tag in item.aiTags" :key="`${section.key}-${item.title}-${tag}`" class="local-news-tag">{{ tag }}</span>
                        </div>
                        <small>
                          {{ item.source }} · {{ formatDate(item.publishTime) }}
                        </small>
                      </li>
                    </ul>
                    <p v-else class="muted">暂无匹配资讯。</p>
                  </article>
                </div>

                <p v-if="workspace.localNewsError" class="muted error">{{ workspace.localNewsError }}</p>

                <ul v-if="workspace.newsImpact?.events?.length" class="news-impact-list">
                  <li v-for="item in workspace.newsImpact.events.slice(0, 6)" :key="item.title">
                    <span class="impact-tag" :class="getImpactClass(item.category)">{{ item.category }}</span>
                    <span class="impact-title">{{ item.title }}</span>
                    <span class="impact-score">{{ formatImpactScore(item.impactScore) }}</span>
                  </li>
                </ul>
                <p v-else-if="!workspace.newsImpactLoading" class="muted">暂无资讯影响数据。</p>
              </div>

              <p v-else class="muted">选择股票后在此查看事件影响。</p>
            </section>

            <StockAgentPanels
              :agents="workspace.agentResults"
              :loading="workspace.agentLoading"
              :error="workspace.agentError"
              :last-updated="workspace.agentUpdatedAt"
              :history-options="workspace.agentHistoryList.map(item => ({ value: item.id ?? item.Id, label: `${item.symbol ?? item.Symbol} - ${formatDate(item.createdAt ?? item.CreatedAt)}` }))"
              :selected-history-id="workspace.selectedAgentHistoryId"
              :history-loading="workspace.agentHistoryLoading"
              :history-error="workspace.agentHistoryError"
              @select-history="selectAgentHistory(workspace.symbolKey, $event)"
              @run="runAgents(workspace.symbolKey, $event)"
              @draft-plan="openTradingPlanDraft(workspace.symbolKey)"
            />

            <section class="copilot-card trading-plan-card">
              <div class="trading-plan-header">
                <div>
                  <h3>当前交易计划</h3>
                  <p class="muted">当前股票的全部交易计划都可在这里编辑或删除。</p>
                </div>
                <button class="market-news-button plan-refresh-button" @click="refreshTradingPlanSection(workspace.symbolKey, true)" :disabled="workspace.planListLoading || workspace.planAlertsLoading || !workspace.detail">
                  刷新
                </button>
              </div>

              <p v-if="workspace.planError" class="muted error">{{ workspace.planError }}</p>
              <p v-else-if="workspace.planListLoading && !workspace.planList.length" class="muted">加载中...</p>
              <ul v-else-if="workspace.planList.length" class="plan-list">
                <li v-for="item in workspace.planList" :key="`plan-${item.id}`" class="plan-item">
                  <div class="plan-item-header">
                    <div class="plan-item-title">
                      <strong>{{ item.name }} · {{ item.status }}</strong>
                      <small class="muted">{{ formatDate(item.updatedAt || item.createdAt) }}</small>
                    </div>
                    <div class="plan-item-actions">
                      <button v-if="item.status === 'Pending'" class="plan-link-button" @click="editTradingPlan(workspace.symbolKey, item)">编辑</button>
                      <button class="plan-danger-button" @click="deleteTradingPlan(workspace.symbolKey, item)" :disabled="deletingPlanId === String(item.id)">
                        {{ deletingPlanId === String(item.id) ? '删除中...' : '删除' }}
                      </button>
                    </div>
                  </div>
                  <p>{{ item.analysisSummary || item.expectedCatalyst || '等待补充计划摘要' }}</p>
                  <div
                    v-if="getLatestPlanAlert(workspace, item.id)"
                    class="plan-alert"
                    :class="getPlanAlertClass(getLatestPlanAlert(workspace, item.id).severity)"
                  >
                    <strong>{{ getLatestPlanAlert(workspace, item.id).eventType }}</strong>
                    <span>{{ formatPlanAlertSummary(getLatestPlanAlert(workspace, item.id)) }}</span>
                  </div>
                  <div class="plan-pill-row">
                    <span class="plan-pill">方向 {{ item.direction }}</span>
                    <span class="plan-pill">触发 {{ formatPlanPrice(item.triggerPrice) }}</span>
                    <span class="plan-pill">失效 {{ formatPlanPrice(item.invalidPrice) }}</span>
                    <span class="plan-pill">止损 {{ formatPlanPrice(item.stopLossPrice) }}</span>
                    <span class="plan-pill">止盈 {{ formatPlanPrice(item.takeProfitPrice) }}</span>
                    <span class="plan-pill">目标 {{ formatPlanPrice(item.targetPrice) }}</span>
                  </div>
                  <small>{{ item.riskLimits || '风险摘要待补充' }}</small>
                </li>
              </ul>
              <p v-else class="muted">暂无交易计划，可从 commander 分析一键起草。</p>
            </section>

            <div class="copilot-card">
              <ChatWindow
                v-if="workspace.detail"
                :ref="setChatRef(workspace.symbolKey)"
                title="股票助手"
                :build-prompt="buildChatPrompt"
                :history-key="getChatHistoryKey(workspace)"
                :enable-history="true"
                :history-adapter="chatHistoryAdapter"
                expandable
                expanded-storage-key="stock_chat_expanded"
                placeholder="请输入关于该股票的问题"
                empty-text="可以询问该股票的走势、风险或盘面解读。"
                max-height="320px"
                expanded-height="600px"
              >
                <template #header-extra>
                  <div class="chat-session">
                    <p class="muted">当前：{{ workspace.detail?.quote?.name ?? '' }}（{{ workspace.detail?.quote?.symbol ?? '' }}）</p>
                    <select v-model="workspace.selectedChatSession" :disabled="!getChatSessionOptions(workspace).length">
                      <option v-for="item in getChatSessionOptions(workspace)" :key="item.key" :value="item.key">
                        {{ item.label }}
                      </option>
                    </select>
                    <button class="chat-session-new" @click="startNewChat(workspace.symbolKey)" :disabled="!workspace.symbolKey">
                      新建对话
                    </button>
                  </div>
                </template>
              </ChatWindow>
            </div>

            <div v-if="workspace.planModalOpen && workspace.planForm" class="plan-modal-backdrop" @click.self="closeTradingPlanModal(workspace.symbolKey)">
              <section class="plan-modal" role="dialog" aria-modal="true" aria-label="交易计划草稿">
                <div class="search-modal-header">
                  <div>
                    <strong>交易计划草稿</strong>
                    <p class="muted">后端基于 commander 历史生成，用户确认后才会入库。</p>
                  </div>
                  <button class="market-news-button" @click="closeTradingPlanModal(workspace.symbolKey)">关闭</button>
                </div>

                <p v-if="workspace.planError" class="muted error">{{ workspace.planError }}</p>

                <div class="plan-form-grid">
                  <label class="plan-field">
                    <span>股票</span>
                    <input v-model="workspace.planForm.symbol" disabled>
                  </label>
                  <label class="plan-field">
                    <span>名称</span>
                    <input v-model="workspace.planForm.name">
                  </label>
                  <label class="plan-field">
                    <span>方向</span>
                    <select v-model="workspace.planForm.direction">
                      <option value="Long">Long</option>
                      <option value="Short">Short</option>
                    </select>
                  </label>
                  <label class="plan-field">
                    <span>触发价</span>
                    <input v-model="workspace.planForm.triggerPrice" type="number" step="0.01" placeholder="可为空，后补录">
                  </label>
                  <label class="plan-field">
                    <span>失效价</span>
                    <input v-model="workspace.planForm.invalidPrice" type="number" step="0.01" placeholder="可为空，后补录">
                  </label>
                  <label class="plan-field">
                    <span>止损价</span>
                    <input v-model="workspace.planForm.stopLossPrice" type="number" step="0.01" placeholder="可为空">
                  </label>
                  <label class="plan-field">
                    <span>止盈价</span>
                    <input v-model="workspace.planForm.takeProfitPrice" type="number" step="0.01" placeholder="优先取指挥/机构目标">
                  </label>
                  <label class="plan-field">
                    <span>目标价</span>
                    <input v-model="workspace.planForm.targetPrice" type="number" step="0.01" placeholder="优先取指挥/趋势目标">
                  </label>
                  <label class="plan-field plan-field-wide">
                    <span>预期催化</span>
                    <textarea v-model="workspace.planForm.expectedCatalyst" rows="2"></textarea>
                  </label>
                  <label class="plan-field plan-field-wide">
                    <span>失效条件</span>
                    <textarea v-model="workspace.planForm.invalidConditions" rows="2"></textarea>
                  </label>
                  <label class="plan-field plan-field-wide">
                    <span>风险上限</span>
                    <textarea v-model="workspace.planForm.riskLimits" rows="2"></textarea>
                  </label>
                  <label class="plan-field plan-field-wide">
                    <span>分析摘要</span>
                    <textarea v-model="workspace.planForm.analysisSummary" rows="3"></textarea>
                  </label>
                  <label class="plan-field plan-field-wide">
                    <span>用户备注</span>
                    <textarea v-model="workspace.planForm.userNote" rows="2" placeholder="可补充执行纪律、仓位、观察点"></textarea>
                  </label>
                </div>

                <div class="plan-modal-actions">
                  <span class="muted">{{ workspace.planForm.id ? `编辑计划 #${workspace.planForm.id}` : `AnalysisHistory #${workspace.planForm.analysisHistoryId}` }}</span>
                  <div class="plan-modal-buttons">
                    <button class="market-news-button" @click="closeTradingPlanModal(workspace.symbolKey)">取消</button>
                    <button class="plan-save-button" @click="saveTradingPlan(workspace.symbolKey)" :disabled="workspace.planSaving || workspace.planDraftLoading">
                      {{ workspace.planSaving ? '保存中...' : (workspace.planForm.id ? '保存修改' : '保存为 Pending 计划') }}
                    </button>
                  </div>
                </div>
              </section>
            </div>
          </div>
        </template>

        <div v-else class="copilot-card">
          <div class="terminal-empty compact-empty">
            <h4>AI Copilot 已待命</h4>
            <p>先在左侧加载股票，再在这里查看事件信号或发起对话。</p>
          </div>
        </div>
      </CopilotPanel>
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

.focus-toggle {
  background: #0f172a;
  color: #f8fafc;
}

.toolbar-shell,
.quote-card-header {
  display: flex;
  display: grid;
  gap: 0.75rem;
}

.compact-toolbar {
  position: relative;
}

.sticky-toolbar {
  position: sticky;
  top: 0;
  z-index: 30;
  padding: 0.85rem 1rem;
  border-radius: 18px;
  border: 1px solid rgba(148, 163, 184, 0.18);
  background: rgba(255, 255, 255, 0.88);
  backdrop-filter: blur(16px);
  box-shadow: 0 12px 28px rgba(15, 23, 42, 0.08);
}

.toolbar-main-row,
.toolbar-sub-row {
  display: grid;
  grid-template-columns: auto minmax(320px, 1fr) auto;
  gap: 0.75rem;
  align-items: center;
}

.toolbar-title {
  display: grid;
  gap: 0.15rem;
  min-width: 0;
}

.toolbar-title strong {
  color: #0f172a;
}

.toolbar-search-field {
  margin-bottom: 0;
}

.toolbar-search-field input {
  min-width: 0;
  flex: 1 1 260px;
}

.toolbar-settings {
  display: flex;
  align-items: center;
  gap: 0.65rem;
  flex-wrap: wrap;
  justify-content: flex-end;
}

.toolbar-select-group {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
}

.toolbar-select-group select,
.toolbar-select-group input {
  width: auto;
  min-width: 86px;
}

.narrow-group input {
  max-width: 84px;
}

.toolbar-check {
  white-space: nowrap;
}

.toolbar-sub-row {
  grid-template-columns: auto 1fr auto;
}

.toolbar-status {
  margin: 0;
}

.toolbar-history-actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
  justify-content: flex-end;
}

.history-ribbon {
  display: grid;
  gap: 0.35rem;
}

.history-ribbon-title {
  margin: 0;
}

.history-chip-row {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(124px, 1fr));
  gap: 0.5rem;
  max-height: 10.5rem;
  overflow-y: auto;
  padding-right: 0.25rem;
}

.history-chip {
  display: grid;
  gap: 0.1rem;
  min-width: 124px;
  padding: 0.5rem 0.65rem;
  border-radius: 14px;
  border: 1px solid rgba(148, 163, 184, 0.2);
  background: rgba(248, 250, 252, 0.96);
  text-align: left;
}

.history-chip span,
.history-chip strong,
.history-chip small {
  display: block;
}

.history-chip strong {
  color: #0f172a;
}

.error-text {
  color: #b91c1c;
}

.compact-field {
  margin-bottom: 0;
}

.inline-check {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
}

.workspace-grid {
  display: grid;
  gap: 1rem;
  grid-template-columns: minmax(0, 1.75fr) minmax(320px, 0.95fr);
  align-items: start;
  min-height: calc(100vh - 238px);
}

.workspace-grid.focused {
  grid-template-columns: minmax(0, 1fr) 280px;
}

.terminal-summary-grid {
  display: grid;
  gap: 1rem;
  grid-template-columns: repeat(3, minmax(0, 1fr));
}

.market-news-panel {
  display: grid;
  gap: 0.85rem;
  padding: 0.95rem 1.1rem;
  border-radius: 20px;
  border: 1px solid rgba(14, 165, 233, 0.18);
  background:
    radial-gradient(circle at top left, rgba(14, 165, 233, 0.14), transparent 28%),
    linear-gradient(135deg, rgba(15, 23, 42, 0.97), rgba(15, 23, 42, 0.9));
  box-shadow: 0 18px 42px rgba(15, 23, 42, 0.14);
}

.market-news-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
}

.market-news-kicker {
  margin: 0 0 0.2rem;
  font-size: 0.7rem;
  letter-spacing: 0.16em;
  text-transform: uppercase;
  color: #7dd3fc;
}

.market-news-header h3 {
  margin: 0;
  color: #f8fafc;
  font-size: 1.1rem;
}

.market-news-actions {
  display: flex;
  gap: 0.55rem;
}

.market-news-button {
  border: 1px solid rgba(148, 163, 184, 0.3);
  border-radius: 999px;
  padding: 0.35rem 0.75rem;
  background: rgba(255, 255, 255, 0.1);
  color: #e2e8f0;
  cursor: pointer;
}

.market-news-button:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}

.market-news-preview-list,
.market-news-modal-list {
  display: grid;
  gap: 0.85rem;
}

.market-news-item {
  display: grid;
  gap: 0.18rem;
  min-width: 0;
  padding: 0.8rem 0.9rem;
  border-radius: 16px;
  background: rgba(255, 255, 255, 0.06);
  border: 1px solid rgba(148, 163, 184, 0.12);
}

.market-news-item a,
.market-news-item span {
  color: #f8fafc;
  font-weight: 600;
  text-decoration: none;
}

.market-news-item small {
  color: #94a3b8;
}

.market-news-modal-backdrop {
  position: fixed;
  inset: 0;
  z-index: 60;
  display: grid;
  place-items: center;
  padding: 1rem;
  background: rgba(15, 23, 42, 0.62);
  backdrop-filter: blur(10px);
  overflow: hidden;
}

.market-news-modal {
  display: grid;
  grid-template-rows: auto minmax(0, 1fr);
  gap: 1rem;
  width: min(960px, 100%);
  height: min(78vh, 820px);
  max-height: min(78vh, 820px);
  padding: 1.2rem;
  border-radius: 24px;
  border: 1px solid rgba(148, 163, 184, 0.18);
  background: linear-gradient(160deg, rgba(15, 23, 42, 0.98), rgba(15, 23, 42, 0.94));
  overflow: hidden;
}

.market-news-modal-list {
  min-height: 0;
  overflow-y: auto;
  padding-right: 0.35rem;
}

.market-news-modal-list::-webkit-scrollbar {
  width: 8px;
}

.market-news-modal-list::-webkit-scrollbar-thumb {
  border-radius: 999px;
  background: rgba(148, 163, 184, 0.45);
}

.market-news-modal-list::-webkit-scrollbar-track {
  background: rgba(15, 23, 42, 0.12);
}

.local-news-meta-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem;
}

.local-news-tag,
.local-news-target {
  display: inline-flex;
  align-items: center;
  border-radius: 999px;
  padding: 0.1rem 0.45rem;
  font-size: 0.72rem;
  line-height: 1.2;
  background: rgba(148, 163, 184, 0.16);
  color: #cbd5e1;
}

.quote-card {
  padding: 1rem;
  border-radius: 16px;
  background: rgba(15, 23, 42, 0.34);
  border: 1px solid rgba(148, 163, 184, 0.12);
}

.quote-card p,
.terminal-empty p {
  margin: 0.2rem 0;
}

.fundamental-facts {
  margin: 0.45rem 0 0;
  padding-left: 1rem;
  display: grid;
  gap: 0.3rem;
}

.fundamental-facts li {
  color: #d9d4c7;
}

.tape-card {
  min-width: 0;
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

.trading-plan-card {
  display: grid;
  gap: 0.85rem;
}

.trading-plan-board-card {
  margin-bottom: 1rem;
}

.trading-plan-header,
.plan-item-header,
.plan-modal-actions,
.plan-modal-buttons {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 0.75rem;
}

.plan-refresh-button {
  color: #0f172a;
  background: rgba(15, 23, 42, 0.08);
}

.plan-board-list {
  max-height: 20rem;
  overflow-y: auto;
  padding-right: 0.2rem;
}

.plan-list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: grid;
  gap: 0.75rem;
}

.plan-item {
  display: grid;
  gap: 0.45rem;
  padding: 0.9rem 1rem;
  border-radius: 16px;
  background: rgba(248, 250, 252, 0.92);
  border: 1px solid rgba(148, 163, 184, 0.18);
}

.plan-item-compact {
  gap: 0.35rem;
}

.plan-item-title {
  display: grid;
  gap: 0.12rem;
}

.plan-item-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.45rem;
}

.plan-item p,
.plan-item small {
  margin: 0;
}

.plan-alert {
  display: grid;
  gap: 0.18rem;
  margin: 0.35rem 0 0.15rem;
  padding: 0.55rem 0.7rem;
  border-radius: 12px;
  border: 1px solid rgba(148, 163, 184, 0.2);
  font-size: 0.82rem;
}

.plan-alert strong {
  font-size: 0.74rem;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

.plan-alert-warning {
  background: rgba(245, 158, 11, 0.12);
  border-color: rgba(245, 158, 11, 0.24);
  color: #92400e;
}

.plan-alert-critical {
  background: rgba(239, 68, 68, 0.12);
  border-color: rgba(239, 68, 68, 0.24);
  color: #991b1b;
}

.plan-alert-info {
  background: rgba(14, 165, 233, 0.12);
  border-color: rgba(14, 165, 233, 0.24);
  color: #0c4a6e;
}

.plan-pill-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.45rem;
}

.plan-pill {
  display: inline-flex;
  align-items: center;
  border-radius: 999px;
  padding: 0.18rem 0.55rem;
  background: rgba(37, 99, 235, 0.08);
  color: #1d4ed8;
  font-size: 0.78rem;
}

.plan-link-button,
.plan-danger-button {
  border: none;
  border-radius: 999px;
  padding: 0.35rem 0.75rem;
  cursor: pointer;
}

.plan-link-button {
  background: rgba(37, 99, 235, 0.12);
  color: #1d4ed8;
}

.plan-danger-button {
  background: rgba(220, 38, 38, 0.12);
  color: #b91c1c;
}

.plan-danger-button:disabled,
.plan-link-button:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.plan-modal-backdrop {
  position: fixed;
  inset: 0;
  z-index: 70;
  display: grid;
  place-items: center;
  padding: 1rem;
  background: rgba(15, 23, 42, 0.58);
  backdrop-filter: blur(10px);
}

.plan-modal {
  display: grid;
  gap: 1rem;
  width: min(920px, 100%);
  max-height: min(82vh, 900px);
  overflow-y: auto;
  padding: 1.2rem;
  border-radius: 24px;
  border: 1px solid rgba(148, 163, 184, 0.18);
  background: linear-gradient(160deg, rgba(255, 255, 255, 0.98), rgba(241, 245, 249, 0.96));
}

.plan-form-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 0.85rem;
}

.plan-field {
  display: grid;
  gap: 0.35rem;
}

.plan-field span {
  color: #334155;
  font-size: 0.82rem;
}

.plan-field input,
.plan-field select,
.plan-field textarea {
  border-radius: 12px;
  border: 1px solid rgba(148, 163, 184, 0.3);
  padding: 0.65rem 0.75rem;
  background: rgba(255, 255, 255, 0.95);
  color: #0f172a;
}

.plan-field-wide {
  grid-column: 1 / -1;
}

.plan-save-button {
  border: none;
  border-radius: 999px;
  padding: 0.55rem 0.95rem;
  background: linear-gradient(135deg, #0f766e, #0891b2);
  color: #f8fafc;
  cursor: pointer;
}

.plan-save-button:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.field {
  gap: 0.75rem;
  flex-wrap: wrap;
}

.field input,
.field select,
.field button {
  border-radius: 10px;
  border: 1px solid rgba(148, 163, 184, 0.4);
  padding: 0.55rem 0.75rem;
  background: rgba(255, 255, 255, 0.9);
}

.field button {
  background: linear-gradient(135deg, #2563eb, #38bdf8);
  color: #ffffff;
  border: none;
  box-shadow: 0 6px 16px rgba(37, 99, 235, 0.25);
}

.field button:disabled {
  background: #94a3b8;
  box-shadow: none;
}

.search-field {
  position: relative;
}

.search-dropdown {
  position: absolute;
  top: calc(100% + 0.5rem);
  left: 0;
  right: 0;
  width: 100%;
  max-width: 560px;
  background: rgba(255, 255, 255, 0.95);
  border-radius: 16px;
  padding: 1rem 1.25rem;
  box-shadow: 0 20px 40px rgba(15, 23, 42, 0.2);
  backdrop-filter: blur(12px);
  border: 1px solid rgba(148, 163, 184, 0.2);
  z-index: 50;
}

.search-modal-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 0.75rem;
  color: #0f172a;
  font-weight: 600;
}

.close-btn {
  background: transparent;
  border: none;
  color: #64748b;
  cursor: pointer;
}

.search-list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: grid;
  gap: 0.5rem;
  max-height: 320px;
  overflow: auto;
}

.search-list li {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.6rem 0.75rem;
  border-radius: 10px;
  background: rgba(248, 250, 252, 0.9);
  border: 1px solid rgba(226, 232, 240, 0.9);
  cursor: pointer;
}

.search-list li:hover {
  background: rgba(226, 232, 240, 0.8);
}

.result-name {
  color: #0f172a;
  font-weight: 500;
}

.result-code {
  color: #475569;
  font-size: 0.85rem;
}

.messages {
  list-style: none;
  padding: 0;
  margin: 0 0 1rem;
}

.messages li {
  padding: 0.25rem 0;
  color: #4b5563;
  font-size: 0.9rem;
}

.chat-session {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.chat-session select {
  border-radius: 8px;
  border: 1px solid rgba(148, 163, 184, 0.4);
  padding: 0.35rem 0.6rem;
  background: #ffffff;
}

.chat-session-new {
  border-radius: 999px;
  border: none;
  padding: 0.3rem 0.75rem;
  background: #e2e8f0;
  color: #1f2937;
  cursor: pointer;
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
.panel.monochrome .field button,
.panel.monochrome .context-menu button {
  background: #6b7280;
  color: #ffffff;
}

.panel.monochrome .field input,
.panel.monochrome .field select,
.panel.monochrome .history,
.panel.monochrome .history-table,
.panel.monochrome .search-dropdown {
  background: #ffffff;
  color: #000000;
}

.panel.monochrome .market-news-button {
  background: rgba(255, 255, 255, 0.88);
  color: #111827;
}

.panel.monochrome .market-news-panel,
.panel.monochrome .market-news-modal,
.panel.monochrome .plan-modal {
  background:
    radial-gradient(circle at top left, rgba(148, 163, 184, 0.12), transparent 28%),
    linear-gradient(135deg, rgba(255, 255, 255, 0.98), rgba(241, 245, 249, 0.94));
}

.panel.monochrome .market-news-item a,
.panel.monochrome .market-news-item span,
.panel.monochrome .market-news-header h3 {
  color: #111827;
}

.panel.monochrome .market-news-kicker {
  color: #0369a1;
}

.panel.monochrome .market-news-item small {
  color: #4b5563;
}

.panel.monochrome .local-news-tag,
.panel.monochrome .local-news-target {
  background: rgba(148, 163, 184, 0.2);
  color: #334155;
}

.panel.monochrome .plan-pill {
  background: rgba(148, 163, 184, 0.18);
  color: #111827;
}

.panel.monochrome .text-rise,
.panel.monochrome .text-fall {
  color: #000000;
  font-weight: 600;
}

.context-menu {
  position: fixed;
  z-index: 60;
  background: rgba(255, 255, 255, 0.98);
  border: 1px solid rgba(148, 163, 184, 0.3);
  border-radius: 10px;
  box-shadow: 0 12px 24px rgba(15, 23, 42, 0.2);
  padding: 0.25rem;
}

.context-menu button {
  background: transparent;
  border: none;
  color: #ef4444;
  padding: 0.4rem 0.8rem;
  cursor: pointer;
}

.messages {
  list-style: none;
  padding: 0;
  margin: 0.65rem 0 0;
  display: grid;
  gap: 0.55rem;
  max-height: 18rem;
  overflow-y: auto;
  padding-right: 0.25rem;
}

.messages li {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  padding-bottom: 0.55rem;
  border-bottom: 1px solid rgba(148, 163, 184, 0.12);
}

.messages li.clickable {
  cursor: pointer;
}

.messages li.clickable:hover {
  color: #0f172a;
}

.messages small {
  color: #94a3b8;
}

.news-impact {
  margin-top: 0;
}

.news-impact-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
}

.news-impact-header h3 {
  margin: 0;
}

.news-impact-content {
  display: grid;
  gap: 0.85rem;
}

.news-buckets {
  display: grid;
  gap: 0.75rem;
}

.news-bucket-card {
  display: grid;
  gap: 0.45rem;
  padding: 0.7rem 0.8rem;
  border-radius: 12px;
  background: rgba(148, 163, 184, 0.09);
}

.news-bucket-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
}

.news-bucket-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: grid;
  gap: 0.45rem;
  max-height: 18rem;
  overflow-y: auto;
  padding-right: 0.25rem;
}

.news-bucket-list li {
  display: grid;
  align-items: start;
  gap: 0.15rem;
}

.news-bucket-list a,
.news-bucket-list span {
  color: #0f172a;
  font-weight: 600;
  text-decoration: none;
}

.news-bucket-list small {
  color: #64748b;
}

.news-impact-summary {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
}

.news-impact-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: grid;
  gap: 0.6rem;
}

.news-impact-list li {
  display: grid;
  grid-template-columns: auto 1fr auto;
  gap: 0.75rem;
  align-items: start;
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

.context-menu {
  position: fixed;
  z-index: 80;
  padding: 0.4rem;
  border-radius: 12px;
  background: #0f172a;
  box-shadow: 0 16px 40px rgba(15, 23, 42, 0.24);
}

.context-menu button {
  background: transparent;
  color: #f8fafc;
}

@media (max-width: 1180px) {
  .workspace-grid,
  .terminal-summary-grid {
    grid-template-columns: 1fr;
  }

  .toolbar-main-row,
  .toolbar-sub-row {
    grid-template-columns: 1fr;
  }

  .toolbar-settings,
  .toolbar-history-actions {
    justify-content: flex-start;
  }

  .workspace-grid.focused {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 720px) {
  .panel-header,
  .deck-card-header,
  .quote-card-header,
  .news-impact-header {
    flex-direction: column;
  }

  .panel-actions {
    width: 100%;
    justify-content: flex-start;
  }
}
</style>
