import DOMPurify from 'dompurify'
import { marked } from 'marked'

const KEY_LABELS = {
  agent: '角色',
  summary: '摘要',
  analysis: '分析',
  confidence: '置信度',
  confidenceScore: '置信度',
  confidence_score: '置信度',
  trigger: '触发条件',
  triggers: '触发条件',
  triggerConditions: '触发条件',
  trigger_conditions: '触发条件',
  invalidation: '失效条件',
  invalidations: '失效条件',
  invalidConditions: '失效条件',
  invalid_conditions: '失效条件',
  risk: '风险',
  risks: '风险',
  riskLimit: '风险限制',
  riskLimits: '风险限制',
  reason: '原因',
  direction: '方向',
  rating: '评级',
  symbol: '代码',
  name: '名称',
  sector: '板块',
  source: '来源',
  title: '标题',
  content: '内容',
  excerpt: '摘录',
  publishedAt: '发布时间',
  readMode: '读取方式',
  readStatus: '读取状态',
  peRatio: '市盈率',
  peTtm: '市盈率(TTM)',
  volumeRatio: '量比',
  shareholderCount: '股东户数',
  floatMarketCap: '流通市值',
  marketCap: '总市值',
  turnoverRate: '换手率',
  changePercent: '涨跌幅',
  probability: '概率',
  probabilities: '概率分布',
  evidence: '证据',
  evidenceRefs: '证据引用',
  counterEvidenceRefs: '反证引用',
  keyPoints: '关键要点',
  // 基本面相关
  qualityView: '质量评估',
  valuationView: '估值评估',
  metrics: '财务指标',
  revenue: '营业收入',
  revenueYoY: '营收同比',
  netProfit: '净利润',
  netProfitYoY: '净利同比',
  eps: '每股收益',
  roe: '净资产收益率',
  debtRatio: '资产负债率',
  highlights: '亮点',
  // 新闻相关
  eventBias: '事件偏向',
  impactScore: '影响分数',
  keyEvents: '关键事件',
  sentiment: '情绪',
  coverage: '覆盖范围',
  category: '分类',
  impact: '影响',
  url: '链接',
  positive: '正面',
  neutral: '中性',
  negative: '负面',
  overall: '总体',
  highQualityCount: '高质量数量',
  recentCount: '近期数量',
  note: '备注',
  // 市场分析相关
  trendState: '趋势状态',
  keyLevels: '关键价位',
  support: '支撑位',
  resistance: '压力位',
  vwap: '成交量加权均价',
  ma5: 'MA5',
  ma20: 'MA20',
  indicators: '技术指标',
  signal: '信号',
  interpretation: '解读',
  volumeAnalysis: '成交量分析',
  structureSummary: '结构总结',
  evidenceTable: '证据表',
  indicator: '技术指标',
  currentValue: '当前值',
  significance: '重要性',
  // 产品/公司相关
  aspect: '层面',
  finding: '发现',
  competitiveAdvantage: '竞争优势',
  competitive_advantage: '竞争优势',
  // 社交情绪相关
  topic: '话题',
  heatLevel: '热度等级',
  heat_level: '热度等级',
  discussionVolume: '讨论热度',
  discussion_volume: '讨论热度',
  riskSignals: '风险信号',
  risk_signals: '风险信号',
  sentimentScore: '情绪评分',
  sentiment_score: '情绪评分',
  sentimentTrend: '情绪趋势',
  sentiment_trend: '情绪趋势',
  hotTopics: '热门话题',
  hot_topics: '热门话题',
  // 其它常见
  period: '周期',
  assessment: '评估',
  value: '字段值',
  holder: '持有人',
  changeType: '变动类型',
  change_type: '变动类型',
  // 推荐系统专用
  sectorName: '板块名称',
  sectorCode: '板块代码',
  mainNetInflow: '主力净流入',
  verdictReason: '裁决理由',
  riskNotes: '风险提示',
  pickType: '选股类型',
  technicalScore: '技术评分',
  supportLevel: '支撑位',
  resistanceLevel: '压力位',
  triggerCondition: '触发条件',
  invalidCondition: '失效条件',
  bullCase: '看多逻辑',
  bearCase: '看空逻辑',
  validityWindow: '有效期',
  overallConfidence: '总体置信度',
  marketSentiment: '市场情绪',
  toolUsageSummary: '工具调用统计',
  candidateSectors: '候选板块',
  catalysts: '催化剂',
  resonanceSectors: '共振板块',
  bullPoints: '看多要点',
  bearPoints: '看空要点',
  selectedSectors: '入选板块',
  eliminatedSectors: '淘汰板块',
  buyLogic: '买入逻辑',
  riskLevel: '风险等级',
  keyDrivers: '关键驱动',
  globalContext: '全球背景',
  policySignals: '政策信号',
  anomalies: '异常信号',
  volumeAssessment: '成交量评估',
  strategySignals: '策略信号',
  verdict: '裁决结论',
  maxLossEstimate: '最大亏损估计',
  recommendation: '建议结论',
  counterArguments: '反驳要点'
}

const SIGNAL_LABELS = {
  death: '死叉',
  golden: '金叉',
  oversold: '超卖',
  overbought: '超买',
  strength: '强势',
  weakness: '弱势',
  bullish: '看涨',
  bearish: '看跌',
  setup_down: '下跌序列',
  setup_up: '上涨序列',
  countdown_buy: '买入倒计',
  countdown_sell: '卖出倒计',
  neutral: '中性',
  strong_buy: '强烈看好',
  buy: '看好',
  hold: '持有观望',
  sell: '看空',
  strong_sell: '强烈看空',
  // 数据读取状态
  url_fetched: '网页抓取',
  summary_only: '仅摘要',
  local_fact: '本地资讯',
  full_text: '全文',
  not_read: '未读取',
  // MCP工具名 → 中文
  companyoverviewmcp: '公司概况',
  marketcontextmcp: '市场背景',
  technicalmcp: '技术分析',
  fundamentalsmcp: '基本面分析',
  newsmcp: '新闻工具',
  socialmcp: '社交舆情',
  shareholdermcp: '股东分析',
  announcementmcp: '公告分析',
  stockproductmcp: '产品分析',
  stockklinemcp: 'K线数据',
  stockminutemcp: '分时数据',
  stocknewsmcp: '个股新闻',
  stocksearchmcp: '股票搜索',
  stockdetailmcp: '股票详情',
  sectorrotationmcp: '板块轮动'
}

export const translateSignal = value => {
  if (typeof value !== 'string') return String(value ?? '')
  const trimmed = value.trim().toLowerCase()
  return SIGNAL_LABELS[trimmed] || value
}

let _translationsLoaded = false
const _pendingKeys = new Set()
let _batchTimer = null

/** Load backend translation dictionary into KEY_LABELS (call once on app/workbench mount) */
export const loadTranslations = async () => {
  if (_translationsLoaded) return
  try {
    const resp = await fetch('/api/stocks/translations/json-keys')
    if (resp.ok) {
      const data = await resp.json()
      if (data && typeof data === 'object') {
        // Only add backend translations for keys NOT already defined locally
        for (const [k, v] of Object.entries(data)) {
          if (!KEY_LABELS[k] && v && typeof v === 'string') KEY_LABELS[k] = v
        }
      }
    }
    _translationsLoaded = true
  } catch {
    // Silently fail — frontend fallback still works
  }
}

/** Queue unknown keys for batch backend translation */
const queueForTranslation = key => {
  if (!key || KEY_LABELS[key]) return
  _pendingKeys.add(key)
  if (_batchTimer) return
  _batchTimer = setTimeout(async () => {
    _batchTimer = null
    const keys = [..._pendingKeys]
    _pendingKeys.clear()
    if (keys.length === 0) return
    try {
      const resp = await fetch('/api/stocks/translations/json-keys', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(keys)
      })
      if (resp.ok) {
        const data = await resp.json()
        if (data && typeof data === 'object') {
          for (const [k, v] of Object.entries(data)) {
            if (!KEY_LABELS[k] && v && typeof v === 'string') KEY_LABELS[k] = v
          }
        }
      }
    } catch {
      // Silently fail
    }
  }, 2000)
}

const isPlainObject = value => value && typeof value === 'object' && !Array.isArray(value)

const normalizeKey = key => {
  const trimmed = String(key || '').trim()
  // Convert space-separated keys like "heat Level" to camelCase "heatLevel"
  if (trimmed.includes(' ')) {
    return trimmed.replace(/\s+(.)/g, (_, c) => c.toUpperCase()).replace(/^\w/, c => c.toLowerCase())
  }
  return trimmed
}

const toLabel = key => {
  const normalized = normalizeKey(key)
  if (!normalized) return '字段'
  if (KEY_LABELS[normalized]) return KEY_LABELS[normalized]

  // Queue for async backend translation
  queueForTranslation(normalized)

  const camelSpaced = normalized
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/_/g, ' ')
    .trim()

  return camelSpaced
    .split(/\s+/)
    .map(token => token.charAt(0).toUpperCase() + token.slice(1))
    .join(' ')
}

const toPercent = value => {
  const n = Number(value)
  if (!Number.isFinite(n)) return null
  if (n >= 0 && n <= 1) return `${Math.round(n * 100)}%`
  if (n > 1 && n <= 100) return `${Math.round(n)}%`
  return null
}

const formatScalar = (value, key = '') => {
  if (value === null || value === undefined || value === '') return '-'
  if (typeof value === 'boolean') return value ? '是' : '否'

  if (typeof value === 'number' && Number.isFinite(value)) {
    const keyLower = normalizeKey(key).toLowerCase()
    if (keyLower.includes('confidence') || keyLower.includes('probability') || keyLower.includes('percent') || keyLower.includes('rate')) {
      const percent = toPercent(value)
      if (percent) return percent
    }
    if (keyLower.includes('volume') && keyLower.includes('ratio')) {
      return value.toFixed(2)
    }
    if (keyLower.includes('marketcap')) {
      return `${(value / 100000000).toFixed(2)} 亿`
    }
    if (keyLower.includes('shareholdercount')) {
      return value.toLocaleString('zh-CN')
    }
    return String(value)
  }

  if (typeof value === 'string') {
    const trimmed = value.trim()
    if (!trimmed) return '-'

    const date = new Date(trimmed)
    if (!Number.isNaN(date.getTime()) && /\d{4}-\d{2}-\d{2}/.test(trimmed)) {
      return date.toLocaleString('zh-CN', { hour12: false })
    }
    // Translate known signal values to Chinese
    if (SIGNAL_LABELS[trimmed.toLowerCase()]) {
      return SIGNAL_LABELS[trimmed.toLowerCase()]
    }
    return trimmed
  }

  return String(value)
}

export const parseJsonIfPossible = input => {
  if (typeof input !== 'string') return input
  const text = input.trim()
  if (!text) return input
  if (!((text.startsWith('{') && text.endsWith('}')) || (text.startsWith('[') && text.endsWith(']')))) {
    return input
  }
  try {
    return JSON.parse(text)
  } catch {
    return input
  }
}

const objectToMarkdownLines = (obj, indent = '') => {
  const lines = []
  for (const [rawKey, rawValue] of Object.entries(obj || {})) {
    const key = normalizeKey(rawKey)
    const value = typeof rawValue === 'string' ? parseJsonIfPossible(rawValue) : rawValue
    const label = toLabel(key)

    if (Array.isArray(value)) {
      if (value.length === 0) continue
      const scalarOnly = value.every(item => !isPlainObject(item) && !Array.isArray(item))
      if (scalarOnly) {
        const text = value.map(item => formatScalar(item, key)).join('；')
        lines.push(`${indent}- **${label}**: ${text}`)
      } else {
        lines.push(`${indent}- **${label}**:`)
        value.forEach((item, index) => {
          if (isPlainObject(item)) {
            lines.push(`${indent}  - 条目 ${index + 1}:`)
            lines.push(...objectToMarkdownLines(item, `${indent}    `))
          } else if (Array.isArray(item)) {
            const inner = item.map(innerItem => formatScalar(innerItem, key)).join('；')
            lines.push(`${indent}  - 条目 ${index + 1}: ${inner}`)
          } else {
            lines.push(`${indent}  - ${formatScalar(item, key)}`)
          }
        })
      }
      continue
    }

    if (isPlainObject(value)) {
      lines.push(`${indent}- **${label}**:`)
      lines.push(...objectToMarkdownLines(value, `${indent}  `))
      continue
    }

    lines.push(`${indent}- **${label}**: ${formatScalar(value, key)}`)
  }
  return lines
}

export const jsonToMarkdown = input => {
  const parsed = parseJsonIfPossible(input)

  if (Array.isArray(parsed)) {
    if (parsed.length === 0) return ''
    const scalarOnly = parsed.every(item => !isPlainObject(item) && !Array.isArray(item))
    if (scalarOnly) {
      return parsed.map(item => `- ${formatScalar(item)}`).join('\n')
    }

    const lines = []
    parsed.forEach((item, index) => {
      if (isPlainObject(item)) {
        lines.push(`- 条目 ${index + 1}:`)
        lines.push(...objectToMarkdownLines(item, '  '))
      } else if (Array.isArray(item)) {
        lines.push(`- 条目 ${index + 1}: ${item.map(inner => formatScalar(inner)).join('；')}`)
      } else {
        lines.push(`- ${formatScalar(item)}`)
      }
    })
    return lines.join('\n')
  }

  if (isPlainObject(parsed)) {
    return objectToMarkdownLines(parsed).join('\n')
  }

  return typeof parsed === 'string' ? parsed : formatScalar(parsed)
}

function looksLikeJson(str) {
  const trimmed = str.trim()
  return (trimmed.startsWith('{') || trimmed.startsWith('[')) && trimmed.length > 2
}

function cleanJsonLikeString(str) {
  try {
    let fixed = str.trim()
    const openBraces = (fixed.match(/{/g) || []).length
    const closeBraces = (fixed.match(/}/g) || []).length
    const openBrackets = (fixed.match(/\[/g) || []).length
    const closeBrackets = (fixed.match(/]/g) || []).length
    for (let i = 0; i < openBrackets - closeBrackets; i++) fixed += ']'
    for (let i = 0; i < openBraces - closeBraces; i++) fixed += '}'
    const reparsed = JSON.parse(fixed)
    return jsonToMarkdown(reparsed)
  } catch {
    return str
      .replace(/[{}\[\]"]/g, '')
      .replace(/,\s*/g, '\n')
      .replace(/:\s*/g, ': ')
      .trim()
  }
}

export const ensureMarkdown = input => {
  if (input === null || input === undefined) return ''
  if (typeof input === 'string') {
    const parsed = parseJsonIfPossible(input)
    if (typeof parsed === 'string') {
      if (looksLikeJson(input)) return cleanJsonLikeString(input)
      return parsed
    }
    return jsonToMarkdown(parsed)
  }
  return jsonToMarkdown(input)
}

export const markdownToSafeHtml = markdown => {
  if (!markdown) return ''
  // 如果整段文本被 ```json 或 ``` 包裹，提取内部内容
  const codeFenceMatch = markdown.match(/^\s*```(?:json)?\s*\n([\s\S]*?)\n\s*```\s*$/)
  if (codeFenceMatch) {
    const inner = codeFenceMatch[1].trim()
    try {
      JSON.parse(inner)
      // 有效 JSON，走结构化渲染
      return markdownToSafeHtml(ensureMarkdown(inner))
    } catch {
      // 不是有效 JSON，继续正常 markdown 渲染
    }
  }
  return DOMPurify.sanitize(marked.parse(markdown, { breaks: true }))
}

export const valueToSafeHtml = value => markdownToSafeHtml(ensureMarkdown(value))

export const parseJsonArray = json => {
  if (!json) return []
  try {
    const arr = typeof json === 'string' ? JSON.parse(json) : json
    return Array.isArray(arr) ? arr : []
  } catch {
    return []
  }
}

export const toReadableInlineText = (value, key = '') => {
  const parsed = parseJsonIfPossible(value)
  if (parsed === null || parsed === undefined || parsed === '') return '-'

  if (typeof parsed === 'string' || typeof parsed === 'number' || typeof parsed === 'boolean') {
    return formatScalar(parsed, key)
  }

  if (Array.isArray(parsed)) {
    if (parsed.length === 0) return '-'
    return parsed
      .map(item => toReadableInlineText(item, key))
      .filter(Boolean)
      .join('；')
  }

  if (isPlainObject(parsed)) {
    return Object.entries(parsed)
      .map(([innerKey, raw]) => `${toLabel(innerKey)}: ${toReadableInlineText(raw, innerKey)}`)
      .join('；')
  }

  return String(parsed)
}
