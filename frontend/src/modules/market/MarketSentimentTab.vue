<script setup>
import { computed, onMounted, ref, watch } from 'vue'
import MarketRealtimeOverview from './MarketRealtimeOverview.vue'

const boardType = ref('concept')
const sort = ref('strength')
const compareWindow = ref('10d')
const page = ref(1)
const pageSize = ref(12)
const loading = ref(false)
const detailLoading = ref(false)
const realtimeLoading = ref(false)
const realtimeSectorBoardLoading = ref(false)
const error = ref('')
const detailError = ref('')
const realtimeError = ref('')
const realtimeSectorBoardError = ref('')
const summary = ref(null)
const history = ref([])
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

const totalPages = computed(() => Math.max(1, Math.ceil((total.value || 0) / pageSize.value)))
const selectedSnapshot = computed(() => sectors.value.find(item => item.sectorCode === selectedSectorCode.value) ?? sectors.value[0] ?? null)
const stageToneClass = computed(() => {
  const label = summary.value?.stageLabelV2 || summary.value?.stageLabel || ''
  if (label === '主升') return 'tone-positive'
  if (label === '退潮') return 'tone-negative'
  if (label === '分歧') return 'tone-warning'
  return 'tone-neutral'
})
const realtimeSectorItems = computed(() => realtimeSectorBoard.value?.items ?? [])

const normalizeSummary = payload => payload ? ({
  snapshotTime: payload.snapshotTime ?? payload.SnapshotTime ?? '',
  sessionPhase: payload.sessionPhase ?? payload.SessionPhase ?? '',
  stageLabel: payload.stageLabel ?? payload.StageLabel ?? '混沌',
  stageScore: Number(payload.stageScore ?? payload.StageScore ?? 0),
  maxLimitUpStreak: Number(payload.maxLimitUpStreak ?? payload.MaxLimitUpStreak ?? 0),
  limitUpCount: Number(payload.limitUpCount ?? payload.LimitUpCount ?? 0),
  limitDownCount: Number(payload.limitDownCount ?? payload.LimitDownCount ?? 0),
  brokenBoardCount: Number(payload.brokenBoardCount ?? payload.BrokenBoardCount ?? 0),
  brokenBoardRate: Number(payload.brokenBoardRate ?? payload.BrokenBoardRate ?? 0),
  advancers: Number(payload.advancers ?? payload.Advancers ?? 0),
  decliners: Number(payload.decliners ?? payload.Decliners ?? 0),
  flatCount: Number(payload.flatCount ?? payload.FlatCount ?? 0),
  totalTurnover: Number(payload.totalTurnover ?? payload.TotalTurnover ?? 0),
  top3SectorTurnoverShare: Number(payload.top3SectorTurnoverShare ?? payload.Top3SectorTurnoverShare ?? 0),
  top10SectorTurnoverShare: Number(payload.top10SectorTurnoverShare ?? payload.Top10SectorTurnoverShare ?? 0),
  diffusionScore: Number(payload.diffusionScore ?? payload.DiffusionScore ?? 0),
  continuationScore: Number(payload.continuationScore ?? payload.ContinuationScore ?? 0),
  stageLabelV2: payload.stageLabelV2 ?? payload.StageLabelV2 ?? '',
  stageConfidence: Number(payload.stageConfidence ?? payload.StageConfidence ?? 0),
  top3SectorTurnoverShare5dAvg: Number(payload.top3SectorTurnoverShare5dAvg ?? payload.Top3SectorTurnoverShare5dAvg ?? 0),
  top10SectorTurnoverShare5dAvg: Number(payload.top10SectorTurnoverShare5dAvg ?? payload.Top10SectorTurnoverShare5dAvg ?? 0),
  limitUpCount5dAvg: Number(payload.limitUpCount5dAvg ?? payload.LimitUpCount5dAvg ?? 0),
  brokenBoardRate5dAvg: Number(payload.brokenBoardRate5dAvg ?? payload.BrokenBoardRate5dAvg ?? 0)
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
  breadthScore: Number(item.breadthScore ?? item.BreadthScore ?? 0),
  continuityScore: Number(item.continuityScore ?? item.ContinuityScore ?? 0),
  strengthScore: Number(item.strengthScore ?? item.StrengthScore ?? 0),
  newsSentiment: item.newsSentiment ?? item.NewsSentiment ?? '中性',
  newsHotCount: Number(item.newsHotCount ?? item.NewsHotCount ?? 0),
  leaderSymbol: item.leaderSymbol ?? item.LeaderSymbol ?? '',
  leaderName: item.leaderName ?? item.LeaderName ?? '',
  leaderChangePercent: item.leaderChangePercent ?? item.LeaderChangePercent ?? null,
  rankNo: Number(item.rankNo ?? item.RankNo ?? 0),
  snapshotTime: item.snapshotTime ?? item.SnapshotTime ?? '',
  rankChange5d: Number(item.rankChange5d ?? item.RankChange5d ?? 0),
  rankChange10d: Number(item.rankChange10d ?? item.RankChange10d ?? 0),
  rankChange20d: Number(item.rankChange20d ?? item.RankChange20d ?? 0),
  strengthAvg5d: Number(item.strengthAvg5d ?? item.StrengthAvg5d ?? 0),
  strengthAvg10d: Number(item.strengthAvg10d ?? item.StrengthAvg10d ?? 0),
  strengthAvg20d: Number(item.strengthAvg20d ?? item.StrengthAvg20d ?? 0),
  diffusionRate: Number(item.diffusionRate ?? item.DiffusionRate ?? 0),
  advancerCount: Number(item.advancerCount ?? item.AdvancerCount ?? 0),
  declinerCount: Number(item.declinerCount ?? item.DeclinerCount ?? 0),
  flatMemberCount: Number(item.flatMemberCount ?? item.FlatMemberCount ?? 0),
  limitUpMemberCount: Number(item.limitUpMemberCount ?? item.LimitUpMemberCount ?? 0),
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
  turnoverShare: Number(item.turnoverShare ?? item.TurnoverShare ?? 0),
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
    breadthScore: Number(item.breadthScore ?? item.BreadthScore ?? 0),
    continuityScore: Number(item.continuityScore ?? item.ContinuityScore ?? 0),
    strengthScore: Number(item.strengthScore ?? item.StrengthScore ?? 0),
    rankNo: Number(item.rankNo ?? item.RankNo ?? 0),
    diffusionRate: Number(item.diffusionRate ?? item.DiffusionRate ?? 0),
    advancerCount: Number(item.advancerCount ?? item.AdvancerCount ?? 0),
    declinerCount: Number(item.declinerCount ?? item.DeclinerCount ?? 0),
    flatMemberCount: Number(item.flatMemberCount ?? item.FlatMemberCount ?? 0),
    limitUpMemberCount: Number(item.limitUpMemberCount ?? item.LimitUpMemberCount ?? 0),
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
  price: Number(item.price ?? item.Price ?? 0),
  change: Number(item.change ?? item.Change ?? 0),
  changePercent: Number(item.changePercent ?? item.ChangePercent ?? 0),
  turnoverAmount: Number(item.turnoverAmount ?? item.TurnoverAmount ?? 0),
  timestamp: item.timestamp ?? item.Timestamp ?? ''
})

const normalizeMainFlow = payload => payload ? ({
  snapshotTime: payload.snapshotTime ?? payload.SnapshotTime ?? '',
  amountUnit: payload.amountUnit ?? payload.AmountUnit ?? '亿元',
  mainNetInflow: Number(payload.mainNetInflow ?? payload.MainNetInflow ?? 0),
  smallOrderNetInflow: Number(payload.smallOrderNetInflow ?? payload.SmallOrderNetInflow ?? 0),
  mediumOrderNetInflow: Number(payload.mediumOrderNetInflow ?? payload.MediumOrderNetInflow ?? 0),
  largeOrderNetInflow: Number(payload.largeOrderNetInflow ?? payload.LargeOrderNetInflow ?? 0),
  superLargeOrderNetInflow: Number(payload.superLargeOrderNetInflow ?? payload.SuperLargeOrderNetInflow ?? 0)
}) : null

const normalizeNorthbound = payload => payload ? ({
  snapshotTime: payload.snapshotTime ?? payload.SnapshotTime ?? '',
  amountUnit: payload.amountUnit ?? payload.AmountUnit ?? '亿元',
  totalNetInflow: Number(payload.totalNetInflow ?? payload.TotalNetInflow ?? 0),
  shanghaiNetInflow: Number(payload.shanghaiNetInflow ?? payload.ShanghaiNetInflow ?? 0),
  shenzhenNetInflow: Number(payload.shenzhenNetInflow ?? payload.ShenzhenNetInflow ?? 0),
  shanghaiBalance: Number(payload.shanghaiBalance ?? payload.ShanghaiBalance ?? 0),
  shenzhenBalance: Number(payload.shenzhenBalance ?? payload.ShenzhenBalance ?? 0)
}) : null

const normalizeBreadth = payload => payload ? ({
  tradingDate: payload.tradingDate ?? payload.TradingDate ?? '',
  advancers: Number(payload.advancers ?? payload.Advancers ?? 0),
  decliners: Number(payload.decliners ?? payload.Decliners ?? 0),
  flatCount: Number(payload.flatCount ?? payload.FlatCount ?? 0),
  limitUpCount: Number(payload.limitUpCount ?? payload.LimitUpCount ?? 0),
  limitDownCount: Number(payload.limitDownCount ?? payload.LimitDownCount ?? 0),
  buckets: Array.isArray(payload.buckets ?? payload.Buckets)
    ? (payload.buckets ?? payload.Buckets).map(item => ({
        changeBucket: Number(item.changeBucket ?? item.ChangeBucket ?? 0),
        label: item.label ?? item.Label ?? '',
        count: Number(item.count ?? item.Count ?? 0)
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

const formatMoney = value => {
  const number = Number(value ?? 0)
  const abs = Math.abs(number)
  if (abs >= 100000000) return `${(number / 100000000).toFixed(2)} 亿`
  if (abs >= 10000) return `${(number / 10000).toFixed(2)} 万`
  return number.toFixed(0)
}

const formatSignedAmount = value => {
  const number = Number(value ?? 0)
  return `${number >= 0 ? '+' : ''}${number.toFixed(2)} 亿`
}

const formatScale = value => `${(Number(value ?? 0) * 100).toFixed(0)}%`

const getRealtimeSectorSort = value => {
  if (value === 'change') return 'change'
  if (value === 'flow') return 'flow'
  return 'rank'
}

const applyRealtimeSectorBoard = (items, realtimePayload) => {
  if (!Array.isArray(items) || !items.length || !realtimeSectorBoardEnabled.value) {
    return Array.isArray(items) ? [...items] : []
  }

  const realtimeItems = Array.isArray(realtimePayload?.items) ? realtimePayload.items : []
  if (!realtimeItems.length) {
    return [...items]
  }

  const realtimeMap = new Map(realtimeItems.map(item => [item.sectorCode, item]))
  const matched = []
  const unmatched = []

  items.forEach(item => {
    const realtimeItem = realtimeMap.get(item.sectorCode)
    const mergedItem = realtimeItem
      ? {
          ...item,
          changePercent: realtimeItem.changePercent,
          mainNetInflow: realtimeItem.mainNetInflow,
          rankNo: realtimeItem.rankNo,
          snapshotTime: realtimeItem.snapshotTime || item.snapshotTime,
          realtimeTurnoverAmount: realtimeItem.turnoverAmount,
          realtimeTurnoverShare: realtimeItem.turnoverShare
        }
      : item

    if (realtimeItem) {
      matched.push(mergedItem)
    } else {
      unmatched.push(mergedItem)
    }
  })

  matched.sort((left, right) => {
    if (left.rankNo !== right.rankNo) return left.rankNo - right.rankNo
    return right.changePercent - left.changePercent
  })

  return [...matched, ...unmatched]
}

const getWindowStrength = item => {
  if (compareWindow.value === '5d') return item.strengthAvg5d
  if (compareWindow.value === '20d') return item.strengthAvg20d
  return item.strengthAvg10d
}

const getWindowRankChange = item => {
  if (compareWindow.value === '5d') return item.rankChange5d
  if (compareWindow.value === '20d') return item.rankChange20d
  return item.rankChange10d
}

const getWindowRankLabel = item => {
  const value = getWindowRankChange(item)
  return `${value >= 0 ? '+' : ''}${value}`
}

const openExternal = url => {
  if (!url) return
  window.open(url, '_blank', 'noopener,noreferrer')
}

const fetchJson = async url => {
  const response = await fetch(url)
  if (!response.ok) {
    const payload = await response.json().catch(() => null)
    throw new Error(payload?.message || '情绪轮动数据加载失败')
  }
  return response.json()
}

const fetchDetail = async sectorCode => {
  if (!sectorCode) {
    detail.value = null
    selectedSectorCode.value = ''
    return
  }

  selectedSectorCode.value = sectorCode
  detailLoading.value = true
  detailError.value = ''
  try {
    const payload = await fetchJson(`/api/market/sectors/${encodeURIComponent(sectorCode)}?boardType=${encodeURIComponent(boardType.value)}&window=${encodeURIComponent(compareWindow.value)}`)
    detail.value = normalizeDetail(payload)
  } catch (err) {
    detail.value = null
    detailError.value = err.message || '板块详情加载失败'
  } finally {
    detailLoading.value = false
  }
}

const fetchRealtimeSectorBoard = async ({ silent = false } = {}) => {
  if (!realtimeSectorBoardEnabled.value) {
    realtimeSectorBoard.value = null
    realtimeSectorBoardError.value = ''
    sectors.value = [...sectorBaseItems.value]
    return null
  }

  if (!silent) {
    realtimeSectorBoardLoading.value = true
  }
  realtimeSectorBoardError.value = ''

  try {
    const realtimeSort = getRealtimeSectorSort(sort.value)
    const payload = await fetchJson(`/api/market/sectors/realtime?boardType=${encodeURIComponent(boardType.value)}&take=${Math.max(pageSize.value * 6, 60)}&sort=${encodeURIComponent(realtimeSort)}`)
    realtimeSectorBoard.value = normalizeRealtimeSectorBoard(payload)
    sectors.value = applyRealtimeSectorBoard(sectorBaseItems.value, realtimeSectorBoard.value)
    return realtimeSectorBoard.value
  } catch (err) {
    realtimeSectorBoard.value = null
    realtimeSectorBoardError.value = err.message || '实时板块榜加载失败'
    sectors.value = [...sectorBaseItems.value]
    return null
  } finally {
    realtimeSectorBoardLoading.value = false
  }
}

const fetchDashboard = async ({ resetPage = false } = {}) => {
  if (resetPage) page.value = 1

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
    sectorBaseItems.value = Array.isArray(sectorPayload?.items ?? sectorPayload?.Items) ? (sectorPayload.items ?? sectorPayload.Items).map(normalizeSectorItem) : []
    sectors.value = [...sectorBaseItems.value]
    await fetchRealtimeSectorBoard({ silent: true })

    const nextSelected = sectors.value.some(item => item.sectorCode === selectedSectorCode.value)
      ? selectedSectorCode.value
      : sectors.value[0]?.sectorCode || ''
    await fetchDetail(nextSelected)
  } catch (err) {
    summary.value = null
    history.value = []
    sectors.value = []
    total.value = 0
    detail.value = null
    selectedSectorCode.value = ''
    error.value = err.message || '情绪轮动数据加载失败'
  } finally {
    loading.value = false
  }
}

const fetchRealtimeOverview = async () => {
  if (!realtimeOverviewEnabled.value) {
    realtimeOverview.value = null
    realtimeError.value = ''
    realtimeLoading.value = false
    return
  }

  realtimeLoading.value = true
  realtimeError.value = ''
  try {
    const payload = await fetchJson('/api/market/realtime/overview')
    realtimeOverview.value = normalizeRealtimeOverview(payload)
  } catch (err) {
    realtimeOverview.value = null
    realtimeError.value = err.message || '实时总览加载失败'
  } finally {
    realtimeLoading.value = false
  }
}

const handleBoardChange = () => fetchDashboard({ resetPage: true })
const handleSortChange = () => fetchDashboard({ resetPage: true })
const handleWindowChange = () => fetchDetail(selectedSectorCode.value || sectors.value[0]?.sectorCode || '')
const goPrev = () => {
  if (page.value <= 1) return
  page.value -= 1
  fetchDashboard()
}
const goNext = () => {
  if (page.value >= totalPages.value) return
  page.value += 1
  fetchDashboard()
}

watch(boardType, () => {
  detail.value = null
})

watch(compareWindow, () => {
  handleWindowChange()
})

watch(realtimeOverviewEnabled, value => {
  localStorage.setItem('market_realtime_overview_enabled', String(value))
  if (value) {
    fetchRealtimeOverview()
    return
  }

  realtimeOverview.value = null
  realtimeError.value = ''
})

watch(realtimeSectorBoardEnabled, value => {
  localStorage.setItem('market_realtime_sector_board_enabled', String(value))
  if (value) {
    fetchRealtimeSectorBoard()
    return
  }

  realtimeSectorBoard.value = null
  realtimeSectorBoardError.value = ''
  sectors.value = [...sectorBaseItems.value]
})

onMounted(() => {
  fetchDashboard()
  fetchRealtimeOverview()
})
</script>

<template>
  <section class="market-shell">
    <header class="market-hero">
      <div class="hero-copy">
        <p class="market-kicker">Market Pulse / Sector Rotation</p>
        <h2>情绪轮动</h2>
        <p class="hero-subtitle">把涨停高度、涨跌家数、炸板率与板块扩散度压成同一屏，快速判断今天是主升、分歧、混沌还是退潮。</p>
        <div class="hero-actions">
          <button class="hero-button" type="button" @click="fetchRealtimeOverview" :disabled="realtimeLoading || !realtimeOverviewEnabled">刷新实时总览</button>
          <button class="hero-button secondary" type="button" @click="realtimeOverviewEnabled = !realtimeOverviewEnabled">
            {{ realtimeOverviewEnabled ? '隐藏实时总览' : '显示实时总览' }}
          </button>
        </div>
      </div>
      <div class="hero-stage" :class="stageToneClass">
        <span class="stage-phase">{{ summary?.sessionPhase || '待同步' }}</span>
        <strong>{{ summary?.stageLabelV2 || summary?.stageLabel || '暂无快照' }}</strong>
        <span>情绪分 {{ (summary?.stageScore ?? 0).toFixed(2) }} / 置信 {{ (summary?.stageConfidence ?? 0).toFixed(0) }}</span>
        <small>{{ formatDate(summary?.snapshotTime) }}</small>
      </div>
    </header>

    <MarketRealtimeOverview
      v-if="realtimeOverviewEnabled"
      :overview="realtimeOverview"
      :loading="realtimeLoading"
      :error="realtimeError"
      :format-date="formatDate"
      :format-money="formatMoney"
      :format-signed-percent="formatSignedPercent"
      :format-signed-amount="formatSignedAmount"
    />

    <section class="metric-grid">
      <article class="metric-card">
        <span>涨停 / 跌停</span>
        <strong>{{ summary?.limitUpCount ?? 0 }} / {{ summary?.limitDownCount ?? 0 }}</strong>
        <small>5日均值 {{ (summary?.limitUpCount5dAvg ?? 0).toFixed(1) }} / 最高连板 {{ summary?.maxLimitUpStreak ?? 0 }}</small>
      </article>
      <article class="metric-card">
        <span>炸板率</span>
        <strong>{{ (summary?.brokenBoardRate ?? 0).toFixed(2) }}%</strong>
        <small>5日均值 {{ (summary?.brokenBoardRate5dAvg ?? 0).toFixed(2) }}% / 炸板数 {{ summary?.brokenBoardCount ?? 0 }}</small>
      </article>
      <article class="metric-card">
        <span>扩散 / 持续</span>
        <strong>{{ (summary?.diffusionScore ?? 0).toFixed(1) }} / {{ (summary?.continuationScore ?? 0).toFixed(1) }}</strong>
        <small>涨跌家数 {{ summary?.advancers ?? 0 }} / {{ summary?.decliners ?? 0 }} / 平盘 {{ summary?.flatCount ?? 0 }}</small>
      </article>
      <article class="metric-card">
        <span>热门板块成交占比</span>
        <strong>{{ (summary?.top3SectorTurnoverShare ?? 0).toFixed(2) }}%</strong>
        <small>5日均值 {{ (summary?.top3SectorTurnoverShare5dAvg ?? 0).toFixed(2) }}% / Top10 {{ (summary?.top10SectorTurnoverShare5dAvg ?? 0).toFixed(2) }}%</small>
      </article>
    </section>

    <section class="history-strip">
      <div v-for="item in history" :key="`${item.tradingDate}-${item.snapshotTime}`" class="history-chip">
        <span>{{ formatDate(item.tradingDate).slice(0, 10) }}</span>
        <strong>{{ item.stageLabel }}</strong>
        <small>{{ item.stageScore.toFixed(1) }} 分</small>
      </div>
    </section>

    <section class="board-toolbar">
      <label class="toolbar-field">
        <span>轮动维度</span>
        <select v-model="boardType" @change="handleBoardChange">
          <option v-for="option in boardOptions" :key="option.value" :value="option.value">{{ option.label }}</option>
        </select>
      </label>
      <label class="toolbar-field">
        <span>排序方式</span>
        <select v-model="sort" @change="handleSortChange">
          <option v-for="option in sortOptions" :key="option.value" :value="option.value">{{ option.label }}</option>
        </select>
      </label>
      <label class="toolbar-field">
        <span>比较窗口</span>
        <select v-model="compareWindow" @change="handleWindowChange">
          <option v-for="option in compareWindowOptions" :key="option.value" :value="option.value">{{ option.label }}</option>
        </select>
      </label>
      <div class="toolbar-meta">
        <strong>共 {{ total }} 个板块</strong>
        <span>快照 {{ formatDate(snapshotTime) }}</span>
        <span v-if="realtimeSectorBoardEnabled">东财实时榜 {{ formatDate(realtimeSectorBoard?.snapshotTime) }}</span>
        <button class="toolbar-inline-button" type="button" @click="fetchRealtimeSectorBoard" :disabled="realtimeSectorBoardLoading || !realtimeSectorBoardEnabled">刷新实时榜</button>
        <button class="toolbar-inline-button secondary" type="button" @click="realtimeSectorBoardEnabled = !realtimeSectorBoardEnabled">
          {{ realtimeSectorBoardEnabled ? '隐藏实时榜' : '显示实时榜' }}
        </button>
      </div>
    </section>

    <p v-if="realtimeSectorBoardError" class="feedback error compact">{{ realtimeSectorBoardError }}</p>

    <div v-if="error" class="feedback error">{{ error }}</div>
    <div v-else-if="loading" class="feedback">正在同步情绪轮动快照...</div>
    <section v-else class="market-grid">
      <div class="sector-list">
        <button
          v-for="item in sectors"
          :key="`${item.boardType}-${item.sectorCode}`"
          type="button"
          class="sector-card"
          :class="{ active: item.sectorCode === selectedSectorCode }"
          @click="fetchDetail(item.sectorCode)"
        >
          <div class="sector-head">
            <span class="sector-rank">#{{ item.rankNo }}</span>
            <strong>{{ item.sectorName }}</strong>
            <span v-if="item.isMainline" class="mainline-badge">主线</span>
            <span class="sector-change" :class="{ positive: item.changePercent >= 0, negative: item.changePercent < 0 }">{{ formatSignedPercent(item.changePercent) }}</span>
          </div>
          <div class="sector-metrics">
            <span>综合 {{ item.strengthScore.toFixed(1) }}</span>
            <span>{{ compareWindowOptions.find(option => option.value === compareWindow)?.label }} {{ getWindowStrength(item).toFixed(1) }}</span>
            <span>扩散 {{ item.diffusionRate.toFixed(1) }}</span>
            <span>排名变化 {{ getWindowRankLabel(item) }}</span>
          </div>
          <div class="sector-meta">
            <span>{{ item.newsSentiment }} / 热点 {{ item.newsHotCount }}</span>
            <span>{{ item.leaderName || '暂无龙头' }}</span>
            <span>主线 {{ item.mainlineScore.toFixed(1) }} / 龙头稳定 {{ item.leaderStabilityScore.toFixed(1) }}</span>
          </div>
        </button>

        <footer class="pagination">
          <button @click="goPrev" :disabled="page <= 1">上一页</button>
          <span>第 {{ page }} / {{ totalPages }} 页</span>
          <button @click="goNext" :disabled="page >= totalPages">下一页</button>
        </footer>
      </div>

      <aside class="detail-panel">
        <div v-if="detailError" class="feedback error compact">{{ detailError }}</div>
        <div v-else-if="detailLoading" class="feedback compact">正在加载板块详情...</div>
        <template v-else-if="detail">
          <header class="detail-head">
            <div>
              <p>{{ detail.snapshot.boardType }} / {{ detail.snapshot.sectorCode }}</p>
              <h3>{{ detail.snapshot.sectorName }}</h3>
            </div>
            <div class="detail-badge" :class="{ positive: detail.snapshot.changePercent >= 0, negative: detail.snapshot.changePercent < 0 }">
              {{ formatSignedPercent(detail.snapshot.changePercent) }}
            </div>
          </header>

          <section class="detail-block">
            <h4>龙头分布</h4>
            <p class="detail-summary">主线分 {{ detail.snapshot.mainlineScore.toFixed(1) }} / 龙头稳定 {{ detail.snapshot.leaderStabilityScore.toFixed(1) }} / {{ detail.snapshot.isMainline ? '当前主线' : '观察中' }}</p>
            <div v-if="!detail.leaders.length" class="empty-hint">当前没有龙头股快照。</div>
            <div v-else class="leader-list">
              <div v-for="leader in detail.leaders" :key="`${detail.snapshot.sectorCode}-${leader.symbol}`" class="leader-item">
                <span>#{{ leader.rankInSector }} {{ leader.name }} {{ leader.symbol }}</span>
                <strong :class="{ positive: leader.changePercent >= 0, negative: leader.changePercent < 0 }">{{ formatSignedPercent(leader.changePercent) }}</strong>
              </div>
            </div>
          </section>

          <section class="detail-block">
            <h4>近端趋势</h4>
            <div v-if="!detail.history.length" class="empty-hint">暂无可用历史。</div>
            <div v-else class="trend-list">
              <div v-for="item in detail.history" :key="`${detail.snapshot.sectorCode}-${item.tradingDate}`" class="trend-item">
                <span>{{ formatDate(item.tradingDate).slice(0, 10) }}</span>
                <span>{{ formatSignedPercent(item.changePercent) }}</span>
                <span>{{ compareWindow }} {{ (compareWindow === '5d' ? item.strengthAvg5d : compareWindow === '20d' ? item.strengthAvg20d : item.strengthAvg10d).toFixed(1) }}</span>
                <span>排名 {{ compareWindow === '5d' ? item.rankChange5d : compareWindow === '20d' ? item.rankChange20d : item.rankChange10d }}</span>
              </div>
            </div>
          </section>

          <section class="detail-block">
            <h4>扩散拆解</h4>
            <div class="trend-list breadth-breakdown">
              <div class="trend-item">
                <span>上涨成员</span>
                <strong>{{ detail.snapshot.advancerCount }}</strong>
              </div>
              <div class="trend-item">
                <span>下跌成员</span>
                <strong>{{ detail.snapshot.declinerCount }}</strong>
              </div>
              <div class="trend-item">
                <span>平盘成员</span>
                <strong>{{ detail.snapshot.flatMemberCount }}</strong>
              </div>
              <div class="trend-item">
                <span>涨停成员</span>
                <strong>{{ detail.snapshot.limitUpMemberCount }}</strong>
              </div>
            </div>
          </section>

          <section class="detail-block">
            <h4>相关新闻</h4>
            <div v-if="!detail.news.length" class="empty-hint">本地事实库暂无该板块新闻。</div>
            <article v-for="item in detail.news" :key="`${detail.snapshot.sectorCode}-${item.title}-${item.publishTime}`" class="news-item">
              <div>
                <strong>{{ item.translatedTitle || item.title }}</strong>
                <p>{{ item.source }} / {{ item.sentiment }} / {{ formatDate(item.publishTime) }}</p>
              </div>
              <button v-if="item.url" class="ghost-button" @click="openExternal(item.url)">原文</button>
            </article>
          </section>
        </template>
        <div v-else class="feedback compact">当前没有可展示的板块详情。</div>
      </aside>
    </section>
  </section>
</template>

<style scoped>
.market-shell {
  display: grid;
  gap: 18px;
  padding: 24px;
  color: #0f172a;
}

.market-hero {
  display: flex;
  justify-content: space-between;
  gap: 18px;
  padding: 24px;
  border-radius: 28px;
  background:
    radial-gradient(circle at right top, rgba(249, 115, 22, 0.24), transparent 28%),
    radial-gradient(circle at left bottom, rgba(14, 165, 233, 0.18), transparent 34%),
    linear-gradient(135deg, #fff9ed 0%, #fff 55%, #eef8ff 100%);
  border: 1px solid rgba(148, 163, 184, 0.24);
  box-shadow: 0 18px 45px rgba(15, 23, 42, 0.08);
}

.market-kicker {
  margin: 0 0 8px;
  font-size: 12px;
  letter-spacing: 0.18em;
  text-transform: uppercase;
  color: #c2410c;
}

.hero-copy h2 {
  margin: 0;
  font-size: 34px;
}

.hero-subtitle {
  margin: 12px 0 0;
  max-width: 720px;
  color: #475569;
  line-height: 1.6;
}

.hero-actions {
  display: flex;
  gap: 10px;
  margin-top: 16px;
  flex-wrap: wrap;
}

.hero-button {
  height: 40px;
  padding: 0 14px;
  border: 0;
  border-radius: 999px;
  background: #c2410c;
  color: #fff;
  font-weight: 700;
  cursor: pointer;
}

.hero-button.secondary {
  background: rgba(255, 255, 255, 0.92);
  color: #9a3412;
  border: 1px solid rgba(194, 65, 12, 0.16);
}

.hero-button:disabled {
  cursor: not-allowed;
  opacity: 0.6;
}

.toolbar-inline-button {
  height: 32px;
  padding: 0 12px;
  border: 1px solid rgba(148, 163, 184, 0.24);
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.92);
  color: #9a3412;
  font-weight: 600;
  cursor: pointer;
}

.toolbar-inline-button.secondary {
  color: #475569;
}

.toolbar-inline-button:disabled {
  cursor: not-allowed;
  opacity: 0.6;
}

.hero-stage {
  display: grid;
  gap: 8px;
  min-width: 180px;
  padding: 18px 20px;
  border-radius: 22px;
  background: rgba(255, 255, 255, 0.86);
  align-content: start;
}

.hero-stage strong {
  font-size: 28px;
}

.stage-phase {
  font-size: 12px;
  letter-spacing: 0.12em;
  text-transform: uppercase;
}

.tone-positive { color: #166534; }
.tone-warning { color: #92400e; }
.tone-negative { color: #b91c1c; }
.tone-neutral { color: #334155; }

.metric-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 14px;
}

.metric-card,
.board-toolbar,
.detail-panel,
.history-chip {
  border: 1px solid rgba(226, 232, 240, 0.92);
  background: #fff;
  box-shadow: 0 12px 30px rgba(15, 23, 42, 0.05);
}

.mainline-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 4px 8px;
  border-radius: 999px;
  background: #dcfce7;
  color: #166534;
  font-size: 12px;
  font-weight: 700;
}

.detail-summary {
  margin: 0 0 12px;
  color: #64748b;
}

.metric-card {
  display: grid;
  gap: 8px;
  padding: 18px;
  border-radius: 20px;
}

.metric-card span,
.metric-card small,
.toolbar-field span,
.toolbar-meta span,
.sector-meta,
.empty-hint,
.news-item p,
.feedback {
  color: #64748b;
}

.metric-card strong {
  font-size: 24px;
}

.history-strip {
  display: flex;
  gap: 12px;
  overflow-x: auto;
  padding-bottom: 4px;
}

.history-chip {
  display: grid;
  gap: 4px;
  min-width: 120px;
  padding: 14px 16px;
  border-radius: 18px;
}

.board-toolbar {
  display: grid;
  grid-template-columns: repeat(2, minmax(180px, 240px)) 1fr;
  gap: 14px;
  align-items: end;
  padding: 18px;
  border-radius: 22px;
}

.toolbar-field {
  display: grid;
  gap: 8px;
}

.toolbar-field span {
  font-size: 12px;
  letter-spacing: 0.08em;
  text-transform: uppercase;
}

.toolbar-field select {
  height: 44px;
  padding: 0 14px;
  border-radius: 14px;
  border: 1px solid #dbe2ea;
  background: #f8fafc;
}

.toolbar-meta {
  display: grid;
  justify-items: end;
  gap: 6px;
}

.market-grid {
  display: grid;
  grid-template-columns: minmax(0, 1.2fr) minmax(320px, 0.88fr);
  gap: 16px;
}

.sector-list {
  display: grid;
  gap: 12px;
}

.sector-card {
  display: grid;
  gap: 10px;
  width: 100%;
  padding: 18px;
  border-radius: 18px;
  text-align: left;
  cursor: pointer;
}

.sector-card.active {
  border-color: rgba(194, 65, 12, 0.34);
  box-shadow: 0 16px 36px rgba(194, 65, 12, 0.15);
}

.sector-head,
.sector-metrics,
.sector-meta,
.detail-head,
.leader-item,
.trend-item,
.news-item,
.pagination {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  align-items: center;
}

.sector-rank {
  color: #c2410c;
  font-weight: 700;
}

.sector-change,
.detail-badge,
.leader-item strong {
  font-variant-numeric: tabular-nums;
}

.positive { color: #b91c1c; }
.negative { color: #166534; }

.detail-panel {
  display: grid;
  gap: 16px;
  padding: 20px;
  border-radius: 22px;
  align-content: start;
}

.detail-head h3,
.detail-block h4 {
  margin: 0;
}

.detail-head p {
  margin: 0 0 6px;
  color: #64748b;
}

.detail-badge {
  padding: 10px 14px;
  border-radius: 14px;
  background: #f8fafc;
  font-weight: 700;
}

.detail-block {
  display: grid;
  gap: 10px;
}

.leader-list,
.trend-list {
  display: grid;
  gap: 8px;
}

.news-item {
  padding: 12px 0;
  border-top: 1px solid rgba(226, 232, 240, 0.8);
}

.news-item:first-of-type {
  border-top: 0;
  padding-top: 0;
}

.ghost-button,
.pagination button {
  height: 38px;
  border: 0;
  border-radius: 12px;
  padding: 0 14px;
  background: #e2e8f0;
  color: #0f172a;
  font-weight: 600;
  cursor: pointer;
}

.pagination {
  padding: 10px 2px 0;
}

.feedback {
  padding: 18px;
  border-radius: 16px;
  background: #fff;
  border: 1px solid rgba(226, 232, 240, 0.92);
}

.feedback.error {
  color: #b91c1c;
  border-color: rgba(185, 28, 28, 0.22);
  background: #fff7f7;
}

@media (max-width: 1100px) {
  .metric-grid,
  .market-grid,
  .board-toolbar {
    grid-template-columns: 1fr;
  }

  .toolbar-meta {
    justify-items: start;
  }
}

@media (max-width: 720px) {
  .market-shell {
    padding: 16px;
  }

  .market-hero,
  .sector-head,
  .sector-metrics,
  .sector-meta,
  .detail-head,
  .leader-item,
  .trend-item,
  .news-item,
  .pagination {
    display: grid;
  }
}
</style>
