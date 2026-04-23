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

const candidates = computed(() => {
  const d = props.detail
  if (!d) return []
  return d.votingCandidates || d.VotingCandidates || []
})

const votingNotes = computed(() => {
  const d = props.detail
  if (!d) return null
  return d.votingNotes || d.VotingNotes || null
})

const textLen = (c) => c.textLength || c.TextLength || 0

const formatTextLen = (len) => {
  if (len >= 10000) return `${(len / 10000).toFixed(1)}万字符`
  if (len >= 1000) return `${(len / 1000).toFixed(1)}k字符`
  return `${len}字符`
}
</script>

<template>
  <section class="fc-pdf-voting-panel" data-testid="fc-pdf-voting-panel">
    <header class="fc-pdf-voting-header">
      <h3 class="fc-pdf-voting-title">解析投票</h3>
      <button
        type="button"
        class="fc-pdf-voting-btn"
        :class="{ 'fc-pdf-voting-btn--loading': reparsing }"
        data-testid="fc-pdf-voting-reparse-btn"
        :disabled="reparsing"
        @click="onReparseClick"
      >
        <span
          v-if="reparsing"
          class="fc-pdf-voting-spinner"
          aria-hidden="true"
          data-testid="fc-pdf-voting-reparse-spinner"
        ></span>
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

    <!-- v0.4.2 NS5: Voting candidates display -->
    <div v-if="candidates.length > 0" class="fc-voting-candidates" data-testid="fc-voting-candidates">
      <h4 class="fc-voting-section-title">提取器对比</h4>
      <div
        v-for="c in candidates" :key="c.extractor || c.Extractor"
        class="fc-voting-candidate"
        :class="{ 'fc-voting-candidate--winner': c.isWinner || c.IsWinner }"
        data-testid="fc-voting-candidate"
      >
        <div class="fc-voting-candidate-header">
          <span class="fc-voting-candidate-name">{{ c.extractor || c.Extractor }}</span>
          <span v-if="c.isWinner || c.IsWinner" class="fc-voting-winner-badge" data-testid="fc-voting-winner-badge">✓ 胜出</span>
          <span v-else-if="!(c.success ?? c.Success ?? true)" class="fc-voting-failed-badge" data-testid="fc-voting-failed-badge">✗ 失败</span>
        </div>
        <div v-if="c.success ?? c.Success" class="fc-voting-candidate-meta">
          <span>{{ c.pageCount || c.PageCount }} 页</span>
          <span v-if="textLen(c) > 0">{{ formatTextLen(textLen(c)) }}</span>
        </div>
        <div v-if="c.sampleText || c.SampleText" class="fc-voting-candidate-sample">
          {{ c.sampleText || c.SampleText }}
        </div>
      </div>
    </div>

    <!-- Voting notes -->
    <div v-if="votingNotes" class="fc-voting-notes" data-testid="fc-voting-notes">
      <h4 class="fc-voting-section-title">投票说明</h4>
      <p class="fc-voting-notes-text">{{ votingNotes }}</p>
    </div>
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
.fc-pdf-voting-btn--loading {
  cursor: progress !important;
  display: inline-flex;
  align-items: center;
  gap: 6px;
}
.fc-pdf-voting-spinner {
  display: inline-block;
  width: 10px;
  height: 10px;
  border: 2px solid rgba(37, 99, 235, 0.25);
  border-top-color: var(--color-primary, #2563eb);
  border-radius: 50%;
  animation: fc-pdf-voting-spin 0.8s linear infinite;
}
@keyframes fc-pdf-voting-spin {
  to { transform: rotate(360deg); }
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
.fc-voting-candidates { margin-top: 12px; }
.fc-voting-section-title { font-size: 13px; font-weight: 600; margin-bottom: 8px; color: var(--color-text, #111827); }
.fc-voting-candidate { padding: 8px 12px; border: 1px solid var(--color-border, #e2e8f0); border-radius: 6px; margin-bottom: 6px; }
.fc-voting-candidate--winner { border-color: var(--color-primary, #3b82f6); background: rgba(59, 130, 246, 0.06); }
.fc-voting-candidate-header { display: flex; align-items: center; gap: 8px; }
.fc-voting-candidate-name { font-weight: 500; font-size: 13px; }
.fc-voting-winner-badge { font-size: 11px; color: var(--color-primary, #3b82f6); font-weight: 600; }
.fc-voting-failed-badge { font-size: 11px; color: #ef4444; }
.fc-voting-candidate-meta { font-size: 12px; color: var(--color-text-muted, #666); margin-top: 4px; }
.fc-voting-candidate-sample { font-size: 11px; color: var(--color-text-muted, #999); margin-top: 4px; max-height: 60px; overflow: hidden; white-space: pre-wrap; word-break: break-all; }
.fc-voting-notes { margin-top: 12px; }
.fc-voting-notes-text { font-size: 12px; color: var(--color-text-muted, #666); white-space: pre-wrap; }
</style>
