<script setup>
import { computed, onMounted, ref } from 'vue'

const DEFAULT_STRATEGY_LIST = 'ma,macd,rsi,kdj,vwap,td,breakout,gap'

const symbol = ref('sh600000')
const klineCount = ref(30)
const strategyList = ref(DEFAULT_STRATEGY_LIST)
const searchQuery = ref('浦发银行 最新公告')
const loading = ref(false)
const errorMessage = ref('')
const lastLoadedAt = ref('')

const replay = ref(null)
const kline = ref(null)
const minute = ref(null)
const strategy = ref(null)
const news = ref(null)
const search = ref(null)

const toStatus = payload => {
  if (!payload) {
    return 'idle'
  }

  return (payload.degradedFlags?.length || payload.warnings?.length) ? 'warn' : 'ok'
}

const formatList = value => {
  if (!Array.isArray(value) || value.length === 0) {
    return 'none'
  }

  return value.join(', ')
}

const compactMeta = values => values.filter(item => item.value)

const diagnostics = computed(() => {
  return [
    {
      key: 'replay',
      title: 'Replay 基线',
      payload: replay.value,
      status: replay.value ? 'ok' : 'idle',
      lines: replay.value
        ? [`scope: ${replay.value.scope}`, `sampleCount: ${replay.value.sampleCount}`, `horizons: ${replay.value.horizons?.length || 0}`]
        : ['尚未加载'],
      meta: compactMeta([
        { label: '样本', value: String(replay.value?.samples?.length || 0) },
        { label: 'traceId', value: replay.value?.samples?.[0]?.traceId || '' }
      ])
    },
    {
      key: 'kline',
      title: 'K 线 MCP',
      payload: kline.value,
      status: toStatus(kline.value),
      lines: kline.value
        ? [`tool: ${kline.value.toolName}`, `bars: ${kline.value.data?.bars?.length || 0}`, `policy: ${kline.value.meta?.policyClass || '-'}`]
        : ['尚未加载'],
      meta: compactMeta([
        { label: 'traceId', value: kline.value?.traceId || '' },
        { label: 'taskId', value: kline.value?.taskId || '' },
        { label: 'degraded', value: formatList(kline.value?.degradedFlags) }
      ])
    },
    {
      key: 'minute',
      title: '分时 MCP',
      payload: minute.value,
      status: toStatus(minute.value),
      lines: minute.value
        ? [`tool: ${minute.value.toolName}`, `points: ${minute.value.data?.points?.length || 0}`, `session: ${minute.value.data?.sessionPhase || '-'}`]
        : ['尚未加载'],
      meta: compactMeta([
        { label: 'traceId', value: minute.value?.traceId || '' },
        { label: 'taskId', value: minute.value?.taskId || '' },
        { label: 'degraded', value: formatList(minute.value?.degradedFlags) }
      ])
    },
    {
      key: 'strategy',
      title: '策略 MCP',
      payload: strategy.value,
      status: toStatus(strategy.value),
      lines: strategy.value
        ? [`signals: ${strategy.value.data?.signals?.length || 0}`, `first: ${strategy.value.data?.signals?.[0]?.strategy || '-'}`, `features: ${strategy.value.features?.length || 0}`]
        : ['尚未加载'],
      meta: compactMeta([
        { label: 'traceId', value: strategy.value?.traceId || '' },
        { label: 'taskId', value: strategy.value?.taskId || '' },
        { label: 'warnings', value: formatList(strategy.value?.warnings) }
      ])
    },
    {
      key: 'news',
      title: '新闻 MCP',
      payload: news.value,
      status: toStatus(news.value),
      lines: news.value
        ? [`items: ${news.value.data?.itemCount || 0}`, `evidence: ${news.value.evidence?.length || 0}`, `policy: ${news.value.meta?.policyClass || '-'}`]
        : ['尚未加载'],
      meta: compactMeta([
        { label: 'traceId', value: news.value?.traceId || '' },
        { label: 'taskId', value: news.value?.taskId || '' },
        { label: 'degraded', value: formatList(news.value?.degradedFlags) }
      ])
    },
    {
      key: 'search',
      title: '搜索 MCP',
      payload: search.value,
      status: toStatus(search.value),
      lines: search.value
        ? [`provider: ${search.value.data?.provider || '-'}`, `results: ${search.value.data?.resultCount || 0}`, `degraded: ${(search.value.degradedFlags || []).join(',') || 'none'}`]
        : ['尚未加载'],
      meta: compactMeta([
        { label: 'traceId', value: search.value?.traceId || '' },
        { label: 'taskId', value: search.value?.taskId || '' },
        { label: 'warnings', value: formatList(search.value?.warnings) }
      ])
    }
  ]
})

const runtimeHighlights = computed(() => {
  const degradedCount = diagnostics.value.filter(item => item.status === 'warn').length
  const traceCount = diagnostics.value.filter(item => item.payload?.traceId).length + (replay.value?.samples?.[0]?.traceId ? 1 : 0)

  return [
    { key: 'symbol', label: '当前标的', value: symbol.value.trim() || '-' },
    { key: 'loaded', label: '已加载模块', value: `${diagnostics.value.filter(item => item.status !== 'idle').length}/6` },
    { key: 'degraded', label: '降级模块', value: String(degradedCount) },
    { key: 'trace', label: '可见 Trace', value: String(traceCount) }
  ]
})

const requestHints = computed(() => {
  const normalizedSymbol = symbol.value.trim() || '-'
  const normalizedCount = String(Math.max(20, Number(klineCount.value) || 30))
  const normalizedStrategies = strategyList.value.trim() || DEFAULT_STRATEGY_LIST
  const normalizedQuery = searchQuery.value.trim() || `${normalizedSymbol} 最新公告`

  return [
    `symbol=${normalizedSymbol}`,
    `count=${normalizedCount}`,
    `strategies=${normalizedStrategies}`,
    `search=${normalizedQuery}`
  ]
})

const prettyJson = value => {
  if (!value) {
    return ''
  }

  return JSON.stringify(value, null, 2)
}

const fetchJson = async path => {
  const response = await fetch(path)
  if (!response.ok) {
    throw new Error(await response.text() || '请求失败')
  }

  return response.json()
}

const loadDashboard = async () => {
  const normalizedSymbol = symbol.value.trim()
  if (!normalizedSymbol) {
    errorMessage.value = 'symbol 不能为空'
    return
  }

  const normalizedCount = Math.max(20, Number(klineCount.value) || 30)
  const normalizedStrategies = strategyList.value.trim() || DEFAULT_STRATEGY_LIST
  const normalizedQuery = searchQuery.value.trim() || `${normalizedSymbol} 最新公告`

  symbol.value = normalizedSymbol
  klineCount.value = normalizedCount
  strategyList.value = normalizedStrategies
  searchQuery.value = normalizedQuery

  loading.value = true
  errorMessage.value = ''
  try {
    const params = new URLSearchParams({ symbol: normalizedSymbol, take: '20' })
    const klineParams = new URLSearchParams({ symbol: normalizedSymbol, interval: 'day', count: String(normalizedCount), taskId: 'stock-copilot-dev-kline' })
    const minuteParams = new URLSearchParams({ symbol: normalizedSymbol, taskId: 'stock-copilot-dev-minute' })
    const strategyParams = new URLSearchParams({ symbol: normalizedSymbol, interval: 'day', count: '60', strategies: normalizedStrategies, taskId: 'stock-copilot-dev-strategy' })
    const newsParams = new URLSearchParams({ symbol: normalizedSymbol, level: 'stock', taskId: 'stock-copilot-dev-news' })
    const searchParams = new URLSearchParams({ q: normalizedQuery, trustedOnly: 'true', taskId: 'stock-copilot-dev-search' })

    const [replayResult, klineResult, minuteResult, strategyResult, newsResult, searchResult] = await Promise.all([
      fetchJson(`/api/stocks/agents/replay/baseline?${params.toString()}`),
      fetchJson(`/api/stocks/mcp/kline?${klineParams.toString()}`),
      fetchJson(`/api/stocks/mcp/minute?${minuteParams.toString()}`),
      fetchJson(`/api/stocks/mcp/strategy?${strategyParams.toString()}`),
      fetchJson(`/api/stocks/mcp/news?${newsParams.toString()}`),
      fetchJson(`/api/stocks/mcp/search?${searchParams.toString()}`)
    ])

    replay.value = replayResult
    kline.value = klineResult
    minute.value = minuteResult
    strategy.value = strategyResult
    news.value = newsResult
    search.value = searchResult
    lastLoadedAt.value = new Date().toLocaleString('zh-CN', { hour12: false })
  } catch (error) {
    errorMessage.value = error.message || '加载失败'
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  loadDashboard()
})
</script>

<template>
  <section class="developer-mode-page">
    <header class="hero">
      <div class="hero-copy">
        <p class="eyebrow">Stock Copilot Runtime</p>
        <h2>股票 Copilot 开发模式</h2>
        <p class="muted">直接打通 replay、K 线、分时、策略、新闻、搜索六类接口，验证返回 shape、降级标记和 evidence/features 输出。</p>
      </div>
      <div class="hero-meta">
        <span class="status" :class="loading ? 'loading' : 'idle'">{{ loading ? '加载中' : '就绪' }}</span>
        <span v-if="lastLoadedAt" class="muted">上次刷新：{{ lastLoadedAt }}</span>
      </div>
    </header>

    <section class="signal-strip">
      <article v-for="item in runtimeHighlights" :key="item.key" class="signal-card">
        <span class="signal-label">{{ item.label }}</span>
        <strong>{{ item.value }}</strong>
      </article>
    </section>

    <form class="toolbar" @submit.prevent="loadDashboard">
      <label>
        <span>股票代码</span>
        <input v-model="symbol" type="text" placeholder="sh600000" />
      </label>
      <label>
        <span>K 线窗口</span>
        <input v-model="klineCount" type="number" min="20" max="240" />
      </label>
      <label class="wide">
        <span>策略列表</span>
        <input v-model="strategyList" type="text" placeholder="ma,macd,rsi,kdj,vwap,td,breakout,gap" />
      </label>
      <label class="wide">
        <span>搜索查询</span>
        <input v-model="searchQuery" type="text" placeholder="浦发银行 最新公告" />
      </label>
      <button type="submit" class="primary" :disabled="loading">{{ loading ? '加载中...' : '刷新诊断' }}</button>
    </form>

    <section class="request-preview" aria-label="请求预览">
      <span class="preview-label">请求预览</span>
      <span v-for="item in requestHints" :key="item" class="preview-chip">{{ item }}</span>
    </section>

    <p v-if="errorMessage" class="error-banner">{{ errorMessage }}</p>

    <section class="summary-grid">
      <article v-for="card in diagnostics" :key="card.key" class="summary-card" :class="`state-${card.status}`">
        <div class="card-head">
          <h3>{{ card.title }}</h3>
          <span class="pill" :class="card.status">{{ card.status }}</span>
        </div>
        <ul>
          <li v-for="line in card.lines" :key="line">{{ line }}</li>
        </ul>
        <div v-if="card.meta.length" class="card-meta">
          <span v-for="item in card.meta" :key="`${card.key}-${item.label}-${item.value}`" class="meta-chip">
            <strong>{{ item.label }}</strong>
            <span>{{ item.value }}</span>
          </span>
        </div>
      </article>
    </section>

    <section class="json-grid">
      <article v-for="card in diagnostics" :key="`json-${card.key}`" class="json-card">
        <div class="card-head">
          <h3>{{ card.title }}</h3>
          <span class="pill" :class="card.status">{{ card.status }}</span>
        </div>
        <pre>{{ prettyJson(card.payload) }}</pre>
      </article>
    </section>
  </section>
</template>

<style scoped>
.developer-mode-page {
  display: flex;
  flex-direction: column;
  gap: 20px;
  color: #173247;
}

.hero {
  display: flex;
  justify-content: space-between;
  gap: 16px;
  padding: 24px;
  border-radius: 20px;
  background: linear-gradient(135deg, #0f2a43 0%, #1d5b79 55%, #f2c14e 130%);
  color: #f7fbff;
  box-shadow: 0 24px 50px rgba(15, 42, 67, 0.14);
}

.hero-copy {
  max-width: 760px;
}

.eyebrow {
  margin: 0 0 8px;
  font-size: 12px;
  letter-spacing: 0.12em;
  text-transform: uppercase;
  opacity: 0.78;
}

.hero h2 {
  margin: 0;
  font-size: 30px;
}

.hero .muted {
  color: rgba(247, 251, 255, 0.82);
  max-width: 62ch;
  line-height: 1.6;
}

.hero-meta {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 8px;
}

.status,
.pill {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 64px;
  padding: 4px 10px;
  border-radius: 999px;
  font-size: 12px;
  font-weight: 700;
  text-transform: uppercase;
}

.status.loading,
.pill.warn {
  background: rgba(242, 193, 78, 0.24);
  color: #593e00;
}

.status.idle,
.pill.ok {
  background: rgba(202, 255, 191, 0.24);
  color: #0f5132;
}

.pill.idle {
  background: rgba(255, 255, 255, 0.2);
  color: #234;
}

.signal-strip {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 14px;
}

.signal-card {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 16px 18px;
  border: 1px solid #d8e2ea;
  border-radius: 18px;
  background:
    radial-gradient(circle at top right, rgba(242, 193, 78, 0.16), transparent 36%),
    linear-gradient(180deg, #ffffff 0%, #f7fbfd 100%);
  box-shadow: 0 12px 24px rgba(18, 57, 84, 0.06);
}

.signal-label {
  font-size: 12px;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: #527289;
}

.signal-card strong {
  font-size: 22px;
  line-height: 1.2;
}

.toolbar {
  display: grid;
  grid-template-columns: repeat(5, minmax(0, 1fr));
  gap: 12px;
  align-items: end;
  padding: 18px;
  border: 1px solid #d7e3ec;
  border-radius: 20px;
  background: linear-gradient(180deg, #ffffff 0%, #f8fbfd 100%);
  box-shadow: 0 18px 36px rgba(18, 57, 84, 0.05);
}

.toolbar label {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.toolbar label span {
  font-size: 13px;
  font-weight: 700;
  color: #48657a;
}

.toolbar label.wide {
  grid-column: span 2;
}

.toolbar input {
  height: 40px;
  padding: 0 12px;
  border: 1px solid #bfd3e0;
  border-radius: 12px;
  background: #fff;
  color: #173247;
  transition: border-color 0.18s ease, box-shadow 0.18s ease, transform 0.18s ease;
}

.toolbar input:focus {
  outline: none;
  border-color: #1d5b79;
  box-shadow: 0 0 0 4px rgba(29, 91, 121, 0.14);
  transform: translateY(-1px);
}

.primary {
  height: 40px;
  border: none;
  border-radius: 12px;
  background: linear-gradient(135deg, #0f766e 0%, #155e75 100%);
  color: #fff;
  font-weight: 700;
  cursor: pointer;
  box-shadow: 0 14px 28px rgba(15, 118, 110, 0.22);
  transition: transform 0.18s ease, box-shadow 0.18s ease, opacity 0.18s ease;
}

.primary:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: 0 18px 34px rgba(15, 118, 110, 0.28);
}

.primary:disabled {
  opacity: 0.7;
  cursor: wait;
}

.request-preview {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  align-items: center;
  padding: 0 4px;
}

.preview-label {
  font-size: 12px;
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: #5d7d90;
}

.preview-chip,
.meta-chip {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 7px 10px;
  border: 1px solid #d7e3ec;
  border-radius: 999px;
  background: #fff;
  color: #365366;
  font-size: 12px;
}

.meta-chip strong {
  color: #1d3c52;
}

.error-banner {
  margin: 0;
  padding: 12px 14px;
  border-radius: 12px;
  background: #fff1f2;
  color: #9f1239;
}

.summary-grid,
.json-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 14px;
}

.summary-card,
.json-card {
  padding: 16px;
  border: 1px solid #d7e3ec;
  border-radius: 18px;
  background: linear-gradient(180deg, #ffffff 0%, #f9fbfc 100%);
  box-shadow: 0 16px 32px rgba(18, 57, 84, 0.05);
}

.summary-card.state-ok {
  border-color: #cde8dd;
}

.summary-card.state-warn {
  border-color: #f0ddaa;
  background: linear-gradient(180deg, #fffaf0 0%, #fff 100%);
}

.card-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
}

.summary-card h3,
.json-card h3 {
  margin: 0 0 10px;
}

.summary-card ul {
  margin: 0;
  padding-left: 18px;
}

.summary-card li {
  margin-bottom: 6px;
}

.card-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 14px;
}

.json-card pre {
  margin: 0;
  max-height: 320px;
  overflow: auto;
  white-space: pre-wrap;
  word-break: break-word;
  font-size: 12px;
  line-height: 1.5;
  color: #183046;
  padding-top: 8px;
}

@media (max-width: 1100px) {
  .signal-strip,
  .toolbar,
  .summary-grid,
  .json-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .toolbar label.wide {
    grid-column: span 2;
  }
}

@media (max-width: 700px) {
  .hero,
  .signal-strip,
  .toolbar,
  .summary-grid,
  .json-grid {
    grid-template-columns: 1fr;
  }

  .hero {
    flex-direction: column;
  }

  .hero h2 {
    font-size: 24px;
  }

  .hero-meta {
    align-items: flex-start;
  }

  .toolbar label.wide {
    grid-column: span 1;
  }
}
</style>