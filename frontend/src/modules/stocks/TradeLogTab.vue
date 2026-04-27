<script setup>
import { computed, nextTick, onMounted, onUnmounted, reactive, ref } from 'vue'
import { fetchBackendDelete, fetchBackendGet, fetchBackendPost, fetchBackendPut, isAbortError, parseResponseMessage, replaceAbortController } from './stockInfoTabRequestUtils'
import { normalizeStockSymbol } from './stockInfoTabFormatting'
import { normalizePortfolioExposure, normalizePortfolioSnapshot, normalizeTradingPlan, normalizeTradingPlanAlert } from './stockInfoTabTradingPlans'
import { markdownToSafeHtml } from '../../utils/jsonMarkdownService'
import { useToast } from '../../composables/useToast.js'
import { useConfirm } from '../../composables/useConfirm.js'
import { pickStockMatch } from '../financial/symbolMarketUtil.js'
import TradeLogPlanWorkspace from './TradeLogPlanWorkspace.vue'

const toast = useToast()
const { confirm: showConfirm } = useConfirm()

const deviationTagOptions = ['追价', '超仓', '低于触发价成交', '高于触发价成交', '未按触发位', '动作偏离', '无计划交易']
const executionActionOptions = ['买入执行', '卖出执行', '加仓执行', '减仓执行', '做T买入', '做T卖出', '清仓执行']
const feedbackFilters = [
  { key: 'all', label: '全部记录' },
  { key: 'deviated', label: '仅看偏离' },
  { key: 'unplanned', label: '仅看无计划' },
]

function createEmptyTradeForm() {
  return {
    planId: null,
    symbol: '',
    name: '',
    direction: 'Buy',
    tradeType: 'Normal',
    executedPrice: '',
    quantity: '',
    executedAt: '',
    commission: '',
    planAction: '',
    executionAction: '买入执行',
    deviationTags: [],
    deviationNote: '',
    abandonReason: '',
    userNote: ''
  }
}

const state = reactive({
  period: 'day',
  customFrom: null,
  customTo: null,
  symbolFilter: '',
  typeFilter: '',
  feedbackFilter: 'all',
  planSearch: '',
  planScope: 'active',

  snapshot: null,
  snapshotLoading: false,
  snapshotError: '',

  exposure: null,
  exposureLoading: false,
  exposureError: '',

  trades: [],
  tradesLoading: false,
  tradesError: '',
  selectedTradeId: null,

  summary: null,
  summaryLoading: false,
  summaryError: '',

  planList: [],
  planListLoading: false,
  planAlerts: [],
  planAlertsLoading: false,
  planError: '',
  selectedPlanId: null,
  planListRequestToken: 0,
  planAlertsRequestToken: 0,
  planListAbortController: null,
  planAlertsAbortController: null,
  lastPlanRefreshAt: 0,

  tradeModalOpen: false,
  tradeModalMode: 'quick',
  editingTradeId: null,
  tradeForm: createEmptyTradeForm(),
  tradeFormDetailOpen: false,
  tradeFormSaving: false,
  tradeFormError: '',
  tradeFormContext: null,
  tradeFormContextLoading: false,
  tradeFormContextError: '',
  tradeFormSymbolMismatch: '',

  selectedPlanContext: null,
  feedbackPlanRuntimeContext: null,
  feedbackContextLoading: false,
  feedbackContextError: '',
  feedbackContextRequestedPlanId: null,
  feedbackFocusMode: 'trade',

  settingsModalOpen: false,
  capitalInput: '',
  settingsSaving: false,

  behaviorStats: null,
  behaviorStatsLoading: false,
  behaviorStatsError: '',

  reviewMenuOpen: false,
  reviewGenerating: false,
  reviewError: '',
  reviewCurrent: null,
  reviewList: [],
  reviewListLoading: false,
  showReviewPanel: false,
})

const searchResults = ref([])
const searchOpen = ref(false)
const searchLoading = ref(false)
let searchTimer = null
let stockSearchRequestToken = 0
let symbolLookupRequestToken = 0
let lastAutoFilledTradeName = ''

function resetTradeNameAutoFillState() {
  lastAutoFilledTradeName = ''
}

function getStockItemName(item) {
  return item?.Name || item?.name || ''
}

function getStockItemSymbol(item) {
  return item?.Symbol || item?.symbol || ''
}

function isCompleteStockSymbolQuery(query) {
  return /^([a-z]{2})?\d{6}$/i.test(String(query || '').trim())
}

function closeStockSuggestions() {
  searchOpen.value = false
  searchResults.value = []
}

function cancelPendingStockSearch() {
  stockSearchRequestToken += 1
  clearTimeout(searchTimer)
  searchTimer = null
  searchLoading.value = false
  closeStockSuggestions()
}

function canAutoFillTradeName() {
  const currentName = String(state.tradeForm.name || '').trim()
  return !currentName || (!!lastAutoFilledTradeName && currentName === lastAutoFilledTradeName)
}

function applyStockHitToTradeForm(hit) {
  const hitName = getStockItemName(hit)
  if (!hitName || !canAutoFillTradeName()) return false

  state.tradeForm.name = hitName
  state.tradeFormSymbolMismatch = ''
  lastAutoFilledTradeName = hitName
  closeStockSuggestions()
  return true
}

function formatPnL(v) { return v == null ? '-' : (v >= 0 ? '+' : '') + Number(v).toFixed(2) }
function formatMoney(v) { return v == null ? '-' : Number(v).toFixed(2) }
function formatPercent(v) { return v == null ? '-' : (Number(v) * 100).toFixed(1) + '%' }
function pnlClass(v) { return v > 0 ? 'text-rise' : v < 0 ? 'text-fall' : '' }
function directionBadgeClass(d) { return d === 'Buy' ? 'badge-danger' : 'badge-success' }
function complianceBadgeClass(tag) {
  if (tag === 'FollowedPlan') return 'badge-success'
  if (tag === 'DeviatedFromPlan') return 'badge-warning'
  return 'badge-info'
}
function complianceLabel(tag) {
  if (tag === 'FollowedPlan') return '计划内'
  if (tag === 'DeviatedFromPlan') return '偏离计划'
  return '无计划'
}
function scenarioBadgeClass(code) {
  if (code === 'Abandon') return 'badge-danger'
  if (code === 'Primary') return 'badge-success'
  if (code === 'Backup') return 'badge-warning'
  return 'badge-info'
}
function formatDateTime(v) {
  if (!v) return '-'
  const d = new Date(v)
  return `${d.getMonth() + 1}/${d.getDate()} ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
}
function formatDateTimeLocal(v) {
  if (!v) return ''
  const d = new Date(v)
  const pad = n => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}
function nowLocalString() {
  const now = new Date()
  const pad = n => String(n).padStart(2, '0')
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}T${pad(now.getHours())}:${pad(now.getMinutes())}`
}
function getPeriodDates(period) {
  const now = new Date()
  const to = now.toISOString()
  let from
  if (period === 'day') {
    from = new Date(now.getFullYear(), now.getMonth(), now.getDate()).toISOString()
  } else if (period === 'week') {
    const d = new Date(now)
    d.setDate(d.getDate() - d.getDay() + 1)
    d.setHours(0, 0, 0, 0)
    from = d.toISOString()
  } else if (period === 'month') {
    from = new Date(now.getFullYear(), now.getMonth(), 1).toISOString()
  }
  return { from, to }
}
function normalizeDirection(planDirection) {
  return planDirection === 'Short' ? 'Sell' : 'Buy'
}
function derivePlanAction(plan) {
  return plan?.direction === 'Short' ? '计划卖出' : '计划买入'
}
function deriveExecutionAction(direction, tradeType = 'Normal') {
  if (direction === 'Buy') {
    return tradeType === 'DayTrade' ? '做T买入' : '买入执行'
  }
  return tradeType === 'DayTrade' ? '做T卖出' : '卖出执行'
}
function normalizeTradeLogSymbol(value) {
  return normalizeStockSymbol(value) || String(value || '').trim().toUpperCase()
}
function getLinkedPlanItem() {
  return state.planList.find(item => String(item.id) === String(state.selectedPlanId))
    || (state.selectedPlanContext?.plan && String(state.selectedPlanContext.plan.id) === String(state.selectedPlanId)
      ? state.selectedPlanContext.plan
      : null)
}
function filterTradesForPlan(plan, trades = state.trades) {
  if (!plan) return Array.isArray(trades) ? trades : []
  const planId = plan?.id == null ? '' : String(plan.id)
  const planSymbol = normalizeTradeLogSymbol(plan?.symbol)
  return (Array.isArray(trades) ? trades : []).filter(trade =>
    (planId && String(trade?.planId ?? '') === planId)
    || (planSymbol && normalizeTradeLogSymbol(trade?.symbol) === planSymbol)
  )
}
function isPlanWorkspaceStale(maxAgeMs = 15000) {
  return !state.lastPlanRefreshAt || (Date.now() - state.lastPlanRefreshAt) > maxAgeMs
}
function summarizePositionSnapshot(snapshot, { emptyLabel = '当前无持仓' } = {}) {
  if (!snapshot) return emptyLabel
  if (!snapshot.quantity) return '当前无持仓'
  return snapshot.summary || `持仓 ${snapshot.quantity} 股 · 成本 ${formatMoney(snapshot.averageCost)} · 浮盈 ${formatPnL(snapshot.unrealizedPnL)}`
}
function summarizeScenario(snapshot, { emptyLabel = '暂无场景状态' } = {}) {
  if (!snapshot) return emptyLabel
  return snapshot.summary || `${snapshot.label} · ${snapshot.reason}`
}
function tradeFormContextSourceLabel() {
  if (state.tradeFormContextLoading) return '加载中'
  if (state.tradeFormContext?.plan?.sourceAgent) return state.tradeFormContext.plan.sourceAgent
  return state.tradeFormContextError && !state.tradeFormContext ? '加载失败' : 'manual'
}
function tradeFormContextScenarioBadgeLabel() {
  if (state.tradeFormContextLoading) return '加载中'
  if (state.tradeFormContext?.scenarioStatus?.label) return state.tradeFormContext.scenarioStatus.label
  return state.tradeFormContextError && !state.tradeFormContext ? '加载失败' : '暂无场景'
}
function tradeFormContextSummaryText() {
  if (state.tradeFormContextLoading) return '正在加载预案执行上下文，请稍候...'
  if (state.tradeFormContext?.plan?.analysisSummary) return state.tradeFormContext.plan.analysisSummary
  if (state.tradeFormContextError && !state.tradeFormContext) return '预案执行上下文暂时未返回，可稍后重试。'
  return '当前预案已接入执行录入，可直接补全本次动作。'
}
function tradeFormContextPortfolioSummaryText() {
  if (state.tradeFormContextLoading) return '正在同步场景和持仓信息...'
  if (state.tradeFormContext?.portfolioSummary?.summary) return state.tradeFormContext.portfolioSummary.summary
  if (state.tradeFormContextError && !state.tradeFormContext) return '上下文加载失败，暂未获取持仓信息。'
  return '暂无持仓信息'
}
function summarizeTradeFormScenario() {
  if (state.tradeFormContextLoading) return '正在加载场景状态...'
  if (state.tradeFormContextError && !state.tradeFormContext) return '上下文加载失败，请稍后重试。'
  return summarizeScenario(state.tradeFormContext?.scenarioStatus)
}
function summarizeTradeFormPosition() {
  if (state.tradeFormContextLoading) return '正在加载持仓快照...'
  if (state.tradeFormContextError && !state.tradeFormContext) return '上下文加载失败，请稍后重试。'
  return summarizePositionSnapshot(state.tradeFormContext?.currentPositionSnapshot, { emptyLabel: '当前无持仓' })
}
function buildCoachTip(trade, runtimeContext = null) {
  if (trade?.coachTip) return trade.coachTip
  if (trade?.complianceTag === 'Unplanned') return '这是一次无计划交易，建议先补齐触发位和失效位，再决定后续动作。'
  if ((trade?.deviationTags || []).includes('追价')) return '执行价格偏离触发位较多，先复核是否存在追价和节奏失控。'
  if (runtimeContext?.scenarioStatus?.abandonTriggered) return '当前预案已触发放弃条件，后续动作更应围绕保护仓位展开。'
  return '这笔执行整体仍有复盘价值，重点看场景是否匹配和仓位是否守纪律。'
}
function deriveFeedbackPlanAction(direction) {
  return normalizeDirection(direction) === 'Sell' ? '计划卖出' : '计划买入'
}
function sourceAgentLabel(sourceAgent) {
  if (!sourceAgent || sourceAgent === 'manual') return '手动计划'
  return sourceAgent
}
function summarizeDeviationTags(tags = [], complianceTag = 'FollowedPlan') {
  if (tags.length) return tags.join(' / ')
  if (complianceTag === 'Unplanned') return '无计划交易'
  if (complianceTag === 'DeviatedFromPlan') return '存在执行偏差'
  return '暂无明显偏差'
}
function buildFeedbackSummary(item, runtimeContext = null) {
  const explicitSummary = item.deviationNote?.trim()
    || (item.mode === 'plan' ? runtimeContext?.executionSummary?.summary?.trim() : '')
    || item.planSummary?.trim()

  if (explicitSummary) return explicitSummary
  if (item.mode === 'plan' && !runtimeContext?.executionSummary?.executionCount) {
    return '当前计划还没有执行记录，可以先录入本次动作，再继续复盘。'
  }
  if (item.complianceTag === 'Unplanned') {
    return '本次执行没有关联计划，先补齐触发依据和退出条件，再看后续动作。'
  }
  if ((item.deviationTags || []).length) {
    return `本次执行与计划存在 ${item.deviationTags.join('、')} 等偏差，优先复核动作、仓位和节奏。`
  }
  if (runtimeContext?.scenarioStatus?.abandonTriggered) {
    return '当前计划已触发放弃条件，后续更适合先保护仓位，再决定是否继续参与。'
  }
  return '本次执行整体与原计划基本一致，重点继续跟踪场景变化和仓位节奏。'
}
function buildFeedbackFocusPoints(item, runtimeContext = null) {
  const points = []
  if (item.complianceTag === 'Unplanned') {
    points.push('这是一次无计划交易，后续要先补齐触发位、失效位和退出条件。')
  }
  if ((item.deviationTags || []).includes('追价')) {
    points.push('存在追价迹象，复盘时重点看是否因为情绪上头打乱了入场节奏。')
  }
  if ((item.deviationTags || []).includes('超仓')) {
    points.push('仓位可能超出原计划，复盘时要一起检查总风险敞口是否失控。')
  }
  if ((item.deviationTags || []).includes('低于触发价成交')) {
    points.push('成交价低于触发价，回看是否在确认信号不足时提前下单。')
  }
  if ((item.deviationTags || []).includes('高于触发价成交')) {
    points.push('成交价高于触发价，回看是否因为犹豫或信息滞后错过了计划节奏。')
  }
  if ((item.deviationTags || []).includes('动作偏离')) {
    points.push('动作和原计划不一致，复盘时要把计划动作和实际操作逐条对上。')
  }
  if (runtimeContext?.scenarioStatus?.abandonTriggered) {
    points.push('计划最新状态已触发放弃条件，后续动作要优先考虑保护仓位。')
  }
  if (runtimeContext?.scenarioStatus?.counterTrendWarning) {
    points.push('当前场景存在逆势提醒，后续处理要降低主观硬扛的风险。')
  }
  return points
}
function buildFeedbackGentleHint(item, runtimeContext = null) {
  if (runtimeContext?.executionSummary?.executionCount > 1) {
    return '这条记录暂无明显问题，可以继续观察后续执行是否始终和计划保持一致。'
  }
  if (item.mode === 'plan') {
    return '当前没有明显风险提示，可继续跟踪计划状态、仓位变化和后续执行回写。'
  }
  return '当前没有明显偏差，复盘时重点看看节奏是否稳定、执行是否可复制。'
}
function buildPlanFeedbackItem(planContext) {
  if (!planContext?.plan) return null
  return {
    mode: 'plan',
    title: `${planContext.plan.symbol} ${planContext.plan.name}`,
    complianceTag: planContext.executionSummary?.latestComplianceTag || 'FollowedPlan',
    scenarioSnapshot: planContext.scenarioStatus,
    positionSnapshot: planContext.currentPositionSnapshot,
    coachTip: buildCoachTip(null, planContext),
    planAction: deriveFeedbackPlanAction(planContext.plan.direction),
    executionAction: planContext.executionSummary?.latestAction,
    deviationTags: planContext.executionSummary?.latestDeviationTags || [],
    deviationNote: planContext.executionSummary?.summary,
    planSummary: planContext.portfolioSummary?.summary,
    sourceLabel: sourceAgentLabel(planContext.plan.sourceAgent),
    detail: planContext
  }
}

const positionRatioClass = computed(() => {
  const r = state.snapshot?.totalPositionRatio ?? 0
  if (r > 0.8) return 'badge-danger'
  if (r > 0.5) return 'badge-warning'
  return 'badge-success'
})
const positionBarWidth = computed(() => `${Math.min(100, Math.max(0, (state.snapshot?.totalPositionRatio ?? 0) * 100))}%`)
const positionBarClass = computed(() => (state.snapshot?.totalPositionRatio ?? 0) > 0.8 ? 'bar-danger' : (state.snapshot?.totalPositionRatio ?? 0) > 0.5 ? 'bar-warning' : 'bar-safe')
const winRateClass = computed(() => {
  const w = state.summary?.winRate ?? 0
  if (w >= 0.6) return 'text-rise'
  if (w < 0.4) return 'text-fall'
  return ''
})
// V048-S1 P0-2: 无交易时汇总字段不应渲染实数，返 null 让模板显示 "—"
const hasSummaryTrades = computed(() => (state.summary?.totalTrades ?? 0) > 0)
const exposureBarWidth = computed(() => `${Math.min(100, Math.max(0, (state.exposure?.combinedExposure ?? 0) * 100))}%`)
const exposureBarClass = computed(() => (state.exposure?.combinedExposure ?? 0) > 0.8 ? 'bar-danger' : (state.exposure?.combinedExposure ?? 0) > 0.5 ? 'bar-warning' : 'bar-safe')
const exposureBadgeClass = computed(() => (state.exposure?.combinedExposure ?? 0) > 0.8 ? 'badge-danger' : (state.exposure?.combinedExposure ?? 0) > 0.5 ? 'badge-warning' : 'badge-success')
const disciplineScoreClass = computed(() => {
  const s = state.behaviorStats?.disciplineScore
  if (s == null) return 'score-neutral'
  if (s >= 80) return 'score-good'
  if (s >= 60) return 'score-warn'
  return 'score-danger'
})
const planRateClass = computed(() => {
  const r = state.behaviorStats?.planExecutionRate
  if (r == null) return ''
  if (r >= 0.8) return 'text-rise'
  if (r >= 0.5) return ''
  return 'text-fall'
})
const lossStreakClass = computed(() => {
  const s = state.behaviorStats?.currentLossStreak ?? 0
  if (s >= 3) return 'text-fall'
  if (s >= 1) return 'text-warning'
  return ''
})
const reviewContentHtml = computed(() => state.reviewCurrent?.reviewContent ? markdownToSafeHtml(state.reviewCurrent.reviewContent) : '')
const selectedPlanItem = computed(() => getLinkedPlanItem())
const linkedTrades = computed(() => {
  const linkedPlan = selectedPlanItem.value
  return linkedPlan ? filterTradesForPlan(linkedPlan, state.trades) : state.trades
})
const displayedTrades = computed(() => linkedTrades.value.filter(trade => {
  if (state.feedbackFilter === 'deviated') return trade.complianceTag === 'DeviatedFromPlan'
  if (state.feedbackFilter === 'unplanned') return trade.complianceTag === 'Unplanned'
  return true
}))
const linkedTradeCount = computed(() => linkedTrades.value.length)
const displayedTradesEmptyText = computed(() => state.selectedPlanId
  ? '当前计划/股票在所选时段内暂无符合条件的关联交易'
  : '暂无符合条件的交易记录')
const selectedPlanLinkageSummary = computed(() => {
  if (!selectedPlanItem.value) return ''
  const title = [selectedPlanItem.value.symbol, selectedPlanItem.value.name].filter(Boolean).join(' ')
  return linkedTradeCount.value
    ? `${title} · 已自动筛到 ${linkedTradeCount.value} 条关联交易`
    : `${title} · 当前时段内暂无关联交易`
})
const showReadonlyTradeIdentity = computed(() => Boolean(state.tradeForm.planId || state.editingTradeId))
const tradeFormIdentityItems = computed(() => {
  if (!showReadonlyTradeIdentity.value) return []
  const items = [
    { key: 'symbol', label: '标的', value: [state.tradeForm.symbol, state.tradeForm.name].filter(Boolean).join(' ') || '-' },
    { key: 'direction', label: '方向', value: state.tradeForm.direction === 'Sell' ? '卖出' : '买入' },
    { key: 'type', label: '类型', value: state.tradeForm.tradeType === 'DayTrade' ? '做T' : '普通' },
  ]
  if (state.tradeForm.planAction) {
    items.push({ key: 'plan-action', label: '对应计划动作', value: state.tradeForm.planAction })
  }
  return items
})
const tradeFormDetailedFeedbackLabels = computed(() => {
  const labels = []
  if (state.tradeForm.deviationNote?.trim()) labels.push('偏差说明')
  if (state.tradeForm.abandonReason?.trim()) labels.push('放弃原因')
  if (state.tradeForm.userNote?.trim()) labels.push('备注')
  return labels
})
const tradeFormDetailedFeedbackSummary = computed(() => {
  if (!tradeFormDetailedFeedbackLabels.value.length) {
    return '默认收起，按需补充偏差说明、放弃原因和备注。'
  }
  return `已填写 ${tradeFormDetailedFeedbackLabels.value.join(' / ')}`
})
const selectedTrade = computed(() => state.trades.find(item => String(item.id) === String(state.selectedTradeId)) || null)
const feedbackActiveItem = computed(() => {
  if (state.feedbackFocusMode === 'plan') {
    return buildPlanFeedbackItem(state.selectedPlanContext)
  }
  if (!selectedTrade.value) return null
  return {
    mode: 'trade',
    title: `${selectedTrade.value.symbol} ${selectedTrade.value.name}`,
    complianceTag: selectedTrade.value.complianceTag,
    scenarioSnapshot: selectedTrade.value.scenarioSnapshot,
    positionSnapshot: selectedTrade.value.positionSnapshot,
    coachTip: buildCoachTip(selectedTrade.value, state.feedbackPlanRuntimeContext),
    planAction: selectedTrade.value.planAction || (selectedTrade.value.planId ? deriveFeedbackPlanAction(selectedTrade.value.direction) : ''),
    executionAction: selectedTrade.value.executionAction,
    deviationTags: selectedTrade.value.deviationTags || [],
    deviationNote: selectedTrade.value.deviationNote,
    planSummary: selectedTrade.value.abandonReason,
    sourceLabel: selectedTrade.value.planId ? sourceAgentLabel(selectedTrade.value.planSourceAgent) : '手动录入',
    detail: selectedTrade.value
  }
})
const feedbackRuntimeContext = computed(() => {
  if (state.feedbackFocusMode === 'plan') {
    return state.selectedPlanContext || null
  }
  if (state.feedbackPlanRuntimeContext) {
    return state.feedbackPlanRuntimeContext
  }
  if (selectedTrade.value?.planId && state.selectedPlanContext?.plan?.id && String(selectedTrade.value.planId) === String(state.selectedPlanContext.plan.id)) {
    return state.selectedPlanContext
  }
  return selectedTrade.value ? null : (state.selectedPlanContext || null)
})
const feedbackRuntimePlanId = computed(() => {
  if (state.feedbackFocusMode === 'plan') {
    return state.selectedPlanContext?.plan?.id ?? state.selectedPlanId ?? null
  }
  return selectedTrade.value?.planId ?? null
})
const feedbackRuntimeRequestAttempted = computed(() => {
  if (!feedbackRuntimePlanId.value) return false
  return String(state.feedbackContextRequestedPlanId ?? '') === String(feedbackRuntimePlanId.value)
})
const feedbackWorkspaceModel = computed(() => {
  const item = feedbackActiveItem.value
  if (!item) return null

  const runtimeContext = feedbackRuntimeContext.value
  const scenarioSnapshot = item.scenarioSnapshot
  const positionSnapshot = item.positionSnapshot
  const focusPoints = buildFeedbackFocusPoints(item, runtimeContext)

  return {
    item,
    objectTypeLabel: item.mode === 'plan' ? '当前计划' : '所选交易记录',
    objectSourceLabel: item.sourceLabel,
    planActionLabel: item.planAction || '暂无计划动作',
    executionActionLabel: item.executionAction || '尚未记录执行',
    deviationSummary: summarizeDeviationTags(item.deviationTags, item.complianceTag),
    oneLineSummary: buildFeedbackSummary(item, runtimeContext),
    focusPoints,
    gentleHint: buildFeedbackGentleHint(item, runtimeContext),
    executionSituation: {
      snapshotLabel: item.mode === 'trade' && scenarioSnapshot?.snapshotType === 'Historical' ? '按下单当时记录' : '计划当前参考',
      sceneSummary: scenarioSnapshot
        ? summarizeScenario(scenarioSnapshot)
        : item.mode === 'plan'
          ? '尚未录入执行，当前先显示计划对应的场景参考。'
          : '暂无下单当时的场景记录。',
      positionSummary: summarizePositionSnapshot(positionSnapshot, {
        emptyLabel: item.mode === 'plan' ? '尚未录入执行，暂无当时持仓情况。' : '暂无当时持仓情况。'
      }),
      referencePrice: scenarioSnapshot?.referencePrice,
      marketStage: scenarioSnapshot?.marketStage,
      planStatus: scenarioSnapshot?.planStatus,
    },
    currentStatus: runtimeContext?.scenarioStatus
      ? {
          label: runtimeContext.scenarioStatus.label,
          code: runtimeContext.scenarioStatus.code,
          scenarioSummary: summarizeScenario(runtimeContext.scenarioStatus),
          positionSummary: summarizePositionSnapshot(runtimeContext.currentPositionSnapshot, { emptyLabel: '当前无持仓' }),
          portfolioSummary: runtimeContext.portfolioSummary?.summary || '',
          executionSummary: runtimeContext.executionSummary?.summary
            || (runtimeContext.executionSummary?.executionCount
              ? `已执行 ${runtimeContext.executionSummary.executionCount} 次`
              : '尚无执行回写')
        }
      : null,
  }
})
const feedbackRuntimeUnavailableMessage = computed(() => {
  const item = feedbackActiveItem.value
  if (!item || state.feedbackContextLoading || state.feedbackContextError) return ''
  if (item.mode === 'trade' && !item.detail?.planId) {
    return '这笔交易没有关联计划，只能查看本次执行摘要，暂无最新预案状态。'
  }
  if (item.detail?.planId && !feedbackRuntimeRequestAttempted.value) {
    return '计划最新状态尚未开始同步，可点击“刷新当前状态”主动获取。'
  }
  if (!feedbackRuntimeContext.value && item.detail?.planId) {
    return '计划最新状态暂未返回，可点击“刷新当前状态”再次同步。'
  }
  return ''
})

function syncTradeFeedbackRuntimeContext(trades = state.trades) {
  if (state.feedbackFocusMode === 'plan') {
    return
  }

  const selected = (Array.isArray(trades) ? trades : []).find(item => String(item?.id) === String(state.selectedTradeId)) || null
  const planId = selected?.planId ?? null

  if (!planId) {
    state.feedbackPlanRuntimeContext = null
    state.feedbackContextRequestedPlanId = null
    state.feedbackContextError = ''
    return
  }

  const matchesExistingContext = String(state.feedbackPlanRuntimeContext?.plan?.id ?? '') === String(planId)
  if (matchesExistingContext || state.feedbackContextLoading) {
    return
  }

  void loadPlanExecutionContext(planId, {
    forTradeFeedback: true,
    fallbackMessage: '暂时没拿到计划的最新状态，可稍后再试。'
  })
}

async function loadTradingPlans({ force = false } = {}) {
  if (!force && state.planListLoading) return

  const requestToken = ++state.planListRequestToken
  const controller = replaceAbortController(state.planListAbortController)
  state.planListAbortController = controller
  state.planListLoading = true
  state.planError = ''

  try {
    const params = new URLSearchParams({ take: '50' })
    const res = await fetchBackendGet(`/api/stocks/plans?${params.toString()}`, { signal: controller.signal })
    if (!res.ok) {
      throw new Error(await parseResponseMessage(res, '交易计划加载失败'))
    }
    const payload = await res.json()
    if (requestToken !== state.planListRequestToken) return
    state.planList = Array.isArray(payload) ? payload.map(normalizeTradingPlan) : []
    state.lastPlanRefreshAt = Date.now()
  } catch (err) {
    if (isAbortError(err)) return
    if (requestToken === state.planListRequestToken) {
      state.planError = err?.message || '交易计划加载失败'
      if (!state.planList.length) {
        state.planList = []
      }
    }
  } finally {
    if (requestToken === state.planListRequestToken) {
      state.planListLoading = false
      if (state.planListAbortController === controller) {
        state.planListAbortController = null
      }
    }
  }
}

async function loadTradingPlanAlerts({ force = false } = {}) {
  if (!force && state.planAlertsLoading) return

  const requestToken = ++state.planAlertsRequestToken
  const controller = replaceAbortController(state.planAlertsAbortController)
  state.planAlertsAbortController = controller
  state.planAlertsLoading = true

  try {
    const params = new URLSearchParams({ take: '20' })
    const res = await fetchBackendGet(`/api/stocks/plans/alerts?${params.toString()}`, { signal: controller.signal })
    if (!res.ok) {
      throw new Error(await parseResponseMessage(res, '交易计划告警加载失败'))
    }
    const payload = await res.json()
    if (requestToken !== state.planAlertsRequestToken) return
    state.planAlerts = Array.isArray(payload) ? payload.map(normalizeTradingPlanAlert) : []
    state.lastPlanRefreshAt = Date.now()
  } catch (err) {
    if (isAbortError(err)) return
    if (requestToken === state.planAlertsRequestToken) {
      state.planError = err?.message || '交易计划告警加载失败'
      if (!state.planAlerts.length) {
        state.planAlerts = []
      }
    }
  } finally {
    if (requestToken === state.planAlertsRequestToken) {
      state.planAlertsLoading = false
      if (state.planAlertsAbortController === controller) {
        state.planAlertsAbortController = null
      }
    }
  }
}

async function searchStocks(query) {
  if (!query || query.length < 1) {
    cancelPendingStockSearch()
    return
  }
  const requestToken = ++stockSearchRequestToken
  const requestedQuery = String(query || '').trim()
  searchLoading.value = true
  try {
    const res = await fetchBackendGet(`/api/stocks/search?q=${encodeURIComponent(requestedQuery)}`)
    if (requestToken !== stockSearchRequestToken) return
    if (res.ok) {
      const results = await res.json()
      if (requestToken !== stockSearchRequestToken) return
      if (String(state.tradeForm.symbol || '').trim() !== requestedQuery) return
      searchResults.value = Array.isArray(results) ? results : []

      if (isCompleteStockSymbolQuery(requestedQuery)) {
        const hit = pickStockMatch(searchResults.value, requestedQuery)
        if (applyStockHitToTradeForm(hit)) return
      }

      searchOpen.value = searchResults.value.length > 0
    }
  } catch {
    if (requestToken === stockSearchRequestToken) closeStockSuggestions()
  } finally {
    if (requestToken === stockSearchRequestToken) searchLoading.value = false
  }
}

function onSymbolInput(e) {
  const val = e.target.value
  state.tradeForm.symbol = val
  stockSearchRequestToken += 1
  clearTimeout(searchTimer)
  searchTimer = null
  searchLoading.value = false
  closeStockSuggestions()
  if (!String(val || '').trim()) return
  searchTimer = setTimeout(() => searchStocks(val), 300)
}

function selectStock(item) {
  state.tradeForm.symbol = getStockItemSymbol(item)
  state.tradeForm.name = getStockItemName(item)
  lastAutoFilledTradeName = state.tradeForm.name
  state.tradeFormSymbolMismatch = ''
  closeStockSuggestions()
}

// V048-S1 #92: 代码框 blur 时查询 search API，填充/校验名称
async function lookupStockBySymbol(symbol) {
  if (!symbol) return null
  try {
    const res = await fetchBackendGet(`/api/stocks/search?q=${encodeURIComponent(symbol)}`)
    if (!res.ok) return null
    const list = await res.json()
    if (!Array.isArray(list) || list.length === 0) return null
    return pickStockMatch(list, symbol)
  } catch {
    return null
  }
}

async function onSymbolBlur() {
  const symbol = (state.tradeForm.symbol || '').trim()
  cancelPendingStockSearch()
  if (!symbol) {
    state.tradeFormSymbolMismatch = ''
    return
  }
  const requestToken = ++symbolLookupRequestToken
  const hit = await lookupStockBySymbol(symbol)
  if (requestToken !== symbolLookupRequestToken) return
  if ((state.tradeForm.symbol || '').trim() !== symbol) return
  if (!hit) {
    state.tradeFormSymbolMismatch = ''
    return
  }
  const hitName = getStockItemName(hit)
  const hitSymbol = getStockItemSymbol(hit)
  // 名称为空 → 自动补齐
  if (applyStockHitToTradeForm(hit)) {
    return
  }
  // 名称存在但不一致 → 提示
  if (hitName && state.tradeForm.name && state.tradeForm.name.trim() !== hitName.trim()) {
    state.tradeFormSymbolMismatch = `${hitSymbol} 应为「${hitName}」，当前名称「${state.tradeForm.name}」`
  } else {
    state.tradeFormSymbolMismatch = ''
  }
}

async function onNameBlur() {
  // 名称改动后若与代码不一致也提示
  const symbol = (state.tradeForm.symbol || '').trim()
  const name = (state.tradeForm.name || '').trim()
  if (!symbol || !name) {
    state.tradeFormSymbolMismatch = ''
    return
  }
  const hit = await lookupStockBySymbol(symbol)
  const hitName = (hit?.Name || hit?.name || '').trim()
  if (hitName && hitName !== name) {
    state.tradeFormSymbolMismatch = `${symbol} 应为「${hitName}」，当前名称「${name}」`
  } else {
    state.tradeFormSymbolMismatch = ''
  }
}

async function loadPortfolioSnapshot() {
  state.snapshotLoading = true
  state.snapshotError = ''
  try {
    const res = await fetchBackendGet('/api/portfolio/snapshot')
    if (res.ok) {
      state.snapshot = normalizePortfolioSnapshot(await res.json())
    } else {
      state.snapshotError = '加载持仓信息失败'
    }
  } catch {
    state.snapshotError = '加载持仓信息失败'
  }
  state.snapshotLoading = false
}

async function loadExposure() {
  state.exposureLoading = true
  state.exposureError = ''
  try {
    const res = await fetchBackendGet('/api/portfolio/exposure')
    if (res.ok) {
      state.exposure = normalizePortfolioExposure(await res.json())
    } else {
      state.exposureError = '加载暴露数据失败'
    }
  } catch {
    state.exposureError = '加载暴露数据失败'
  }
  state.exposureLoading = false
}

async function loadBehaviorStats() {
  state.behaviorStatsLoading = true
  state.behaviorStatsError = ''
  try {
    const res = await fetchBackendGet('/api/trades/behavior-stats')
    if (res.ok) {
      state.behaviorStats = await res.json()
    } else {
      state.behaviorStatsError = '加载健康度数据失败'
    }
  } catch {
    state.behaviorStatsError = '加载健康度数据失败'
  }
  state.behaviorStatsLoading = false
}

async function loadTrades() {
  state.tradesLoading = true
  state.tradesError = ''
  try {
    const params = new URLSearchParams()
    if (state.symbolFilter) params.set('symbol', state.symbolFilter)
    if (state.typeFilter) params.set('type', state.typeFilter)
    if (state.period === 'custom') {
      if (state.customFrom) params.set('from', new Date(state.customFrom).toISOString())
      if (state.customTo) {
        const d = new Date(state.customTo)
        d.setHours(23, 59, 59, 999)
        params.set('to', d.toISOString())
      }
    } else {
      const { from, to } = getPeriodDates(state.period)
      if (from) params.set('from', from)
      if (to) params.set('to', to)
    }
    const res = await fetchBackendGet(`/api/trades?${params}`)
    if (!res.ok) {
      state.tradesError = await parseResponseMessage(res, '加载失败')
      return
    }
    state.trades = await res.json()
    if (state.feedbackFocusMode !== 'plan') {
      const candidateTrades = state.selectedPlanId ? filterTradesForPlan(getLinkedPlanItem(), state.trades) : state.trades
      const exists = candidateTrades.some(item => String(item.id) === String(state.selectedTradeId))
      state.selectedTradeId = exists ? state.selectedTradeId : (candidateTrades[0]?.id ?? null)
      syncTradeFeedbackRuntimeContext(candidateTrades)
    }
  } catch {
    state.tradesError = '网络错误'
  }
  state.tradesLoading = false
}

async function loadSummary() {
  state.summaryLoading = true
  state.summaryError = ''
  try {
    const params = new URLSearchParams()
    params.set('period', state.period)
    if (state.period === 'custom') {
      if (state.customFrom) params.set('from', new Date(state.customFrom).toISOString())
      if (state.customTo) {
        const d = new Date(state.customTo)
        d.setHours(23, 59, 59, 999)
        params.set('to', d.toISOString())
      }
    } else {
      const { from, to } = getPeriodDates(state.period)
      if (from) params.set('from', from)
      if (to) params.set('to', to)
    }
    const res = await fetchBackendGet(`/api/trades/summary?${params}`)
    if (res.ok) {
      state.summary = await res.json()
    }
  } catch {
    state.summaryError = '加载汇总数据失败'
  }
  state.summaryLoading = false
}

async function loadPlanExecutionContext(planId, { forForm = false, selectAsPlan = false, forTradeFeedback = false, fallbackMessage = '' } = {}) {
  if (!planId) return null
  if (forForm) {
    state.tradeFormContextLoading = true
    state.tradeFormContextError = ''
  }
  if (selectAsPlan || forTradeFeedback) {
    state.feedbackContextLoading = true
    state.feedbackContextError = ''
    state.feedbackContextRequestedPlanId = planId
  }
  const resolvedFallbackMessage = fallbackMessage || (forForm
    ? '暂时没拿到计划上下文，可稍后重试。'
    : '暂时没拿到最新预案状态，请稍后再试。')
  try {
    const res = await fetchBackendGet(`/api/stocks/plans/${planId}/execution-context`)
    if (!res.ok) {
      const message = await parseResponseMessage(res, resolvedFallbackMessage)
      if (forForm) state.tradeFormContextError = message
      if (selectAsPlan || forTradeFeedback) state.feedbackContextError = message
      return null
    }
    const context = await res.json()
    if (forForm) state.tradeFormContext = context
    if (selectAsPlan) {
      state.selectedPlanContext = context
      state.selectedTradeId = null
      state.feedbackPlanRuntimeContext = null
      state.feedbackFocusMode = 'plan'
    }
    if (forTradeFeedback) {
      state.feedbackPlanRuntimeContext = context
    }
    return context
  } catch {
    if (forForm) state.tradeFormContextError = resolvedFallbackMessage
    if (selectAsPlan || forTradeFeedback) state.feedbackContextError = resolvedFallbackMessage
    return null
  } finally {
    if (forForm) state.tradeFormContextLoading = false
    if (selectAsPlan || forTradeFeedback) state.feedbackContextLoading = false
  }
}

async function refreshTradingPlanWorkspace(force = false, { refreshSelectedContext = true } = {}) {
  await Promise.allSettled([
    loadTradingPlans({ force }),
    loadTradingPlanAlerts({ force })
  ])

  if (!state.selectedPlanId) {
    return
  }

  const selectedStillExists = state.planList.some(item => String(item.id) === String(state.selectedPlanId))
  if (!selectedStillExists) {
    clearSelectedPlanWorkspace()
    return
  }

  if (refreshSelectedContext && state.feedbackFocusMode === 'plan') {
    await loadPlanExecutionContext(state.selectedPlanId, {
      selectAsPlan: true,
      fallbackMessage: '暂时没拿到计划的最新状态，可稍后重试。'
    })
  }
}

async function selectPlanForWorkspace(plan, options = {}) {
  if (!plan?.id) return
  state.selectedPlanId = plan.id
  state.selectedTradeId = null
  state.feedbackFocusMode = 'plan'
  state.feedbackPlanRuntimeContext = null

  if (options.forceRefresh || isPlanWorkspaceStale()) {
    void refreshTradingPlanWorkspace(true, { refreshSelectedContext: false })
  }

  await loadPlanExecutionContext(plan.id, {
    selectAsPlan: true,
    fallbackMessage: '暂时没拿到计划的最新状态，可稍后重试。'
  })
}

function clearSelectedPlanWorkspace() {
  const shouldRestoreTradeFocus = state.feedbackFocusMode === 'plan'
  state.selectedPlanId = null
  state.feedbackPlanRuntimeContext = null

  if (shouldRestoreTradeFocus) {
    state.selectedPlanContext = null
    state.feedbackFocusMode = 'trade'
    const exists = state.trades.some(item => String(item.id) === String(state.selectedTradeId))
    state.selectedTradeId = exists ? state.selectedTradeId : (state.trades[0]?.id ?? null)
    syncTradeFeedbackRuntimeContext(state.trades)
  }
}

function viewPlanStock(plan) {
  if (!plan?.symbol) return
  window.dispatchEvent(new CustomEvent('navigate-stock', {
    detail: { symbol: plan.symbol, name: plan.name }
  }))
}

// V048-S1 #91: 持仓总览点击行跳股票信息（手册 F 场景）
function navigateToStockInfo(position) {
  if (!position?.symbol) return
  window.dispatchEvent(new CustomEvent('navigate-stock', {
    detail: { symbol: position.symbol, name: position.name }
  }))
  toast.info(`已跳转到 ${position.name || position.symbol} 股票信息`)
}

function recordTradeFromWorkspace(plan) {
  if (!plan) return
  window.dispatchEvent(new CustomEvent('navigate-trade-log', { detail: { plan } }))
}

async function refreshFeedbackWorkspace() {
  if (!feedbackActiveItem.value || state.feedbackContextLoading) return

  state.feedbackContextLoading = true
  state.feedbackContextError = ''

  try {
    await Promise.allSettled([
      loadTrades(),
      loadSummary(),
      loadPortfolioSnapshot(),
      loadExposure(),
      loadBehaviorStats()
    ])

    const latestSelectedTrade = state.trades.find(item => String(item.id) === String(state.selectedTradeId)) || selectedTrade.value
    const activePlanId = state.feedbackFocusMode === 'plan'
      ? (state.selectedPlanContext?.plan?.id ?? state.selectedPlanId)
      : (latestSelectedTrade?.planId ?? null)
    if (!activePlanId) return

    await loadPlanExecutionContext(activePlanId, {
      selectAsPlan: state.feedbackFocusMode === 'plan',
      forTradeFeedback: state.feedbackFocusMode !== 'plan',
      fallbackMessage: '暂时没拿到最新预案状态，可稍后再点一次“刷新当前状态”。'
    })
  } finally {
    state.feedbackContextLoading = false
  }
}

async function refreshTradeStateAfterSave(savedTrade) {
  await Promise.allSettled([
    loadTrades(),
    loadSummary(),
    loadPortfolioSnapshot(),
    loadExposure(),
    loadBehaviorStats(),
    loadTradingPlans({ force: true }),
    loadTradingPlanAlerts({ force: true })
  ])

  state.selectedTradeId = savedTrade?.id ?? state.selectedTradeId

  if (savedTrade?.planId) {
    state.selectedPlanId = savedTrade.planId
    state.feedbackFocusMode = 'plan'
    await loadPlanExecutionContext(savedTrade.planId, { selectAsPlan: true, forTradeFeedback: true })
    return
  }

  state.feedbackPlanRuntimeContext = null
  if (state.feedbackFocusMode === 'plan' && state.selectedPlanId) {
    await loadPlanExecutionContext(state.selectedPlanId, {
      selectAsPlan: true,
      fallbackMessage: '暂时没拿到计划的最新状态，可稍后重试。'
    })
    return
  }

  state.selectedPlanContext = null
}

async function saveTrade() {
  state.tradeFormSaving = true
  state.tradeFormError = ''
  const price = Number(state.tradeForm.executedPrice)
  const qty = Number(state.tradeForm.quantity)
  if (!state.tradeForm.symbol?.trim()) { state.tradeFormError = '请输入股票代码'; state.tradeFormSaving = false; return }
  if (!price || price <= 0) { state.tradeFormError = '请输入有效的成交价'; state.tradeFormSaving = false; return }
  if (!qty || qty <= 0) { state.tradeFormError = '请输入有效的数量'; state.tradeFormSaving = false; return }
  if (!state.tradeForm.executedAt) { state.tradeFormError = '请选择成交时间'; state.tradeFormSaving = false; return }

  try {
    const f = state.tradeForm
    const isEdit = !!state.editingTradeId
    const baseDto = {
      executedPrice: Number(f.executedPrice),
      quantity: Number(f.quantity),
      executedAt: new Date(f.executedAt).toISOString(),
      commission: f.commission ? Number(f.commission) : 0,
      userNote: f.userNote || undefined,
      planAction: f.planAction || undefined,
      executionAction: f.executionAction || undefined,
      deviationTags: f.deviationTags?.length ? f.deviationTags : undefined,
      deviationNote: f.deviationNote || undefined,
      abandonReason: f.abandonReason || undefined,
    }
    const dto = isEdit
      ? baseDto
      : {
          planId: f.planId || undefined,
          symbol: f.symbol,
          name: f.name,
          direction: f.direction,
          tradeType: f.tradeType,
          ...baseDto,
        }
    const res = isEdit
      ? await fetchBackendPut(`/api/trades/${state.editingTradeId}`, dto)
      : await fetchBackendPost('/api/trades', dto)

    if (!res.ok) {
      state.tradeFormError = await parseResponseMessage(res, '保存失败')
      return
    }

    const savedTrade = await res.json()
    const normalizedSavedTrade = {
      ...savedTrade,
      id: savedTrade?.id ?? state.editingTradeId ?? null,
      symbol: savedTrade?.symbol ?? f.symbol,
      planId: savedTrade?.planId ?? f.planId ?? null,
    }

    state.tradeModalOpen = false
    state.editingTradeId = null
    state.tradeFormContext = null
    state.tradeFormContextError = ''
    resetTradeForm()
    if (!normalizedSavedTrade.planId && !state.selectedPlanId) {
      state.selectedPlanContext = null
    }
    window.dispatchEvent(new CustomEvent('trade-execution-saved', {
      detail: { symbol: normalizedSavedTrade.symbol, planId: normalizedSavedTrade.planId }
    }))
    void refreshTradeStateAfterSave(normalizedSavedTrade)
  } catch {
    state.tradeFormError = '网络错误'
  } finally {
    state.tradeFormSaving = false
  }
}

async function deleteTrade(id) {
  if (!(await showConfirm({ message: '确定删除此交易记录？' }))) return
  try {
    const res = await fetchBackendDelete(`/api/trades/${id}`)
    if (res.ok) {
      await Promise.all([loadTrades(), loadSummary(), loadPortfolioSnapshot(), loadExposure(), loadBehaviorStats()])
      if (String(state.selectedTradeId) === String(id)) {
        state.selectedTradeId = state.trades[0]?.id ?? null
      }
    }
  } catch {
    state.tradesError = '删除交易记录失败'
  }
}

async function selectTradeForFeedback(trade) {
  state.selectedTradeId = trade.id
  state.feedbackFocusMode = 'trade'
  if (!state.selectedPlanId) {
    state.selectedPlanContext = null
  }
  state.feedbackPlanRuntimeContext = null
  if (trade.planId) {
    await loadPlanExecutionContext(trade.planId, { forTradeFeedback: true })
  }
}

function editTrade(trade) {
  state.editingTradeId = trade.id
  state.tradeModalMode = trade.planId ? 'plan' : 'quick'
  state.tradeFormDetailOpen = false
  Object.assign(state.tradeForm, {
    planId: trade.planId,
    symbol: trade.symbol,
    name: trade.name,
    direction: trade.direction,
    tradeType: trade.tradeType,
    executedPrice: trade.executedPrice,
    quantity: trade.quantity,
    executedAt: formatDateTimeLocal(trade.executedAt),
    commission: trade.commission || '',
    planAction: trade.planAction || '',
    executionAction: trade.executionAction || deriveExecutionAction(trade.direction, trade.tradeType),
    deviationTags: [...(trade.deviationTags || [])],
    deviationNote: trade.deviationNote || '',
    abandonReason: trade.abandonReason || '',
    userNote: trade.userNote || ''
  })
  state.tradeFormContext = trade.planId ? {
    plan: {
      id: trade.planId,
      symbol: trade.symbol,
      name: trade.name,
      sourceAgent: trade.planSourceAgent,
      analysisSummary: trade.planTitle,
    },
    scenarioStatus: trade.scenarioSnapshot,
    currentPositionSnapshot: trade.positionSnapshot,
    portfolioSummary: { summary: trade.positionSnapshot?.summary || '历史持仓快照' },
    executionSummary: null,
  } : null
  state.tradeFormError = ''
  state.tradeModalOpen = true
}

async function saveSettings() {
  state.settingsSaving = true
  try {
    const res = await fetchBackendPut('/api/portfolio/settings', { totalCapital: Number(state.capitalInput) })
    if (res.ok) {
      state.settingsModalOpen = false
      loadPortfolioSnapshot()
      loadExposure()
    }
  } catch (err) {
    state.tradeFormError = '保存本金设置失败：' + (err?.message || '')
  }
  state.settingsSaving = false
}

function resetTradeForm() {
  Object.assign(state.tradeForm, createEmptyTradeForm())
  state.tradeFormDetailOpen = false
  resetTradeNameAutoFillState()
}

function openQuickEntry() {
  state.editingTradeId = null
  state.tradeModalMode = 'quick'
  if (!state.selectedPlanId) {
    state.selectedPlanContext = null
  }
  state.tradeFormContext = null
  state.tradeFormContextError = ''
  state.tradeFormSymbolMismatch = ''
  resetTradeForm()
  state.tradeForm.executedAt = nowLocalString()
  state.tradeForm.executionAction = '买入执行'
  state.tradeModalOpen = true
}

function openSettings() {
  state.capitalInput = state.snapshot?.totalCapital ?? ''
  state.settingsModalOpen = true
}

function onPeriodChange() {
  loadTrades()
  loadSummary()
}

async function handleNavigateTradeLog(e) {
  const plan = e?.detail?.plan
  if (!plan) return

  delete window.__pendingNavigateTradeLog

  state.selectedPlanId = plan.id
  state.feedbackFocusMode = 'plan'

  state.editingTradeId = null
  state.tradeModalMode = 'plan'
  state.tradeFormContext = null
  state.tradeFormContextError = ''
  resetTradeForm()
  state.tradeForm.planId = plan.id
  state.tradeForm.symbol = plan.symbol || ''
  state.tradeForm.name = plan.name || ''
  state.tradeForm.direction = normalizeDirection(plan.direction)
  state.tradeForm.planAction = derivePlanAction(plan)
  state.tradeForm.executionAction = deriveExecutionAction(state.tradeForm.direction)
  state.tradeForm.executedAt = nowLocalString()
  state.tradeModalOpen = true
  await loadPlanExecutionContext(plan.id, { forForm: true, selectAsPlan: true })
}

function handleGlobalClick() {
  state.reviewMenuOpen = false
  searchOpen.value = false
}

async function generateReview(type) {
  state.reviewMenuOpen = false
  state.reviewGenerating = true
  state.reviewError = ''
  state.reviewCurrent = null
  state.showReviewPanel = true
  try {
    const body = { type }
    if (type === 'custom' && state.period === 'custom') {
      if (state.customFrom) body.from = new Date(state.customFrom).toISOString()
      if (state.customTo) body.to = new Date(state.customTo).toISOString()
    }
    const res = await fetchBackendPost('/api/trades/reviews/generate', body)
    if (res.ok) {
      state.reviewCurrent = await res.json()
      loadReviewList()
      await nextTick()
      document.querySelector('.review-panel')?.scrollIntoView({ behavior: 'smooth', block: 'start' })
    } else {
      state.reviewError = await parseResponseMessage(res, '生成复盘失败')
    }
  } catch {
    state.reviewError = '网络错误'
  }
  state.reviewGenerating = false
}

async function loadReviewList() {
  state.reviewListLoading = true
  try {
    const res = await fetchBackendGet('/api/trades/reviews')
    if (res.ok) {
      state.reviewList = await res.json()
    }
  } catch {
    // silent
  }
  state.reviewListLoading = false
}

async function viewReview(id) {
  state.showReviewPanel = true
  state.reviewGenerating = true
  state.reviewError = ''
  try {
    const res = await fetchBackendGet(`/api/trades/reviews/${id}`)
    if (res.ok) {
      state.reviewCurrent = await res.json()
    } else {
      state.reviewError = '加载复盘详情失败'
    }
  } catch {
    state.reviewError = '网络错误'
  }
  state.reviewGenerating = false
}

function reviewTypeLabel(type) {
  const map = { Daily: '日复盘', Weekly: '周复盘', Monthly: '月复盘', Custom: '自定义' }
  return map[type] || type
}

async function handleResetAll() {
  if (!(await showConfirm({ title: '重置确认', message: '⚠️ 确定重置所有交易记录和持仓吗？\n\n将删除：\n• 所有交易记录\n• 所有持仓数据\n• 所有复盘历史\n\n保留：\n• 本金设置\n• 交易计划\n\n此操作不可撤销！', type: 'danger', confirmText: '确定重置' }))) return
  if (!(await showConfirm({ title: '最后确认', message: '真的要删除所有交易记录和持仓？', type: 'danger', confirmText: '删除' }))) return

  try {
    const res = await fetchBackendPost('/api/trades/reset-all', { confirmText: 'RESET_ALL_TRADES' })
    if (res.ok) {
      const data = await res.json()
      toast.success(`重置完成：删除交易 ${data.deletedTradeCount} 条、持仓 ${data.deletedPositionCount} 条、复盘 ${data.deletedReviewCount} 条`)
      state.selectedTradeId = null
      state.selectedPlanId = null
      state.selectedPlanContext = null
      state.feedbackPlanRuntimeContext = null
      state.feedbackFocusMode = 'trade'
      await Promise.all([loadTrades(), loadSummary(), loadPortfolioSnapshot(), loadExposure(), loadBehaviorStats(), loadReviewList()])
    } else {
      const err = await res.text()
      toast.error('重置失败: ' + err)
    }
  } catch (err) {
    toast.error('重置失败: ' + (err?.message || '未知错误'))
  }
}

function handleEscKey(e) {
  if (e.key === 'Escape' && state.tradeModalOpen) {
    state.tradeModalOpen = false
  }
}

onMounted(() => {
  loadPortfolioSnapshot()
  loadExposure()
  loadTrades()
  loadSummary()
  loadTradingPlans()
  loadTradingPlanAlerts()
  loadReviewList()
  loadBehaviorStats()
  window.addEventListener('navigate-trade-log', handleNavigateTradeLog)
  document.addEventListener('click', handleGlobalClick)
  document.addEventListener('keydown', handleEscKey)

  if (window.__pendingNavigateTradeLog?.plan) {
    const pending = window.__pendingNavigateTradeLog
    delete window.__pendingNavigateTradeLog
    void handleNavigateTradeLog({ detail: pending })
  }
})

onUnmounted(() => {
  state.planListAbortController?.abort()
  state.planAlertsAbortController?.abort()
  window.removeEventListener('navigate-trade-log', handleNavigateTradeLog)
  document.removeEventListener('click', handleGlobalClick)
  document.removeEventListener('keydown', handleEscKey)
})
</script>

<template>
  <div class="trade-log-tab">
    <!-- 持仓总览 -->
    <div class="portfolio-overview card" v-if="state.snapshot">
      <div class="portfolio-header">
        <h3>持仓总览</h3>
        <span class="badge" :class="positionRatioClass">
          仓位 {{ formatPercent(state.snapshot.totalPositionRatio) }}
        </span>
      </div>
      <div class="portfolio-metrics">
        <div class="metric">
          <span class="metric-label">总本金</span>
          <span class="metric-value" data-testid="portfolio-total-capital">{{ formatMoney(state.snapshot.totalCapital) }}</span>
        </div>
        <div class="metric">
          <span class="metric-label">持仓成本</span>
          <span class="metric-value" data-testid="portfolio-total-cost">{{ formatMoney(state.snapshot.totalCost) }}</span>
        </div>
        <div class="metric">
          <span class="metric-label">总市值</span>
          <span class="metric-value" data-testid="portfolio-total-market-value">{{ formatMoney(state.snapshot.totalMarketValue) }}</span>
        </div>
        <div class="metric">
          <span class="metric-label">总浮盈</span>
          <span class="metric-value" :class="pnlClass(state.snapshot.totalUnrealizedPnL)" data-testid="portfolio-total-unrealized-pnl">
            {{ formatPnL(state.snapshot.totalUnrealizedPnL) }}
          </span>
        </div>
        <div class="metric">
          <span class="metric-label">可用资金</span>
          <span class="metric-value" data-testid="portfolio-available-cash">{{ formatMoney(state.snapshot.availableCash) }}</span>
        </div>
      </div>
      <div class="position-bar">
        <div class="position-bar-fill" :style="{ width: positionBarWidth }" :class="positionBarClass"></div>
      </div>
      <div class="position-list" v-if="state.snapshot.positions?.length">
        <div class="position-item position-item-clickable"
             v-for="p in state.snapshot.positions"
             :key="p.symbol"
             role="button"
             tabindex="0"
             :title="`点击查看 ${p.name || p.symbol} 股票信息`"
             @click="navigateToStockInfo(p)"
             @keydown.enter.prevent="navigateToStockInfo(p)"
             @keydown.space.prevent="navigateToStockInfo(p)">
          <span class="position-symbol">{{ p.name || p.symbol }}</span>
          <span>{{ p.quantityLots ?? 0 }}股</span>
          <span :data-testid="`portfolio-position-cost-${p.symbol}`">成本 {{ formatMoney(p.totalCost) }}</span>
          <span :data-testid="`portfolio-position-market-value-${p.symbol}`">市值 {{ formatMoney(p.marketValue) }}</span>
          <span :class="pnlClass(p.unrealizedPnL)" :data-testid="`portfolio-position-pnl-${p.symbol}`">浮盈 {{ formatPnL(p.unrealizedPnL) }}</span>
          <span class="badge badge-pill">{{ formatPercent(p.positionRatio) }}</span>
        </div>
      </div>
    </div>
    <div v-else-if="state.snapshotLoading" class="card loading-state">加载持仓中...</div>
    <div v-else-if="state.snapshotError" class="card text-danger" style="padding:1rem">{{ state.snapshotError }}</div>

    <!-- 仓位暴露条 -->
    <div class="exposure-bar-card card" v-if="state.exposure">
      <div class="exposure-header">
        <span class="exposure-title">风险敞口</span>
        <span class="badge" :class="exposureBadgeClass">{{ formatPercent(state.exposure.combinedExposure) }}</span>
        <span v-if="state.exposure.currentMode" class="execution-mode-tag" :class="'mode-' + state.exposure.currentMode.confirmationLevel">
          {{ state.exposure.currentMode.executionMode }}
        </span>
      </div>
      <div class="exposure-detail">
        <span class="exposure-item">
          <span class="exposure-label">真实暴露</span>
          <span class="exposure-value">{{ formatPercent(state.exposure.totalExposure) }}</span>
        </span>
        <span class="exposure-plus">+</span>
        <span class="exposure-item">
          <span class="exposure-label">待执行计划</span>
          <span class="exposure-value">{{ formatPercent(state.exposure.pendingExposure) }}</span>
        </span>
        <span class="exposure-eq">=</span>
        <span class="exposure-item">
          <span class="exposure-label">总风险敞口</span>
          <span class="exposure-value" :class="state.exposure.combinedExposure > 0.8 ? 'text-fall' : ''">{{ formatPercent(state.exposure.combinedExposure) }}</span>
        </span>
      </div>
      <div class="position-bar">
        <div class="position-bar-fill" :style="{ width: exposureBarWidth }" :class="exposureBarClass"></div>
      </div>
      <div v-if="state.exposure.combinedExposure > 0.8" class="exposure-warning">
        ⚠ 总风险敞口超过 80%，新建仓或加仓请谨慎
      </div>
      <div v-if="state.exposure.symbolExposures?.length" class="exposure-symbols">
        <span v-for="s in state.exposure.symbolExposures" :key="s.symbol" class="exposure-symbol-item">
          {{ s.name || s.symbol }} {{ formatPercent(s.exposure) }}
        </span>
      </div>
    </div>
    <div v-else-if="state.exposureLoading" class="card loading-state" style="padding:0.5rem">加载暴露数据中...</div>
    <div v-else-if="state.exposureError" class="card text-danger" style="padding:0.5rem">{{ state.exposureError }}</div>

    <!-- 交易健康度 -->
    <div class="behavior-dashboard card" v-if="state.behaviorStats">
      <div class="behavior-header">
        <h4>🧘 交易健康度</h4>
        <span class="discipline-score" :class="disciplineScoreClass" title="满分100分。计划执行率<50%扣20分；连亏≥3笔扣15分；过度交易扣15分；追涨率>50%扣20分">
          {{ state.behaviorStats.disciplineScore == null ? '—' : state.behaviorStats.disciplineScore + ' 分' }}
        </span>
      </div>
      <div class="behavior-metrics">
        <div class="behavior-metric">
          <span class="metric-label" title="最近7天的交易总笔数">7日交易</span>
          <span class="metric-value">{{ state.behaviorStats.trades7Days }}笔</span>
        </div>
        <div class="behavior-metric">
          <span class="metric-label" title="有关联计划的交易占总交易的比例，越高越好">计划执行率</span>
          <span class="metric-value" :class="planRateClass">
            {{ state.behaviorStats.planExecutionRate == null ? '—' : formatPercent(state.behaviorStats.planExecutionRate) }}
          </span>
        </div>
        <div class="behavior-metric">
          <span class="metric-label" title="从最近的卖出交易向前计算连续亏损笔数">当前连亏</span>
          <span class="metric-value" :class="lossStreakClass">
            {{ state.behaviorStats.currentLossStreak }}笔
          </span>
        </div>
        <div class="behavior-metric">
          <span class="metric-label" title="判定规则：7日日均交易量 > 30日日均交易量 × 1.5 时触发">过度交易</span>
          <span class="metric-value" :class="{ 'text-danger': state.behaviorStats.isOverTrading }" title="判定规则：7日日均交易量 > 30日日均交易量 × 1.5 时触发">
            {{ state.behaviorStats.isOverTrading ? '⚠️ 是' : '✅ 否' }}
          </span>
        </div>
      </div>
      <div class="behavior-alerts" v-if="state.behaviorStats.activeAlerts?.length">
        <div v-for="alert in state.behaviorStats.activeAlerts" :key="alert.alertType"
             class="behavior-alert" :class="'alert-' + alert.severity">
          {{ alert.message }}
        </div>
      </div>
    </div>
    <div v-else-if="state.behaviorStatsLoading" class="card loading-state" style="padding:0.5rem">加载健康度数据中...</div>
    <div v-else-if="state.behaviorStatsError" class="card text-danger" style="padding:0.5rem">{{ state.behaviorStatsError }}</div>

    <div class="trade-workspace">
      <div class="trade-workspace-main">
        <div class="toolbar">
          <div class="toolbar-filters">
            <button
              v-for="p in [{ key: 'day', label: '今日' }, { key: 'week', label: '本周' }, { key: 'month', label: '本月' }, { key: 'custom', label: '自定义' }]"
              :key="p.key"
              class="btn btn-sm"
              :class="state.period === p.key ? 'btn-primary' : 'btn-secondary'"
              @click="state.period = p.key; onPeriodChange()"
            >{{ p.label }}</button>
            <template v-if="state.period === 'custom'">
              <input class="input input-sm date-input" type="date" v-model="state.customFrom" @change="onPeriodChange" />
              <span class="text-secondary">至</span>
              <input class="input input-sm date-input" type="date" v-model="state.customTo" @change="onPeriodChange" />
            </template>
          </div>
          <div class="toolbar-actions">
            <div class="review-btn-group">
              <button class="btn btn-sm btn-accent" @click.stop="state.reviewMenuOpen = !state.reviewMenuOpen">
                📝 生成复盘总结
              </button>
              <div v-if="state.reviewMenuOpen" class="review-menu" @click.stop>
                <button class="review-menu-item" @click="generateReview('daily')">今日复盘</button>
                <button class="review-menu-item" @click="generateReview('weekly')">本周复盘</button>
                <button class="review-menu-item" @click="generateReview('monthly')">本月复盘</button>
                <button class="review-menu-item" @click="generateReview('custom')">自定义时段</button>
              </div>
            </div>
            <button class="btn btn-sm btn-primary" @click="openQuickEntry">快速录入</button>
            <button class="btn btn-sm btn-secondary" @click="openSettings">设置本金</button>
            <button class="btn btn-sm" style="color: var(--text-secondary); border-color: var(--border-color);" @click="handleResetAll">🔄 重置</button>
          </div>
        </div>

        <div v-if="state.summaryLoading" class="loading-state" style="padding:1rem">汇总加载中...</div>
        <div v-else-if="state.summaryError" class="text-danger" style="padding:1rem">{{ state.summaryError }}</div>
        <div class="trade-summary card" v-else-if="state.summary">
          <div class="summary-grid">
            <div class="summary-item">
              <span class="summary-label">总盈亏</span>
              <span class="summary-value" :class="pnlClass(state.summary.totalPnL)">{{ hasSummaryTrades ? formatPnL(state.summary.totalPnL) : '—' }}</span>
            </div>
            <div class="summary-item">
              <span class="summary-label">胜率</span>
              <span class="summary-value" :class="winRateClass">{{ hasSummaryTrades ? formatPercent(state.summary.winRate) : '—' }}</span>
            </div>
            <div class="summary-item">
              <span class="summary-label">盈亏比</span>
              <span class="summary-value">{{ !hasSummaryTrades ? '—' : (state.summary.profitLossRatio === -1 ? '全胜' : (state.summary.profitLossRatio == null ? '—' : state.summary.profitLossRatio.toFixed(2))) }}</span>
            </div>
            <div class="summary-item">
              <span class="summary-label">做T盈亏</span>
              <span class="summary-value" :class="pnlClass(state.summary.dayTradePnL)">{{ hasSummaryTrades ? formatPnL(state.summary.dayTradePnL) : '—' }}</span>
            </div>
            <div class="summary-item">
              <span class="summary-label">计划执行率</span>
              <span class="summary-value">{{ state.behaviorStats?.planExecutionRate == null ? '—' : formatPercent(state.behaviorStats.planExecutionRate) }}</span>
            </div>
            <div class="summary-item">
              <span class="summary-label">Agent遵守率</span>
              <span class="summary-value">{{ !hasSummaryTrades || state.summary.complianceRate == null ? '—' : formatPercent(state.summary.complianceRate) }}</span>
            </div>
            <div class="summary-item">
              <span class="summary-label">最大单笔亏损</span>
              <span class="summary-value text-fall">{{ !hasSummaryTrades || state.summary.maxSingleLoss == null || state.summary.maxSingleLoss === 0 ? '—' : formatPnL(state.summary.maxSingleLoss) }}</span>
            </div>
          </div>
        </div>

        <TradeLogPlanWorkspace
          :plans="state.planList"
          :plan-alerts="state.planAlerts"
          :loading="state.planListLoading || state.planAlertsLoading"
          :error="state.planError"
          :selected-plan-id="state.selectedPlanId"
          :selected-plan-context="state.selectedPlanContext"
          :search-value="state.planSearch"
          :scope="state.planScope"
          :refreshing="state.planListLoading || state.planAlertsLoading"
          @update:search-value="state.planSearch = $event"
          @update:scope="state.planScope = $event"
          @refresh="refreshTradingPlanWorkspace(true)"
          @select="selectPlanForWorkspace"
          @record-trade="recordTradeFromWorkspace"
          @view-stock="viewPlanStock"
        />

        <div class="trade-filter-strip card card-compact">
          <span class="summary-label">快捷筛选</span>
          <button
            v-for="filter in feedbackFilters"
            :key="filter.key"
            class="btn btn-sm"
            :class="state.feedbackFilter === filter.key ? 'btn-primary' : 'btn-secondary'"
            @click="state.feedbackFilter = filter.key"
          >{{ filter.label }}</button>
        </div>

        <div v-if="selectedPlanItem" class="trade-plan-linkage-bar card card-compact" data-testid="trade-plan-linkage-bar">
          <div>
            <strong>当前联动：</strong>
            <span>{{ selectedPlanLinkageSummary }}</span>
          </div>
          <div class="trade-plan-linkage-actions">
            <button
              v-if="state.feedbackFocusMode !== 'plan'"
              type="button"
              class="btn btn-sm btn-secondary"
              @click="selectPlanForWorkspace(selectedPlanItem, { forceRefresh: true })"
            >回到计划复盘</button>
            <button type="button" class="btn btn-sm btn-secondary" @click="clearSelectedPlanWorkspace">清除联动</button>
          </div>
        </div>

        <div class="trade-list">
          <div
            class="trade-item card card-compact"
            :class="{ 'trade-item-selected': String(state.selectedTradeId) === String(t.id) }"
            v-for="t in displayedTrades"
            :key="t.id"
            @click="selectTradeForFeedback(t)"
          >
            <div class="trade-item-header">
              <span class="trade-symbol">{{ t.symbol }} {{ t.name }}</span>
              <span class="badge" :class="directionBadgeClass(t.direction)">{{ t.direction === 'Buy' ? '买入' : '卖出' }}</span>
              <span class="badge badge-pill" v-if="t.tradeType === 'DayTrade'">做T</span>
              <span class="badge" :class="complianceBadgeClass(t.complianceTag)">{{ complianceLabel(t.complianceTag) }}</span>
              <span class="trade-time">{{ formatDateTime(t.executedAt) }}</span>
            </div>
            <div class="trade-item-body">
              <span>{{ Number(t.executedPrice).toFixed(2) }} × {{ t.quantity }}股</span>
              <span v-if="t.realizedPnL != null" :class="pnlClass(t.realizedPnL)">盈亏 {{ formatPnL(t.realizedPnL) }} ({{ formatPercent(t.returnRate) }})</span>
              <span v-if="t.planTitle" class="text-secondary">计划: {{ t.planTitle }}</span>
              <span v-if="t.executionAction" class="text-secondary">动作: {{ t.executionAction }}</span>
              <span v-if="t.agentDirection" class="text-secondary">Agent: {{ t.agentDirection }} ({{ formatPercent(t.agentConfidence) }})</span>
            </div>
            <div v-if="t.deviationTags?.length" class="trade-tag-row">
              <span v-for="tag in t.deviationTags" :key="`${t.id}-${tag}`" class="tag-chip">{{ tag }}</span>
            </div>
            <div class="trade-item-actions">
              <button class="btn btn-ghost btn-sm" @click.stop="editTrade(t)">编辑</button>
              <button class="btn btn-ghost btn-sm" @click.stop="deleteTrade(t.id)">删除</button>
            </div>
          </div>
          <div v-if="!state.tradesLoading && !displayedTrades.length" class="empty-state">{{ displayedTradesEmptyText }}</div>
          <div v-if="state.tradesLoading" class="loading-state">加载中...</div>
          <div v-if="state.tradesError" class="text-danger" style="padding:0.5rem">{{ state.tradesError }}</div>
        </div>

        <div v-if="state.showReviewPanel" class="review-panel card">
          <div class="review-panel-header">
            <h3>📋 交易复盘</h3>
            <button class="btn btn-ghost btn-sm" @click="state.showReviewPanel = false; state.reviewCurrent = null">✕</button>
          </div>
          <div v-if="state.reviewGenerating" class="loading-state"><span class="review-spinner"></span> AI 正在生成复盘总结...</div>
          <div v-else-if="state.reviewError" class="text-danger" style="padding:1rem">{{ state.reviewError }}</div>
          <div v-else-if="state.reviewCurrent" class="review-content">
            <div class="review-meta">
              <span class="badge badge-accent">{{ reviewTypeLabel(state.reviewCurrent.reviewType) }}</span>
              <span class="text-secondary">{{ formatDateTime(state.reviewCurrent.periodStart) }} - {{ formatDateTime(state.reviewCurrent.periodEnd) }}</span>
              <span>{{ state.reviewCurrent.tradeCount }} 笔卖出</span>
              <span :class="pnlClass(state.reviewCurrent.totalPnL)">盈亏 {{ formatPnL(state.reviewCurrent.totalPnL) }}</span>
              <span>胜率 {{ formatPercent(state.reviewCurrent.winRate) }}</span>
            </div>
            <div class="review-body markdown-body" v-html="reviewContentHtml"></div>
          </div>
        </div>

        <div v-if="state.reviewList.length" class="review-history card">
          <h3>📆 复盘历史</h3>
          <div class="review-history-list">
            <div class="review-history-item" v-for="r in state.reviewList" :key="r.id" @click="viewReview(r.id)">
              <span class="badge" :class="r.reviewType === 'Daily' ? 'badge-info' : r.reviewType === 'Weekly' ? 'badge-success' : 'badge-warning'">{{ reviewTypeLabel(r.reviewType) }}</span>
              <span class="text-secondary">{{ formatDateTime(r.periodStart) }}</span>
              <span :class="pnlClass(r.totalPnL)">{{ formatPnL(r.totalPnL) }}</span>
              <span>胜率 {{ formatPercent(r.winRate) }}</span>
            </div>
          </div>
        </div>
      </div>

      <aside class="feedback-panel card" data-testid="feedback-workspace">
        <div class="feedback-panel-header">
          <div>
            <h3>复盘工作区</h3>
            <p class="text-secondary">查看所选交易或预案的执行摘要与最新状态</p>
          </div>
          <div class="feedback-panel-actions">
            <span v-if="feedbackWorkspaceModel" class="badge" :class="complianceBadgeClass(feedbackWorkspaceModel.item.complianceTag)">{{ complianceLabel(feedbackWorkspaceModel.item.complianceTag) }}</span>
            <button
              type="button"
              class="btn btn-secondary btn-sm feedback-refresh-btn"
              data-testid="feedback-refresh-button"
              :disabled="state.feedbackContextLoading || !feedbackActiveItem"
              @click="refreshFeedbackWorkspace"
            >刷新当前状态</button>
          </div>
        </div>

        <div v-if="state.feedbackContextLoading" class="loading-state">正在自动加载计划最新状态...</div>
        <div v-else-if="feedbackWorkspaceModel" class="feedback-panel-body">
          <div class="feedback-card feedback-card-highlight" data-testid="feedback-summary-card">
            <div class="feedback-card-header">
              <strong>本次执行摘要</strong>
              <span class="badge" :class="complianceBadgeClass(feedbackWorkspaceModel.item.complianceTag)">{{ complianceLabel(feedbackWorkspaceModel.item.complianceTag) }}</span>
            </div>
            <div class="feedback-summary-grid">
              <div class="feedback-summary-item">
                <span class="feedback-kicker">当前对象</span>
                <strong>{{ feedbackWorkspaceModel.item.title }}</strong>
                <small class="text-secondary">{{ feedbackWorkspaceModel.objectTypeLabel }} · {{ feedbackWorkspaceModel.objectSourceLabel }}</small>
              </div>
              <div class="feedback-summary-item">
                <span class="feedback-kicker">计划动作</span>
                <strong>{{ feedbackWorkspaceModel.planActionLabel }}</strong>
              </div>
              <div class="feedback-summary-item">
                <span class="feedback-kicker">实际执行</span>
                <strong>{{ feedbackWorkspaceModel.executionActionLabel }}</strong>
              </div>
              <div class="feedback-summary-item">
                <span class="feedback-kicker">偏差标签</span>
                <div v-if="feedbackWorkspaceModel.item.deviationTags?.length" class="trade-tag-row">
                  <span v-for="tag in feedbackWorkspaceModel.item.deviationTags" :key="tag" class="tag-chip">{{ tag }}</span>
                </div>
                <p v-else class="feedback-inline-note">{{ feedbackWorkspaceModel.deviationSummary }}</p>
              </div>
            </div>
            <p class="feedback-description"><strong>一句话总结：</strong>{{ feedbackWorkspaceModel.oneLineSummary }}</p>
          </div>

          <div class="feedback-card" data-testid="feedback-situation-card">
            <div class="feedback-card-header">
              <strong>执行当时情况</strong>
              <span class="badge badge-pill">{{ feedbackWorkspaceModel.executionSituation.snapshotLabel }}</span>
            </div>
            <div class="feedback-fact-list">
              <div class="feedback-fact-item">
                <span class="feedback-kicker">下单时场景</span>
                <p>{{ feedbackWorkspaceModel.executionSituation.sceneSummary }}</p>
              </div>
              <div class="feedback-fact-item">
                <span class="feedback-kicker">当时持仓情况</span>
                <p>{{ feedbackWorkspaceModel.executionSituation.positionSummary }}</p>
              </div>
            </div>
            <div class="feedback-meta-row">
              <span v-if="feedbackWorkspaceModel.executionSituation.marketStage">当时市场阶段 {{ feedbackWorkspaceModel.executionSituation.marketStage }}</span>
              <span v-if="feedbackWorkspaceModel.executionSituation.referencePrice != null">参考价 {{ formatMoney(feedbackWorkspaceModel.executionSituation.referencePrice) }}</span>
              <span v-if="feedbackWorkspaceModel.executionSituation.planStatus">计划状态 {{ feedbackWorkspaceModel.executionSituation.planStatus }}</span>
            </div>
          </div>

          <div v-if="feedbackWorkspaceModel.currentStatus" class="feedback-card feedback-card-subtle" data-testid="feedback-current-status-card">
            <div class="feedback-card-header">
              <strong>当前最新状态</strong>
              <span class="badge" :class="scenarioBadgeClass(feedbackWorkspaceModel.currentStatus.code)">{{ feedbackWorkspaceModel.currentStatus.label }}</span>
            </div>
            <p>{{ feedbackWorkspaceModel.currentStatus.scenarioSummary }}</p>
            <div class="feedback-fact-list feedback-fact-list-compact">
              <div class="feedback-fact-item">
                <span class="feedback-kicker">当前持仓</span>
                <p>{{ feedbackWorkspaceModel.currentStatus.positionSummary }}</p>
              </div>
              <div class="feedback-fact-item">
                <span class="feedback-kicker">最新回写</span>
                <p>{{ feedbackWorkspaceModel.currentStatus.executionSummary }}</p>
              </div>
            </div>
            <p v-if="feedbackWorkspaceModel.currentStatus.portfolioSummary" class="text-secondary">{{ feedbackWorkspaceModel.currentStatus.portfolioSummary }}</p>
          </div>

          <div v-else-if="feedbackRuntimeUnavailableMessage" class="feedback-note-card" data-testid="feedback-runtime-note">
            {{ feedbackRuntimeUnavailableMessage }}
          </div>

          <div class="feedback-card" :class="feedbackWorkspaceModel.focusPoints.length ? 'feedback-card-alert' : 'feedback-card-soft'" data-testid="feedback-focus-card">
            <div class="feedback-card-header">
              <strong>复盘关注点</strong>
            </div>
            <ul v-if="feedbackWorkspaceModel.focusPoints.length" class="feedback-focus-list">
              <li v-for="point in feedbackWorkspaceModel.focusPoints" :key="point">{{ point }}</li>
            </ul>
            <p v-else class="feedback-description">{{ feedbackWorkspaceModel.gentleHint }}</p>
          </div>
        </div>
        <div v-else class="empty-state feedback-empty">请先选一条交易记录，或从股票信息里的计划卡点击「录入执行」，这里会整理执行摘要与最新状态。</div>
        <div v-if="state.feedbackContextError" class="text-danger feedback-error">{{ state.feedbackContextError }}</div>
      </aside>
    </div>

    <!-- 交易录入弹窗 -->
    <div v-if="state.tradeModalOpen" class="trade-modal-backdrop" @click="state.tradeModalOpen = false">
      <div class="trade-modal card card-elevated" @click.stop>
        <div class="trade-modal-header">
          <h3>{{ state.editingTradeId ? '编辑交易' : (state.tradeForm.planId ? '录入执行' : '快速录入') }}</h3>
          <button class="btn btn-ghost btn-sm" @click="state.tradeModalOpen = false">✕</button>
        </div>
        <form @submit.prevent="saveTrade" @click="searchOpen = false" class="trade-form">
          <div v-if="state.tradeForm.planId" class="execution-context-grid">
            <div class="context-card" :class="{ 'context-card-loading': state.tradeFormContextLoading }">
              <h4>预案来源 / 场景</h4>
              <template v-if="state.tradeFormContextLoading">
                <div class="feedback-meta-row">
                  <span class="badge badge-pill">加载中</span>
                  <span class="badge badge-info">同步中</span>
                </div>
                <p class="context-loading-text">正在加载预案执行上下文，请稍候...</p>
                <div class="context-skeleton" aria-hidden="true">
                  <span class="skeleton-line skeleton-line-short"></span>
                  <span class="skeleton-line"></span>
                  <span class="skeleton-line skeleton-line-long"></span>
                </div>
              </template>
              <template v-else>
                <div class="feedback-meta-row">
                  <span class="badge badge-pill">{{ tradeFormContextSourceLabel() }}</span>
                  <span class="badge" :class="scenarioBadgeClass(state.tradeFormContext?.scenarioStatus?.code)">{{ tradeFormContextScenarioBadgeLabel() }}</span>
                </div>
                <p>{{ tradeFormContextSummaryText() }}</p>
                <small class="text-secondary">{{ tradeFormContextPortfolioSummaryText() }}</small>
              </template>
            </div>
            <div class="context-card" :class="{ 'context-card-loading': state.tradeFormContextLoading }">
              <h4>当前场景状态</h4>
              <template v-if="state.tradeFormContextLoading">
                <p class="context-loading-text">正在加载场景状态...</p>
                <div class="context-skeleton" aria-hidden="true">
                  <span class="skeleton-line"></span>
                  <span class="skeleton-line skeleton-line-long"></span>
                </div>
              </template>
              <template v-else>
                <p>{{ summarizeTradeFormScenario() }}</p>
                <div class="feedback-meta-row" v-if="state.tradeFormContext?.scenarioStatus">
                  <span v-if="state.tradeFormContext.scenarioStatus.marketStage">市场阶段 {{ state.tradeFormContext.scenarioStatus.marketStage }}</span>
                  <span v-if="state.tradeFormContext.scenarioStatus.referencePrice != null">参考价 {{ formatMoney(state.tradeFormContext.scenarioStatus.referencePrice) }}</span>
                </div>
              </template>
            </div>
            <div class="context-card" :class="{ 'context-card-loading': state.tradeFormContextLoading }">
              <h4>当前持仓快照</h4>
              <template v-if="state.tradeFormContextLoading">
                <p class="context-loading-text">正在加载持仓快照...</p>
                <div class="context-skeleton" aria-hidden="true">
                  <span class="skeleton-line"></span>
                  <span class="skeleton-line skeleton-line-short"></span>
                </div>
              </template>
              <template v-else>
                <p>{{ summarizeTradeFormPosition() }}</p>
                <div class="feedback-meta-row" v-if="state.tradeFormContext?.currentPositionSnapshot">
                  <span>仓位 {{ formatPercent(state.tradeFormContext.currentPositionSnapshot.positionRatio) }}</span>
                  <span>市值 {{ formatMoney(state.tradeFormContext.currentPositionSnapshot.marketValue) }}</span>
                </div>
              </template>
            </div>
          </div>

          <div v-if="tradeFormIdentityItems.length" class="trade-identity-strip" data-testid="trade-identity-strip">
            <div v-for="item in tradeFormIdentityItems" :key="item.key" class="trade-identity-chip">
              <span class="trade-identity-label">{{ item.label }}</span>
              <strong class="trade-identity-value">{{ item.value }}</strong>
            </div>
          </div>

          <div class="trade-form-section">
            <div class="trade-form-section-header">
              <div>
                <h4>快速确认</h4>
                <p class="text-secondary">先完成执行动作、价格、数量、时间与偏差标签。</p>
              </div>
            </div>

            <div class="form-grid trade-form-primary-grid">
            <div v-if="!showReadonlyTradeIdentity" class="form-group" style="position: relative;">
              <label>股票代码</label>
              <input class="input input-sm"
                     data-testid="trade-symbol"
                     :value="state.tradeForm.symbol"
                     @input="onSymbolInput"
                     @blur="onSymbolBlur"
                     @focus="state.tradeForm.symbol && searchStocks(state.tradeForm.symbol)"
                     placeholder="输入代码/名称/拼音"
                     required
                     :disabled="!!state.tradeForm.planId || !!state.editingTradeId"
                     autocomplete="off" />
              <div v-if="searchOpen" class="search-dropdown">
                <div v-if="searchLoading" class="search-dropdown-loading">搜索中...</div>
                <div v-for="item in searchResults" :key="item.Symbol || item.symbol"
                     @mousedown.prevent="selectStock(item)"
                     class="search-result-item">
                  <span>{{ item.Name || item.name }}</span>
                  <span style="color: var(--color-text-secondary);">{{ item.Code || item.code }}</span>
                </div>
              </div>
              <div v-if="state.tradeFormSymbolMismatch" class="text-warning" data-testid="trade-symbol-mismatch" style="font-size:12px;margin-top:4px;">
                ⚠ 代码与名称不一致，请确认：{{ state.tradeFormSymbolMismatch }}
              </div>
            </div>
            <div v-if="!showReadonlyTradeIdentity" class="form-group">
              <label>股票名称</label>
              <input class="input input-sm" data-testid="trade-name" v-model="state.tradeForm.name" @blur="onNameBlur" required :disabled="!!state.editingTradeId" />
            </div>
            <div v-if="!showReadonlyTradeIdentity" class="form-group">
              <label>方向</label>
              <select class="input input-sm" data-testid="trade-direction" v-model="state.tradeForm.direction" :disabled="!!state.editingTradeId">
                <option value="Buy">买入</option>
                <option value="Sell">卖出</option>
              </select>
            </div>
            <div v-if="!showReadonlyTradeIdentity" class="form-group">
              <label>类型</label>
              <select class="input input-sm" data-testid="trade-type" v-model="state.tradeForm.tradeType" :disabled="!!state.editingTradeId">
                <option value="Normal">普通</option>
                <option value="DayTrade">做T</option>
              </select>
            </div>
            <div class="form-group">
              <label>本次执行动作</label>
              <select class="input input-sm" data-testid="trade-execution-action" v-model="state.tradeForm.executionAction">
                <option v-for="action in executionActionOptions" :key="action" :value="action">{{ action }}</option>
              </select>
            </div>
            <div class="form-group">
              <label>成交价</label>
              <input class="input input-sm" data-testid="trade-executed-price" type="number" step="0.001" min="0.001" v-model="state.tradeForm.executedPrice" required />
            </div>
            <div class="form-group">
              <label>数量（股）</label>
              <input class="input input-sm" data-testid="trade-quantity" type="number" step="100" v-model="state.tradeForm.quantity" required />
            </div>
            <div class="form-group">
              <label>成交时间</label>
              <input class="input input-sm" data-testid="trade-executed-at" type="datetime-local" v-model="state.tradeForm.executedAt" required />
            </div>
            <div class="form-group">
              <label>手续费</label>
              <input class="input input-sm" data-testid="trade-commission" type="number" step="0.01" v-model="state.tradeForm.commission" />
            </div>
            <div class="form-group form-group-full">
              <label>偏差标签</label>
              <div class="tag-selector">
                <label v-for="tag in deviationTagOptions" :key="tag" class="tag-selector-item">
                  <input type="checkbox" :value="tag" v-model="state.tradeForm.deviationTags" />
                  <span>{{ tag }}</span>
                </label>
              </div>
            </div>
            </div>
          </div>

          <div class="trade-form-section trade-form-section-subtle">
            <div class="detail-feedback-header">
              <div>
                <h4>详细反馈 / 更多说明</h4>
                <p class="text-secondary">{{ tradeFormDetailedFeedbackSummary }}</p>
              </div>
              <button
                type="button"
                class="btn btn-secondary btn-sm trade-detail-toggle-btn"
                data-testid="trade-detail-toggle"
                :aria-expanded="state.tradeFormDetailOpen ? 'true' : 'false'"
                @click="state.tradeFormDetailOpen = !state.tradeFormDetailOpen"
              >{{ state.tradeFormDetailOpen ? '收起' : '展开' }}</button>
            </div>
            <div v-if="tradeFormDetailedFeedbackLabels.length" class="trade-detail-badge-row">
              <span v-for="label in tradeFormDetailedFeedbackLabels" :key="label" class="trade-detail-badge">{{ label }}已填写</span>
            </div>
            <div v-if="state.tradeFormDetailOpen" class="form-grid trade-detail-grid" data-testid="trade-detail-panel">
              <div class="form-group form-group-full">
                <label>偏差说明</label>
                <textarea class="input input-sm" data-testid="trade-deviation-note" v-model="state.tradeForm.deviationNote" rows="2" placeholder="补充说明本次偏差发生的原因、判断或临场变化"></textarea>
              </div>
              <div class="form-group form-group-full">
                <label>放弃原因</label>
                <textarea class="input input-sm" data-testid="trade-abandon-reason" v-model="state.tradeForm.abandonReason" rows="2" placeholder="如本次动作意味着放弃原计划，可补充原因"></textarea>
              </div>
              <div class="form-group form-group-full">
                <label>备注</label>
                <textarea class="input input-sm" data-testid="trade-user-note" v-model="state.tradeForm.userNote" rows="2"></textarea>
              </div>
            </div>
          </div>
          <div class="trade-modal-actions">
            <button type="button" class="btn btn-secondary btn-sm" @click="state.tradeModalOpen = false">取消</button>
            <button type="submit" class="btn btn-primary btn-sm" :disabled="state.tradeFormSaving">
              {{ state.tradeFormSaving ? '保存中...' : '保存' }}
            </button>
          </div>
          <div v-if="state.tradeFormContextError" class="text-danger" style="margin-top:8px">{{ state.tradeFormContextError }}</div>
          <div v-if="state.tradeFormError" class="text-danger" style="margin-top:8px">{{ state.tradeFormError }}</div>
        </form>
      </div>
    </div>

    <!-- 本金设置弹窗 -->
    <div v-if="state.settingsModalOpen" class="trade-modal-backdrop" @click="state.settingsModalOpen = false">
      <div class="trade-modal card card-elevated" style="max-width: 400px" @click.stop>
        <div class="trade-modal-header">
          <h3>设置本金</h3>
          <button class="btn btn-ghost btn-sm" @click="state.settingsModalOpen = false">✕</button>
        </div>
        <form @submit.prevent="saveSettings">
          <div class="form-group">
            <label>总本金（元）</label>
            <input class="input" type="number" step="0.01" v-model="state.capitalInput" required />
          </div>
          <div class="trade-modal-actions" style="margin-top:12px">
            <button type="button" class="btn btn-secondary btn-sm" @click="state.settingsModalOpen = false">取消</button>
            <button type="submit" class="btn btn-primary btn-sm" :disabled="state.settingsSaving">保存</button>
          </div>
        </form>
      </div>
    </div>
  </div>
</template>

<style scoped>
.trade-log-tab {
  display: grid;
  gap: var(--space-4);
  padding: var(--space-5);
}

.trade-workspace {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 340px;
  gap: var(--space-4);
  align-items: start;
}

.trade-workspace-main {
  display: grid;
  gap: var(--space-4);
}

.feedback-panel {
  display: grid;
  gap: var(--space-3);
  position: sticky;
  top: var(--space-4);
}

.feedback-panel-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--space-3);
}

.feedback-panel-actions {
  display: grid;
  justify-items: end;
  gap: var(--space-2);
}

.feedback-refresh-btn {
  white-space: nowrap;
}

.feedback-panel-header h3 {
  margin: 0;
  font-size: var(--text-lg);
}

.feedback-panel-header p {
  margin: 4px 0 0;
  font-size: var(--text-sm);
}

.feedback-panel-body {
  display: grid;
  gap: var(--space-3);
}

.feedback-card {
  display: grid;
  gap: var(--space-2);
  padding: var(--space-3);
  border-radius: var(--radius-lg);
  background: rgba(248, 250, 252, 0.92);
  border: 1px solid var(--color-border-light);
}

.feedback-card-highlight {
  background: linear-gradient(180deg, rgba(239, 246, 255, 0.95), rgba(248, 250, 252, 0.95));
  border-color: rgba(96, 165, 250, 0.28);
}

.feedback-card-subtle {
  background: rgba(239, 246, 255, 0.7);
}

.feedback-card-alert {
  background: rgba(255, 247, 237, 0.95);
  border-color: rgba(251, 146, 60, 0.26);
}

.feedback-card-soft {
  background: rgba(238, 242, 255, 0.72);
}

.feedback-card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: var(--space-2);
}

.feedback-meta-row {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2);
  font-size: var(--text-sm);
  color: var(--color-text-secondary);
}

.feedback-description {
  margin: 0;
  font-size: var(--text-sm);
  color: var(--color-text-body);
}

.feedback-description strong {
  color: var(--color-text-primary);
}

.feedback-summary-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: var(--space-2);
}

.feedback-summary-item,
.feedback-fact-item {
  display: grid;
  gap: 4px;
  padding: 0.75rem 0.85rem;
  border-radius: 12px;
  background: rgba(255, 255, 255, 0.68);
}

.feedback-kicker {
  font-size: var(--text-xs);
  color: var(--color-text-muted);
}

.feedback-inline-note {
  margin: 0;
  font-size: var(--text-sm);
  color: var(--color-text-secondary);
}

.feedback-fact-list {
  display: grid;
  gap: var(--space-2);
}

.feedback-fact-list-compact {
  grid-template-columns: 1fr;
}

.feedback-fact-item p {
  margin: 0;
  font-size: var(--text-sm);
  color: var(--color-text-body);
}

.feedback-note-card {
  padding: var(--space-3);
  border-radius: var(--radius-lg);
  background: rgba(248, 250, 252, 0.78);
  border: 1px dashed rgba(148, 163, 184, 0.55);
  color: var(--color-text-secondary);
  font-size: var(--text-sm);
}

.feedback-focus-list {
  margin: 0;
  padding-left: 1.1rem;
  display: grid;
  gap: 6px;
  color: var(--color-text-body);
  font-size: var(--text-sm);
}

.feedback-error {
  font-size: var(--text-sm);
}

.feedback-empty {
  min-height: 220px;
  display: grid;
  place-items: center;
}

.trade-filter-strip {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: var(--space-2);
}

.trade-plan-linkage-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-3);
  flex-wrap: wrap;
}

.trade-plan-linkage-actions {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex-wrap: wrap;
}

.trade-item-selected {
  border-color: rgba(37, 99, 235, 0.35);
  box-shadow: 0 0 0 1px rgba(37, 99, 235, 0.08);
}

.trade-tag-row {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.tag-chip {
  display: inline-flex;
  align-items: center;
  padding: 2px 8px;
  border-radius: 999px;
  background: rgba(245, 158, 11, 0.14);
  color: #92400e;
  font-size: 12px;
}

/* ── 持仓总览 ── */
.portfolio-overview {
  display: grid;
  gap: var(--space-3);
}
.portfolio-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.portfolio-header h3 {
  margin: 0;
  font-size: var(--text-lg);
  font-weight: 700;
  color: var(--color-text-primary);
}
.portfolio-metrics {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(120px, 1fr));
  gap: var(--space-3);
}
.metric {
  display: flex;
  flex-direction: column;
  gap: var(--space-0-5);
}
.metric-label {
  font-size: var(--text-sm);
  color: var(--color-text-secondary);
}
.metric-value {
  font-size: var(--text-lg);
  font-weight: 700;
  color: var(--color-text-primary);
  font-family: var(--font-family-mono);
}
.position-bar {
  height: 6px;
  border-radius: var(--radius-full);
  background: var(--color-bg-inset);
  overflow: hidden;
}
.position-bar-fill {
  height: 100%;
  border-radius: var(--radius-full);
  transition: width var(--transition-normal);
}
.bar-safe { background: var(--color-success); }
.bar-warning { background: var(--color-warning); }
.bar-danger { background: var(--color-danger); }

.position-list {
  display: grid;
  gap: var(--space-2);
  margin-top: var(--space-1);
}
.position-item {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  font-size: var(--text-sm);
  color: var(--color-text-body);
  padding: var(--space-1-5) 0;
  border-bottom: 1px solid var(--color-border-light);
}
.position-item:last-child { border-bottom: none; }
.position-item-clickable {
  cursor: pointer;
  transition: background-color 0.15s ease;
}
.position-item-clickable:hover,
.position-item-clickable:focus {
  background-color: var(--color-bg-hover, rgba(0,0,0,0.04));
  outline: none;
}
.position-symbol {
  font-weight: 600;
  min-width: 120px;
}

/* ── 仓位暴露条 ── */
.exposure-bar-card {
  display: grid;
  gap: var(--space-2);
}
.exposure-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.exposure-title {
  font-size: var(--text-base);
  font-weight: 700;
  color: var(--color-text-primary);
}
.exposure-detail {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  font-size: var(--text-sm);
  flex-wrap: wrap;
}
.exposure-item {
  display: flex;
  flex-direction: column;
  gap: 2px;
  text-align: center;
}
.exposure-label {
  font-size: var(--text-xs);
  color: var(--color-text-muted);
}
.exposure-value {
  font-weight: 700;
  font-family: var(--font-family-mono);
  color: var(--color-text-primary);
}
.exposure-plus, .exposure-eq {
  font-weight: 700;
  color: var(--color-text-muted);
}
.exposure-warning {
  font-size: var(--text-sm);
  color: var(--color-danger);
  font-weight: 600;
  padding: var(--space-1) var(--space-2);
  background: var(--color-danger-subtle);
  border-radius: var(--radius-md);
}
.exposure-symbols {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2);
}
.exposure-symbol-item {
  font-size: var(--text-xs);
  padding: 2px 6px;
  background: var(--color-bg-inset);
  border-radius: var(--radius-sm);
  color: var(--color-text-body);
}

/* ── 工具栏 ── */
.toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-3);
  flex-wrap: wrap;
}
.toolbar-filters {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex-wrap: wrap;
}
.toolbar-actions {
  display: flex;
  align-items: center;
  gap: var(--space-2);
}
.date-input {
  width: 140px;
}

/* ── 盈亏汇总 ── */
.trade-summary {
  display: grid;
  gap: var(--space-3);
}
.summary-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: var(--space-3);
}
.summary-item {
  display: flex;
  flex-direction: column;
  gap: var(--space-0-5);
}
.summary-label {
  font-size: var(--text-sm);
  color: var(--color-text-secondary);
}
.summary-value {
  font-size: var(--text-lg);
  font-weight: 700;
  color: var(--color-text-primary);
  font-family: var(--font-family-mono);
}

/* ── 交易列表 ── */
.trade-list {
  display: grid;
  gap: var(--space-2);
}
.trade-item {
  display: grid;
  gap: var(--space-2);
}
.trade-item-header {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex-wrap: wrap;
}
.trade-symbol {
  font-weight: 700;
  color: var(--color-text-primary);
}
.trade-time {
  margin-left: auto;
  font-size: var(--text-sm);
  color: var(--color-text-muted);
}
.trade-item-body {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  font-size: var(--text-sm);
  color: var(--color-text-body);
  flex-wrap: wrap;
}
.trade-item-actions {
  display: flex;
  justify-content: flex-end;
}

.execution-context-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: var(--space-3);
  margin-bottom: var(--space-4);
}

.context-card {
  display: grid;
  gap: var(--space-2);
  padding: var(--space-3);
  border-radius: var(--radius-lg);
  background: rgba(248, 250, 252, 0.92);
  border: 1px solid var(--color-border-light);
}

.context-card h4 {
  margin: 0;
  font-size: var(--text-base);
}

.context-card p,
.context-card small {
  margin: 0;
}

.context-card-loading {
  overflow: hidden;
}

.context-loading-text {
  margin: 0;
  font-size: var(--text-sm);
  color: var(--color-text-secondary);
}

.context-skeleton {
  display: grid;
  gap: 8px;
}

.skeleton-line {
  display: block;
  width: 100%;
  height: 12px;
  border-radius: 999px;
  background: linear-gradient(90deg, rgba(226, 232, 240, 0.9) 0%, rgba(241, 245, 249, 1) 50%, rgba(226, 232, 240, 0.9) 100%);
  background-size: 200% 100%;
  animation: skeleton-shimmer 1.2s ease-in-out infinite;
}

.skeleton-line-short {
  width: 46%;
}

.skeleton-line-long {
  width: 82%;
}

/* ── 弹窗 ── */
.trade-modal-backdrop {
  position: fixed;
  inset: 0;
  background: var(--color-bg-overlay);
  backdrop-filter: blur(4px);
  z-index: var(--z-modal);
  display: flex;
  align-items: center;
  justify-content: center;
}
.trade-modal {
  width: 90%;
  max-width: 640px;
  max-height: 90vh;
  overflow-y: auto;
}
.trade-modal-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: var(--space-4);
}
.trade-modal-header h3 {
  margin: 0;
  font-size: var(--text-lg);
  font-weight: 700;
  color: var(--color-text-primary);
}
.trade-modal-actions {
  display: flex;
  justify-content: flex-end;
  gap: var(--space-2);
  margin-top: var(--space-4);
}

/* ── 表单 ── */
.form-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: var(--space-3);
}
.trade-form {
  display: grid;
  gap: var(--space-4);
}
.trade-form-section {
  display: grid;
  gap: var(--space-3);
}
.trade-form-section-subtle {
  padding: var(--space-3);
  border-radius: var(--radius-lg);
  border: 1px solid var(--color-border-light);
  background: rgba(248, 250, 252, 0.7);
}
.trade-form-section-header,
.detail-feedback-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--space-3);
}
.trade-form-section-header h4,
.detail-feedback-header h4 {
  margin: 0;
  font-size: var(--text-base);
}
.trade-form-section-header p,
.detail-feedback-header p {
  margin: 4px 0 0;
  font-size: var(--text-sm);
}
.trade-identity-strip {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(120px, 1fr));
  gap: var(--space-2);
}
.trade-identity-chip {
  display: grid;
  gap: 4px;
  padding: 0.7rem 0.8rem;
  border-radius: 12px;
  border: 1px solid var(--color-border-light);
  background: rgba(248, 250, 252, 0.9);
}
.trade-identity-label {
  font-size: var(--text-xs);
  color: var(--color-text-muted);
}
.trade-identity-value {
  font-size: var(--text-sm);
  color: var(--color-text-primary);
}
.form-group {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
}
.form-group label {
  font-size: var(--text-sm);
  font-weight: 600;
  color: var(--color-text-secondary);
}
.form-group-full {
  grid-column: 1 / -1;
}

.tag-selector {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2);
}

.tag-selector-item {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 6px 10px;
  border-radius: 999px;
  border: 1px solid var(--color-border-light);
  background: rgba(248, 250, 252, 0.92);
  font-size: var(--text-sm);
  cursor: pointer;
}

.trade-detail-toggle-btn {
  flex-shrink: 0;
}

.trade-detail-badge-row {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.trade-detail-badge {
  display: inline-flex;
  align-items: center;
  padding: 2px 8px;
  border-radius: 999px;
  background: rgba(14, 165, 233, 0.1);
  color: #0c4a6e;
  font-size: 12px;
}

/* ── 状态 ── */
.loading-state {
  text-align: center;
  padding: var(--space-6);
  color: var(--color-text-muted);
  font-size: var(--text-sm);
}
.text-rise { color: var(--color-market-rise); }
.text-fall { color: var(--color-market-fall); }
.text-secondary { color: var(--color-text-secondary); }
.text-danger { color: var(--color-danger); }

@media (max-width: 640px) {
  .portfolio-metrics,
  .summary-grid {
    grid-template-columns: repeat(2, 1fr);
  }
  .feedback-summary-grid {
    grid-template-columns: 1fr;
  }
  .form-grid {
    grid-template-columns: 1fr;
  }
  .form-group-full {
    grid-column: auto;
  }
  .execution-context-grid {
    grid-template-columns: 1fr;
  }
}

.execution-mode-tag {
  display: inline-flex;
  align-items: center;
  border-radius: 999px;
  padding: 0.15rem 0.55rem;
  font-size: 0.75rem;
  font-weight: 600;
  white-space: nowrap;
}

.execution-mode-tag.mode-normal { background: rgba(22, 163, 74, 0.12); color: #15803d; }
.execution-mode-tag.mode-confirm { background: rgba(234, 179, 8, 0.15); color: #a16207; }
.execution-mode-tag.mode-strong-confirm { background: rgba(234, 179, 8, 0.25); color: #92400e; }
.execution-mode-tag.mode-discouraged { background: rgba(239, 68, 68, 0.15); color: #b91c1c; }

/* ── 复盘按钮组 ── */
.review-btn-group {
  position: relative;
}
.btn-accent {
  background: var(--color-accent, #6366f1);
  color: #fff;
  border: none;
  cursor: pointer;
  border-radius: var(--radius-md);
  padding: 0.25rem 0.75rem;
  font-size: var(--text-sm);
  font-weight: 600;
}
.btn-accent:hover {
  opacity: 0.9;
}
.review-menu {
  position: absolute;
  top: 100%;
  right: 0;
  margin-top: 4px;
  background: var(--color-bg-elevated, #fff);
  border: 1px solid var(--color-border-light);
  border-radius: var(--radius-md);
  box-shadow: var(--shadow-lg, 0 4px 12px rgba(0,0,0,0.1));
  z-index: 10;
  min-width: 140px;
  overflow: hidden;
}
.review-menu-item {
  display: block;
  width: 100%;
  padding: 0.5rem 0.75rem;
  border: none;
  background: none;
  text-align: left;
  font-size: var(--text-sm);
  color: var(--color-text-body);
  cursor: pointer;
}
.review-menu-item:hover {
  background: var(--color-bg-hover, #f3f4f6);
}

/* ── 复盘面板 ── */
.review-panel {
  display: grid;
  gap: var(--space-3);
}
.review-panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.review-panel-header h3 {
  margin: 0;
  font-size: var(--text-lg);
  font-weight: 700;
  color: var(--color-text-primary);
}
.review-meta {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  flex-wrap: wrap;
  font-size: var(--text-sm);
  color: var(--color-text-body);
  margin-bottom: var(--space-3);
}
.badge-accent {
  background: rgba(99, 102, 241, 0.12);
  color: #6366f1;
}
.review-body {
  font-size: var(--text-sm);
  line-height: 1.7;
  color: var(--color-text-body);
}
.review-body :deep(h3) {
  font-size: var(--text-base);
  font-weight: 700;
  color: var(--color-text-primary);
  margin: 1rem 0 0.5rem;
}
.review-body :deep(ul),
.review-body :deep(ol) {
  padding-left: 1.2em;
}
.review-body :deep(li) {
  margin: 0.25rem 0;
}
.review-spinner {
  display: inline-block;
  width: 16px;
  height: 16px;
  border: 2px solid var(--color-border-light);
  border-top-color: var(--color-accent, #6366f1);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}
@keyframes spin { to { transform: rotate(360deg); } }

@keyframes skeleton-shimmer {
  0% { background-position: 200% 0; }
  100% { background-position: -200% 0; }
}

/* ── 复盘历史 ── */
.review-history {
  display: grid;
  gap: var(--space-2);
}
.review-history h3 {
  margin: 0;
  font-size: var(--text-base);
  font-weight: 700;
  color: var(--color-text-primary);
}
.review-history-list {
  display: grid;
  gap: var(--space-1);
}
.review-history-item {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  padding: var(--space-2) var(--space-1);
  border-bottom: 1px solid var(--color-border-light);
  cursor: pointer;
  font-size: var(--text-sm);
  transition: background var(--transition-fast);
}
.review-history-item:hover {
  background: var(--color-bg-hover, #f3f4f6);
}
.review-history-item:last-child {
  border-bottom: none;
}

/* ── 交易健康度 ── */
.behavior-dashboard {
  display: grid;
  gap: var(--space-3);
}
.behavior-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.behavior-header h4 {
  margin: 0;
  font-size: var(--text-base);
  font-weight: 700;
  color: var(--color-text-primary);
}
.discipline-score {
  font-size: var(--text-xl, 1.25rem);
  font-weight: 800;
  font-family: var(--font-family-mono);
  padding: 0.15rem 0.6rem;
  border-radius: var(--radius-md);
}
.score-good { color: #15803d; background: rgba(22, 163, 74, 0.12); }
.score-warn { color: #a16207; background: rgba(234, 179, 8, 0.15); }
.score-danger { color: #b91c1c; background: rgba(239, 68, 68, 0.15); }
.behavior-metrics {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: var(--space-3);
}
.behavior-metric {
  display: flex;
  flex-direction: column;
  gap: var(--space-0-5);
}
.text-warning { color: var(--color-warning, #a16207); }
.behavior-alerts {
  display: grid;
  gap: var(--space-1-5);
}
.behavior-alert {
  font-size: var(--text-sm);
  padding: var(--space-1-5) var(--space-2);
  border-radius: var(--radius-md);
  font-weight: 500;
}
.alert-info { background: rgba(59, 130, 246, 0.1); color: #1d4ed8; }
.alert-warning { background: rgba(234, 179, 8, 0.15); color: #92400e; }
.alert-danger { background: rgba(239, 68, 68, 0.12); color: #b91c1c; }

@media (max-width: 640px) {
  .behavior-metrics {
    grid-template-columns: repeat(2, 1fr);
  }
}

@media (max-width: 1100px) {
  .trade-workspace {
    grid-template-columns: 1fr;
  }

  .feedback-panel {
    position: static;
  }
}

/* ── Stock search dropdown ── */
.search-dropdown {
  position: absolute;
  z-index: 100;
  left: 0;
  right: 0;
  top: 100%;
  background: var(--color-bg-elevated, #1e1e1e);
  border: 1px solid var(--color-border-light);
  border-radius: var(--radius-md);
  max-height: 200px;
  overflow-y: auto;
  box-shadow: 0 4px 8px rgba(0, 0, 0, 0.3);
}
.search-dropdown-loading {
  padding: 8px;
  color: var(--color-text-secondary);
  font-size: 12px;
}
.search-result-item {
  padding: 6px 10px;
  cursor: pointer;
  display: flex;
  justify-content: space-between;
  font-size: 13px;
  color: var(--color-text-body);
}
.search-result-item:hover {
  background: var(--color-bg-hover, #2a2a2a);
}
</style>
