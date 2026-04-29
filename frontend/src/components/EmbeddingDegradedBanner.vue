<script setup>
import { computed, ref } from 'vue'
import { isEmbeddingStatusDegraded } from '../composables/useEmbeddingStatus.js'

const props = defineProps({
  status: { type: Object, default: null },
  loading: { type: Boolean, default: false },
  error: { type: String, default: '' },
  compact: { type: Boolean, default: false }
})

defineEmits(['refresh'])

const dismissed = ref(sessionStorage.getItem('rag-banner-dismissed') === 'true')
function dismiss() {
  dismissed.value = true
  sessionStorage.setItem('rag-banner-dismissed', 'true')
}

const pick = (source, camelKey, pascalKey = null) => source?.[camelKey] ?? (pascalKey ? source?.[pascalKey] : undefined)
const toNumber = value => {
  const numeric = Number(value)
  return Number.isFinite(numeric) ? numeric : null
}

const shouldRender = computed(() => !dismissed.value && isEmbeddingStatusDegraded(props.status, props.error))
const modelText = computed(() => pick(props.status, 'model', 'Model') || '未识别')
const dimensionText = computed(() => pick(props.status, 'dimension', 'Dimension') ?? '—')
const embeddingCount = computed(() => toNumber(pick(props.status, 'embeddingCount', 'EmbeddingCount')) ?? 0)
const chunkCount = computed(() => toNumber(pick(props.status, 'chunkCount', 'ChunkCount')) ?? 0)
const coverageText = computed(() => {
  const coverage = toNumber(pick(props.status, 'coverage', 'Coverage'))
  if (coverage === null) return '—'
  return `${Math.max(0, Math.min(100, coverage * 100)).toFixed(1)}%`
})
const chunkSummary = computed(() => `${embeddingCount.value} / ${chunkCount.value} chunks (${coverageText.value})`)
</script>

<template>
  <section
    v-if="shouldRender"
    class="embedding-degraded-banner"
    :class="{ 'embedding-degraded-banner--compact': compact }"
    role="alert"
  >
    <div class="embedding-degraded-banner__body">
      <strong class="embedding-degraded-banner__title">RAG 检索能力已降级</strong>
      <p class="embedding-degraded-banner__message">
        Embedding 不可用或覆盖率不足，AI 分析和财报问答可能无法读取完整证据。
      </p>
      <p v-if="error" class="embedding-degraded-banner__error">{{ error }}</p>
      <dl class="embedding-degraded-banner__meta">
        <div>
          <dt>模型</dt>
          <dd>{{ modelText }}</dd>
        </div>
        <div>
          <dt>维度</dt>
          <dd>{{ dimensionText }}</dd>
        </div>
        <div>
          <dt>覆盖率</dt>
          <dd>{{ chunkSummary }}</dd>
        </div>
      </dl>
    </div>
    <button
      type="button"
      class="embedding-degraded-banner__refresh"
      :disabled="loading"
      @click="$emit('refresh')"
    >{{ loading ? '刷新中' : '刷新状态' }}</button>
    <button
      type="button"
      class="embedding-degraded-banner__dismiss"
      @click="dismiss"
      title="关闭提示（刷新页面后重新显示）"
    >×</button>
  </section>
</template>

<style scoped>
.embedding-degraded-banner {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: var(--space-3);
  padding: var(--space-2) var(--space-3);
  border: 1px solid #d97706;
  border-left-width: 4px;
  border-radius: 6px;
  background: #fefce8;
  color: #78350f;
  box-shadow: 0 2px 6px rgba(180, 83, 9, 0.06);
}

.embedding-degraded-banner--compact {
  padding: var(--space-2) var(--space-3);
  gap: var(--space-2);
}

.embedding-degraded-banner__body {
  display: grid;
  gap: var(--space-2);
  min-width: 0;
}

.embedding-degraded-banner__title {
  font-size: var(--text-md);
  font-weight: 800;
  color: #78350f;
}

.embedding-degraded-banner__message,
.embedding-degraded-banner__error {
  margin: 0;
  font-size: var(--text-sm);
  line-height: 1.5;
}

.embedding-degraded-banner__error {
  font-weight: 700;
  color: #92400e;
}

.embedding-degraded-banner__meta {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2) var(--space-4);
  margin: 0;
  font-size: var(--text-sm);
}

.embedding-degraded-banner__meta div {
  display: flex;
  gap: var(--space-1);
  min-width: 0;
}

.embedding-degraded-banner__meta dt {
  font-weight: 700;
}

.embedding-degraded-banner__meta dd {
  margin: 0;
  color: #451a03;
  word-break: break-word;
}

.embedding-degraded-banner__refresh {
  flex-shrink: 0;
  border: 1px solid #92400e;
  border-radius: 6px;
  background: #92400e;
  color: #fff7ed;
  font-weight: 700;
  padding: var(--space-2) var(--space-3);
  cursor: pointer;
}

.embedding-degraded-banner__refresh:disabled {
  opacity: 0.65;
  cursor: not-allowed;
}

.embedding-degraded-banner__dismiss {
  flex-shrink: 0;
  border: none;
  background: transparent;
  color: #92400e;
  font-size: 18px;
  line-height: 1;
  cursor: pointer;
  padding: 2px 6px;
  border-radius: 4px;
  opacity: 0.6;
}
.embedding-degraded-banner__dismiss:hover {
  opacity: 1;
  background: rgba(146, 64, 14, 0.08);
}

@media (max-width: 720px) {
  .embedding-degraded-banner {
    flex-direction: column;
  }

  .embedding-degraded-banner__refresh {
    align-self: flex-start;
  }
}
</style>