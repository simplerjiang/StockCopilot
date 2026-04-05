<script setup>
import { nextTick, onUnmounted, ref, watch } from 'vue'
import { dispose, init } from 'klinecharts'
import { fetchBackendGet } from './stockInfoTabRequestUtils'

const props = defineProps({
  visible: { type: Boolean, default: false },
  indexItem: { type: Object, default: null }
})

const emit = defineEmits(['close'])

const activeTab = ref('minute')
const loading = ref(false)
const error = ref('')
const noData = ref(false)
const minuteLoading = ref(false)
const minuteRef = ref(null)
const klineRef = ref(null)
let minuteChart = null
let klineChart = null
let abortController = null

const GLOBAL_INDEX_FALLBACK_NAMES = {
  hsi: '恒生指数', hstech: '恒生科技', n225: '日经225',
  ndx: '纳斯达克', spx: '标普500', ftse: '富时100', ks11: '韩国KOSPI'
}
const resolveIndexName = item => {
  if (!item) return ''
  if (item.name && item.name !== item.symbol && !/^[A-Za-z0-9_.]+$/.test(item.name)) return item.name
  const key = (item.symbol || '').replace(/^[a-z]+_/i, '').toLowerCase()
  return GLOBAL_INDEX_FALLBACK_NAMES[key] || item.name || item.symbol || '未知指数'
}

const MINUTE_STYLES = {
  grid: {
    horizontal: { show: true, color: 'rgba(148, 163, 184, 0.12)', style: 'dashed', size: 1, dashedValue: [4, 4] },
    vertical: { show: true, color: 'rgba(148, 163, 184, 0.12)', style: 'dashed', size: 1, dashedValue: [4, 4] }
  },
  candle: {
    type: 'area',
    area: {
      lineSize: 2, lineColor: '#2563eb', value: 'close', smooth: true,
      backgroundColor: [
        { offset: 0, color: 'rgba(37, 99, 235, 0.18)' },
        { offset: 1, color: 'rgba(37, 99, 235, 0.04)' }
      ],
      point: { show: false }
    },
    bar: { compareRule: 'previous_close', upColor: '#ef4444', downColor: '#22c55e', noChangeColor: '#94a3b8' },
    tooltip: { showRule: 'none' },
    priceMark: { high: { show: false }, low: { show: false }, last: { show: false } }
  },
  indicator: {
    tooltip: { showRule: 'none' },
    bars: [{ upColor: '#ef4444', downColor: '#22c55e', noChangeColor: '#94a3b8' }]
  },
  xAxis: { axisLine: { show: false }, tickLine: { show: false }, tickText: { color: '#94a3b8', size: 10 } },
  yAxis: { axisLine: { show: false }, tickLine: { show: false }, tickText: { color: '#94a3b8', size: 10 } },
  crosshair: {
    horizontal: { line: { show: true, color: '#334155', style: 'dashed', size: 1, dashedValue: [4, 4] }, text: { show: true, color: '#e2e8f0', backgroundColor: '#1e293b' } },
    vertical: { line: { show: true, color: '#334155', style: 'dashed', size: 1, dashedValue: [4, 4] }, text: { show: true, color: '#e2e8f0', backgroundColor: '#1e293b' } }
  },
  separator: { color: 'rgba(148, 163, 184, 0.18)' }
}

const KLINE_STYLES = {
  ...MINUTE_STYLES,
  candle: {
    type: 'candle_solid',
    bar: {
      compareRule: 'previous_close',
      upColor: '#ef4444', downColor: '#22c55e', noChangeColor: '#94a3b8',
      upBorderColor: '#ef4444', downBorderColor: '#22c55e', noChangeBorderColor: '#94a3b8',
      upWickColor: '#ef4444', downWickColor: '#22c55e', noChangeWickColor: '#94a3b8'
    },
    tooltip: { showRule: 'none' },
    priceMark: { high: { show: false }, low: { show: false }, last: { show: false } }
  }
}

const parseDateStr = raw => {
  if (!raw) return null
  const str = String(raw).trim()
  if (/^\d{8}$/.test(str)) {
    return new Date(Number(str.slice(0, 4)), Number(str.slice(4, 6)) - 1, Number(str.slice(6, 8))).getTime()
  }
  const src = str.includes('T') ? str.split('T')[0] : str
  const parts = src.split('-').map(Number)
  if (parts.length === 3 && parts.every(Number.isFinite)) {
    return new Date(parts[0], parts[1] - 1, parts[2]).getTime()
  }
  return null
}

const parseDateTimeStr = (dateRaw, timeRaw) => {
  if (!dateRaw) return null
  const str = String(dateRaw).trim()
  let y, m, d
  if (/^\d{8}$/.test(str)) {
    y = Number(str.slice(0, 4)); m = Number(str.slice(4, 6)) - 1; d = Number(str.slice(6, 8))
  } else {
    const src = str.includes('T') ? str.split('T')[0] : str
    const parts = src.split('-').map(Number)
    if (parts.length !== 3 || !parts.every(Number.isFinite)) return null
    ;[y, m, d] = [parts[0], parts[1] - 1, parts[2]]
  }
  let hh = 0, mm = 0
  if (timeRaw != null) {
    if (typeof timeRaw === 'number') {
      hh = Math.floor(timeRaw / 100); mm = timeRaw % 100
    } else {
      const tp = String(timeRaw).split(':').map(Number)
      if (tp.length >= 2) { hh = tp[0]; mm = tp[1] }
      else if (/^\d{3,4}$/.test(String(timeRaw).trim())) {
        const v = Number(timeRaw); hh = Math.floor(v / 100); mm = v % 100
      }
    }
  }
  return new Date(y, m, d, hh, mm, 0).getTime()
}

const buildKline = raw => {
  if (!Array.isArray(raw)) return []
  return raw.map(item => {
    const ts = parseDateStr(item?.date)
    const o = Number(item?.open), h = Number(item?.high), l = Number(item?.low), c = Number(item?.close)
    if (!ts || [o, h, l, c].some(v => !Number.isFinite(v))) return null
    return { timestamp: ts, open: o, high: h, low: l, close: c, volume: Number(item?.volume) || 0 }
  }).filter(Boolean).sort((a, b) => a.timestamp - b.timestamp)
}

const buildMinute = raw => {
  if (!Array.isArray(raw)) return []
  const records = raw.map(item => {
    const ts = parseDateTimeStr(item?.date, item?.time)
    const c = Number(item?.price ?? item?.close ?? item?.value)
    if (!ts || !Number.isFinite(c)) return null
    return { timestamp: ts, open: c, high: c, low: c, close: c, volume: Number(item?.volume) || 0 }
  }).filter(Boolean).sort((a, b) => a.timestamp - b.timestamp)

  for (let i = records.length - 1; i > 0; i--) {
    if (records[i].volume >= records[i - 1].volume) {
      records[i] = { ...records[i], volume: records[i].volume - records[i - 1].volume }
    }
  }

  for (let i = 1; i < records.length; i++) {
    const open = records[i - 1].close
    records[i] = {
      ...records[i],
      open,
      high: Math.max(open, records[i].close),
      low: Math.min(open, records[i].close)
    }
  }

  return records
}

const destroyCharts = () => {
  if (minuteChart) { dispose(minuteChart); minuteChart = null }
  if (klineChart) { dispose(klineChart); klineChart = null }
}

let cachedMinuteData = []
let cachedKlineData = []

const renderChart = (container, data, styles, periodType = 'minute') => {
  if (!container) return null
  const chart = init(container)
  if (!chart) return null
  chart.setStyles(styles)
  chart.setDataLoader({ getBars: ({ callback }) => callback(data, false) })
  chart.createIndicator({ name: 'VOL', shortName: 'VOL' }, false, { id: 'volume_pane', height: 60 })
  if (periodType !== 'minute') {
    chart.createIndicator({ name: 'MA', calcParams: [5, 10, 20] }, false, { id: 'candle_pane' })
  }
  chart.setSymbol({ ticker: 'INDEX', pricePrecision: 2, volumePrecision: 0 })
  chart.setPeriod({ type: periodType, span: 1 })
  chart.resetData()
  chart.scrollToRealTime?.(0)
  chart.resize()
  if (periodType === 'minute' && data.length > 0) {
    const basePrice = data[0]?.close
    if (Number.isFinite(basePrice)) {
      chart.createOverlay({
        name: 'priceLine',
        lock: true,
        points: [{ timestamp: data[0].timestamp, value: basePrice }],
        styles: {
          line: { color: '#94a3b8', style: 'dashed', size: 1 },
          text: { color: '#94a3b8', backgroundColor: 'rgba(15, 23, 42, 0.88)', borderColor: '#94a3b8' }
        }
      })
    }
  }
  return chart
}

const ensureActiveChart = async () => {
  await nextTick()
  if (activeTab.value === 'minute' && !minuteChart && minuteRef.value && cachedMinuteData.length) {
    minuteChart = renderChart(minuteRef.value, cachedMinuteData, MINUTE_STYLES, 'minute')
  }
  if (activeTab.value === 'kline' && !klineChart && klineRef.value && cachedKlineData.length) {
    klineChart = renderChart(klineRef.value, cachedKlineData, KLINE_STYLES, 'day')
  }
  if (activeTab.value === 'minute') minuteChart?.resize()
  if (activeTab.value === 'kline') klineChart?.resize()
}

const fetchAndRender = async () => {
  if (!props.indexItem?.symbol) return

  destroyCharts()
  cachedMinuteData = []
  cachedKlineData = []
  loading.value = true
  error.value = ''
  noData.value = false

  abortController?.abort()
  abortController = new AbortController()
  const timeoutId = setTimeout(() => abortController?.abort(), 30000)
  const signal = abortController.signal

  try {
    // Phase 1: fetch K-line only (fast, often cached)
    const klineParams = new URLSearchParams({
      symbol: props.indexItem.symbol,
      interval: 'day',
      count: '30',
      includeQuote: 'false',
      includeMinute: 'false'
    })
    const klineResponse = await fetchBackendGet(`/api/stocks/chart?${klineParams}`, { signal })
    if (!klineResponse.ok) throw new Error('加载图表数据失败')
    const klineData = await klineResponse.json()
    cachedKlineData = buildKline(klineData?.kLines ?? klineData?.KLines ?? [])

    // Show K-line immediately if available — auto-switch to 日K so user sees data
    if (cachedKlineData.length) {
      loading.value = false
      activeTab.value = 'kline'
      minuteLoading.value = true
      await ensureActiveChart()
    }

    // Phase 2: fetch minute data (may be slower on cold start)
    const minuteParams = new URLSearchParams({
      symbol: props.indexItem.symbol,
      interval: 'day',
      count: '1',
      includeQuote: 'false',
      includeMinute: 'true'
    })
    const minuteResponse = await fetchBackendGet(`/api/stocks/chart?${minuteParams}`, { signal })
    if (minuteResponse.ok) {
      const minuteData = await minuteResponse.json()
      cachedMinuteData = buildMinute(minuteData?.minuteLines ?? minuteData?.MinuteLines ?? [])
      minuteLoading.value = false
      if (cachedMinuteData.length) {
        await ensureActiveChart()
      }
    } else {
      minuteLoading.value = false
    }

    if (!cachedKlineData.length && !cachedMinuteData.length) {
      noData.value = true
      loading.value = false
      return
    }
    loading.value = false
  } catch (err) {
    minuteLoading.value = false
    if (err?.name === 'AbortError') {
      error.value = '图表加载超时，请稍后重试'
      loading.value = false
      return
    }
    error.value = err.message || '加载失败'
  } finally {
    clearTimeout(timeoutId)
    loading.value = false
    minuteLoading.value = false
  }
}

const close = () => emit('close')
const onOverlayClick = e => { if (e.target === e.currentTarget) close() }
const onKeydown = e => { if (e.key === 'Escape') close() }

watch(() => props.visible, async val => {
  if (val) {
    activeTab.value = 'minute'
    noData.value = false
    await nextTick()
    fetchAndRender()
    window.addEventListener('keydown', onKeydown)
  } else {
    destroyCharts()
    abortController?.abort()
    window.removeEventListener('keydown', onKeydown)
  }
})

watch(activeTab, () => {
  ensureActiveChart()
})

onUnmounted(() => {
  destroyCharts()
  abortController?.abort()
  window.removeEventListener('keydown', onKeydown)
})
</script>

<template>
  <Teleport to="body">
    <div v-if="visible" class="chart-dialog-overlay" @click="onOverlayClick">
      <div class="chart-dialog" @click.stop>
        <div class="chart-dialog-header">
          <div class="chart-dialog-header-info">
            <div class="dialog-index-title">
              <span class="dialog-index-icon">{{ indexItem?.icon ?? '📈' }}</span>
              <strong class="dialog-index-name">{{ resolveIndexName(indexItem) }}</strong>
              <small class="dialog-index-symbol">{{ indexItem?.symbol ?? '' }}</small>
            </div>
            <div v-if="indexItem" class="dialog-index-price-row">
              <strong
                class="dialog-index-price"
                :class="{ positive: (indexItem.changePercent ?? 0) >= 0, negative: (indexItem.changePercent ?? 0) < 0 }"
              >{{ indexItem.price?.toFixed(2) ?? '-' }}</strong>
              <span
                class="dialog-index-change"
                :class="{ positive: (indexItem.changePercent ?? 0) >= 0, negative: (indexItem.changePercent ?? 0) < 0 }"
              >
                {{ (indexItem.change ?? 0) >= 0 ? '+' : '' }}{{ indexItem.change?.toFixed(2) ?? '' }}
                ({{ (indexItem.changePercent ?? 0) >= 0 ? '+' : '' }}{{ indexItem.changePercent?.toFixed(2) ?? '' }}%)
              </span>
            </div>
          </div>
          <button class="chart-dialog-close" @click="close" title="关闭">✕</button>
        </div>

        <div class="chart-dialog-tabs">
          <button class="chart-tab" :class="{ active: activeTab === 'minute' }" @click="activeTab = 'minute'">分时图</button>
          <button class="chart-tab" :class="{ active: activeTab === 'kline' }" @click="activeTab = 'kline'">日K</button>
        </div>

        <div class="chart-dialog-canvas">
          <p v-if="loading" class="chart-feedback">加载图表数据中...</p>
          <div v-else-if="error" class="chart-feedback chart-error">
            <p>{{ error }}</p>
            <button class="chart-retry-btn" @click="fetchAndRender">重试</button>
          </div>
          <p v-else-if="noData" class="chart-feedback chart-no-data">暂无图表数据</p>
          <template v-else>
            <p v-if="activeTab === 'minute' && minuteLoading" class="chart-feedback">分时数据加载中...</p>
            <div ref="minuteRef" class="chart-container" :style="{ display: activeTab === 'minute' && !minuteLoading ? 'block' : 'none' }"></div>
            <div ref="klineRef" class="chart-container" :style="{ display: activeTab === 'kline' ? 'block' : 'none' }"></div>
          </template>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.chart-dialog-overlay {
  position: fixed;
  inset: 0;
  z-index: 500;
  background: var(--color-bg-overlay, rgba(15, 23, 42, 0.55));
  display: flex;
  align-items: center;
  justify-content: center;
  animation: dialogFadeIn 200ms ease;
}

.chart-dialog {
  width: min(640px, 90vw);
  max-height: 80vh;
  background: var(--color-bg-surface, #fff);
  border-radius: var(--radius-xl, 16px);
  border: 1px solid var(--color-border-light, #e5e7eb);
  box-shadow: 0 20px 50px rgba(15, 23, 42, 0.14);
  overflow: hidden;
  display: flex;
  flex-direction: column;
}

.chart-dialog-header {
  display: flex;
  justify-content: space-between;
  align-items: start;
  padding: var(--space-4, 16px) var(--space-5, 20px);
  border-bottom: 1px solid var(--color-border-light, #e5e7eb);
}

.chart-dialog-header-info { display: grid; gap: var(--space-1, 4px); }
.dialog-index-title { display: flex; align-items: center; gap: var(--space-2, 8px); }
.dialog-index-icon { font-size: var(--text-lg, 15px); }
.dialog-index-name { font-size: var(--text-lg, 15px); font-weight: 700; color: var(--color-text-primary, #0f172a); }
.dialog-index-symbol { font-size: var(--text-xs, 11px); color: var(--color-text-muted, #94a3b8); }
.dialog-index-price-row { display: flex; align-items: baseline; gap: var(--space-2, 8px); }
.dialog-index-price { font-size: var(--text-2xl, 20px); font-weight: 700; font-variant-numeric: tabular-nums; }
.dialog-index-change { font-size: var(--text-sm, 12px); font-weight: 600; font-variant-numeric: tabular-nums; }

.chart-dialog-close {
  border: none;
  background: var(--color-bg-surface-alt, #f8f9fb);
  border-radius: 9999px;
  width: 28px; height: 28px;
  cursor: pointer;
  font-size: var(--text-md, 14px);
  color: var(--color-text-secondary, #475569);
  display: flex; align-items: center; justify-content: center;
}
.chart-dialog-close:hover {
  background: var(--color-bg-inset, #f0f2f5);
  color: var(--color-text-primary, #0f172a);
}

.chart-dialog-tabs {
  display: flex;
  border-bottom: 1px solid var(--color-border-light, #e5e7eb);
}

.chart-tab {
  padding: var(--space-2, 8px) var(--space-4, 16px);
  font-size: var(--text-sm, 12px);
  color: var(--color-text-secondary, #475569);
  cursor: pointer;
  border: none;
  background: none;
  border-bottom: 2px solid transparent;
  transition: all 150ms;
}
.chart-tab.active {
  color: var(--color-accent, #2563eb);
  border-bottom-color: var(--color-accent, #2563eb);
  font-weight: 600;
}
.chart-tab:hover:not(.active) {
  color: var(--color-text-primary, #0f172a);
  background: var(--color-accent-subtle, rgba(37, 99, 235, 0.08));
}

.chart-dialog-canvas {
  flex: 1;
  min-height: 320px;
  padding: var(--space-3, 12px);
  background: var(--color-bg-surface, #fff);
}

.chart-container {
  width: 100%;
  height: 300px;
}

.chart-feedback {
  text-align: center;
  padding: var(--space-8, 32px);
  color: var(--color-text-muted, #94a3b8);
  font-size: var(--text-sm, 12px);
  margin: 0;
}
.chart-error { color: var(--color-danger, #dc2626); }
.chart-no-data { color: var(--color-text-muted, #94a3b8); }

.chart-retry-btn {
  margin-top: 8px;
  padding: 6px 20px;
  background: var(--color-brand, #2563eb);
  color: #fff;
  border: none;
  border-radius: 6px;
  cursor: pointer;
  font-size: 13px;
  transition: opacity 0.15s;
}
.chart-retry-btn:hover {
  opacity: 0.85;
}

.positive { color: var(--color-market-rise, #ef4444); }
.negative { color: var(--color-market-fall, #16a34a); }

@keyframes dialogFadeIn {
  from { opacity: 0; transform: scale(0.95); }
  to { opacity: 1; transform: scale(1); }
}

@media (max-width: 720px) {
  .chart-dialog { width: 95vw; max-height: 85vh; }
}
</style>
