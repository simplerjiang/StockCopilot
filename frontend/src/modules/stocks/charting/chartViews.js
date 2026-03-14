export const CHART_VIEW_OPTIONS = [
  { id: 'minute', label: '分时图', legend: ['分时', '量能', '昨收基线', 'AI 价位'] },
  { id: 'day', label: '日K图', legend: ['蜡烛', '量能', 'MA5', 'MA10', 'AI 价位'] },
  { id: 'month', label: '月K图', legend: ['蜡烛', '量能', 'MA5', 'MA10', 'AI 价位'] },
  { id: 'year', label: '年K图', legend: ['蜡烛', '量能', 'MA5', 'MA10', 'AI 价位'] }
]

export const KLINE_VIEW_IDS = ['day', 'month', 'year']

export const isKlineChartView = viewId => KLINE_VIEW_IDS.includes(viewId)

export const normalizeKlineInterval = interval => (KLINE_VIEW_IDS.includes(interval) ? interval : 'day')

export const resolveInitialChartView = interval => normalizeKlineInterval(interval)
