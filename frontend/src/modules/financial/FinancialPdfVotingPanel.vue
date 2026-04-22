<script setup>
import { computed } from 'vue'

/**
 * V041-S4: FinancialPdfVotingPanel
 *
 * 展示 PdfFileDetail 中的解析投票元信息（extractor / voteConfidence / fieldCount /
 * lastError / lastParsedAt / lastReparsedAt），并对外 emit 'reparse' 事件，由父组件
 * 决定是否调用 reparsePdfFile API（保持调用与刷新职责集中）。
 *
 * 后端当前未暴露 candidates 数组，本面板仅展示当前采用的提取器；候选排序明细将在后续版本补充。
 */

const props = defineProps({
  detail: { type: Object, default: null },
  reparsing: { type: Boolean, default: false }
})

const emit = defineEmits(['reparse'])

const PLACEHOLDER = '—'
const UNKNOWN = '未知'
const ERROR_MAX = 200

const formatTime = (raw) => {
  if (raw == null || raw === '') return PLACEHOLDER
  const date = new Date(raw)
  if (Number.isNaN(date.getTime())) return PLACEHOLDER
  try {
    return date.toLocaleString()
  } catch {
    return String(raw)
  }
}

const extractor = computed(() => {
  const value = props.detail?.extractor
  return value == null || String(value).trim() === '' ? UNKNOWN : String(value)
})

const voteConfidence = computed(() => {
  const value = props.detail?.voteConfidence
  return value == null || String(value).trim() === '' ? UNKNOWN : String(value)
})

const fieldCount = computed(() => {
  const value = props.detail?.fieldCount
  return Number.isFinite(Number(value)) ? Number(value) : 0
})

const lastParsedAt = computed(() => formatTime(props.detail?.lastParsedAt))
const lastReparsedAt = computed(() => formatTime(props.detail?.lastReparsedAt))

const lastError = computed(() => {
  const value = props.detail?.lastError
  if (value == null) return ''
  const text = String(value).trim()
  if (!text) return ''
  return text.length > ERROR_MAX ? `${text.slice(0, ERROR_MAX)}…` : text
})

const onReparseClick = () => {
  if (props.reparsing) return
  emit('reparse')
}
</script>

<template>
  <section class="fc-pdf-voting-panel" data-testid="fc-pdf-voting-panel">
    <header class="fc-pdf-voting-header">
      <h3 class="fc-pdf-voting-title">解析投票</h3>
      <button
        type="button"
        class="fc-pdf-voting-btn"
        data-testid="fc-pdf-voting-reparse-btn"
        :disabled="reparsing"
        @click="onReparseClick"
      >
        {{ reparsing ? '解析中…' : '重新解析' }}
      </button>
    </header>

    <dl class="fc-pdf-voting-grid">
      <div class="fc-pdf-voting-row">
        <dt>当前提取器</dt>
        <dd data-testid="fc-pdf-voting-extractor">{{ extractor }}</dd>
      </div>
      <div class="fc-pdf-voting-row">
        <dt>投票置信度</dt>
        <dd data-testid="fc-pdf-voting-confidence">{{ voteConfidence }}</dd>
      </div>
      <div class="fc-pdf-voting-row">
        <dt>字段总数</dt>
        <dd data-testid="fc-pdf-voting-field-count">{{ fieldCount }}</dd>
      </div>
      <div class="fc-pdf-voting-row">
        <dt>首次解析</dt>
        <dd>{{ lastParsedAt }}</dd>
      </div>
      <div class="fc-pdf-voting-row">
        <dt>最近重解析</dt>
        <dd>{{ lastReparsedAt }}</dd>
      </div>
    </dl>

    <div
      v-if="lastError"
      class="fc-pdf-voting-error"
      role="alert"
      data-testid="fc-pdf-voting-error"
    >
      <strong>解析错误：</strong>
      <span>{{ lastError }}</span>
    </div>

    <p class="fc-pdf-voting-footnote">
      * 候选提取器排序与投票明细将在后续版本暴露
    </p>
  </section>
</template>

<style scoped>
.fc-pdf-voting-panel {
  border: 1px solid var(--color-border, #e5e7eb);
  border-radius: 8px;
  background: var(--color-surface, #fff);
  padding: 12px 14px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.fc-pdf-voting-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}
.fc-pdf-voting-title {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
  color: var(--color-text, #111827);
}
.fc-pdf-voting-btn {
  border: 1px solid var(--color-primary, #2563eb);
  color: var(--color-primary, #2563eb);
  background: #fff;
  padding: 4px 12px;
  border-radius: 4px;
  font-size: 12px;
  cursor: pointer;
}
.fc-pdf-voting-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}
.fc-pdf-voting-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
  gap: 8px 16px;
  margin: 0;
}
.fc-pdf-voting-row {
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.fc-pdf-voting-row dt {
  font-size: 12px;
  color: var(--color-text-muted, #6b7280);
}
.fc-pdf-voting-row dd {
  margin: 0;
  font-size: 13px;
  color: var(--color-text, #111827);
  font-variant-numeric: tabular-nums;
}
.fc-pdf-voting-error {
  border: 1px solid rgba(220, 38, 38, 0.35);
  background: rgba(220, 38, 38, 0.08);
  color: #b91c1c;
  border-radius: 6px;
  padding: 8px 10px;
  font-size: 12px;
  line-height: 1.5;
  word-break: break-all;
}
.fc-pdf-voting-footnote {
  margin: 0;
  font-size: 11px;
  color: var(--color-text-muted, #9ca3af);
}
</style>
