<script setup>
import { computed, onMounted, onUnmounted, ref } from 'vue'

const props = defineProps({
  session: { type: Object, default: null },
  sseEvents: { type: Array, default: () => [] },
  isRunning: { type: Boolean, default: false }
})

const emit = defineEmits(['retry-from-stage'])

// V048-S2 #85: 1s tick 让 elapsed 秒数自动刷新，给运行中角色显示 ETA 文案
const nowMs = ref(Date.now())
let tickHandle = null
onMounted(() => {
  tickHandle = setInterval(() => { nowMs.value = Date.now() }, 1000)
})
onUnmounted(() => {
  if (tickHandle) { clearInterval(tickHandle); tickHandle = null }
})

// V048-S2 #85: 安全解析 SSE detailJson（可能为 null/字符串/对象）
const parseEventDetail = (detail) => {
  if (!detail) return null
  if (typeof detail === 'object') return detail
  if (typeof detail === 'string') {
    try { return JSON.parse(detail) } catch { return null }
  }
  return null
}

const STAGES = [
  { index: 0, type: 'MarketScan', label: '市场扫描', roles: ['recommend_macro_analyst', 'recommend_sector_hunter', 'recommend_smart_money'] },
  { index: 1, type: 'SectorDebate', label: '板块辩论', roles: ['recommend_sector_bull', 'recommend_sector_bear', 'recommend_sector_judge'] },
  { index: 2, type: 'StockPicking', label: '选股精选', roles: ['recommend_leader_picker', 'recommend_growth_picker', 'recommend_chart_validator'] },
  { index: 3, type: 'StockDebate', label: '个股辩论', roles: ['recommend_stock_bull', 'recommend_stock_bear', 'recommend_risk_reviewer'] },
  { index: 4, type: 'FinalDecision', label: '推荐决策', roles: ['recommend_director'] }
]

const ROLE_LABELS = {
  recommend_macro_analyst: '宏观分析师',
  recommend_sector_hunter: '板块猎手',
  recommend_smart_money: '资金分析师',
  recommend_sector_bull: '板块多头',
  recommend_sector_bear: '板块空头',
  recommend_sector_judge: '板块裁决官',
  recommend_leader_picker: '龙头猎手',
  recommend_growth_picker: '潜力猎手',
  recommend_chart_validator: '技术验证师',
  recommend_stock_bull: '个股多头',
  recommend_stock_bear: '个股空头',
  recommend_risk_reviewer: '风控审查',
  recommend_director: '推荐总监'
}

const TURN_LIVE_STATUSES = new Set(['Pending', 'Queued', 'Running'])

const getSessionTurns = session => {
  if (Array.isArray(session?.turns)) return session.turns
  if (Array.isArray(session?.Turns)) return session.Turns
  return []
}

const getTurnSortValue = turn => {
  const turnIndex = Number(turn?.turnIndex ?? turn?.TurnIndex)
  if (Number.isFinite(turnIndex)) return turnIndex

  const requestedAt = Date.parse(turn?.requestedAt ?? turn?.RequestedAt ?? '')
  if (Number.isFinite(requestedAt)) return requestedAt

  const turnId = Number(turn?.id ?? turn?.Id)
  return Number.isFinite(turnId) ? turnId : -1
}

const getTurnStatus = turn => turn?.status ?? turn?.Status ?? null
const getSessionStatus = session => session?.status ?? session?.Status ?? null
const getSnapshotType = snapshot => snapshot?.stageType ?? snapshot?.StageType ?? null
const getSnapshotStatus = snapshot => snapshot?.status ?? snapshot?.Status ?? null

const getSnapshotRoleStates = snapshot => {
  if (Array.isArray(snapshot?.roleStates)) return snapshot.roleStates
  if (Array.isArray(snapshot?.RoleStates)) return snapshot.RoleStates
  return []
}

const getRoleStateId = roleState => roleState?.roleId ?? roleState?.RoleId ?? null
const getRoleStateStatus = roleState => roleState?.status ?? roleState?.Status ?? null
const getRoleStateOutput = roleState => roleState?.outputContentJson ?? roleState?.OutputContentJson ?? null
const getRoleStateElapsed = roleState => roleState?.elapsed ?? roleState?.Elapsed ?? null
const getTurnFeedItems = turn => Array.isArray(turn?.feedItems)
  ? turn.feedItems
  : Array.isArray(turn?.FeedItems)
    ? turn.FeedItems
    : []
const getSessionFeedItems = session => Array.isArray(session?.feedItems)
  ? session.feedItems
  : Array.isArray(session?.FeedItems)
    ? session.FeedItems
    : []
const getFeedItemTurnId = item => item?.turnId ?? item?.TurnId ?? null

const sessionTurns = computed(() => {
  return getSessionTurns(props.session)
    .slice()
    .sort((left, right) => getTurnSortValue(left) - getTurnSortValue(right))
})

const activeTurn = computed(() => {
  const turns = sessionTurns.value
  if (!turns.length) return null

  const activeTurnId = props.session?.activeTurnId ?? props.session?.ActiveTurnId
  const selectedTurn = turns.find(turn => (turn.id ?? turn.Id) === activeTurnId) || null
  const latestLiveTurn = [...turns].reverse().find(turn => TURN_LIVE_STATUSES.has(getTurnStatus(turn))) || null

  if (latestLiveTurn && !TURN_LIVE_STATUSES.has(getTurnStatus(selectedTurn))) {
    return latestLiveTurn
  }

  return selectedTurn || turns[turns.length - 1]
})

const activeTurnId = computed(() => activeTurn.value?.id ?? activeTurn.value?.Id ?? null)
const sessionStatus = computed(() => getSessionStatus(props.session))

const shouldIsolateCurrentTurn = computed(() => {
  const currentTurnStatus = getTurnStatus(activeTurn.value)
  return props.isRunning
    || sessionStatus.value === 'Running'
    || TURN_LIVE_STATUSES.has(currentTurnStatus)
})

const isTurnTerminalFailed = computed(() => {
  const status = activeTurn.value?.status ?? activeTurn.value?.Status
  return status === 'Failed' || status === 'Cancelled'
})

const getTurnSnapshots = turn => {
  if (Array.isArray(turn?.stageSnapshots)) return turn.stageSnapshots
  if (Array.isArray(turn?.StageSnapshots)) return turn.StageSnapshots
  return []
}

const currentTurnFeedItems = computed(() => {
  const activeId = activeTurnId.value
  const sessionFeedItems = getSessionFeedItems(props.session).filter(item => {
    const eventTurnId = getFeedItemTurnId(item)
    if (eventTurnId == null) return !shouldIsolateCurrentTurn.value
    return activeId == null || eventTurnId === activeId
  })

  return [...sessionFeedItems, ...getTurnFeedItems(activeTurn.value)]
})

const snapshotTurn = computed(() => {
  const turns = sessionTurns.value
  const currentTurn = activeTurn.value
  if (!currentTurn) return null

  const activeSnapshots = getTurnSnapshots(currentTurn)
  if (activeSnapshots.length) return currentTurn

  if (shouldIsolateCurrentTurn.value) return currentTurn

  // Fallback: find any turn that has stageSnapshots (prefer later turns)
  return [...turns].reverse().find(turn => getTurnSnapshots(turn).length) || null
})

const snapshotTurnHasSnapshots = computed(() => getTurnSnapshots(snapshotTurn.value).length > 0)

// When the current turn has no snapshots yet, infer stage status from current-turn feed items.
const feedInferredStageStatus = computed(() => {
  if (snapshotTurnHasSnapshots.value) return {}

  const status = {}

  for (const item of currentTurnFeedItems.value) {
    const eventType = item?.eventType ?? item?.EventType ?? item?.itemType ?? item?.ItemType ?? ''
    const stageType = item?.stageType ?? item?.StageType ?? ''
    if (!stageType) continue

    // Map stageType number to string if needed
    const stageKey = typeof stageType === 'number' ? (STAGES[stageType]?.type ?? stageType) : stageType

    if (eventType === 'StageStarted') status[stageKey] = 'Running'
    else if (eventType === 'StageCompleted') status[stageKey] = 'Completed'
    else if (eventType === 'StageFailed') status[stageKey] = 'Failed'
    else if (eventType === 'DegradedNotice' && !status[stageKey]) status[stageKey] = 'Degraded'
    // Infer from role events too
    else if ((eventType === 'RoleStarted' || eventType === 'RoleCompleted') && !status[stageKey]) {
      status[stageKey] = eventType === 'RoleCompleted' ? 'Completed' : 'Running'
    }
  }
  return status
})

const feedInferredRoleStatus = computed(() => {
  if (snapshotTurnHasSnapshots.value) return {}

  const status = {}

  for (const item of currentTurnFeedItems.value) {
    const eventType = item?.eventType ?? item?.EventType ?? item?.itemType ?? item?.ItemType ?? ''
    const roleId = item?.roleId ?? item?.RoleId ?? ''
    if (!roleId) continue

    if (eventType === 'RoleStarted') status[roleId] = { status: 'Running', toolCalls: 0 }
    else if (eventType === 'RoleCompleted') {
      if (!status[roleId]) status[roleId] = {}
      status[roleId].status = 'Completed'
    }
    else if (eventType === 'RoleFailed') {
      if (!status[roleId]) status[roleId] = {}
      status[roleId].status = 'Failed'
    }
  }
  return status
})

const currentTurnEvents = computed(() => {
  if (!Array.isArray(props.sseEvents)) return []
  return props.sseEvents.filter(event => {
    const eventTurnId = event.turnId ?? event.TurnId ?? null
    return !eventTurnId || !activeTurnId.value || eventTurnId === activeTurnId.value
  })
})

const snapshotMap = computed(() => {
  const map = {}
  const snapshots = getTurnSnapshots(snapshotTurn.value)
  for (const ss of snapshots) {
    const key = getSnapshotType(ss)
    if (key == null) continue

    map[key] = ss

    const matchedStage = STAGES.find(stage => stage.type === key || stage.index === key)
    if (matchedStage) {
      map[matchedStage.type] = ss
      map[matchedStage.index] = ss
    }
  }
  return map
})

const liveStageStatus = computed(() => {
  const status = {}
  for (const e of currentTurnEvents.value) {
    const st = e.stageType
    if (!st) continue
    if (e.eventType === 'StageStarted') status[st] = 'Running'
    if (e.eventType === 'StageCompleted') status[st] = 'Completed'
    if (e.eventType === 'StageFailed') status[st] = 'Failed'
    if (e.eventType === 'DegradedNotice' && !status[st]) status[st] = 'Degraded'
  }
  return status
})

const liveRoleStatus = computed(() => {
  const status = {}
  for (const e of currentTurnEvents.value) {
    if (!e.roleId) continue
    if (e.eventType === 'RoleStarted') {
      // V048-S2 #85: 解析 detailJson 中的 maxToolCalls / startedAt，给 ETA 文案使用
      const meta = parseEventDetail(e.detailJson ?? e.DetailJson)
      status[e.roleId] = {
        status: 'Running',
        toolCalls: 0,
        maxToolCalls: meta?.maxToolCalls ?? null,
        startedAt: meta?.startedAt ?? e.timestamp ?? e.Timestamp ?? null
      }
    }
    if (e.eventType === 'RoleCompleted') {
      if (!status[e.roleId]) status[e.roleId] = {}
      status[e.roleId].status = 'Completed'
    }
    if (e.eventType === 'RoleFailed') {
      if (!status[e.roleId]) status[e.roleId] = {}
      status[e.roleId].status = 'Failed'
      status[e.roleId].errorMessage = e.summary ?? e.Summary ?? null
    }
    if (e.eventType === 'ToolDispatched' || e.eventType === 'ToolCompleted') {
      if (!status[e.roleId]) status[e.roleId] = { status: 'Running', toolCalls: 0 }
      if (e.eventType === 'ToolDispatched') status[e.roleId].toolCalls = (status[e.roleId].toolCalls || 0) + 1
    }
  }
  return status
})

const getStageStatus = stage => {
  const live = liveStageStatus.value[stage.type] || liveStageStatus.value[stage.index]
  if (live) {
    // Frontend fallback: if turn is terminally failed, treat Running stages as Failed
    if (live === 'Running' && isTurnTerminalFailed.value) return 'Failed'
    return live
  }

  const snapshot = snapshotMap.value[stage.type] || snapshotMap.value[stage.index]
  if (snapshot) {
    const status = getSnapshotStatus(snapshot) || 'Pending'
    if (status === 'Running' && isTurnTerminalFailed.value) return 'Failed'
    return status
  }

  // Fallback: infer from feedItems when no snapshots available
  const inferred = feedInferredStageStatus.value[stage.type]
  if (inferred) return inferred

  return 'Pending'
}

const getStageIcon = status => {
  if (status === 'Completed') return '✅'
  if (status === 'Running') return '🔄'
  if (status === 'Degraded') return '⚠️'
  if (status === 'Failed') return '❌'
  if (status === 'Skipped') return '⏭️'
  return '⏳'
}

const getRoleInfo = (stage, roleId) => {
  const live = liveRoleStatus.value[roleId]
  if (live) return live

  const snapshot = snapshotMap.value[stage.type] || snapshotMap.value[stage.index]
  const roleStates = getSnapshotRoleStates(snapshot)
  if (roleStates.length) {
    const rs = roleStates.find(roleState => getRoleStateId(roleState) === roleId)
    if (rs) return {
      status: getRoleStateStatus(rs) || (getRoleStateOutput(rs) ? 'Completed' : 'Pending'),
      toolCalls: 0,
      elapsed: getRoleStateElapsed(rs),
      errorMessage: rs?.errorMessage ?? rs?.ErrorMessage ?? null
    }
  }

  // Fallback: infer from feedItems
  const inferred = feedInferredRoleStatus.value[roleId]
  if (inferred) return inferred

  return { status: 'Pending', toolCalls: 0 }
}

const STAGE_STATUS_LABELS = {
  Pending: '待执行',
  Running: '进行中',
  Completed: '已完成',
  Degraded: '降级完成',
  Failed: '失败',
  Skipped: '已跳过'
}

const stageStatusLabel = status => STAGE_STATUS_LABELS[status] || status

const getRoleStatusIcon = status => {
  if (status === 'Completed') return '✅'
  if (status === 'Running') return '🔄'
  if (status === 'Failed') return '❌'
  if (status === 'Degraded') return '⚠️'
  return '○'
}

const formatElapsed = ms => {
  if (!ms) return ''
  if (ms < 1000) return `${ms}ms`
  return `${(ms / 1000).toFixed(1)}s`
}

// V048-S2 #85: 角色 ETA 警示文案
// 触发条件：运行中且（已耗时 > 180s 或 工具用量 > 80%）
const buildRoleEtaInfo = (info) => {
  if (!info || info.status !== 'Running') return null
  const startedAt = info.startedAt ? Date.parse(info.startedAt) : null
  const elapsedSec = startedAt && Number.isFinite(startedAt)
    ? Math.max(0, Math.floor((nowMs.value - startedAt) / 1000))
    : null
  const used = Number(info.toolCalls) || 0
  const max = Number(info.maxToolCalls) || 0
  const toolText = max > 0 ? `🔧 ${used}/${max}` : (used > 0 ? `🔧 ${used}` : '')
  const elapsedText = elapsedSec != null ? `⏱️ ${elapsedSec}s` : ''
  const overTime = elapsedSec != null && elapsedSec > 300
  const overTool = max > 0 && used / max >= 0.8
  const warn = overTime || overTool
  return {
    toolText,
    elapsedText,
    elapsedSec,
    warn,
    warnLabel: warn
      ? (overTime && overTool ? '⏳ 已超过预期时间且接近工具上限' : (overTime ? '⏳ 已超过预期时间' : '工具使用接近上限'))
      : ''
  }
}

const truncateError = (msg) => {
  if (!msg) return ''
  return msg.length > 40 ? msg.slice(0, 40) + '...' : msg
}

const firstFailedStageIndex = computed(() => {
  for (const stage of STAGES) {
    const status = getStageStatus(stage)
    if (status === 'Failed') return stage.index
  }
  return null
})

const hasFailedStages = computed(() => firstFailedStageIndex.value !== null)
</script>

<template>
  <div class="progress">
    <div v-if="!session && !isRunning" class="progress-empty">
      <p class="muted">输入你的问题，启动推荐分析。</p>
    </div>
    <div v-else-if="!session && isRunning" class="progress-empty progress-waiting">
      <div class="waiting-animation">
        <span class="waiting-dot"></span>
        <span class="waiting-dot"></span>
        <span class="waiting-dot"></span>
      </div>
      <p>正在准备分析团队...</p>
    </div>
    <div v-else class="progress-stages">
      <div v-for="stage in STAGES" :key="stage.index" class="stage-row"
        :class="{ 'stage-active': getStageStatus(stage) === 'Running' }">
        <div class="stage-header">
          <span class="stage-icon">{{ getStageIcon(getStageStatus(stage)) }}</span>
          <strong>{{ stage.label }}</strong>
          <span class="stage-status">{{ stageStatusLabel(getStageStatus(stage)) }}</span>
          <button v-if="getStageStatus(stage) === 'Failed' && !isRunning"
            class="stage-retry-btn" @click.stop="emit('retry-from-stage', stage.index)"
            title="从此阶段开始重跑">
            🔄 重试
          </button>
        </div>
        <div class="stage-roles">
          <div v-for="roleId in stage.roles" :key="roleId" class="role-row"
            :class="{ 'role-failed': getRoleInfo(stage, roleId).status === 'Failed' }">
            <span class="role-status-icon">{{ getRoleStatusIcon(getRoleInfo(stage, roleId).status) }}</span>
            <span class="role-name">{{ ROLE_LABELS[roleId] || roleId }}</span>
            <template v-if="buildRoleEtaInfo(getRoleInfo(stage, roleId))">
              <span v-if="buildRoleEtaInfo(getRoleInfo(stage, roleId)).toolText" class="role-tools">
                {{ buildRoleEtaInfo(getRoleInfo(stage, roleId)).toolText }}
              </span>
              <span v-if="buildRoleEtaInfo(getRoleInfo(stage, roleId)).elapsedText" class="role-elapsed-live">
                {{ buildRoleEtaInfo(getRoleInfo(stage, roleId)).elapsedText }}
              </span>
              <span v-if="buildRoleEtaInfo(getRoleInfo(stage, roleId)).warn" class="role-eta-warn"
                :title="buildRoleEtaInfo(getRoleInfo(stage, roleId)).warnLabel">
                ⚠️ {{ buildRoleEtaInfo(getRoleInfo(stage, roleId)).warnLabel }}
              </span>
            </template>
            <template v-else>
              <span v-if="getRoleInfo(stage, roleId).toolCalls" class="role-tools">
                🔧 {{ getRoleInfo(stage, roleId).toolCalls }}
              </span>
              <span v-if="getRoleInfo(stage, roleId).elapsed" class="role-elapsed">
                {{ formatElapsed(getRoleInfo(stage, roleId).elapsed) }}
              </span>
            </template>
            <span v-if="getRoleInfo(stage, roleId).status === 'Failed' && getRoleInfo(stage, roleId).errorMessage"
              class="role-error" :title="getRoleInfo(stage, roleId).errorMessage">
              {{ truncateError(getRoleInfo(stage, roleId).errorMessage) }}
            </span>
          </div>
        </div>
      </div>
      <div v-if="hasFailedStages && !isRunning" class="progress-retry-bar">
        <button class="progress-retry-btn" @click="emit('retry-from-stage', firstFailedStageIndex)">
          🔄 从失败处继续
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.progress { display: flex; flex-direction: column; gap: 0.75rem; }
.progress-empty { padding: 2rem; text-align: center; }
.progress-waiting {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1rem;
}
.waiting-animation {
  display: flex;
  gap: 0.4rem;
}
.waiting-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  background: var(--color-accent, #3b82f6);
  animation: waitingBounce 1.4s ease-in-out infinite;
}
.waiting-dot:nth-child(2) { animation-delay: 0.2s; }
.waiting-dot:nth-child(3) { animation-delay: 0.4s; }
@keyframes waitingBounce {
  0%, 80%, 100% { transform: scale(0.6); opacity: 0.4; }
  40% { transform: scale(1); opacity: 1; }
}
.progress-stages { display: flex; flex-direction: column; gap: 0.5rem; }
.stage-row {
  padding: 0.75rem; border-radius: var(--radius-md);
  background: var(--color-bg-surface-alt); border: 1px solid var(--color-border-light);
}
.stage-active { border-color: var(--color-accent); box-shadow: 0 0 0 1px var(--color-accent); }
.stage-header { display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.4rem; }
.stage-icon { font-size: 1rem; }
.stage-status { margin-left: auto; font-size: 0.8rem; color: var(--color-text-secondary); }
.stage-roles { display: flex; flex-direction: column; gap: 0.25rem; padding-left: 1.5rem; }
.role-row { display: flex; align-items: center; gap: 0.5rem; font-size: 0.85rem; }
.role-failed { color: var(--color-market-fall); }
.role-status-icon { font-size: 0.85rem; }
.role-name { min-width: 6rem; }
.role-tools { font-size: 0.8rem; color: var(--color-text-secondary); }
.role-elapsed { margin-left: auto; font-size: 0.75rem; color: var(--color-text-secondary); }
.role-elapsed-live { font-size: 0.75rem; color: var(--color-text-secondary); margin-left: auto; }
.role-eta-warn {
  font-size: 0.75rem;
  color: #b45309;
  background: #fef3c7;
  padding: 0.05rem 0.4rem;
  border-radius: 4px;
  border: 1px solid #fcd34d;
}
.stage-retry-btn {
  margin-left: 0.5rem;
  padding: 0.15rem 0.5rem;
  font-size: 0.75rem;
  border: 1px solid var(--color-accent, #3b82f6);
  border-radius: var(--radius-sm, 4px);
  background: transparent;
  color: var(--color-accent, #3b82f6);
  cursor: pointer;
  transition: background 0.15s;
}
.stage-retry-btn:hover {
  background: var(--color-accent, #3b82f6);
  color: white;
}
.role-error {
  font-size: 0.75rem;
  color: var(--color-market-fall, #ef4444);
  margin-left: auto;
  max-width: 200px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.progress-retry-bar {
  display: flex;
  justify-content: center;
  padding: 0.75rem;
  border-top: 1px solid var(--color-border-light);
}
.progress-retry-btn {
  padding: 0.5rem 1.5rem;
  font-size: 0.85rem;
  border: 1px solid var(--color-accent, #3b82f6);
  border-radius: var(--radius-md, 8px);
  background: var(--color-accent, #3b82f6);
  color: white;
  cursor: pointer;
  transition: opacity 0.15s;
}
.progress-retry-btn:hover {
  opacity: 0.85;
}
</style>
