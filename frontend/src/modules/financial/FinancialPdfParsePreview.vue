<script setup>
import { computed } from 'vue'
import EmptyState from '../../components/EmptyState.vue'
import ErrorState from '../../components/ErrorState.vue'
import LoadingState from '../../components/LoadingState.vue'
import { getBlockKindMeta } from './blockKindTag.js'

/**
 * V041-S4: FinancialPdfParsePreview
 *
 * 展示 PdfFileDetail.parseUnits 的可视化分组预览。
 * 不直接调用 API，仅消费父组件传入的 parseUnits/loading/error。
 *
 * 事件：
 *   jump-to-page(pageStart) — 用户点击页码角标时触发，供 ComparePane（S5）联动 PdfViewer。
 */

const props = defineProps({
  parseUnits: { type: Array, default: () => [] },
  loading: { type: Boolean, default: false },
  error: { type: String, default: null }
})

const emit = defineEmits(['jump-to-page', 'retry'])

const SNIPPET_MAX = 120

// 分组渲染顺序：table → narrative_section → figure_caption → unknown
const GROUP_ORDER = ['table', 'narrative_section', 'figure_caption', 'unknown']

const groups = computed(() => {
  const buckets = new Map()
  for (const key of GROUP_ORDER) buckets.set(key, [])

  const list = Array.isArray(props.parseUnits) ? props.parseUnits : []
  for (const unit of list) {
    const meta = getBlockKindMeta(unit?.blockKind)
    const bucketKey = buckets.has(meta.key) ? meta.key : 'unknown'
    buckets.get(bucketKey).push({ unit, meta })
  }

  return GROUP_ORDER
    .map(key => ({
      key,
      meta: getBlockKindMeta(key),
      items: buckets.get(key) || []
    }))
    .filter(group => group.items.length > 0)
})

const isEmpty = computed(() => {
  if (props.loading || props.error) return false
  const list = Array.isArray(props.parseUnits) ? props.parseUnits : []
  return list.length === 0
})

const formatPageRange = (unit) => {
  const start = Number(unit?.pageStart)
  const end = Number(unit?.pageEnd)
  if (!Number.isFinite(start)) return ''
  if (!Number.isFinite(end) || end === start) return `P${start}`
  return `P${start}-P${end}`
}

const truncateSnippet = (snippet) => {
  if (snippet == null) return ''
  const text = String(snippet)
  if (text.length <= SNIPPET_MAX) return text
  return `${text.slice(0, SNIPPET_MAX)}…`
}

const onPageClick = (unit) => {
  const start = Number(unit?.pageStart)
  if (!Number.isFinite(start)) return
  emit('jump-to-page', start)
}

const tagStyle = (meta) => {
  if (!meta) return ''
  return `color:${meta.color};background:${meta.bg};border:1px solid ${meta.border}`
}
</script>

<template>
  <div class="fc-pdf-parse-preview" data-testid="fc-pdf-parse-preview">
    <LoadingState
      v-if="loading"
      message="正在加载解析结果…"
      compact
      data-testid="fc-pdf-parse-loading"
    />

    <ErrorState
      v-else-if="error"
      title="解析结果加载失败"
      :description="error"
      data-testid="fc-pdf-parse-error"
      @retry="$emit('retry')"
    />

    <EmptyState
      v-else-if="isEmpty"
      icon="📄"
      title="暂无解析结果"
      description="该 PDF 尚未生成 ParseUnits，可尝试重新解析"
      compact
      data-testid="fc-pdf-parse-empty"
    />

    <div v-else class="fc-pdf-parse-groups">
      <section
        v-for="group in groups"
        :key="group.key"
        class="fc-pdf-parse-group"
        :data-group="group.key"
      >
        <header class="fc-pdf-parse-group-header">
          <span class="fc-pdf-parse-tag" :style="tagStyle(group.meta)">
            {{ group.meta.label }}
          </span>
          <span class="fc-pdf-parse-group-count" data-testid="fc-pdf-parse-group-count">
            共 {{ group.items.length }} 条
          </span>
        </header>

        <ul class="fc-pdf-parse-list">
          <li
            v-for="(entry, idx) in group.items"
            :key="`${group.key}-${idx}`"
            class="fc-pdf-parse-item"
          >
            <div class="fc-pdf-parse-item-meta">
              <span class="fc-pdf-parse-tag" :style="tagStyle(entry.meta)">
                {{ entry.meta.label }}
              </span>
              <button
                type="button"
                class="fc-pdf-parse-page"
                data-testid="fc-pdf-parse-page-btn"
                @click="onPageClick(entry.unit)"
              >
                {{ formatPageRange(entry.unit) }}
              </button>
              <span v-if="entry.unit?.sectionName" class="fc-pdf-parse-section">
                {{ entry.unit.sectionName }}
              </span>
              <span class="fc-pdf-parse-fields">字段 {{ entry.unit?.fieldCount ?? 0 }}</span>
            </div>
            <p
              v-if="entry.unit?.snippet"
              class="fc-pdf-parse-snippet"
              :title="entry.unit.snippet"
            >
              {{ truncateSnippet(entry.unit.snippet) }}
            </p>
          </li>
        </ul>
      </section>
    </div>
  </div>
</template>

<style scoped>
.fc-pdf-parse-preview {
  display: flex;
  flex-direction: column;
  gap: var(--space-3, 12px);
}
.fc-pdf-parse-groups {
  display: flex;
  flex-direction: column;
  gap: var(--space-4, 16px);
}
.fc-pdf-parse-group {
  border: 1px solid var(--color-border, #e5e7eb);
  border-radius: 8px;
  background: var(--color-surface, #fff);
  padding: 12px;
}
.fc-pdf-parse-group-header {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
}
.fc-pdf-parse-group-count {
  font-size: 12px;
  color: var(--color-text-muted, #6b7280);
}
.fc-pdf-parse-tag {
  display: inline-flex;
  align-items: center;
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 12px;
  line-height: 1.4;
}
.fc-pdf-parse-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.fc-pdf-parse-item {
  border: 1px solid var(--color-border-subtle, #f3f4f6);
  border-radius: 6px;
  padding: 8px 10px;
  background: var(--color-surface-alt, #fafafa);
}
.fc-pdf-parse-item-meta {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
.fc-pdf-parse-page {
  display: inline-flex;
  align-items: center;
  padding: 2px 8px;
  border: 1px solid var(--color-border, #e5e7eb);
  border-radius: 4px;
  background: #fff;
  font-size: 12px;
  font-family: var(--font-mono, ui-monospace, monospace);
  cursor: pointer;
}
.fc-pdf-parse-page:hover {
  background: var(--color-hover, #f3f4f6);
}
.fc-pdf-parse-section {
  font-size: 12px;
  color: var(--color-text, #374151);
}
.fc-pdf-parse-fields {
  font-size: 12px;
  color: var(--color-text-muted, #6b7280);
}
.fc-pdf-parse-snippet {
  margin: 6px 0 0;
  font-size: 12px;
  color: var(--color-text-muted, #4b5563);
  line-height: 1.5;
  word-break: break-all;
}
</style>
