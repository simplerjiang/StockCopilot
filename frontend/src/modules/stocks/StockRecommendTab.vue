<script setup>
import { computed, onActivated, onDeactivated, onMounted, onUnmounted, ref, watch } from 'vue'
import RecommendReportCard from './recommend/RecommendReportCard.vue'
import RecommendFeed from './recommend/RecommendFeed.vue'
import RecommendProgress from './recommend/RecommendProgress.vue'

// ---------- State ----------
const activeTab = ref('report') // 'report' | 'debate' | 'progress'
const isRunning = ref(false)
const activeSession = ref(null)
const sessionHistory = ref([])
const sseEvents = ref([])
const followUpText = ref('')
const followUpSending = ref(false)
const followUpError = ref('')

// Market sidebar state (preserved from original)
const realtimeContextEnabled = ref(localStorage.getItem('stock_recommend_realtime_context_enabled') !== 'false')
const realtimeOverview = ref(null)
const realtimeSectors = ref([])
const marketLoading = ref(false)
const marketError = ref('')

const statusPhase = ref('idle') // 'idle' | 'submitting' | 'connecting' | 'running' | 'completed' | 'failed'
const statusMessage = ref('')
const lastSseEventAt = ref(null)
const sseReconnectDelaySeconds = ref(null)
const sseBackendStatusText = ref('')
const submitStartTime = ref(null)
const elapsedSeconds = ref(0)
let elapsedTimer = null

let eventSource = null
let eventSourceSessionId = null
let sseRetryCount = 0
let seenSseEventIds = new Map()
const SSE_MAX_RETRIES = 3
let componentAlive = true
let failedSseSessionId = null
let activeSessionLoadToken = 0

const TURN_LIVE_STATUSES = new Set(['Pending', 'Queued', 'Running'])
const TURN_RUNNING_STATUSES = new Set(['Queued', 'Running'])
const TURN_TERMINAL_STATUSES = new Set(['Completed', 'Failed', 'Cancelled'])
const SESSION_TERMINAL_STATUSES = new Set(['Completed', 'Degraded', 'Failed', 'Closed', 'TimedOut'])
const SSE_RETRY_DELAYS_SECONDS = [1, 2, 5]

const formatSseEventTime = value => value ? cnDateTimeFormatter.format(new Date(value)) : '暂无'

const parseDateMs = value => {
  if (!value) return null
  const parsed = Date.parse(value)
  return Number.isFinite(parsed) ? parsed : null
}

const startElapsedTimer = (startedAt = Date.now()) => {
  const startMs = typeof startedAt === 'number'
    ? startedAt
    : (parseDateMs(startedAt) ?? Date.now())
  submitStartTime.value = startMs
  elapsedSeconds.value = Math.max(0, Math.floor((Date.now() - startMs) / 1000))
  if (elapsedTimer) clearInterval(elapsedTimer)
  elapsedTimer = setInterval(() => {
    elapsedSeconds.value = Math.max(0, Math.floor((Date.now() - submitStartTime.value) / 1000))
  }, 1000)
}

const stopElapsedTimer = () => {
  if (elapsedTimer) { clearInterval(elapsedTimer); elapsedTimer = null }
}

const getSessionStatusValue = session => session?.status ?? session?.Status ?? ''
const getTurnStatusValue = turn => turn?.status ?? turn?.Status ?? ''
const getTurnRequestedAt = turn => turn?.requestedAt ?? turn?.RequestedAt ?? null
const getTurnStartedAt = turn => turn?.startedAt ?? turn?.StartedAt ?? null
const getTurnCompletedAt = turn => turn?.completedAt ?? turn?.CompletedAt ?? null
const getTurnStageSnapshots = turn => Array.isArray(turn?.stageSnapshots)
  ? turn.stageSnapshots
  : Array.isArray(turn?.StageSnapshots)
    ? turn.StageSnapshots
    : []

const getTurnSortValue = turn => {
  const turnIndex = Number(turn?.turnIndex ?? turn?.TurnIndex)
  if (Number.isFinite(turnIndex)) return turnIndex

  const requestedAt = parseDateMs(getTurnRequestedAt(turn))
  if (Number.isFinite(requestedAt)) return requestedAt

  const turnId = Number(turn?.id ?? turn?.Id)
  return Number.isFinite(turnId) ? turnId : -1
}

const getActiveSessionTurn = session => {
  const turns = getSessionTurns(session)
    .slice()
    .sort((left, right) => getTurnSortValue(left) - getTurnSortValue(right))
  if (!turns.length) return null

  const activeTurnId = session?.activeTurnId ?? session?.ActiveTurnId ?? null
  const activeTurn = turns.find(turn => (turn.id ?? turn.Id) === activeTurnId) ?? null
  const latestLiveTurn = [...turns].reverse().find(turn => TURN_LIVE_STATUSES.has(getTurnStatusValue(turn))) ?? null

  if (latestLiveTurn && !TURN_LIVE_STATUSES.has(getTurnStatusValue(activeTurn))) {
    return latestLiveTurn
  }

  return activeTurn ?? turns[turns.length - 1]
}

const buildDurationSeconds = (startedAt, completedAt) => {
  const startMs = parseDateMs(startedAt)
  if (!Number.isFinite(startMs)) return null
  const endMs = parseDateMs(completedAt) ?? Date.now()
  return Math.max(0, Math.floor((endMs - startMs) / 1000))
}

const buildTerminalStatusMessage = ({ sessionStatus, turnStatus, durationSeconds, hasDegradedStage }) => {
  const suffix = durationSeconds != null ? `，耗时 ${durationSeconds} 秒` : ''
  if (sessionStatus === 'TimedOut') return `分析超时结束${suffix}`
  if (turnStatus === 'Cancelled') return `分析已取消${suffix}`
  if (turnStatus === 'Failed' || sessionStatus === 'Failed') return `分析流程失败${suffix}`

  const degraded = sessionStatus === 'Degraded' || hasDegradedStage
  if (degraded) {
    return durationSeconds != null ? `分析完成（部分降级），耗时 ${durationSeconds} 秒` : '分析完成（部分降级）'
  }

  return durationSeconds != null ? `分析完成！耗时 ${durationSeconds} 秒` : '分析完成'
}

const describeLoadedSessionRuntime = session => {
  if (!session) return null

  const sessionId = session.id ?? session.Id ?? null
  const sessionStatus = getSessionStatusValue(session)
  const activeTurn = getActiveSessionTurn(session)
  const turnStatus = getTurnStatusValue(activeTurn)
  const stageSnapshots = getTurnStageSnapshots(activeTurn)
  const hasDegradedStage = stageSnapshots.some(snapshot => (snapshot?.status ?? snapshot?.Status) === 'Degraded')
  const startedAt = getTurnStartedAt(activeTurn) ?? getTurnRequestedAt(activeTurn)
  const completedAt = getTurnCompletedAt(activeTurn) ?? session?.updatedAt ?? session?.UpdatedAt ?? null
  const durationSeconds = buildDurationSeconds(startedAt, completedAt)

  if (TURN_TERMINAL_STATUSES.has(turnStatus)) {
    return {
      sessionId,
      isRunning: false,
      phase: turnStatus === 'Completed' ? 'completed' : 'failed',
      message: buildTerminalStatusMessage({ sessionStatus, turnStatus, durationSeconds, hasDegradedStage }),
      durationSeconds,
      startedAt,
      shouldConnect: false
    }
  }

  if (TURN_RUNNING_STATUSES.has(turnStatus) || sessionStatus === 'Running') {
    return {
      sessionId,
      isRunning: true,
      phase: eventSourceSessionId === sessionId ? 'running' : 'connecting',
      message: eventSourceSessionId === sessionId
        ? '分析进行中，团队正在讨论...'
        : '检测到会话仍在执行，正在连接分析流...',
      durationSeconds,
      startedAt,
      shouldConnect: sessionId != null && failedSseSessionId !== sessionId
    }
  }

  if (SESSION_TERMINAL_STATUSES.has(sessionStatus)) {
    return {
      sessionId,
      isRunning: false,
      phase: sessionStatus === 'Failed' || sessionStatus === 'TimedOut' ? 'failed' : 'completed',
      message: buildTerminalStatusMessage({ sessionStatus, turnStatus, durationSeconds, hasDegradedStage }),
      durationSeconds,
      startedAt,
      shouldConnect: false
    }
  }

  return {
    sessionId,
    isRunning: false,
    phase: 'idle',
    message: '',
    durationSeconds,
    startedAt,
    shouldConnect: false
  }
}

const syncRuntimeFromLoadedSession = session => {
  const runtime = describeLoadedSessionRuntime(session)
  if (!runtime) return

  if (runtime.isRunning) {
    if (failedSseSessionId === runtime.sessionId && statusPhase.value === 'failed') {
      stopElapsedTimer()
      isRunning.value = false
      return
    }

    startElapsedTimer(runtime.startedAt ?? Date.now())
    isRunning.value = true

    const hasLiveBanner = runtime.sessionId != null
      && eventSourceSessionId === runtime.sessionId
      && ['submitting', 'connecting', 'running'].includes(statusPhase.value)

    if (runtime.shouldConnect && runtime.sessionId != null && eventSourceSessionId !== runtime.sessionId) {
      statusPhase.value = 'connecting'
      statusMessage.value = runtime.message
      connectSse(runtime.sessionId)
      return
    }

    if (!hasLiveBanner) {
      statusPhase.value = runtime.phase
      statusMessage.value = runtime.message
    }
    return
  }

  closeSse()
  failedSseSessionId = null
  stopElapsedTimer()
  isRunning.value = false

  if (runtime.durationSeconds != null) {
    elapsedSeconds.value = runtime.durationSeconds
  }

  statusPhase.value = runtime.phase
  statusMessage.value = runtime.message
}

const updateSseBackendStatusFromSession = session => {
  if (!session) {
    sseBackendStatusText.value = '后台状态不明'
    return
  }
  const sessionStatus = getSessionStatusValue(session)
  const turnStatus = getTurnStatusValue(getActiveSessionTurn(session))
  if (sessionStatus === 'Running' || TURN_RUNNING_STATUSES.has(turnStatus) || TURN_LIVE_STATUSES.has(turnStatus)) {
    sseBackendStatusText.value = '后台分析仍在运行'
  } else if (SESSION_TERMINAL_STATUSES.has(sessionStatus) || TURN_TERMINAL_STATUSES.has(turnStatus)) {
    sseBackendStatusText.value = '后台分析已结束'
  } else {
    sseBackendStatusText.value = '后台状态不明'
  }
}

const topSectors = computed(() => realtimeSectors.value.slice(0, 6))
const cnDateTimeFormatter = new Intl.DateTimeFormat('zh-CN', {
  timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit',
  hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false
})

// ---------- Market helpers (unchanged) ----------
const normalizeRealtimeQuote = item => ({
  symbol: item.symbol ?? item.Symbol ?? '',
  name: item.name ?? item.Name ?? '',
  price: Number(item.price ?? item.Price ?? 0),
  changePercent: Number(item.changePercent ?? item.ChangePercent ?? 0),
  turnoverAmount: Number(item.turnoverAmount ?? item.TurnoverAmount ?? 0)
})

const normalizeRealtimeOverviewSection = source => source ? ({
  snapshotTime: source.snapshotTime ?? source.SnapshotTime ?? '',
  mainNetInflow: Number(source.mainNetInflow ?? source.MainNetInflow ?? 0),
  totalNetInflow: Number(source.totalNetInflow ?? source.TotalNetInflow ?? 0),
  advancers: Number(source.advancers ?? source.Advancers ?? 0),
  decliners: Number(source.decliners ?? source.Decliners ?? 0),
  limitUpCount: Number(source.limitUpCount ?? source.LimitUpCount ?? 0),
  limitDownCount: Number(source.limitDownCount ?? source.LimitDownCount ?? 0)
}) : null

const normalizeRealtimeOverview = payload => payload ? ({
  snapshotTime: payload.snapshotTime ?? payload.SnapshotTime ?? '',
  indices: Array.isArray(payload.indices ?? payload.Indices) ? (payload.indices ?? payload.Indices).map(normalizeRealtimeQuote) : [],
  mainCapitalFlow: normalizeRealtimeOverviewSection(payload.mainCapitalFlow ?? payload.MainCapitalFlow ?? null),
  northboundFlow: normalizeRealtimeOverviewSection(payload.northboundFlow ?? payload.NorthboundFlow ?? null),
  breadth: normalizeRealtimeOverviewSection(payload.breadth ?? payload.Breadth ?? null)
}) : null

const normalizeRealtimeSector = item => ({
  sectorCode: item.sectorCode ?? item.SectorCode ?? '',
  sectorName: item.sectorName ?? item.SectorName ?? '',
  changePercent: Number(item.changePercent ?? item.ChangePercent ?? 0),
  mainNetInflow: Number(item.mainNetInflow ?? item.MainNetInflow ?? 0),
  rankNo: Number(item.rankNo ?? item.RankNo ?? 0)
})

const formatDate = value => {
  if (!value) return '--'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '--'
  return cnDateTimeFormatter.format(date)
}

const formatSignedPercent = value => {
  const number = Number(value ?? 0)
  return `${number >= 0 ? '+' : ''}${number.toFixed(2)}%`
}

const formatSignedAmount = value => {
  const number = Number(value ?? 0)
  return `${number >= 0 ? '+' : ''}${number.toFixed(2)} 亿`
}

const getChangeClass = value => {
  const number = Number(value ?? 0)
  if (number > 0) return 'positive'
  if (number < 0) return 'negative'
  return ''
}

const isGarbled = (str) => {
  if (!str) return true
  // Detect 3+ consecutive '?' or replacement characters
  return /[?\ufffd]{3,}/.test(str)
}

const getSessionPrompt = (s) => {
  const turns = s.turns ?? s.Turns ?? []
  const firstTurn = turns[0]
  const prompt = firstTurn?.userPrompt ?? firstTurn?.UserPrompt ?? s.userPrompt ?? s.UserPrompt ?? s.lastUserIntent ?? s.LastUserIntent ?? ''
  if (isGarbled(prompt)) return '推荐查询'
  return prompt.length > 30 ? prompt.slice(0, 30) + '...' : prompt
}

const getSessionTurnCount = (s) => {
  const turns = s.turns ?? s.Turns ?? []
  return turns.length
}

const statusClass = (s) => {
  const status = (s.status ?? s.Status ?? '').toLowerCase()
  if (status === 'completed') return 'status-success'
  if (status === 'failed' || status === 'timedout') return 'status-error'
  if (status === 'running') return 'status-running'
  if (status === 'degraded') return 'status-warning'
  return 'status-default'
}

const statusLabel = (s) => {
  const status = (s.status ?? s.Status ?? '').toLowerCase()
  const map = { completed: '完成', failed: '失败', running: '运行中', degraded: '降级', idle: '待执行', timedout: '已超时' }
  return map[status] || status
}

const fetchJson = async (url, options) => {
  const response = await fetch(url, options)
  if (!response.ok) {
    const payload = await response.json().catch(() => null)
    throw new Error(payload?.message || `请求失败: ${response.status}`)
  }
  const text = await response.text()
  if (!text || !text.trim()) return null
  return JSON.parse(text)
}

const clearActiveSessionView = () => {
  activeSessionLoadToken += 1
  activeSession.value = null
}

const fetchMarketContext = async () => {
  if (!realtimeContextEnabled.value || typeof fetch !== 'function') {
    realtimeOverview.value = null
    realtimeSectors.value = []
    marketError.value = ''
    marketLoading.value = false
    return
  }
  marketLoading.value = true
  marketError.value = ''
  try {
    const [overviewPayload, sectorsPayload] = await Promise.all([
      fetchJson('/api/market/realtime/overview'),
      fetchJson('/api/market/sectors/realtime?boardType=concept&take=8&sort=rank')
    ])
    realtimeOverview.value = normalizeRealtimeOverview(overviewPayload)
    realtimeSectors.value = Array.isArray(sectorsPayload?.items ?? sectorsPayload?.Items)
      ? (sectorsPayload.items ?? sectorsPayload.Items).map(normalizeRealtimeSector)
      : []
  } catch (err) {
    realtimeOverview.value = null
    realtimeSectors.value = []
    marketError.value = err.message || '推荐页市场快照加载失败'
  } finally {
    marketLoading.value = false
  }
}

// ---------- Session CRUD ----------
const loadSessionHistory = async () => {
  try {
    const data = await fetchJson('/api/recommend/sessions')
    sessionHistory.value = Array.isArray(data) ? data : (data?.items ?? data?.Items ?? [])
  } catch {
    sessionHistory.value = []
  }
}

// V048-S2 #82: mount 时检测左侧"运行中"会话，自动选中并续上 SSE
const resumeRunningSessionIfAny = async () => {
  await loadSessionHistory()
  if (!componentAlive) return
  const list = Array.isArray(sessionHistory.value) ? sessionHistory.value : []
  const running = list.find(s => {
    const status = s?.status ?? s?.Status
    return status === 'Running'
  })
  if (!running) return
  const id = running.id ?? running.Id
  if (id == null) return
  await loadSessionDetail(id)
  if (!componentAlive) return
  // 切到进度 Tab 让用户立刻看到正在跑的角色
  activeTab.value = 'progress'
  connectSse(id)
}

const loadSessionDetail = async (id) => {
  const loadToken = ++activeSessionLoadToken
  try {
    const detail = await fetchJson(`/api/recommend/sessions/${id}`)
    if (!componentAlive || loadToken !== activeSessionLoadToken) return null
    activeSession.value = detail
    syncRuntimeFromLoadedSession(detail)
    return detail
  } catch {
    if (!componentAlive || loadToken !== activeSessionLoadToken) return null
    activeSession.value = null
    return null
  }
}

const selectSession = async (session) => {
  failedSseSessionId = null
  closeSse()
  sseEvents.value = []
  followUpText.value = ''
  followUpError.value = ''
  const id = session.id ?? session.Id
  await loadSessionDetail(id)
}

// ---------- Create new recommendation ----------
const handleNewRecommend = async (userPrompt) => {
  if (!userPrompt?.trim()) return
  const prompt = userPrompt.trim()
  failedSseSessionId = null
  isRunning.value = true
  statusPhase.value = 'submitting'
  statusMessage.value = '正在创建推荐会话...'
  startElapsedTimer()
  sseEvents.value = []
  followUpError.value = ''
  try {
    const result = await fetchJson('/api/recommend/sessions', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ userPrompt: prompt })
    })
    const sessionId = result.id ?? result.Id
    if (!componentAlive) return
    clearActiveSessionView()
    statusPhase.value = 'connecting'
    statusMessage.value = '会话已创建，正在连接分析流...'
    await loadSessionDetail(sessionId)
    await loadSessionHistory()
    if (!componentAlive) return
    activeTab.value = 'progress'
    followUpText.value = ''
  } catch (err) {
    followUpError.value = err.message || '创建推荐会话失败'
    statusPhase.value = 'failed'
    statusMessage.value = err.message || '创建推荐会话失败'
    stopElapsedTimer()
    isRunning.value = false
  }
}

// ---------- SSE ----------
const connectSse = (sessionId) => {
  closeSse()
  failedSseSessionId = null
  sseRetryCount = 0
  sseReconnectDelaySeconds.value = null
  sseBackendStatusText.value = ''
  seenSseEventIds = new Map()
  isRunning.value = true
  eventSourceSessionId = sessionId
  statusPhase.value = 'connecting'
  statusMessage.value = '检测到会话仍在执行，正在连接分析流...'
  const url = `/api/recommend/sessions/${sessionId}/events`
  eventSource = new EventSource(url)

  eventSource.onopen = () => {
    sseRetryCount = 0
    sseReconnectDelaySeconds.value = null
    sseBackendStatusText.value = ''
    failedSseSessionId = null
    statusPhase.value = 'running'
    statusMessage.value = '分析进行中，团队正在讨论...'
  }

  eventSource.onmessage = (e) => {
    lastSseEventAt.value = new Date().toISOString()
    if (e.data === '[DONE]') {
      statusPhase.value = 'completed'
      statusMessage.value = `分析完成！耗时 ${elapsedSeconds.value} 秒`
      stopElapsedTimer()
      isRunning.value = false
      failedSseSessionId = null
      closeSse()
      void loadSessionDetail(sessionId)
      void loadSessionHistory()
      return
    }

    if (e.lastEventId) {
      if (seenSseEventIds.has(e.lastEventId)) {
        return
      }
      seenSseEventIds.set(e.lastEventId, true)
      if (seenSseEventIds.size > 256) {
        const oldestEventId = seenSseEventIds.keys().next().value
        seenSseEventIds.delete(oldestEventId)
      }
    }

    try {
      const evt = JSON.parse(e.data)
      if (evt.eventType === 'TurnSwitched') {
        void loadSessionDetail(sessionId)
        statusMessage.value = '已切换到新的追问轮次，继续分析中...'
        return
      }
      sseEvents.value = [...sseEvents.value, evt]
      if (evt.eventType === 'TurnCompleted') {
        statusPhase.value = 'completed'
        statusMessage.value = `分析完成！耗时 ${elapsedSeconds.value} 秒`
        stopElapsedTimer()
        isRunning.value = false
        failedSseSessionId = null
        closeSse()
        void loadSessionDetail(sessionId)
        void loadSessionHistory()
      } else if (evt.eventType === 'TurnFailed' || evt.eventType === 'ErrorOccurred') {
        statusPhase.value = 'failed'
        statusMessage.value = evt.summary || '分析流程失败'
        stopElapsedTimer()
        isRunning.value = false
        failedSseSessionId = null
        closeSse()
        void loadSessionDetail(sessionId)
        void loadSessionHistory()
      } else {
        // Update status with latest event summary
        if (evt.summary) {
          statusMessage.value = evt.summary
        } else if (evt.eventType === 'StageStarted') {
          statusMessage.value = `正在执行: ${evt.stageName || evt.stageType || '阶段处理中'}...`
        } else if (evt.eventType === 'RoleStarted') {
          statusMessage.value = `${evt.roleName || evt.roleId || '角色'} 分析中...`
        }
      }
    } catch { /* ignore parse errors */ }
  }

  eventSource.onerror = () => {
    sseRetryCount++
    const nextDelay = SSE_RETRY_DELAYS_SECONDS[Math.min(sseRetryCount - 1, SSE_RETRY_DELAYS_SECONDS.length - 1)]
    sseReconnectDelaySeconds.value = nextDelay
    if (sseRetryCount >= SSE_MAX_RETRIES) {
      failedSseSessionId = sessionId
      statusPhase.value = 'failed'
      statusMessage.value = 'SSE 连接失败，已停止自动重试'
      stopElapsedTimer()
      isRunning.value = false
      closeSse()
      void loadSessionDetail(sessionId).then(updateSseBackendStatusFromSession)
      return
    }
    statusMessage.value = `SSE 连接中断，浏览器将在约 ${nextDelay} 秒后自动重试（${sseRetryCount}/${SSE_MAX_RETRIES}）`
  }
}

const closeSse = () => {
  if (eventSource) {
    eventSource.close()
    eventSource = null
  }
  eventSourceSessionId = null
}

const reconnectSse = () => {
  if (!activeSession.value) return
  failedSseSessionId = null
  sseRetryCount = 0
  sseReconnectDelaySeconds.value = null
  sseBackendStatusText.value = ''
  statusPhase.value = 'connecting'
  statusMessage.value = '正在重试...'
  const activeTurn = getActiveSessionTurn(activeSession.value)
  startElapsedTimer(getTurnStartedAt(activeTurn) ?? getTurnRequestedAt(activeTurn) ?? Date.now())
  const sessionId = activeSession.value.id ?? activeSession.value.Id
  connectSse(sessionId)
}

const getSessionTurns = session => Array.isArray(session?.turns)
  ? session.turns
  : Array.isArray(session?.Turns)
    ? session.Turns
    : []

const appendPendingFollowUpTurn = prompt => {
  if (!activeSession.value) return

  const currentSession = activeSession.value
  const currentTurns = getSessionTurns(currentSession)
  const nextTurnIndex = currentTurns.reduce((maxTurnIndex, turn) => {
    const turnIndex = Number(turn?.turnIndex ?? turn?.TurnIndex ?? -1)
    return Number.isFinite(turnIndex) ? Math.max(maxTurnIndex, turnIndex) : maxTurnIndex
  }, -1) + 1
  const requestedAt = new Date().toISOString()
  const pendingTurnId = `pending-followup-${Date.now()}`
  const pendingTurn = {
    id: pendingTurnId,
    Id: pendingTurnId,
    turnIndex: nextTurnIndex,
    TurnIndex: nextTurnIndex,
    userPrompt: prompt,
    UserPrompt: prompt,
    requestedAt,
    RequestedAt: requestedAt,
    status: 'Pending',
    Status: 'Pending',
    feedItems: [],
    FeedItems: [],
    stageSnapshots: [],
    StageSnapshots: []
  }

  activeSession.value = {
    ...currentSession,
    activeTurnId: pendingTurnId,
    ActiveTurnId: pendingTurnId,
    status: 'Running',
    Status: 'Running',
    turns: [...currentTurns, pendingTurn],
    Turns: [...currentTurns, pendingTurn],
    updatedAt: requestedAt,
    UpdatedAt: requestedAt
  }

  return {
    pendingTurnId,
    prompt,
    requestedAt,
    turnIndex: nextTurnIndex,
    previousActiveTurnId: currentSession.activeTurnId ?? currentSession.ActiveTurnId ?? null,
    previousStatus: currentSession.status ?? currentSession.Status ?? null,
    previousUpdatedAt: currentSession.updatedAt ?? currentSession.UpdatedAt ?? null
  }
}

const applyOptimisticFollowUpTurn = (pendingTurn, result) => {
  if (!activeSession.value || !pendingTurn) return

  const turnId = result?.turnId ?? result?.TurnId
  if (!turnId) return

  const turnIndex = result?.turnIndex ?? result?.TurnIndex ?? pendingTurn.turnIndex
  const requestedAt = pendingTurn.requestedAt
  const status = result?.status ?? result?.Status ?? 'Queued'
  const currentSession = activeSession.value
  const currentTurns = getSessionTurns(currentSession)
  const nextTurns = [
    ...currentTurns.filter(turn => {
      const currentTurnId = turn?.id ?? turn?.Id
      return currentTurnId !== pendingTurn.pendingTurnId && currentTurnId !== turnId
    }),
    {
      id: turnId,
      Id: turnId,
      turnIndex,
      TurnIndex: turnIndex,
      userPrompt: pendingTurn.prompt,
      UserPrompt: pendingTurn.prompt,
      requestedAt,
      RequestedAt: requestedAt,
      status,
      Status: status,
      feedItems: [],
      FeedItems: [],
      stageSnapshots: [],
      StageSnapshots: []
    }
  ]

  activeSession.value = {
    ...currentSession,
    activeTurnId: turnId,
    ActiveTurnId: turnId,
    status: 'Running',
    Status: 'Running',
    turns: nextTurns,
    Turns: nextTurns,
    updatedAt: requestedAt,
    UpdatedAt: requestedAt
  }
}

const removePendingFollowUpTurn = pendingTurn => {
  if (!activeSession.value || !pendingTurn) return

  const currentSession = activeSession.value
  const currentTurns = getSessionTurns(currentSession)
  const nextTurns = currentTurns.filter(turn => (turn?.id ?? turn?.Id) !== pendingTurn.pendingTurnId)
  const restoredStatus = pendingTurn.previousStatus ?? currentSession.status ?? currentSession.Status ?? null
  const restoredUpdatedAt = pendingTurn.previousUpdatedAt ?? currentSession.updatedAt ?? currentSession.UpdatedAt ?? null

  activeSession.value = {
    ...currentSession,
    activeTurnId: pendingTurn.previousActiveTurnId,
    ActiveTurnId: pendingTurn.previousActiveTurnId,
    status: restoredStatus,
    Status: restoredStatus,
    turns: nextTurns,
    Turns: nextTurns,
    updatedAt: restoredUpdatedAt,
    UpdatedAt: restoredUpdatedAt
  }
}

// ---------- Follow-up ----------
const handleFollowUp = async (prompt) => {
  if (!prompt?.trim() || !activeSession.value) return
  const normalizedPrompt = prompt.trim()
  const sessionId = activeSession.value.id ?? activeSession.value.Id
  const pendingTurn = appendPendingFollowUpTurn(normalizedPrompt)
  failedSseSessionId = null
  followUpSending.value = true
  followUpError.value = ''
  sseEvents.value = []
  isRunning.value = true
  statusPhase.value = 'submitting'
  statusMessage.value = '正在提交追问...'
  startElapsedTimer()
  const controller = new AbortController()
  const timeoutId = setTimeout(() => controller.abort(), 90000)
  try {
    const result = await fetchJson(`/api/recommend/sessions/${sessionId}/follow-up`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ userPrompt: normalizedPrompt }),
      signal: controller.signal
    })
    applyOptimisticFollowUpTurn(pendingTurn, result)
    followUpText.value = ''
    const strategy = result.strategy ?? result.Strategy
    if (strategy === 'DirectAnswer') {
      const directAnswer = result.directAnswer ?? result.DirectAnswer
      await loadSessionDetail(sessionId)
      await loadSessionHistory()
      // Inject DirectAnswer into active session's current turn feed as optimistic update
      if (directAnswer && activeSession.value) {
        const turns = activeSession.value.turns ?? activeSession.value.Turns ?? []
        const rTurnId = result.turnId ?? result.TurnId
        const currentTurn = turns.find(t => (t.id ?? t.Id) === rTurnId)
        if (currentTurn) {
          const feedItems = currentTurn.feedItems ?? currentTurn.FeedItems ?? []
          const hasAnswer = feedItems.some(fi => (fi.roleId ?? fi.RoleId) === 'direct_answer')
          if (!hasAnswer) {
            feedItems.push({
              id: Date.now(),
              turnId: rTurnId,
              itemType: 'RoleMessage',
              ItemType: 'RoleMessage',
              roleId: 'direct_answer',
              RoleId: 'direct_answer',
              content: directAnswer,
              Content: directAnswer,
              metadataJson: JSON.stringify({ eventType: 'DirectAnswer', directAnswer: true }),
              MetadataJson: JSON.stringify({ eventType: 'DirectAnswer', directAnswer: true }),
              createdAt: new Date().toISOString(),
              CreatedAt: new Date().toISOString()
            })
            currentTurn.feedItems = feedItems
            currentTurn.FeedItems = feedItems
          }
        }
      }
      statusPhase.value = 'completed'
      statusMessage.value = directAnswer
        ? '已根据现有辩论生成直接回答'
        : '直接回答生成失败'
      stopElapsedTimer()
      isRunning.value = false
      activeTab.value = 'debate'
    } else if (strategy === 'WorkbenchHandoff') {
      await loadSessionDetail(sessionId)
      await loadSessionHistory()
      statusPhase.value = 'completed'
      statusMessage.value = '已转交到个股工作台继续深挖'
      stopElapsedTimer()
      isRunning.value = false
      // Emit navigation event or use simple URL change
      const symbol = result.handoffSymbol ?? result.HandoffSymbol
      if (symbol && typeof window !== 'undefined') {
        window.dispatchEvent(new CustomEvent('navigate-stock', { detail: { symbol } }))
      }
    } else {
      // PartialRerun or FullRerun — re-connect SSE
      isRunning.value = true
      statusPhase.value = 'connecting'
      statusMessage.value = '追问已提交，正在连接新的分析流...'
      await loadSessionDetail(sessionId)
      await loadSessionHistory()
      if (!componentAlive) return
      activeTab.value = 'progress'
    }
  } catch (err) {
    removePendingFollowUpTurn(pendingTurn)
    const isTimeout = err.name === 'AbortError'
    followUpError.value = isTimeout ? '追问请求超时(90秒)，请稍后重试' : (err.message || '追问失败')
    statusPhase.value = 'failed'
    statusMessage.value = followUpError.value
    stopElapsedTimer()
    isRunning.value = false
  } finally {
    clearTimeout(timeoutId)
    followUpSending.value = false
  }
}

const cancelAnalysis = async () => {
  if (activeSession.value) {
    const id = activeSession.value.Id ?? activeSession.value.id
    if (id) {
      try {
        await fetch(`/api/recommend/sessions/${id}/cancel`, { method: 'POST' })
      } catch { /* best-effort */ }
    }
  }
  closeSse()
  stopElapsedTimer()
  isRunning.value = false
  statusPhase.value = 'idle'
  statusMessage.value = '分析已手动停止'
  followUpSending.value = false
}

const handleFollowUpSubmit = () => {
  const text = followUpText.value?.trim()
  if (!text) return
  // If analysis is running, interrupt it first then submit new input
  if (isRunning.value) {
    cancelAnalysis()
  }
  if (activeSession.value) {
    handleFollowUp(text)
  } else {
    handleNewRecommend(text)
  }
}

const handleRetryFromStage = async (fromStageIndex) => {
  if (!activeSession.value || isRunning.value) return
  const sessionId = activeSession.value.id ?? activeSession.value.Id
  failedSseSessionId = null
  isRunning.value = true
  statusPhase.value = 'connecting'
  statusMessage.value = `正在从阶段 ${fromStageIndex} 重新执行...`
  sseEvents.value = []
  startElapsedTimer()
  try {
    await fetchJson(`/api/recommend/sessions/${sessionId}/retry-from-stage`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ fromStageIndex })
    })
    await loadSessionDetail(sessionId)
    activeTab.value = 'progress'
  } catch (err) {
    statusPhase.value = 'failed'
    statusMessage.value = err.message || '重试失败'
    stopElapsedTimer()
    isRunning.value = false
  }
}

const handleClearSession = () => {
  failedSseSessionId = null
  closeSse()
  clearActiveSessionView()
  sseEvents.value = []
  followUpError.value = ''
  statusPhase.value = 'idle'
  statusMessage.value = ''
  stopElapsedTimer()
  activeTab.value = 'report'
}

const inputPlaceholder = computed(() =>
  activeSession.value ? '追问: 半导体再深入看看...' : '输入你的问题，例如：今天有什么值得关注的板块？'
)

const defaultQuickActions = [
  { label: '板块深挖', prompt: '板块再选几只股票' },
  { label: '换方向', prompt: '换个方向看看其他板块' },
  { label: '重新推荐', prompt: '重新推荐' }
]

const quickActions = computed(() => {
  const session = activeSession.value
  if (!session) return defaultQuickActions

  const turns = session.turns ?? session.Turns ?? []
  let selectedSectors = []

  for (const turn of [...turns].reverse()) {
    const snapshots = turn.stageSnapshots ?? turn.StageSnapshots ?? []
    const finalStage = snapshots.find(s =>
      (s.stageType ?? s.StageType) === 'FinalDecision' || (s.stageType ?? s.StageType) === 4)
    if (!finalStage) continue

    const roles = finalStage.roleStates ?? finalStage.RoleStates ?? []
    const director = roles.find(r => (r.roleId ?? r.RoleId) === 'recommend_director')
    if (!director) continue

    try {
      const output = typeof (director.outputContentJson ?? director.OutputContentJson) === 'string'
        ? JSON.parse(director.outputContentJson ?? director.OutputContentJson)
        : (director.outputContentJson ?? director.OutputContentJson)
      if (output) {
        selectedSectors = (output.selectedSectors || output.sectors || []).map(s => s.name || s.sectorName || s).slice(0, 2)
        break
      }
    } catch { /* ignore */ }
  }

  const actions = []

  if (selectedSectors.length > 0) {
    actions.push({ label: `${selectedSectors[0]}深挖`, prompt: `${selectedSectors[0]}板块再选几只股票` })
  } else {
    actions.push({ label: '板块深挖', prompt: '板块再选几只股票' })
  }

  actions.push({ label: '换方向', prompt: '换个方向看看其他板块' })
  actions.push({ label: '重新推荐', prompt: '重新推荐' })

  return actions
})

const handleQuickAction = (prompt) => {
  if (activeSession.value) {
    handleFollowUp(prompt)
  } else {
    handleNewRecommend(prompt)
  }
}

const handleViewStock = (symbol) => {
  if (symbol && typeof window !== 'undefined') {
    window.dispatchEvent(new CustomEvent('navigate-stock', { detail: { symbol } }))
  }
}

const handleDeepAnalyze = (symbol) => {
  if (symbol && typeof window !== 'undefined') {
    window.dispatchEvent(new CustomEvent('navigate-stock', { detail: { symbol, tab: 'workbench' } }))
  }
}

// ---------- Lifecycle ----------
onMounted(() => {
  componentAlive = true
  // Guard: reset stale running state on fresh mount
  if (statusPhase.value !== 'completed' && statusPhase.value !== 'idle' && statusPhase.value !== 'failed') {
    statusPhase.value = 'idle'
    isRunning.value = false
  }
  fetchMarketContext()
  // V048-S2 #82: 切走再切回时自动检测运行中会话并续上 SSE，
  // 避免推荐 Tab 默认空白、必须手点左侧条目才能恢复
  void resumeRunningSessionIfAny()
})

onUnmounted(() => {
  componentAlive = false
  closeSse()
  stopElapsedTimer()
})

// V048-S2 #82: keep-alive re-activation — reconnect SSE if session still running
onActivated(() => {
  componentAlive = true
  const session = activeSession.value
  if (!session) {
    void resumeRunningSessionIfAny()
    return
  }
  const sessionStatus = getSessionStatusValue(session)
  const activeTurn = getActiveSessionTurn(session)
  const turnStatus = getTurnStatusValue(activeTurn)
  const sessionId = session.id ?? session.Id
  const needsSse = (sessionStatus === 'Running' || TURN_RUNNING_STATUSES.has(turnStatus) || TURN_LIVE_STATUSES.has(turnStatus))
    && sessionId != null
    && eventSourceSessionId !== sessionId
  if (needsSse) {
    failedSseSessionId = null
    sseRetryCount = 0
    statusPhase.value = 'connecting'
    statusMessage.value = '检测到会话仍在执行，正在重新连接分析流...'
    const startedAt = getTurnStartedAt(activeTurn) ?? getTurnRequestedAt(activeTurn) ?? Date.now()
    startElapsedTimer(startedAt)
    isRunning.value = true
    connectSse(sessionId)
  }
})

onDeactivated(() => {
  // Don't close SSE on deactivation — keep the connection alive
  // so events are not lost while the tab is inactive.
  // SSE will be cleaned up properly in onUnmounted.
})

watch(realtimeContextEnabled, value => {
  localStorage.setItem('stock_recommend_realtime_context_enabled', String(value))
  if (value) { fetchMarketContext(); return }
  realtimeOverview.value = null
  realtimeSectors.value = []
  marketError.value = ''
})
</script>

<template>
  <section class="panel">
    <div class="panel-header">
      <h2>股票推荐</h2>
      <p class="muted">13-Agent 多阶段辩论推荐系统</p>
    </div>

    <div class="recommend-split">
      <!-- Left sidebar: market snapshot + session history -->
      <aside class="recommend-sidebar" :class="{ collapsed: !realtimeContextEnabled }">
        <section class="recommend-market-card">
          <div class="recommend-market-head">
            <div>
              <p class="recommend-market-kicker">Realtime Context</p>
              <h3>推荐前市场快照</h3>
              <p class="muted">先看指数、资金和实时板块榜，再决定让推荐系统往哪个方向发力。</p>
            </div>
            <div class="session-selector">
              <button class="session-new market-toggle" @click="fetchMarketContext" :disabled="marketLoading || !realtimeContextEnabled">刷新快照</button>
              <button class="session-new market-toggle secondary" @click="realtimeContextEnabled = !realtimeContextEnabled">
                {{ realtimeContextEnabled ? '隐藏快照' : '显示快照' }}
              </button>
            </div>
          </div>

          <p v-if="!realtimeContextEnabled" class="muted">推荐前市场快照已隐藏。</p>
          <template v-else>
            <p v-if="marketError" class="muted error">{{ marketError }}</p>
            <p v-else-if="marketLoading && !realtimeOverview" class="muted">加载中...</p>
            <template v-else-if="realtimeOverview">
              <div class="recommend-index-grid">
                <article v-for="item in realtimeOverview.indices" :key="item.symbol" class="recommend-index-card">
                  <span>{{ item.name }}</span>
                  <strong>{{ item.price.toFixed(2) }}</strong>
                  <small :class="getChangeClass(item.changePercent)">{{ formatSignedPercent(item.changePercent) }}</small>
                </article>
              </div>
              <div class="recommend-market-pills">
                <span class="market-pill">主力 {{ formatSignedAmount(realtimeOverview.mainCapitalFlow?.mainNetInflow) }}</span>
                <span class="market-pill">北向 {{ formatSignedAmount(realtimeOverview.northboundFlow?.totalNetInflow) }}</span>
                <span class="market-pill">涨跌 {{ realtimeOverview.breadth?.advancers ?? 0 }} / {{ realtimeOverview.breadth?.decliners ?? 0 }}</span>
                <span class="market-pill">涨停 {{ realtimeOverview.breadth?.limitUpCount ?? 0 }} / 跌停 {{ realtimeOverview.breadth?.limitDownCount ?? 0 }}</span>
                <span class="market-pill">时间 {{ formatDate(realtimeOverview.snapshotTime) }}</span>
              </div>
              <div class="recommend-sector-list">
                <article v-for="item in topSectors" :key="item.sectorCode" class="recommend-sector-card">
                  <div>
                    <strong>#{{ item.rankNo }} {{ item.sectorName }}</strong>
                    <small :class="getChangeClass(item.changePercent)">{{ formatSignedPercent(item.changePercent) }}</small>
                  </div>
                  <small :class="getChangeClass(item.mainNetInflow)">主力 {{ formatSignedAmount(item.mainNetInflow / 100000000) }}</small>
                </article>
              </div>
              <p v-if="!topSectors.length" class="muted">实时板块榜当前为空，推荐分析将继续使用指数与资金快照。</p>
            </template>
          </template>
        </section>

        <!-- Session history -->
        <section class="session-history">
          <div class="session-history-head">
            <h4>历史推荐</h4>
            <button class="session-new" @click="handleClearSession" :disabled="isRunning">新建推荐</button>
          </div>
          <div v-if="!sessionHistory.length" class="muted" style="padding:0.5rem 0">暂无历史推荐记录。</div>
          <div v-else class="session-list">
            <button v-for="s in sessionHistory" :key="s.id ?? s.Id"
              class="session-item" :class="{ active: (activeSession?.id ?? activeSession?.Id) === (s.id ?? s.Id) }"
              @click="selectSession(s)">
              <div class="session-prompt" v-if="getSessionPrompt(s)">
                {{ getSessionPrompt(s) }}
              </div>
              <div class="session-meta">
                <span class="session-date">{{ formatDate(s.createdAt ?? s.CreatedAt) }}</span>
                <span class="session-turns" v-if="getSessionTurnCount(s) > 0">
                  {{ getSessionTurnCount(s) }}轮
                </span>
                <span :class="['session-status-badge', statusClass(s)]">
                  {{ statusLabel(s) }}
                </span>
              </div>
            </button>
          </div>
        </section>
      </aside>

      <!-- Main content area -->
      <div class="recommend-main">
        <!-- Status banner -->
        <div v-if="statusPhase !== 'idle'" class="status-banner" :class="'status-' + statusPhase">
          <span class="status-indicator" :class="{ 'status-pulse': statusPhase === 'submitting' || statusPhase === 'connecting' || statusPhase === 'running' }"></span>
          <span class="status-text">{{ statusMessage }}</span>
          <span v-if="statusPhase === 'submitting' || statusPhase === 'connecting' || statusPhase === 'running'" class="status-elapsed">{{ elapsedSeconds }}s</span>
          <button v-if="statusPhase === 'submitting' || statusPhase === 'connecting' || statusPhase === 'running'" class="status-cancel" @click="cancelAnalysis" title="停止分析">⏹ 停止</button>
          <button v-if="statusPhase === 'completed' || statusPhase === 'failed'" class="status-dismiss" @click="statusPhase = 'idle'; statusMessage = ''">✕</button>
        </div>

        <div v-if="statusPhase === 'failed'" class="sse-reconnect-bar">
          <div class="sse-error-text">
            <span>{{ statusMessage }}</span>
            <small>最后事件：{{ formatSseEventTime(lastSseEventAt) }} · {{ sseBackendStatusText || '后台状态不明' }} · 重试 {{ sseRetryCount }}/{{ SSE_MAX_RETRIES }}<template v-if="sseReconnectDelaySeconds"> · 最近退避 {{ sseReconnectDelaySeconds }}s</template></small>
          </div>
          <button class="sse-reconnect-btn" @click="reconnectSse">
            再试一次
          </button>
        </div>

        <!-- Bug #59: Empty guide when no active session -->
        <div v-if="!activeSession && statusPhase === 'idle' && !isRunning" class="recommend-empty-guide">
          <div class="empty-guide-icon">🤖</div>
          <h3>智能选股推荐</h3>
          <p>在下方输入你的投资问题，或点击快捷按钮开始分析。</p>
          <p class="muted">系统将调度 13 个 AI 角色，通过多阶段辩论为你筛选优质标的。</p>
        </div>

        <template v-else>
          <!-- Tab bar -->
          <div class="tab-bar">
            <button class="tab-btn" :class="{ active: activeTab === 'report' }" @click="activeTab = 'report'">推荐报告</button>
            <button class="tab-btn" :class="{ active: activeTab === 'debate' }" @click="activeTab = 'debate'">辩论过程</button>
            <button class="tab-btn" :class="{ active: activeTab === 'progress' }" @click="activeTab = 'progress'">团队进度</button>
          </div>

          <!-- Tab content -->
          <div class="tab-content">
            <RecommendReportCard v-if="activeTab === 'report'"
              :session="activeSession"
              @view-stock="handleViewStock"
              @deep-analyze="handleDeepAnalyze" />
            <RecommendFeed v-else-if="activeTab === 'debate'"
              :session="activeSession"
              :sse-events="sseEvents"
              :is-running="isRunning" />
            <RecommendProgress v-else-if="activeTab === 'progress'"
              :session="activeSession"
              :sse-events="sseEvents"
              :is-running="isRunning"
              @retry-from-stage="handleRetryFromStage" />
          </div>
        </template>

        <!-- Follow-up input -->
        <div class="follow-up-bar">
          <div class="quick-actions">
            <button v-for="action in quickActions" :key="action.label"
              class="quick-btn" @click="handleQuickAction(action.prompt)"
              :disabled="followUpSending">
              {{ action.label }}
            </button>
          </div>
          <div class="follow-up-input-row">
            <input v-model="followUpText" class="follow-up-input" type="text"
              :placeholder="isRunning ? '输入新问题可打断当前分析...' : inputPlaceholder"
              :disabled="followUpSending"
              @keydown.enter="handleFollowUpSubmit" />
            <button class="follow-up-send" @click="handleFollowUpSubmit"
              :disabled="!followUpText.trim() || followUpSending">{{ isRunning ? '打断并发送' : '发送' }}</button>
          </div>
          <p v-if="followUpError" class="follow-up-error">{{ followUpError }}</p>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.panel {
  background: var(--color-bg-surface);
  border: 1px solid var(--color-border-light);
  box-shadow: var(--shadow-md);
  border-radius: var(--radius-xl);
  padding: var(--space-6);
}

.recommend-split {
  display: grid;
  grid-template-columns: 360px 1fr;
  gap: var(--space-4);
  min-height: 0;
}

.recommend-sidebar {
  min-width: 0;
  overflow-y: auto;
  max-height: calc(100vh - 200px);
}

.recommend-sidebar.collapsed {
  grid-column: 1;
}

.recommend-main {
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}

@media (max-width: 960px) {
  .recommend-split {
    grid-template-columns: 1fr;
  }
}

.panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
}

.session-selector {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.session-new {
  border-radius: var(--radius-full);
  border: none;
  padding: 0.35rem 0.8rem;
  background: var(--color-bg-surface-alt);
  color: var(--color-text-heading);
  cursor: pointer;
}

.session-new:disabled { opacity: 0.5; cursor: not-allowed; }

.recommend-market-card {
  display: grid;
  gap: 0.9rem;
  margin-bottom: 1rem;
  padding: var(--space-4);
  border-radius: var(--radius-lg);
  background: var(--color-bg-surface-alt);
  border: 1px solid var(--color-border-light);
}

.recommend-market-head {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  align-items: flex-start;
}

.recommend-market-kicker {
  margin: 0 0 0.35rem;
  font-size: 12px;
  letter-spacing: 0.18em;
  text-transform: uppercase;
  color: var(--color-accent);
}

.recommend-market-head h3 { margin: 0; }

.market-toggle.secondary { background: var(--color-bg-surface); }

.recommend-index-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(120px, 1fr));
  gap: 0.75rem;
}

.recommend-index-card,
.recommend-sector-card {
  display: grid;
  gap: 0.25rem;
  padding: 0.75rem;
  border-radius: var(--radius-md);
  background: var(--color-bg-surface);
  border: 1px solid var(--color-border-light);
}

.recommend-market-pills {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.market-pill {
  padding: 0.35rem 0.7rem;
  border-radius: var(--radius-full);
  background: var(--color-bg-surface);
  color: var(--color-text-secondary);
  font-size: 0.85rem;
  border: 1px solid var(--color-border-light);
}

.recommend-sector-list {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 0.75rem;
}

.positive { color: var(--color-market-rise); }
.negative { color: var(--color-market-fall); }

/* Session history */
.session-history {
  padding: var(--space-3) 0;
}
.session-history-head {
  display: flex; justify-content: space-between; align-items: center;
  margin-bottom: 0.5rem;
}
.session-history-head h4 { margin: 0; font-size: 0.9rem; }
.session-list { display: flex; flex-direction: column; gap: 0.25rem; max-height: 200px; overflow-y: auto; }
.session-item {
  display: flex; justify-content: space-between; align-items: center;
  padding: 0.4rem 0.6rem; border-radius: var(--radius-md);
  background: var(--color-bg-surface); border: 1px solid transparent;
  cursor: pointer; font-size: 0.82rem; text-align: left; width: 100%;
}
.session-item:hover { border-color: var(--color-border-light); }
.session-item.active { border-color: var(--color-accent); background: var(--color-bg-surface-alt); }
.session-date { color: var(--color-text-secondary); }
.session-status { font-size: 0.75rem; color: var(--color-text-secondary); }

/* Tab bar */
.recommend-empty-guide {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 3rem 1.5rem;
  text-align: center;
  flex: 1;
}
.empty-guide-icon {
  font-size: 3rem;
  margin-bottom: 0.75rem;
}
.recommend-empty-guide h3 {
  margin: 0 0 0.5rem;
  font-size: 1.25rem;
  color: var(--color-text-heading);
}
.recommend-empty-guide p {
  margin: 0.25rem 0;
  font-size: 0.9rem;
  color: var(--color-text-secondary);
}

.tab-bar {
  display: flex; align-items: center; gap: 0.5rem;
  border-bottom: 1px solid var(--color-border-light); padding-bottom: 0.5rem;
}
.tab-btn {
  padding: 0.4rem 0.8rem; border-radius: var(--radius-md) var(--radius-md) 0 0;
  border: 1px solid transparent; border-bottom: none;
  background: var(--color-bg-surface); cursor: pointer;
  font-size: 0.85rem; color: var(--color-text-secondary);
}
.tab-btn.active {
  background: var(--color-bg-surface-alt); color: var(--color-text-heading);
  border-color: var(--color-border-light);
}
/* Status banner */
.status-banner {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.6rem 1rem;
  border-radius: var(--radius-md);
  font-size: 0.85rem;
  animation: statusSlideIn 0.3s ease;
}

@keyframes statusSlideIn {
  from { opacity: 0; transform: translateY(-8px); }
  to { opacity: 1; transform: translateY(0); }
}

.status-submitting, .status-connecting {
  background: var(--color-bg-surface-alt);
  border: 1px solid var(--color-border-light);
  color: var(--color-text-secondary);
}

.status-running {
  background: #eff6ff;
  border: 1px solid #3b82f6;
  color: #1d4ed8;
}

.status-completed {
  background: #f0fdf4;
  border: 1px solid #22c55e;
  color: #15803d;
}

.status-failed {
  background: #fef2f2;
  border: 1px solid #ef4444;
  color: #b91c1c;
}

.status-indicator {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex-shrink: 0;
}

.status-submitting .status-indicator,
.status-connecting .status-indicator { background: #94a3b8; }
.status-running .status-indicator { background: #3b82f6; }
.status-completed .status-indicator { background: #22c55e; }
.status-failed .status-indicator { background: #ef4444; }

.status-pulse {
  animation: statusPulse 1.5s ease-in-out infinite;
}

@keyframes statusPulse {
  0%, 100% { opacity: 1; transform: scale(1); }
  50% { opacity: 0.4; transform: scale(1.3); }
}

.status-text {
  flex: 1;
}

.status-elapsed {
  font-variant-numeric: tabular-nums;
  color: inherit;
  opacity: 0.7;
  min-width: 2.5em;
  text-align: right;
}

.status-dismiss {
  background: none;
  border: none;
  cursor: pointer;
  color: inherit;
  opacity: 0.5;
  font-size: 0.9rem;
  padding: 0;
  line-height: 1;
}
.status-dismiss:hover { opacity: 1; }

.status-cancel {
  background: #ef4444;
  border: none;
  border-radius: 4px;
  cursor: pointer;
  color: #fff;
  font-size: 0.8rem;
  font-weight: 600;
  padding: 4px 14px;
  margin-left: 8px;
  line-height: 1.4;
  white-space: nowrap;
}
.status-cancel:hover { background: #dc2626; }

/* Tab content */
.tab-content {
  flex: 1; min-height: 300px; max-height: calc(100vh - 400px);
  overflow-y: auto; padding: var(--space-3) 0;
}

/* Follow-up bar */
.follow-up-bar {
  display: flex; flex-direction: column; gap: 0.5rem;
  padding-top: 0.5rem; border-top: 1px solid var(--color-border-light);
}
.quick-actions { display: flex; gap: 0.5rem; }
.quick-btn {
  padding: 0.3rem 0.6rem; border-radius: var(--radius-full);
  border: 1px solid var(--color-border-light); background: var(--color-bg-surface);
  cursor: pointer; font-size: 0.8rem;
}
.quick-btn:hover { background: var(--color-bg-surface-alt); }
.quick-btn:disabled { opacity: 0.5; cursor: not-allowed; }
.follow-up-input-row { display: flex; gap: 0.5rem; }
.follow-up-input {
  flex: 1; padding: 0.5rem 0.75rem; border-radius: var(--radius-md);
  border: 1px solid var(--color-border-light); background: var(--color-bg-surface);
  font-size: 0.85rem;
}
.follow-up-input:disabled { opacity: 0.5; }
.follow-up-send {
  padding: 0.5rem 1rem; border-radius: var(--radius-md);
  border: none; background: var(--color-accent); color: #fff;
  cursor: pointer; font-size: 0.85rem;
}
.follow-up-send:disabled { opacity: 0.5; cursor: not-allowed; }
.follow-up-error { color: var(--color-market-fall); font-size: 0.82rem; margin: 0; }

/* Session history summaries */
.session-prompt {
  font-size: 13px;
  color: var(--color-text, #333);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  margin-bottom: 4px;
}
.session-meta {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 11px;
  color: var(--color-text-secondary, #999);
}
.session-turns {
  color: var(--color-text-secondary, #888);
}
.session-status-badge {
  padding: 1px 6px;
  border-radius: 8px;
  font-size: 10px;
}
.status-success { background: #e6f7e6; color: #389e0d; }
.status-error { background: #fff2f0; color: #cf1322; }
.status-running { background: #e6f7ff; color: #1677ff; }
.status-warning { background: #fffbe6; color: #d48806; }
.status-default { background: #f5f5f5; color: #999; }

/* SSE reconnect bar */
.sse-reconnect-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  background: #fff2f0;
  border: 1px solid #ffccc7;
  border-radius: 6px;
  margin: 8px 12px;
}
.sse-error-text {
  display: grid;
  gap: 2px;
  font-size: 12px;
  color: #cf1322;
  flex: 1;
}
.sse-error-text small {
  color: #8c4a44;
}
.sse-reconnect-btn {
  padding: 4px 12px;
  background: #1677ff;
  color: #fff;
  border: none;
  border-radius: 4px;
  cursor: pointer;
  font-size: 12px;
  white-space: nowrap;
}
.sse-reconnect-btn:hover {
  background: #4096ff;
}
</style>
