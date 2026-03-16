import { getIndicatorFiltersForView, getOverlayGroupIdsForView } from './chartStrategyRegistry'
import { CANDLE_PANE_ID, VOLUME_PANE_ID } from './chartPanes'

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

const buildMarkerOverlay = ({ viewId, marker }) => ({
  name: 'simpleAnnotation',
  groupId: `${viewId}-markers`,
  paneId: CANDLE_PANE_ID,
  lock: true,
  points: [{ timestamp: marker.timestamp, value: marker.value }],
  extendData: marker.text,
  styles: {
    line: {
      color: marker.color,
      style: 'solid',
      size: marker.lineSize ?? 1,
      dashedValue: [0, 0]
    },
    polygon: {
      color: marker.color,
      style: 'fill'
    },
    text: {
      color: marker.color,
      size: marker.textSize ?? 12,
      weight: marker.textWeight ?? '600'
    }
  }
})

export function ensureKLineChartsRegistry() {
  return {
    candlePaneId: CANDLE_PANE_ID,
    volumePaneId: VOLUME_PANE_ID
  }
}

export function syncIndicatorRegistry(chart, { viewId, renderPlan }) {
  getIndicatorFiltersForView(viewId).forEach(filter => {
    chart.removeIndicator(filter)
  })

  renderPlan.indicators.forEach(item => {
    const indicator = {
      name: item.name,
      shortName: item.shortName ?? item.name
    }
    if (Array.isArray(item.calcParams) && item.calcParams.length) {
      indicator.calcParams = item.calcParams
    }
    if (item.series) {
      indicator.series = item.series
    }
    if (item.styles) {
      indicator.styles = item.styles
    }

    chart.createIndicator(indicator, item.isStack === true, item.paneOptions)
  })
}

export function syncOverlayRegistry(chart, { viewId, firstTimestamp, renderPlan }) {
  getOverlayGroupIdsForView(viewId).forEach(groupId => {
    chart.removeOverlay({ groupId })
  })

  if (!Number.isFinite(firstTimestamp)) {
    return
  }

  const overlays = renderPlan.overlays
    .filter(item => item.type === 'priceLine' && Number.isFinite(item.value))
    .map(item => buildPriceLineOverlay({
      groupId: item.groupId,
      timestamp: firstTimestamp,
      value: item.value,
      color: item.color
    }))

  if (overlays.length) {
    chart.createOverlay(overlays)
  }
}

export function syncMarkerSignalRegistry(chart, { viewId, renderPlan }) {
  chart.removeOverlay({ groupId: `${viewId}-markers` })

  const markers = Array.isArray(renderPlan.markers) ? renderPlan.markers : []
  if (!markers.length) {
    return []
  }

  chart.createOverlay(markers.map(marker => buildMarkerOverlay({ viewId, marker })))
  return markers
}

export { CANDLE_PANE_ID, VOLUME_PANE_ID }