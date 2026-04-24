<template>
  <span
    class="rag-citation-chip"
    :class="{ 'has-page': citation.pageStart }"
    :title="tooltipText"
    @click="handleClick"
  >
    <span class="chip-icon">📄</span>
    <span class="chip-label">{{ label }}</span>
    <span v-if="citation.pageStart" class="chip-page">P.{{ citation.pageStart }}</span>
  </span>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  citation: {
    type: Object,
    required: true
    // Expected shape: { chunkId, symbol, reportDate, reportType, section, pageStart, pageEnd, text, score }
  }
})

const emit = defineEmits(['open-pdf'])

const label = computed(() => {
  const parts = []
  if (props.citation.reportDate) parts.push(props.citation.reportDate)
  if (props.citation.reportType) parts.push(props.citation.reportType)
  if (props.citation.section) {
    const sec = props.citation.section
    parts.push(sec.length > 15 ? sec.slice(0, 15) + '…' : sec)
  }
  return parts.join(' ') || '财报引用'
})

const tooltipText = computed(() => {
  const lines = []
  if (props.citation.reportDate) lines.push(`报告期: ${props.citation.reportDate}`)
  if (props.citation.reportType) lines.push(`类型: ${props.citation.reportType}`)
  if (props.citation.section) lines.push(`章节: ${props.citation.section}`)
  if (props.citation.pageStart) {
    const pageRange = props.citation.pageEnd && props.citation.pageEnd !== props.citation.pageStart
      ? `${props.citation.pageStart}-${props.citation.pageEnd}`
      : `${props.citation.pageStart}`
    lines.push(`页码: ${pageRange}`)
  }
  if (props.citation.text) {
    lines.push(`摘要: ${props.citation.text.slice(0, 100)}...`)
  }
  return lines.join('\n')
})

function handleClick() {
  if (props.citation.pageStart) {
    emit('open-pdf', {
      symbol: props.citation.symbol,
      page: props.citation.pageStart,
      chunkId: props.citation.chunkId
    })
  }
}
</script>

<style scoped>
.rag-citation-chip {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 2px 8px;
  border-radius: 12px;
  background: rgba(30, 64, 175, 0.08);
  color: var(--color-text-secondary, #64748b);
  font-size: 12px;
  line-height: 1.4;
  cursor: default;
  user-select: none;
  transition: background 0.15s;
}

.rag-citation-chip.has-page {
  cursor: pointer;
}

.rag-citation-chip.has-page:hover {
  background: rgba(30, 64, 175, 0.16);
  color: var(--color-text-primary, #1e40af);
}

.chip-icon {
  font-size: 11px;
}

.chip-label {
  max-width: 180px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.chip-page {
  font-weight: 600;
  color: var(--color-primary, #2563eb);
  font-size: 11px;
}
</style>
