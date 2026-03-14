import { registerIndicator } from 'klinecharts'
import { CHART_VIEW_OPTIONS, KLINE_VIEW_IDS } from './chartViews'
import { CANDLE_PANE_ID, KDJ_PANE_ID, MACD_PANE_ID, RSI_PANE_ID, VOLUME_PANE_ID } from './chartPanes'

const CATEGORY_LABELS = Object.freeze({
  core: '基础图层',
  trend: '趋势策略',
  oscillator: '动量指标',
  signal: '信号标记'
})

const PRICE_LABELS = Object.freeze({
  minute: '分时',
  day: '蜡烛',
  month: '蜡烛',
  year: '蜡烛'
})

const ORB_WINDOW_MS = 30 * 60 * 1000
let customIndicatorsRegistered = false

const roundNumber = (value, precision = 4) => {
  const number = Number(value)
  if (!Number.isFinite(number)) return null
  return Number(number.toFixed(precision))
}

const uniqueSortedNumbers = values => Array.from(new Set(values.filter(Number.isFinite))).sort((left, right) => left - right)

const createIndicatorSpec = ({ aggregateKey, name, paneId, paneOptions, series, calcParams = [], isStack = false, order = 0 }) => ({
  aggregateKey,
  name,
  paneId,
  paneOptions,
  series,
  calcParams,
  isStack,
  order
})

const createPriceLineOverlay = ({ groupId, value, color, textColor = color }) => ({
  type: 'priceLine',
  groupId,
  value,
  color,
  textColor
})

const createStrategyDefinition = definition => Object.freeze(definition)

const resolveLabel = (definition, viewId) => {
  if (typeof definition.label === 'function') {
    return definition.label(viewId)
  }
  if (definition.label && typeof definition.label === 'object') {
    return definition.label[viewId] ?? definition.label.default ?? definition.id
  }
  return definition.label ?? definition.id
}

const getOrbRange = records => {
  if (!Array.isArray(records) || records.length < 2) {
    return null
  }

  const startTimestamp = records[0]?.timestamp
  if (!Number.isFinite(startTimestamp)) {
    return null
  }

  const windowRecords = records.filter(item => Number.isFinite(item.timestamp) && item.timestamp - startTimestamp <= ORB_WINDOW_MS)
  if (windowRecords.length < 2) {
    return null
  }

  const high = Math.max(...windowRecords.map(item => Number(item.high ?? item.close)).filter(Number.isFinite))
  const low = Math.min(...windowRecords.map(item => Number(item.low ?? item.close)).filter(Number.isFinite))
  if (!Number.isFinite(high) || !Number.isFinite(low)) {
    return null
  }

  return { high, low }
}

const CHART_STRATEGIES = Object.freeze([
  createStrategyDefinition({
    id: 'price',
    label: PRICE_LABELS,
    category: 'core',
    kind: 'core',
    supportedViews: CHART_VIEW_OPTIONS.map(view => view.id),
    defaultVisible: true,
    requires: ['price'],
    compute: () => null
  }),
  createStrategyDefinition({
    id: 'volume',
    label: '量能',
    category: 'core',
    kind: 'indicator',
    supportedViews: CHART_VIEW_OPTIONS.map(view => view.id),
    defaultVisible: true,
    requires: ['volume'],
    compute: () => ({
      indicators: [
        createIndicatorSpec({
          aggregateKey: 'VOL',
          name: 'VOL',
          paneId: VOLUME_PANE_ID,
          paneOptions: { id: VOLUME_PANE_ID, height: 96, minHeight: 72 },
          series: 'volume',
          calcParams: [5, 10, 20],
          isStack: true,
          order: 20
        })
      ]
    })
  }),
  createStrategyDefinition({
    id: 'baseLine',
    label: '昨收基线',
    category: 'core',
    kind: 'overlay',
    supportedViews: ['minute'],
    defaultVisible: true,
    requires: ['basePrice'],
    compute: ({ basePrice }) => Number.isFinite(basePrice)
      ? {
          overlays: [
            createPriceLineOverlay({
              groupId: 'minute-base-line',
              value: basePrice,
              color: '#64748b'
            })
          ]
        }
      : null
  }),
  createStrategyDefinition({
    id: 'aiLevels',
    label: 'AI 价位',
    category: 'core',
    kind: 'overlay',
    supportedViews: CHART_VIEW_OPTIONS.map(view => view.id),
    defaultVisible: true,
    requires: ['aiLevels'],
    compute: ({ aiLevels, viewId }) => {
      const overlays = []
      const resistance = roundNumber(aiLevels?.resistance)
      const support = roundNumber(aiLevels?.support)
      if (Number.isFinite(resistance)) {
        overlays.push(createPriceLineOverlay({ groupId: `${viewId}-ai-levels`, value: resistance, color: '#f97316' }))
      }
      if (Number.isFinite(support)) {
        overlays.push(createPriceLineOverlay({ groupId: `${viewId}-ai-levels`, value: support, color: '#10b981' }))
      }
      return overlays.length ? { overlays } : null
    }
  }),
  createStrategyDefinition({
    id: 'ma5',
    label: 'MA5',
    category: 'trend',
    kind: 'indicator',
    supportedViews: KLINE_VIEW_IDS,
    defaultVisible: true,
    requires: ['close'],
    compute: () => ({ indicators: [createIndicatorSpec({ aggregateKey: 'MA', name: 'MA', paneId: CANDLE_PANE_ID, paneOptions: { id: CANDLE_PANE_ID }, series: 'price', calcParams: [5], order: 40 })] })
  }),
  createStrategyDefinition({
    id: 'ma10',
    label: 'MA10',
    category: 'trend',
    kind: 'indicator',
    supportedViews: KLINE_VIEW_IDS,
    defaultVisible: true,
    requires: ['close'],
    compute: () => ({ indicators: [createIndicatorSpec({ aggregateKey: 'MA', name: 'MA', paneId: CANDLE_PANE_ID, paneOptions: { id: CANDLE_PANE_ID }, series: 'price', calcParams: [10], order: 41 })] })
  }),
  createStrategyDefinition({
    id: 'ma20',
    label: 'MA20',
    category: 'trend',
    kind: 'indicator',
    supportedViews: KLINE_VIEW_IDS,
    defaultVisible: false,
    requires: ['close'],
    compute: () => ({ indicators: [createIndicatorSpec({ aggregateKey: 'MA', name: 'MA', paneId: CANDLE_PANE_ID, paneOptions: { id: CANDLE_PANE_ID }, series: 'price', calcParams: [20], order: 42 })] })
  }),
  createStrategyDefinition({
    id: 'ma60',
    label: 'MA60',
    category: 'trend',
    kind: 'indicator',
    supportedViews: KLINE_VIEW_IDS,
    defaultVisible: false,
    requires: ['close'],
    compute: () => ({ indicators: [createIndicatorSpec({ aggregateKey: 'MA', name: 'MA', paneId: CANDLE_PANE_ID, paneOptions: { id: CANDLE_PANE_ID }, series: 'price', calcParams: [60], order: 43 })] })
  }),
  createStrategyDefinition({
    id: 'vwap',
    label: 'VWAP',
    category: 'trend',
    kind: 'indicator',
    supportedViews: ['minute'],
    defaultVisible: true,
    requires: ['close', 'volume'],
    compute: () => ({ indicators: [createIndicatorSpec({ aggregateKey: 'VWAP', name: 'VWAP', paneId: CANDLE_PANE_ID, paneOptions: { id: CANDLE_PANE_ID }, series: 'price', order: 45 })] })
  }),
  createStrategyDefinition({
    id: 'boll',
    label: 'BOLL',
    category: 'trend',
    kind: 'indicator',
    supportedViews: KLINE_VIEW_IDS,
    defaultVisible: false,
    requires: ['high', 'low', 'close'],
    compute: () => ({ indicators: [createIndicatorSpec({ aggregateKey: 'BOLL', name: 'BOLL', paneId: CANDLE_PANE_ID, paneOptions: { id: CANDLE_PANE_ID }, series: 'price', calcParams: [20, 2], order: 46 })] })
  }),
  createStrategyDefinition({
    id: 'donchian',
    label: 'Donchian',
    category: 'trend',
    kind: 'indicator',
    supportedViews: KLINE_VIEW_IDS,
    defaultVisible: false,
    requires: ['high', 'low'],
    compute: () => ({ indicators: [createIndicatorSpec({ aggregateKey: 'DONCHIAN', name: 'DONCHIAN', paneId: CANDLE_PANE_ID, paneOptions: { id: CANDLE_PANE_ID }, series: 'price', calcParams: [20], order: 47 })] })
  }),
  createStrategyDefinition({
    id: 'macd',
    label: 'MACD',
    category: 'oscillator',
    kind: 'indicator',
    supportedViews: KLINE_VIEW_IDS,
    defaultVisible: false,
    requires: ['close'],
    compute: () => ({ indicators: [createIndicatorSpec({ aggregateKey: 'MACD', name: 'MACD', paneId: MACD_PANE_ID, paneOptions: { id: MACD_PANE_ID, height: 88, minHeight: 64 }, calcParams: [12, 26, 9], isStack: true, order: 60 })] })
  }),
  createStrategyDefinition({
    id: 'rsi',
    label: 'RSI',
    category: 'oscillator',
    kind: 'indicator',
    supportedViews: KLINE_VIEW_IDS,
    defaultVisible: false,
    requires: ['close'],
    compute: () => ({ indicators: [createIndicatorSpec({ aggregateKey: 'RSI', name: 'RSI', paneId: RSI_PANE_ID, paneOptions: { id: RSI_PANE_ID, height: 88, minHeight: 64 }, calcParams: [6, 12, 24], isStack: true, order: 61 })] })
  }),
  createStrategyDefinition({
    id: 'kdj',
    label: 'KDJ',
    category: 'oscillator',
    kind: 'indicator',
    supportedViews: ['day'],
    defaultVisible: false,
    requires: ['high', 'low', 'close'],
    compute: () => ({ indicators: [createIndicatorSpec({ aggregateKey: 'KDJ', name: 'KDJ', paneId: KDJ_PANE_ID, paneOptions: { id: KDJ_PANE_ID, height: 88, minHeight: 64 }, calcParams: [9, 3, 3], isStack: true, order: 62 })] })
  }),
  createStrategyDefinition({
    id: 'orb',
    label: 'ORB',
    category: 'signal',
    kind: 'overlay',
    supportedViews: ['minute'],
    defaultVisible: false,
    requires: ['high', 'low'],
    compute: ({ records }) => {
      const range = getOrbRange(records)
      if (!range) {
        return null
      }
      return {
        overlays: [
          createPriceLineOverlay({ groupId: 'minute-orb-range', value: range.high, color: '#0f766e' }),
          createPriceLineOverlay({ groupId: 'minute-orb-range', value: range.low, color: '#dc2626' })
        ],
        signals: [
          { id: 'orb-high', label: `ORB 高点 ${range.high.toFixed(2)}` },
          { id: 'orb-low', label: `ORB 低点 ${range.low.toFixed(2)}` }
        ]
      }
    }
  })
])

const INDICATOR_FILTERS_BY_VIEW = Object.freeze({
  minute: [
    { paneId: VOLUME_PANE_ID, name: 'VOL' },
    { paneId: CANDLE_PANE_ID, name: 'VWAP' }
  ],
  day: [
    { paneId: CANDLE_PANE_ID, name: 'MA' },
    { paneId: CANDLE_PANE_ID, name: 'BOLL' },
    { paneId: CANDLE_PANE_ID, name: 'DONCHIAN' },
    { paneId: VOLUME_PANE_ID, name: 'VOL' },
    { paneId: MACD_PANE_ID, name: 'MACD' },
    { paneId: RSI_PANE_ID, name: 'RSI' },
    { paneId: KDJ_PANE_ID, name: 'KDJ' }
  ],
  month: [
    { paneId: CANDLE_PANE_ID, name: 'MA' },
    { paneId: CANDLE_PANE_ID, name: 'BOLL' },
    { paneId: CANDLE_PANE_ID, name: 'DONCHIAN' },
    { paneId: VOLUME_PANE_ID, name: 'VOL' },
    { paneId: MACD_PANE_ID, name: 'MACD' },
    { paneId: RSI_PANE_ID, name: 'RSI' }
  ],
  year: [
    { paneId: CANDLE_PANE_ID, name: 'MA' },
    { paneId: CANDLE_PANE_ID, name: 'BOLL' },
    { paneId: CANDLE_PANE_ID, name: 'DONCHIAN' },
    { paneId: VOLUME_PANE_ID, name: 'VOL' },
    { paneId: MACD_PANE_ID, name: 'MACD' },
    { paneId: RSI_PANE_ID, name: 'RSI' }
  ]
})

const OVERLAY_GROUPS_BY_VIEW = Object.freeze({
  minute: ['minute-ai-levels', 'minute-base-line', 'minute-orb-range', 'minute-markers'],
  day: ['day-ai-levels', 'day-markers'],
  month: ['month-ai-levels', 'month-markers'],
  year: ['year-ai-levels', 'year-markers']
})

function registerVwapIndicator() {
  registerIndicator({
    name: 'VWAP',
    shortName: 'VWAP',
    series: 'price',
    precision: 2,
    shouldOhlc: true,
    calcParams: [],
    figures: [{ key: 'vwap', title: 'VWAP: ', type: 'line' }],
    calc: dataList => {
      let cumulativeVolume = 0
      let cumulativeTurnover = 0
      return dataList.map(item => {
        const close = Number(item?.close)
        const volume = Math.max(0, Number(item?.volume ?? 0))
        if (!Number.isFinite(close)) {
          return {}
        }
        cumulativeVolume += volume
        cumulativeTurnover += close * volume
        return cumulativeVolume > 0 ? { vwap: cumulativeTurnover / cumulativeVolume } : {}
      })
    }
  })
}

function registerDonchianIndicator() {
  registerIndicator({
    name: 'DONCHIAN',
    shortName: 'DON',
    series: 'price',
    precision: 2,
    shouldOhlc: true,
    calcParams: [20],
    figures: [
      { key: 'upper', title: 'UP: ', type: 'line' },
      { key: 'middle', title: 'MID: ', type: 'line' },
      { key: 'lower', title: 'DN: ', type: 'line' }
    ],
    calc: (dataList, indicator) => {
      const period = Number(indicator?.calcParams?.[0] ?? 20)
      return dataList.map((item, index) => {
        if (index < period - 1) {
          return {}
        }
        const window = dataList.slice(index - period + 1, index + 1)
        const highs = window.map(point => Number(point?.high)).filter(Number.isFinite)
        const lows = window.map(point => Number(point?.low)).filter(Number.isFinite)
        if (!highs.length || !lows.length) {
          return {}
        }
        const upper = Math.max(...highs)
        const lower = Math.min(...lows)
        return {
          upper,
          middle: (upper + lower) / 2,
          lower
        }
      })
    }
  })
}

export function ensureChartStrategiesRegistered() {
  if (customIndicatorsRegistered || typeof registerIndicator !== 'function') {
    return
  }
  registerVwapIndicator()
  registerDonchianIndicator()
  customIndicatorsRegistered = true
}

export function getChartStrategiesForView(viewId) {
  return CHART_STRATEGIES
    .filter(item => item.supportedViews.includes(viewId))
    .map(item => ({ ...item, resolvedLabel: resolveLabel(item, viewId) }))
}

export function createStrategyVisibilityState() {
  return Object.fromEntries(
    CHART_VIEW_OPTIONS.map(view => [
      view.id,
      Object.fromEntries(
        getChartStrategiesForView(view.id).map(item => [item.id, item.defaultVisible !== false])
      )
    ])
  )
}

export function getStrategyGroupsForView(viewId, visibilityState = {}) {
  const strategies = getChartStrategiesForView(viewId)
  return Object.entries(CATEGORY_LABELS)
    .map(([categoryId, categoryLabel]) => ({
      id: categoryId,
      label: categoryLabel,
      items: strategies
        .filter(item => item.category === categoryId)
        .map(item => ({
          id: item.id,
          label: item.resolvedLabel,
          active: visibilityState[item.id] !== false,
          kind: item.kind
        }))
    }))
    .filter(group => group.items.length > 0)
}

export function getIndicatorFiltersForView(viewId) {
  return INDICATOR_FILTERS_BY_VIEW[viewId] ?? []
}

export function getOverlayGroupIdsForView(viewId) {
  return OVERLAY_GROUPS_BY_VIEW[viewId] ?? []
}

export function buildStrategyRenderPlan({ viewId, records, visibility = {}, aiLevels, basePrice }) {
  const renderPlan = {
    indicators: [],
    overlays: [],
    markers: [],
    signals: []
  }

  getChartStrategiesForView(viewId)
    .filter(item => visibility[item.id] !== false)
    .forEach(item => {
      const result = item.compute?.({ viewId, records, aiLevels, basePrice, visibility })
      if (!result) {
        return
      }
      if (Array.isArray(result.indicators)) {
        renderPlan.indicators.push(...result.indicators)
      }
      if (Array.isArray(result.overlays)) {
        renderPlan.overlays.push(...result.overlays)
      }
      if (Array.isArray(result.markers)) {
        renderPlan.markers.push(...result.markers)
      }
      if (Array.isArray(result.signals)) {
        renderPlan.signals.push(...result.signals)
      }
    })

  const aggregatedIndicators = new Map()
  renderPlan.indicators.forEach(item => {
    const key = item.aggregateKey ?? `${item.name}:${item.paneId}`
    if (!aggregatedIndicators.has(key)) {
      aggregatedIndicators.set(key, { ...item, calcParams: [...(item.calcParams ?? [])] })
      return
    }
    const current = aggregatedIndicators.get(key)
    current.calcParams = uniqueSortedNumbers([...(current.calcParams ?? []), ...(item.calcParams ?? [])])
  })

  renderPlan.indicators = Array.from(aggregatedIndicators.values())
    .map(item => ({ ...item, calcParams: uniqueSortedNumbers(item.calcParams ?? []) }))
    .sort((left, right) => (left.order ?? 0) - (right.order ?? 0))

  return renderPlan
}
