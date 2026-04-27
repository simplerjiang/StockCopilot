<template>
  <div v-if="citations && citations.length" class="rag-citation-list">
    <div class="citation-header">
      <span class="citation-title">📋 引用来源</span>
      <span class="citation-count">{{ citations.length }} 条</span>
    </div>
    <div class="citation-chips">
      <RagCitationChip
        v-for="(c, idx) in citations"
        :key="c.chunkId || c.sourceRecordId || idx"
        :citation="c"
        @open-pdf="$emit('open-pdf', $event)"
      />
    </div>
    <details v-if="showDetails" class="citation-details">
      <summary>查看引用原文</summary>
      <div v-for="(c, idx) in citations" :key="'d-' + (c.chunkId || c.sourceRecordId || idx)" class="citation-detail-item">
        <div class="detail-meta">
          <span v-if="c.section" class="detail-section">{{ c.section }}</span>
          <span v-if="c.title" class="detail-section">{{ c.title }}</span>
          <span v-if="c.source" class="detail-source">{{ c.source }}</span>
          <span v-if="c.pageStart" class="detail-page">P.{{ c.pageStart }}{{ c.pageEnd && c.pageEnd !== c.pageStart ? `-${c.pageEnd}` : '' }}</span>
          <span v-if="c.publishedAt" class="detail-date">{{ c.publishedAt }}</span>
        </div>
        <p class="detail-text">{{ c.text || c.point || c.excerpt || '' }}</p>
      </div>
    </details>
  </div>
</template>

<script setup>
import RagCitationChip from './RagCitationChip.vue'

defineProps({
  citations: {
    type: Array,
    default: () => []
  },
  showDetails: {
    type: Boolean,
    default: true
  }
})

defineEmits(['open-pdf'])
</script>

<style scoped>
.rag-citation-list {
  margin-top: 8px;
  padding: 8px 12px;
  background: var(--color-surface-secondary, #f8fafc);
  border-radius: 8px;
  border: 1px solid var(--color-border, #e2e8f0);
}

.citation-header {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 6px;
}

.citation-title {
  font-size: 12px;
  font-weight: 600;
  color: var(--color-text-secondary, #64748b);
}

.citation-count {
  font-size: 11px;
  color: var(--color-text-muted, #94a3b8);
}

.citation-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

.citation-details {
  margin-top: 8px;
}

.citation-details summary {
  font-size: 12px;
  color: var(--color-text-secondary, #64748b);
  cursor: pointer;
}

.citation-detail-item {
  margin-top: 6px;
  padding: 6px 8px;
  background: var(--color-surface, #fff);
  border-radius: 4px;
  border-left: 2px solid var(--color-primary, #2563eb);
}

.detail-meta {
  display: flex;
  gap: 8px;
  margin-bottom: 4px;
}

.detail-section {
  font-size: 11px;
  font-weight: 600;
  color: var(--color-text-secondary, #64748b);
}

.detail-page {
  font-size: 11px;
  color: var(--color-primary, #2563eb);
}

.detail-source {
  font-size: 11px;
  color: var(--color-text-muted, #94a3b8);
}

.detail-date {
  font-size: 11px;
  color: var(--color-text-muted, #94a3b8);
}

.detail-text {
  font-size: 12px;
  line-height: 1.5;
  color: var(--color-text-primary, #334155);
  margin: 0;
  white-space: pre-wrap;
  max-height: 100px;
  overflow-y: auto;
}
</style>
