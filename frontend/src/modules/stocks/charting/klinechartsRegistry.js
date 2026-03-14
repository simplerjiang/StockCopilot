const CANDLE_PANE_ID = 'candle_pane'
const VOLUME_PANE_ID = 'volume_pane'

const REGISTRY = Object.freeze({
  minute: {
    indicators: [
      { name: 'VOL', paneId: VOLUME_PANE_ID, paneOptions: { id: VOLUME_PANE_ID, height: 96, minHeight: 72 } }
    ],
    overlays: ['ai-levels', 'base-line'],
    markerMount: 'reserved'
  },
  kline: {
    indicators: [
      { name: 'MA', paneId: CANDLE_PANE_ID, calcParams: [5, 10], paneOptions: { id: CANDLE_PANE_ID } },
      { name: 'VOL', paneId: VOLUME_PANE_ID, paneOptions: { id: VOLUME_PANE_ID, height: 96, minHeight: 72 } }
    ],
    overlays: ['ai-levels'],
    markerMount: 'reserved'
  }
})

const parseLevelValue = value => {
  if (value == null || value === '') return null
  const number = Number(value)
  return Number.isFinite(number) ? number : null
}

const buildPriceLineOverlay = ({ groupId, value, timestamp, color }) => ({
  name: 'priceLine',
  groupId,
  paneId: CANDLE_PANE_ID,
  lock: true,
  points: [{ timestamp, value }],
  styles: {
    line: {
      color,
      style: 'dashed',
      size: 1,
      smooth: false
    },
    text: {
      color,
      backgroundColor: 'rgba(15, 23, 42, 0.88)',
      borderColor: color
    }
  }
})

export function ensureKLineChartsRegistry() {
  return REGISTRY
}

export function syncIndicatorRegistry(chart, viewType) {
  const registry = ensureKLineChartsRegistry()[viewType]
  chart.removeIndicator({ paneId: CANDLE_PANE_ID, name: 'MA' })
  chart.removeIndicator({ paneId: VOLUME_PANE_ID, name: 'VOL' })

  registry.indicators.forEach(item => {
    if (item.name === 'MA') {
      chart.createIndicator(
        {
          name: 'MA',
          shortName: 'MA',
          calcParams: item.calcParams,
          series: 'price'
        },
        false,
        item.paneOptions
      )
      return
    }

    chart.createIndicator(
      {
        name: item.name,
        shortName: item.name,
        series: 'volume'
      },
      true,
      item.paneOptions
    )
  })

  return registry
}

export function syncOverlayRegistry(chart, { viewType, aiLevels, basePrice, firstTimestamp }) {
  const aiGroupId = `${viewType}-ai-levels`
  const baseLineGroupId = `${viewType}-base-line`
  chart.removeOverlay({ groupId: aiGroupId })
  chart.removeOverlay({ groupId: baseLineGroupId })

  if (!Number.isFinite(firstTimestamp)) {
    return
  }

  const overlays = []
  const resistance = parseLevelValue(aiLevels?.resistance)
  const support = parseLevelValue(aiLevels?.support)

  if (Number.isFinite(resistance)) {
    overlays.push(buildPriceLineOverlay({
      groupId: aiGroupId,
      timestamp: firstTimestamp,
      value: resistance,
      color: '#f97316'
    }))
  }

  if (Number.isFinite(support)) {
    overlays.push(buildPriceLineOverlay({
      groupId: aiGroupId,
      timestamp: firstTimestamp,
      value: support,
      color: '#10b981'
    }))
  }

  if (viewType === 'minute' && Number.isFinite(basePrice)) {
    overlays.push(buildPriceLineOverlay({
      groupId: baseLineGroupId,
      timestamp: firstTimestamp,
      value: basePrice,
      color: '#64748b'
    }))
  }

  if (overlays.length) {
    chart.createOverlay(overlays)
  }
}

export function syncMarkerSignalRegistry(chart, { viewType, markers = [] }) {
  chart.removeOverlay({ groupId: `${viewType}-markers` })
  return Array.isArray(markers) ? markers : []
}

export { CANDLE_PANE_ID, VOLUME_PANE_ID }