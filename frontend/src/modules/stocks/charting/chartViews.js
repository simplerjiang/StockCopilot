export const CHART_VIEW_OPTIONS = [
  { id: 'minute', label: '分时图' },
  { id: 'day', label: '日K图' },
  { id: 'month', label: '月K图' },
  { id: 'year', label: '年K图' }
]

export const KLINE_VIEW_IDS = ['day', 'month', 'year']

export const isKlineChartView = viewId => KLINE_VIEW_IDS.includes(viewId)

export const normalizeKlineInterval = interval => (KLINE_VIEW_IDS.includes(interval) ? interval : 'day')

export const resolveInitialChartView = interval => normalizeKlineInterval(interval)
