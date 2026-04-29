<script setup>
import { computed, ref, toRef, watch } from 'vue'
import { getSourceChannelTag, sourceChannelTagStyle } from '../financial/sourceChannelTag.js'
import { formatMoneyDisplay } from '../financial/financialFieldDictionary.js'
import { listPdfFiles, collectPdfFiles } from '../financial/financialApi.js'
import FinancialReportComparePane from '../financial/FinancialReportComparePane.vue'

const props = defineProps({
  symbol: { type: String, default: '' },
  active: { type: Boolean, default: false }
})

const symbolRef = toRef(props, 'symbol')
const loading = ref(false)
const error = ref('')
const trend = ref(null)
const summary = ref(null)
const activeStatement = ref('income')
const collecting = ref(false)
const collectResult = ref(null)
const collectError = ref('')
let fetchRequestId = 0

function getCollectField(result, camelKey, pascalKey) {
  return result?.[camelKey] ?? result?.[pascalKey]
}

function toCollectBoolean(value) {
  if (typeof value === 'boolean') return value
  if (typeof value === 'string') return value.toLowerCase() === 'true'
  if (typeof value === 'number') return value !== 0
  return false
}

function toCollectNumber(value) {
  const num = Number(value)
  return Number.isFinite(num) ? num : 0
}

function pickFirstNonEmptyString(result, ...keys) {
  if (!result || typeof result !== 'object') return ''
  for (const k of keys) {
    const v = result[k]
    if (v == null) continue
    if (typeof v === 'string') {
      if (v.trim() !== '') return v
    } else {
      const s = String(v)
      if (s !== '') return s
    }
  }
  return ''
}

function pickFirstStringFromArray(result, ...keys) {
  if (!result || typeof result !== 'object') return ''
  for (const k of keys) {
    const v = result[k]
    if (Array.isArray(v) && v.length > 0) {
      const first = v[0]
      if (first != null && first !== '') return String(first)
    }
  }
  return ''
}

function normalizeCollectResult(result) {
  if (!result || typeof result !== 'object') {
    return {
      success: false,
      channel: '',
      reportCount: 0,
      durationMs: 0,
      isDegraded: false,
      degradeReason: '',
      errorMessage: '',
      reportPeriod: '',
      reportTitle: '',
      sourceChannel: '',
      fallbackReason: '',
      pdfSummary: ''
    }
  }

  const channel = String(getCollectField(result, 'channel', 'Channel') ?? '')
  const degradeReason = String(getCollectField(result, 'degradeReason', 'DegradeReason') ?? '')

  return {
    success: toCollectBoolean(getCollectField(result, 'success', 'Success')),
    channel,
    reportCount: toCollectNumber(getCollectField(result, 'reportCount', 'ReportCount')),
    durationMs: toCollectNumber(getCollectField(result, 'durationMs', 'DurationMs')),
    isDegraded: toCollectBoolean(getCollectField(result, 'isDegraded', 'IsDegraded')),
    degradeReason,
    errorMessage: String(getCollectField(result, 'errorMessage', 'ErrorMessage') ?? ''),
    // V040-S4 采集结果透明化新字段（含友好别名 + 兜底回退）
    reportPeriod: pickFirstNonEmptyString(result, 'reportPeriod', 'ReportPeriod')
      || pickFirstStringFromArray(result, 'reportPeriods', 'ReportPeriods'),
    reportTitle: pickFirstNonEmptyString(result, 'reportTitle', 'ReportTitle')
      || pickFirstStringFromArray(result, 'reportTitles', 'ReportTitles'),
    sourceChannel: pickFirstNonEmptyString(result, 'sourceChannel', 'SourceChannel', 'mainSourceChannel', 'MainSourceChannel') || channel,
    fallbackReason: pickFirstNonEmptyString(result, 'fallbackReason', 'FallbackReason') || degradeReason,
    pdfSummary: pickFirstNonEmptyString(result, 'pdfSummary', 'PdfSummary', 'pdfSummarySupplement', 'PdfSummarySupplement')
  }
}

const localizedCollectMessages = [
  {
    pattern: /all channels \(api \+ pdf\) failed or returned empty data/i,
    text: '所有采集渠道都未返回有效财务数据，请稍后重试或更换股票。'
  },
  {
    pattern: /all channels exhausted/i,
    text: '采集渠道未返回有效数据，请稍后重试。'
  },
  {
    pattern: /(?:financial )?worker (?:is )?unavailable|worker unavailable|service unavailable|collector unavailable/i,
    text: '财务采集服务暂不可用，请稍后重试。'
  },
  {
    pattern: /emweb\s*\+\s*datacenter empty data/i,
    text: '常用采集渠道未返回有效财务数据，请稍后重试。'
  },
  {
    pattern: /(?:emweb|datacenter|api|pdf) empty data/i,
    text: '采集渠道未返回有效数据。'
  }
]

function localizeCollectMessage(message) {
  const text = String(message ?? '').trim()
  if (!text) return ''
  const matched = localizedCollectMessages.find(item => item.pattern.test(text))
  return matched?.text || text
}

function getCollectMessage(result) {
  return localizeCollectMessage(result.errorMessage || result.degradeReason || '采集未成功')
}

function hasRenderableMetricValue(value) {
  if (value == null) return false
  if (typeof value === 'number') return Number.isFinite(value)
  if (typeof value === 'string') {
    const trimmed = value.trim()
    return trimmed !== '' && trimmed !== '-' && trimmed !== '--'
  }
  return true
}

function findLatestRenderableEntry(series) {
  if (!Array.isArray(series)) return null
  return series.find(item => hasRenderableMetricValue(item?.value)) || null
}

function uniqueStrings(values) {
  return Array.from(new Set(values.map(value => String(value ?? '').trim()).filter(Boolean)))
}

async function fetchData(options = {}) {
  const { preserveCollectState = false } = options
  const symbol = symbolRef.value?.trim() || ''
  const requestId = ++fetchRequestId

  error.value = ''
  if (!preserveCollectState) {
    collectError.value = ''
    collectResult.value = null
  }
  trend.value = null
  summary.value = null

  if (!symbol || !props.active) {
    loading.value = false
    return
  }

  loading.value = true
  try {
    const [trendRes, summaryRes] = await Promise.all([
      fetch(`/api/stocks/financial/trend/${symbol}`),
      fetch(`/api/stocks/financial/summary/${symbol}`)
    ])

    const [trendData, summaryData] = await Promise.all([
      trendRes.ok ? trendRes.json() : Promise.resolve(null),
      summaryRes.ok ? summaryRes.json() : Promise.resolve(null)
    ])

    if (requestId !== fetchRequestId || symbol !== (symbolRef.value?.trim() || '') || !props.active) {
      return
    }

    trend.value = trendData
    summary.value = summaryData
  } catch (e) {
    if (requestId !== fetchRequestId) {
      return
    }
    error.value = '加载失败: ' + e.message
  } finally {
    if (requestId === fetchRequestId) {
      loading.value = false
    }
  }
}

async function collectData() {
  if (!symbolRef.value || collecting.value) return
  collecting.value = true
  collectError.value = ''
  collectResult.value = null
  pdfSummaryOpen.value = false
  try {
    const resp = await fetch(`/api/stocks/financial/collect/${encodeURIComponent(symbolRef.value)}`, { method: 'POST' })
    if (!resp.ok) {
      const body = await resp.json().catch(() => null)
      throw new Error(body?.errorMessage || body?.ErrorMessage || body?.error || `采集失败 (${resp.status})`)
    }
    const result = await resp.json().catch(() => null)
    const normalizedResult = normalizeCollectResult(result)
    collectResult.value = normalizedResult
    if (normalizedResult.success) {
      await fetchData({ preserveCollectState: true })
    } else {
      collectError.value = getCollectMessage(normalizedResult)
    }
  } catch (e) {
    collectError.value = localizeCollectMessage(e.message || '采集请求异常')
  } finally {
    collecting.value = false
  }
}

watch([symbolRef, () => props.active], () => { fetchData() }, { immediate: true })

const hasTrendMetricData = computed(() => {
  if (!trend.value) return false
  return [trend.value.revenue, trend.value.netProfit, trend.value.totalAssets]
    .some(series => Array.isArray(series) && series.some(item => hasRenderableMetricValue(item?.value)))
})

const hasDividendData = computed(() => Array.isArray(trend.value?.recentDividends) && trend.value.recentDividends.length > 0)
const hasSummaryPeriods = computed(() => Array.isArray(summary.value?.periods) && summary.value.periods.length > 0)

const statementTypes = [
  { key: 'income', label: '利润表' },
  { key: 'balance', label: '资产负债表' },
  { key: 'cashflow', label: '现金流量表' }
]

const topMetrics = computed(() => {
  if (!trend.value) return []
  const metrics = []
  const rev = findLatestRenderableEntry(trend.value.revenue)
  const np = findLatestRenderableEntry(trend.value.netProfit)
  const ta = findLatestRenderableEntry(trend.value.totalAssets)

  if (rev) {
    const m = formatMoneyDisplay(rev.value)
    metrics.push({
      label: '营业收入',
      value: m.display,
      fullValue: m.full,
      yoyText: formatYoY(rev.yoY),
      yoyClass: getYoYClass(rev.yoY)
    })
  }
  if (np) {
    const m = formatMoneyDisplay(np.value)
    metrics.push({
      label: '净利润',
      value: m.display,
      fullValue: m.full,
      yoyText: formatYoY(np.yoY),
      yoyClass: getYoYClass(np.yoY)
    })
  }
  if (ta) {
    const m = formatMoneyDisplay(ta.value)
    metrics.push({
      label: '总资产',
      value: m.display,
      fullValue: m.full,
      yoyText: formatYoY(ta.yoY),
      yoyClass: getYoYClass(ta.yoY)
    })
  }
  return metrics
})

const trendRows = computed(() => {
  if (!trend.value) return []
  const rev = trend.value.revenue || []
  const np = trend.value.netProfit || []
  const ta = trend.value.totalAssets || []
  const maxLen = Math.max(rev.length, np.length, ta.length)
  const rows = []
  for (let i = 0; i < Math.min(maxLen, 8); i++) {
    const revenueItem = rev[i]
    const netProfitItem = np[i]
    const totalAssetsItem = ta[i]
    const hasAnyValue = [revenueItem, netProfitItem, totalAssetsItem]
      .some(item => hasRenderableMetricValue(item?.value))

    if (!hasAnyValue) continue

    rows.push({
      period: revenueItem?.period || netProfitItem?.period || totalAssetsItem?.period || '-',
      revenue: formatCellValue(revenueItem?.value, revenueItem?.yoY),
      revenueQoQ: formatQoQ(revenueItem?.qoQ),
      netProfit: formatCellValue(netProfitItem?.value, netProfitItem?.yoY),
      netProfitQoQ: formatQoQ(netProfitItem?.qoQ),
      totalAssets: formatCellValue(totalAssetsItem?.value, totalAssetsItem?.yoY),
      totalAssetsQoQ: formatQoQ(totalAssetsItem?.qoQ)
    })
  }
  return rows
})

const summaryPeriods = computed(() => {
  if (!hasSummaryPeriods.value) return []
  return summary.value.periods.map(p => p.reportDate).slice(0, 6)
})

const statementFieldMap = {
  income: [
    { key: 'Revenue', label: '营业收入' },
    { key: 'NetProfit', label: '净利润' },
    { key: 'GrossProfit', label: '毛利润' },
    { key: 'OperatingProfit', label: '营业利润' },
    { key: 'TotalRevenue', label: '营业总收入' },
    { key: 'TotalCost', label: '营业总成本' }
  ],
  balance: [
    { key: 'TotalAssets', label: '总资产' },
    { key: 'TotalLiabilities', label: '总负债' },
    { key: 'TotalEquity', label: '所有者权益' },
    { key: 'DebtToAssetRatio', label: '资产负债率', isRatio: true },
    { key: 'CurrentAssets', label: '流动资产' },
    { key: 'CurrentLiabilities', label: '流动负债' }
  ],
  cashflow: [
    { key: 'OperatingCashFlow', label: '经营活动现金流' },
    { key: 'InvestingCashFlow', label: '投资活动现金流' },
    { key: 'FinancingCashFlow', label: '筹资活动现金流' },
    { key: 'NetCashFlow', label: '现金净增加额' }
  ]
}

const summaryMetricDefinitions = Object.values(statementFieldMap).flat()

const hasStructuredSummaryData = computed(() => {
  if (!hasSummaryPeriods.value) return false
  return summary.value.periods.some(period => {
    const keyMetrics = period?.keyMetrics
    if (!keyMetrics || typeof keyMetrics !== 'object') return false
    return summaryMetricDefinitions.some(field => hasRenderableMetricValue(keyMetrics[field.key] ?? keyMetrics[field.label]))
  })
})

const hasRenderableFinancialData = computed(() => hasTrendMetricData.value || hasStructuredSummaryData.value)

const showPartialDataState = computed(() => {
  if (hasRenderableFinancialData.value) return false
  return hasSummaryPeriods.value || (collectResult.value?.success && collectResult.value.reportCount > 0)
})

const partialDataTitle = computed(() => {
  if (!showPartialDataState.value) return ''
  const count = hasSummaryPeriods.value ? summary.value.periods.length : collectResult.value?.reportCount || 0
  const countText = count > 0 ? `${count} 期报表` : '报表数据'

  if (collectResult.value?.success && collectResult.value.channel) {
    return `已通过 ${collectResult.value.channel} 获取 ${countText}，但当前暂无可展示的结构化财务指标。`
  }

  return `已获取 ${countText}，但当前暂无可展示的结构化财务指标。`
})

const partialDataMeta = computed(() => {
  if (!showPartialDataState.value || !hasSummaryPeriods.value) return ''

  const periods = summary.value.periods
    .map(period => period?.reportDate)
    .filter(Boolean)
    .slice(0, 6)
  const sources = uniqueStrings(summary.value.periods.map(period => period?.sourceChannel)).slice(0, 3)
  const parts = []

  if (periods.length > 0) parts.push(`期次：${periods.join('、')}`)
  if (sources.length > 0) parts.push(`来源：${sources.join(' / ')}`)

  return parts.join('；')
})

const summaryRows = computed(() => {
  if (!summary.value?.periods?.length) return []
  const fields = statementFieldMap[activeStatement.value] || []
  return fields.map(f => {
    const values = {}
    let hasValue = false
    for (const p of summary.value.periods) {
      const km = p.keyMetrics || {}
      const rawValue = km[f.key] ?? km[f.label]
      if (hasRenderableMetricValue(rawValue)) {
        hasValue = true
      }
      values[p.reportDate] = formatMetricValue(rawValue, f.isRatio)
    }
    return { key: f.key, label: f.label, values, hasValue }
  }).filter(row => row.hasValue)
})

const dividendRows = computed(() => {
  if (!trend.value?.recentDividends) return []
  return trend.value.recentDividends.map(d => ({
    plan: d.plan || '-',
    amount: d.dividendPerShare != null ? `¥${d.dividendPerShare.toFixed(4)}` : '-'
  }))
})



function formatYoY(yoy) {
  if (yoy == null) return ''
  return (yoy >= 0 ? '+' : '') + yoy.toFixed(2) + '%'
}

function formatQoQ(qoq) {
  if (qoq == null) return ''
  return (qoq >= 0 ? '+' : '') + qoq.toFixed(2) + '%'
}

function getYoYClass(yoy) {
  if (yoy == null) return ''
  return yoy >= 0 ? 'yoy-positive' : 'yoy-negative'
}

function formatCellValue(val, yoy) {
  const money = formatMoneyDisplay(val)
  if (money.display === '—') return '-'
  const yoyStr = yoy != null ? ` (${formatYoY(yoy)})` : ''
  return money.display + yoyStr
}

function formatMetricValue(val, isRatio = false) {
  if (val == null) return '-'
  const num = Number(val)
  if (isNaN(num)) return String(val)
  if (isRatio) return (num * 100).toFixed(2) + '%'
  if (Math.abs(num) >= 1e8) return (num / 1e8).toFixed(2) + '亿'
  if (Math.abs(num) >= 1e4) return (num / 1e4).toFixed(2) + '万'
  return num.toFixed(2)
}

// ---- V040-S4 采集结果透明化新字段 ----
const pdfSummaryOpen = ref(false)

const collectReportPeriod = computed(() => collectResult.value?.reportPeriod || '')
const collectReportTitle = computed(() => collectResult.value?.reportTitle || '')
const collectSourceChannel = computed(() => collectResult.value?.sourceChannel || '')
const collectFallbackReason = computed(() => collectResult.value?.fallbackReason || '')
const collectPdfSummary = computed(() => collectResult.value?.pdfSummary || '')
const collectChannelTag = computed(() => getSourceChannelTag(collectSourceChannel.value))

const hasCollectMeta = computed(() => Boolean(
  collectReportPeriod.value
  || collectReportTitle.value
  || collectSourceChannel.value
  || collectFallbackReason.value
  || collectPdfSummary.value
))

// ---- V041-S6: 「查看 PDF 原件 / 对照」入口 ----
const pdfViewerOpen = ref(false)
const pdfViewerLoading = ref(false)
const pdfViewerError = ref('')
const pdfViewerFileId = ref(null)
const pdfViewerFileList = ref([])  // V042-P0-A: 候选列表，传给 ComparePane 渲染 picker
let pdfResolveToken = 0

// ---- V041-S8-FU-1：「采集 PDF 原件」入口（与「查看 PDF 原件」区分） ----
const collectingPdf = ref(false)
const collectPdfMessage = ref('')
const collectPdfErrorMsg = ref('')

// V042-P0-C (B3) 修复：后端 pdf_files 集合的 Symbol 字段统一存为 6 位数字（如
// "600519"），而本 Tab 的 props.symbol 经常带市场前缀（如 "sh600519" / "SZ000001"），
// 直接用 raw 值查询会被精确匹配过滤掉，回到 items.length === 0 的空态分支，
// 表现就是「Modal 打开后只有『该报告暂无 PDF 原件』alert」。这里统一抽出数字部分。
function normalizeSymbolForPdf(raw) {
  if (raw == null) return ''
  const digits = String(raw).replace(/\D+/g, '')
  return digits
}

async function openPdfViewer() {
  const rawSymbol = symbolRef.value?.trim() || ''
  if (!rawSymbol || pdfViewerLoading.value) return
  pdfViewerOpen.value = true
  pdfViewerError.value = ''
  pdfViewerFileId.value = null
  pdfViewerLoading.value = true
  const token = ++pdfResolveToken
  try {
    const reportDate = summary.value?.periods?.[0]?.reportDate
      || summary.value?.periods?.[0]?.ReportDate
      || null
    // V041-S8 NIT-3 / V042-P0-C (B3) 修复：
    // 1) summary 的 reportType（Annual/Quarterly）与后端按 PDF 标题判定的
    //    reportType（Annual/Q1/Q2/Q3/Unknown）经常不匹配，所以只按 symbol 拉。
    // 2) symbol 必须用数字形式（"600519"），不能带 sh/sz 前缀，否则后端
    //    LiteDB `$.Symbol = @p0` 精确匹配返回 0 条。
    // 3) reportPeriod 命中则用之；否则不再退化到「items[0]」（受 LastParsedAt
    //    desc 排序影响，可能选到 fieldCount=0 的摘要 PDF），改为挑 fieldCount
    //    最大那条（更可能是主报告，与 ComparePane B1 切换器策略一致）。
    const symbolForApi = normalizeSymbolForPdf(rawSymbol) || rawSymbol
    // V042-R3 N4 修复：把 pageSize 从 10 抬到 20，保证主报告 / 摘要 / 英文版三件套
    // 都能进入候选列表，避免后端按 LastParsedAt desc 排序时主报告被挤出去。
    const res = await listPdfFiles({ symbol: symbolForApi, page: 1, pageSize: 20 })
    if (token !== pdfResolveToken) return
    const items = Array.isArray(res?.items) ? res.items : (Array.isArray(res?.Items) ? res.Items : [])
    pdfViewerFileList.value = items
    if (items.length === 0) {
      pdfViewerError.value = '该报告暂无 PDF 原件，请先触发「📥 采集 PDF 原件」'
      return
    }
    let pick = null
    // V042-R3 N4 + B1 二阶段：reportDate 命中可能匹配「摘要 PDF」，造成默认选中
    // fieldCount=0 的摘要而非主报告。改成 reportDate 命中后再按 fieldCount/摘要降权
    // 二次筛选，与 ComparePane.smartPickPdfId 策略一致。
    const SUMMARY_PATTERN = /(摘要|summary|英文|english)/i
    const fcOf = (it) => Number(it?.fieldCount ?? it?.FieldCount ?? 0) || 0
    const isSummary = (it) => SUMMARY_PATTERN.test(String(it?.fileName ?? it?.FileName ?? ''))
    const smartPick = (pool) => {
      if (!Array.isArray(pool) || pool.length === 0) return null
      return [...pool].sort((a, b) => {
        const fa = fcOf(a), fb = fcOf(b)
        const hasA = fa > 0 ? 1 : 0, hasB = fb > 0 ? 1 : 0
        if (hasA !== hasB) return hasB - hasA
        const sa = isSummary(a) ? 1 : 0, sb = isSummary(b) ? 1 : 0
        if (sa !== sb) return sa - sb
        if (fa !== fb) return fb - fa
        return 0
      })[0] || null
    }
    if (reportDate) {
      const matched = items.filter(x => (x.reportPeriod || x.ReportPeriod) === reportDate)
      if (matched.length > 0) {
        pick = smartPick(matched)
      }
    }
    if (!pick) {
      // 全集兜底：fieldCount 最大且非摘要/英文
      pick = smartPick(items) || items[0]
    }
    const id = pick?.id ?? pick?.Id ?? null
    if (!id) {
      pdfViewerError.value = '该报告暂无可用 PDF 文件 ID。'
      return
    }
    pdfViewerFileId.value = String(id)
  } catch (e) {
    if (token !== pdfResolveToken) return
    pdfViewerError.value = e?.message || '加载 PDF 列表失败'
  } finally {
    if (token === pdfResolveToken) {
      pdfViewerLoading.value = false
    }
  }
}

function closePdfViewer() {
  pdfViewerOpen.value = false
  pdfViewerFileId.value = null
  pdfViewerFileList.value = []
  pdfViewerError.value = ''
  pdfResolveToken++
}

// V042-P0-A：用户在 ComparePane picker 中切换 PDF
function onPdfViewerPicked(id) {
  if (!id) return
  const v = String(id)
  if (pdfViewerFileId.value !== v) {
    pdfViewerFileId.value = v
  }
}

// V041-S8-FU-1：触发 PDF 原件采集。成功后重置 pdfResolveToken，下次「查看 PDF 原件」重新解析。
async function onCollectPdf() {
  const symbol = symbolRef.value?.trim() || ''
  if (!symbol || collectingPdf.value) return
  collectingPdf.value = true
  collectPdfMessage.value = ''
  collectPdfErrorMsg.value = ''
  try {
    const result = await collectPdfFiles(symbol)
    const downloaded = result?.downloadedCount ?? 0
    const parsed = result?.parsedCount ?? 0
    if (downloaded > 0 || parsed > 0) {
      collectPdfMessage.value = `PDF 原件采集完成（下载 ${downloaded} 个，解析 ${parsed} 个），点击「📄 查看 PDF 原件」查看。`
    } else {
      collectPdfErrorMsg.value = result?.notes || 'cninfo 未找到可下载的 PDF 公告'
    }
    // 如果查看器当前是打开状态且处于错误/空态，重置 token 以避免错误状态残留
    pdfResolveToken++
  } catch (e) {
    collectPdfErrorMsg.value = e?.message || 'PDF 原件采集失败'
  } finally {
    collectingPdf.value = false
  }
}

function onComparePaneRefresh(detail) {
  // V042-P0-C (B4)：ComparePane 触发 reparse 后，结构化趋势/摘要也应该跟着刷新，
  // 否则用户看到 PDF 解析换了新版本但 Tab 上方表格还停留在旧值。这里重新拉
  // trend + summary，但保留采集结果横幅（preserveCollectState=true），避免把
  // 用户刚看到的「✅ 已通过 emweb 获取 X 期报表」给清掉。
  // eslint-disable-next-line no-console
  console.debug('[FinancialReportTab] ComparePane refresh', detail)
  // 失败不抛（避免 ComparePane 内部 emit 链路冒泡）。
  Promise.resolve(fetchData({ preserveCollectState: true })).catch((err) => {
    // eslint-disable-next-line no-console
    console.warn('[FinancialReportTab] ComparePane refresh -> fetchData 失败', err)
  })

  // V042-R3 B4 二阶段：reparse 后还要刷 picker 候选列表里的 fieldCount /
  // lastReparsedAt，否则用户切回别的 PDF 再切回来时 picker 文案是旧的。
  refreshPdfViewerFileList(detail).catch((err) => {
    // eslint-disable-next-line no-console
    console.warn('[FinancialReportTab] refreshPdfViewerFileList 失败', err)
  })
}

// V042-R3 B4 二阶段：reparse 后让 picker 候选列表跟上最新 fieldCount / lastReparsedAt。
// 优先把刚拿到的 detail 直接合并进列表（即时反馈），再异步重拉一次 listPdfFiles 兜底。
async function refreshPdfViewerFileList(detail) {
  const rawSymbol = symbolRef.value?.trim() || ''
  if (!rawSymbol) return
  const symbolForApi = normalizeSymbolForPdf(rawSymbol) || rawSymbol

  // 先用 detail 即时打补丁，避免等待 HTTP
  if (detail && (detail.id || detail.Id)) {
    const id = String(detail.id ?? detail.Id)
    const idx = pdfViewerFileList.value.findIndex((it) => String(it.id ?? it.Id) === id)
    if (idx >= 0) {
      const merged = { ...pdfViewerFileList.value[idx], ...detail }
      const next = pdfViewerFileList.value.slice()
      next[idx] = merged
      pdfViewerFileList.value = next
    }
  }

  try {
    const res = await listPdfFiles({ symbol: symbolForApi, page: 1, pageSize: 20 })
    const items = Array.isArray(res?.items) ? res.items : (Array.isArray(res?.Items) ? res.Items : [])
    if (items.length > 0) {
      pdfViewerFileList.value = items
    }
  } catch (e) {
    // eslint-disable-next-line no-console
    console.warn('[FinancialReportTab] listPdfFiles 重拉失败', e)
  }
}
</script>

<template>
  <div class="financial-report-tab">
    <div v-if="loading" class="loading-state">加载中...</div>
    <div v-else-if="error" class="error-state">{{ error }}</div>
    <div v-else-if="!symbolRef" class="empty-state">
      <p>请先选择一只股票</p>
    </div>
    <div v-else-if="!hasRenderableFinancialData && !hasDividendData && !showPartialDataState" class="empty-state">
      <p>暂无财务数据</p>
      <button class="collect-btn" @click="collectData" :disabled="collecting">
        {{ collecting ? '获取中...' : '获取财务数据' }}
      </button>
      <p v-if="collectError" class="error-msg error-msg-prominent">{{ collectError }}</p>
    </div>
    <template v-else>

      <div class="report-header">
        <span class="header-title">财务报表</span>
        <div class="report-header-actions">
          <button
            type="button"
            class="view-pdf-btn"
            data-testid="view-pdf-btn"
            @click="openPdfViewer"
            :disabled="pdfViewerLoading"
          >{{ pdfViewerLoading ? '加载中...' : '📄 查看 PDF 原件' }}</button>
          <button
            type="button"
            class="view-pdf-btn"
            data-testid="collect-pdf-btn"
            @click="onCollectPdf"
            :disabled="collectingPdf"
            title="从巨潮下载 PDF 原件并入库（耗时几十秒到几分钟）"
          >{{ collectingPdf ? '正在采集 PDF...' : '📥 采集 PDF 原件' }}</button>
          <button class="refresh-btn" @click="collectData" :disabled="collecting">
            {{ collecting ? '刷新中...' : '🔄 刷新数据' }}
          </button>
        </div>
      </div>
      <p v-if="collectPdfErrorMsg" class="error-msg" data-testid="collect-pdf-error">{{ collectPdfErrorMsg }}</p>
      <p v-else-if="collectPdfMessage" class="collect-info" data-testid="collect-pdf-info">{{ collectPdfMessage }}</p>
      <p v-else-if="collectingPdf" class="collect-info" data-testid="collect-pdf-hint">
        正在下载并解析 PDF 原件，可能需要几十秒到几分钟…
      </p>
      <p v-if="collectResult && collectResult.success && hasRenderableFinancialData" class="collect-info">
        ✅ 已通过 {{ collectResult.channel }} 获取 {{ collectResult.reportCount }} 期报表，耗时 {{ (collectResult.durationMs / 1000).toFixed(1) }}s
        <span v-if="collectResult.isDegraded && collectResult.degradeReason">（提示：{{ localizeCollectMessage(collectResult.degradeReason) }}）</span>
      </p>
      <div v-else-if="showPartialDataState" class="partial-data-message">
        <p class="partial-data-title">{{ partialDataTitle }}</p>
        <p v-if="partialDataMeta" class="partial-data-meta">{{ partialDataMeta }}</p>
        <p v-if="collectResult && collectResult.isDegraded && collectResult.degradeReason" class="partial-data-meta">
          提示：{{ localizeCollectMessage(collectResult.degradeReason) }}
        </p>
      </div>
      <!-- V040-S4 采集结果透明化：报告期 / 标题 / 来源 / 降级原因 / PDF 摘要 -->
      <dl v-if="collectResult && hasCollectMeta" class="collect-meta">
        <div v-if="collectReportPeriod" class="collect-meta-row" data-field="reportPeriod">
          <dt>报告期</dt>
          <dd>{{ collectReportPeriod }}</dd>
        </div>
        <div v-if="collectReportTitle" class="collect-meta-row" data-field="reportTitle">
          <dt>报告标题</dt>
          <dd>{{ collectReportTitle }}</dd>
        </div>
        <div v-if="collectSourceChannel" class="collect-meta-row" data-field="sourceChannel">
          <dt>来源渠道</dt>
          <dd>
            <span
              class="source-channel-tag"
              :data-channel-key="collectChannelTag.key"
              :style="sourceChannelTagStyle(collectChannelTag)"
            >{{ collectChannelTag.label }}</span>
          </dd>
        </div>
        <div v-if="collectFallbackReason" class="collect-meta-row" data-field="fallbackReason">
          <dt>降级原因</dt>
          <dd class="collect-meta-fallback">{{ collectFallbackReason }}</dd>
        </div>
        <div v-if="collectPdfSummary" class="collect-meta-row" data-field="pdfSummary">
          <dt>PDF 摘要</dt>
          <dd>
            <button
              type="button"
              class="pdf-summary-toggle"
              @click="pdfSummaryOpen = !pdfSummaryOpen"
            >{{ pdfSummaryOpen ? '收起' : '展开' }}</button>
            <pre v-if="pdfSummaryOpen" class="pdf-summary-content">{{ collectPdfSummary }}</pre>
          </dd>
        </div>
      </dl>
      <p v-if="collectError" class="error-msg">{{ collectError }}</p>

      <template v-if="!showPartialDataState">
        <!-- 区域 1: 核心指标卡片 -->
        <div v-if="topMetrics.length > 0" class="metric-cards">
          <div class="metric-card" v-for="metric in topMetrics" :key="metric.label">
            <div class="metric-label">{{ metric.label }}</div>
            <div class="metric-value" :title="metric.fullValue">{{ metric.value }}</div>
            <div class="metric-yoy" :class="metric.yoyClass">{{ metric.yoyText }}</div>
          </div>
        </div>

        <!-- 区域 2: 财务趋势表格 -->
        <div class="section-title">📈 财务趋势</div>
        <div v-if="trendRows.length > 0" class="trend-table-container">
          <table class="trend-table">
            <thead>
              <tr>
                <th>期间</th>
                <th>营业收入</th>
                <th>收入环比</th>
                <th>净利润</th>
                <th>利润环比</th>
                <th>总资产</th>
                <th>资产环比</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(item, idx) in trendRows" :key="idx">
                <td>{{ item.period }}</td>
                <td>{{ item.revenue }}</td>
                <td>{{ item.revenueQoQ }}</td>
                <td>{{ item.netProfit }}</td>
                <td>{{ item.netProfitQoQ }}</td>
                <td>{{ item.totalAssets }}</td>
                <td>{{ item.totalAssetsQoQ }}</td>
              </tr>
            </tbody>
          </table>
        </div>
        <div v-else class="empty-section">暂无趋势数据</div>

        <!-- 区域 3: 报表摘要 -->
        <div class="section-title">📋 报表摘要</div>
        <div class="statement-tabs">
          <button v-for="st in statementTypes" :key="st.key"
                  :class="{ active: activeStatement === st.key }"
                  @click="activeStatement = st.key">
            {{ st.label }}
          </button>
        </div>
        <div class="summary-table-container" v-if="summaryRows.length > 0">
          <table class="summary-table">
            <thead>
              <tr>
                <th>指标</th>
                <th v-for="period in summaryPeriods" :key="period">{{ period }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="row in summaryRows" :key="row.key">
                <td class="metric-name">{{ row.label }}</td>
                <td v-for="period in summaryPeriods" :key="period">{{ row.values[period] ?? '-' }}</td>
              </tr>
            </tbody>
          </table>
        </div>
        <div v-else class="empty-section">暂无报表数据</div>
      </template>

      <!-- 区域 4: 分红记录 -->
      <div v-if="hasDividendData || !showPartialDataState" class="section-title">💰 近期分红</div>
      <div v-if="dividendRows.length > 0">
        <table class="dividend-table">
          <thead>
            <tr><th>方案</th><th>每股现金分红</th></tr>
          </thead>
          <tbody>
            <tr v-for="d in dividendRows" :key="d.plan">
              <td>{{ d.plan }}</td>
              <td>{{ d.amount }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <div v-else-if="!showPartialDataState" class="empty-section">暂无分红数据</div>

    </template>

    <!-- V041-S6: PDF 原件 / 对照查看器 -->
    <Teleport to="body">
      <div
        v-if="pdfViewerOpen"
        class="pdf-viewer-overlay"
        data-testid="pdf-viewer-modal"
        @click.self="closePdfViewer"
      >
        <div class="pdf-viewer-dialog" role="dialog" aria-modal="true" aria-label="PDF 原件对照">
          <header class="pdf-viewer-header">
            <h3 class="pdf-viewer-title">PDF 原件 / 对照</h3>
            <button
              type="button"
              class="pdf-viewer-close"
              data-testid="pdf-viewer-close"
              @click="closePdfViewer"
              title="关闭"
            >✕</button>
          </header>
          <div class="pdf-viewer-body">
            <div v-if="pdfViewerLoading" class="pdf-viewer-state">正在查找 PDF 原件...</div>
            <div v-else-if="pdfViewerError" class="pdf-viewer-state pdf-viewer-error" role="alert">{{ pdfViewerError }}</div>
            <FinancialReportComparePane
              v-else-if="pdfViewerFileId"
              :pdf-file-id="pdfViewerFileId"
              :pdf-files="pdfViewerFileList"
              @refresh="onComparePaneRefresh"
              @pdf-change="onPdfViewerPicked"
              @close="closePdfViewer"
            />
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
.financial-report-tab {
  padding: 12px;
  color: #e0e0e0;
  font-size: 13px;
  overflow-y: auto;
  max-height: calc(100vh - 120px);
}

.loading-state, .error-state, .empty-state {
  text-align: center;
  padding: 40px 0;
  color: #888;
}
.error-state { color: #e74c3c; }

.metric-cards {
  display: flex;
  gap: 10px;
  margin-bottom: 16px;
  flex-wrap: wrap;
}
.metric-card {
  flex: 1;
  min-width: 120px;
  background: #1e2a3a;
  border-radius: 6px;
  padding: 10px 12px;
  border: 1px solid #2a3a4a;
}
.metric-label { color: #888; font-size: 11px; margin-bottom: 4px; }
.metric-value { font-size: 18px; font-weight: bold; color: #fff; }
.metric-yoy { font-size: 11px; margin-top: 2px; }
.yoy-positive { color: #e74c3c; }
.yoy-negative { color: #2ecc71; }

.section-title {
  font-size: 14px;
  font-weight: bold;
  margin: 16px 0 8px;
  color: #ccc;
}

.statement-tabs {
  display: flex;
  gap: 6px;
  margin-bottom: 8px;
}
.statement-tabs button {
  padding: 4px 12px;
  border: 1px solid #3a4a5a;
  background: transparent;
  color: #aaa;
  border-radius: 4px;
  cursor: pointer;
  font-size: 12px;
}
.statement-tabs button.active {
  background: #2a6ccf;
  color: #fff;
  border-color: #2a6ccf;
}

.trend-table-container, .summary-table-container {
  overflow-x: auto;
}

table {
  width: 100%;
  border-collapse: collapse;
  font-size: 12px;
  background: var(--vscode-editor-background, var(--bg-secondary, #1e1e2e));
  color: var(--vscode-editor-foreground, var(--text-primary, #e0e0e0));
}
th, td {
  padding: 6px 8px;
  border-bottom: 1px solid var(--vscode-panel-border, var(--border-color, #2a3a4a));
  text-align: right;
  white-space: nowrap;
}
th { color: var(--vscode-descriptionForeground, #999); font-weight: normal; background: var(--vscode-editorWidget-background, var(--bg-secondary, #1a2535)); }
td:first-child, th:first-child { text-align: left; }
.metric-name { color: var(--vscode-descriptionForeground, #bbb); }

.dividend-table { max-width: 400px; }

.empty-section { color: #666; font-size: 12px; padding: 8px 0; }

.report-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}
.header-title {
  font-size: 15px;
  font-weight: 600;
  color: var(--text-primary, #e0e0e0);
}
.collect-btn, .refresh-btn {
  padding: 6px 16px;
  border: 1px solid var(--border-color, #444);
  border-radius: 6px;
  background: var(--bg-secondary, #2a2a2e);
  color: var(--text-primary, #e0e0e0);
  cursor: pointer;
  font-size: 13px;
  transition: background 0.2s;
}
.collect-btn:hover:not(:disabled), .refresh-btn:hover:not(:disabled) {
  background: var(--bg-hover, #3a3a3e);
}
.collect-btn:disabled, .refresh-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}
.collect-btn {
  margin-top: 12px;
  padding: 8px 24px;
  font-size: 14px;
}
.collect-info {
  font-size: 12px;
  color: var(--text-secondary, #aaa);
  margin-top: 6px;
}

.partial-data-message {
  margin-top: 6px;
  padding: 10px 12px;
  border: 1px solid rgba(238, 191, 64, 0.28);
  border-left: 3px solid #eebf40;
  border-radius: 8px;
  background: rgba(238, 191, 64, 0.1);
}

.partial-data-title {
  margin: 0;
  color: #f0ddb0;
  font-size: 13px;
  line-height: 1.5;
}

.partial-data-meta {
  margin: 6px 0 0;
  color: #cbb781;
  font-size: 12px;
  line-height: 1.5;
}

.collect-meta {
  margin: 8px 0 0;
  padding: 10px 12px;
  display: flex;
  flex-direction: column;
  gap: 4px;
  background: #152030;
  border-radius: 6px;
  border: 1px solid #2a3a4a;
  font-size: 12px;
}
.collect-meta-row {
  display: flex;
  gap: 8px;
  align-items: flex-start;
  margin: 0;
}
.collect-meta-row dt {
  color: #91a6bc;
  min-width: 70px;
  flex-shrink: 0;
  margin: 0;
}
.collect-meta-row dd {
  color: #ddd;
  margin: 0;
  word-break: break-word;
  flex: 1;
}
.collect-meta-fallback { color: #d97706; }
.source-channel-tag {
  display: inline-block;
  padding: 1px 8px;
  font-size: 11px;
  line-height: 1.5;
  border-radius: 10px;
  border: 1px solid transparent;
  font-weight: 500;
}
.pdf-summary-toggle {
  padding: 2px 8px;
  font-size: 11px;
  background: #3a4a5a;
  color: #fff;
  border: none;
  border-radius: 3px;
  cursor: pointer;
}
.pdf-summary-toggle:hover { background: #4a5a6a; }
.pdf-summary-content {
  margin: 6px 0 0;
  padding: 8px;
  background: #0d1622;
  border-radius: 4px;
  color: #c8d3e0;
  font-size: 11px;
  max-height: 220px;
  overflow: auto;
  white-space: pre-wrap;
}

.error-msg {
  color: #e74c3c;
  font-size: 12px;
  margin-top: 6px;
}

.error-msg-prominent {
  display: inline-block;
  max-width: 420px;
  margin-top: 12px;
  padding: 10px 12px;
  border: 1px solid rgba(231, 76, 60, 0.35);
  border-left: 3px solid #e74c3c;
  border-radius: 8px;
  background: rgba(231, 76, 60, 0.12);
  color: #ffb3ab;
  font-size: 13px;
  line-height: 1.5;
}

/* V041-S6: PDF viewer entry button + modal */
.report-header-actions {
  display: flex;
  gap: 8px;
  align-items: center;
}
.view-pdf-btn {
  padding: 6px 14px;
  border: 1px solid var(--border-color, #444);
  border-radius: 6px;
  background: var(--bg-secondary, #2a2a2e);
  color: var(--text-primary, #e0e0e0);
  cursor: pointer;
  font-size: 13px;
  transition: background 0.2s;
}
.view-pdf-btn:hover:not(:disabled) {
  background: var(--bg-hover, #3a3a3e);
}
.view-pdf-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.pdf-viewer-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.55);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 9000;
}
.pdf-viewer-dialog {
  width: min(92vw, 1280px);
  height: min(90vh, 860px);
  background: #15202b;
  border: 1px solid #2a3a4a;
  border-radius: 8px;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  box-shadow: 0 12px 40px rgba(0, 0, 0, 0.5);
}
.pdf-viewer-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 14px;
  border-bottom: 1px solid #2a3a4a;
  background: #1a2535;
}
.pdf-viewer-title {
  margin: 0;
  font-size: 14px;
  color: #e0e0e0;
  font-weight: 600;
}
.pdf-viewer-close {
  border: none;
  background: transparent;
  color: #aaa;
  font-size: 16px;
  cursor: pointer;
  padding: 4px 8px;
  border-radius: 4px;
}
.pdf-viewer-close:hover { background: #2a3a4a; color: #fff; }
.pdf-viewer-body {
  flex: 1;
  min-height: 0;
  overflow: hidden;
  display: flex;
}
.pdf-viewer-body > * { flex: 1; min-height: 0; }
.pdf-viewer-state {
  padding: 24px;
  color: #aaa;
  font-size: 13px;
}
.pdf-viewer-error { color: #ffb3ab; }
</style>
