<script setup>
import { computed, onMounted, ref, watch } from 'vue'
import FinancialPdfViewer from './FinancialPdfViewer.vue'
import FinancialPdfParsePreview from './FinancialPdfParsePreview.vue'
import FinancialPdfVotingPanel from './FinancialPdfVotingPanel.vue'
import FinancialPdfStageTimeline from './FinancialPdfStageTimeline.vue'
import {
  buildPdfFileContentUrl,
  fetchPdfFileDetail,
  reparsePdfFile
} from './financialApi.js'
import { useToast } from '../../composables/useToast.js'

/**
 * V041-S5 / V042-P0-A 升级：FinancialReportComparePane
 *
 * 左 PDF 原件 / 右（解析单元 | 投票信息 | 解析阶段）三 Tab 对照面板。
 *
 * Props:
 *   pdfFileId        PDF 文件 ID。可选——存在则作为「初始选中」（picker 中可手动切走）。
 *   pdfFiles         可选，PDF 候选列表（含 id/fileName/fieldCount/lastParsedAt 等元数据）。
 *                    传入后顶部渲染切换器；未传则只展示当前 pdfFileId。
 *   pdfFileDetail    可选，外部已加载的 detail；缺省时内部调 fetchPdfFileDetail
 *   loading / error  外部状态透传（与外部 detail 配套使用）
 *
 * V042-P0-A 智能默认：未传 pdfFileId 但传了 pdfFiles 时，按
 *   1) fieldCount > 0 优先（即真正解析出内容的）
 *   2) 文件名启发：「摘要 / summary / 英文 / english」降权
 *   3) fieldCount desc → lastParsedAt desc
 * 选出 currentPdfId。已传 pdfFileId 时尊重外部选择。
 *
 * V042-P0-D：reparse 完成后主动调 fetchPdfFileDetail 重拉，不依赖 reparse 返回值。
 * V042-P1-A：顶部 + VotingPanel 双按钮共享 reparsing 状态，结束后 toast 反馈。
 * V042-P1-B：右栏新增「解析阶段」Tab，渲染 FinancialPdfStageTimeline。
 *
 * Emits:
 *   refresh(detail)  reparse 成功后通知父级
 *   pdfChange(id)    用户在 picker 中切换 PDF 时通知父级（可选）
 *   close            关闭按钮（保留以便父级控制）
 *
 * 文案约定（避免与抽屉「重新采集报告」混淆）:
 *   本面板「重新解析 PDF」按钮触发 reparsePdfFile，不调用 recollectFinancialReport。
 */

const props = defineProps({
  pdfFileId: { type: [String, Number], default: null },
  pdfFiles: { type: Array, default: () => [] },
  pdfFileDetail: { type: Object, default: null },
  loading: { type: Boolean, default: false },
  error: { type: String, default: null }
})

const emit = defineEmits(['refresh', 'pdfChange', 'close'])

const toast = useToast()

const internalDetail = ref(null)
const internalLoading = ref(false)
const internalError = ref(null)
const reparsing = ref(false)
const rightPane = ref('parse')
const viewerJumpPage = ref(null)
const currentPdfId = ref(null)

let fetchToken = 0

// ── V042-P0-A: smart pick ─────────────────────────────────────────────────
const SUMMARY_PATTERN = /(摘要|summary|英文|english)/i

function fieldCountOf(item) {
  const raw = item?.fieldCount ?? item?.FieldCount ?? 0
  const n = Number(raw)
  return Number.isFinite(n) ? n : 0
}

function lastParsedTimeOf(item) {
  const raw = item?.lastParsedAt ?? item?.LastParsedAt ?? null
  if (!raw) return 0
  const t = new Date(raw).getTime()
  return Number.isFinite(t) ? t : 0
}

function isSummaryFile(item) {
  const name = String(item?.fileName ?? item?.FileName ?? '')
  return SUMMARY_PATTERN.test(name)
}

function pickIdOf(item) {
  if (!item) return null
  const raw = item.id ?? item.Id ?? null
  if (raw === null || raw === undefined || raw === '') return null
  return String(raw)
}

function smartPickPdfId(items) {
  if (!Array.isArray(items) || items.length === 0) return null
  const sorted = [...items].sort((a, b) => {
    const fa = fieldCountOf(a)
    const fb = fieldCountOf(b)
    const hasA = fa > 0 ? 1 : 0
    const hasB = fb > 0 ? 1 : 0
    if (hasA !== hasB) return hasB - hasA
    const sumA = isSummaryFile(a) ? 1 : 0
    const sumB = isSummaryFile(b) ? 1 : 0
    if (sumA !== sumB) return sumA - sumB
    if (fa !== fb) return fb - fa
    return lastParsedTimeOf(b) - lastParsedTimeOf(a)
  })
  return pickIdOf(sorted[0])
}

// ── 候选列表（含元数据，用于 picker）─────────────────────────────────────
const pdfOptions = computed(() => {
  const list = Array.isArray(props.pdfFiles) ? props.pdfFiles : []
  return list
    .map((item) => {
      const id = pickIdOf(item)
      if (!id) return null
      return {
        id,
        fileName: String(item.fileName ?? item.FileName ?? id),
        fieldCount: fieldCountOf(item),
        isSummary: isSummaryFile(item),
        lastParsedAt: item.lastParsedAt ?? item.LastParsedAt ?? null
      }
    })
    .filter(Boolean)
})

const showPicker = computed(() => pdfOptions.value.length > 1)

function resolveInitialPdfId() {
  // 1) 显式传入 pdfFileId 优先
  if (props.pdfFileId !== null && props.pdfFileId !== undefined && props.pdfFileId !== '') {
    return String(props.pdfFileId)
  }
  // 2) 从候选列表智能选
  const picked = smartPickPdfId(props.pdfFiles)
  if (picked) return picked
  return null
}

// 优先使用外部 detail；外部未提供时使用内部加载结果
const effectiveDetail = computed(() => props.pdfFileDetail || internalDetail.value)
const effectiveLoading = computed(() => props.loading || internalLoading.value)
const effectiveError = computed(() => props.error || internalError.value)

const parseUnits = computed(() => {
  const detail = effectiveDetail.value
  if (!detail) return []
  return Array.isArray(detail.parseUnits) ? detail.parseUnits : []
})

const stageLogs = computed(() => {
  const detail = effectiveDetail.value
  if (!detail) return []
  const raw = detail.stageLogs ?? detail.StageLogs ?? []
  return Array.isArray(raw) ? raw : []
})

const headerTitle = computed(() => {
  const d = effectiveDetail.value
  if (!d) return 'PDF 报告对照'
  const parts = []
  if (d.fileName) parts.push(d.fileName)
  if (d.reportPeriod) parts.push(d.reportPeriod)
  return parts.length ? parts.join(' · ') : 'PDF 报告对照'
})

const pdfSrc = computed(() => {
  const id = currentPdfId.value
  if (id === null || id === undefined || id === '') return ''
  const rel = buildPdfFileContentUrl(id)
  // V042-P0-B：在 packaged WebView2 下用绝对 URL，避免 iframe.src 被解析到 file://。
  if (typeof window !== 'undefined' && window.location && window.location.origin
      && /^https?:/.test(window.location.origin)) {
    return `${window.location.origin}${rel}`
  }
  return rel
})

async function loadDetailIfNeeded() {
  // 外部已传 detail 则不重复请求
  if (props.pdfFileDetail) return
  const id = currentPdfId.value
  if (id === null || id === undefined || id === '') {
    internalDetail.value = null
    return
  }
  const token = ++fetchToken
  internalLoading.value = true
  internalError.value = null
  try {
    const data = await fetchPdfFileDetail(id)
    if (token !== fetchToken) return
    internalDetail.value = data
  } catch (e) {
    if (token !== fetchToken) return
    internalError.value = e?.message || '加载 PDF 详情失败'
    internalDetail.value = null
  } finally {
    if (token === fetchToken) {
      internalLoading.value = false
    }
  }
}

async function handleReparse() {
  const id = currentPdfId.value
  if (!id || reparsing.value) return
  reparsing.value = true
  internalError.value = null
  // V042-R3.1：把「成功/失败」与「时间字段刷新」解耦。
  // 后端语义：reparse 哪怕解析失败（success=false 或 parseUnits 为空），也会更新 lastReparsedAt。
  // 因此无论 reparse 返回 success 还是 failed，都必须重拉 detail / 用 result.detail，
  // 让 UI 上「最近重解析」能反映出后端写入的新时间戳；错误展示走 toast/alert，不再阻断刷新。
  let result = null
  let reparseError = null
  try {
    result = await reparsePdfFile(id)
  } catch (e) {
    reparseError = e?.message || '重新解析失败'
  }

  // 不管 reparse 结果，都尝试拉最新 detail；优先使用重拉到的，其次 fallback 到 result.detail。
  let nextDetail = null
  try {
    const fresh = await fetchPdfFileDetail(id)
    if (fresh) nextDetail = fresh
  } catch (_) {
    // 重拉失败时使用 reparse 返回的 detail
  }
  if (!nextDetail && result && result.detail) {
    nextDetail = result.detail
  }

  if (nextDetail) {
    internalDetail.value = nextDetail
    emit('refresh', nextDetail)
  }

  // 错误优先级：先 fetch/throw 异常 → 再 reparse 返回的 success===false / parseError
  const failMsg =
    reparseError ||
    (result && result.success === false ? (result.error || '重新解析失败') : null) ||
    (result && result.parseError ? result.parseError : null)
  if (failMsg) {
    internalError.value = failMsg
    toast.error(`重新解析失败：${failMsg}`)
  } else {
    toast.success('重新解析完成')
  }

  reparsing.value = false
}

function onJumpToPage(page) {
  const n = Number(page)
  if (!Number.isFinite(n) || n <= 0) return
  viewerJumpPage.value = Math.floor(n)
}

function switchTab(name) {
  if (name === 'parse' || name === 'voting' || name === 'stages') {
    rightPane.value = name
  }
}

function onPickerChange(event) {
  const value = event?.target?.value ?? ''
  if (!value || value === currentPdfId.value) return
  currentPdfId.value = value
  viewerJumpPage.value = null
  internalDetail.value = null
  emit('pdfChange', value)
  loadDetailIfNeeded()
}

watch(
  () => props.pdfFileId,
  (next) => {
    if (next === null || next === undefined || next === '') return
    const v = String(next)
    if (v === currentPdfId.value) return
    currentPdfId.value = v
    viewerJumpPage.value = null
    internalDetail.value = null
    loadDetailIfNeeded()
  }
)

watch(
  () => props.pdfFiles,
  (nextList) => {
    // 仅在尚未选定时根据新列表智能选
    if (currentPdfId.value) return
    const picked = smartPickPdfId(nextList)
    if (picked) {
      currentPdfId.value = picked
      loadDetailIfNeeded()
    }
  }
)

watch(
  () => props.pdfFileDetail,
  (next) => {
    // 外部传入新的 detail 时清掉内部缓存的旧值
    if (next) {
      internalDetail.value = null
      internalError.value = null
    }
  }
)

onMounted(() => {
  currentPdfId.value = resolveInitialPdfId()
  loadDetailIfNeeded()
})
</script>

<template>
  <section class="fc-compare-pane" data-testid="fc-compare-pane">
    <header class="fc-compare-header">
      <div class="fc-compare-title-wrap">
        <h3 class="fc-compare-title" data-testid="fc-compare-title">{{ headerTitle }}</h3>
        <p class="fc-compare-subtitle">左侧为 PDF 原件，右侧为结构化解析；「重新解析 PDF」按钮见右上角或投票信息面板。</p>
      </div>
      <div class="fc-compare-header-actions">
        <button
          type="button"
          class="fc-compare-reparse-btn"
          data-testid="fc-compare-reparse-btn"
          :class="{ 'fc-compare-reparse-btn--loading': reparsing }"
          :disabled="reparsing || !currentPdfId"
          @click="handleReparse"
        >
          <span
            v-if="reparsing"
            class="fc-compare-reparse-spinner"
            aria-hidden="true"
            data-testid="fc-compare-reparse-spinner"
          ></span>
          {{ reparsing ? '解析中…' : '重新解析 PDF' }}
        </button>
        <button
          v-if="$attrs.onClose !== undefined"
          type="button"
          class="fc-compare-close"
          data-testid="fc-compare-close"
          @click="emit('close')"
        >关闭</button>
      </div>
    </header>

    <div
      v-if="showPicker"
      class="fc-compare-picker"
      data-testid="fc-compare-picker"
    >
      <label class="fc-compare-picker-label" for="fc-compare-picker-select">PDF：</label>
      <select
        id="fc-compare-picker-select"
        class="fc-compare-picker-select"
        data-testid="fc-compare-picker-select"
        :value="currentPdfId || ''"
        :disabled="reparsing"
        @change="onPickerChange"
      >
        <option
          v-for="opt in pdfOptions"
          :key="opt.id"
          :value="opt.id"
          :data-testid="`fc-compare-picker-opt-${opt.id}`"
        >{{ opt.fileName }} · 字段 {{ opt.fieldCount }}{{ opt.isSummary ? ' · 摘要' : '' }}</option>
      </select>
    </div>

    <div v-if="effectiveError" class="fc-compare-banner-error" role="alert" data-testid="fc-compare-error">
      {{ effectiveError }}
    </div>

    <div class="fc-compare-grid">
      <div class="fc-compare-left" data-testid="fc-compare-left">
        <FinancialPdfViewer
          :src="pdfSrc"
          :page="viewerJumpPage"
          :title="headerTitle"
        />
      </div>

      <div class="fc-compare-right" data-testid="fc-compare-right">
        <div class="fc-compare-tabs" role="tablist">
          <button
            type="button"
            role="tab"
            class="fc-compare-tab"
            :class="{ 'fc-compare-tab--active': rightPane === 'parse' }"
            :aria-selected="rightPane === 'parse'"
            data-testid="fc-compare-tab-parse"
            @click="switchTab('parse')"
          >解析单元</button>
          <button
            type="button"
            role="tab"
            class="fc-compare-tab"
            :class="{ 'fc-compare-tab--active': rightPane === 'voting' }"
            :aria-selected="rightPane === 'voting'"
            data-testid="fc-compare-tab-voting"
            @click="switchTab('voting')"
          >投票信息</button>
          <button
            type="button"
            role="tab"
            class="fc-compare-tab"
            :class="{ 'fc-compare-tab--active': rightPane === 'stages' }"
            :aria-selected="rightPane === 'stages'"
            data-testid="fc-compare-tab-stages"
            @click="switchTab('stages')"
          >解析阶段</button>
        </div>

        <div class="fc-compare-pane-body">
          <div v-show="rightPane === 'parse'" data-testid="fc-compare-parse-wrap">
            <FinancialPdfParsePreview
              :parse-units="parseUnits"
              :loading="effectiveLoading"
              :error="effectiveError"
              @jump-to-page="onJumpToPage"
            />
          </div>

          <div v-show="rightPane === 'voting'" data-testid="fc-compare-voting-wrap">
            <FinancialPdfVotingPanel
              :detail="effectiveDetail"
              :reparsing="reparsing"
              @reparse="handleReparse"
            />
          </div>

          <div v-show="rightPane === 'stages'" data-testid="fc-compare-stages-wrap">
            <FinancialPdfStageTimeline
              :stage-logs="stageLogs"
              :compact="false"
            />
          </div>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.fc-compare-pane {
  display: flex;
  flex-direction: column;
  width: 100%;
  height: 100%;
  min-height: 480px;
  gap: 12px;
}

.fc-compare-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 12px;
}

.fc-compare-title-wrap {
  flex: 1;
  min-width: 0;
}

.fc-compare-title {
  margin: 0;
  font-size: 16px;
  font-weight: 700;
  color: var(--color-text-primary, #111827);
  word-break: break-all;
}

.fc-compare-subtitle {
  margin: 4px 0 0;
  font-size: 12px;
  color: var(--color-text-secondary, #6b7280);
}

.fc-compare-close {
  flex-shrink: 0;
  padding: 4px 12px;
  border: 1px solid var(--color-border, #e4e7eb);
  background: var(--color-bg-elevated, #fff);
  border-radius: 4px;
  cursor: pointer;
  font-size: 12px;
}

.fc-compare-header-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
}

.fc-compare-reparse-btn {
  padding: 6px 14px;
  border: 1px solid var(--color-primary, #2563eb);
  background: var(--color-primary, #2563eb);
  color: #fff;
  border-radius: 4px;
  cursor: pointer;
  font-size: 13px;
  font-weight: 500;
}

.fc-compare-reparse-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.fc-compare-reparse-btn--loading {
  /* V042-R3 M1：解析中加载态视觉反馈 —— 略微下沉 + 文字间距 */
  opacity: 0.85;
  cursor: progress !important;
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.fc-compare-reparse-spinner {
  display: inline-block;
  width: 12px;
  height: 12px;
  border: 2px solid rgba(255, 255, 255, 0.45);
  border-top-color: #fff;
  border-radius: 50%;
  animation: fc-compare-spin 0.8s linear infinite;
}

@keyframes fc-compare-spin {
  to { transform: rotate(360deg); }
}

.fc-compare-picker {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 8px;
  background: var(--color-bg-surface-alt, #f9fafb);
  border: 1px solid var(--color-border, #e4e7eb);
  border-radius: 4px;
  font-size: 13px;
}

.fc-compare-picker-label {
  color: var(--color-text-secondary, #6b7280);
  font-weight: 500;
  flex-shrink: 0;
}

.fc-compare-picker-select {
  flex: 1;
  min-width: 0;
  padding: 4px 8px;
  border: 1px solid var(--color-border, #d1d5db);
  border-radius: 4px;
  background: #fff;
  font-size: 13px;
  cursor: pointer;
}

.fc-compare-picker-select:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.fc-compare-banner-error {
  padding: 8px 12px;
  border-radius: 4px;
  background: #fef2f2;
  border: 1px solid #fecaca;
  color: #b91c1c;
  font-size: 13px;
}

.fc-compare-grid {
  flex: 1;
  display: grid;
  grid-template-columns: 60% 40%;
  gap: 12px;
  min-height: 0;
}

@media (max-width: 768px) {
  .fc-compare-grid {
    grid-template-columns: 1fr;
    grid-auto-rows: minmax(360px, auto);
  }
}

.fc-compare-left {
  min-height: 360px;
  display: flex;
  flex-direction: column;
}

.fc-compare-right {
  display: flex;
  flex-direction: column;
  border: 1px solid var(--color-border, #e4e7eb);
  border-radius: 6px;
  overflow: hidden;
  background: var(--color-bg-elevated, #fff);
  min-height: 360px;
}

.fc-compare-tabs {
  display: flex;
  border-bottom: 1px solid var(--color-border, #e4e7eb);
  background: var(--color-bg-surface-alt, #f9fafb);
}

.fc-compare-tab {
  flex: 1;
  padding: 10px 12px;
  border: 0;
  background: transparent;
  cursor: pointer;
  font-size: 13px;
  color: var(--color-text-secondary, #6b7280);
  border-bottom: 2px solid transparent;
}

.fc-compare-tab--active {
  color: var(--color-accent-text, #2563eb);
  border-bottom-color: var(--color-accent-text, #2563eb);
  background: var(--color-bg-elevated, #fff);
  font-weight: 600;
}

.fc-compare-pane-body {
  flex: 1;
  overflow: auto;
  padding: 12px;
}
</style>
