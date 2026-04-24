import { registerIndicator } from 'klinecharts'
import { CHART_VIEW_OPTIONS, KLINE_VIEW_IDS } from './chartViews'
import { ATR_PANE_ID, CANDLE_PANE_ID, KDJ_PANE_ID, MACD_PANE_ID, RETAIL_HEAT_PANE_ID, RSI_PANE_ID, VOLUME_PANE_ID } from './chartPanes'

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

const KDJ_LINE_DEFINITIONS = Object.freeze([
  { name: 'KDJ_K_VISUAL', shortName: 'KDJ-K', key: 'k', title: 'K: ', color: '#ff005c', size: 3, style: 'solid', dashedValue: [0, 0] },
  { name: 'KDJ_D_VISUAL', shortName: 'KDJ-D', key: 'd', title: 'D: ', color: '#39ff14', size: 2, style: 'dashed', dashedValue: [10, 6] },
  { name: 'KDJ_J_VISUAL', shortName: 'KDJ-J', key: 'j', title: 'J: ', color: '#00e5ff', size: 4, style: 'solid', dashedValue: [0, 0] }
])

const TD_MARKER_STYLES = Object.freeze({
  buyWeak: { color: '#86efac', lineSize: 1, textSize: 10, textWeight: '500' },
  buyStrong: { color: '#22c55e', lineSize: 2, textSize: 12, textWeight: '700' },
  sellWeak: { color: '#fca5a5', lineSize: 1, textSize: 10, textWeight: '500' },
  sellStrong: { color: '#ef4444', lineSize: 2, textSize: 12, textWeight: '700' }
})

const TD_MARKER_PRICE_OFFSETS = Object.freeze({
  buyWeak: 0.99,
  buyStrong: 0.982,
  sellWeak: 1.01,
  sellStrong: 1.018
})

const MINUTE_TD_MARKER_PRICE_OFFSETS = Object.freeze({
  buyWeak: 0.9996,
  buyStrong: 0.9992,
  sellWeak: 1.0004,
  sellStrong: 1.0008
})

const collapseCompletedTdRuns = markers => {
  const collapsed = []
  let run = []

  const flushRun = () => {
    if (!run.length) {
      return
    }

    const completedMarker = run.find(item => item.count === 9)
    if (completedMarker) {
      collapsed.push(completedMarker)
    } else {
      collapsed.push(...run)
    }
    run = []
  }

  markers.forEach(marker => {
    const previous = run.at(-1)
    const isSameRun = previous
      && previous.direction === marker.direction
      && marker.count === previous.count + 1

    if (!isSameRun) {
      flushRun()
    }

    run.push(marker)
  })

  flushRun()
  return collapsed
}

const roundNumber = (value, precision = 4) => {
  const number = Number(value)
  if (!Number.isFinite(number)) return null
  return Number(number.toFixed(precision))
}

const roundPrice = value => roundNumber(value, 2)

const averageNumbers = values => {
  const numbers = values.filter(Number.isFinite)
  if (!numbers.length) {
    return null
  }
  return numbers.reduce((sum, value) => sum + value, 0) / numbers.length
}

const uniqueSortedNumbers = values => Array.from(new Set(values.filter(Number.isFinite))).sort((left, right) => left - right)

const createIndicatorSpec = ({ aggregateKey, name, shortName, paneId, paneOptions, series, calcParams = [], styles = null, isStack = false, order = 0 }) => ({
  aggregateKey,
  name,
  shortName,
  paneId,
  paneOptions,
  series,
  calcParams,
  styles,
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

const createHelp = (description, interpretation, usage) => ({
  description,
  interpretation,
  usage
})

const createLineLegend = (color, label, meaning) => ({
  color,
  label,
  meaning
})

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

const calculateMovingAverageSeries = (records, period) => {
  if (!Array.isArray(records) || period <= 0) {
    return []
  }

  let rollingSum = 0
  return records.map((item, index) => {
    const close = Number(item?.close)
    if (!Number.isFinite(close)) {
      return null
    }
    rollingSum += close
    if (index >= period) {
      const previousClose = Number(records[index - period]?.close)
      if (Number.isFinite(previousClose)) {
        rollingSum -= previousClose
      }
    }
    return index >= period - 1 ? rollingSum / period : null
  })
}

const buildMaCrossMarkers = records => {
  const fastSeries = calculateMovingAverageSeries(records, 5)
  const slowSeries = calculateMovingAverageSeries(records, 10)
  const markers = []

  for (let index = 1; index < records.length; index += 1) {
    const previousFast = Number(fastSeries[index - 1])
    const previousSlow = Number(slowSeries[index - 1])
    const currentFast = Number(fastSeries[index])
    const currentSlow = Number(slowSeries[index])
    const high = Number(records[index]?.high)
    const low = Number(records[index]?.low)
    const timestamp = Number(records[index]?.timestamp)

    if (![previousFast, previousSlow, currentFast, currentSlow, high, low, timestamp].every(Number.isFinite)) {
      continue
    }

    const previousDiff = previousFast - previousSlow
    const currentDiff = currentFast - currentSlow
    if (previousDiff <= 0 && currentDiff > 0) {
      markers.push({
        id: `ma-cross-golden-${timestamp}`,
        timestamp,
        value: roundPrice(low * 0.985),
        text: 'MA金叉',
        color: '#22c55e'
      })
      continue
    }

    if (previousDiff >= 0 && currentDiff < 0) {
      markers.push({
        id: `ma-cross-death-${timestamp}`,
        timestamp,
        value: roundPrice(high * 1.015),
        text: 'MA死叉',
        color: '#ef4444'
      })
    }
  }

  return markers
}

const buildTdSequentialMarkers = (records, options = {}) => {
  const {
    priceOffsets = TD_MARKER_PRICE_OFFSETS,
    collapseCompletedRuns = false
  } = options
  const markers = []
  let activeDirection = null
  let activeCount = 0

  const resolveTdDirection = (currentClose, referenceClose, previousDirection) => {
    if (currentClose > referenceClose) {
      return 'sell'
    }
    if (currentClose < referenceClose) {
      return 'buy'
    }
    return previousDirection
  }

  const pushTdMarker = ({ direction, count, timestamp, high, low }) => {
    if (count < 6) {
      return
    }

    const isBuy = direction === 'buy'
    const isStrong = count >= 8
    const markerStyle = isBuy
      ? (isStrong ? TD_MARKER_STYLES.buyStrong : TD_MARKER_STYLES.buyWeak)
      : (isStrong ? TD_MARKER_STYLES.sellStrong : TD_MARKER_STYLES.sellWeak)
    const valueOffset = isBuy
      ? (isStrong ? priceOffsets.buyStrong : priceOffsets.buyWeak)
      : (isStrong ? priceOffsets.sellStrong : priceOffsets.sellWeak)

    markers.push({
      id: `td-sequential-${direction}-${count}-${timestamp}`,
      timestamp,
      value: roundPrice((isBuy ? low : high) * valueOffset),
      direction,
      count,
      text: `${isBuy ? 'TD买' : 'TD卖'}${count}`,
      color: markerStyle.color,
      lineSize: markerStyle.lineSize,
      textSize: markerStyle.textSize,
      textWeight: markerStyle.textWeight
    })
  }

  for (let index = 4; index < records.length; index += 1) {
    const currentClose = Number(records[index]?.close)
    const referenceClose = Number(records[index - 4]?.close)
    const high = Number(records[index]?.high)
    const low = Number(records[index]?.low)
    const timestamp = Number(records[index]?.timestamp)

    if (![currentClose, referenceClose, high, low, timestamp].every(Number.isFinite)) {
      activeDirection = null
      activeCount = 0
      continue
    }

    const nextDirection = resolveTdDirection(currentClose, referenceClose, activeDirection)
    if (!nextDirection) {
      activeDirection = null
      activeCount = 0
      continue
    }

    const previousCount = activeDirection === nextDirection ? activeCount : 0
    activeDirection = nextDirection
    activeCount = previousCount >= 9 ? 9 : previousCount + 1

    if (activeCount > previousCount) {
      pushTdMarker({ direction: activeDirection, count: activeCount, timestamp, high, low })
    }
  }

  return collapseCompletedRuns ? collapseCompletedTdRuns(markers) : markers
}

const calculateMacdSeries = (records, shortPeriod = 12, longPeriod = 26, signalPeriod = 9) => {
  if (!Array.isArray(records) || !records.length) {
    return []
  }

  const shortMultiplier = 2 / (shortPeriod + 1)
  const longMultiplier = 2 / (longPeriod + 1)
  const signalMultiplier = 2 / (signalPeriod + 1)
  let shortEma = null
  let longEma = null
  let signalEma = null

  return records.map(item => {
    const close = Number(item?.close)
    if (!Number.isFinite(close)) {
      return null
    }

    shortEma = shortEma === null ? close : close * shortMultiplier + shortEma * (1 - shortMultiplier)
    longEma = longEma === null ? close : close * longMultiplier + longEma * (1 - longMultiplier)
    const diff = shortEma - longEma
    signalEma = signalEma === null ? diff : diff * signalMultiplier + signalEma * (1 - signalMultiplier)

    return {
      diff,
      signal: signalEma,
      histogram: (diff - signalEma) * 2
    }
  })
}

const buildMacdCrossMarkers = records => {
  const shortPeriod = 12
  const longPeriod = 26
  const signalPeriod = 9
  const firstStableIndex = longPeriod + signalPeriod - 2
  const macdSeries = calculateMacdSeries(records, shortPeriod, longPeriod, signalPeriod)
  const markers = []

  for (let index = Math.max(1, firstStableIndex); index < records.length; index += 1) {
    const previous = macdSeries[index - 1]
    const current = macdSeries[index]
    const high = Number(records[index]?.high)
    const low = Number(records[index]?.low)
    const timestamp = Number(records[index]?.timestamp)

    if (!previous || !current || ![previous.diff, previous.signal, current.diff, current.signal, high, low, timestamp].every(Number.isFinite)) {
      continue
    }

    const previousGap = previous.diff - previous.signal
    const currentGap = current.diff - current.signal

    if (previousGap <= 0 && currentGap > 0) {
      markers.push({
        id: `macd-cross-golden-${timestamp}`,
        timestamp,
        value: roundPrice(low * 0.982),
        text: 'MACD金叉',
        color: '#22c55e'
      })
      continue
    }

    if (previousGap >= 0 && currentGap < 0) {
      markers.push({
        id: `macd-cross-death-${timestamp}`,
        timestamp,
        value: roundPrice(high * 1.018),
        text: 'MACD死叉',
        color: '#ef4444'
      })
    }
  }

  return markers
}

const calculateVwapSeries = records => {
  let cumulativeVolume = 0
  let cumulativeTurnover = 0

  return records.map(item => {
    const close = Number(item?.close)
    const volume = Math.max(0, Number(item?.volume ?? 0))
    if (!Number.isFinite(close)) {
      return null
    }

    cumulativeVolume += volume
    cumulativeTurnover += close * volume
    return cumulativeVolume > 0 ? cumulativeTurnover / cumulativeVolume : null
  })
}

const buildKdjCrossMarkers = records => {
  const kdjSeries = calculateKdjValues(records, { calcParams: [9, 3, 3] })
  const markers = []

  for (let index = 1; index < records.length; index += 1) {
    const previous = kdjSeries[index - 1]
    const current = kdjSeries[index]
    const high = Number(records[index]?.high)
    const low = Number(records[index]?.low)
    const timestamp = Number(records[index]?.timestamp)

    if (!previous || !current || ![previous.k, previous.d, current.k, current.d, high, low, timestamp].every(Number.isFinite)) {
      continue
    }

    const previousGap = previous.k - previous.d
    const currentGap = current.k - current.d
    if (previousGap <= 0 && currentGap > 0) {
      markers.push({
        id: `kdj-cross-golden-${timestamp}`,
        timestamp,
        value: roundPrice(low * 0.982),
        text: 'KDJ金叉',
        color: '#22c55e'
      })
      continue
    }

    if (previousGap >= 0 && currentGap < 0) {
      markers.push({
        id: `kdj-cross-death-${timestamp}`,
        timestamp,
        value: roundPrice(high * 1.018),
        text: 'KDJ死叉',
        color: '#ef4444'
      })
    }
  }

  return markers
}

const buildBreakoutMarkers = records => {
  const lookback = 20
  const volumeWindow = 5
  const markers = []

  for (let index = Math.max(lookback, volumeWindow); index < records.length; index += 1) {
    const priorWindow = records.slice(index - lookback, index)
    const recentVolumes = records.slice(index - volumeWindow, index).map(item => Number(item?.volume))
    const upper = Math.max(...priorWindow.map(item => Number(item?.high)).filter(Number.isFinite))
    const averageVolume = averageNumbers(recentVolumes)
    const current = records[index]
    const close = Number(current?.close)
    const high = Number(current?.high)
    const low = Number(current?.low)
    const volume = Number(current?.volume)
    const timestamp = Number(current?.timestamp)

    if (![upper, averageVolume, close, high, low, volume, timestamp].every(Number.isFinite)) {
      continue
    }

    if (close > upper && volume >= averageVolume * 1.25) {
      markers.push({
        id: `breakout-volume-${timestamp}`,
        timestamp,
        value: roundPrice(low * 0.982),
        text: '放量突破',
        color: '#22c55e'
      })
      continue
    }

    if (high > upper && close <= upper && volume >= averageVolume * 1.05) {
      markers.push({
        id: `breakout-fake-${timestamp}`,
        timestamp,
        value: roundPrice(high * 1.018),
        text: '假突破',
        color: '#ef4444'
      })
    }
  }

  return markers
}

const buildGapMarkers = records => {
  const markers = []
  const openGaps = []

  for (let index = 1; index < records.length; index += 1) {
    const current = records[index]
    const previous = records[index - 1]
    const high = Number(current?.high)
    const low = Number(current?.low)
    const timestamp = Number(current?.timestamp)

    if (![high, low, timestamp].every(Number.isFinite)) {
      continue
    }

    for (let gapIndex = openGaps.length - 1; gapIndex >= 0; gapIndex -= 1) {
      const gap = openGaps[gapIndex]
      if (gap.direction === 'up' && low <= gap.fillPrice) {
        markers.push({
          id: `gap-fill-${gap.id}-${timestamp}`,
          timestamp,
          value: roundPrice(gap.fillPrice),
          text: '回补缺口',
          color: '#f59e0b'
        })
        openGaps.splice(gapIndex, 1)
        continue
      }

      if (gap.direction === 'down' && high >= gap.fillPrice) {
        markers.push({
          id: `gap-fill-${gap.id}-${timestamp}`,
          timestamp,
          value: roundPrice(gap.fillPrice),
          text: '回补缺口',
          color: '#f59e0b'
        })
        openGaps.splice(gapIndex, 1)
      }
    }

    const previousHigh = Number(previous?.high)
    const previousLow = Number(previous?.low)
    if (![previousHigh, previousLow].every(Number.isFinite)) {
      continue
    }

    if (low > previousHigh * 1.002) {
      openGaps.push({ id: `up-${timestamp}`, direction: 'up', fillPrice: previousHigh })
      markers.push({
        id: `gap-up-${timestamp}`,
        timestamp,
        value: roundPrice(low * 0.995),
        text: '高开缺口',
        color: '#22c55e'
      })
      continue
    }

    if (high < previousLow * 0.998) {
      openGaps.push({ id: `down-${timestamp}`, direction: 'down', fillPrice: previousLow })
      markers.push({
        id: `gap-down-${timestamp}`,
        timestamp,
        value: roundPrice(high * 1.005),
        text: '低开缺口',
        color: '#ef4444'
      })
    }
  }

  return markers
}

const buildPriceVolumeDivergenceMarkers = records => {
  const markers = []
  const windowSize = 3
  let lastTopIndex = -Infinity
  let lastBottomIndex = -Infinity

  for (let index = windowSize * 2 - 1; index < records.length; index += 1) {
    const previousWindow = records.slice(index - windowSize * 2 + 1, index - windowSize + 1)
    const recentWindow = records.slice(index - windowSize + 1, index + 1)
    const previousPriceAverage = averageNumbers(previousWindow.map(item => Number(item?.close)))
    const recentPriceAverage = averageNumbers(recentWindow.map(item => Number(item?.close)))
    const previousVolumeAverage = averageNumbers(previousWindow.map(item => Number(item?.volume)))
    const recentVolumeAverage = averageNumbers(recentWindow.map(item => Number(item?.volume)))
    const current = records[index]
    const high = Number(current?.high)
    const low = Number(current?.low)
    const timestamp = Number(current?.timestamp)

    if (![previousPriceAverage, recentPriceAverage, previousVolumeAverage, recentVolumeAverage, high, low, timestamp].every(Number.isFinite)) {
      continue
    }

    if (recentPriceAverage > previousPriceAverage * 1.003 && recentVolumeAverage < previousVolumeAverage * 0.95 && index - lastTopIndex >= 4) {
      markers.push({
        id: `divergence-top-${timestamp}`,
        timestamp,
        value: roundPrice(high * 1.01),
        text: '顶背离',
        color: '#ef4444'
      })
      lastTopIndex = index
      continue
    }

    if (recentPriceAverage < previousPriceAverage * 0.997 && recentVolumeAverage > previousVolumeAverage * 1.05 && index - lastBottomIndex >= 4) {
      markers.push({
        id: `divergence-bottom-${timestamp}`,
        timestamp,
        value: roundPrice(low * 0.99),
        text: '底背离',
        color: '#22c55e'
      })
      lastBottomIndex = index
    }
  }

  return markers
}

const buildVwapStrengthMarkers = records => {
  const vwapSeries = calculateVwapSeries(records)
  const markers = []
  let lastStrengthIndex = -Infinity
  let lastWeaknessIndex = -Infinity

  for (let index = 1; index < records.length; index += 1) {
    const previousClose = Number(records[index - 1]?.close)
    const currentClose = Number(records[index]?.close)
    const previousVwap = Number(vwapSeries[index - 1])
    const currentVwap = Number(vwapSeries[index])
    const high = Number(records[index]?.high)
    const low = Number(records[index]?.low)
    const timestamp = Number(records[index]?.timestamp)

    if (![previousClose, currentClose, previousVwap, currentVwap, high, low, timestamp].every(Number.isFinite)) {
      continue
    }

    if (previousClose <= previousVwap && currentClose > currentVwap && currentClose > previousClose && index - lastStrengthIndex >= 3) {
      markers.push({
        id: `vwap-strength-${timestamp}`,
        timestamp,
        value: roundPrice(low * 0.998),
        text: 'VWAP企稳',
        color: '#22c55e'
      })
      lastStrengthIndex = index
      continue
    }

    if (previousClose >= previousVwap && currentClose < currentVwap && currentClose < previousClose && index - lastWeaknessIndex >= 3) {
      markers.push({
        id: `vwap-weakness-${timestamp}`,
        timestamp,
        value: roundPrice(high * 1.002),
        text: 'VWAP转弱',
        color: '#ef4444'
      })
      lastWeaknessIndex = index
    }
  }

  return markers
}

const CHART_STRATEGIES = Object.freeze([
  createStrategyDefinition({
    id: 'price',
    label: PRICE_LABELS,
    category: 'core',
    kind: 'core',
    accentColor: '#2563eb',
    help: createHelp(
      '展示当前主图的价格轨迹，分时视图显示分时线，K 线视图显示蜡烛。',
      '这是识别趋势方向和波动节奏的主图层，关闭后更适合单独观察副图指标。',
      '默认保持开启；只有在你想专注量能、MACD、RSI 等副图时再临时关闭。'
    ),
    lineLegends: [
      createLineLegend('#2563eb', '主价格线', '当前主图价格轨迹。')
    ],
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
    accentColor: '#64748b',
    help: createHelp(
      '显示每根 bar 对应的成交量柱，帮助判断放量、缩量和承接强弱。',
      '价格上涨但量能跟不上时，趋势延续性往往会变差；放量突破则更可信。',
      '建议与突破、回踩或 AI 价位线联动观察，不要只看价格不看量。'
    ),
    lineLegends: [
      createLineLegend('#64748b', '量柱', '每根 bar 的成交量强弱。')
    ],
    supportedViews: CHART_VIEW_OPTIONS.map(view => view.id),
    defaultVisible: true,
    requires: ['volume'],
    compute: () => ({
      indicators: [
        createIndicatorSpec({
          aggregateKey: 'VOL',
          name: 'VOL',
          shortName: 'VOL',
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
    accentColor: '#64748b',
    help: createHelp(
      '以昨收价为基准画出水平参考线。',
      '分时价格站在昨收线上方，通常代表当日强于前一交易日；跌破则偏弱。',
      '适合和 VWAP、量能一起看盘中强弱，不建议孤立使用。'
    ),
    lineLegends: [
      createLineLegend('#64748b', '昨收线', '前一交易日收盘价的参考水平线。')
    ],
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
    accentColor: '#f97316',
    accentSecondaryColor: '#10b981',
    help: createHelp(
      '显示 AI 推断的关键支撑位与压力位。',
      '橙色通常对应压力，绿色通常对应支撑；越接近这些价位，市场反应越值得观察。',
      '只能作为参考层，必须和真实量价、消息、趋势一起判断，不能单独当交易信号。'
    ),
    lineLegends: [
      createLineLegend('#f97316', '压力位', 'AI 推断的上方阻力价格。'),
      createLineLegend('#10b981', '支撑位', 'AI 推断的下方支撑价格。')
    ],
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
    accentColor: '#f59e0b',
    help: createHelp(
      '5 日均线，代表最近 5 个交易日的平均成本。',
      '对短线节奏最敏感，拐头速度快，适合观察超短和短线趋势变化。',
      '建议和 MA10/MA20 对照使用；单独贴近价格时更容易被震荡反复打脸。'
    ),
    lineLegends: [
      createLineLegend('#f59e0b', 'MA5', '5 个周期的平均成本线。')
    ],
    supportedViews: KLINE_VIEW_IDS,
    defaultVisible: true,
    requires: ['close'],
    compute: () => ({ indicators: [createIndicatorSpec({ aggregateKey: 'MA', name: 'MA', shortName: 'MA', paneId: CANDLE_PANE_ID, paneOptions: { id: CANDLE_PANE_ID }, series: 'price', calcParams: [5], order: 40 })] })
  }),
  createStrategyDefinition({
    id: 'ma10',
    label: 'MA10',
    category: 'trend',
    kind: 'indicator',
    accentColor: '#8b5cf6',
    help: createHelp(
      '10 日均线，代表最近 10 个交易日的平均成本。',
      '相对 MA5 更稳，常用于确认短线趋势是否真正延续。',
      '适合和 MA5 做快慢线对照：MA5 上穿 MA10 常被视为短线转强参考。'
    ),
    lineLegends: [
      createLineLegend('#8b5cf6', 'MA10', '10 个周期的平均成本线。')
    ],
    supportedViews: KLINE_VIEW_IDS,
    defaultVisible: true,
    requires: ['close'],
    compute: () => ({ indicators: [createIndicatorSpec({ aggregateKey: 'MA', name: 'MA', shortName: 'MA', paneId: CANDLE_PANE_ID, paneOptions: { id: CANDLE_PANE_ID }, series: 'price', calcParams: [10], order: 41 })] })
  }),
  createStrategyDefinition({
    id: 'ma20',
    label: 'MA20',
    category: 'trend',
    kind: 'indicator',
    accentColor: '#06b6d4',
    help: createHelp(
      '20 日均线，接近一个自然月的平均持仓成本。',
      '常作为趋势股的重要支撑或压力带，跌破后中短期结构会明显变弱。',
      '更适合波段视角；若只是看日内节奏，可临时关闭避免主图过密。'
    ),
    lineLegends: [
      createLineLegend('#06b6d4', 'MA20', '20 个周期的平均成本线。')
    ],
    supportedViews: KLINE_VIEW_IDS,
    defaultVisible: false,
    requires: ['close'],
    compute: () => ({ indicators: [createIndicatorSpec({ aggregateKey: 'MA', name: 'MA', shortName: 'MA', paneId: CANDLE_PANE_ID, paneOptions: { id: CANDLE_PANE_ID }, series: 'price', calcParams: [20], order: 42 })] })
  }),
  createStrategyDefinition({
    id: 'ma60',
    label: 'MA60',
    category: 'trend',
    kind: 'indicator',
    accentColor: '#ef4444',
    help: createHelp(
      '60 日均线，接近一个季度的平均成本。',
      '常用于判断中期强弱分界，能明显区分“回调中的强趋势”和“趋势已坏”。',
      '通常不需要一直开着；做中线趋势研判时打开价值更高。'
    ),
    lineLegends: [
      createLineLegend('#ef4444', 'MA60', '60 个周期的平均成本线。')
    ],
    supportedViews: KLINE_VIEW_IDS,
    defaultVisible: false,
    requires: ['close'],
    compute: () => ({ indicators: [createIndicatorSpec({ aggregateKey: 'MA', name: 'MA', shortName: 'MA', paneId: CANDLE_PANE_ID, paneOptions: { id: CANDLE_PANE_ID }, series: 'price', calcParams: [60], order: 43 })] })
  }),
  createStrategyDefinition({
    id: 'vwap',
    label: 'VWAP',
    category: 'trend',
    kind: 'indicator',
    accentColor: '#0f766e',
    help: createHelp(
      'VWAP 是成交量加权平均价，强调真实成交重心。',
      '价格站稳 VWAP 往往说明盘中承接更强；跌回 VWAP 下方则代表强度减弱。',
      '主要用于分时图，适合和昨收线、量能、ORB 一起看盘中强弱。'
    ),
    lineLegends: [
      createLineLegend('#0f766e', 'VWAP', '成交量加权平均价主线。')
    ],
    supportedViews: ['minute'],
    defaultVisible: true,
    requires: ['close', 'volume'],
    compute: () => ({ indicators: [createIndicatorSpec({ aggregateKey: 'VWAP', name: 'VWAP', shortName: 'VWAP', paneId: CANDLE_PANE_ID, paneOptions: { id: CANDLE_PANE_ID }, series: 'price', order: 45 })] })
  }),
  createStrategyDefinition({
    id: 'boll',
    label: 'BOLL',
    category: 'trend',
    kind: 'indicator',
    accentColor: '#14b8a6',
    help: createHelp(
      '布林带由中轨和上下轨组成，用于观察波动率扩张与收敛。',
      '开口扩大通常意味着趋势加速，通道收窄则常见于整理或变盘前。',
      '不要把触碰上轨简单当成卖点，趋势行情里价格可以沿轨运行很久。'
    ),
    lineLegends: [
      createLineLegend('#f59e0b', '上轨', '波动上边界，代表强势区上沿。'),
      createLineLegend('#a855f7', '中轨', '布林中线，常作为均值回归参考。'),
      createLineLegend('#38bdf8', '下轨', '波动下边界，代表弱势区下沿。')
    ],
    supportedViews: KLINE_VIEW_IDS,
    defaultVisible: false,
    requires: ['high', 'low', 'close'],
    compute: () => ({ indicators: [createIndicatorSpec({ aggregateKey: 'BOLL', name: 'BOLL', shortName: 'BOLL', paneId: CANDLE_PANE_ID, paneOptions: { id: CANDLE_PANE_ID }, series: 'price', calcParams: [20, 2], order: 46 })] })
  }),
  createStrategyDefinition({
    id: 'donchian',
    label: 'Donchian',
    category: 'trend',
    kind: 'indicator',
    accentColor: '#22c55e',
    help: createHelp(
      'Donchian 通道用最近一段时间的最高价和最低价构造突破区间。',
      '价格突破上轨更容易被视为趋势延续，跌破下轨则代表弱化。',
      '适合配合量能与突破信号一起看，单独使用容易被假突破欺骗。'
    ),
    lineLegends: [
      createLineLegend('#f59e0b', '上轨', '最近窗口最高价形成的上边界。'),
      createLineLegend('#a855f7', '中轨', '上轨和下轨的中值参考。'),
      createLineLegend('#38bdf8', '下轨', '最近窗口最低价形成的下边界。')
    ],
    supportedViews: KLINE_VIEW_IDS,
    defaultVisible: false,
    requires: ['high', 'low'],
    compute: () => ({ indicators: [createIndicatorSpec({ aggregateKey: 'DONCHIAN', name: 'DONCHIAN', shortName: 'DON', paneId: CANDLE_PANE_ID, paneOptions: { id: CANDLE_PANE_ID }, series: 'price', calcParams: [20], order: 47 })] })
  }),
  createStrategyDefinition({
    id: 'macd',
    label: 'MACD',
    category: 'oscillator',
    kind: 'indicator',
    accentColor: '#ec4899',
    help: createHelp(
      'MACD 用快慢均线差和柱体变化衡量趋势动能。',
      'DIFF、DEA 与柱体同向扩张时，趋势延续性通常更高；背离则提示动能衰减。',
      '适合和主图趋势一起用，少在纯震荡区间内把每次金叉都当成买点。'
    ),
    lineLegends: [
      createLineLegend('#f59e0b', 'DIFF', '快线，反映短周期与长周期均值差。'),
      createLineLegend('#a855f7', 'DEA', '慢线，对 DIFF 做平滑后的信号线。'),
      createLineLegend('#64748b', 'MACD 柱', 'DIFF 与 DEA 的差值柱体，代表动能强弱。')
    ],
    supportedViews: KLINE_VIEW_IDS,
    defaultVisible: false,
    requires: ['close'],
    compute: () => ({ indicators: [createIndicatorSpec({ aggregateKey: 'MACD', name: 'MACD', shortName: 'MACD', paneId: MACD_PANE_ID, paneOptions: { id: MACD_PANE_ID, height: 88, minHeight: 64 }, calcParams: [12, 26, 9], isStack: true, order: 60 })] })
  }),
  createStrategyDefinition({
    id: 'rsi',
    label: 'RSI',
    category: 'oscillator',
    kind: 'indicator',
    accentColor: '#f97316',
    help: createHelp(
      'RSI 衡量一段时间内上涨与下跌力度的相对强弱。',
      '高位持续强势并不一定意味着马上见顶，真正要警惕的是高位背离。',
      '更适合看强弱变化和背离，不建议把 70/30 当成机械化买卖线。'
    ),
    lineLegends: [
      createLineLegend('#f59e0b', 'RSI6', '6 周期 RSI，最敏感，变化最快。'),
      createLineLegend('#a855f7', 'RSI12', '12 周期 RSI，中等节奏的强弱线。'),
      createLineLegend('#38bdf8', 'RSI24', '24 周期 RSI，最平稳，偏中周期强弱。')
    ],
    supportedViews: KLINE_VIEW_IDS,
    defaultVisible: false,
    requires: ['close'],
    compute: () => ({ indicators: [createIndicatorSpec({ aggregateKey: 'RSI', name: 'RSI', shortName: 'RSI', paneId: RSI_PANE_ID, paneOptions: { id: RSI_PANE_ID, height: 88, minHeight: 64 }, calcParams: [6, 12, 24], isStack: true, order: 61 })] })
  }),
  createStrategyDefinition({
    id: 'atr',
    label: 'ATR',
    category: 'oscillator',
    kind: 'indicator',
    accentColor: '#14b8a6',
    help: createHelp(
      'ATR 用真实波幅衡量最近一段时间的价格波动强度。',
      'ATR 走高通常代表波动放大，ATR 走低则说明市场进入收敛阶段。',
      '它更适合辅助设置止损距离和判断节奏，不适合单独拿来判断涨跌方向。'
    ),
    lineLegends: [
      createLineLegend('#14b8a6', 'ATR14', '14 周期真实波幅均值，用来观察波动率变化。')
    ],
    supportedViews: KLINE_VIEW_IDS,
    defaultVisible: false,
    requires: ['high', 'low', 'close'],
    compute: () => ({
      indicators: [
        createIndicatorSpec({
          aggregateKey: 'ATR',
          name: 'ATR',
          shortName: 'ATR',
          paneId: ATR_PANE_ID,
          paneOptions: { id: ATR_PANE_ID, height: 88, minHeight: 64 },
          calcParams: [14],
          styles: {
            lines: [
              { color: '#14b8a6', size: 2, style: 'solid', dashedValue: [0, 0] }
            ]
          },
          isStack: true,
          order: 62
        })
      ]
    })
  }),
  createStrategyDefinition({
    id: 'kdj',
    label: 'KDJ',
    category: 'oscillator',
    kind: 'indicator',
    accentColor: '#ff005c',
    help: createHelp(
      'KDJ 通过随机指标观察短线超买超卖与拐点。',
      '对短线节奏反应很快，但噪音也更大，趋势强时容易连续钝化。',
      '建议只把它当成辅助节奏工具，最好配合主趋势和量能一起判断。'
    ),
    lineLegends: [
      createLineLegend('#ff005c', 'K 线', '快速随机值，对短线波动最敏感。'),
      createLineLegend('#39ff14', 'D 线', 'K 线的平滑线，用于确认节奏。'),
      createLineLegend('#00e5ff', 'J 线', '放大后的敏感线，拐点最快但噪音最大。')
    ],
    supportedViews: ['day'],
    defaultVisible: false,
    requires: ['high', 'low', 'close'],
    compute: () => ({
      indicators: KDJ_LINE_DEFINITIONS.map((line, index) => createIndicatorSpec({
        aggregateKey: `KDJ-${line.key.toUpperCase()}`,
        name: line.name,
        shortName: line.shortName,
        paneId: KDJ_PANE_ID,
        paneOptions: { id: KDJ_PANE_ID, height: 118, minHeight: 90 },
        calcParams: [9, 3, 3],
        styles: {
          lines: [
            { color: line.color, size: line.size, style: line.style, dashedValue: line.dashedValue }
          ]
        },
        isStack: true,
        order: 63 + index
      }))
    })
  }),
  createStrategyDefinition({
    id: 'orb',
    label: 'ORB',
    category: 'signal',
    kind: 'overlay',
    accentColor: '#0f766e',
    accentSecondaryColor: '#dc2626',
    help: createHelp(
      'ORB 是开盘区间突破，通常取开盘后一段时间的高低点作为关键边界。',
      '上破高点偏强，下破低点偏弱；如果很快回到区间内，往往是假突破预警。',
      '更适合分时图观察，必须配合量能与 VWAP，不能只看价格一瞬间刺穿。'
    ),
    lineLegends: [
      createLineLegend('#0f766e', 'ORB 高点', '开盘区间上沿，向上突破偏强。'),
      createLineLegend('#dc2626', 'ORB 低点', '开盘区间下沿，向下跌破偏弱。')
    ],
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
  }),
  createStrategyDefinition({
    id: 'maCross',
    label: 'MA金叉/死叉',
    category: 'signal',
    kind: 'marker',
    accentColor: '#22c55e',
    accentSecondaryColor: '#ef4444',
    help: createHelp(
      'MA 金叉/死叉用于标记 MA5 与 MA10 的快慢线交叉。',
      'MA5 上穿 MA10 常被视为短线转强信号，MA5 下穿 MA10 则更偏向节奏转弱。',
      '它只能辅助看节奏切换，不能脱离量能和价格结构单独下结论。'
    ),
    lineLegends: [
      createLineLegend('#22c55e', 'MA金叉', 'MA5 自下向上穿过 MA10，偏短线转强。'),
      createLineLegend('#ef4444', 'MA死叉', 'MA5 自上向下跌破 MA10，偏短线转弱。')
    ],
    supportedViews: ['day'],
    defaultVisible: false,
    requires: ['close'],
    compute: ({ records }) => {
      const markers = buildMaCrossMarkers(records)
      return markers.length ? { markers } : null
    }
  }),
  createStrategyDefinition({
    id: 'tdSequential',
    label: 'TD九转',
    category: 'signal',
    kind: 'marker',
    accentColor: '#22c55e',
    accentSecondaryColor: '#ef4444',
    help: createHelp(
      'TD 九转用收盘价相对四根之前收盘价的连续强弱，寻找短线衰竭点。',
      '6 和 7 适合作为后半段序列预警，8 和 9 通常更接近短线衰竭观察位。',
      '它更适合和趋势、量能、支撑阻力共振使用，单独出现时不要把它当成必然拐点。'
    ),
    lineLegends: [
      createLineLegend('#86efac', 'TD买6-7', '买方序列的弱提示，表示下行衰竭已进入后半段。'),
      createLineLegend('#22c55e', 'TD买8-9', '买方序列的强提示，通常更接近反抽观察位。'),
      createLineLegend('#fca5a5', 'TD卖6-7', '卖方序列的弱提示，表示上行过热已进入后半段。'),
      createLineLegend('#ef4444', 'TD卖8-9', '卖方序列的强提示，通常更接近回落观察位。')
    ],
    supportedViews: ['day'],
    defaultVisible: false,
    requires: ['close'],
    compute: ({ records }) => {
      const markers = buildTdSequentialMarkers(records)
      return markers.length ? { markers } : null
    }
  }),
  createStrategyDefinition({
    id: 'minuteTdSequential',
    label: '分时九转',
    category: 'signal',
    kind: 'marker',
    accentColor: '#22c55e',
    accentSecondaryColor: '#ef4444',
    help: createHelp(
      '分时九转沿用 TD setup 的四周期比较逻辑，用分时收盘价寻找盘中衰竭位。',
      '当分时连续 9 根都弱于或强于各自 4 根之前的收盘价时，往往说明盘中节奏已进入后半段。',
      '它只适合盘中节奏观察，必须和 VWAP、量能、关键价位一起看，不能单独当成反转确认。'
    ),
    lineLegends: [
      createLineLegend('#86efac', '分时买6-7', '盘中买方序列后半段预警，说明下行节奏正在接近衰竭。'),
      createLineLegend('#22c55e', '分时买8-9', '盘中买方序列强提示，通常更接近分时反抽观察位。'),
      createLineLegend('#fca5a5', '分时卖6-7', '盘中卖方序列后半段预警，说明上行节奏已明显过热。'),
      createLineLegend('#ef4444', '分时卖8-9', '盘中卖方序列强提示，通常更接近分时回落观察位。')
    ],
    supportedViews: ['minute'],
    defaultVisible: false,
    requires: ['close'],
    compute: ({ records }) => {
      const markers = buildTdSequentialMarkers(records, {
        priceOffsets: MINUTE_TD_MARKER_PRICE_OFFSETS,
        collapseCompletedRuns: true
      })
      return markers.length ? { markers } : null
    }
  }),
  createStrategyDefinition({
    id: 'macdCross',
    label: 'MACD金叉/死叉',
    category: 'signal',
    kind: 'marker',
    accentColor: '#22c55e',
    accentSecondaryColor: '#ef4444',
    help: createHelp(
      'MACD 金叉/死叉用于标记 DIFF 与 DEA 的快慢线交叉。',
      'DIFF 上穿 DEA 常被视为节奏修复或转强信号，DIFF 下穿 DEA 则更偏向动能转弱。',
      '它属于动量确认类信号，最好和价格结构、量能以及趋势位置一起看。'
    ),
    lineLegends: [
      createLineLegend('#22c55e', 'MACD金叉', 'DIFF 自下向上穿过 DEA，偏向动量修复。'),
      createLineLegend('#ef4444', 'MACD死叉', 'DIFF 自上向下跌破 DEA，偏向动量转弱。')
    ],
    supportedViews: ['day'],
    defaultVisible: false,
    requires: ['close'],
    compute: ({ records }) => {
      const markers = buildMacdCrossMarkers(records)
      return markers.length ? { markers } : null
    }
  }),
  createStrategyDefinition({
    id: 'kdjCross',
    label: 'KDJ金叉/死叉',
    category: 'signal',
    kind: 'marker',
    accentColor: '#22c55e',
    accentSecondaryColor: '#ef4444',
    help: createHelp(
      'KDJ 金叉/死叉用于标记 K 线与 D 线的短线节奏交叉。',
      'K 上穿 D 常被视为超短修复信号，K 下穿 D 则代表节奏转弱。',
      'KDJ 噪音较大，只适合辅助确认短线拐点，不能脱离趋势与量能单独使用。'
    ),
    lineLegends: [
      createLineLegend('#22c55e', 'KDJ金叉', 'K 线自下向上穿过 D 线，偏向短线修复。'),
      createLineLegend('#ef4444', 'KDJ死叉', 'K 线自上向下跌破 D 线，偏向短线转弱。')
    ],
    supportedViews: ['day'],
    defaultVisible: false,
    requires: ['high', 'low', 'close'],
    compute: ({ records }) => {
      const markers = buildKdjCrossMarkers(records)
      return markers.length ? { markers } : null
    }
  }),
  createStrategyDefinition({
    id: 'breakoutSignals',
    label: '放量突破/假突破',
    category: 'signal',
    kind: 'marker',
    accentColor: '#22c55e',
    accentSecondaryColor: '#ef4444',
    help: createHelp(
      '放量突破/假突破用于标记价格越过近期区间上沿后的有效性。',
      '收盘站上区间上沿且放量更偏向有效突破，只是盘中冲高但收不住更偏向假突破。',
      '它适合和 Donchian、量能、趋势位置一起看，避免把所有冲高都当成新趋势。'
    ),
    lineLegends: [
      createLineLegend('#22c55e', '放量突破', '收盘有效站上近期上沿且量能明显放大。'),
      createLineLegend('#ef4444', '假突破', '盘中冲破上沿但收盘回落，偏向诱多。')
    ],
    supportedViews: ['day'],
    defaultVisible: false,
    requires: ['high', 'low', 'close', 'volume'],
    compute: ({ records }) => {
      const markers = buildBreakoutMarkers(records)
      return markers.length ? { markers } : null
    }
  }),
  createStrategyDefinition({
    id: 'gapSignals',
    label: '缺口',
    category: 'signal',
    kind: 'marker',
    accentColor: '#22c55e',
    accentSecondaryColor: '#ef4444',
    help: createHelp(
      '缺口信号用于标记高开缺口、低开缺口以及后续回补。',
      '向上跳空通常代表情绪快速强化，向下跳空则代表情绪快速转弱；回补缺口意味着原来的跳空驱动被部分消化。',
      '缺口的意义取决于位置和量能，不能只看有没有跳空。'
    ),
    lineLegends: [
      createLineLegend('#22c55e', '高开缺口', '当日最低价仍高于前一日最高价。'),
      createLineLegend('#ef4444', '低开缺口', '当日最高价仍低于前一日最低价。'),
      createLineLegend('#f59e0b', '回补缺口', '后续价格重新回到此前缺口边界。')
    ],
    supportedViews: ['day'],
    defaultVisible: false,
    requires: ['high', 'low'],
    compute: ({ records }) => {
      const markers = buildGapMarkers(records)
      return markers.length ? { markers } : null
    }
  }),
  createStrategyDefinition({
    id: 'priceVolumeDivergence',
    label: '量价背离',
    category: 'signal',
    kind: 'marker',
    accentColor: '#22c55e',
    accentSecondaryColor: '#ef4444',
    help: createHelp(
      '量价背离用于标记价格和成交量节奏不同步的时刻。',
      '价升量缩更容易提示冲高乏力，价跌量增则更容易提示恐慌释放后的低位承接。',
      '它更适合分时观察，主要是提醒你别把单边价格走势直接当成真实强弱。'
    ),
    lineLegends: [
      createLineLegend('#ef4444', '顶背离', '价格抬升但量能衰减，偏向冲高乏力。'),
      createLineLegend('#22c55e', '底背离', '价格走低但量能回升，偏向恐慌释放后的承接。')
    ],
    supportedViews: ['minute'],
    defaultVisible: false,
    requires: ['close', 'volume'],
    compute: ({ records }) => {
      const markers = buildPriceVolumeDivergenceMarkers(records)
      return markers.length ? { markers } : null
    }
  }),
  createStrategyDefinition({
    id: 'vwapStrength',
    label: 'VWAP强弱',
    category: 'signal',
    kind: 'marker',
    accentColor: '#22c55e',
    accentSecondaryColor: '#ef4444',
    help: createHelp(
      'VWAP 强弱用于标记价格重新站上或跌回成交量加权均价的节奏变化。',
      '回踩后重新站上 VWAP 更偏向承接企稳，跌破 VWAP 则代表盘中强度减弱。',
      '它最适合分时图，只能辅助判断盘中节奏，不能替代主趋势判断。'
    ),
    lineLegends: [
      createLineLegend('#22c55e', 'VWAP企稳', '价格重新站上 VWAP，偏向盘中承接修复。'),
      createLineLegend('#ef4444', 'VWAP转弱', '价格跌回 VWAP 下方，偏向盘中强度减弱。')
    ],
    supportedViews: ['minute'],
    defaultVisible: false,
    requires: ['close', 'volume'],
    compute: ({ records }) => {
      const markers = buildVwapStrengthMarkers(records)
      return markers.length ? { markers } : null
    }
  }),
  createStrategyDefinition({
    id: 'retailHeat',
    label: '散户帖子数',
    category: 'signal',
    kind: 'indicator',
    accentColor: '#f59e0b',
    help: createHelp(
      '散户论坛帖子数：直接显示各平台当日新增帖子数，帮助直观了解散户活跃度。',
      '柱状图高度代表当日新增帖子数量，留空的日期表示当日无采集数据。',
      '数据来源：东方财富股吧、新浪股吧、淘股吧。'
    ),
    lineLegends: [
      createLineLegend('#f59e0b', '日增帖', '当日各平台新增帖子数'),
      createLineLegend('#9ca3af', '无数据', '该日期未采集到数据（留空）')
    ],
    supportedViews: ['day'],
    defaultVisible: false,
    requires: [],
    compute: ({ records }) => {
      return {
        indicators: [
          createIndicatorSpec({
            aggregateKey: 'RETAIL_HEAT',
            name: 'RETAIL_HEAT',
            shortName: '日增帖',
            paneId: RETAIL_HEAT_PANE_ID,
            paneOptions: { id: RETAIL_HEAT_PANE_ID, height: 120, minHeight: 80 },
            isStack: true,
            order: 70
          })
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
    { paneId: ATR_PANE_ID, name: 'ATR' },
    { paneId: KDJ_PANE_ID, name: 'KDJ' },
    { paneId: KDJ_PANE_ID, name: 'KDJ_VISUAL' },
    { paneId: KDJ_PANE_ID, name: 'KDJ_K_VISUAL' },
    { paneId: KDJ_PANE_ID, name: 'KDJ_D_VISUAL' },
    { paneId: KDJ_PANE_ID, name: 'KDJ_J_VISUAL' },
    { paneId: RETAIL_HEAT_PANE_ID, name: 'RETAIL_HEAT' }
  ],
  month: [
    { paneId: CANDLE_PANE_ID, name: 'MA' },
    { paneId: CANDLE_PANE_ID, name: 'BOLL' },
    { paneId: CANDLE_PANE_ID, name: 'DONCHIAN' },
    { paneId: VOLUME_PANE_ID, name: 'VOL' },
    { paneId: MACD_PANE_ID, name: 'MACD' },
    { paneId: RSI_PANE_ID, name: 'RSI' },
    { paneId: ATR_PANE_ID, name: 'ATR' }
  ],
  year: [
    { paneId: CANDLE_PANE_ID, name: 'MA' },
    { paneId: CANDLE_PANE_ID, name: 'BOLL' },
    { paneId: CANDLE_PANE_ID, name: 'DONCHIAN' },
    { paneId: VOLUME_PANE_ID, name: 'VOL' },
    { paneId: MACD_PANE_ID, name: 'MACD' },
    { paneId: RSI_PANE_ID, name: 'RSI' },
    { paneId: ATR_PANE_ID, name: 'ATR' }
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

function calculateKdjValues(dataList, indicator) {
  const params = indicator.calcParams
  const result = []
  dataList.forEach((kLineData, index) => {
    const kdj = {}
    const close = Number(kLineData?.close)
    if (index >= params[0] - 1 && Number.isFinite(close)) {
      const window = dataList.slice(index - (params[0] - 1), index + 1)
      const highs = window.map(item => Number(item?.high)).filter(Number.isFinite)
      const lows = window.map(item => Number(item?.low)).filter(Number.isFinite)
      const high = Math.max(...highs)
      const low = Math.min(...lows)
      const span = high - low
      const rsv = ((close - low) / (span === 0 ? 1 : span)) * 100
      const previous = result[index - 1] ?? {}
      kdj.k = ((params[1] - 1) * (Number(previous.k) || 50) + rsv) / params[1]
      kdj.d = ((params[2] - 1) * (Number(previous.d) || 50) + kdj.k) / params[2]
      kdj.j = 3 * kdj.k - 2 * kdj.d
    }
    result.push(kdj)
  })
  return result
}

function registerKdjVisualIndicator() {
  KDJ_LINE_DEFINITIONS.forEach(line => {
    registerIndicator({
      name: line.name,
      shortName: line.shortName,
      precision: 2,
      series: 'normal',
      shouldOhlc: true,
      calcParams: [9, 3, 3],
      figures: [{ key: line.key, title: line.title, type: 'line' }],
      calc: (dataList, indicator) => calculateKdjValues(dataList, indicator).map(item => ({ [line.key]: item[line.key] }))
    })
  })
}

function registerRetailHeatIndicator() {
  registerIndicator({
    name: 'RETAIL_HEAT',
    shortName: '日增帖',
    precision: 0,
    calcParams: [],
    figures: [
      {
        key: 'dailyCount',
        title: '日增帖: ',
        type: 'bar',
        baseValue: 0,
        styles: (data) => {
          const val = data.current?.indicatorData?.dailyCount
          if (!Number.isFinite(val) || val === 0) return {}
          return { color: '#f59e0b' }
        }
      },
      {
        key: 'platformCount',
        title: '平台: ',
        type: 'line',
        styles: () => ({ color: 'rgba(0,0,0,0)' })
      }
    ],
    calc: dataList => dataList.map(item => {
      const heat = item._retailHeat
      if (!heat || !heat.hasData) return {}
      return { dailyCount: heat.dailyCount, platformCount: heat.platformCount }
    })
  })
}

export function ensureChartStrategiesRegistered() {
  if (customIndicatorsRegistered || typeof registerIndicator !== 'function') {
    return
  }
  registerVwapIndicator()
  registerDonchianIndicator()
  registerKdjVisualIndicator()
  registerRetailHeatIndicator()
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
          kind: item.kind,
          accentColor: item.accentColor ?? '#2563eb',
          accentSecondaryColor: item.accentSecondaryColor ?? null,
          description: item.help?.description ?? '',
          interpretation: item.help?.interpretation ?? '',
          usage: item.help?.usage ?? '',
          lineLegends: item.lineLegends ?? []
        }))
    }))
    .filter(group => group.items.length > 0)
}

export function getActiveStrategyBadgesForView(viewId, visibilityState = {}) {
  return getChartStrategiesForView(viewId)
    .filter(item => visibilityState[item.id] !== false)
    .map(item => ({
      id: item.id,
      label: item.resolvedLabel,
      accentColor: item.accentColor ?? '#2563eb',
      accentSecondaryColor: item.accentSecondaryColor ?? null,
      description: item.help?.description ?? '',
      interpretation: item.help?.interpretation ?? '',
      usage: item.help?.usage ?? '',
      lineLegends: item.lineLegends ?? []
    }))
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
    .map(item => ({ ...item, calcParams: [...(item.calcParams ?? [])] }))
    .sort((left, right) => (left.order ?? 0) - (right.order ?? 0))

  return renderPlan
}
