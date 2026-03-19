<script setup>
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import ChatWindow from '../../components/ChatWindow.vue'

const presets = [
  { label: '每日国内外新闻', prompt: '请汇总今日国内外重要财经新闻，分点列出。' },
  { label: '当日股票推荐', prompt: '请给出今日值得关注的股票方向或个股，并简述理由与风险。' },
  { label: '当日行情分析', prompt: '请对今日市场行情进行简要分析，包含指数、热点板块与风险提示。' }
]

const chatRef = ref(null)
const sessionStorageKey = 'stock_recommend_chat_sessions'
const historyStorageKey = 'stock_recommend_chat_history_map'
const sessions = ref([])
const selectedSession = ref('')
const realtimeContextEnabled = ref(localStorage.getItem('stock_recommend_realtime_context_enabled') !== 'false')
const realtimeOverview = ref(null)
const realtimeSectors = ref([])
const marketLoading = ref(false)
const marketError = ref('')

const sessionOptions = computed(() => sessions.value)
const topSectors = computed(() => realtimeSectors.value.slice(0, 6))
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

const fetchJson = async url => {
  const response = await fetch(url)
  if (!response.ok) {
    const payload = await response.json().catch(() => null)
    throw new Error(payload?.message || '推荐页市场快照加载失败')
  }
  return response.json()
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

const persistSessions = () => {
  localStorage.setItem(sessionStorageKey, JSON.stringify(sessions.value))
}

const loadSessions = () => {
  try {
    const raw = localStorage.getItem(sessionStorageKey)
    sessions.value = raw ? JSON.parse(raw) : []
  } catch {
    sessions.value = []
  }
}

const createSession = () => {
  const timestamp = new Date()
  const key = `recommend-${timestamp.getTime()}`
  const label = `${timestamp.getFullYear()}-${String(timestamp.getMonth() + 1).padStart(2, '0')}-${String(
    timestamp.getDate()
  ).padStart(2, '0')} ${String(timestamp.getHours()).padStart(2, '0')}:${String(timestamp.getMinutes()).padStart(2, '0')}`
  const entry = { key, label }
  sessions.value = [entry, ...sessions.value]
  persistSessions()
  selectedSession.value = key
}

const handleNewChat = async () => {
  createSession()
  await nextTick()
  chatRef.value?.createNewChat()
}

onMounted(() => {
  loadSessions()
  if (!sessions.value.length) {
    createSession()
  } else if (!selectedSession.value) {
    selectedSession.value = sessions.value[0].key
  }
  fetchMarketContext()
})

watch(realtimeContextEnabled, value => {
  localStorage.setItem('stock_recommend_realtime_context_enabled', String(value))
  if (value) {
    fetchMarketContext()
    return
  }

  realtimeOverview.value = null
  realtimeSectors.value = []
  marketError.value = ''
})
</script>

<template>
  <section class="panel">
    <div class="panel-header">
      <h2>股票推荐</h2>
      <p class="muted">使用 LLM 汇总新闻、推荐与行情分析。</p>
    </div>

    <section class="recommend-market-card">
      <div class="recommend-market-head">
        <div>
          <p class="recommend-market-kicker">Realtime Context</p>
          <h3>推荐前市场快照</h3>
          <p class="muted">先看指数、资金和实时板块榜，再决定让 LLM 往哪个方向发力。</p>
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

    <ChatWindow
      ref="chatRef"
      title="推荐助手"
      :presets="presets"
      :history-key="selectedSession"
      :enable-history="true"
      :history-storage-key="historyStorageKey"
      placeholder="输入你的问题，例如：今天有哪些热点板块？"
      empty-text="点击上方按钮或输入问题开始对话。"
      max-height="520px"
    >
      <template #header-extra>
        <div class="session-selector">
          <select v-model="selectedSession">
            <option v-for="item in sessionOptions" :key="item.key" :value="item.key">
              {{ item.label }}
            </option>
          </select>
          <button class="session-new" @click="handleNewChat">新建对话</button>
        </div>
      </template>
    </ChatWindow>
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

.session-selector select {
  border-radius: 8px;
  border: 1px solid rgba(148, 163, 184, 0.4);
  padding: 0.35rem 0.6rem;
  background: #ffffff;
}

.session-new {
  border-radius: 999px;
  border: none;
  padding: 0.35rem 0.8rem;
  background: #e2e8f0;
  color: #0f172a;
  cursor: pointer;
}

.recommend-market-card {
  display: grid;
  gap: 0.9rem;
  margin-bottom: 1rem;
  padding: 1rem;
  border-radius: 18px;
  background: linear-gradient(135deg, rgba(255, 248, 237, 0.96), rgba(238, 248, 255, 0.92));
  border: 1px solid rgba(148, 163, 184, 0.18);
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
  color: #c2410c;
}

.recommend-market-head h3 {
  margin: 0;
}

.market-toggle.secondary {
  background: #fff;
}

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
  border-radius: 14px;
  background: rgba(255, 255, 255, 0.88);
  border: 1px solid rgba(148, 163, 184, 0.16);
}

.recommend-market-pills {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.market-pill {
  padding: 0.35rem 0.7rem;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.9);
  color: #334155;
  font-size: 0.85rem;
  border: 1px solid rgba(148, 163, 184, 0.16);
}

.recommend-sector-list {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 0.75rem;
}

.positive {
  color: #15803d;
}

.negative {
  color: #b91c1c;
}

</style>
