/**
 * V040-S3 财报中心 — 常量定义
 * 报告类型枚举、来源渠道样式、默认查询参数
 */

export const REPORT_TYPE_OPTIONS = [
  { key: 'annual', label: '年报' },
  { key: 'q1', label: '一季报' },
  { key: 'q2', label: '中报' },
  { key: 'q3', label: '三季报' }
]

export const REPORT_TYPE_LABEL = {
  annual: '年报',
  q1: '一季报',
  q2: '中报',
  q3: '三季报'
}

/**
 * 来源渠道 → tag 着色样式
 * 使用 design-tokens 语义色，避免硬编码
 */
export const SOURCE_CHANNEL_STYLE = {
  eastmoney: {
    label: '东方财富',
    bg: 'var(--color-info-bg)',
    color: 'var(--color-info)',
    border: 'var(--color-info-border)'
  },
  sina: {
    label: '新浪财经',
    bg: 'var(--color-warning-bg)',
    color: 'var(--color-warning)',
    border: 'var(--color-warning-border)'
  },
  cninfo: {
    label: '巨潮资讯',
    bg: 'var(--color-success-bg)',
    color: 'var(--color-success)',
    border: 'var(--color-success-border)'
  },
  manual: {
    label: '手动录入',
    bg: 'var(--color-neutral-bg)',
    color: 'var(--color-neutral)',
    border: 'var(--color-neutral-border)'
  }
}

export const FALLBACK_CHANNEL_STYLE = {
  bg: 'var(--color-neutral-bg)',
  color: 'var(--color-neutral)',
  border: 'var(--color-neutral-border)'
}

export const PAGE_SIZE_OPTIONS = [10, 20, 50, 100]

const ONE_YEAR_MS = 365 * 24 * 60 * 60 * 1000

const formatDateOnly = (date) => {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

export const buildDefaultDateRange = () => {
  const now = new Date()
  const start = new Date(now.getTime() - ONE_YEAR_MS)
  return {
    startDate: formatDateOnly(start),
    endDate: formatDateOnly(now)
  }
}

const defaultRange = buildDefaultDateRange()

export const DEFAULT_QUERY = {
  symbols: [],
  startDate: defaultRange.startDate,
  endDate: defaultRange.endDate,
  reportTypes: ['annual', 'q1', 'q2', 'q3'],
  keyword: '',
  page: 1,
  pageSize: 20,
  sortField: 'reportDate',
  sortDirection: 'desc'
}

export const SORT_FIELDS = ['symbol', 'reportDate', 'reportType', 'sourceChannel', 'collectedAt']
