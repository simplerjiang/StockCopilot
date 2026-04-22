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
  formatFieldValue
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

async function loadDetail() {
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
  resolvePdfFileId()
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

  // 没有则按 symbol + reportType 查 PDF 列表，取最新一份
  const symbol = resolveSymbol()
  if (!symbol) {
    resolvedPdfId.value = null
    return
  }
  const reportType = props.item?.reportType || props.item?.ReportType || detail.value?.reportType || detail.value?.ReportType || null

  resolvingPdf.value = true
  try {
    const res = await listPdfFiles({ symbol, reportType, page: 1, pageSize: 5 })
    if (token !== pdfResolveToken) return
    const items = Array.isArray(res?.items) ? res.items : (Array.isArray(res?.Items) ? res.Items : [])
    if (items.length === 0) {
      resolvedPdfId.value = null
      return
    }
    // 优先匹配 reportPeriod === reportDate；不匹配则取第一条（后端按 LastParsedAt desc 排序）
    const reportDate = props.item?.reportDate || props.item?.ReportDate || detail.value?.reportDate || detail.value?.ReportDate || null
    let pick = null
    if (reportDate) {
      pick = items.find(x => (x.reportPeriod || x.ReportPeriod) === reportDate) || null
    }
    if (!pick) pick = items[0]
    const newId = String(pick.id || pick.Id || '') || null
    if (resolvedPdfId.value !== newId) resolvedPdfId.value = newId
  } catch (e) {
    if (token !== pdfResolveToken) return
    resolvePdfError.value = e?.message || '加载 PDF 列表失败'
    resolvedPdfId.value = null
  } finally {
    if (token === pdfResolveToken) {
      resolvingPdf.value = false
    }
  }
}

async function onRecollect() {
  const symbol = resolveSymbol()
  if (!symbol || recollecting.value) return
  recollecting.value = true
  recollectError.value = ''
  recollectMessage.value = ''
  try {
    await recollectFinancialReport(symbol)
    recollectMessage.value = '已重新采集，刷新中...'
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
    await collectPdfFiles(symbol)
    collectPdfMessage.value = 'PDF 原件采集完成，刷新中...'
    // 重新解析 PDF 列表（这里不重新拉财报详情，避免不必要的上游调用）
    await resolvePdfFileId()
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
  return fields.map(f => ({
    key: f.key,
    label: f.label,
    display: formatFieldValue(pickFieldValue(dict, f))
  }))
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
                  <dd class="fc-drawer-num">{{ row.display }}</dd>
                </div>
              </dl>
            </section>

            <section class="fc-drawer-section">
              <h4 class="fc-drawer-section-title">利润表</h4>
              <dl class="fc-drawer-list">
                <div v-for="row in incomeRows" :key="row.key" class="fc-drawer-row">
                  <dt>{{ row.label }}</dt>
                  <dd class="fc-drawer-num">{{ row.display }}</dd>
                </div>
              </dl>
            </section>

            <section class="fc-drawer-section">
              <h4 class="fc-drawer-section-title">现金流量表</h4>
              <dl class="fc-drawer-list">
                <div v-for="row in cashRows" :key="row.key" class="fc-drawer-row">
                  <dt>{{ row.label }}</dt>
                  <dd class="fc-drawer-num">{{ row.display }}</dd>
                </div>
              </dl>
            </section>

            <section class="fc-drawer-section">
              <h4 class="fc-drawer-section-title">PDF 原件</h4>
              <!-- V041-S8 NIT-4：只要已有 resolvedPdfId 就保持 ComparePane 挂载，
                   避免 reparse 后 loadDetail() → resolvePdfFileId() 把 resolvingPdf
                   置 true 导致 ComparePane 短暂卸载/重新挂载、internalDetail 被冲掉。 -->
              <div
                v-if="resolvedPdfId"
                class="fc-drawer-pdf-compare"
                data-testid="fc-drawer-pdf-compare"
              >
                <FinancialReportComparePane
                  :pdf-file-id="resolvedPdfId"
                  @refresh="loadDetail"
                />
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
