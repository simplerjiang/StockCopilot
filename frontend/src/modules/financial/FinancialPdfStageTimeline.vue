<script setup>
import { computed } from 'vue'

/**
 * V041-S7: FinancialPdfStageTimeline
 *
 * 展示 PdfFileDetail.stageLogs 的 5 阶段执行 timeline：
 *   download → extract → vote → parse → persist
 *
 * stageLogs 任一阶段缺失时显示为 pending；按 stage 字段建 map，
 * 大小写不敏感。compact=true 时折叠 message 详情。
 *
 * 仅展示，不发出事件，不调用 API。
 */

const props = defineProps({
  stageLogs: { type: Array, default: () => [] },
  compact: { type: Boolean, default: false }
})

const STAGE_ORDER = ['download', 'extract', 'vote', 'parse', 'persist']
const STAGE_LABELS = {
  download: '下载',
  extract: '文本提取',
  vote: '提取器投票',
  parse: '结构化解析',
  persist: '入库'
}

const STATUS_LABELS = {
  success: '成功',
  failed: '失败',
  skipped: '跳过',
  pending: '待执行'
}

const PLACEHOLDER = '—'
const MESSAGE_MAX = 240

const normalizeStatus = (raw) => {
  if (raw == null) return 'pending'
  const text = String(raw).trim().toLowerCase()
  if (text === 'success' || text === 'failed' || text === 'skipped') return text
  return 'pending'
}

const formatDuration = (raw) => {
  if (raw == null || raw === '') return PLACEHOLDER
  const ms = Number(raw)
  if (!Number.isFinite(ms) || ms < 0) return PLACEHOLDER
  if (ms > 1000) return `${(ms / 1000).toFixed(1)}s`
  return `${Math.round(ms)}ms`
}

const truncateMessage = (raw) => {
  if (raw == null) return ''
  const text = String(raw).trim()
  if (!text) return ''
  return text.length > MESSAGE_MAX ? `${text.slice(0, MESSAGE_MAX)}…` : text
}

const stageMap = computed(() => {
  const map = new Map()
  const list = Array.isArray(props.stageLogs) ? props.stageLogs : []
  for (const entry of list) {
    if (!entry || entry.stage == null) continue
    const key = String(entry.stage).trim().toLowerCase()
    if (!STAGE_ORDER.includes(key)) continue
    // 后写入覆盖前写入，保留同阶段最新一条
    map.set(key, entry)
  }
  return map
})

const nodes = computed(() =>
  STAGE_ORDER.map((stage) => {
    const entry = stageMap.value.get(stage) || null
    const status = entry ? normalizeStatus(entry.status) : 'pending'
    return {
      stage,
      label: STAGE_LABELS[stage],
      status,
      statusLabel: STATUS_LABELS[status],
      durationText: entry ? formatDuration(entry.durationMs) : PLACEHOLDER,
      message: entry ? truncateMessage(entry.message) : '',
      hasEntry: !!entry
    }
  })
)

const isEmpty = computed(() => stageMap.value.size === 0)

const successCount = computed(() =>
  nodes.value.filter((n) => n.status === 'success').length
)

const totalDurationText = computed(() => {
  const list = Array.isArray(props.stageLogs) ? props.stageLogs : []
  let total = 0
  let any = false
  for (const entry of list) {
    if (!entry) continue
    const ms = Number(entry.durationMs)
    if (Number.isFinite(ms) && ms >= 0) {
      total += ms
      any = true
    }
  }
  if (!any) return PLACEHOLDER
  return formatDuration(total)
})

const lastFailedStageLabel = computed(() => {
  // 按 5 阶段固定顺序找最后一个 failed
  for (let i = nodes.value.length - 1; i >= 0; i -= 1) {
    if (nodes.value[i].status === 'failed') return nodes.value[i].label
  }
  return ''
})
</script>

<template>
  <section class="fc-pdf-stage-timeline" data-testid="fc-pdf-stage-timeline">
    <header class="fc-pdf-stage-summary" data-testid="fc-pdf-stage-summary">
      <span class="fc-pdf-stage-summary-item">
        成功 <strong data-testid="fc-pdf-stage-success-count">{{ successCount }}</strong> / 5
      </span>
      <span class="fc-pdf-stage-summary-item">
        总耗时 <strong data-testid="fc-pdf-stage-total-duration">{{ totalDurationText }}</strong>
      </span>
      <span
        v-if="lastFailedStageLabel"
        class="fc-pdf-stage-summary-item fc-pdf-stage-summary-failed"
        data-testid="fc-pdf-stage-last-failed"
      >
        最后失败：{{ lastFailedStageLabel }}
      </span>
    </header>

    <p
      v-if="isEmpty"
      class="fc-pdf-stage-empty"
      data-testid="fc-pdf-stage-empty"
    >
      尚未解析
    </p>

    <ol class="fc-pdf-stage-list" :class="{ 'is-compact': compact }">
      <li
        v-for="(node, index) in nodes"
        :key="node.stage"
        class="fc-pdf-stage-item"
        :class="[`is-${node.status}`, { 'has-connector': index < nodes.length - 1 }]"
        :data-testid="`fc-pdf-stage-item-${node.stage}`"
        :data-status="node.status"
      >
        <div class="fc-pdf-stage-marker" aria-hidden="true">
          <span class="fc-pdf-stage-dot"></span>
          <span v-if="index < nodes.length - 1" class="fc-pdf-stage-line"></span>
        </div>
        <div class="fc-pdf-stage-body">
          <div class="fc-pdf-stage-headline">
            <span class="fc-pdf-stage-label">{{ node.label }}</span>
            <span
              class="fc-pdf-stage-badge"
              :class="`is-${node.status}`"
              :data-testid="`fc-pdf-stage-badge-${node.stage}`"
            >{{ node.statusLabel }}</span>
            <span
              class="fc-pdf-stage-duration"
              :data-testid="`fc-pdf-stage-duration-${node.stage}`"
            >{{ node.durationText }}</span>
          </div>
          <p
            v-if="node.status === 'failed' && node.message && !compact"
            class="fc-pdf-stage-message"
            :data-testid="`fc-pdf-stage-message-${node.stage}`"
          >{{ node.message }}</p>
          <p
            v-else-if="node.status === 'failed' && node.message && compact"
            class="fc-pdf-stage-message-tooltip"
            :title="node.message"
            :data-testid="`fc-pdf-stage-message-${node.stage}`"
          >错误详情</p>
        </div>
      </li>
    </ol>
  </section>
</template>

<style scoped>
.fc-pdf-stage-timeline {
  --stage-success: #059669;
  --stage-failed:  #dc2626;
  --stage-skipped: #9ca3af;
  --stage-pending: #e5e7eb;
  border: 1px solid var(--color-border-light, #e5e7eb);
  border-radius: 8px;
  background: var(--color-bg-surface, #ffffff);
  padding: 12px 14px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.fc-pdf-stage-summary {
  display: flex;
  flex-wrap: wrap;
  gap: 8px 16px;
  font-size: 12px;
  color: var(--color-text-secondary, #6b7280);
}
.fc-pdf-stage-summary-item strong {
  color: var(--color-text-primary, #111827);
  font-variant-numeric: tabular-nums;
  margin: 0 2px;
}
.fc-pdf-stage-summary-failed {
  color: var(--stage-failed);
}

.fc-pdf-stage-empty {
  margin: 0;
  font-size: 12px;
  color: var(--color-text-muted, #9ca3af);
}

.fc-pdf-stage-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
}

.fc-pdf-stage-item {
  display: flex;
  gap: 10px;
  position: relative;
  padding: 4px 0;
}

.fc-pdf-stage-marker {
  position: relative;
  width: 14px;
  flex: 0 0 14px;
  display: flex;
  flex-direction: column;
  align-items: center;
}

.fc-pdf-stage-dot {
  width: 12px;
  height: 12px;
  border-radius: 50%;
  background: var(--stage-pending);
  border: 2px solid var(--stage-pending);
  margin-top: 4px;
  z-index: 1;
}

.fc-pdf-stage-line {
  flex: 1 1 auto;
  width: 2px;
  background: var(--stage-pending);
  margin-top: 2px;
  min-height: 14px;
}

.fc-pdf-stage-item.is-success .fc-pdf-stage-dot {
  background: var(--stage-success);
  border-color: var(--stage-success);
}
.fc-pdf-stage-item.is-failed .fc-pdf-stage-dot {
  background: var(--stage-failed);
  border-color: var(--stage-failed);
}
.fc-pdf-stage-item.is-skipped .fc-pdf-stage-dot {
  background: var(--stage-skipped);
  border-color: var(--stage-skipped);
}

.fc-pdf-stage-body {
  flex: 1 1 auto;
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 2px 8px 6px;
  border-radius: 6px;
  border: 1px solid transparent;
}

.fc-pdf-stage-item.is-failed .fc-pdf-stage-body {
  border-color: var(--stage-failed);
  background: rgba(220, 38, 38, 0.06);
}
.fc-pdf-stage-item.is-skipped .fc-pdf-stage-body {
  color: var(--color-text-muted, #9ca3af);
}

.fc-pdf-stage-headline {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.fc-pdf-stage-label {
  font-size: 13px;
  font-weight: 600;
  color: var(--color-text-primary, #111827);
}
.fc-pdf-stage-item.is-skipped .fc-pdf-stage-label,
.fc-pdf-stage-item.is-pending .fc-pdf-stage-label {
  color: var(--color-text-secondary, #6b7280);
  font-weight: 500;
}

.fc-pdf-stage-badge {
  font-size: 11px;
  padding: 1px 8px;
  border-radius: 999px;
  background: var(--stage-pending);
  color: var(--color-text-secondary, #6b7280);
  border: 1px solid transparent;
}
.fc-pdf-stage-badge.is-success {
  background: rgba(5, 150, 105, 0.12);
  color: var(--stage-success);
}
.fc-pdf-stage-badge.is-failed {
  background: rgba(220, 38, 38, 0.12);
  color: var(--stage-failed);
}
.fc-pdf-stage-badge.is-skipped {
  background: rgba(156, 163, 175, 0.16);
  color: var(--stage-skipped);
}

.fc-pdf-stage-duration {
  font-size: 11px;
  color: var(--color-text-muted, #9ca3af);
  font-variant-numeric: tabular-nums;
}

.fc-pdf-stage-message {
  margin: 0;
  font-size: 12px;
  line-height: 1.5;
  color: var(--stage-failed);
  word-break: break-all;
}
.fc-pdf-stage-message-tooltip {
  margin: 0;
  font-size: 11px;
  color: var(--stage-failed);
  cursor: help;
  text-decoration: underline dotted;
  width: fit-content;
}

.fc-pdf-stage-list.is-compact .fc-pdf-stage-item {
  padding: 2px 0;
}
</style>
