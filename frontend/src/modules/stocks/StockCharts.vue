<script setup>
import { onMounted, onUnmounted, ref, watch, nextTick } from 'vue'
import { createChart, ColorType, CandlestickSeries, HistogramSeries, AreaSeries, LineSeries } from 'lightweight-charts'

const props = defineProps({
  kLines: {
    type: Array,
    default: () => []
  },
  minuteLines: {
    type: Array,
    default: () => []
  },
  basePrice: {
    type: Number,
    default: null
  },
  interval: {
    type: String,
    default: 'day'
  },
  aiLevels: {
    type: Object,
    default: null
  }
})

const emit = defineEmits(['update:interval'])

const klineRef = ref(null)
const minuteRef = ref(null)
const klineWrapperRef = ref(null)
const minuteWrapperRef = ref(null)
const klineChartRef = ref(null)
const minuteChartRef = ref(null)
const klineSeriesRef = ref(null)
const klineVolumeSeriesRef = ref(null)
const klineMa5SeriesRef = ref(null)
const klineMa10SeriesRef = ref(null)
const klineResistanceSeriesRef = ref(null)
const klineSupportSeriesRef = ref(null)
const minuteSeriesRef = ref(null)
const minuteVolumeSeriesRef = ref(null)
const minuteResistanceSeriesRef = ref(null)
const minuteSupportSeriesRef = ref(null)
let resizeObserver = null
let resizeHandler = null
let resizeFrame = null
const klineValues = ref([])
const minuteValues = ref([])
const minuteBasePrice = ref(null)
const minuteBaseLineRef = ref(null)
const klineHover = ref({ visible: false, x: 0, y: 0, lines: [] })
const minuteHover = ref({ visible: false, x: 0, y: 0, lines: [] })

const parseLevelValue = value => {
  const number = Number(value)
  return Number.isFinite(number) ? number : null
}

const aiResistance = () => parseLevelValue(props.aiLevels?.resistance)
const aiSupport = () => parseLevelValue(props.aiLevels?.support)

const aiLevelText = () => {
  const resistance = aiResistance()
  const support = aiSupport()
  if (!Number.isFinite(resistance) && !Number.isFinite(support)) return ''
  const parts = []
  if (Number.isFinite(resistance)) parts.push(`突破线 ${resistance}`)
  if (Number.isFinite(support)) parts.push(`支撑线 ${support}`)
  return `AI线：${parts.join(' / ')}`
}

const buildKlineLevelData = value => {
  if (!Number.isFinite(value) || !klineValues.value.length) return []
  const first = klineValues.value[0]
  const last = klineValues.value[klineValues.value.length - 1]
  if (!first?.datePart || !last?.datePart) return []
  return [
    { time: { year: first.datePart.year, month: first.datePart.month, day: first.datePart.day }, value },
    { time: { year: last.datePart.year, month: last.datePart.month, day: last.datePart.day }, value }
  ]
}

const buildMinuteLevelData = value => {
  if (!Number.isFinite(value) || !minuteValues.value.length) return []
  const first = minuteValues.value[0]
  const last = minuteValues.value[minuteValues.value.length - 1]
  if (!Number.isFinite(first?.time) || !Number.isFinite(last?.time)) return []
  return [
    { time: first.time, value },
    { time: last.time, value }
  ]
}

const pad = value => String(value).padStart(2, '0')

const formatTimeLabel = time => {
  if (!time) return ''
  if (typeof time === 'number' && Number.isFinite(time)) {
    const date = new Date(time * 1000)
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`
  }
  if (typeof time === 'string') {
    return time
  }
  if (typeof time === 'object' && time.year && time.month && time.day) {
    const hour = Number.isFinite(time.hour) ? ` ${pad(time.hour)}:${pad(time.minute ?? 0)}` : ''
    return `${time.year}-${pad(time.month)}-${pad(time.day)}${hour}`
  }
  return ''
}

const parseDatePart = raw => {
  if (!raw) return null
  if (raw instanceof Date && !Number.isNaN(raw.getTime())) {
    return { year: raw.getFullYear(), month: raw.getMonth() + 1, day: raw.getDate() }
  }
  if (typeof raw !== 'string') return null
  const trimmed = raw.trim()
  if (!trimmed) return null
  if (/^\d{8}$/.test(trimmed)) {
    return {
      year: Number(trimmed.slice(0, 4)),
      month: Number(trimmed.slice(4, 6)),
      day: Number(trimmed.slice(6, 8))
    }
  }
  const source = trimmed.includes('T') ? trimmed.split('T')[0] : trimmed
  const parts = source.split('-').map(Number)
  if (parts.length === 3 && parts.every(Number.isFinite)) {
    return { year: parts[0], month: parts[1], day: parts[2] }
  }
  return null
}

const parseTimePart = raw => {
  if (raw == null) return null
  if (typeof raw === 'number' && Number.isFinite(raw)) {
    return { hour: Math.floor(raw / 100), minute: raw % 100, second: 0 }
  }
  if (raw instanceof Date && !Number.isNaN(raw.getTime())) {
    return { hour: raw.getHours(), minute: raw.getMinutes(), second: raw.getSeconds() }
  }
  if (typeof raw !== 'string') return null
  const trimmed = raw.trim()
  if (!trimmed) return null
  const parts = trimmed.split(':').map(Number)
  if (parts.length >= 2 && parts.every(Number.isFinite)) {
    return { hour: parts[0], minute: parts[1], second: parts[2] ?? 0 }
  }
  if (/^\d{3,4}$/.test(trimmed) && Number.isFinite(Number(trimmed))) {
    const hhmm = Number(trimmed)
    return { hour: Math.floor(hhmm / 100), minute: hhmm % 100, second: 0 }
  }
  return null
}

const parseVolumeValue = raw => {
  if (raw == null) return 0
  if (typeof raw === 'number' && Number.isFinite(raw)) return raw
  const text = String(raw).trim()
  if (!text) return 0
  const normalized = text.replace(/,/g, '')
  if (/[万亿]$/.test(normalized)) {
    const unit = normalized.slice(-1)
    const base = Number(normalized.slice(0, -1))
    if (!Number.isFinite(base)) return 0
    if (unit === '万') return base * 10000
    if (unit === '亿') return base * 100000000
  }
  const value = Number(normalized)
  return Number.isFinite(value) ? value : 0
}

const calculateMovingAverage = (records, period) => {
  if (!Array.isArray(records) || period <= 0) return []
  let rollingSum = 0
  return records.map((item, index) => {
    const close = Number(item?.close)
    if (!Number.isFinite(close)) return null
    rollingSum += close
    if (index >= period) {
      const outClose = Number(records[index - period]?.close)
      if (Number.isFinite(outClose)) {
        rollingSum -= outClose
      }
    }
    return index >= period - 1 ? Number((rollingSum / period).toFixed(4)) : null
  })
}

const applyChartSize = (container, chart, fallbackTarget = null) => {
  if (!container || !chart) return
  const width = Math.max(1, Math.floor(container.clientWidth || fallbackTarget?.clientWidth || 1))
  const height = Math.max(1, Math.floor(container.clientHeight || fallbackTarget?.clientHeight || 1))
  chart.applyOptions({ width, height })
}

const resizeCharts = () => {
  applyChartSize(klineRef.value, klineChartRef.value, klineWrapperRef.value)
  applyChartSize(minuteRef.value, minuteChartRef.value, minuteWrapperRef.value)
}

const queueResize = () => {
  if (typeof window === 'undefined') {
    resizeCharts()
    return
  }
  if (resizeFrame) {
    window.cancelAnimationFrame(resizeFrame)
  }
  resizeFrame = window.requestAnimationFrame(() => {
    resizeFrame = null
    resizeCharts()
  })
}

const ensureKlineChart = () => {
  if (!klineRef.value || klineChartRef.value) return
  if (typeof window !== 'undefined' && typeof window.matchMedia !== 'function') return
  const chart = createChart(klineRef.value, {
    width: Math.max(1, Math.floor(klineRef.value.clientWidth || 1)),
    height: Math.max(1, Math.floor(klineRef.value.clientHeight || 1)),
    layout: {
      background: { type: ColorType.Solid, color: 'transparent' },
      textColor: '#94a3b8'
    },
    rightPriceScale: {
      borderVisible: false,
      scaleMargins: { top: 0.08, bottom: 0.28 }
    },
    timeScale: {
      borderVisible: false,
      timeVisible: false
    },
    grid: {
      vertLines: { color: 'rgba(148, 163, 184, 0.12)' },
      horzLines: { color: 'rgba(148, 163, 184, 0.12)' }
    },
    crosshair: {
      vertLine: { labelBackgroundColor: '#334155' },
      horzLine: { labelBackgroundColor: '#334155' }
    }
  })

  const candleSeries = chart.addSeries(CandlestickSeries, {
    upColor: '#ef4444',
    downColor: '#22c55e',
    wickUpColor: '#ef4444',
    wickDownColor: '#22c55e',
    borderVisible: false,
    priceLineVisible: false
  })

  const volumeSeries = chart.addSeries(HistogramSeries, {
    priceScaleId: 'volume',
    priceFormat: { type: 'volume' }
  })

  const ma5Series = chart.addSeries(LineSeries, {
    color: '#f59e0b',
    lineWidth: 1,
    priceLineVisible: false,
    lastValueVisible: false,
    title: 'MA5'
  })

  const ma10Series = chart.addSeries(LineSeries, {
    color: '#a855f7',
    lineWidth: 1,
    priceLineVisible: false,
    lastValueVisible: false,
    title: 'MA10'
  })

  const resistanceSeries = chart.addSeries(LineSeries, {
    color: '#f97316',
    lineWidth: 1,
    lineStyle: 2,
    priceLineVisible: false,
    lastValueVisible: true,
    title: '突破线'
  })

  const supportSeries = chart.addSeries(LineSeries, {
    color: '#10b981',
    lineWidth: 1,
    lineStyle: 2,
    priceLineVisible: false,
    lastValueVisible: true,
    title: '支撑线'
  })

  chart.priceScale('volume').applyOptions({
    borderVisible: false,
    scaleMargins: { top: 0.78, bottom: 0 }
  })

  chart.subscribeCrosshairMove(param => {
    if (!param?.point || !param.time || !klineRef.value) {
      klineHover.value.visible = false
      return
    }
    const candle = param.seriesData?.get(candleSeries)
    if (!candle) {
      klineHover.value.visible = false
      return
    }
    const index = klineValues.value.findIndex(item => item.timeKey === formatTimeLabel(param.time))
    const prevClose = index > 0 ? Number(klineValues.value[index - 1]?.close) : NaN
    const closePrice = Number(candle.close)
    const ma5 = Number(klineValues.value[index]?.ma5)
    const ma10 = Number(klineValues.value[index]?.ma10)
    const changePercent = Number.isFinite(prevClose) && prevClose !== 0 && Number.isFinite(closePrice)
      ? (((closePrice - prevClose) / prevClose) * 100).toFixed(2)
      : '-'

    const pointX = Math.max(8, Math.min(param.point.x + 12, (klineRef.value.clientWidth || 200) - 120))
    const pointY = Math.max(8, Math.min(param.point.y + 12, (klineRef.value.clientHeight || 200) - 120))
    klineHover.value = {
      visible: true,
      x: pointX,
      y: pointY,
      lines: [
        formatTimeLabel(param.time),
        `开: ${candle.open}`,
        `收: ${candle.close}`,
        `高: ${candle.high}`,
        `低: ${candle.low}`,
        `成交量: ${Math.round(Number(candle.volume ?? 0))}`,
        `MA5: ${Number.isFinite(ma5) ? ma5 : '-'}`,
        `MA10: ${Number.isFinite(ma10) ? ma10 : '-'}`,
        `涨跌幅: ${changePercent}%`
      ]
    }
  })

  klineChartRef.value = chart
  klineSeriesRef.value = candleSeries
  klineVolumeSeriesRef.value = volumeSeries
  klineMa5SeriesRef.value = ma5Series
  klineMa10SeriesRef.value = ma10Series
  klineResistanceSeriesRef.value = resistanceSeries
  klineSupportSeriesRef.value = supportSeries
}

const ensureMinuteChart = () => {
  if (!minuteRef.value || minuteChartRef.value) return
  if (typeof window !== 'undefined' && typeof window.matchMedia !== 'function') return
  const chart = createChart(minuteRef.value, {
    width: Math.max(1, Math.floor(minuteRef.value.clientWidth || 1)),
    height: Math.max(1, Math.floor(minuteRef.value.clientHeight || 1)),
    layout: {
      background: { type: ColorType.Solid, color: 'transparent' },
      textColor: '#94a3b8'
    },
    rightPriceScale: {
      borderVisible: false,
      scaleMargins: { top: 0.08, bottom: 0.28 }
    },
    timeScale: {
      borderVisible: false,
      timeVisible: true,
      secondsVisible: false
    },
    grid: {
      vertLines: { color: 'rgba(148, 163, 184, 0.12)' },
      horzLines: { color: 'rgba(148, 163, 184, 0.12)' }
    }
  })

  const minuteSeries = chart.addSeries(AreaSeries, {
    lineColor: '#2563eb',
    topColor: 'rgba(37, 99, 235, 0.28)',
    bottomColor: 'rgba(37, 99, 235, 0.05)',
    lineWidth: 2,
    priceLineVisible: true
  })

  const minuteVolumeSeries = chart.addSeries(HistogramSeries, {
    priceScaleId: 'volume',
    priceFormat: { type: 'volume' }
  })

  const minuteResistanceSeries = chart.addSeries(LineSeries, {
    color: '#f97316',
    lineWidth: 1,
    lineStyle: 2,
    priceLineVisible: false,
    lastValueVisible: true,
    title: '突破线'
  })

  const minuteSupportSeries = chart.addSeries(LineSeries, {
    color: '#10b981',
    lineWidth: 1,
    lineStyle: 2,
    priceLineVisible: false,
    lastValueVisible: true,
    title: '支撑线'
  })

  chart.priceScale('volume').applyOptions({
    borderVisible: false,
    scaleMargins: { top: 0.78, bottom: 0 }
  })

  chart.subscribeCrosshairMove(param => {
    if (!param?.point || !param.time || !minuteRef.value) {
      minuteHover.value.visible = false
      return
    }
    const lineItem = param.seriesData?.get(minuteSeries)
    if (!lineItem || !Number.isFinite(Number(lineItem.value))) {
      minuteHover.value.visible = false
      return
    }

    const value = Number(lineItem.value)
    const base = minuteBasePrice.value
    const volume = Number(minuteValues.value.find(item => item.time === param.time)?.volume)
    const changePercent = Number.isFinite(base) && base !== 0
      ? (((value - base) / base) * 100).toFixed(2)
      : '-'

    const pointX = Math.max(8, Math.min(param.point.x + 12, (minuteRef.value.clientWidth || 200) - 120))
    const pointY = Math.max(8, Math.min(param.point.y + 12, (minuteRef.value.clientHeight || 200) - 96))
    minuteHover.value = {
      visible: true,
      x: pointX,
      y: pointY,
      lines: [
        formatTimeLabel(param.time),
        `${value}`,
        `成交量: ${Number.isFinite(volume) ? Math.round(volume) : '-'}`,
        `涨跌幅: ${changePercent}%`
      ]
    }
  })

  minuteChartRef.value = chart
  minuteSeriesRef.value = minuteSeries
  minuteVolumeSeriesRef.value = minuteVolumeSeries
  minuteResistanceSeriesRef.value = minuteResistanceSeries
  minuteSupportSeriesRef.value = minuteSupportSeries
}

const renderKLine = () => {
  ensureKlineChart()
  if (!klineSeriesRef.value || !klineVolumeSeriesRef.value || !klineMa5SeriesRef.value || !klineMa10SeriesRef.value || !klineResistanceSeriesRef.value || !klineSupportSeriesRef.value) return

  const normalized = props.kLines
    .map(item => {
      const datePart = parseDatePart(item?.date ?? item?.Date)
      if (!datePart) return null
      const open = Number(item?.open ?? item?.Open)
      const close = Number(item?.close ?? item?.Close)
      const low = Number(item?.low ?? item?.Low)
      const high = Number(item?.high ?? item?.High)
      const volume = parseVolumeValue(item?.volume ?? item?.Volume ?? item?.amount ?? item?.Amount ?? 0)
      const timeKey = `${datePart.year}-${pad(datePart.month)}-${pad(datePart.day)}`
      return {
        datePart,
        timeKey,
        sortKey: Date.UTC(datePart.year, datePart.month - 1, datePart.day),
        open,
        close,
        low,
        high,
        volume: Number.isFinite(volume) ? volume : 0
      }
    })
    .filter(item => item && [item.open, item.close, item.low, item.high].every(Number.isFinite))
    .sort((a, b) => a.sortKey - b.sortKey)

  if (!normalized.length) {
    klineValues.value = []
    klineSeriesRef.value.setData([])
    klineVolumeSeriesRef.value.setData([])
    klineMa5SeriesRef.value.setData([])
    klineMa10SeriesRef.value.setData([])
    klineResistanceSeriesRef.value.setData([])
    klineSupportSeriesRef.value.setData([])
    return
  }

  const ma5Values = calculateMovingAverage(normalized, 5)
  const ma10Values = calculateMovingAverage(normalized, 10)
  const withIndicators = normalized.map((item, index) => ({
    ...item,
    ma5: ma5Values[index],
    ma10: ma10Values[index]
  }))

  klineValues.value = withIndicators
  const candleData = withIndicators.map(item => ({
    time: { year: item.datePart.year, month: item.datePart.month, day: item.datePart.day },
    open: item.open,
    high: item.high,
    low: item.low,
    close: item.close,
    volume: item.volume
  }))

  const volumeData = withIndicators.map(item => ({
    time: { year: item.datePart.year, month: item.datePart.month, day: item.datePart.day },
    value: item.volume,
    color: item.close >= item.open ? 'rgba(239,68,68,0.9)' : 'rgba(34,197,94,0.9)'
  }))

  const ma5Data = withIndicators
    .filter(item => Number.isFinite(item.ma5))
    .map(item => ({
      time: { year: item.datePart.year, month: item.datePart.month, day: item.datePart.day },
      value: item.ma5
    }))

  const ma10Data = withIndicators
    .filter(item => Number.isFinite(item.ma10))
    .map(item => ({
      time: { year: item.datePart.year, month: item.datePart.month, day: item.datePart.day },
      value: item.ma10
    }))

  klineSeriesRef.value.setData(candleData)
  klineVolumeSeriesRef.value.setData(volumeData)
  klineMa5SeriesRef.value.setData(ma5Data)
  klineMa10SeriesRef.value.setData(ma10Data)
  klineResistanceSeriesRef.value.setData(buildKlineLevelData(aiResistance()))
  klineSupportSeriesRef.value.setData(buildKlineLevelData(aiSupport()))
  klineChartRef.value?.timeScale().fitContent()
}

const renderMinute = () => {
  ensureMinuteChart()
  if (!minuteSeriesRef.value || !minuteVolumeSeriesRef.value || !minuteResistanceSeriesRef.value || !minuteSupportSeriesRef.value) return

  const data = props.minuteLines.map(item => {
    const datePart = parseDatePart(item?.date ?? item?.Date)
    const timePart = parseTimePart(item?.time ?? item?.Time)
    const price = Number(item?.price ?? item?.Price)
    const volume = parseVolumeValue(item?.volume ?? item?.Volume ?? item?.amount ?? item?.Amount ?? 0)
    if (!datePart || !timePart || !Number.isFinite(price)) return null
    const { year, month, day } = datePart
    const { hour, minute, second } = timePart
    if (![year, month, day, hour, minute, second].every(Number.isFinite)) return null
    const timestamp = Math.floor(new Date(year, month - 1, day, hour, minute, second).getTime() / 1000)
    return Number.isFinite(timestamp)
      ? { time: timestamp, value: price, volume: Number.isFinite(volume) ? volume : 0 }
      : null
  })
    .filter(Boolean)
    .sort((a, b) => a.time - b.time)
  minuteValues.value = data
  const basePrice = Number.isFinite(props.basePrice) ? props.basePrice : null
  minuteBasePrice.value = basePrice ?? (data.length > 0 ? data[0].value : null)

  if (!data.length) {
    minuteSeriesRef.value.setData([])
    minuteVolumeSeriesRef.value.setData([])
    minuteResistanceSeriesRef.value.setData([])
    minuteSupportSeriesRef.value.setData([])
    return
  }

  minuteSeriesRef.value.setData(data.map(item => ({ time: item.time, value: item.value })))

  const minuteVolumeData = data.map((item, index) => {
    const prev = index > 0 ? data[index - 1]?.value : item.value
    const color = item.value >= prev ? 'rgba(239,68,68,0.72)' : 'rgba(34,197,94,0.72)'
    return {
      time: item.time,
      value: item.volume,
      color
    }
  })
  minuteVolumeSeriesRef.value.setData(minuteVolumeData)
  minuteResistanceSeriesRef.value.setData(buildMinuteLevelData(aiResistance()))
  minuteSupportSeriesRef.value.setData(buildMinuteLevelData(aiSupport()))

  if (minuteBaseLineRef.value) {
    minuteSeriesRef.value.removePriceLine(minuteBaseLineRef.value)
    minuteBaseLineRef.value = null
  }
  if (Number.isFinite(minuteBasePrice.value)) {
    minuteBaseLineRef.value = minuteSeriesRef.value.createPriceLine({
      price: minuteBasePrice.value,
      color: 'rgba(148, 163, 184, 0.7)',
      lineStyle: 2,
      lineWidth: 1,
      axisLabelVisible: true,
      title: '昨收'
    })
  }
  minuteChartRef.value?.timeScale().fitContent()
}

const renderAll = () => {
  renderKLine()
  renderMinute()
}

onMounted(() => {
  ensureKlineChart()
  ensureMinuteChart()
  renderAll()
  if (typeof ResizeObserver !== 'undefined') {
    resizeObserver = new ResizeObserver(entries => {
      if (!entries?.length) return
      queueResize()
    })
    if (klineWrapperRef.value) {
      resizeObserver.observe(klineWrapperRef.value)
    }
    if (minuteWrapperRef.value) {
      resizeObserver.observe(minuteWrapperRef.value)
    }
    if (klineRef.value) {
      resizeObserver.observe(klineRef.value)
    }
    if (minuteRef.value) {
      resizeObserver.observe(minuteRef.value)
    }
  }
  resizeHandler = () => {
    queueResize()
  }
  window.addEventListener('resize', resizeHandler)
  nextTick(() => {
    queueResize()
  })
})

onUnmounted(() => {
  if (resizeHandler) {
    window.removeEventListener('resize', resizeHandler)
    resizeHandler = null
  }
  if (resizeFrame && typeof window !== 'undefined') {
    window.cancelAnimationFrame(resizeFrame)
    resizeFrame = null
  }
  if (resizeObserver) {
    resizeObserver.disconnect()
    resizeObserver = null
  }
  if (klineChartRef.value) {
    klineChartRef.value.remove()
    klineChartRef.value = null
  }
  if (minuteChartRef.value) {
    minuteChartRef.value.remove()
    minuteChartRef.value = null
  }
  klineMa5SeriesRef.value = null
  klineMa10SeriesRef.value = null
  klineResistanceSeriesRef.value = null
  klineSupportSeriesRef.value = null
  minuteVolumeSeriesRef.value = null
  minuteResistanceSeriesRef.value = null
  minuteSupportSeriesRef.value = null
  minuteBaseLineRef.value = null
})

watch(() => [props.kLines, props.minuteLines, props.basePrice, props.aiLevels], async () => {
  renderAll()
  await nextTick()
  queueResize()
})
</script>

<template>
  <div class="charts">
    <div ref="klineWrapperRef" class="chart-wrapper">
      <div class="chart-header">
        <h3>K 线图</h3>
        <div class="chart-tabs">
          <button
            v-for="item in ['day', 'week', 'month', 'year']"
            :key="item"
            class="tab"
            :class="{ active: interval === item }"
            @click="emit('update:interval', item)"
          >
            {{ item === 'day' ? '日线' : item === 'week' ? '周线' : item === 'month' ? '月线' : '年线' }}
          </button>
        </div>
      </div>
      <p v-if="aiLevelText()" class="ai-level-tip">{{ aiLevelText() }}</p>
      <div ref="klineRef" class="chart" v-show="kLines.length" />
      <p class="placeholder" v-show="!kLines.length">暂无 K 线数据</p>
      <div
        v-if="klineHover.visible"
        class="hover-tip"
        :style="{ left: `${klineHover.x}px`, top: `${klineHover.y}px` }"
      >
        <div v-for="(line, idx) in klineHover.lines" :key="idx">{{ line }}</div>
      </div>
    </div>
    <div ref="minuteWrapperRef" class="chart-wrapper">
      <h3>分时图</h3>
      <p v-if="aiLevelText()" class="ai-level-tip">{{ aiLevelText() }}</p>
      <div ref="minuteRef" class="chart" v-show="minuteLines.length" />
      <p class="placeholder" v-show="!minuteLines.length">暂无分时数据</p>
      <div
        v-if="minuteHover.visible"
        class="hover-tip"
        :style="{ left: `${minuteHover.x}px`, top: `${minuteHover.y}px` }"
      >
        <div v-for="(line, idx) in minuteHover.lines" :key="idx">{{ line }}</div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.charts {
  display: grid;
  grid-template-columns: 1fr;
  grid-template-rows: minmax(360px, 1.25fr) minmax(280px, 0.85fr);
  gap: 1rem;
  width: 100%;
  min-width: 0;
  min-height: min(78vh, 980px);
}

.chart-wrapper {
  display: grid;
  grid-template-rows: auto auto minmax(0, 1fr);
  width: 100%;
  min-width: 0;
  overflow: visible;
  position: relative;
  min-height: 0;
}

.chart-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}

.chart-tabs {
  display: flex;
  gap: 0.5rem;
}

.chart {
  width: 100%;
  height: 100%;
  min-width: 0;
  min-height: 280px;
  display: block;
}

.placeholder {
  color: #9ca3af;
  margin: 0.5rem 0 0;
}

.ai-level-tip {
  color: #64748b;
  margin: 0.35rem 0 0.4rem;
  font-size: 0.82rem;
}

.hover-tip {
  position: absolute;
  z-index: 20;
  background: rgba(15, 23, 42, 0.9);
  color: #f8fafc;
  padding: 0.4rem 0.6rem;
  border-radius: 6px;
  font-size: 0.8rem;
  pointer-events: none;
  white-space: nowrap;
}

@media (max-width: 1180px) {
  .charts {
    grid-template-rows: minmax(320px, 1fr) minmax(240px, 0.8fr);
    min-height: auto;
  }
}
</style>
