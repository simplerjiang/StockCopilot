<script setup>
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import MarketTemperatureBar from './MarketTemperatureBar.vue'
import IndexMetricStrip from './IndexMetricStrip.vue'
import BreadthBucketChart from './BreadthBucketChart.vue'
import CapitalBreadthPanel from './CapitalBreadthPanel.vue'
import SectorToolbar from './SectorToolbar.vue'
import SectorRankingList from './SectorRankingList.vue'
import SectorDetailPanel from './SectorDetailPanel.vue'
import SentimentHistoryStrip from './SentimentHistoryStrip.vue'
import DataAuditPanel from './DataAuditPanel.vue'

// ── UI state ──
const boardType = ref('concept')
const sort = ref('strength')
const compareWindow = ref('10d')
const page = ref(1)
const pageSize = ref(12)
const loading = ref(false)
const syncing = ref(false)
const syncCooldown = ref(0)
let syncCooldownTimer = null
const detailLoading = ref(false)
const realtimeLoading = ref(false)
const realtimeSectorBoardLoading = ref(false)
const error = ref('')
const detailError = ref('')
const realtimeError = ref('')
const realtimeSectorBoardError = ref('')

// ── Data refs ──
const summary = ref(null)
const history = ref([])
const auditData = ref(null)
const sectorBaseItems = ref([])
const sectors = ref([])
const total = ref(0)
const snapshotTime = ref('')
const selectedSectorCode = ref('')
const detail = ref(null)
const realtimeOverviewEnabled = ref(localStorage.getItem('market_realtime_overview_enabled') !== 'false')
const realtimeOverview = ref(null)
const realtimeSectorBoardEnabled = ref(localStorage.getItem('market_realtime_sector_board_enabled') !== 'false')
const realtimeSectorBoard = ref(null)
const sectorPageStatus = ref({ isDegraded: false, degradeReason: '' })
const syncFeedback = ref(null)
const initialDashboardResolved = ref(false)

// ── Constants ──
const REFRESH_INTERVAL = 30_000
let refreshTimer = null

const boardOptions = [
  { value: 'concept', label: '概念轮动' },
  { value: 'industry', label: '行业轮动' },
  { value: 'style', label: '风格轮动' }
]
const compareWindowOptions = [
  { value: '5d', label: '5日持续' },
  { value: '10d', label: '10日主线' },
  { value: '20d', label: '20日趋势' }
]
const sortOptions = [
  { value: 'strength', label: '综合强度' },
  { value: 'change', label: '涨幅优先' },
  { value: 'flow', label: '净流入优先' },
  { value: 'breadth', label: '领涨扩散' },
  { value: 'continuity', label: '连续性' }
]
const boardLabelMap = { concept: '概念', industry: '行业', style: '风格' }
const DEGRADE_FALLBACK_TEXT = '部分指标暂不可用，请以下方审计面板为准'

// ── Helpers ──
const hasPositiveMetricValues = values => values.some(value => Number(value ?? 0) > 0)
const normalizeOptionalNumber = value => {
  if (value === null || value === undefined || value === '') return null
  const number = Number(value)
  return Number.isFinite(number) ? number : null
}
const toBoolean = value => {
  if (typeof value === 'boolean') return value
  if (typeof value === 'number') return value !== 0
  if (typeof value === 'string') return value.trim().toLowerCase() === 'true'
  return false
}
const toDateKey = value => {
  if (!value) return ''
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '' : date.toISOString().slice(0, 10)
}
const splitDegradeReasons = value => String(value ?? '').split(',').map(item => item.trim()).filter(Boolean)
const localizeDegradeCode = code => {
  const sectorMatch = code.match(/^sector_rankings_(concept|industry|style)_unavailable$/)
  if (sectorMatch) return `${boardLabelMap[sectorMatch[1]] ?? '板块'}板块排行暂未同步完成`
  if (code === 'market_breadth_unavailable') return '市场涨跌与涨跌停数据暂未同步完成'
  if (code === 'market_turnover_unavailable') return '市场成交额暂未同步完成'
  if (code === 'limit_up_unavailable') return '涨停统计暂未同步完成'
  if (code === 'limit_down_unavailable') return '跌停统计暂未同步完成'
  if (code === 'broken_board_unavailable') return '炸板统计暂未同步完成'
  if (code === 'max_streak_unavailable') return '连板高度暂未同步完成'
  if (code === 'sector_rankings_unavailable') return '板块排行暂未同步完成'
  if (code === 'sync_incomplete') return '本次同步只完成了部分市场数据'
  return ''
}
const localizeDegradeReason = value => {
  const items = splitDegradeReasons(value).map(localizeDegradeCode).filter(Boolean)
  return [...new Set(items)].join('、')
}
const localizeDegradeReasonOrFallback = value => localizeDegradeReason(value) || DEGRADE_FALLBACK_TEXT
const hasSectorRankingGap = value => splitDegradeReasons(value)
  .some(code => code === 'sector_rankings_unavailable' || /^sector_rankings_(concept|industry|style)_unavailable$/.test(code))
const hasDegradeCode = (value, code) => splitDegradeReasons(value).includes(code)

// ── Computed ──
const totalPages = computed(() => Math.max(1, Math.ceil((total.value || 0) / pageSize.value)))
const compareWindowLabel = computed(() => compareWindowOptions.find(o => o.value === compareWindow.value)?.label ?? '10日主线')
const isSummaryDegraded = computed(() => Boolean(summary.value?.isDegraded) || (summary.value?.stageLabelV2 || '') === '同步不完整')
const isMarketDegraded = computed(() => isSummaryDegraded.value || sectorPageStatus.value.isDegraded)
const activeDegradeReasonText = computed(() => localizeDegradeReason(summary.value?.degradeReason || sectorPageStatus.value.degradeReason))
const isBrokenBoardUnavailable = computed(() => isSummaryDegraded.value && hasDegradeCode(summary.value?.degradeReason, 'broken_board_unavailable'))
const isLimitUpUnavailable = computed(() => isSummaryDegraded.value && hasDegradeCode(summary.value?.degradeReason, 'limit_up_unavailable'))
const isMaxStreakUnavailable = computed(() => isSummaryDegraded.value && hasDegradeCode(summary.value?.degradeReason, 'max_streak_unavailable'))
const isTopSectorUnavailable = computed(() => hasSectorRankingGap(summary.value?.degradeReason) || hasSectorRankingGap(sectorPageStatus.value.degradeReason))
const isTurnoverUnavailable = computed(() => isSummaryDegraded.value && hasDegradeCode(summary.value?.degradeReason, 'market_turnover_unavailable'))
const isBreadthUnavailable = computed(() => isSummaryDegraded.value && hasDegradeCode(summary.value?.degradeReason, 'market_breadth_unavailable'))

// ── Centralized unavailability (passed to children as explicit props) ──
const isDegradedContext = computed(() => isSummaryDegraded.value || sectorPageStatus.value.isDegraded)
const _hasUsableTs = value => Boolean(String(value ?? '').trim())
const _showAsUnavailable = (value, code, hasTimestamp) => {
  if (value === null || value === undefined || value === '') return true
  if (!isDegradedContext.value) return false
  if (code) {
    const reasons = summary.value?.degradeReason || sectorPageStatus.value.degradeReason
    if (hasDegradeCode(reasons, code)) return true
  }
  return Number(value ?? 0) === 0 && !hasTimestamp
}
const mainFlowUnavailable = computed(() => {
  const flow = realtimeOverview.value?.mainCapitalFlow
  if (!flow) return true
  return _showAsUnavailable(flow.mainNetInflow, null, _hasUsableTs(flow.snapshotTime))
})
const northboundUnavailable = computed(() => {
  const flow = realtimeOverview.value?.northboundFlow
  if (!flow) return true
  return _showAsUnavailable(flow.totalNetInflow, null, _hasUsableTs(flow.snapshotTime))
})
const diffusionUnavailable = computed(() => _showAsUnavailable(displaySummary.value?.diffusionScore, null, false))
const continuationUnavailable = computed(() => _showAsUnavailable(displaySummary.value?.continuationScore, null, false))
const turnoverShareUnavailable = computed(() =>
  isTurnoverUnavailable.value || _showAsUnavailable(displaySummary.value?.top3SectorTurnoverShare, null, false)
)

// Display summary with realtime breadth fallback
const summaryNeedsBreadthFallback = computed(() => {
  const breadth = realtimeOverview.value?.breadth
  if (!summary.value || !breadth) return false
  const summaryBreadthMissing = [summary.value.advancers, summary.value.decliners, summary.value.flatCount].every(v => Number(v ?? 0) === 0)
  const summaryLimitCountsMissing = Number(summary.value.limitUpCount ?? 0) === 0
  const realtimeHasBreadth = [breadth.limitUpCount, breadth.limitDownCount, breadth.advancers, breadth.decliners, breadth.flatCount].some(v => Number(v ?? 0) > 0)
  return realtimeHasBreadth && (summaryBreadthMissing || summaryLimitCountsMissing)
})

const displaySummary = computed(() => {
  if (!summary.value) return null
  if (!summaryNeedsBreadthFallback.value) return summary.value
  const breadth = realtimeOverview.value?.breadth
  if (!breadth) return summary.value
  return {
    ...summary.value,
    limitUpCount: Number(breadth.limitUpCount ?? summary.value.limitUpCount ?? 0),
    limitDownCount: Number(breadth.limitDownCount ?? summary.value.limitDownCount ?? 0),
    advancers: Number(breadth.advancers ?? summary.value.advancers ?? 0),
    decliners: Number(breadth.decliners ?? summary.value.decliners ?? 0),
    flatCount: Number(breadth.flatCount ?? summary.value.flatCount ?? 0)
  }
})

const dataStaleHours = computed(() => {
  const t = summary.value?.snapshotTime
  if (!t) return -1
  const ms = Date.now() - new Date(t).getTime()
  return Number.isFinite(ms) ? ms / 3600000 : -1
})

// Sector detail computed
const showDegradedSectorEmptyState = computed(() => !sectors.value.length && sectorPageStatus.value.isDegraded && hasSectorRankingGap(sectorPageStatus.value.degradeReason))
const sectorEmptyTitle = computed(() => showDegradedSectorEmptyState.value ? '板块排行暂未同步完成。' : '当前暂无板块榜单。')
const sectorEmptyBody = computed(() => {
  if (showDegradedSectorEmptyState.value) {
    const ts = snapshotTime.value ? `（快照时间：${formatDate(snapshotTime.value)}）` : ''
    return `下方展示最近有效数据，请注意快照时间。${ts}`
  }
  return '请稍后重试或切换轮动维度。'
})
const toolbarBoardCountText = computed(() => {
  if (!initialDashboardResolved.value) return '榜单加载中'
  if (isMarketDegraded.value && !sectors.value.length) return '榜单暂无数据'
  return `共 ${total.value} 个板块`
})

const pageOffset = computed(() => (page.value - 1) * pageSize.value)

const historyDisplayItems = computed(() => history.value.map(item => ({
  tradingDate: item.tradingDate,
  date: formatDate(item.tradingDate).slice(5, 10),
  label: isHistoryChipDegraded(item) ? '同步不完整' : item.stageLabel,
  detail: isHistoryChipDegraded(item)
    ? (localizeDegradeReason(summary.value?.degradeReason) || '仅同步到摘要结果')
    : `${item.stageScore.toFixed(1)} 分`,
  isDegraded: isHistoryChipDegraded(item)
})))

// ── Normalizers ──
const normalizeSummary = payload => payload ? ({
  snapshotTime: payload.snapshotTime ?? payload.SnapshotTime ?? '',
  sessionPhase: payload.sessionPhase ?? payload.SessionPhase ?? '',
  stageLabel: payload.stageLabel ?? payload.StageLabel ?? '混沌',
  stageScore: Number(payload.stageScore ?? payload.StageScore ?? 0),
  maxLimitUpStreak: Number(payload.maxLimitUpStreak ?? payload.MaxLimitUpStreak ?? 0),
  limitUpCount: normalizeOptionalNumber(payload.limitUpCount ?? payload.LimitUpCount),
  limitDownCount: normalizeOptionalNumber(payload.limitDownCount ?? payload.LimitDownCount),
  brokenBoardCount: Number(payload.brokenBoardCount ?? payload.BrokenBoardCount ?? 0),
  brokenBoardRate: Number(payload.brokenBoardRate ?? payload.BrokenBoardRate ?? 0),
  advancers: normalizeOptionalNumber(payload.advancers ?? payload.Advancers),
  decliners: normalizeOptionalNumber(payload.decliners ?? payload.Decliners),
  flatCount: normalizeOptionalNumber(payload.flatCount ?? payload.FlatCount),
  totalTurnover: Number(payload.totalTurnover ?? payload.TotalTurnover ?? 0),
  top3SectorTurnoverShare: normalizeOptionalNumber(payload.top3SectorTurnoverShare ?? payload.Top3SectorTurnoverShare),
  top10SectorTurnoverShare: normalizeOptionalNumber(payload.top10SectorTurnoverShare ?? payload.Top10SectorTurnoverShare),
  diffusionScore: normalizeOptionalNumber(payload.diffusionScore ?? payload.DiffusionScore),
  continuationScore: normalizeOptionalNumber(payload.continuationScore ?? payload.ContinuationScore),
  stageLabelV2: payload.stageLabelV2 ?? payload.StageLabelV2 ?? '',
  stageConfidence: Number(payload.stageConfidence ?? payload.StageConfidence ?? 0),
  top3SectorTurnoverShare5dAvg: normalizeOptionalNumber(payload.top3SectorTurnoverShare5dAvg ?? payload.Top3SectorTurnoverShare5dAvg),
  top10SectorTurnoverShare5dAvg: normalizeOptionalNumber(payload.top10SectorTurnoverShare5dAvg ?? payload.Top10SectorTurnoverShare5dAvg),
  limitUpCount5dAvg: Number(payload.limitUpCount5dAvg ?? payload.LimitUpCount5dAvg ?? 0),
  brokenBoardRate5dAvg: Number(payload.brokenBoardRate5dAvg ?? payload.BrokenBoardRate5dAvg ?? 0),
  isDegraded: toBoolean(payload.isDegraded ?? payload.IsDegraded ?? false),
  degradeReason: payload.degradeReason ?? payload.DegradeReason ?? ''
}) : null

const normalizeHistoryItem = item => ({
  tradingDate: item.tradingDate ?? item.TradingDate ?? '',
  snapshotTime: item.snapshotTime ?? item.SnapshotTime ?? '',
  stageLabel: item.stageLabel ?? item.StageLabel ?? '混沌',
  stageScore: Number(item.stageScore ?? item.StageScore ?? 0),
  limitUpCount: Number(item.limitUpCount ?? item.LimitUpCount ?? 0),
  limitDownCount: Number(item.limitDownCount ?? item.LimitDownCount ?? 0),
  brokenBoardCount: Number(item.brokenBoardCount ?? item.BrokenBoardCount ?? 0)
})

const normalizeSectorItem = item => ({
  boardType: item.boardType ?? item.BoardType ?? '',
  sectorCode: item.sectorCode ?? item.SectorCode ?? '',
  sectorName: item.sectorName ?? item.SectorName ?? '',
  changePercent: Number(item.changePercent ?? item.ChangePercent ?? 0),
  mainNetInflow: Number(item.mainNetInflow ?? item.MainNetInflow ?? 0),
  breadthScore: normalizeOptionalNumber(item.breadthScore ?? item.BreadthScore),
  continuityScore: Number(item.continuityScore ?? item.ContinuityScore ?? 0),
  strengthScore: Number(item.strengthScore ?? item.StrengthScore ?? 0),
  newsSentiment: item.newsSentiment ?? item.NewsSentiment ?? '中性',
  newsHotCount: Number(item.newsHotCount ?? item.NewsHotCount ?? 0),
  leaderSymbol: item.leaderSymbol ?? item.LeaderSymbol ?? '',
  leaderName: item.leaderName ?? item.LeaderName ?? '',
  leaderChangePercent: normalizeOptionalNumber(item.leaderChangePercent ?? item.LeaderChangePercent),
  rankNo: Number(item.rankNo ?? item.RankNo ?? 0),
  snapshotTime: item.snapshotTime ?? item.SnapshotTime ?? '',
  rankChange5d: Number(item.rankChange5d ?? item.RankChange5d ?? 0),
  rankChange10d: Number(item.rankChange10d ?? item.RankChange10d ?? 0),
  rankChange20d: Number(item.rankChange20d ?? item.RankChange20d ?? 0),
  strengthAvg5d: Number(item.strengthAvg5d ?? item.StrengthAvg5d ?? 0),
  strengthAvg10d: Number(item.strengthAvg10d ?? item.StrengthAvg10d ?? 0),
  strengthAvg20d: Number(item.strengthAvg20d ?? item.StrengthAvg20d ?? 0),
  diffusionRate: normalizeOptionalNumber(item.diffusionRate ?? item.DiffusionRate),
  advancerCount: normalizeOptionalNumber(item.advancerCount ?? item.AdvancerCount),
  declinerCount: normalizeOptionalNumber(item.declinerCount ?? item.DeclinerCount),
  flatMemberCount: normalizeOptionalNumber(item.flatMemberCount ?? item.FlatMemberCount),
  limitUpMemberCount: normalizeOptionalNumber(item.limitUpMemberCount ?? item.LimitUpMemberCount),
  leaderStabilityScore: Number(item.leaderStabilityScore ?? item.LeaderStabilityScore ?? 0),
  mainlineScore: Number(item.mainlineScore ?? item.MainlineScore ?? 0),
  isMainline: Boolean(item.isMainline ?? item.IsMainline ?? false)
})

const normalizeRealtimeSectorItem = item => ({
  boardType: item.boardType ?? item.BoardType ?? '',
  sectorCode: item.sectorCode ?? item.SectorCode ?? '',
  sectorName: item.sectorName ?? item.SectorName ?? '',
  changePercent: Number(item.changePercent ?? item.ChangePercent ?? 0),
  mainNetInflow: Number(item.mainNetInflow ?? item.MainNetInflow ?? 0),
  superLargeNetInflow: Number(item.superLargeNetInflow ?? item.SuperLargeNetInflow ?? 0),
  largeNetInflow: Number(item.largeNetInflow ?? item.LargeNetInflow ?? 0),
  mediumNetInflow: Number(item.mediumNetInflow ?? item.MediumNetInflow ?? 0),
  smallNetInflow: Number(item.smallNetInflow ?? item.SmallNetInflow ?? 0),
  turnoverAmount: Number(item.turnoverAmount ?? item.TurnoverAmount ?? 0),
  turnoverShare: normalizeOptionalNumber(item.turnoverShare ?? item.TurnoverShare),
  rankNo: Number(item.rankNo ?? item.RankNo ?? 0),
  snapshotTime: item.snapshotTime ?? item.SnapshotTime ?? ''
})

const normalizeRealtimeSectorBoard = payload => payload ? ({
  boardType: payload.boardType ?? payload.BoardType ?? '',
  take: Number(payload.take ?? payload.Take ?? 0),
  sort: payload.sort ?? payload.Sort ?? 'rank',
  snapshotTime: payload.snapshotTime ?? payload.SnapshotTime ?? '',
  items: Array.isArray(payload.items ?? payload.Items) ? (payload.items ?? payload.Items).map(normalizeRealtimeSectorItem) : []
}) : null

const normalizeDetail = payload => payload ? ({
  snapshot: normalizeSectorItem(payload.snapshot ?? payload.Snapshot ?? {}),
  history: Array.isArray(payload.history ?? payload.History) ? (payload.history ?? payload.History).map(item => ({
    tradingDate: item.tradingDate ?? item.TradingDate ?? '',
    changePercent: Number(item.changePercent ?? item.ChangePercent ?? 0),
    breadthScore: normalizeOptionalNumber(item.breadthScore ?? item.BreadthScore),
    continuityScore: Number(item.continuityScore ?? item.ContinuityScore ?? 0),
    strengthScore: Number(item.strengthScore ?? item.StrengthScore ?? 0),
    rankNo: Number(item.rankNo ?? item.RankNo ?? 0),
    diffusionRate: normalizeOptionalNumber(item.diffusionRate ?? item.DiffusionRate),
    advancerCount: normalizeOptionalNumber(item.advancerCount ?? item.AdvancerCount),
    declinerCount: normalizeOptionalNumber(item.declinerCount ?? item.DeclinerCount),
    flatMemberCount: normalizeOptionalNumber(item.flatMemberCount ?? item.FlatMemberCount),
    limitUpMemberCount: normalizeOptionalNumber(item.limitUpMemberCount ?? item.LimitUpMemberCount),
    rankChange5d: Number(item.rankChange5d ?? item.RankChange5d ?? 0),
    rankChange10d: Number(item.rankChange10d ?? item.RankChange10d ?? 0),
    rankChange20d: Number(item.rankChange20d ?? item.RankChange20d ?? 0),
    strengthAvg5d: Number(item.strengthAvg5d ?? item.StrengthAvg5d ?? 0),
    strengthAvg10d: Number(item.strengthAvg10d ?? item.StrengthAvg10d ?? 0),
    strengthAvg20d: Number(item.strengthAvg20d ?? item.StrengthAvg20d ?? 0),
    leaderStabilityScore: Number(item.leaderStabilityScore ?? item.LeaderStabilityScore ?? 0),
    mainlineScore: Number(item.mainlineScore ?? item.MainlineScore ?? 0),
    isMainline: Boolean(item.isMainline ?? item.IsMainline ?? false)
  })) : [],
  leaders: Array.isArray(payload.leaders ?? payload.Leaders) ? (payload.leaders ?? payload.Leaders).map(item => ({
    rankInSector: Number(item.rankInSector ?? item.RankInSector ?? 0),
    symbol: item.symbol ?? item.Symbol ?? '',
    name: item.name ?? item.Name ?? '',
    changePercent: Number(item.changePercent ?? item.ChangePercent ?? 0),
    turnoverAmount: Number(item.turnoverAmount ?? item.TurnoverAmount ?? 0),
    isLimitUp: Boolean(item.isLimitUp ?? item.IsLimitUp ?? false)
  })) : [],
  news: Array.isArray(payload.news ?? payload.News) ? (payload.news ?? payload.News).map(item => ({
    title: item.title ?? item.Title ?? '',
    translatedTitle: item.translatedTitle ?? item.TranslatedTitle ?? '',
    source: item.source ?? item.Source ?? '',
    sentiment: item.sentiment ?? item.Sentiment ?? '中性',
    publishTime: item.publishTime ?? item.PublishTime ?? '',
    url: item.url ?? item.Url ?? ''
  })) : []
}) : null

const normalizeRealtimeQuote = item => ({
  symbol: item.symbol ?? item.Symbol ?? '',
  name: item.name ?? item.Name ?? '',
  price: normalizeOptionalNumber(item.price ?? item.Price),
  change: normalizeOptionalNumber(item.change ?? item.Change),
  changePercent: normalizeOptionalNumber(item.changePercent ?? item.ChangePercent),
  turnoverAmount: normalizeOptionalNumber(item.turnoverAmount ?? item.TurnoverAmount),
  timestamp: item.timestamp ?? item.Timestamp ?? ''
})

const normalizeMainFlow = payload => payload ? ({
  snapshotTime: payload.snapshotTime ?? payload.SnapshotTime ?? '',
  amountUnit: payload.amountUnit ?? payload.AmountUnit ?? '亿元',
  mainNetInflow: normalizeOptionalNumber(payload.mainNetInflow ?? payload.MainNetInflow),
  smallOrderNetInflow: normalizeOptionalNumber(payload.smallOrderNetInflow ?? payload.SmallOrderNetInflow),
  mediumOrderNetInflow: normalizeOptionalNumber(payload.mediumOrderNetInflow ?? payload.MediumOrderNetInflow),
  largeOrderNetInflow: normalizeOptionalNumber(payload.largeOrderNetInflow ?? payload.LargeOrderNetInflow),
  superLargeOrderNetInflow: normalizeOptionalNumber(payload.superLargeOrderNetInflow ?? payload.SuperLargeOrderNetInflow)
}) : null

const normalizeNorthbound = payload => payload ? ({
  snapshotTime: payload.snapshotTime ?? payload.SnapshotTime ?? '',
  amountUnit: payload.amountUnit ?? payload.AmountUnit ?? '亿元',
  totalNetInflow: normalizeOptionalNumber(payload.totalNetInflow ?? payload.TotalNetInflow),
  shanghaiNetInflow: normalizeOptionalNumber(payload.shanghaiNetInflow ?? payload.ShanghaiNetInflow),
  shenzhenNetInflow: normalizeOptionalNumber(payload.shenzhenNetInflow ?? payload.ShenzhenNetInflow),
  shanghaiBalance: normalizeOptionalNumber(payload.shanghaiBalance ?? payload.ShanghaiBalance),
  shenzhenBalance: normalizeOptionalNumber(payload.shenzhenBalance ?? payload.ShenzhenBalance)
}) : null

const normalizeBreadth = payload => payload ? ({
  tradingDate: payload.tradingDate ?? payload.TradingDate ?? '',
  advancers: normalizeOptionalNumber(payload.advancers ?? payload.Advancers),
  decliners: normalizeOptionalNumber(payload.decliners ?? payload.Decliners),
  flatCount: normalizeOptionalNumber(payload.flatCount ?? payload.FlatCount),
  limitUpCount: normalizeOptionalNumber(payload.limitUpCount ?? payload.LimitUpCount),
  limitDownCount: normalizeOptionalNumber(payload.limitDownCount ?? payload.LimitDownCount),
  buckets: Array.isArray(payload.buckets ?? payload.Buckets)
    ? (payload.buckets ?? payload.Buckets).map(item => ({
        changeBucket: normalizeOptionalNumber(item.changeBucket ?? item.ChangeBucket),
        label: item.label ?? item.Label ?? '',
        count: normalizeOptionalNumber(item.count ?? item.Count)
      }))
    : []
}) : null

const normalizeRealtimeOverview = payload => payload ? ({
  snapshotTime: payload.snapshotTime ?? payload.SnapshotTime ?? '',
  indices: Array.isArray(payload.indices ?? payload.Indices) ? (payload.indices ?? payload.Indices).map(normalizeRealtimeQuote) : [],
  mainCapitalFlow: normalizeMainFlow(payload.mainCapitalFlow ?? payload.MainCapitalFlow ?? null),
  northboundFlow: normalizeNorthbound(payload.northboundFlow ?? payload.NorthboundFlow ?? null),
  breadth: normalizeBreadth(payload.breadth ?? payload.Breadth ?? null)
}) : null

// ── Formatters ──
const cnDateTimeFormatter = new Intl.DateTimeFormat('zh-CN', {
  timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit',
  hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false
})

const formatDate = value => {
  if (!value) return '--'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '--'
  return cnDateTimeFormatter.format(date)
}

// ── Sector helpers ──
const isHistoryChipDegraded = item => {
  if (!item) return false
  if (item.stageScore === 0 && item.stageLabel === '混沌') return true
  if (!isSummaryDegraded.value || !summary.value?.snapshotTime) return false
  return toDateKey(item.tradingDate) === toDateKey(summary.value.snapshotTime)
}

const getRealtimeSectorSort = value => {
  if (value === 'change') return 'change'
  if (value === 'flow') return 'flow'
  return 'rank'
}

const applyRealtimeSectorBoard = (items, realtimePayload, sortValue) => {
  if (!Array.isArray(items) || !items.length || !realtimeSectorBoardEnabled.value) return Array.isArray(items) ? [...items] : []
  const realtimeItems = Array.isArray(realtimePayload?.items) ? realtimePayload.items : []
  if (!realtimeItems.length) return [...items]
  const realtimeMap = new Map(realtimeItems.map(item => [item.sectorCode, item]))
  const mergedItems = items.map(item => {
    const ri = realtimeMap.get(item.sectorCode)
    return ri ? { ...item, changePercent: ri.changePercent, mainNetInflow: ri.mainNetInflow, rankNo: ri.rankNo, snapshotTime: ri.snapshotTime || item.snapshotTime, realtimeTurnoverAmount: ri.turnoverAmount, realtimeTurnoverShare: ri.turnoverShare } : item
  })
  if (sortValue === 'change') return [...mergedItems].sort((a, b) => b.changePercent !== a.changePercent ? b.changePercent - a.changePercent : a.rankNo - b.rankNo)
  if (sortValue === 'flow') return [...mergedItems].sort((a, b) => b.mainNetInflow !== a.mainNetInflow ? b.mainNetInflow - a.mainNetInflow : b.changePercent - a.changePercent)
  return mergedItems
}

// ── API ──
const fetchJson = async url => {
  const response = await fetch(url)
  if (!response.ok) {
    const payload = await response.json().catch(() => null)
    throw new Error(payload?.message || '情绪轮动数据加载失败')
  }
  const text = await response.text()
  if (!text || !text.trim()) return null
  return JSON.parse(text)
}

const fetchDetail = async sectorCode => {
  if (!sectorCode) { detail.value = null; selectedSectorCode.value = ''; return }
  selectedSectorCode.value = sectorCode
  detailLoading.value = true
  detailError.value = ''
  try {
    const payload = await fetchJson(`/api/market/sectors/${encodeURIComponent(sectorCode)}?boardType=${encodeURIComponent(boardType.value)}&window=${encodeURIComponent(compareWindow.value)}`)
    detail.value = normalizeDetail(payload)
  } catch (err) {
    detail.value = null
    detailError.value = err.message || '板块详情加载失败'
  } finally { detailLoading.value = false }
}

const fetchRealtimeSectorBoard = async ({ silent = false } = {}) => {
  if (!realtimeSectorBoardEnabled.value) {
    realtimeSectorBoard.value = null; realtimeSectorBoardError.value = ''; sectors.value = [...sectorBaseItems.value]; return null
  }
  if (!silent) realtimeSectorBoardLoading.value = true
  realtimeSectorBoardError.value = ''
  try {
    const realtimeSort = getRealtimeSectorSort(sort.value)
    const payload = await fetchJson(`/api/market/sectors/realtime?boardType=${encodeURIComponent(boardType.value)}&take=${Math.max(pageSize.value * 6, 60)}&sort=${encodeURIComponent(realtimeSort)}`)
    realtimeSectorBoard.value = normalizeRealtimeSectorBoard(payload)
    sectors.value = applyRealtimeSectorBoard(sectorBaseItems.value, realtimeSectorBoard.value, sort.value)
    return realtimeSectorBoard.value
  } catch (err) {
    realtimeSectorBoard.value = null; realtimeSectorBoardError.value = err.message || '实时板块榜加载失败'; sectors.value = [...sectorBaseItems.value]; return null
  } finally { realtimeSectorBoardLoading.value = false }
}

const fetchDashboard = async ({ resetPage = false, preserveSyncFeedback = false } = {}) => {
  if (resetPage) page.value = 1
  if (!preserveSyncFeedback) syncFeedback.value = null
  loading.value = true
  error.value = ''
  try {
    const [summaryPayload, historyPayload, sectorPayload] = await Promise.all([
      fetchJson('/api/market/sentiment/latest'),
      fetchJson('/api/market/sentiment/history?days=10'),
      fetchJson(`/api/market/sectors?boardType=${encodeURIComponent(boardType.value)}&page=${page.value}&pageSize=${pageSize.value}&sort=${encodeURIComponent(sort.value)}`)
    ])
    summary.value = normalizeSummary(summaryPayload)
    history.value = Array.isArray(historyPayload) ? historyPayload.map(normalizeHistoryItem) : []
    total.value = Number(sectorPayload?.total ?? sectorPayload?.Total ?? 0)
    snapshotTime.value = sectorPayload?.snapshotTime ?? sectorPayload?.SnapshotTime ?? ''
    sectorPageStatus.value = {
      isDegraded: toBoolean(sectorPayload?.isDegraded ?? sectorPayload?.IsDegraded ?? false),
      degradeReason: sectorPayload?.degradeReason ?? sectorPayload?.DegradeReason ?? ''
    }
    sectorBaseItems.value = Array.isArray(sectorPayload?.items ?? sectorPayload?.Items) ? (sectorPayload.items ?? sectorPayload.Items).map(normalizeSectorItem) : []
    sectors.value = [...sectorBaseItems.value]
    await fetchRealtimeSectorBoard({ silent: true })
    const nextSelected = sectors.value.some(i => i.sectorCode === selectedSectorCode.value) ? selectedSectorCode.value : sectors.value[0]?.sectorCode || ''
    await fetchDetail(nextSelected)
  } catch (err) {
    summary.value = null; history.value = []; sectors.value = []; total.value = 0
    sectorPageStatus.value = { isDegraded: false, degradeReason: '' }
    detail.value = null; selectedSectorCode.value = ''
    error.value = err.message || '情绪轮动数据加载失败'
  } finally {
    initialDashboardResolved.value = true
    loading.value = false
  }
}

const fetchAudit = async () => {
  try {
    const raw = await fetchJson('/api/market/audit')
    if (!raw) return
    auditData.value = {
      dataSources: (raw.sources ?? []).map(s => ({
        name: s.name,
        status: s.status,
        lastSuccessTime: s.lastSuccess,
        consecutiveFailures: s.consecutiveFailures,
        latency: s.avgLatencyMs
      })),
        latestSync: (() => {
          const s = (raw.recentSyncs ?? [])[0]
          if (!s) return null
          return {
            sourceHealthy: s.sourceHealthy ?? null,
            businessComplete: s.businessComplete ?? s.wasComplete ?? null,
            degradedSources: s.degradedSources ?? [],
            sectorRowCount: s.sectorRowCount ?? null,
            timestamp: s.timestamp ?? null
          }
        })(),
      formula: [
        `阶段评分 = ${raw.algorithm?.stageScore ?? ''}`,
        `扩散评分 = ${raw.algorithm?.diffusionScore ?? ''}`,
        `延续评分 = ${raw.algorithm?.continuationScore ?? ''}`,
        raw.algorithm?.stageLabel ?? ''
      ].filter(Boolean).join('\n'),
      computedAt: raw.lastComputation?.timestamp ?? '',
      computeDurationMs: raw.lastComputation?.durationMs ?? 0
    }
  } catch { /* audit is non-critical */ }
}

const fetchRealtimeOverview = async () => {
  if (!realtimeOverviewEnabled.value) { realtimeOverview.value = null; realtimeError.value = ''; realtimeLoading.value = false; return }
  realtimeLoading.value = true
  realtimeError.value = ''
  try {
    const payload = await fetchJson('/api/market/realtime/overview')
    realtimeOverview.value = normalizeRealtimeOverview(payload)
  } catch (err) {
    realtimeOverview.value = null
    realtimeError.value = err.message || '实时总览加载失败'
  } finally { realtimeLoading.value = false }
}

// ── Sync ──
const forceSync = async () => {
  syncing.value = true
  syncFeedback.value = null
  try {
    const res = await fetch('/api/market/sync', { method: 'POST' })
    if (res.status === 429) {
      const body = await res.json().catch(() => null)
      const wait = body?.retryAfter || 30
      syncCooldown.value = wait
      if (syncCooldownTimer) clearInterval(syncCooldownTimer)
      syncCooldownTimer = setInterval(() => {
        syncCooldown.value--
        if (syncCooldown.value <= 0) { clearInterval(syncCooldownTimer); syncCooldownTimer = null; syncCooldown.value = 0 }
      }, 1000)
      syncFeedback.value = { type: 'error', message: body?.message || '同步正在进行中，请稍后重试' }
      return
    }
    if (!res.ok) {
      const body = await res.json().catch(() => null)
      throw new Error(body?.message || `同步失败 (${res.status})`)
    }
    const syncResult = await res.json()
    const localizedSyncReason = localizeDegradeReasonOrFallback(syncResult.degradationReason)
    await fetchDashboard({ preserveSyncFeedback: true })
    await fetchRealtimeOverview()
    if (!syncResult.synced) {
      syncFeedback.value = { type: 'error', message: syncResult.degradationReason ? `同步失败：${localizedSyncReason}` : '同步失败：所有数据源不可用' }
    } else if (syncResult.degradationReason) {
      syncFeedback.value = { type: 'partial', message: `同步完成（部分降级）：${localizedSyncReason}` }
    } else {
      syncFeedback.value = isMarketDegraded.value
        ? { type: 'partial', message: `本次同步已完成，但仍有部分数据缺失：${activeDegradeReasonText.value || '关键广度或板块榜单仍未同步完成'}。` }
        : { type: 'success', message: '最新市场摘要与板块榜单已同步完成。' }
    }
  } catch (err) {
    syncFeedback.value = { type: 'error', message: err.message || '强制同步失败' }
  } finally { syncing.value = false }
}

// ── Auto-refresh ──
function isMarketHours() {
  const now = new Date()
  const day = now.getDay()
  if (day === 0 || day === 6) return false
  const mins = now.getHours() * 60 + now.getMinutes()
  return mins >= 555 && mins <= 930 // 9:15 – 15:30
}

function startAutoRefresh() {
  stopAutoRefresh()
  refreshTimer = setInterval(() => {
    if (isMarketHours()) {
      fetchDashboard({ resetPage: false, preserveSyncFeedback: true })
      fetchRealtimeOverview()
    }
  }, REFRESH_INTERVAL)
}

function stopAutoRefresh() {
  if (refreshTimer) { clearInterval(refreshTimer); refreshTimer = null }
}

// ── Event handlers ──
const handleBoardChange = () => fetchDashboard({ resetPage: true })
const handleSortChange = () => fetchDashboard({ resetPage: true })
const handleWindowChange = () => fetchDetail(selectedSectorCode.value || sectors.value[0]?.sectorCode || '')
const goPrev = () => { if (page.value > 1) { page.value -= 1; fetchDashboard() } }
const goNext = () => { if (page.value < totalPages.value) { page.value += 1; fetchDashboard() } }
const navigateToStockInfo = leader => {
  const symbol = String(leader?.symbol ?? '').trim()
  if (!symbol) return
  window.dispatchEvent(new CustomEvent('navigate-stock', {
    detail: {
      symbol,
      name: leader?.name ?? ''
    }
  }))
}

// ── Watchers ──
watch(boardType, () => { detail.value = null })
watch(compareWindow, () => { handleWindowChange() })
watch(realtimeOverviewEnabled, value => {
  localStorage.setItem('market_realtime_overview_enabled', String(value))
  if (value) { fetchRealtimeOverview(); return }
  realtimeOverview.value = null; realtimeError.value = ''
})
watch(realtimeSectorBoardEnabled, value => {
  localStorage.setItem('market_realtime_sector_board_enabled', String(value))
  if (value) { fetchRealtimeSectorBoard(); return }
  realtimeSectorBoard.value = null; realtimeSectorBoardError.value = ''; sectors.value = [...sectorBaseItems.value]
})

// ── Lifecycle ──
onMounted(() => {
  fetchDashboard()
  fetchRealtimeOverview()
  fetchAudit()
  startAutoRefresh()
})
onUnmounted(() => {
  stopAutoRefresh()
  if (syncCooldownTimer) { clearInterval(syncCooldownTimer); syncCooldownTimer = null }
})
</script>

<template>
  <section class="market-shell">
    <!-- 1. 温度条 -->
    <MarketTemperatureBar
      :stage-label="displaySummary?.stageLabelV2 || displaySummary?.stageLabel || '加载中'"
      :stage-score="displaySummary?.stageScore ?? 0"
      :confidence="displaySummary?.stageConfidence ?? 0"
      :session-phase="displaySummary?.sessionPhase ?? ''"
      :snapshot-time="displaySummary?.snapshotTime ?? ''"
      :is-degraded="isSummaryDegraded"
      :degrade-reason="activeDegradeReasonText"
      :syncing="syncing"
      :sync-cooldown="syncCooldown"
      @sync="forceSync"
    />

    <!-- 反馈条 -->
    <div v-if="syncFeedback" class="feedback" :class="[`feedback-${syncFeedback.type}`]">{{ syncFeedback.message }}</div>
    <div v-if="isSummaryDegraded && activeDegradeReasonText" class="feedback feedback-partial">{{ activeDegradeReasonText }}</div>
    <div v-if="realtimeError" class="feedback error compact">{{ realtimeError }}</div>
    <div v-if="realtimeSectorBoardError" class="feedback error compact">{{ realtimeSectorBoardError }}</div>
    <div v-if="dataStaleHours > 24" class="feedback stale-warning">数据快照距今已超过 {{ Math.floor(dataStaleHours) }} 小时，点击同步按钮获取最新行情。</div>

    <!-- 2. 指数+涨停跌停行 -->
    <IndexMetricStrip
      :indices="realtimeOverview?.indices ?? []"
      :limit-up-count="displaySummary?.limitUpCount ?? null"
      :limit-down-count="displaySummary?.limitDownCount ?? null"
      :broken-board-count="displaySummary?.brokenBoardCount ?? 0"
      :broken-board-rate="displaySummary?.brokenBoardRate ?? 0"
      :max-limit-up-streak="displaySummary?.maxLimitUpStreak ?? 0"
      :limit-up-count5d-avg="displaySummary?.limitUpCount5dAvg ?? 0"
      :broken-board-rate5d-avg="displaySummary?.brokenBoardRate5dAvg ?? 0"
      :limit-timestamp="displaySummary?.snapshotTime ?? ''"
      :broken-board-unavailable="isBrokenBoardUnavailable"
      :limit-up-unavailable="isLimitUpUnavailable"
      :max-streak-unavailable="isMaxStreakUnavailable"
    />

    <!-- 3. 涨跌分布 + 资金广度 并排 -->
    <div class="overview-row">
      <BreadthBucketChart
        :buckets="realtimeOverview?.breadth?.buckets ?? []"
        :advancers="displaySummary?.advancers ?? null"
        :decliners="displaySummary?.decliners ?? null"
        :flat-count="displaySummary?.flatCount ?? null"
        :timestamp="realtimeOverview?.breadth?.tradingDate ?? displaySummary?.snapshotTime ?? ''"
        :unavailable="isBreadthUnavailable"
      />
      <CapitalBreadthPanel
        :main-capital-flow="realtimeOverview?.mainCapitalFlow"
        :northbound-flow="realtimeOverview?.northboundFlow"
        :diffusion-score="displaySummary?.diffusionScore ?? null"
        :continuation-score="displaySummary?.continuationScore ?? null"
        :top3-sector-turnover-share="displaySummary?.top3SectorTurnoverShare ?? null"
        :top10-sector-turnover-share="displaySummary?.top10SectorTurnoverShare ?? null"
        :top3-sector-turnover-share5d-avg="displaySummary?.top3SectorTurnoverShare5dAvg ?? null"
        :top10-sector-turnover-share5d-avg="displaySummary?.top10SectorTurnoverShare5dAvg ?? null"
        :total-turnover="displaySummary?.totalTurnover ?? 0"
        :advancers="displaySummary?.advancers ?? null"
        :decliners="displaySummary?.decliners ?? null"
        :main-flow-unavailable="mainFlowUnavailable"
        :northbound-unavailable="northboundUnavailable"
        :diffusion-unavailable="diffusionUnavailable"
        :continuation-unavailable="continuationUnavailable"
        :turnover-share-unavailable="turnoverShareUnavailable"
      />
    </div>

    <!-- 4. 工具栏 -->
    <SectorToolbar
      v-model:board-type="boardType"
      v-model:sort="sort"
      v-model:compare-window="compareWindow"
      :board-options="boardOptions"
      :sort-options="sortOptions"
      :compare-window-options="compareWindowOptions"
      :board-count-text="toolbarBoardCountText"
      :snapshot-time="snapshotTime"
      :refresh-loading="realtimeSectorBoardLoading"
      @board-change="handleBoardChange"
      @sort-change="handleSortChange"
      @window-change="handleWindowChange"
      @refresh="fetchRealtimeSectorBoard"
    />

    <!-- 5. 错误/加载状态 -->
    <div v-if="error" class="feedback error">{{ error }}</div>
    <div v-else-if="loading" class="feedback">正在同步情绪轮动快照...</div>
    <!-- 6. 板块排名 + 详情 -->
    <section v-else class="market-grid">
      <SectorRankingList
        :sectors="sectors"
        :selected-sector-code="selectedSectorCode"
        :page="page"
        :total-pages="totalPages"
        :compare-window="compareWindow"
        :compare-window-label="compareWindowLabel"
        :page-offset="pageOffset"
        :empty-title="sectorEmptyTitle"
        :empty-body="sectorEmptyBody"
        @select="fetchDetail"
        @prev="goPrev"
        @next="goNext"
      />
      <SectorDetailPanel
        :detail="detail"
        :detail-loading="detailLoading"
        :detail-error="detailError"
        :compare-window="compareWindow"
        :compare-window-label="compareWindowLabel"
        @navigate-stock="navigateToStockInfo"
      />
    </section>

    <!-- 7. 10日情绪历史 -->
    <SentimentHistoryStrip :items="historyDisplayItems" />

    <!-- 8. 数据审计面板 -->
    <DataAuditPanel
      :data-sources="auditData?.dataSources ?? []"
      :latest-sync="auditData?.latestSync ?? null"
      :formula="auditData?.formula ?? ''"
      :computed-at="auditData?.computedAt ?? ''"
      :compute-duration-ms="auditData?.computeDurationMs ?? 0"
    />
  </section>
</template>

<style scoped>
.market-shell {
  display: grid;
  gap: 6px;
  padding: 8px;
  background: #111827;
  color: #e6eaf2;
  font-family: 'Consolas', 'Monaco', 'Menlo', monospace;
  font-size: 13px;
  min-height: 100vh;
}

/* feedback */
.feedback {
  padding: 6px 10px;
  border: 1px solid #334155;
  background: #1a2233;
  font-size: 12px;
  color: #e6eaf2;
  line-height: 1.35;
}
.feedback.error { color: #ff5c5c; border-color: #6a2a2a; background: #251417; }
.feedback.feedback-success { color: #1ee88f; border-color: #2d6a52; background: #12271f; }
.feedback.feedback-partial { color: #c2cad8; border-color: #4a566d; background: #1a2233; }
.feedback.feedback-error { color: #ff5c5c; border-color: #6a2a2a; background: #251417; }
.feedback.stale-warning { color: #c2cad8; border-color: #4a566d; background: #1a2233; }
.feedback.compact { padding: 4px 8px; }

/* overview row */
.overview-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  gap: 6px;
}
.overview-row > * { min-width: 0; }

/* grid */
.market-grid {
  display: grid;
  grid-template-columns: minmax(0, 1.1fr) minmax(0, 0.9fr);
  gap: 6px;
}
.market-grid > * { min-width: 0; }

/* responsive */
@media (max-width: 1100px) {
  .overview-row, .market-grid { grid-template-columns: 1fr; }
}
</style>


