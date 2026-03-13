<script setup>
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import StockCharts from './StockCharts.vue'
import StockAgentPanels from './StockAgentPanels.vue'
import TerminalView from './TerminalView.vue'
import CopilotPanel from './CopilotPanel.vue'
import ChatWindow from '../../components/ChatWindow.vue'

const symbol = ref('')
const loading = ref(false)
const error = ref('')
const detail = ref(null)
const interval = ref(localStorage.getItem('stock_interval') || 'day')
const refreshSeconds = ref(Number(localStorage.getItem('stock_refresh_seconds') || 30))
const autoRefresh = ref(localStorage.getItem('stock_auto_refresh') === 'true')
const sources = ref([])
const selectedSource = ref(localStorage.getItem('stock_source') || '')
let refreshTimer = null
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
const chatRef = ref(null)
const chatSessions = ref([])
const chatSessionsLoading = ref(false)
const chatSessionsError = ref('')
const selectedChatSession = ref('')
const agentResults = ref([])
const agentLoading = ref(false)
const agentError = ref('')
const agentUpdatedAt = ref('')
const agentHistoryList = ref([])
const agentHistoryLoading = ref(false)
const agentHistoryError = ref('')
const selectedAgentHistoryId = ref('')
const newsImpact = ref(null)
const newsImpactLoading = ref(false)
const newsImpactError = ref('')
const localNewsBuckets = ref({ stock: null, sector: null, market: null })
const localNewsLoading = ref(false)
const localNewsError = ref('')
const copilotPanelOpen = ref(localStorage.getItem('stock_copilot_panel_open') !== 'false')

const newsSections = [
  { key: 'stock', title: '个股事实' },
  { key: 'sector', title: '板块上下文' },
  { key: 'market', title: '大盘环境' }
]

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

const applyHistorySymbol = item => {
  selectedSymbol.value = item.symbol || item.Symbol || ''
  symbol.value = selectedSymbol.value
  if (symbol.value.trim()) {
    fetchQuote()
  }
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
  source: item?.source ?? item?.Source ?? '',
  sourceTag: item?.sourceTag ?? item?.SourceTag ?? '',
  category: item?.category ?? item?.Category ?? '',
  sentiment: item?.sentiment ?? item?.Sentiment ?? '中性',
  publishTime: item?.publishTime ?? item?.PublishTime ?? '',
  crawledAt: item?.crawledAt ?? item?.CrawledAt ?? '',
  url: item?.url ?? item?.Url ?? ''
})

const normalizeNewsBucket = (level, payload) => ({
  level,
  symbol: payload?.symbol ?? payload?.Symbol ?? '',
  sectorName: payload?.sectorName ?? payload?.SectorName ?? '',
  items: Array.isArray(payload?.items ?? payload?.Items) ? (payload.items ?? payload.Items).map(normalizeLocalNewsItem) : []
})

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
  const timestamp = quote.timestamp ?? ''
  return `股票：${name}（${symbol})\n价格：${price}\n涨跌：${change}（${changePercent}%）\n高：${high} 低：${low}\n时间：${formatDate(timestamp)}`
}

const chatSymbolKey = computed(() => {
  const quote = detail.value?.quote
  const raw = quote?.symbol || selectedSymbol.value || symbol.value || ''
  return String(raw || '').trim().toLowerCase()
})

const chatSessionOptions = computed(() => {
  return Array.isArray(chatSessions.value) ? chatSessions.value : []
})

const chatHistoryKey = computed(() => {
  return selectedChatSession.value || ''
})

const fetchChatSessions = async () => {
  const symbolKey = chatSymbolKey.value
  if (!symbolKey) {
    chatSessions.value = []
    selectedChatSession.value = ''
    return
  }

  chatSessionsLoading.value = true
  chatSessionsError.value = ''
  try {
    const params = new URLSearchParams({ symbol: symbolKey })
    const response = await fetch(`/api/stocks/chat/sessions?${params.toString()}`)
    if (!response.ok) {
      throw new Error('聊天历史加载失败')
    }
    const list = await response.json()
    chatSessions.value = Array.isArray(list) ? list.map(item => ({
      key: item.sessionKey ?? item.SessionKey,
      label: item.title ?? item.Title
    })) : []
    if (!chatSessions.value.length) {
      await createChatSession()
      return
    }
    if (!chatSessions.value.some(item => item.key === selectedChatSession.value)) {
      selectedChatSession.value = chatSessions.value[0]?.key || ''
    }
  } catch (err) {
    chatSessionsError.value = err.message || '聊天历史加载失败'
    chatSessions.value = []
  } finally {
    chatSessionsLoading.value = false
  }
}

const createChatSession = async () => {
  const symbolKey = chatSymbolKey.value
  if (!symbolKey) return
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
  chatSessions.value = [entry, ...chatSessions.value]
  selectedChatSession.value = entry.key
}

const startNewChat = async () => {
  try {
    await createChatSession()
  } catch (err) {
    chatSessionsError.value = err.message || '创建会话失败'
    return
  }
  await nextTick()
  chatRef.value?.createNewChat()
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

const fetchAgentHistory = async () => {
  const symbolKey = chatSymbolKey.value
  if (!symbolKey) {
    agentHistoryList.value = []
    selectedAgentHistoryId.value = ''
    return
  }
  agentHistoryLoading.value = true
  agentHistoryError.value = ''
  try {
    const params = new URLSearchParams({ symbol: symbolKey })
    const response = await fetch(`/api/stocks/agents/history?${params.toString()}`)
    if (!response.ok) {
      throw new Error('多Agent历史加载失败')
    }
    const list = await response.json()
    agentHistoryList.value = Array.isArray(list) ? list : []
  } catch (err) {
    agentHistoryError.value = err.message || '多Agent历史加载失败'
    agentHistoryList.value = []
  } finally {
    agentHistoryLoading.value = false
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

const loadAgentHistoryDetail = async historyId => {
  if (!historyId) return
  agentHistoryLoading.value = true
  agentHistoryError.value = ''
  try {
    const response = await fetch(`/api/stocks/agents/history/${historyId}`)
    if (!response.ok) {
      throw new Error('历史详情加载失败')
    }
    const detail = await response.json()
    const result = detail.result ?? detail.Result
    agentResults.value = result?.agents ?? result?.Agents ?? []
    agentUpdatedAt.value = formatDate(detail.createdAt ?? detail.CreatedAt)
  } catch (err) {
    agentHistoryError.value = err.message || '历史详情加载失败'
  } finally {
    agentHistoryLoading.value = false
  }
}

const saveAgentHistory = async () => {
  if (!detail.value?.quote?.symbol) return
  const payload = {
    symbol: detail.value.quote.symbol,
    name: detail.value.quote.name,
    interval: interval.value,
    source: selectedSource.value || null,
    provider: 'openai',
    model: null,
    useInternet: true,
    result: {
      symbol: detail.value.quote.symbol,
      name: detail.value.quote.name,
      timestamp: new Date().toISOString(),
      agents: agentResults.value
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
  selectedAgentHistoryId.value = saved.id ?? saved.Id ?? ''
}

const selectAgentHistory = async value => {
  selectedAgentHistoryId.value = value || ''
  if (!selectedAgentHistoryId.value) {
    return
  }
  await loadAgentHistoryDetail(selectedAgentHistoryId.value)
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

const aiLevels = computed(() => {
  const list = Array.isArray(agentResults.value) ? agentResults.value : []
  const commanderData = getAgentData(list.find(item => getAgentId(item) === 'commander'))
  const trendData = getAgentData(list.find(item => getAgentId(item) === 'trend_analysis'))

  const recommendation = commanderData?.recommendation ?? {}
  const resistanceFromRecommendation = parseLevelNumber(recommendation.targetPrice ?? recommendation.takeProfitPrice)
  const supportFromRecommendation = parseLevelNumber(recommendation.stopLossPrice)

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

  if (!/^(\d{6}|(sh|sz)\d{6})$/i.test(query)) {
    searchStocks(query)
    return
  }

  const targetSymbol = selectedSymbol.value || query

  loading.value = true
  error.value = ''
  // 保留已有数据，避免页面闪烁

  try {
    const params = new URLSearchParams({
      symbol: targetSymbol,
      interval: interval.value
    })
    if (selectedSource.value) {
      params.set('source', selectedSource.value)
    }

    const response = await fetch(`/api/stocks/detail?${params.toString()}`)
    if (!response.ok) {
      throw new Error('接口请求失败')
    }
    detail.value = await response.json()
  } catch (err) {
    error.value = err.message || '请求失败'
  } finally {
    loading.value = false
  }
}

const fetchNewsImpact = async () => {
  const symbolValue = detail.value?.quote?.symbol
  if (!symbolValue) {
    newsImpact.value = null
    newsImpactError.value = ''
    return
  }

  newsImpactLoading.value = true
  newsImpactError.value = ''
  try {
    const params = new URLSearchParams({ symbol: symbolValue })
    if (selectedSource.value) {
      params.set('source', selectedSource.value)
    }
    const response = await fetch(`/api/stocks/news/impact?${params.toString()}`)
    if (!response.ok) {
      throw new Error('资讯影响加载失败')
    }
    newsImpact.value = await response.json()
  } catch (err) {
    newsImpactError.value = err.message || '资讯影响加载失败'
    newsImpact.value = null
  } finally {
    newsImpactLoading.value = false
  }
}

const fetchLocalNews = async () => {
  const symbolValue = detail.value?.quote?.symbol
  if (!symbolValue) {
    localNewsBuckets.value = { stock: null, sector: null, market: null }
    localNewsError.value = ''
    return
  }

  localNewsLoading.value = true
  localNewsError.value = ''
  try {
    const buckets = await Promise.all(
      newsSections.map(async section => {
        const params = new URLSearchParams({ symbol: symbolValue, level: section.key })
        const response = await fetch(`/api/news?${params.toString()}`)
        if (!response.ok) {
          throw new Error('本地新闻加载失败')
        }
        const payload = await response.json()
        return [section.key, normalizeNewsBucket(section.key, payload)]
      })
    )

    localNewsBuckets.value = Object.fromEntries(buckets)
  } catch (err) {
    localNewsError.value = err.message || '本地新闻加载失败'
    localNewsBuckets.value = { stock: null, sector: null, market: null }
  } finally {
    localNewsLoading.value = false
  }
}

const runAgents = async (isPro = false) => {
  if (!detail.value?.quote?.symbol) {
    agentError.value = '请先选择股票'
    return
  }

  agentLoading.value = true
  agentError.value = ''
  try {
    agentResults.value = []
    selectedAgentHistoryId.value = ''
    const order = ['stock_news', 'sector_news', 'financial_analysis', 'trend_analysis', 'commander']
    for (const agentId of order) {
      const payload = {
        symbol: detail.value.quote.symbol,
        agentId,
        interval: interval.value,
        count: detail.value.kLines?.length || 60,
        source: selectedSource.value || null,
        provider: 'openai',
        useInternet: true,
        dependencyResults: agentId === 'commander' ? agentResults.value : [],
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
        upsertAgentResult(result)
      } catch (err) {
        upsertAgentResult({
          agentId,
          agentName: agentId,
          success: false,
          error: err.message || `${agentId} 请求失败`,
          data: null,
          rawContent: null
        })
      }

      agentUpdatedAt.value = formatDate(new Date().toISOString())
    }

    try {
      await saveAgentHistory()
      await fetchAgentHistory()
    } catch (err) {
      agentHistoryError.value = err.message || '保存多Agent历史失败'
    }
  } catch (err) {
    agentError.value = err.message || '多Agent请求失败'
  } finally {
    agentLoading.value = false
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
  symbol.value = item.code || item.Code || item.symbol || item.Symbol || ''
  selectedSymbol.value = item.symbol || item.Symbol || ''
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

watch(interval, value => {
  localStorage.setItem('stock_interval', value)
  if (symbol.value.trim()) {
    fetchQuote()
  }
})

watch(refreshSeconds, value => {
  localStorage.setItem('stock_refresh_seconds', String(value))
  setupRefresh()
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
    fetchNewsImpact()
  }
})

watch(chatSymbolKey, value => {
  if (!value) {
    chatSessions.value = []
    selectedChatSession.value = ''
    agentHistoryList.value = []
    selectedAgentHistoryId.value = ''
    return
  }
  fetchChatSessions()
  fetchAgentHistory()
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
  setupHistoryRefresh()
  window.addEventListener('click', closeContextMenu)
})

onUnmounted(() => {
  if (refreshTimer) {
    clearInterval(refreshTimer)
  }
  if (historyTimer) {
    clearInterval(historyTimer)
  }
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
  () => {
    agentResults.value = []
    agentError.value = ''
    agentUpdatedAt.value = ''
    fetchNewsImpact()
    fetchLocalNews()
  }
)
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
            <button @click="fetchQuote" :disabled="loading">查询</button>
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
          <p v-else-if="loading && !detail" class="muted toolbar-status">查询中...</p>

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
              v-for="item in sortedHistoryList.slice(0, 10)"
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
        <section class="copilot-card news-impact">
          <div class="news-impact-header">
            <div>
              <h3>资讯影响</h3>
              <p class="muted">事件信号在右侧集中查看，不遮挡 K 线。</p>
            </div>
            <button @click="fetchNewsImpact" :disabled="newsImpactLoading || !detail">刷新</button>
          </div>

          <div v-if="detail" class="news-impact-content">
            <p v-if="newsImpactError" class="muted error">{{ newsImpactError }}</p>
            <p v-else-if="newsImpactLoading" class="muted">分析中...</p>
            <div v-else-if="newsImpact" class="news-impact-summary">
              <span>利好 {{ newsImpact.summary.positive }}</span>
              <span>中性 {{ newsImpact.summary.neutral }}</span>
              <span>利空 {{ newsImpact.summary.negative }}</span>
              <span class="overall">总体：{{ newsImpact.summary.overall }}</span>
            </div>
            <p v-else class="muted">资讯影响待生成。</p>

            <div class="news-buckets">
              <article v-for="section in newsSections" :key="section.key" class="news-bucket-card">
                <div class="news-bucket-header">
                  <strong>{{ section.title }}</strong>
                  <span v-if="section.key === 'sector' && localNewsBuckets[section.key]?.sectorName" class="muted">
                    {{ localNewsBuckets[section.key]?.sectorName }}
                  </span>
                </div>

                <p v-if="localNewsLoading" class="muted">加载中...</p>
                <ul v-else-if="localNewsBuckets[section.key]?.items?.length" class="news-bucket-list">
                  <li v-for="item in localNewsBuckets[section.key].items" :key="`${section.key}-${item.title}-${item.publishTime}`">
                    <span class="impact-tag" :class="getImpactClass(item.sentiment)">{{ item.sentiment }}</span>
                    <a v-if="item.url ?? item.Url" :href="item.url ?? item.Url" target="_blank" rel="noreferrer">
                      {{ item.title }}
                    </a>
                    <span v-else>{{ item.title }}</span>
                    <small>
                      {{ item.source }} · {{ formatDate(item.publishTime) }}
                    </small>
                  </li>
                </ul>
                <p v-else class="muted">暂无匹配资讯。</p>
              </article>
            </div>

            <p v-if="localNewsError" class="muted error">{{ localNewsError }}</p>

            <ul v-if="newsImpact?.events?.length" class="news-impact-list">
              <li v-for="item in newsImpact.events.slice(0, 6)" :key="item.title">
                <span class="impact-tag" :class="getImpactClass(item.category)">{{ item.category }}</span>
                <span class="impact-title">{{ item.title }}</span>
                <span class="impact-score">{{ formatImpactScore(item.impactScore) }}</span>
              </li>
            </ul>
            <p v-else-if="!newsImpactLoading" class="muted">暂无资讯影响数据。</p>
          </div>

          <p v-else class="muted">选择股票后在此查看事件影响。</p>
        </section>

        <StockAgentPanels
          :agents="agentResults"
          :loading="agentLoading"
          :error="agentError"
          :last-updated="agentUpdatedAt"
          :history-options="agentHistoryOptions"
          :selected-history-id="selectedAgentHistoryId"
          :history-loading="agentHistoryLoading"
          :history-error="agentHistoryError"
          @select-history="selectAgentHistory"
          @run="runAgents"
        />

        <div class="copilot-card">
          <ChatWindow
            v-if="detail"
            ref="chatRef"
            title="股票助手"
            :build-prompt="buildChatPrompt"
            :history-key="chatHistoryKey"
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
                <p class="muted">当前：{{ currentStockLabel }}</p>
                <select v-model="selectedChatSession" :disabled="!chatSessionOptions.length">
                  <option v-for="item in chatSessionOptions" :key="item.key" :value="item.key">
                    {{ item.label }}
                  </option>
                </select>
                <button class="chat-session-new" @click="startNewChat" :disabled="!chatSymbolKey">
                  新建对话
                </button>
              </div>
            </template>
          </ChatWindow>
          <div v-else class="terminal-empty compact-empty">
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
  display: flex;
  gap: 0.5rem;
  overflow-x: auto;
  padding-bottom: 0.1rem;
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
  grid-template-columns: minmax(220px, 0.75fr) minmax(0, 1.25fr);
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
