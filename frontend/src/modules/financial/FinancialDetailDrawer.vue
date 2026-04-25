<script setup>
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import {
  fetchFinancialReportDetail,
  listPdfFiles,
  recollectFinancialReport,
  collectPdfFiles
} from './financialApi.js'
import FinancialReportComparePane from './FinancialReportComparePane.vue'
import {
  BALANCE_SHEET_FIELDS,
  INCOME_STATEMENT_FIELDS,
  CASH_FLOW_FIELDS,
  pickFieldValue,
  formatFieldValue,
  formatMoneyDisplay
} from './financialFieldDictionary.js'
import { REPORT_TYPE_LABEL } from './financialCenterConstants.js'
import { getSourceChannelTag, sourceChannelTagStyle } from './sourceChannelTag.js'

const props = defineProps({
  visible: { type: Boolean, default: false },
  item: { type: Object, default: null },
  reportId: { type: [String, Number], default: null }
})

const emit = defineEmits(['close'])

const detail = ref(null)
const loading = ref(false)
const error = ref('')

const recollecting = ref(false)
const recollectError = ref('')
const recollectMessage = ref('')

// V041-S8-FU-1: PDF 原件采集（巨潮下载 + 提取 + 投票 + 解析 + 持久化，可能耗时几十秒到几分钟）
const collectingPdf = ref(false)
const collectPdfError = ref('')
const collectPdfMessage = ref('')

// V041-S5: PDF 文件解析（detail 中无 pdfFileId 时回退到 listPdfFiles）
const resolvedPdfId = ref(null)
const pdfFileList = ref([])  // V042-P0-A: 完整候选列表，传给 ComparePane 渲染 picker
const resolvingPdf = ref(false)
const resolvePdfError = ref('')
let pdfResolveToken = 0

const close = () => emit('close')

const onOverlayClick = (e) => {
  if (e.target === e.currentTarget) close()
}

const onKeydown = (e) => {
  if (e.key === 'Escape' && props.visible) close()
}

const resolveId = () => {
  if (props.reportId !== null && props.reportId !== undefined && props.reportId !== '') {
    return props.reportId
  }
  if (props.item) {
    return props.item.id ?? props.item.Id ?? null
  }
  return null
}

const resolveSymbol = () => {
  if (!props.item) return ''
  return props.item.symbol || props.item.Symbol || ''
}

let fetchToken = 0

async function loadDetail(options = {}) {
  const { resolvePdf = true } = options
  const id = resolveId()
  if (!id) {
    detail.value = null
    error.value = ''
    loading.value = false
    return
  }
  const token = ++fetchToken
  // V041-S8 NIT-4：仅在初次加载时显示骨架屏。reparse/recollect 等场景的「静默刷新」
  // 不卸载 v-else 内的 PDF/ComparePane 子树，避免 ComparePane 重新挂载导致 internalDetail
  // 被覆盖（包括刚通过 handleReparse 写入的新 lastReparsedAt）。
  const silentRefresh = detail.value != null && !error.value
  if (!silentRefresh) loading.value = true
  error.value = ''
  try {
    const data = await fetchFinancialReportDetail(id)
    if (token !== fetchToken) return
    detail.value = data
  } catch (e) {
    if (token !== fetchToken) return
    error.value = e?.message || '加载详情失败'
    detail.value = null
  } finally {
    if (token === fetchToken) {
      loading.value = false
    }
  }
  // detail 加载完成后解析对应 PDF（独立 token，避免互相阻塞）
  // V042-R3 BLOCKER1：reparse 后调用方需要保留用户当前选中的 PDF（resolvedPdfId），
  // 此时通过 resolvePdf=false 跳过 smartPick，避免把用户选中的「年度报告摘要」
  // 切回主报告，让 lastReparsedAt 看似「倒退」。
  if (resolvePdf) resolvePdfFileId()
}

function pickPdfIdFromDetail(d) {
  if (!d) return null
  return d.pdfFileId || d.PdfFileId || d.latestPdf?.id || d.latestPdf?.Id || null
}

async function resolvePdfFileId() {
  const token = ++pdfResolveToken
  resolvePdfError.value = ''
  // V041-S8 NIT-4 修复：reparse 完成后 ComparePane emit('refresh') → loadDetail() →
  // 这里被再次调用。如果起手就把 resolvedPdfId 置 null，会触发 ComparePane 的
  // pdfFileId watch 把 internalDetail 清掉，刚刚通过 handleReparse 写入的新
  // lastReparsedAt 也一起被冲掉。这里改成「保留旧值、只在结果变化时再赋新值」。

  // 先从 detail 字段中找
  const direct = pickPdfIdFromDetail(detail.value)
  if (direct) {
    const v = String(direct)
    if (resolvedPdfId.value !== v) resolvedPdfId.value = v
    return
  }

  // 没有则按 symbol + reportType 查 PDF 列表，按智能策略选
  const symbol = resolveSymbol()
  if (!symbol) {
    resolvedPdfId.value = null
    pdfFileList.value = []
    return
  }
  const reportType = props.item?.reportType || props.item?.ReportType || detail.value?.reportType || detail.value?.ReportType || null

  resolvingPdf.value = true
  try {
    const res = await listPdfFiles({ symbol, reportType, page: 1, pageSize: 5 })
    if (token !== pdfResolveToken) return
    const items = Array.isArray(res?.items) ? res.items : (Array.isArray(res?.Items) ? res.Items : [])
    pdfFileList.value = items
    if (items.length === 0) {
      resolvedPdfId.value = null
      return
    }
    // V042-P0-A 智能默认：
    //   1) 报告期严格匹配 → 同期内再按 fieldCount 智能选（兼顾「年报正本」vs「年报摘要」）
    //   2) 无报告期匹配 → 在所有候选中按 fieldCount > 0 优先 + 摘要降权 + lastParsedAt desc
    const reportDate = props.item?.reportDate || props.item?.ReportDate || detail.value?.reportDate || detail.value?.ReportDate || null
    const matched = reportDate
      ? items.filter(x => (x.reportPeriod || x.ReportPeriod) === reportDate)
      : []
    const pool = matched.length > 0 ? matched : items
    const pick = smartPickPdf(pool) || pool[0]
    const newId = String(pick.id || pick.Id || '') || null
    if (resolvedPdfId.value !== newId) resolvedPdfId.value = newId
  } catch (e) {
    if (token !== pdfResolveToken) return
    resolvePdfError.value = e?.message || '加载 PDF 列表失败'
    resolvedPdfId.value = null
    pdfFileList.value = []
  } finally {
    if (token === pdfResolveToken) {
      resolvingPdf.value = false
    }
  }
}

// V042-P0-A: 智能选 PDF —— 字段量优先 / 摘要降权 / lastParsedAt desc
const SUMMARY_PATTERN = /(摘要|summary|英文|english)/i
function smartPickPdf(items) {
  if (!Array.isArray(items) || items.length === 0) return null
  const sorted = [...items].sort((a, b) => {
    const fa = Number(a?.fieldCount ?? a?.FieldCount ?? 0) || 0
    const fb = Number(b?.fieldCount ?? b?.FieldCount ?? 0) || 0
    const hasA = fa > 0 ? 1 : 0
    const hasB = fb > 0 ? 1 : 0
    if (hasA !== hasB) return hasB - hasA
    const sumA = SUMMARY_PATTERN.test(String(a?.fileName ?? a?.FileName ?? '')) ? 1 : 0
    const sumB = SUMMARY_PATTERN.test(String(b?.fileName ?? b?.FileName ?? '')) ? 1 : 0
    if (sumA !== sumB) return sumA - sumB
    if (fa !== fb) return fb - fa
    const ta = a?.lastParsedAt ? new Date(a.lastParsedAt).getTime() || 0 : 0
    const tb = b?.lastParsedAt ? new Date(b.lastParsedAt).getTime() || 0 : 0
    return tb - ta
  })
  return sorted[0] || null
}

// V042-P0-A：用户在 ComparePane picker 中切换 PDF 时同步给 drawer
function onPdfPicked(id) {
  if (!id) return
  const v = String(id)
  if (resolvedPdfId.value !== v) {
    resolvedPdfId.value = v
  }
}

// V042-R3 N3：抽屉宽度 ~470px，PDF iframe 列只剩 ~270px，Chrome PDF viewer 在
// 此宽度下不渲染。改成「在 Modal 中查看 PDF」—— 抽屉里只保留入口按钮 +
// PDF 元数据简介，深度对照走全屏 Modal（与股票详情入口体验一致）。
const pdfModalOpen = ref(false)
function openPdfModal() {
  if (!resolvedPdfId.value) return
  pdfModalOpen.value = true
}
function closePdfModal() {
  pdfModalOpen.value = false
}
function onPdfModalRefresh(detailFromModal) {
  // 重解析后刷新 detail（financial_reports 的字段可能变）+ 重拉 PDF 列表（picker 候选）。
  // V042-R3 BLOCKER1：reparse 必须保留用户当前选中的 PDF。loadDetail 走 resolvePdf=false
  // 跳过 smartPick；listPdfFiles 重拉只更新数组数据，不改 resolvedPdfId。
  loadDetail({ resolvePdf: false })
  // 优先用 modal 透传过来的 detail 即时打补丁，避免等待 HTTP 时 picker 文案陈旧
  if (detailFromModal && (detailFromModal.id || detailFromModal.Id)) {
    const id = String(detailFromModal.id ?? detailFromModal.Id)
    const idx = pdfFileList.value.findIndex((it) => String(it.id ?? it.Id) === id)
    if (idx >= 0) {
      const merged = { ...pdfFileList.value[idx], ...detailFromModal }
      const next = pdfFileList.value.slice()
      next[idx] = merged
      pdfFileList.value = next
    }
  }
  // 单独重拉 listPdfFiles 以让 pdfFileList 反映新 fieldCount / lastReparsedAt
  refreshPdfFileList().catch(() => {})
}
async function refreshPdfFileList() {
  const symbol = resolveSymbol()
  if (!symbol) return
  const reportType = props.item?.reportType || props.item?.ReportType || detail.value?.reportType || detail.value?.ReportType || null
  try {
    const res = await listPdfFiles({ symbol, reportType, page: 1, pageSize: 20 })
    const items = Array.isArray(res?.items) ? res.items : (Array.isArray(res?.Items) ? res.Items : [])
    if (items.length > 0) pdfFileList.value = items
  } catch (e) {
    // ignore - picker 显示旧值无伤大雅
  }
}

// 当前选中 PDF 的元数据快照（抽屉里展示，让用户在打开 Modal 前先看到关键信息）
const currentPdfMeta = computed(() => {
  const id = resolvedPdfId.value
  if (!id) return null
  const found = pdfFileList.value.find(it => String(it.id ?? it.Id) === String(id))
  return found || null
})

const formatDateTimeShort = (raw) => {
  if (!raw) return '—'
  const d = new Date(raw)
  if (Number.isNaN(d.getTime())) return String(raw)
  return d.toLocaleString()
}

function buildRecollectSummary(result) {
  if (!result || typeof result !== 'object') return '已重新采集，刷新中...'
  const channel = result.channel || '未知'
  const count = result.reportCount ?? 0
  const ms = result.durationMs
  const duration = ms != null ? `${(ms / 1000).toFixed(1)}s` : ''

  let msg = ''
  if (result.isDegraded && result.degradeReason) {
    msg = `采集完成（降级至 ${channel} 通道，原因：${result.degradeReason}，获取 ${count} 期报告`
  } else {
    msg = `采集完成（通道：${channel}，获取 ${count} 期报告`
  }
  if (duration) msg += `，耗时 ${duration}`
  msg += '）'

  const warns = result.warnings
  if (Array.isArray(warns) && warns.length) {
    msg += `\n⚠ ${warns.join('；')}`
  }
  return msg + '\n刷新中...'
}

async function onRecollect() {
  const symbol = resolveSymbol()
  if (!symbol || recollecting.value) return
  recollecting.value = true
  recollectError.value = ''
  recollectMessage.value = ''
  try {
    const result = await recollectFinancialReport(symbol)
    recollectMessage.value = buildRecollectSummary(result)
    await loadDetail()
  } catch (e) {
    recollectError.value = e?.message || '重新采集失败'
  } finally {
    recollecting.value = false
  }
}

// V041-S8-FU-1：调用 FinancialWorker 的 PDF 采集流水线，成功后重新解析 pdfFileId。
async function onCollectPdf() {
  const symbol = resolveSymbol()
  if (!symbol || collectingPdf.value) return
  collectingPdf.value = true
  collectPdfError.value = ''
  collectPdfMessage.value = ''
  recollectError.value = ''
  recollectMessage.value = ''
  try {
    const result = await collectPdfFiles(symbol)
    const downloaded = result?.downloadedCount ?? 0
    const parsed = result?.parsedCount ?? 0
    if (downloaded > 0 || parsed > 0) {
      collectPdfMessage.value = `PDF 原件采集完成（下载 ${downloaded} 个，解析 ${parsed} 个），刷新中...`
      // 重新解析 PDF 列表（这里不重新拉财报详情，避免不必要的上游调用）
      await resolvePdfFileId()
    } else {
      collectPdfError.value = result?.notes || 'cninfo 未找到可下载的 PDF 公告'
    }
  } catch (e) {
    collectPdfError.value = e?.message || 'PDF 原件采集失败'
  } finally {
    collectingPdf.value = false
  }
}

watch(
  () => props.visible,
  (v) => {
    if (v) {
      window.addEventListener('keydown', onKeydown)
      recollectError.value = ''
      recollectMessage.value = ''
      loadDetail()
    } else {
      window.removeEventListener('keydown', onKeydown)
    }
  },
  { immediate: true }
)

watch(
  () => [props.item, props.reportId],
  () => {
    if (props.visible) loadDetail()
  }
)

onBeforeUnmount(() => {
  window.removeEventListener('keydown', onKeydown)
})

const formatDate = (raw) => {
  if (!raw) return '—'
  const d = new Date(raw)
  if (Number.isNaN(d.getTime())) return String(raw)
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

const formatDateTime = (raw) => {
  if (!raw) return '—'
  const d = new Date(raw)
  if (Number.isNaN(d.getTime())) return String(raw)
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  const hh = String(d.getHours()).padStart(2, '0')
  const mm = String(d.getMinutes()).padStart(2, '0')
  return `${y}-${m}-${day} ${hh}:${mm}`
}

const reportTypeLabel = (raw) => {
  if (!raw) return '—'
  const key = String(raw).toLowerCase()
  return REPORT_TYPE_LABEL[key] || raw
}

const reportScopeLabel = computed(() => {
  const v = view.value
  if (!v) return '口径未确认'
  const rawType = String(v.reportType || v.ReportType || '').toLowerCase()
  const rawDate = String(v.reportDate || v.ReportDate || '')
  if (rawType.includes('annual') || rawType.includes('年报') || /-12-31$/.test(rawDate)) return '年度'
  if (rawType.includes('q1') || rawType.includes('一季') || /-03-31$/.test(rawDate)) return '一季度累计(YTD)'
  if (rawType.includes('q2') || rawType.includes('mid') || rawType.includes('中报') || rawType.includes('半年度') || /-06-30$/.test(rawDate)) return '半年度累计(YTD)'
  if (rawType.includes('q3') || rawType.includes('三季') || /-09-30$/.test(rawDate)) return '三季度累计(YTD)'
  return '口径未确认'
})

const view = computed(() => detail.value || props.item || null)

const headerTitle = computed(() => {
  const v = view.value
  if (!v) return '财报详情'
  const sym = v.symbol || v.Symbol || ''
  const date = v.reportDate || v.ReportDate || ''
  const type = reportTypeLabel(v.reportType || v.ReportType)
  const parts = [sym, date, type].filter(p => p && p !== '—')
  return parts.length > 0 ? parts.join(' · ') : '财报详情'
})

const sourceTag = computed(() => {
  const v = view.value
  if (!v) return null
  const ch = v.sourceChannel || v.SourceChannel
  return getSourceChannelTag(ch)
})

const buildRows = (dictKeyLower, fields) => {
  const v = detail.value
  let dict = null
  if (v) {
    const upper = dictKeyLower.charAt(0).toUpperCase() + dictKeyLower.slice(1)
    dict = v[dictKeyLower] || v[upper] || null
  }
  return fields.map(f => {
    const raw = pickFieldValue(dict, f)
    if (f.key === 'epsBasic') {
      // 基本每股收益单位为 元/股，formatFieldValue 只输出数值，追加 " 元" 避免与比率/百分比混淆
      const formatted = formatFieldValue(raw)
      const display = formatted === '—' ? '—' : `${formatted} 元`
      return { key: f.key, label: f.label, display, fullValue: '' }
    }
    const money = formatMoneyDisplay(raw)
    return { key: f.key, label: f.label, display: money.display, fullValue: money.full }
  })
}

const balanceRows = computed(() => buildRows('balanceSheet', BALANCE_SHEET_FIELDS))
const incomeRows = computed(() => buildRows('incomeStatement', INCOME_STATEMENT_FIELDS))
const cashRows = computed(() => buildRows('cashFlow', CASH_FLOW_FIELDS))
</script>

<template>
  <Teleport to="body">
    <div
      v-if="visible"
      class="fc-drawer-overlay"
      @click="onOverlayClick"
    >
      <aside
        class="fc-drawer"
        :class="{ 'fc-drawer--open': visible }"
        role="dialog"
        aria-modal="true"
        aria-label="财报详情"
        @click.stop
      >
        <header class="fc-drawer-header">
          <h3 class="fc-drawer-title">{{ headerTitle }}</h3>
          <button type="button" class="fc-drawer-close" @click="close" title="关闭">✕</button>
        </header>

        <div class="fc-drawer-body">
          <div v-if="loading" class="fc-drawer-skeleton">
            <p>正在加载财报详情...</p>
            <div class="fc-skel-block" />
            <div class="fc-skel-block" />
            <div class="fc-skel-block" />
          </div>

          <div v-else-if="error" class="fc-drawer-error" role="alert">
            <p class="fc-drawer-error-msg">{{ error }}</p>
            <button type="button" class="fc-drawer-btn" @click="loadDetail">重试</button>
          </div>

          <template v-else>
            <section class="fc-drawer-section">
              <h4 class="fc-drawer-section-title">基本信息</h4>
              <dl class="fc-drawer-list">
                <div class="fc-drawer-row">
                  <dt>报告期</dt>
                  <dd>{{ formatDate(view?.reportDate || view?.ReportDate) }}</dd>
                </div>
                <div class="fc-drawer-row">
                  <dt>报告类型</dt>
                  <dd>{{ reportTypeLabel(view?.reportType || view?.ReportType) }}</dd>
                </div>
                <div class="fc-drawer-row">
                  <dt>数据口径</dt>
                  <dd><span class="fc-drawer-tag">{{ reportScopeLabel }}</span></dd>
                </div>
                <div class="fc-drawer-row">
                  <dt>来源渠道</dt>
                  <dd>
                    <span
                      v-if="sourceTag"
                      class="fc-drawer-tag"
                      :style="sourceChannelTagStyle(sourceTag)"
                    >{{ sourceTag.label }}</span>
                    <span v-else>—</span>
                  </dd>
                </div>
                <div class="fc-drawer-row">
                  <dt>采集时间</dt>
                  <dd>{{ formatDateTime(view?.collectedAt || view?.CollectedAt) }}</dd>
                </div>
                <div class="fc-drawer-row">
                  <dt>更新时间</dt>
                  <dd>{{ formatDateTime(view?.updatedAt || view?.UpdatedAt) }}</dd>
                </div>
                <div class="fc-drawer-row">
                  <dt>Report ID</dt>
                  <dd class="fc-drawer-mono">{{ view?.id ?? view?.Id ?? '—' }}</dd>
                </div>
              </dl>
            </section>

            <section class="fc-drawer-section">
              <h4 class="fc-drawer-section-title">资产负债表</h4>
              <dl class="fc-drawer-list">
                <div v-for="row in balanceRows" :key="row.key" class="fc-drawer-row">
                  <dt>{{ row.label }}</dt>
                  <dd class="fc-drawer-num" :title="row.fullValue">{{ row.display }}</dd>
                </div>
              </dl>
            </section>

            <section class="fc-drawer-section">
              <h4 class="fc-drawer-section-title">利润表</h4>
              <dl class="fc-drawer-list">
                <div v-for="row in incomeRows" :key="row.key" class="fc-drawer-row">
                  <dt>{{ row.label }}</dt>
                  <dd class="fc-drawer-num" :title="row.fullValue">{{ row.display }}</dd>
                </div>
              </dl>
            </section>

            <section class="fc-drawer-section">
              <h4 class="fc-drawer-section-title">现金流量表</h4>
              <dl class="fc-drawer-list">
                <div v-for="row in cashRows" :key="row.key" class="fc-drawer-row">
                  <dt>{{ row.label }}</dt>
                  <dd class="fc-drawer-num" :title="row.fullValue">{{ row.display }}</dd>
                </div>
              </dl>
            </section>

            <section class="fc-drawer-section">
              <h4 class="fc-drawer-section-title">PDF 原件</h4>
              <!-- V042-R3 N3：抽屉宽度太窄会让 Chrome PDF viewer 不渲染（~270px 列宽）。
                   改成「打开 PDF 对照 Modal」—— 抽屉里只展示候选数 + 当前选择 + 入口按钮，
                   深度对照在全屏 Modal 里完成。 -->
              <div
                v-if="resolvedPdfId"
                class="fc-drawer-pdf-summary"
                data-testid="fc-drawer-pdf-summary"
              >
                <dl class="fc-drawer-pdf-meta">
                  <div class="fc-drawer-pdf-meta-row">
                    <dt>当前 PDF</dt>
                    <dd data-testid="fc-drawer-pdf-current-name">{{ currentPdfMeta?.fileName || currentPdfMeta?.FileName || '—' }}</dd>
                  </div>
                  <div class="fc-drawer-pdf-meta-row">
                    <dt>字段数</dt>
                    <dd>{{ currentPdfMeta?.fieldCount ?? currentPdfMeta?.FieldCount ?? '—' }}</dd>
                  </div>
                  <div class="fc-drawer-pdf-meta-row">
                    <dt>候选数</dt>
                    <dd>{{ pdfFileList.length }}</dd>
                  </div>
                  <div class="fc-drawer-pdf-meta-row">
                    <dt>最近重解析</dt>
                    <dd>{{ formatDateTimeShort(currentPdfMeta?.lastReparsedAt || currentPdfMeta?.LastReparsedAt) }}</dd>
                  </div>
                </dl>
                <button
                  type="button"
                  class="fc-drawer-pdf-open-btn"
                  data-testid="fc-drawer-pdf-open-btn"
                  @click="openPdfModal"
                >📄 在 Modal 中查看 / 重新解析 PDF</button>
              </div>
              <div
                v-else-if="resolvingPdf"
                class="fc-drawer-pdf-placeholder"
                data-testid="fc-drawer-pdf-resolving"
              >正在定位 PDF 原件…</div>
              <div
                v-else
                class="fc-drawer-pdf-placeholder"
                data-testid="fc-drawer-pdf-empty"
              >
                {{ resolvePdfError || '该报告暂无 PDF 原件，请先触发「📥 采集 PDF 原件」' }}
              </div>
            </section>
          </template>
        </div>

        <footer class="fc-drawer-footer">
          <div v-if="recollectError" class="fc-drawer-footer-error" role="alert">
            {{ recollectError }}
          </div>
          <div v-else-if="recollectMessage" class="fc-drawer-footer-info">
            {{ recollectMessage }}
          </div>
          <div v-if="collectPdfError" class="fc-drawer-footer-error" data-testid="fc-drawer-pdf-collect-error" role="alert">
            {{ collectPdfError }}
          </div>
          <div v-else-if="collectPdfMessage" class="fc-drawer-footer-info" data-testid="fc-drawer-pdf-collect-info">
            {{ collectPdfMessage }}
          </div>
          <div v-if="collectingPdf" class="fc-drawer-footer-info" data-testid="fc-drawer-pdf-collect-hint">
            正在下载并解析 PDF 原件，可能需要几十秒到几分钟…
          </div>
          <div class="fc-drawer-footer-actions">
            <button
              type="button"
              class="fc-drawer-btn fc-drawer-btn--primary"
              :disabled="recollecting || !resolveSymbol()"
              @click="onRecollect"
            >
              {{ recollecting ? '正在重新采集报告...' : '重新采集报告' }}
            </button>
            <button
              type="button"
              class="fc-drawer-btn fc-drawer-btn--primary"
              data-testid="fc-drawer-collect-pdf-btn"
              :disabled="collectingPdf || !resolveSymbol()"
              @click="onCollectPdf"
              title="从巨潮下载 PDF 并入库（耗时几十秒到几分钟）"
            >
              {{ collectingPdf ? '正在采集 PDF...' : '📥 采集 PDF 原件' }}
            </button>
            <button
              type="button"
              class="fc-drawer-btn"
              @click="close"
            >关闭</button>
          </div>
        </footer>
      </aside>
    </div>
  </Teleport>

  <!-- V042-R3 N3：PDF 对照独立 Modal（teleport 到 body，绕过抽屉宽度限制） -->
  <Teleport to="body">
    <div
      v-if="pdfModalOpen"
      class="fc-drawer-pdf-modal-overlay"
      data-testid="fc-drawer-pdf-modal"
      @click.self="closePdfModal"
    >
      <div
        class="fc-drawer-pdf-modal-dialog"
        role="dialog"
        aria-modal="true"
        aria-label="PDF 原件对照"
      >
        <header class="fc-drawer-pdf-modal-header">
          <h3 class="fc-drawer-pdf-modal-title">PDF 原件 / 对照</h3>
          <button
            type="button"
            class="fc-drawer-pdf-modal-close"
            data-testid="fc-drawer-pdf-modal-close"
            @click="closePdfModal"
            title="关闭"
          >✕</button>
        </header>
        <div class="fc-drawer-pdf-modal-body">
          <FinancialReportComparePane
            v-if="resolvedPdfId"
            :pdf-file-id="resolvedPdfId"
            :pdf-files="pdfFileList"
            @refresh="onPdfModalRefresh"
            @pdf-change="onPdfPicked"
            @close="closePdfModal"
          />
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.fc-drawer-overlay {
  position: fixed;
  inset: 0;
  z-index: 500;
  background: var(--color-bg-overlay);
  display: flex;
  justify-content: flex-end;
  animation: fc-drawer-fade var(--transition-normal);
}

@keyframes fc-drawer-fade {
  from { opacity: 0; }
  to { opacity: 1; }
}

.fc-drawer {
  width: 520px;
  max-width: 100vw;
  height: 100vh;
  background: var(--color-bg-surface);
  border-left: 1px solid var(--color-border-light);
  box-shadow: var(--shadow-xl);
  transform: translateX(100%);
  transition: transform var(--transition-normal);
  display: flex;
  flex-direction: column;
}

.fc-drawer--open {
  transform: translateX(0);
}

.fc-drawer-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: var(--space-4) var(--space-5);
  border-bottom: 1px solid var(--color-border-light);
  flex-shrink: 0;
}

.fc-drawer-title {
  margin: 0;
  font-size: var(--text-lg);
  font-weight: 700;
  color: var(--color-text-primary);
}

.fc-drawer-close {
  border: none;
  background: transparent;
  color: var(--color-text-muted);
  font-size: var(--text-lg);
  cursor: pointer;
  padding: var(--space-1) var(--space-2);
  border-radius: var(--radius-sm);
  transition: background var(--transition-fast), color var(--transition-fast);
}

.fc-drawer-close:hover {
  background: var(--color-bg-surface-alt);
  color: var(--color-text-primary);
}

.fc-drawer-body {
  flex: 1;
  overflow-y: auto;
  padding: var(--space-5);
  display: flex;
  flex-direction: column;
  gap: var(--space-5);
}

.fc-drawer-section {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}

.fc-drawer-section-title {
  margin: 0;
  font-size: var(--text-base);
  font-weight: 700;
  color: var(--color-text-primary);
  padding-bottom: var(--space-2);
  border-bottom: 1px solid var(--color-border-light);
}

.fc-drawer-list {
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}

.fc-drawer-row {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: var(--space-3);
  padding-bottom: var(--space-2);
  border-bottom: 1px dashed var(--color-border-light);
}

.fc-drawer-row dt {
  margin: 0;
  font-size: var(--text-sm);
  color: var(--color-text-secondary);
  font-weight: 600;
}

.fc-drawer-row dd {
  margin: 0;
  font-size: var(--text-base);
  color: var(--color-text-body);
  text-align: right;
  word-break: break-all;
}

.fc-drawer-mono {
  font-family: var(--font-family-mono);
  color: var(--color-accent-text);
}

.fc-drawer-num {
  font-family: var(--font-family-mono);
  color: var(--color-text-primary);
}

.fc-drawer-tag {
  display: inline-block;
  padding: 2px 8px;
  border-radius: var(--radius-sm);
  font-size: var(--text-xs);
  font-weight: 600;
  white-space: nowrap;
}

.fc-drawer-skeleton {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  color: var(--color-text-secondary);
}

.fc-skel-block {
  height: 12px;
  border-radius: var(--radius-sm);
  background: var(--color-bg-surface-alt);
}

.fc-drawer-error {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  padding: var(--space-4);
  border-radius: var(--radius-md);
  background: var(--color-danger-bg);
  border: 1px solid var(--color-danger-border);
  color: var(--color-danger);
}

.fc-drawer-error-msg {
  margin: 0;
  font-size: var(--text-sm);
}

.fc-drawer-pdf-placeholder {
  border: 1px dashed var(--color-border-medium);
  border-radius: var(--radius-md);
  padding: var(--space-5);
  text-align: center;
  color: var(--color-text-muted);
  font-size: var(--text-sm);
  background: var(--color-bg-surface-alt);
}

/* V042-R3 N3：抽屉里 PDF 元数据简介 + 入口按钮 */
.fc-drawer-pdf-summary {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  padding: var(--space-3) var(--space-4);
  border: 1px solid var(--color-border-light);
  border-radius: var(--radius-md);
  background: var(--color-bg-surface-alt);
}
.fc-drawer-pdf-meta {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: var(--space-2) var(--space-4);
  margin: 0;
}
.fc-drawer-pdf-meta-row {
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.fc-drawer-pdf-meta-row dt {
  font-size: var(--text-xs);
  color: var(--color-text-muted);
}
.fc-drawer-pdf-meta-row dd {
  margin: 0;
  font-size: var(--text-sm);
  color: var(--color-text-primary);
  word-break: break-all;
}
.fc-drawer-pdf-open-btn {
  appearance: none;
  border: 1px solid var(--color-primary, #2563eb);
  background: var(--color-primary, #2563eb);
  color: #fff;
  font-size: var(--text-sm);
  padding: 8px 14px;
  border-radius: var(--radius-sm);
  cursor: pointer;
  align-self: flex-start;
}
.fc-drawer-pdf-open-btn:hover {
  filter: brightness(1.05);
}

/* V042-R3 N3：PDF 对照 Modal（独立全屏） */
.fc-drawer-pdf-modal-overlay {
  position: fixed;
  inset: 0;
  z-index: 600;
  background: rgba(15, 23, 42, 0.55);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 24px;
}
.fc-drawer-pdf-modal-dialog {
  width: min(1280px, calc(100vw - 48px));
  height: min(880px, calc(100vh - 48px));
  background: var(--color-bg-surface, #fff);
  border-radius: 8px;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  box-shadow: 0 20px 50px rgba(15, 23, 42, 0.35);
}
.fc-drawer-pdf-modal-header {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 16px;
  border-bottom: 1px solid var(--color-border-light, #e5e7eb);
}
.fc-drawer-pdf-modal-title {
  margin: 0;
  font-size: 15px;
  font-weight: 600;
}
.fc-drawer-pdf-modal-close {
  appearance: none;
  border: 0;
  background: transparent;
  font-size: 18px;
  cursor: pointer;
  color: var(--color-text-muted);
  padding: 4px 8px;
  border-radius: 4px;
}
.fc-drawer-pdf-modal-close:hover {
  background: var(--color-bg-surface-alt, #f3f4f6);
}
.fc-drawer-pdf-modal-body {
  flex: 1;
  min-height: 0;
  padding: 12px 16px 16px;
  overflow: hidden;
}

.fc-drawer-footer {
  flex-shrink: 0;
  border-top: 1px solid var(--color-border-light);
  padding: var(--space-3) var(--space-5);
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  background: var(--color-bg-surface);
}

.fc-drawer-footer-error {
  font-size: var(--text-sm);
  color: var(--color-danger);
}

.fc-drawer-footer-info {
  font-size: var(--text-sm);
  color: var(--color-info);
}

.fc-drawer-footer-actions {
  display: flex;
  justify-content: flex-end;
  gap: var(--space-2);
}

.fc-drawer-btn {
  appearance: none;
  border: 1px solid var(--color-border-medium);
  background: var(--color-bg-surface);
  color: var(--color-text-primary);
  font-size: var(--text-sm);
  padding: 6px 14px;
  border-radius: var(--radius-sm);
  cursor: pointer;
  transition: background var(--transition-fast), color var(--transition-fast);
}

.fc-drawer-btn:hover:not(:disabled) {
  background: var(--color-bg-surface-alt);
}

.fc-drawer-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.fc-drawer-btn--primary {
  background: var(--color-accent);
  color: var(--color-text-on-accent, #fff);
  border-color: var(--color-accent);
}

.fc-drawer-btn--primary:hover:not(:disabled) {
  background: var(--color-accent-hover, var(--color-accent));
}
</style>
