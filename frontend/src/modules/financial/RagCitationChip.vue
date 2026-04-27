<template>
  <span
    class="rag-citation-chip"
    :class="{ 'has-page': displayPage, 'has-url': displayUrl }"
    :title="tooltipText"
    @click="handleClick"
  >
    <span class="chip-icon">{{ isEvidence ? '🔗' : '📄' }}</span>
    <span class="chip-label">{{ label }}</span>
    <span v-if="displayPage" class="chip-page">P.{{ displayPage }}</span>
  </span>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  citation: {
    type: Object,
    required: true
    // Supports two formats:
    // RagCitationDto: { chunkId, symbol, reportDate, reportType, section, pageStart, pageEnd, text, score }
    // StockCopilotMcpEvidenceDto: { point, title, source, publishedAt, url, excerpt, sourceRecordId }
  }
})

const emit = defineEmits(['open-pdf'])

// Detect which format this citation is in
const isEvidence = computed(() => !!(props.citation.point || props.citation.sourceRecordId || (props.citation.title && !props.citation.chunkId)))

// Unified field accessors
const displayPage = computed(() => props.citation.pageStart || null)
const displayUrl = computed(() => props.citation.url || null)

const label = computed(() => {
  if (isEvidence.value) {
    // MCP evidence format
    const parts = []
    if (props.citation.title) {
      const t = props.citation.title
      parts.push(t.length > 20 ? t.slice(0, 20) + '…' : t)
    }
    if (props.citation.source) parts.push(props.citation.source)
    if (parts.length === 0 && props.citation.point) {
      const p = props.citation.point
      parts.push(p.length > 25 ? p.slice(0, 25) + '…' : p)
    }
    return parts.join(' · ') || '引用来源'
  }
  // RAG citation format
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
  if (isEvidence.value) {
    if (props.citation.title) lines.push(`标题: ${props.citation.title}`)
    if (props.citation.source) lines.push(`来源: ${props.citation.source}`)
    if (props.citation.publishedAt) lines.push(`发布: ${props.citation.publishedAt}`)
    if (props.citation.point) lines.push(`要点: ${props.citation.point.slice(0, 120)}`)
    if (props.citation.excerpt) lines.push(`摘要: ${props.citation.excerpt.slice(0, 100)}`)
    if (props.citation.url) lines.push(`链接: ${props.citation.url}`)
  } else {
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
  }
  return lines.join('\n')
})

function handleClick() {
  if (isEvidence.value && props.citation.url) {
    window.open(props.citation.url, '_blank', 'noopener,noreferrer')
    return
  }
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

.rag-citation-chip.has-url {
  cursor: pointer;
}

.rag-citation-chip.has-page:hover,
.rag-citation-chip.has-url:hover {
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
