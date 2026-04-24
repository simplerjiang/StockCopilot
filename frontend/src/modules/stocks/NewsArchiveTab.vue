<script setup>
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useToast } from '../../composables/useToast.js'

const toast = useToast()

const sourceTagDisplayNames = {
  'sina-roll-market': '新浪财经', 'cls-telegraph': '财联社',
  'eastmoney-market-news': '东方财富·全球', 'eastmoney-ashare-news': '东方财富·A股',
  'gnews-cn-stocks': 'Google·A股', 'gnews-cn-finance': 'Google·金融',
  'gnews-cn-macro': 'Google·宏观', 'gnews-us-stocks': 'Google·美股',
  'gnews-us-macro': 'Google·美宏观', 'gnews-global-macro': 'Google·全球',
  'gnews-reuters': '路透社', 'gnews-bloomberg': '彭博社',
  'gnews-ft': 'FT金融时报', 'gnews-wsj': 'WSJ华尔街日报',
  'cnbc-finance-rss': 'CNBC金融', 'cnbc-us-markets-rss': 'CNBC美股',
  'cnbc-economy-rss': 'CNBC经济', 'cnbc-world-rss': 'CNBC国际',
  'marketwatch-top-rss': '市场观察', 'marketwatch-pulse-rss': 'MW脉搏',
  'bbc-business-rss': 'BBC商业', 'nyt-business-rss': 'NYT商业',
  'seeking-alpha-rss': '投资研究', 'investing-com-rss': '英为财情',
  'sky-business-rss': 'Sky商业', 'cointelegraph-rss': '加密电报'
}
function formatSourceTag(tag) { return sourceTagDisplayNames[tag] || tag }

const keyword = ref('')
const level = ref('')
const sentiment = ref('')
const page = ref(1)
const pageSize = ref(20)
const total = ref(0)
const loading = ref(false)
const error = ref('')
const items = ref([])

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

const levelOptions = [
  { value: '', label: '全部层级' },
  { value: 'market', label: '大盘' },
  { value: 'sector', label: '板块' },
  { value: 'stock', label: '个股' }
]

const sentimentOptions = [
  { value: '', label: '全部情绪' },
  { value: '利好', label: '利好' },
  { value: '中性', label: '中性' },
  { value: '利空', label: '利空' }
]

const ARCHIVE_PROCESS_POLL_INTERVAL_MS = 2000

const totalPages = computed(() => Math.max(1, Math.ceil((total.value || 0) / pageSize.value)))
const pageSummary = computed(() => `第 ${page.value} / ${totalPages.value} 页`)
const resultSummary = computed(() => `共 ${total.value} 条资讯`)
const archiveStatsHeadline = computed(() => loading.value ? '资讯库加载中' : resultSummary.value)
const archiveStatsDetail = computed(() => loading.value ? '统计与分页信息加载中' : pageSummary.value)
const showPagination = computed(() => !loading.value && !error.value && total.value > 0)

const visiblePages = computed(() => {
  const tp = totalPages.value
  const cp = page.value
  if (tp <= 7) return Array.from({ length: tp }, (_, i) => i + 1)
  const pages = [1]
  let start = Math.max(2, cp - 1)
  let end = Math.min(tp - 1, cp + 1)
  if (start > 2) pages.push('...')
  for (let i = start; i <= end; i++) pages.push(i)
  if (end < tp - 1) pages.push('...')
  pages.push(tp)
  return pages
})

const normalizeArchiveItem = item => ({
  level: item?.level ?? item?.Level ?? '',
  symbol: item?.symbol ?? item?.Symbol ?? '',
  name: item?.name ?? item?.Name ?? '',
  sectorName: item?.sectorName ?? item?.SectorName ?? '',
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
  aiTags: Array.isArray(item?.aiTags ?? item?.AiTags) ? (item.aiTags ?? item.AiTags) : [],
  isAiProcessed: item?.isAiProcessed ?? item?.IsAiProcessed ?? false,
})

const formatDate = value => {
  if (!value) return ''
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''
  return cnDateTimeFormatter.format(date)
}

const toSafeCount = value => {
  const parsed = Number(value ?? 0)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : 0
}

const normalizeProcessCounts = value => {
  const market = toSafeCount(value?.market ?? value?.Market)
  const sector = toSafeCount(value?.sector ?? value?.Sector)
  const stock = toSafeCount(value?.stock ?? value?.Stock)
  return {
    market,
    sector,
    stock,
    total: market + sector + stock
  }
}

const normalizeProcessContinuation = value => ({
  mayContinueAutomatically: Boolean(value?.mayContinueAutomatically ?? value?.MayContinueAutomatically),
  reasonCode: value?.reasonCode ?? value?.ReasonCode ?? ''
})

const cloneProcessCounts = counts => {
  if (!counts) return null
  return {
    market: counts.market,
    sector: counts.sector,
    stock: counts.stock,
    total: counts.total
  }
}

const normalizeArchiveJobEvent = value => ({
  timestamp: value?.timestamp ?? value?.Timestamp ?? '',
  level: String(value?.level ?? value?.Level ?? 'info').trim().toLowerCase() || 'info',
  type: String(value?.type ?? value?.Type ?? 'event').trim().toLowerCase() || 'event',
  message: value?.message ?? value?.Message ?? '',
  details: value?.details ?? value?.Details ?? '',
  round: toSafeCount(value?.round ?? value?.Round),
  retry: toSafeCount(value?.retry ?? value?.Retry)
})

const normalizeArchiveJobStatus = value => {
  const processed = normalizeProcessCounts(value?.processed ?? value?.Processed)
  const remaining = normalizeProcessCounts(value?.remaining ?? value?.Remaining)
  const rawContinuation = value?.continuation ?? value?.Continuation
  const continuation = rawContinuation
    ? normalizeProcessContinuation(rawContinuation)
    : {
        mayContinueAutomatically: false,
        reasonCode: ''
      }
  const rawRecentEvents = value?.recentEvents ?? value?.RecentEvents ?? []
  const recentEvents = Array.isArray(rawRecentEvents)
    ? rawRecentEvents.map(normalizeArchiveJobEvent).reverse()
    : []
  const rawState = String(value?.state ?? value?.State ?? 'idle').trim().toLowerCase() || 'idle'
  const state = rawState === 'stopped' ? 'failed' : rawState

  return {
    runId: toSafeCount(value?.runId ?? value?.RunId),
    state,
    isRunning: Boolean(value?.isRunning ?? value?.IsRunning ?? state === 'running'),
    completed: Boolean(value?.completed ?? value?.Completed ?? state === 'completed'),
    requiresManualResume: Boolean(value?.requiresManualResume ?? value?.RequiresManualResume),
    rounds: toSafeCount(value?.rounds ?? value?.Rounds),
    processed,
    remaining,
    stopReason: value?.stopReason ?? value?.StopReason ?? '',
    message: value?.message ?? value?.Message ?? '',
    attentionMessage: value?.attentionMessage ?? value?.AttentionMessage ?? '',
    consecutiveRecoverableFailures: toSafeCount(value?.consecutiveRecoverableFailures ?? value?.ConsecutiveRecoverableFailures),
    maxRecoverableFailures: toSafeCount(value?.maxRecoverableFailures ?? value?.MaxRecoverableFailures) || 3,
    recentEvents,
    continuation,
    startedAt: value?.startedAt ?? value?.StartedAt ?? '',
    updatedAt: value?.updatedAt ?? value?.UpdatedAt ?? '',
    finishedAt: value?.finishedAt ?? value?.FinishedAt ?? ''
  }
}

const formatProcessCounts = counts => `大盘 ${counts.market} / 板块 ${counts.sector} / 个股 ${counts.stock}`

const formatArchiveEventType = type => {
  if (type === 'request') return '请求'
  if (type === 'response') return '响应'
  if (type === 'round') return '累计轮次'
  if (type === 'retry') return '重试'
  if (type === 'warning') return '警告'
  if (type === 'pause') return '暂停'
  if (type === 'restart') return '重新开始'
  if (type === 'error') return '错误'
  if (type === 'parse') return '解析'
  if (type === 'progress') return '进度'
  if (type === 'state') return '状态'
  return '记录'
}

const lastProcessOutcome = ref(null)
const activeArchiveJobStatus = ref(null)
const archiveActionPending = ref('')
const lastAnnouncedTerminalKey = ref('')
let archiveProcessPollTimer = null
let archiveProcessPollGeneration = 0

const createProcessOutcome = ({
  kicker = '后台清洗状态',
  tone,
  title,
  summary,
  processed = null,
  remaining = null,
  rounds = 0,
  stopReason = '',
  canContinue = false,
  isRunning = false,
  nextStep = '',
  events = []
}) => ({
  kicker,
  tone,
  title,
  summary,
  processed,
  remaining,
  rounds,
  stopReason,
  canContinue,
  isRunning,
  nextStep,
  events
})

const dismissProcessOutcome = () => {
  if (lastProcessOutcome.value?.isRunning) return
  lastProcessOutcome.value = null
}

const currentArchiveJobState = computed(() => activeArchiveJobStatus.value?.state ?? 'idle')

const hasResumableArchiveWork = computed(() => {
  const status = activeArchiveJobStatus.value
  if (!status || status.isRunning) {
    return false
  }

  if (status.state === 'paused') {
    return true
  }

  return status.remaining.total > 0 && (
    status.requiresManualResume ||
    status.state === 'failed'
  )
})

const primaryArchiveButtonState = computed(() => {
  if (archiveActionPending.value === 'start') {
    return 'submitting'
  }

  if (currentArchiveJobState.value === 'running') {
    return 'running'
  }

  if (hasResumableArchiveWork.value) {
    return 'resume'
  }

  return 'idle'
})

const primaryArchiveActionLabel = computed(() => {
  if (archiveActionPending.value === 'start') {
    return hasResumableArchiveWork.value ? '继续提交中...' : '启动中...'
  }

  if (currentArchiveJobState.value === 'running') {
    return activeArchiveJobStatus.value?.rounds > 0 ? '后台清洗进行中' : '等待首轮结果'
  }

  if (hasResumableArchiveWork.value) {
    return '继续后台清洗'
  }

  return '批量清洗待处理'
})

const isPrimaryArchiveActionDisabled = computed(() => archiveActionPending.value !== '' || currentArchiveJobState.value === 'running')
const showPauseArchiveAction = computed(() => currentArchiveJobState.value === 'running')
const isPauseArchiveActionDisabled = computed(() => archiveActionPending.value !== '' || currentArchiveJobState.value !== 'running')
const showRestartArchiveAction = computed(() => currentArchiveJobState.value !== 'idle' && currentArchiveJobState.value !== 'running')
const isRestartArchiveActionDisabled = computed(() => archiveActionPending.value !== '' || !showRestartArchiveAction.value)

const getHeadline = item => item.translatedTitle || item.title || ''

const getLevelLabel = value => {
  if (value === 'market') return '大盘'
  if (value === 'sector') return '板块'
  if (value === 'stock') return '个股'
  return '未知'
}

const getSentimentClass = value => {
  if (value === '利好') return 'is-positive'
  if (value === '利空') return 'is-negative'
  return 'is-neutral'
}

const getLevelClass = value => {
  if (value === 'market') return 'is-market'
  if (value === 'sector') return 'is-sector'
  return 'is-stock'
}

const buildMetaLine = item => {
  if (item.level === 'stock') {
    return [item.name, item.symbol, item.sectorName].filter(Boolean).join(' / ')
  }

  return [item.symbol, item.sectorName || item.name].filter(Boolean).join(' / ')
}

const openExternal = url => {
  if (!url) return
  window.open(url, '_blank', 'noopener,noreferrer')
}

const fetchArchive = async ({ resetPage = false } = {}) => {
  if (resetPage) {
    page.value = 1
  }

  loading.value = true
  error.value = ''
  try {
    const params = new URLSearchParams({
      page: String(page.value),
      pageSize: String(pageSize.value)
    })

    if (keyword.value.trim()) params.set('keyword', keyword.value.trim())
    if (level.value) params.set('level', level.value)
    if (sentiment.value) params.set('sentiment', sentiment.value)

    const response = await fetch(`/api/news/archive?${params.toString()}`)
    if (!response.ok) {
      const payload = await response.json().catch(() => null)
      throw new Error(payload?.message || '资讯库加载失败')
    }

    const text = await response.text()
    const payload = (text && text.trim()) ? JSON.parse(text) : null
    total.value = Number(payload?.total ?? payload?.Total ?? 0)
    items.value = Array.isArray(payload?.items ?? payload?.Items) ? (payload.items ?? payload.Items).map(normalizeArchiveItem) : []
  } catch (err) {
    items.value = []
    total.value = 0
    error.value = err.message || '资讯库加载失败'
  } finally {
    loading.value = false
  }
}

const readJsonPayload = async response => {
  const text = await response.text()
  return (text && text.trim()) ? JSON.parse(text) : null
}

const archiveDetailsHint = '可展开“最近请求 / 响应详情”查看模型原始返回。'
const archiveLlmSettingsHint = '若详情显示空响应、非 JSON 数组或 JSON 解析失败，请先检查 LLM 设置。'

const hasModelOutputIssue = value => /null 或空内容|JSON 数组|JSON 解析失败/.test(String(value || ''))

const buildProcessOutcomeFromStatus = status => {
  if (!status || status.state === 'idle') return null

  const hasProcessed = status.processed.total > 0
  const hasRemaining = status.remaining.total > 0
  let tone = 'info'
  let title = '后台清洗状态'
  let summary = status.message || '后台清洗状态已更新。'
  let detail = status.attentionMessage || status.stopReason || ''
  let canContinue = false
  let nextStep = ''

  if (status.isRunning) {
    tone = status.attentionMessage ? 'warning' : 'info'
    title = '后台清洗进行中'
    summary = status.consecutiveRecoverableFailures > 0
      ? `后台清洗遇到可恢复问题，正在进行第 ${status.consecutiveRecoverableFailures} 次自动重试。`
      : status.rounds > 0
        ? '页面正在自动刷新后台进度。'
        : '后台任务已启动，正在等待首轮清洗结果。'
    nextStep = status.consecutiveRecoverableFailures > 0
      ? `本轮待处理资讯已保留。页面会继续自动重试，最多 ${status.maxRecoverableFailures} 次；${archiveDetailsHint}`
      : ''
  } else if (status.completed) {
    tone = hasProcessed ? 'success' : 'info'
    title = hasProcessed ? '后台清洗完成' : '当前没有待清洗资讯'
    summary = hasProcessed
      ? `本次后台任务共处理 ${status.processed.total} 条，当前没有剩余待清洗资讯。`
      : '当前没有待清洗资讯，列表已保持最新状态。'
    detail = status.stopReason || status.attentionMessage || (status.message !== summary ? status.message : '')
  } else if (status.state === 'paused') {
    tone = 'warning'
    title = '后台清洗已暂停'
    summary = hasRemaining
      ? `本次后台任务已处理 ${status.processed.total} 条，仍剩 ${status.remaining.total} 条待清洗资讯。`
      : (status.message || '后台清洗已暂停。')
    detail = status.attentionMessage || status.message
    canContinue = hasRemaining || status.requiresManualResume
    nextStep = '可点击“继续后台清洗”恢复处理，或点击“重新开始清洗”重置本次运行状态。'
  } else if (status.state === 'failed') {
    tone = 'error'
    title = '清洗失败'
    summary = status.stopReason || (hasRemaining
      ? `后台任务异常结束，仍剩 ${status.remaining.total} 条待清洗资讯。`
      : (status.message || '后台清洗任务执行失败。'))
    detail = status.attentionMessage || (status.message !== summary ? status.message : '')
    canContinue = status.requiresManualResume && hasRemaining
    nextStep = hasRemaining
      ? hasModelOutputIssue(`${status.attentionMessage} ${status.stopReason} ${status.message}`)
        ? `可先展开“最近请求 / 响应详情”查看模型原始返回；${archiveLlmSettingsHint}再点击“继续后台清洗”或“重新开始清洗”。`
        : '可先展开“最近请求 / 响应详情”查看最后一轮请求与响应，再决定点击“继续后台清洗”或“重新开始清洗”。'
      : ''
  } else {
    tone = 'warning'
    title = hasRemaining ? '后台清洗状态已更新' : '后台清洗已结束'
    summary = hasRemaining
      ? `本次后台任务已处理 ${status.processed.total} 条，仍剩 ${status.remaining.total} 条待清洗资讯。`
      : (status.message || '后台清洗已结束。')
    detail = status.attentionMessage || status.stopReason || status.message
    canContinue = status.requiresManualResume && hasRemaining
  }

  if (canContinue && !nextStep) {
    nextStep = '仍有剩余待处理资讯，可点击“继续后台清洗”继续下一轮，或点击“重新开始清洗”重新发起任务。'
  }

  return createProcessOutcome({
    kicker: status.isRunning ? '后台清洗状态' : '最近一次清洗结果',
    tone,
    title,
    summary,
    processed: cloneProcessCounts(status.processed),
    remaining: cloneProcessCounts(status.remaining),
    rounds: status.rounds,
    stopReason: detail,
    canContinue,
    isRunning: status.isRunning,
    nextStep,
    events: status.recentEvents
  })
}

const applyArchiveJobStatus = status => {
  activeArchiveJobStatus.value = status
  const outcome = buildProcessOutcomeFromStatus(status)
  if (outcome) {
    lastProcessOutcome.value = outcome
  }
}

const clearArchiveProcessPoll = () => {
  if (archiveProcessPollTimer) {
    clearTimeout(archiveProcessPollTimer)
    archiveProcessPollTimer = null
  }
}

const stopArchiveProcessPoll = () => {
  archiveProcessPollGeneration += 1
  clearArchiveProcessPoll()
}

const scheduleArchiveProcessPoll = (generation, delay = ARCHIVE_PROCESS_POLL_INTERVAL_MS) => {
  clearArchiveProcessPoll()
  archiveProcessPollTimer = setTimeout(() => {
    void pollArchiveProcessStatus(generation)
  }, delay)
}

const buildArchiveTerminalKey = status => {
  return [status.runId, status.state, status.rounds, status.processed.total, status.remaining.total, status.stopReason, status.attentionMessage].join(':')
}

const announceArchiveTerminalStatus = status => {
  if (!status || status.isRunning || status.state === 'idle') return

  const key = buildArchiveTerminalKey(status)
  if (lastAnnouncedTerminalKey.value === key) return
  lastAnnouncedTerminalKey.value = key

  if (status.completed && status.processed.total === 0) {
    toast.info('当前没有待清洗资讯')
    return
  }

  if (status.completed) {
    toast.success(`后台清洗完成，累计处理 ${status.processed.total} 条（${formatProcessCounts(status.processed)}）`, 5000)
    return
  }

  if (status.state === 'paused') {
    toast.info('后台清洗已暂停，可继续或重新开始。')
    return
  }

  if (status.state === 'failed') {
    toast.error(status.stopReason || status.message || '后台清洗任务执行失败。')
    return
  }

  toast.info(status.message || '后台清洗状态已更新。')
}

const settleArchiveJobStatus = async (status, { announceTerminal = true, refreshArchiveOnTerminal = true } = {}) => {
  applyArchiveJobStatus(status)

  if (status.isRunning) {
    return
  }

  stopArchiveProcessPoll()
  if (refreshArchiveOnTerminal) {
    await fetchArchive()
  }
  if (announceTerminal) {
    announceArchiveTerminalStatus(status)
  }
}

const fetchArchiveJobStatus = async () => {
  const response = await fetch('/api/news/archive/process-pending/status')
  if (!response.ok) {
    const message = (await response.text()) || '获取后台清洗状态失败'
    throw new Error(message)
  }

  const payload = await readJsonPayload(response)
  return normalizeArchiveJobStatus(payload)
}

async function pollArchiveProcessStatus(generation) {
  if (generation !== archiveProcessPollGeneration) {
    return
  }

  try {
    const status = await fetchArchiveJobStatus()
    if (generation !== archiveProcessPollGeneration) {
      return
    }

    applyArchiveJobStatus(status)
    if (status.isRunning) {
      scheduleArchiveProcessPoll(generation)
      return
    }

    await settleArchiveJobStatus(status)
  } catch (e) {
    if (generation !== archiveProcessPollGeneration) {
      return
    }

    if (activeArchiveJobStatus.value?.isRunning) {
      lastProcessOutcome.value = createProcessOutcome({
        ...(buildProcessOutcomeFromStatus(activeArchiveJobStatus.value) ?? createProcessOutcome({
          tone: 'info',
          title: '后台清洗进行中',
          summary: '页面正在轮询后台进度。'
        })),
        stopReason: `后台进度轮询暂时失败，将自动重试。${e?.message || '未知错误'}`,
        nextStep: '页面将自动重试进度查询，无需重新启动任务。'
      })
      scheduleArchiveProcessPoll(generation, ARCHIVE_PROCESS_POLL_INTERVAL_MS * 2)
      return
    }

    lastProcessOutcome.value = createProcessOutcome({
      tone: 'error',
      title: '清洗状态获取失败',
      summary: e?.message || '未知错误',
      processed: activeArchiveJobStatus.value ? cloneProcessCounts(activeArchiveJobStatus.value.processed) : null,
      remaining: activeArchiveJobStatus.value ? cloneProcessCounts(activeArchiveJobStatus.value.remaining) : null,
      rounds: activeArchiveJobStatus.value?.rounds ?? 0,
      stopReason: activeArchiveJobStatus.value?.stopReason ?? ''
    })
    toast.error('清洗状态获取失败: ' + (e?.message || '未知错误'))
  }
}

async function refreshArchiveProcessStatus({ announceTerminal = false, refreshArchiveOnTerminal = false } = {}) {
  try {
    const status = await fetchArchiveJobStatus()
    applyArchiveJobStatus(status)
    if (status.isRunning) {
      archiveProcessPollGeneration += 1
      scheduleArchiveProcessPoll(archiveProcessPollGeneration)
      return
    }

    await settleArchiveJobStatus(status, { announceTerminal, refreshArchiveOnTerminal })
  } catch (e) {
    if (activeArchiveJobStatus.value?.isRunning) {
      return
    }
  }
}

const buildArchiveActionErrorOutcome = (title, error) => createProcessOutcome({
  tone: 'error',
  title,
  summary: error?.message || '未知错误',
  processed: activeArchiveJobStatus.value ? cloneProcessCounts(activeArchiveJobStatus.value.processed) : null,
  remaining: activeArchiveJobStatus.value ? cloneProcessCounts(activeArchiveJobStatus.value.remaining) : null,
  rounds: activeArchiveJobStatus.value?.rounds ?? 0,
  stopReason: activeArchiveJobStatus.value?.attentionMessage || activeArchiveJobStatus.value?.stopReason || '',
  events: activeArchiveJobStatus.value?.recentEvents ?? []
})

async function runArchiveJobAction({ actionKey, url, pendingOutcome, errorTitle }) {
  archiveActionPending.value = actionKey
  lastProcessOutcome.value = createProcessOutcome({
    ...pendingOutcome,
    events: activeArchiveJobStatus.value?.recentEvents ?? []
  })

  try {
    const response = await fetch(url, { method: 'POST' })
    if (!response.ok) {
      const message = (await response.text()) || errorTitle
      throw new Error(message)
    }

    const payload = await readJsonPayload(response)
    const status = normalizeArchiveJobStatus(payload)
    applyArchiveJobStatus(status)

    if (status.isRunning) {
      archiveProcessPollGeneration += 1
      scheduleArchiveProcessPoll(archiveProcessPollGeneration)
      return
    }

    await settleArchiveJobStatus(status)
  } catch (e) {
    if (!activeArchiveJobStatus.value?.isRunning) {
      stopArchiveProcessPoll()
    }

    lastProcessOutcome.value = buildArchiveActionErrorOutcome(errorTitle, e)
    toast.error(`${errorTitle}: ${e?.message || '未知错误'}`)
  } finally {
    archiveActionPending.value = ''
  }
}

async function processPending() {
  await runArchiveJobAction({
    actionKey: 'start',
    url: '/api/news/archive/process-pending',
    pendingOutcome: {
      tone: 'info',
      title: hasResumableArchiveWork.value ? '继续后台清洗中' : '后台清洗启动中',
      summary: hasResumableArchiveWork.value
        ? '正在提交继续后台清洗请求，页面将继续轮询后台进度。'
        : '正在提交后台清洗请求，页面将切换到轮询进度模式。',
      isRunning: true,
      nextStep: '提交成功后，页面会自动显示后台进度。'
    },
    errorTitle: hasResumableArchiveWork.value ? '继续后台清洗失败' : '清洗失败'
  })
}

async function pauseArchiveProcess() {
  await runArchiveJobAction({
    actionKey: 'pause',
    url: '/api/news/archive/process-pending/pause',
    pendingOutcome: {
      tone: 'info',
      title: '正在暂停后台清洗',
      summary: '页面正在请求暂停后台清洗，当前批次结束后将进入暂停状态。',
      isRunning: true,
      nextStep: '暂停生效前，页面会继续轮询当前任务状态。'
    },
    errorTitle: '暂停后台清洗失败'
  })
}

async function restartArchiveProcess() {
  await runArchiveJobAction({
    actionKey: 'restart',
    url: '/api/news/archive/process-pending/restart',
    pendingOutcome: {
      tone: 'info',
      title: '正在重新开始后台清洗',
      summary: '正在重置本次运行状态并重新发起后台清洗请求。',
      isRunning: true,
      nextStep: '重新开始成功后，页面会继续显示新的后台进度。'
    },
    errorTitle: '重新开始后台清洗失败'
  })
}

const submitSearch = () => fetchArchive({ resetPage: true })
const goToPage = p => {
  if (p < 1 || p > totalPages.value || p === page.value) return
  page.value = p
  fetchArchive()
}
const goPrev = () => goToPage(page.value - 1)
const goNext = () => goToPage(page.value + 1)

onMounted(() => {
  void fetchArchive()
  void refreshArchiveProcessStatus()
})

onBeforeUnmount(() => {
  stopArchiveProcessPoll()
})
</script>

<template>
  <section class="archive-shell">
    <header class="archive-hero">
      <div>
        <p class="archive-kicker">News Archive Console</p>
        <h2>全量资讯库</h2>
        <p class="archive-subtitle">统一检索本地事实库中已清洗的个股、板块与大盘资讯，支持译题优先、情绪筛选与原文跳转。</p>
      </div>
      <div class="archive-stats">
        <strong>{{ archiveStatsHeadline }}</strong>
        <span>{{ archiveStatsDetail }}</span>
      </div>
    </header>

    <section class="archive-toolbar">
      <label class="archive-field archive-search">
        <span>搜索</span>
        <input
          v-model="keyword"
          type="text"
          placeholder="标题 / 译题 / 代码 / 板块 / 靶点"
          @keydown.enter="submitSearch"
        />
      </label>

      <label class="archive-field">
        <span>层级</span>
        <select v-model="level" @change="submitSearch">
          <option v-for="option in levelOptions" :key="option.value || 'all-level'" :value="option.value">
            {{ option.label }}
          </option>
        </select>
      </label>

      <label class="archive-field">
        <span>情绪</span>
        <select v-model="sentiment" @change="submitSearch">
          <option v-for="option in sentimentOptions" :key="option.value || 'all-sentiment'" :value="option.value">
            {{ option.label }}
          </option>
        </select>
      </label>

      <div class="archive-actions">
        <button class="primary-button" @click="submitSearch" :disabled="loading">检索</button>
        <button
          class="primary-button archive-process-button"
          :class="`is-${primaryArchiveButtonState}`"
          @click="processPending"
          :disabled="isPrimaryArchiveActionDisabled"
          :aria-busy="String(archiveActionPending === 'start')"
        >
          {{ primaryArchiveActionLabel }}
        </button>
        <button
          v-if="showPauseArchiveAction"
          class="secondary-button archive-process-secondary-button is-pause"
          type="button"
          @click="pauseArchiveProcess"
          :disabled="isPauseArchiveActionDisabled"
          :aria-busy="String(archiveActionPending === 'pause')"
        >
          {{ archiveActionPending === 'pause' ? '暂停提交中...' : '暂停后台清洗' }}
        </button>
        <button
          v-if="showRestartArchiveAction"
          class="secondary-button archive-process-secondary-button is-restart"
          type="button"
          @click="restartArchiveProcess"
          :disabled="isRestartArchiveActionDisabled"
          :aria-busy="String(archiveActionPending === 'restart')"
        >
          {{ archiveActionPending === 'restart' ? '重新开始提交中...' : '重新开始清洗' }}
        </button>
      </div>
    </section>

    <section
      v-if="lastProcessOutcome"
      class="archive-process-result"
      :class="`is-${lastProcessOutcome.tone}`"
    >
      <div class="archive-process-result-head">
        <div>
          <p class="archive-process-result-kicker">{{ lastProcessOutcome.kicker }}</p>
          <strong>{{ lastProcessOutcome.title }}</strong>
        </div>
        <button v-if="!lastProcessOutcome.isRunning" class="archive-process-dismiss" type="button" @click="dismissProcessOutcome">关闭</button>
      </div>
      <p class="archive-process-summary">{{ lastProcessOutcome.summary }}</p>
      <div class="archive-process-metrics">
        <span v-if="lastProcessOutcome.processed">已处理 {{ lastProcessOutcome.processed.total }} 条（{{ formatProcessCounts(lastProcessOutcome.processed) }}）</span>
        <span v-if="lastProcessOutcome.remaining">剩余 {{ lastProcessOutcome.remaining.total }} 条（{{ formatProcessCounts(lastProcessOutcome.remaining) }}）</span>
        <span v-if="lastProcessOutcome.rounds > 0">累计轮次 {{ lastProcessOutcome.rounds }}</span>
      </div>
      <p v-if="lastProcessOutcome.stopReason" class="archive-process-detail">{{ lastProcessOutcome.stopReason }}</p>
      <p v-if="lastProcessOutcome.nextStep" class="archive-process-next-step">{{ lastProcessOutcome.nextStep }}</p>
      <details v-if="lastProcessOutcome.events?.length" class="archive-process-events">
        <summary>查看最近请求 / 响应详情（{{ lastProcessOutcome.events.length }} 条，最新在上）</summary>
        <ol class="archive-process-event-list">
          <li
            v-for="(event, index) in lastProcessOutcome.events"
            :key="`${index}-${event.timestamp}-${event.type}`"
            class="archive-process-event"
            :class="`is-${event.level}`"
          >
            <div class="archive-process-event-head">
              <strong>{{ formatArchiveEventType(event.type) }}</strong>
              <span v-if="event.round > 0 || event.retry > 0" class="archive-process-event-meta">
                <span v-if="event.round > 0">累计轮次 {{ event.round }}</span>
                <span v-if="event.retry > 0">重试 {{ event.retry }}</span>
              </span>
              <time v-if="event.timestamp">{{ formatDate(event.timestamp) }}</time>
            </div>
            <p class="archive-process-event-message">{{ event.message }}</p>
            <pre v-if="event.details" class="archive-process-event-details">{{ event.details }}</pre>
          </li>
        </ol>
      </details>
    </section>

    <div v-if="error" class="archive-feedback error">{{ error }}</div>
    <div v-else-if="loading" class="archive-feedback">正在加载资讯库...</div>
    <div v-else-if="!items.length" class="archive-feedback">当前筛选条件下没有可展示的资讯。</div>

    <section v-else class="archive-list">
      <article v-for="item in items" :key="`${item.level}-${item.symbol}-${item.title}-${item.publishTime}`" class="archive-card">
        <div class="archive-card-top">
          <div class="archive-badges">
            <span class="archive-badge level-badge" :class="getLevelClass(item.level)">{{ getLevelLabel(item.level) }}</span>
            <span class="archive-badge sentiment-badge" :class="getSentimentClass(item.sentiment)">{{ item.sentiment }}</span>
            <span v-if="item.sourceTag" class="archive-badge source-tag-badge">📡 {{ formatSourceTag(item.sourceTag) }}</span>
            <span v-if="item.aiTarget" class="archive-badge target-badge">{{ item.aiTarget }}</span>
            <span v-for="tag in item.aiTags" :key="`${item.level}-${item.title}-${tag}`" class="archive-badge tag-badge">{{ tag }}</span>
          </div>
          <button v-if="item.url" class="link-button link-ghost" @click="openExternal(item.url)">
            <span class="link-ghost-icon">↗</span> 查看原文
          </button>
        </div>

        <h3>{{ getHeadline(item) }}</h3>
        <p v-if="item.translatedTitle && item.translatedTitle !== item.title" class="archive-raw-title">原题：{{ item.title }}</p>
        <p v-else class="archive-raw-title">{{ buildMetaLine(item) || item.source }}</p>

        <div class="archive-meta-row">
          <span>{{ buildMetaLine(item) || '未标注实体' }}</span>
          <span>{{ item.source }}</span>
          <span>{{ formatDate(item.publishTime) }}</span>
          <span v-if="item.isAiProcessed" class="ai-processed-badge" title="已 AI 清洗">🤖 已清洗</span>
          <span v-else class="ai-unprocessed-badge" title="待 AI 清洗">⏳ 待清洗</span>
        </div>
      </article>
    </section>

    <footer v-if="showPagination" class="archive-pagination">
      <button @click="goToPage(1)" :disabled="loading || page <= 1">首页</button>
      <button @click="goPrev" :disabled="loading || page <= 1">上一页</button>
      <template v-for="p in visiblePages" :key="'page-' + p">
        <span v-if="p === '...'" class="pagination-ellipsis">…</span>
        <button v-else class="pagination-page" :class="{ 'pagination-current': p === page }" @click="goToPage(p)" :disabled="loading">{{ p }}</button>
      </template>
      <button @click="goNext" :disabled="loading || page >= totalPages">下一页</button>
      <button @click="goToPage(totalPages)" :disabled="loading || page >= totalPages">末页</button>
    </footer>
  </section>
</template>

<style scoped>
.archive-shell {
  display: grid;
  gap: var(--space-4);
  padding: var(--space-6);
  color: var(--color-text-body);
}

.archive-hero {
  display: flex;
  justify-content: space-between;
  gap: var(--space-5);
  padding: var(--space-6);
  border-radius: var(--radius-xl);
  background:
    radial-gradient(circle at top left, color-mix(in srgb, var(--color-accent) 14%, transparent), transparent 35%),
    radial-gradient(circle at bottom right, color-mix(in srgb, var(--color-warning) 10%, transparent), transparent 40%),
    var(--color-bg-surface-alt);
  border: 1px solid var(--color-border-light);
  box-shadow: var(--shadow-md);
}

.archive-kicker {
  margin: 0 0 8px;
  font-size: 12px;
  letter-spacing: 0.18em;
  text-transform: uppercase;
  color: var(--color-accent);
}

.archive-hero h2 {
  margin: 0;
  font-size: 34px;
  line-height: 1.05;
}

.archive-subtitle {
  margin: 12px 0 0;
  max-width: 720px;
  color: var(--color-text-secondary);
  line-height: 1.6;
}

.archive-stats {
  display: grid;
  align-content: start;
  gap: 6px;
  min-width: 140px;
  padding: var(--space-4) var(--space-4);
  border-radius: var(--radius-lg);
  background: var(--color-bg-surface-alt);
  border: 1px solid var(--color-border-light);
  color: var(--color-text-heading);
}

.archive-stats strong {
  font-size: 20px;
}

.archive-stats span {
  color: var(--color-text-secondary);
}

.archive-toolbar {
  display: grid;
  grid-template-columns: minmax(0, 2.1fr) repeat(2, minmax(140px, 0.8fr)) auto;
  gap: var(--space-3);
  align-items: end;
  padding: var(--space-4);
  border-radius: var(--radius-lg);
  background: var(--color-bg-surface);
  border: 1px solid var(--color-border-light);
  box-shadow: var(--shadow-sm);
}

.archive-field {
  display: grid;
  gap: 8px;
}

.archive-field span {
  font-size: 12px;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--color-text-secondary);
}

.archive-field input,
.archive-field select {
  height: 34px;
  padding: 0 var(--space-3);
  border-radius: var(--radius-md);
  border: 1px solid var(--color-border-light);
  background: var(--color-bg-surface-alt);
  color: var(--color-text-heading);
}

.archive-actions {
  display: flex;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 8px;
}

.archive-process-button {
  margin-left: 0;
}

.primary-button,
.secondary-button,
.archive-pagination button,
.link-button {
  height: 34px;
  border: 0;
  border-radius: var(--radius-full);
  padding: 0 var(--space-4);
  font-weight: 600;
  cursor: pointer;
}

.primary-button {
  background: var(--color-accent);
  color: #fff;
  box-shadow: none;
}

.secondary-button {
  background: transparent;
  color: var(--color-text-heading);
  border: 1px solid var(--color-border-light);
}

.primary-button:disabled,
.secondary-button:disabled,
.archive-pagination button:disabled,
.link-button:disabled {
  opacity: 0.45;
  cursor: not-allowed;
  box-shadow: none;
}

.archive-process-button.is-running,
.archive-process-button.is-submitting {
  border: 1px solid rgba(59, 130, 246, 0.18);
  background: rgba(59, 130, 246, 0.1);
  color: var(--color-info);
}

.archive-process-button.is-resume {
  border: 1px solid rgba(34, 197, 94, 0.18);
  background: rgba(34, 197, 94, 0.12);
  color: var(--color-success);
}

.archive-process-button.is-running:disabled,
.archive-process-button.is-submitting:disabled {
  opacity: 1;
  cursor: wait;
}

.archive-process-secondary-button.is-pause {
  color: var(--color-warning);
  border-color: rgba(234, 179, 8, 0.3);
  background: rgba(234, 179, 8, 0.08);
}

.archive-process-secondary-button.is-restart {
  color: var(--color-text-heading);
  border-color: rgba(148, 163, 184, 0.3);
  background: rgba(148, 163, 184, 0.08);
}

.archive-feedback {
  padding: var(--space-5) var(--space-5);
  border-radius: var(--radius-lg);
  background: var(--color-bg-surface);
  border: 1px solid var(--color-border-light);
  color: var(--color-text-secondary);
}

.archive-feedback.error {
  border-color: var(--color-danger-border);
  background: var(--color-danger-bg);
  color: var(--color-danger);
}

.archive-process-result {
  display: grid;
  gap: 10px;
  padding: var(--space-5);
  border-radius: var(--radius-lg);
  border: 1px solid var(--color-border-light);
  background: var(--color-bg-surface);
  box-shadow: var(--shadow-sm);
}

.archive-process-result.is-success {
  border-color: rgba(34, 197, 94, .24);
  background: rgba(34, 197, 94, .08);
}

.archive-process-result.is-warning {
  border-color: rgba(234, 179, 8, .28);
  background: rgba(234, 179, 8, .09);
}

.archive-process-result.is-info {
  border-color: rgba(59, 130, 246, .24);
  background: rgba(59, 130, 246, .08);
}

.archive-process-result.is-error {
  border-color: var(--color-danger-border);
  background: var(--color-danger-bg);
}

.archive-process-result-head {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  align-items: start;
}

.archive-process-result-kicker {
  margin: 0 0 6px;
  font-size: 12px;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--color-text-secondary);
}

.archive-process-result-head strong {
  color: var(--color-text-heading);
  font-size: 18px;
}

.archive-process-dismiss {
  border: 0;
  background: transparent;
  color: var(--color-text-secondary);
  font-weight: 600;
  cursor: pointer;
}

.archive-process-summary,
.archive-process-detail,
.archive-process-next-step {
  margin: 0;
  line-height: 1.6;
}

.archive-process-metrics {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  color: var(--color-text-secondary);
  font-size: 13px;
}

.archive-process-next-step {
  color: var(--color-text-heading);
  font-weight: 600;
}

.archive-process-events {
  display: grid;
  gap: 12px;
  padding-top: 4px;
}

.archive-process-events summary {
  cursor: pointer;
  font-weight: 600;
  color: var(--color-text-heading);
}

.archive-process-event-list {
  display: grid;
  gap: 12px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.archive-process-event {
  display: grid;
  gap: 8px;
  padding: 12px;
  border-radius: var(--radius-md);
  border: 1px solid rgba(148, 163, 184, 0.2);
  background: rgba(255, 255, 255, 0.3);
}

.archive-process-event.is-warning {
  border-color: rgba(234, 179, 8, 0.25);
}

.archive-process-event.is-error {
  border-color: var(--color-danger-border);
}

.archive-process-event-head {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  color: var(--color-text-secondary);
  font-size: 12px;
}

.archive-process-event-head strong {
  color: var(--color-text-heading);
  font-size: 13px;
}

.archive-process-event-meta {
  display: inline-flex;
  gap: 8px;
}

.archive-process-event-message {
  margin: 0;
  color: var(--color-text-heading);
  line-height: 1.5;
}

.archive-process-event-details {
  margin: 0;
  padding: 12px;
  border-radius: var(--radius-md);
  background: rgba(15, 23, 42, 0.06);
  overflow-x: auto;
  white-space: pre-wrap;
  word-break: break-word;
  font-size: 12px;
  line-height: 1.5;
}

.archive-list {
  display: grid;
  gap: 14px;
}

.archive-card {
  display: grid;
  gap: 12px;
  padding: var(--space-5);
  border-radius: var(--radius-lg);
  background: var(--color-bg-surface);
  border: 1px solid var(--color-border-light);
  box-shadow: var(--shadow-sm);
  transition: border-color 0.15s ease;
}

.archive-card:hover {
  border-color: var(--color-accent-border);
}

.archive-card-top {
  display: flex;
  justify-content: space-between;
  gap: 16px;
  align-items: start;
}

.archive-badges {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.archive-badge {
  display: inline-flex;
  align-items: center;
  min-height: 28px;
  padding: 0 10px;
  border-radius: 999px;
  font-size: 12px;
  font-weight: 700;
}

.level-badge.is-market {
  background: var(--color-info-bg);
  color: var(--color-info);
}

.level-badge.is-sector {
  background: var(--color-tag-sector-bg);
  color: var(--color-tag-sector);
}

.level-badge.is-stock {
  background: var(--color-success-bg);
  color: var(--color-success);
}

.sentiment-badge.is-positive {
  background: var(--color-market-rise-bg);
  color: var(--color-market-rise);
}

.sentiment-badge.is-neutral {
  background: var(--color-bg-surface-alt);
  color: var(--color-text-secondary);
}

.sentiment-badge.is-negative {
  background: var(--color-market-fall-bg);
  color: var(--color-market-fall);
}

.target-badge {
  background: var(--color-danger-bg);
  color: var(--color-danger);
}

.source-tag-badge {
  background: rgba(14, 165, 233, .12);
  color: #38bdf8;
  border: 1px solid rgba(14, 165, 233, .2);
}

.tag-badge {
  background: var(--color-warning-bg);
  color: var(--color-warning);
}

.link-button {
  background: var(--color-accent-subtle);
  color: var(--color-accent);
  white-space: nowrap;
}

.link-ghost {
  background: transparent;
  border: 1px solid var(--color-border-light);
  color: var(--color-accent);
  font-weight: 500;
  font-size: 13px;
  display: inline-flex;
  align-items: center;
  gap: 4px;
  transition: background 0.15s, border-color 0.15s;
}

.link-ghost:hover {
  background: var(--color-accent-subtle);
  border-color: var(--color-accent);
}

.link-ghost-icon {
  font-size: 15px;
  line-height: 1;
}

.archive-card h3 {
  margin: 0;
  font-size: 22px;
  line-height: 1.35;
}

.archive-raw-title {
  margin: 0;
  color: var(--color-text-secondary);
  line-height: 1.5;
}

.archive-meta-row {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  color: var(--color-text-secondary);
  font-size: 13px;
}

.archive-pagination {
  display: flex;
  justify-content: flex-end;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}

.archive-pagination button {
  background: var(--color-bg-surface);
  color: var(--color-text-heading);
  border: 1px solid var(--color-border-light);
}

.pagination-page {
  min-width: 34px;
  text-align: center;
}

.pagination-current {
  background: var(--color-accent) !important;
  color: #fff !important;
  border-color: var(--color-accent) !important;
}

.pagination-ellipsis {
  padding: 0 4px;
  color: var(--color-text-secondary);
  user-select: none;
}

@media (max-width: 960px) {
  .archive-shell {
    padding: var(--space-4);
  }

  .archive-hero,
  .archive-toolbar,
  .archive-card-top,
  .archive-pagination {
    grid-template-columns: 1fr;
    flex-direction: column;
    align-items: stretch;
  }

  .archive-toolbar {
    display: grid;
  }

  .archive-actions,
  .archive-pagination {
    justify-content: stretch;
  }
}
.ai-processed-badge { display:inline-flex; align-items:center; gap:.2rem; padding:.1rem .4rem; border-radius:999px; font-size:.68rem; font-weight:600; background:rgba(34,197,94,.12); color:#16a34a; }
.ai-unprocessed-badge { display:inline-flex; align-items:center; gap:.2rem; padding:.1rem .4rem; border-radius:999px; font-size:.68rem; font-weight:600; background:rgba(234,179,8,.12); color:#ca8a04; }
</style>