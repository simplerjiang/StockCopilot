import { ref, computed, watch, onUnmounted } from 'vue'
import { loadTranslations } from '../../../utils/jsonMarkdownService'

const API_BASE = '/api/stocks/research'
const ROLE_LABELS = {
  CompanyOverviewAnalyst: '公司概览',
  MarketAnalyst: '市场分析',
  SocialSentimentAnalyst: '社交情绪',
  NewsAnalyst: '新闻事件',
  FundamentalsAnalyst: '基本面',
  ShareholderAnalyst: '股东结构',
  ProductAnalyst: '产品业务',
  BullResearcher: '看多研究员',
  BearResearcher: '看空研究员',
  ResearchManager: '研究主管',
  Trader: '交易员',
  AggressiveRiskAnalyst: '激进风控',
  NeutralRiskAnalyst: '中性风控',
  ConservativeRiskAnalyst: '保守风控',
  PortfolioManager: '组合经理',
  // snake_case variants (backend sends these)
  company_overview_analyst: '公司概览',
  market_analyst: '市场分析',
  social_sentiment_analyst: '社交情绪',
  news_analyst: '新闻事件',
  fundamentals_analyst: '基本面',
  shareholder_analyst: '股东结构',
  product_analyst: '产品业务',
  bull_researcher: '看多研究员',
  bear_researcher: '看空研究员',
  research_manager: '研究主管',
  trader: '交易员',
  aggressive_risk_analyst: '激进风控',
  neutral_risk_analyst: '中性风控',
  conservative_risk_analyst: '保守风控',
  portfolio_manager: '组合经理'
}

async function apiGet(path, signal) {
  const res = await fetch(`${API_BASE}${path}`, { signal })
  if (!res.ok) {
    if (res.status === 404) return null
    throw new Error(`API ${res.status}: ${res.statusText}`)
  }
  const text = await res.text()
  if (!text || !text.trim()) return null
  try { return JSON.parse(text) } catch { return null }
}

async function apiPost(path, body) {
  const res = await fetch(`${API_BASE}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  })
  if (!res.ok) {
    const errText = await res.text().catch(() => '')
    throw new Error(`API ${res.status}: ${errText || res.statusText}`)
  }
  const text = await res.text()
  if (!text || !text.trim()) return null
  try { return JSON.parse(text) } catch { return null }
}

/** Research stage definitions in pipeline order. */
export const STAGES = [
  { key: 'CompanyOverviewPreflight', label: '公司概览', icon: '🏢' },
  { key: 'AnalystTeam', label: '分析师团队', icon: '📊' },
  { key: 'ResearchDebate', label: '研究辩论', icon: '⚔️' },
  { key: 'TraderProposal', label: '交易方案', icon: '💹' },
  { key: 'RiskDebate', label: '风险评估', icon: '🛡️' },
  { key: 'PortfolioDecision', label: '投资决策', icon: '🎯' }
]

/** Map stage key to ordered index. */
const STAGE_INDEX = Object.fromEntries(STAGES.map((s, i) => [s.key, i]))

/** Status badge properties. */
export const STATUS_MAP = {
  Queued: { label: '排队中', cls: 'status-queued' },
  Running: { label: '执行中', cls: 'status-running' },
  Completed: { label: '已完成', cls: 'status-completed' },
  Failed: { label: '失败', cls: 'status-failed' },
  Cancelled: { label: '已取消', cls: 'status-cancelled' },
  Idle: { label: '空闲', cls: 'status-idle' }
}

/**
 * Composable for Trading Workbench state management.
 * @param {import('vue').Ref<string>} symbolRef - reactive symbol string
 */
export function useTradingWorkbench(symbolRef) {
  // Load backend translation dictionary for JSON key labels
  loadTranslations()

  // ── Core state ─────────────────────────────────────
  const liveSession = ref(null)
  const sessionDetail = ref(null)
  const activeTurn = ref(null)
  const reportBlocks = ref([])
  const decision = ref(null)
  const ragCitations = ref([])
  const feedItems = ref([])
  const loading = ref(false)
  const error = ref(null)
  const activeTab = ref('report') // report | progress | feed | history

  // Replay state
  const replayTurnId = ref(null)
  const replaySessionId = ref(null)
  const replaySession = ref(null)
  const sessions = ref([])
  const expandedHistorySessionId = ref(null)
  const expandedTurns = ref([])
  const historyLoadingSessionId = ref(null)
  const historySessionErrorId = ref(null)
  const historySessionErrorMessage = ref('')

  // Polling
  let pollTimer = null
  let abortCtrl = null
  let viewGeneration = 0
  let historyViewGeneration = 0

  // ── Derived state ──────────────────────────────────

  const session = computed(() => {
    if (replayTurnId.value != null) return replaySession.value
    return liveSession.value
  })

  const stageSnapshots = computed(() => {
    if (!sessionDetail.value?.stageSnapshots) return []
    const snaps = sessionDetail.value.stageSnapshots
    return STAGES.map(s => {
      const snap = snaps.find(ss => ss.stageType === s.key)
      return {
        ...s,
        status: snap?.status ?? 'Pending',
        roles: (snap?.roleStates ?? []).map(role => ({
          ...role,
          roleLabel: ROLE_LABELS[role.roleId] ?? role.roleId
        })),
        degradedFlags: snap?.degradedFlags ?? [],
        started: snap?.startedAt,
        finished: snap?.finishedAt
      }
    })
  })

  const isRunning = computed(() =>
    session.value?.status === 'Running' ||
    activeTurn.value?.status === 'Running' ||
    activeTurn.value?.status === 'Queued'
  )

  const sessionStatus = computed(() => {
    const s = session.value?.status ?? 'Idle'
    return STATUS_MAP[s] ?? STATUS_MAP.Idle
  })

  const currentStageName = computed(() => {
    if (!sessionDetail.value?.stageSnapshots) return null
    const running = sessionDetail.value.stageSnapshots.find(s => s.status === 'Running')
    if (running) {
      const def = STAGES.find(st => st.key === running.stageType)
      return def?.label ?? running.stageType
    }
    return null
  })

  const nextActions = computed(() => decision.value?.nextActions ?? [])

  function createReplaySessionSummary(detail, sessionId) {
    const resolvedId = detail?.id ?? detail?.sessionId ?? sessionId ?? null
    if (resolvedId == null) return null
    return {
      id: resolvedId,
      sessionId: resolvedId,
      name: detail?.name ?? detail?.sessionName ?? null,
      status: detail?.status ?? 'Idle'
    }
  }

  function mergeLiveSessionDetail(detail, sessionId) {
    const resolvedId = detail?.id ?? detail?.sessionId ?? sessionId ?? null
    if (resolvedId == null && !liveSession.value) return
    liveSession.value = {
      ...liveSession.value,
      id: resolvedId ?? liveSession.value?.id ?? null,
      sessionId: liveSession.value?.sessionId ?? resolvedId ?? null,
      name: detail?.name ?? detail?.sessionName ?? liveSession.value?.name ?? null,
      status: detail?.status ?? liveSession.value?.status ?? 'Idle'
    }
  }

  function preserveOptimisticFeedItems() {
    return feedItems.value.filter(item => item?._optimistic)
  }

  function nextViewGeneration() {
    viewGeneration += 1
    return viewGeneration
  }

  function nextHistoryViewGeneration() {
    historyViewGeneration += 1
    return historyViewGeneration
  }

  function isCurrentViewGeneration(generation, signal) {
    return generation === viewGeneration && !signal?.aborted
  }

  function resetExpandedHistoryState() {
    nextHistoryViewGeneration()
    expandedHistorySessionId.value = null
    expandedTurns.value = []
    historyLoadingSessionId.value = null
    historySessionErrorId.value = null
    historySessionErrorMessage.value = ''
  }

  function beginExpandedHistoryLoad(sessionId) {
    const generation = nextHistoryViewGeneration()
    expandedHistorySessionId.value = sessionId
    expandedTurns.value = []
    historyLoadingSessionId.value = sessionId
    historySessionErrorId.value = null
    historySessionErrorMessage.value = ''
    return generation
  }

  function clearDisplayedTurnState(nextFeedItems = []) {
    sessionDetail.value = null
    activeTurn.value = null
    reportBlocks.value = []
    decision.value = null
    ragCitations.value = []
    feedItems.value = nextFeedItems
  }

  function beginTurnTransition(nextFeedItems = []) {
    nextViewGeneration()
    cancelPending()
    replayTurnId.value = null
    replaySessionId.value = null
    replaySession.value = null
    clearDisplayedTurnState(nextFeedItems)
    return viewGeneration
  }

  // ── API Actions ────────────────────────────────────

  function cancelPending() {
    abortCtrl?.abort()
    abortCtrl = new AbortController()
    return abortCtrl.signal
  }

  async function loadActiveSession(generation = viewGeneration) {
    const sym = symbolRef.value
    if (!sym) return
    loading.value = true
    error.value = null
    const signal = cancelPending()
    try {
      const data = await apiGet(`/active-session?symbol=${encodeURIComponent(sym)}`, signal)
      if (!isCurrentViewGeneration(generation, signal)) return
      liveSession.value = data ? { ...data, id: data.sessionId } : null
      if (liveSession.value?.id) {
        await loadSessionDetail(liveSession.value.id, signal, generation)
      } else {
        clearDisplayedTurnState()
      }
    } catch (e) {
      if (e.name !== 'AbortError' && generation === viewGeneration) error.value = e.message
    } finally {
      loading.value = false
    }
  }

  async function loadSessionDetail(sessionId, signal, generation = viewGeneration) {
    signal = signal ?? cancelPending()
    try {
      const data = await apiGet(`/sessions/${sessionId}`, signal)
      if (!isCurrentViewGeneration(generation, signal)) return
      const isReplayDetail = replayTurnId.value != null && replaySessionId.value === sessionId
      sessionDetail.value = data
      const serverItems = data?.feedItems ?? []
      // Preserve optimistic follow-up items whose text is not yet in server data
      const optimistic = feedItems.value.filter(i => i._optimistic)
      const kept = optimistic.filter(oi => {
        const text = (oi.content || oi.summary || '').trim()
        return !serverItems.some(si => {
          const t = (si.type || si.feedType || si.itemType || '').toLowerCase()
          return (t.includes('userfollowup') || t.includes('turnstarted')) &&
            (si.content || si.summary || si.message || '').trim() === text
        })
      })
      feedItems.value = [...serverItems, ...kept]

      if (isReplayDetail) {
        replaySession.value = createReplaySessionSummary(data, sessionId)
      } else {
        mergeLiveSessionDetail(data, sessionId)
      }

      // Find active or latest turn
      const turns = data?.turns ?? []
      const active = (replayTurnId.value
        ? turns.find(t => t.id === replayTurnId.value)
        : null)
        ?? turns.find(t => t.status === 'Running' || t.status === 'Queued')
        ?? turns[turns.length - 1]
      activeTurn.value = active ?? null

      if (active?.id) {
        await loadTurnReport(active.id, signal, generation)
      } else {
        reportBlocks.value = []
        decision.value = null
      }
    } catch (e) {
      if (e.name !== 'AbortError' && generation === viewGeneration) {
        error.value = e.message
        clearDisplayedTurnState(preserveOptimisticFeedItems())
      }
    }
  }

  async function loadTurnReport(turnId, signal, generation = viewGeneration) {
    signal = signal ?? cancelPending()
    try {
      const data = await apiGet(`/turns/${turnId}/report`, signal)
      if (!isCurrentViewGeneration(generation, signal)) return
      reportBlocks.value = data?.blocks ?? []
      decision.value = data?.finalDecision ?? null
      ragCitations.value = data?.ragCitations ?? []
    } catch (e) {
      if (e.name !== 'AbortError' && generation === viewGeneration) {
        error.value = e.message
        reportBlocks.value = []
        decision.value = null
        ragCitations.value = []
      }
    }
  }

  async function submitFollowUp(prompt, options = {}) {
    const sym = symbolRef.value
    if (!sym || !prompt.trim()) return
    error.value = null
    const trimmedPrompt = prompt.trim()
    // Immediately show user's follow-up question in feed before backend responds
    const optimisticItem = {
      id: `optimistic-${Date.now()}`,
      turnId: null,
      type: 'UserFollowUp',
      itemType: 'UserFollowUp',
      content: trimmedPrompt,
      summary: trimmedPrompt,
      createdAt: new Date().toISOString(),
      _optimistic: true
    }
    feedItems.value = [...feedItems.value, optimisticItem]
    try {
      const body = {
        symbol: sym,
        userPrompt: trimmedPrompt,
        continuationMode: options.continuationMode ?? 'ContinueSession',
        sessionKey: liveSession.value?.sessionKey || undefined
      }
      const result = await apiPost('/turns', body)
      // Refresh session after submission
      if (result?.sessionId) {
        const optimisticItems = feedItems.value.filter(i => i._optimistic)
        const generation = beginTurnTransition(optimisticItems)
        liveSession.value = { ...liveSession.value, id: result.sessionId, status: 'Running' }
        startPolling()
        await loadSessionDetail(result.sessionId, undefined, generation)
      }
      return result
    } catch (e) {
      // Remove optimistic item on failure
      feedItems.value = feedItems.value.filter(i => !i._optimistic)
      error.value = e.message
      throw e
    }
  }

  async function loadSessions() {
    const sym = symbolRef.value
    if (!sym) return
    try {
      const data = await apiGet(`/sessions?symbol=${encodeURIComponent(sym)}&limit=20`)
      sessions.value = data ?? []
    } catch (e) {
      if (e.name !== 'AbortError') error.value = e.message
    }
  }

  async function switchReplayTurn(turnId) {
    replayTurnId.value = turnId
    if (turnId) {
      await loadTurnReport(turnId)
    }
  }

  async function expandHistorySession(sessionId) {
    const isSameExpandedSession = expandedHistorySessionId.value === sessionId
    const shouldRetryExpandedError = isSameExpandedSession && historySessionErrorId.value === sessionId

    if (isSameExpandedSession && !shouldRetryExpandedError) {
      resetExpandedHistoryState()
      return
    }

    const generation = beginExpandedHistoryLoad(sessionId)
    try {
      const detail = await apiGet(`/sessions/${sessionId}`)
      if (generation !== historyViewGeneration || expandedHistorySessionId.value !== sessionId) return
      expandedTurns.value = detail?.turns ?? []
      historyLoadingSessionId.value = null
    } catch (e) {
      if (e.name !== 'AbortError' && generation === historyViewGeneration && expandedHistorySessionId.value === sessionId) {
        expandedTurns.value = []
        historyLoadingSessionId.value = null
        historySessionErrorId.value = sessionId
        historySessionErrorMessage.value = '历史记录加载失败，请重试。'
      }
    }
  }

  async function enterReplay(sessionId, turnId) {
    stopPolling()
    error.value = null
    const generation = nextViewGeneration()
    cancelPending()
    replayTurnId.value = turnId
    replaySessionId.value = sessionId
    replaySession.value = null
    await loadSessionDetail(sessionId, undefined, generation)
    activeTab.value = 'report'
  }

  function exitReplay() {
    const generation = beginTurnTransition()
    liveSession.value = null
    resetExpandedHistoryState()
    loadActiveSession(generation)
  }

  async function rerunFromStage(stageIndex) {
    const sym = symbolRef.value
    if (!sym || isRunning.value) return
    error.value = null
    try {
      const body = {
        symbol: sym,
        userPrompt: activeTurn.value?.userPrompt || '重新分析',
        sessionKey: liveSession.value?.sessionKey || undefined,
        continuationMode: 'PartialRerun',
        fromStageIndex: stageIndex
      }
      const result = await apiPost('/turns', body)
      if (result?.sessionId) {
        const generation = beginTurnTransition()
        liveSession.value = { ...liveSession.value, id: result.sessionId, status: 'Running' }
        startPolling()
        activeTab.value = 'feed'
        await loadSessionDetail(result.sessionId, undefined, generation)
      }
      return result
    } catch (e) {
      error.value = e.message
      throw e
    }
  }

  async function cancelAnalysis() {
    const sessionId = liveSession.value?.id
    if (!sessionId) return false
    error.value = null
    try {
      await apiPost(`/sessions/${sessionId}/cancel`, {})
      stopPolling()
      await loadActiveSession()
      return true
    } catch (e) {
      error.value = e.message
      return false
    }
  }

  // ── Polling ────────────────────────────────────────

  let pollInFlight = false

  function startPolling() {
    stopPolling()
    pollTimer = setInterval(async () => {
      if (!liveSession.value?.id || pollInFlight) return
      pollInFlight = true
      const ctrl = new AbortController()
      try {
        await loadSessionDetail(liveSession.value.id, ctrl.signal)
        if (!isRunning.value) stopPolling()
      } catch (e) {
        if (e?.name !== 'AbortError') console.warn('[workbench] poll error:', e)
      } finally {
        pollInFlight = false
      }
    }, 1500)
  }

  function stopPolling() {
    if (pollTimer) {
      clearInterval(pollTimer)
      pollTimer = null
    }
    pollInFlight = false
  }

  // ── Symbol watcher ─────────────────────────────────

  watch(symbolRef, (newSym) => {
    stopPolling()
    nextViewGeneration()
    cancelPending()
    liveSession.value = null
    clearDisplayedTurnState()
    resetExpandedHistoryState()
    replayTurnId.value = null
    replaySessionId.value = null
    replaySession.value = null
    error.value = null
    if (newSym) loadActiveSession()
  }, { immediate: true })

  onUnmounted(() => {
    stopPolling()
    abortCtrl?.abort()
  })

  return {
    // State
    session,
    sessionDetail,
    activeTurn,
    reportBlocks,
    decision,
    ragCitations,
    feedItems,
    loading,
    error,
    activeTab,
    sessions,
    expandedHistorySessionId,
    expandedTurns,
    historyLoadingSessionId,
    historySessionErrorId,
    historySessionErrorMessage,
    replayTurnId,

    // Derived
    stageSnapshots,
    isRunning,
    sessionStatus,
    currentStageName,
    nextActions,

    // Actions
    loadActiveSession,
    loadSessionDetail,
    loadTurnReport,
    submitFollowUp,
    loadSessions,
    switchReplayTurn,
    expandHistorySession,
    enterReplay,
    exitReplay,
    rerunFromStage,
    cancelAnalysis,
    startPolling,
    stopPolling
  }
}
