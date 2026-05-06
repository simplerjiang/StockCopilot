<script setup>
import { computed, ref } from 'vue'
import { isEmbeddingStatusDegraded } from '../composables/useEmbeddingStatus.js'

const props = defineProps({
  status: { type: Object, default: null },
  loading: { type: Boolean, default: false },
  error: { type: String, default: '' },
  compact: { type: Boolean, default: false }
})

const emit = defineEmits(['refresh'])

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
const coverageRaw = computed(() => toNumber(pick(props.status, 'coverage', 'Coverage')))
const coverageText = computed(() => {
  if (coverageRaw.value === null) return '—'
  return `${Math.max(0, Math.min(100, coverageRaw.value * 100)).toFixed(1)}%`
})
const chunkSummary = computed(() => `${embeddingCount.value} / ${chunkCount.value} chunks (${coverageText.value})`)

const embedderAvailable = computed(() => {
  const val = pick(props.status, 'available', 'Available')
  return val === true
})

// Degradation cause: 'ollama' | 'zero' | 'low' | 'unknown'
const degradationCause = computed(() => {
  if (props.error) return 'unknown'
  if (!props.status) return 'unknown'
  if (!embedderAvailable.value) return 'ollama'
  if (embeddingCount.value === 0 && chunkCount.value > 0) return 'zero'
  if (coverageRaw.value !== null && coverageRaw.value < 0.5) return 'low'
  return 'unknown'
})

const bannerTitle = computed(() => {
  switch (degradationCause.value) {
    case 'ollama': return '向量模型未就绪'
    case 'zero': return '尚无向量数据'
    case 'low': return '向量覆盖不足'
    default: return 'RAG 检索能力已降级'
  }
})

const bannerMessage = computed(() => {
  switch (degradationCause.value) {
    case 'ollama': return 'Ollama 未运行或 bge-m3 模型未安装'
    case 'zero': return '点击补建开始构建检索索引'
    case 'low': return `当前覆盖率 ${coverageText.value} — AI 分析引用质量可能受影响`
    default: return 'Embedding 不可用或覆盖率不足，AI 分析和财报问答可能无法读取完整证据。'
  }
})

const showBackfillButton = computed(() => embedderAvailable.value && degradationCause.value !== 'unknown')

const backfilling = ref(false)
const backfillResult = ref(null)

const startingOllama = ref(false)
const ollamaStartResult = ref(null)

async function startOllama() {
  startingOllama.value = true
  ollamaStartResult.value = null
  try {
    const res = await fetch('/api/admin/ollama/start', { method: 'POST' })
    const data = await res.json()
    if (data.success) {
      ollamaStartResult.value = { type: 'success', text: 'Ollama 已启动，正在刷新状态...' }
      setTimeout(() => emit('refresh'), 3000)
    } else {
      ollamaStartResult.value = { type: 'error', text: data.message || '启动失败' }
    }
  } catch {
    ollamaStartResult.value = { type: 'error', text: '无法连接后端' }
  } finally {
    startingOllama.value = false
  }
}

async function triggerBackfill() {
  backfilling.value = true
  backfillResult.value = null
  try {
    const res = await fetch('/api/stocks/financial/embedding/backfill', { method: 'POST' })
    if (!res.ok) {
      backfillResult.value = { type: 'error', text: `补建失败 (${res.status})` }
      return
    }

    const reader = res.body.getReader()
    const decoder = new TextDecoder()
    let buffer = ''
    let lastProgress = null

    while (true) {
      const { done, value } = await reader.read()
      if (done) break
      buffer += decoder.decode(value, { stream: true })

      const lines = buffer.split('\n')
      buffer = lines.pop() // keep incomplete line in buffer

      for (const line of lines) {
        if (!line.trim()) continue
        try {
          const data = JSON.parse(line)
          lastProgress = data
          backfillResult.value = { type: 'progress', text: `补建中... ${data.filled}/${data.total || '?'}` }
        } catch { /* ignore */ }
      }
    }

    if (lastProgress?.done) {
      if (lastProgress.aborted) {
        backfillResult.value = { type: 'error', text: `补建中断：Ollama 可能离线，已处理 ${lastProgress.filled} 条，${lastProgress.errors} 个错误` }
      } else if (lastProgress.errors > 0) {
        backfillResult.value = { type: 'success', text: `补建完成：已处理 ${lastProgress.filled} 条（${lastProgress.errors} 个跳过）` }
      } else {
        backfillResult.value = { type: 'success', text: `补建完成：已处理 ${lastProgress.filled} 条` }
      }
      setTimeout(() => emit('refresh'), 2000)
    } else {
      backfillResult.value = { type: 'success', text: '补建任务已完成' }
      setTimeout(() => emit('refresh'), 2000)
    }
  } catch (e) {
    if (e.name !== 'AbortError') {
      backfillResult.value = { type: 'error', text: `补建异常: ${e.message}` }
    }
  } finally {
    backfilling.value = false
  }
}
</script>

<template>
  <section
    v-if="shouldRender"
    class="embedding-degraded-banner"
    :class="{ 'embedding-degraded-banner--compact': compact }"
    role="alert"
  >
    <div class="embedding-degraded-banner__body">
      <strong class="embedding-degraded-banner__title">{{ bannerTitle }}</strong>
      <p class="embedding-degraded-banner__message">{{ bannerMessage }}</p>
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
      <p v-if="backfillResult" class="embedding-degraded-banner__backfill-result" :class="'embedding-degraded-banner__backfill-result--' + backfillResult.type">
        {{ backfillResult.text }}
      </p>
      <p v-if="ollamaStartResult" class="embedding-degraded-banner__backfill-result" :class="'embedding-degraded-banner__backfill-result--' + ollamaStartResult.type">
        {{ ollamaStartResult.text }}
      </p>
    </div>
    <div class="embedding-degraded-banner__actions">
      <button
        v-if="degradationCause === 'ollama'"
        type="button"
        class="embedding-degraded-banner__backfill"
        :disabled="startingOllama"
        @click="startOllama"
      >{{ startingOllama ? '启动中...' : '启动 Ollama' }}</button>
      <button
        v-if="showBackfillButton"
        type="button"
        class="embedding-degraded-banner__backfill"
        :disabled="backfilling"
        @click="triggerBackfill"
      >{{ backfilling ? '补建中...' : (degradationCause === 'zero' ? '开始补建' : '补建向量') }}</button>
      <button
        type="button"
        class="embedding-degraded-banner__refresh"
        :disabled="loading"
        @click="$emit('refresh')"
      >{{ loading ? '刷新中' : '刷新状态' }}</button>
    </div>
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

.embedding-degraded-banner__actions {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  flex-shrink: 0;
}

.embedding-degraded-banner__backfill {
  border: 1px solid #166534;
  border-radius: 6px;
  background: #166534;
  color: #f0fdf4;
  font-weight: 700;
  padding: var(--space-2) var(--space-3);
  cursor: pointer;
  white-space: nowrap;
}

.embedding-degraded-banner__backfill:disabled {
  opacity: 0.65;
  cursor: not-allowed;
}

.embedding-degraded-banner__backfill-result {
  margin: 0;
  font-size: var(--text-sm);
  font-weight: 600;
}

.embedding-degraded-banner__backfill-result--success {
  color: #166534;
}

.embedding-degraded-banner__backfill-result--error {
  color: #991b1b;
}

.embedding-degraded-banner__backfill-result--progress {
  color: #1d4ed8;
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