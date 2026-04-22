/**
 * V040-S5 财报中心详情抽屉 — 三表字段白名单 + 中文 label + fallback 链
 *
 * 每个字段：
 *   key       主字段名（取值优先级最高，大小写不敏感）
 *   label     中文展示名
 *   fallbacks 后端可能返回的备用字段名数组（大小写不敏感、按顺序尝试）
 *
 * V040-S5-FU（B-1）：后端 ths 渠道实际返回中文键，部分带 `*` 前缀
 * （如 `*资产合计`、`*营业总收入`、`*净利润`、`*经营活动产生的现金流量净额`）。
 * 我们：
 *   1) 在 fallbacks 中追加对应中文键（不带 `*` 前缀），
 *   2) 增强 `pickFieldValue` 对 `*` 前缀做透明剥离，
 * 这样无需改后端字段映射即可命中。
 */

export const BALANCE_SHEET_FIELDS = [
  { key: 'totalAssets', label: '总资产', fallbacks: ['资产合计'] },
  { key: 'totalLiabilities', label: '总负债', fallbacks: ['负债合计'] },
  {
    key: 'totalEquity',
    label: '股东权益合计',
    fallbacks: [
      '所有者权益（或股东权益）合计',
      '股东权益合计',
      '归属于母公司所有者权益合计'
    ]
  },
  { key: 'monetaryFunds', label: '货币资金', fallbacks: ['cashAndEquivalents', '货币资金'] },
  { key: 'accountsReceivable', label: '应收账款', fallbacks: ['应收账款'] }
]

export const INCOME_STATEMENT_FIELDS = [
  {
    key: 'revenue',
    label: '营业收入',
    fallbacks: [
      'operatingRevenue',
      'totalRevenue',
      '营业总收入',
      '一、营业总收入',
      '其中：营业收入',
      '营业收入'
    ]
  },
  { key: 'operatingProfit', label: '营业利润', fallbacks: ['三、营业利润', '营业利润'] },
  { key: 'netProfit', label: '净利润', fallbacks: ['netIncome', '净利润', '五、净利润'] },
  {
    key: 'epsBasic',
    label: '基本每股收益',
    fallbacks: ['basicEps', 'eps', '（一）基本每股收益', '基本每股收益']
  },
  { key: 'grossProfit', label: '毛利润', fallbacks: ['毛利润', '营业毛利'] }
]

export const CASH_FLOW_FIELDS = [
  {
    key: 'operatingCashFlow',
    label: '经营活动现金流净额',
    fallbacks: ['netCashFromOperating', '经营活动产生的现金流量净额']
  },
  {
    key: 'investingCashFlow',
    label: '投资活动现金流净额',
    fallbacks: ['netCashFromInvesting', '投资活动产生的现金流量净额']
  },
  {
    key: 'financingCashFlow',
    label: '筹资活动现金流净额',
    fallbacks: ['netCashFromFinancing', '筹资活动产生的现金流量净额']
  },
  {
    key: 'netIncreaseInCash',
    label: '现金及现金等价物净增加额',
    fallbacks: ['现金及现金等价物净增加额']
  },
  {
    key: 'cashEnd',
    label: '期末现金及现金等价物余额',
    fallbacks: ['endingCashBalance', '期末现金及现金等价物余额']
  }
]

/**
 * 在 dict 中按 key + fallbacks 进行大小写不敏感取值。
 * 透明剥离 dict key 上的 `*` 前缀（后端 ths 渠道核心指标使用 `*` 标记，
 * 如 `*资产合计` / `*净利润`），让 fallback 可以用不带前缀的中文键命中。
 *
 * 优先级：原始 key → 剥 `*` 后的 key；命中且值非 null/undefined 才返回。
 *
 * @param {Record<string, any> | null | undefined} dict
 * @param {{key:string, fallbacks?:string[]}} field
 * @returns {any} 命中的原始值；未命中或为 null/undefined 返回 null
 */
export function pickFieldValue(dict, field) {
  if (!dict || typeof dict !== 'object' || !field) return null
  const candidates = [field.key, ...(field.fallbacks || [])]
  // 索引：lowercase → 原始 key
  const indexed = Object.create(null)
  for (const k of Object.keys(dict)) {
    indexed[k.toLowerCase()] = k
  }
  // 二次索引：去掉 `*` 前缀后的 lowercase → 原始 key（仅当未与原始键冲突时）
  for (const k of Object.keys(dict)) {
    if (k.length > 0 && k.charAt(0) === '*') {
      const stripped = k.slice(1).toLowerCase()
      if (!(stripped in indexed)) {
        indexed[stripped] = k
      }
    }
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
