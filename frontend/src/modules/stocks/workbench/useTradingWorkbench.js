import { ref, computed, watch, onUnmounted } from 'vue'

const API_BASE = '/api/stocks/research'

async function apiGet(path, signal) {
  const res = await fetch(`${API_BASE}${path}`, { signal })
  if (!res.ok) {
    if (res.status === 404) return null
    throw new Error(`API ${res.status}: ${res.statusText}`)
  }
  return res.json()
}

async function apiPost(path, body) {
  const res = await fetch(`${API_BASE}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  })
  if (!res.ok) throw new Error(`API ${res.status}: ${res.statusText}`)
  return res.json()
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
  // ── Core state ─────────────────────────────────────
  const session = ref(null)
  const sessionDetail = ref(null)
  const activeTurn = ref(null)
  const reportBlocks = ref([])
  const decision = ref(null)
  const feedItems = ref([])
  const loading = ref(false)
  const error = ref(null)
  const activeTab = ref('report') // report | progress | feed

  // Replay state
  const replayTurnId = ref(null)
  const sessions = ref([])

  // Polling
  let pollTimer = null
  let abortCtrl = null

  // ── Derived state ──────────────────────────────────

  const stageSnapshots = computed(() => {
    if (!sessionDetail.value?.stageSnapshots) return []
    const snaps = sessionDetail.value.stageSnapshots
    return STAGES.map(s => {
      const snap = snaps.find(ss => ss.stageType === s.key)
      return {
        ...s,
        status: snap?.status ?? 'Pending',
        roles: snap?.roleStates ?? [],
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

  // ── API Actions ────────────────────────────────────

  function cancelPending() {
    abortCtrl?.abort()
    abortCtrl = new AbortController()
    return abortCtrl.signal
  }

  async function loadActiveSession() {
    const sym = symbolRef.value
    if (!sym) return
    loading.value = true
    error.value = null
    const signal = cancelPending()
    try {
      const data = await apiGet(`/active-session?symbol=${encodeURIComponent(sym)}`, signal)
      session.value = data
      if (data?.id) {
        await loadSessionDetail(data.id, signal)
      } else {
        sessionDetail.value = null
        activeTurn.value = null
        reportBlocks.value = []
        decision.value = null
        feedItems.value = []
      }
    } catch (e) {
      if (e.name !== 'AbortError') error.value = e.message
    } finally {
      loading.value = false
    }
  }

  async function loadSessionDetail(sessionId, signal) {
    signal = signal ?? cancelPending()
    try {
      const data = await apiGet(`/sessions/${sessionId}`, signal)
      sessionDetail.value = data
      feedItems.value = data?.feedItems ?? []

      // Find active or latest turn
      const turns = data?.turns ?? []
      const active = turns.find(t => t.status === 'Running' || t.status === 'Queued')
        ?? turns[turns.length - 1]
      activeTurn.value = active ?? null

      if (active?.id) {
        await loadTurnReport(active.id, signal)
      }
    } catch (e) {
      if (e.name !== 'AbortError') error.value = e.message
    }
  }

  async function loadTurnReport(turnId, signal) {
    signal = signal ?? cancelPending()
    try {
      const data = await apiGet(`/turns/${turnId}/report`, signal)
      reportBlocks.value = data?.blocks ?? []
      decision.value = data?.finalDecision ?? null
    } catch (e) {
      if (e.name !== 'AbortError') error.value = e.message
    }
  }

  async function submitFollowUp(prompt, options = {}) {
    const sym = symbolRef.value
    if (!sym || !prompt.trim()) return
    error.value = null
    try {
      const body = {
        symbol: sym,
        prompt: prompt.trim(),
        continuationMode: options.continuationMode ?? 'ContinueSession',
        sessionId: session.value?.id ?? undefined
      }
      const result = await apiPost('/turns', body)
      // Refresh session after submission
      if (result?.sessionId) {
        session.value = { ...session.value, id: result.sessionId, status: 'Running' }
        startPolling()
        await loadSessionDetail(result.sessionId)
      }
      return result
    } catch (e) {
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

  // ── Polling ────────────────────────────────────────

  function startPolling() {
    stopPolling()
    pollTimer = setInterval(async () => {
      if (!session.value?.id) return
      try {
        await loadSessionDetail(session.value.id)
        // Stop polling when no longer running
        if (!isRunning.value) stopPolling()
      } catch (e) { if (e?.name !== 'AbortError') console.warn('[workbench] poll error:', e) }
    }, 3000)
  }

  function stopPolling() {
    if (pollTimer) {
      clearInterval(pollTimer)
      pollTimer = null
    }
  }

  // ── Symbol watcher ─────────────────────────────────

  watch(symbolRef, (newSym) => {
    stopPolling()
    session.value = null
    sessionDetail.value = null
    activeTurn.value = null
    reportBlocks.value = []
    decision.value = null
    feedItems.value = []
    replayTurnId.value = null
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
    feedItems,
    loading,
    error,
    activeTab,
    sessions,
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
    startPolling,
    stopPolling
  }
}
