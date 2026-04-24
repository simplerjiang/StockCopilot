import { ref } from 'vue'
import { dispose, init } from 'klinecharts'
import { normalizeKlineInterval } from './chartViews'
import { buildStrategyRenderPlan, ensureChartStrategiesRegistered } from './chartStrategyRegistry'
import { ensureKLineChartsRegistry, syncIndicatorRegistry, syncMarkerSignalRegistry, syncOverlayRegistry } from './klinechartsRegistry'

const DEFAULT_SYMBOL = {
  ticker: 'A-SHARE',
  pricePrecision: 2,
  volumePrecision: 0
}

const PERIOD_BY_VIEW = {
  minute: { type: 'minute', span: 1 },
  day: { type: 'day', span: 1 },
  month: { type: 'month', span: 1 },
  year: { type: 'year', span: 1 }
}

export const CHART_COLORS = {
  up: '#ef4444',
  down: '#22c55e',
  flat: '#94a3b8'
}

const pad = value => String(value).padStart(2, '0')

const clamp = (value, min, max) => Math.min(Math.max(value, min), max)

const trimTrailingZeros = value => value.replace(/\.0+$|(?<=\.\d*[1-9])0+$/g, '')

const parseLevelValue = value => {
  if (value == null || value === '') return null
  const number = Number(value)
  return Number.isFinite(number) ? number : null
}

const parseVolumeValue = raw => {
  if (raw == null) return 0
  if (typeof raw === 'number' && Number.isFinite(raw)) return raw
  const text = String(raw).trim().replace(/,/g, '')
  if (!text) return 0
  if (/[万亿]$/.test(text)) {
    const unit = text.slice(-1)
    const base = Number(text.slice(0, -1))
    if (!Number.isFinite(base)) return 0
    if (unit === '万') return base * 10000
    if (unit === '亿') return base * 100000000
  }
  const value = Number(text)
  return Number.isFinite(value) ? value : 0
}

const formatHands = value => {
  const number = Number(value)
  if (!Number.isFinite(number)) return '-'
  const absolute = Math.abs(number)
  if (absolute >= 100000000) {
    return `${trimTrailingZeros((number / 100000000).toFixed(2))}亿手`
  }
  if (absolute >= 10000) {
    return `${trimTrailingZeros((number / 10000).toFixed(2))}万手`
  }
  return `${Math.round(number)}手`
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
  if (/^\d{3,4}$/.test(trimmed)) {
    const hhmm = Number(trimmed)
    return { hour: Math.floor(hhmm / 100), minute: hhmm % 100, second: 0 }
  }
  return null
}

const toTimestamp = (datePart, timePart = { hour: 0, minute: 0, second: 0 }) => {
  if (!datePart) return null
  const date = new Date(
    datePart.year,
    (datePart.month ?? 1) - 1,
    datePart.day ?? 1,
    timePart.hour ?? 0,
    timePart.minute ?? 0,
    timePart.second ?? 0,
    0
  )
  if (Number.isNaN(date.getTime())) return null
  return date.getTime()
}

const formatTimestampLabel = timestamp => {
  if (!Number.isFinite(timestamp)) return ''
  const date = new Date(timestamp)
  const dateLabel = `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
  const hasClock = date.getHours() !== 0 || date.getMinutes() !== 0 || date.getSeconds() !== 0
  return hasClock ? `${dateLabel} ${pad(date.getHours())}:${pad(date.getMinutes())}` : dateLabel
}

const formatNumber = value => (Number.isFinite(Number(value)) ? Number(value) : '-')

const formatSignedNumber = value => {
  const number = Number(value)
  if (!Number.isFinite(number)) return '-'
  if (number === 0) return '0.00'
  return `${number > 0 ? '+' : ''}${number.toFixed(2)}`
}

const detectPrecision = values => {
  const precision = values.reduce((max, value) => {
    if (!Number.isFinite(value)) return max
    const text = String(value)
    if (!text.includes('.')) return max
    return Math.max(max, text.split('.')[1]?.length ?? 0)
  }, 0)
  return clamp(precision, 0, 4)
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

export const resolveMinuteTrend = (records, basePrice) => {
  const latestClose = Number(records.at(-1)?.close)
  if (!Number.isFinite(latestClose) || !Number.isFinite(basePrice)) {
    return 'flat'
  }
  if (latestClose > basePrice) {
    return 'up'
  }
  if (latestClose < basePrice) {
    return 'down'
  }
  return 'flat'
}

const buildMinuteAreaStyle = (visibility, minuteTrend) => {
  const lineColor = visibility.price === false
    ? 'rgba(148, 163, 184, 0)'
    : CHART_COLORS[minuteTrend] ?? CHART_COLORS.flat
  const topColor = visibility.price === false ? 'rgba(148, 163, 184, 0)' : withOpacity(lineColor, 0.18)
  const bottomColor = visibility.price === false ? 'rgba(148, 163, 184, 0)' : withOpacity(lineColor, 0.04)

  return {
    lineSize: 2,
    lineColor,
    value: 'close',
    smooth: true,
    backgroundColor: [
      { offset: 0, color: topColor },
      { offset: 1, color: bottomColor }
    ],
    point: {
      show: false
    }
  }
}

const withOpacity = (hexColor, opacity) => {
  const normalized = String(hexColor || '').replace('#', '')
  if (normalized.length !== 6) {
    return `rgba(148, 163, 184, ${opacity})`
  }
  const red = Number.parseInt(normalized.slice(0, 2), 16)
  const green = Number.parseInt(normalized.slice(2, 4), 16)
  const blue = Number.parseInt(normalized.slice(4, 6), 16)
  return `rgba(${red}, ${green}, ${blue}, ${opacity})`
}

const buildChartStyles = (viewType, visibility = {}, options = {}) => ({
  grid: {
    horizontal: { show: true, color: 'rgba(148, 163, 184, 0.12)', style: 'dashed', size: 1, dashedValue: [4, 4] },
    vertical: { show: true, color: 'rgba(148, 163, 184, 0.12)', style: 'dashed', size: 1, dashedValue: [4, 4] }
  },
  candle: {
    type: viewType === 'minute' ? 'area' : 'candle_solid',
    area: viewType === 'minute'
      ? buildMinuteAreaStyle(visibility, options.minuteTrend ?? 'flat')
      : {
          lineSize: 2,
          lineColor: visibility.price === false ? 'rgba(37, 99, 235, 0)' : '#2563eb',
          value: 'close',
          smooth: true,
          backgroundColor: [
            { offset: 0, color: visibility.price === false ? 'rgba(37, 99, 235, 0)' : 'rgba(37, 99, 235, 0.28)' },
            { offset: 1, color: visibility.price === false ? 'rgba(37, 99, 235, 0)' : 'rgba(37, 99, 235, 0.05)' }
          ],
          point: {
            show: false
          }
        },
    bar: {
      compareRule: 'previous_close',
      upColor: '#ef4444',
      downColor: '#22c55e',
      noChangeColor: '#94a3b8',
      upBorderColor: visibility.price === false ? 'rgba(239, 68, 68, 0)' : '#ef4444',
      downBorderColor: visibility.price === false ? 'rgba(34, 197, 94, 0)' : '#22c55e',
      noChangeBorderColor: visibility.price === false ? 'rgba(148, 163, 184, 0)' : '#94a3b8',
      upWickColor: visibility.price === false ? 'rgba(239, 68, 68, 0)' : '#ef4444',
      downWickColor: visibility.price === false ? 'rgba(34, 197, 94, 0)' : '#22c55e',
      noChangeWickColor: visibility.price === false ? 'rgba(148, 163, 184, 0)' : '#94a3b8'
    },
    tooltip: {
      showRule: 'none'
    },
    priceMark: {
      high: { show: false },
      low: { show: false },
      last: { show: false }
    }
  },
  indicator: {
    tooltip: {
      showRule: 'none'
    },
    bars: [
      {
        upColor: visibility.volume === false ? 'rgba(239, 68, 68, 0)' : '#ef4444',
        downColor: visibility.volume === false ? 'rgba(34, 197, 94, 0)' : '#22c55e',
        noChangeColor: visibility.volume === false ? 'rgba(148, 163, 184, 0)' : '#94a3b8'
      }
    ],
    lines: [
      { color: '#f59e0b', size: 1, smooth: true },
      { color: '#a855f7', size: 1, smooth: true },
      { color: '#38bdf8', size: 1, smooth: true },
      { color: '#ef4444', size: 1, smooth: true },
      { color: '#14b8a6', size: 1, smooth: true },
      { color: '#22c55e', size: 1, smooth: true }
    ]
  },
  xAxis: {
    axisLine: { show: false, color: 'transparent' },
    tickLine: { show: false, color: 'transparent' },
    tickText: {
      color: '#94a3b8',
      size: 11,
      weight: 400
    }
  },
  yAxis: {
    axisLine: { show: false, color: 'transparent' },
    tickLine: { show: false, color: 'transparent' },
    tickText: { color: '#94a3b8', size: 11, weight: 400 }
  },
  crosshair: {
    horizontal: {
      line: { show: true, color: '#334155', style: 'dashed', size: 1, dashedValue: [4, 4] },
      text: { show: true, color: '#e2e8f0', backgroundColor: '#1e293b' }
    },
    vertical: {
      line: { show: true, color: '#334155', style: 'dashed', size: 1, dashedValue: [4, 4] },
      text: { show: true, color: '#e2e8f0', backgroundColor: '#1e293b' }
    }
  },
  separator: {
    color: 'rgba(148, 163, 184, 0.18)'
  },
  overlay: {
    line: {
      color: '#f97316',
      style: 'dashed',
      size: 1,
      dashedValue: [4, 4],
      smooth: false
    },
    text: {
      color: '#f8fafc',
      backgroundColor: 'rgba(15, 23, 42, 0.9)'
    }
  }
})

const buildKlineRecords = rawLines => {
  const records = (Array.isArray(rawLines) ? rawLines : [])
    .map(item => {
      const datePart = parseDatePart(item?.date)
      const timestamp = toTimestamp(datePart)
      const open = Number(item?.open)
      const high = Number(item?.high)
      const low = Number(item?.low)
      const close = Number(item?.close)
      if (!Number.isFinite(timestamp) || ![open, high, low, close].every(Number.isFinite)) {
        return null
      }

      return {
        timestamp,
        open,
        high,
        low,
        close,
        volume: parseVolumeValue(item?.volume),
        label: formatTimestampLabel(timestamp)
      }
    })
    .filter(Boolean)
    .sort((left, right) => left.timestamp - right.timestamp)

  const ma5 = calculateMovingAverage(records, 5)
  const ma10 = calculateMovingAverage(records, 10)

  return records.map((item, index) => ({
    ...item,
    prevClose: index > 0 ? records[index - 1].close : NaN,
    ma5: ma5[index],
    ma10: ma10[index]
  }))
}

export const buildMinuteRecords = (rawLines, fallbackBasePrice) => {
  const normalizedRecords = (Array.isArray(rawLines) ? rawLines : [])
    .map(item => {
      const datePart = parseDatePart(item?.date)
      const timePart = parseTimePart(item?.time)
      const timestamp = toTimestamp(datePart, timePart)
      const close = Number(item?.price ?? item?.close ?? item?.value)
      const volume = parseVolumeValue(item?.volume)
      if (!Number.isFinite(timestamp) || !Number.isFinite(close)) {
        return null
      }

      return {
        timestamp,
        open: close,
        high: close,
        low: close,
        close,
        volume,
        label: formatTimestampLabel(timestamp)
      }
    })
    .filter(Boolean)
    .sort((left, right) => left.timestamp - right.timestamp)
    .map((item, index, list) => {
      if (index === 0) {
        return item
      }

      const previousVolume = Number(list[index - 1]?.volume)
      const currentVolume = Number(item.volume)
      if (!Number.isFinite(previousVolume) || !Number.isFinite(currentVolume) || currentVolume < previousVolume) {
        return item
      }

      return {
        ...item,
        volume: currentVolume - previousVolume
      }
    })

  const basePrice = fallbackBasePrice == null || fallbackBasePrice === ''
    ? normalizedRecords[0]?.close ?? null
    : (Number.isFinite(Number(fallbackBasePrice)) ? Number(fallbackBasePrice) : normalizedRecords[0]?.close ?? null)

  const records = normalizedRecords.map((item, index, list) => {
    const previousClose = index > 0
      ? list[index - 1]?.close
      : (Number.isFinite(basePrice) ? basePrice : item.close)
    const open = Number.isFinite(previousClose) ? previousClose : item.close

    return {
      ...item,
      open,
      high: Math.max(open, item.close),
      low: Math.min(open, item.close)
    }
  })

  return {
    records,
    basePrice
  }
}

const toChartData = records => records.map(item => {
  const entry = {
    timestamp: item.timestamp,
    open: item.open,
    high: item.high,
    low: item.low,
    close: item.close,
    volume: item.volume
  }
  if (item._retailHeat) {
    entry._retailHeat = item._retailHeat
  }
  return entry
})

const buildSymbol = records => ({
  ...DEFAULT_SYMBOL,
  pricePrecision: detectPrecision(records.flatMap(item => [item.open, item.high, item.low, item.close])),
  volumePrecision: 0
})

const buildRecordLookup = records => new Map(records.map(item => [item.timestamp, item]))

const mergeRetailHeat = (records, heatData) => {
  if (!heatData?.data?.length) return
  const heatMap = new Map()
  for (const point of heatData.data) {
    if (point.hasData) {
      heatMap.set(point.date, point)
    }
  }
  for (const record of records) {
    const d = new Date(record.timestamp)
    const dateStr = `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
    const heat = heatMap.get(dateStr)
    if (heat) {
      record._retailHeat = heat
    }
  }
}

const emptyHoverState = { visible: false, x: 0, y: 0, lines: [] }

export function useStockChartAdapter({ props, klineRef, minuteRef, featureVisibilityByView }) {
  const klineHover = ref({ ...emptyHoverState })
  const minuteHover = ref({ ...emptyHoverState })
  const minuteBasePrice = ref(null)
  let resizeFrame = null

  const createController = ({ containerRef, viewType, hoverRef }) => {
    let chart = null
    let dataList = []
    let recordMap = new Map()
    let actionHandler = null

    const hideHover = () => {
      hoverRef.value = { ...emptyHoverState }
    }

    const ensureChart = () => {
      if (chart || !containerRef.value) return chart
      if (typeof window !== 'undefined' && typeof window.matchMedia !== 'function') return null

      ensureKLineChartsRegistry()
      ensureChartStrategiesRegistered()
      chart = init(containerRef.value)
      if (!chart) return null

      const initialVisibility = featureVisibilityByView?.value?.[viewType === 'minute' ? 'minute' : normalizeKlineInterval(props.interval)] ?? {}
      chart.setStyles(buildChartStyles(viewType, initialVisibility))
      chart.setFormatter?.({
        formatBigNumber: formatHands
      })
      chart.setDataLoader({
        getBars: ({ callback }) => {
          callback(dataList, false)
        }
      })

      actionHandler = payload => {
        if (!payload || !containerRef.value || !chart) {
          hideHover()
          return
        }

        // klinecharts v10: payload only has {x, y, paneId}, derive data from coordinates
        const x = payload.x ?? payload.realX
        const y = payload.y
        if (!Number.isFinite(x) || !Number.isFinite(y)) {
          hideHover()
          return
        }

        const points = chart.convertFromPixel([{ x, y }])
        const point = Array.isArray(points) ? points[0] : points
        if (!point || !Number.isFinite(point.dataIndex)) {
          hideHover()
          return
        }

        const allData = chart.getDataList()
        const kLineData = allData[point.dataIndex]
        if (!kLineData) {
          hideHover()
          return
        }

        const record = recordMap.get(kLineData.timestamp)
        if (!record) {
          hideHover()
          return
        }

        const width = containerRef.value.clientWidth || 200
        const height = containerRef.value.clientHeight || 200
        const pointX = clamp((payload.x ?? 0) + 12, 8, Math.max(8, width - 136))
        const pointY = clamp((payload.y ?? 0) + 12, 8, Math.max(8, height - 132))

        if (viewType === 'minute') {
          const changePercent = Number.isFinite(minuteBasePrice.value) && minuteBasePrice.value !== 0
            ? (((record.close - minuteBasePrice.value) / minuteBasePrice.value) * 100).toFixed(2)
            : '-'

          const vr = props.volumeRatio
          const vrText = Number.isFinite(vr) && vr > 0 ? `量比: ${Number(vr).toFixed(2)}` : null

          hoverRef.value = {
            visible: true,
            x: pointX,
            y: pointY,
            lines: [
              record.label,
              `${formatNumber(record.close)}`,
              `成交量: ${formatHands(record.volume ?? 0)}`,
              ...(vrText ? [vrText] : []),
              `涨跌幅: ${Number(changePercent) > 0 ? '+' : ''}${changePercent}%`
            ]
          }
          return
        }

        const changePercent = Number.isFinite(record.prevClose) && record.prevClose !== 0
          ? (((record.close - record.prevClose) / record.prevClose) * 100).toFixed(2)
          : '-'
        const priceChange = Number.isFinite(record.prevClose)
          ? record.close - record.prevClose
          : NaN

        hoverRef.value = {
          visible: true,
          x: pointX,
          y: pointY,
          lines: [
            record.label,
            `开: ${formatNumber(record.open)}`,
            `收: ${formatNumber(record.close)}`,
            `高: ${formatNumber(record.high)}`,
            `低: ${formatNumber(record.low)}`,
            `成交量: ${formatHands(record.volume ?? 0)}`,
            `MA5: ${Number.isFinite(record.ma5) ? record.ma5 : '-'}`,
            `MA10: ${Number.isFinite(record.ma10) ? record.ma10 : '-'}`,
            `涨跌: ${formatSignedNumber(priceChange)}`,
            `涨跌幅: ${Number(changePercent) > 0 ? '+' : ''}${changePercent}%`
          ]
        }
      }

      chart.subscribeAction('onCrosshairChange', actionHandler)

      return chart
    }

    const render = ({ records, periodKey, aiLevels, basePrice, styleOptions }) => {
      const instance = ensureChart()
      if (!instance) return

      const viewId = viewType === 'minute' ? 'minute' : periodKey
      const visibility = featureVisibilityByView?.value?.[viewId] ?? {}
      const renderPlan = buildStrategyRenderPlan({
        viewId,
        records,
        visibility,
        aiLevels,
        basePrice
      })

      dataList = toChartData(records)
      recordMap = buildRecordLookup(records)

      instance.setStyles(buildChartStyles(viewType, visibility, styleOptions))
      instance.setSymbol(buildSymbol(records))
      instance.setPeriod(PERIOD_BY_VIEW[periodKey] ?? PERIOD_BY_VIEW.day)
      instance.resetData()
      syncIndicatorRegistry(instance, { viewId, renderPlan })
      syncOverlayRegistry(instance, {
        viewId,
        firstTimestamp: dataList[0]?.timestamp ?? null,
        renderPlan
      })
      syncMarkerSignalRegistry(instance, { viewId, renderPlan })
      instance.scrollToRealTime?.(0)
      instance.resize()
    }

    const resize = () => {
      chart?.resize()
    }

    const destroy = () => {
      hideHover()
      if (chart && actionHandler) {
        chart.unsubscribeAction?.('onCrosshairChange', actionHandler)
      }
      if (chart) {
        dispose(chart)
      }
      chart = null
      actionHandler = null
      dataList = []
      recordMap = new Map()
    }

    const scrollToRealTime = () => {
      chart?.scrollToRealTime?.(0)
    }

    return { ensureChart, render, resize, destroy, scrollToRealTime }
  }

  const klineController = createController({ containerRef: klineRef, viewType: 'kline', hoverRef: klineHover })
  const minuteController = createController({ containerRef: minuteRef, viewType: 'minute', hoverRef: minuteHover })

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

  const renderKLine = () => {
    const records = buildKlineRecords(props.kLines)
    mergeRetailHeat(records, props.retailHeatData)
    klineController.render({
      records,
      periodKey: normalizeKlineInterval(props.interval),
      aiLevels: props.aiLevels,
      basePrice: null
    })
  }

  const renderMinute = () => {
    const { records, basePrice } = buildMinuteRecords(props.minuteLines, props.basePrice)
    minuteBasePrice.value = basePrice
    minuteController.render({
      records,
      periodKey: 'minute',
      aiLevels: props.aiLevels,
      basePrice,
      styleOptions: {
        minuteTrend: resolveMinuteTrend(records, basePrice)
      }
    })
  }

  const renderAll = () => {
    renderKLine()
    renderMinute()
  }

  const mountCharts = () => {
    klineController.ensureChart()
    minuteController.ensureChart()
    renderAll()
  }

  const resizeCharts = () => {
    klineController.resize()
    minuteController.resize()
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

  const destroyCharts = () => {
    if (resizeFrame && typeof window !== 'undefined') {
      window.cancelAnimationFrame(resizeFrame)
      resizeFrame = null
    }
    klineController.destroy()
    minuteController.destroy()
  }

  const scrollChartsToRealTime = () => {
    klineController.scrollToRealTime()
    minuteController.scrollToRealTime()
  }

  return {
    aiLevelText,
    destroyCharts,
    klineHover,
    minuteHover,
    mountCharts,
    queueResize,
    renderAll,
    scrollChartsToRealTime
  }
}
