/**
 * V040-S5 财报中心详情抽屉 — 三表字段白名单 + 中文 label + fallback 链
 *
 * 每个字段：
 *   key       主字段名（取值优先级最高，大小写不敏感）
 *   label     中文展示名
 *   fallbacks 后端可能返回的备用字段名数组（大小写不敏感、按顺序尝试）
 */

export const BALANCE_SHEET_FIELDS = [
  { key: 'totalAssets', label: '总资产', fallbacks: [] },
  { key: 'totalLiabilities', label: '总负债', fallbacks: [] },
  { key: 'totalEquity', label: '股东权益合计', fallbacks: [] },
  { key: 'monetaryFunds', label: '货币资金', fallbacks: ['cashAndEquivalents'] },
  { key: 'accountsReceivable', label: '应收账款', fallbacks: [] }
]

export const INCOME_STATEMENT_FIELDS = [
  { key: 'revenue', label: '营业收入', fallbacks: ['operatingRevenue', 'totalRevenue'] },
  { key: 'operatingProfit', label: '营业利润', fallbacks: [] },
  { key: 'netProfit', label: '净利润', fallbacks: ['netIncome'] },
  { key: 'epsBasic', label: '基本每股收益', fallbacks: ['basicEps', 'eps'] },
  { key: 'grossProfit', label: '毛利润', fallbacks: [] }
]

export const CASH_FLOW_FIELDS = [
  { key: 'operatingCashFlow', label: '经营活动现金流净额', fallbacks: ['netCashFromOperating'] },
  { key: 'investingCashFlow', label: '投资活动现金流净额', fallbacks: ['netCashFromInvesting'] },
  { key: 'financingCashFlow', label: '筹资活动现金流净额', fallbacks: ['netCashFromFinancing'] },
  { key: 'netIncreaseInCash', label: '现金及现金等价物净增加额', fallbacks: [] },
  { key: 'cashEnd', label: '期末现金及现金等价物余额', fallbacks: ['endingCashBalance'] }
]

/**
 * 在 dict 中按 key + fallbacks 进行大小写不敏感取值。
 * @param {Record<string, any> | null | undefined} dict
 * @param {{key:string, fallbacks?:string[]}} field
 * @returns {any} 命中的原始值；未命中或为 null/undefined 返回 null
 */
export function pickFieldValue(dict, field) {
  if (!dict || typeof dict !== 'object' || !field) return null
  const candidates = [field.key, ...(field.fallbacks || [])]
  // 构建小写 key → 原始 key 的索引
  const indexed = {}
  for (const k of Object.keys(dict)) {
    indexed[k.toLowerCase()] = k
  }
  for (const cand of candidates) {
    if (!cand) continue
    const realKey = indexed[String(cand).toLowerCase()]
    if (realKey === undefined) continue
    const v = dict[realKey]
    if (v !== null && v !== undefined) return v
  }
  return null
}

const intFmt = new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 0 })
const decFmt = new Intl.NumberFormat('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })

/**
 * 把财报字段值格式化为可展示字符串。null/undefined/空 → '—'。
 * 数字：整数不带小数；小数保留 2 位。
 */
export function formatFieldValue(value) {
  if (value === null || value === undefined || value === '') return '—'
  if (typeof value === 'number' && Number.isFinite(value)) {
    return Number.isInteger(value) ? intFmt.format(value) : decFmt.format(value)
  }
  if (typeof value === 'string') {
    const trimmed = value.trim()
    if (!trimmed) return '—'
    const num = Number(trimmed)
    if (Number.isFinite(num) && /^-?\d+(\.\d+)?$/.test(trimmed)) {
      return Number.isInteger(num) ? intFmt.format(num) : decFmt.format(num)
    }
    return trimmed
  }
  return String(value)
}
