<script setup>
import { computed, onBeforeUnmount, ref, watch } from 'vue'

/**
 * FinancialPdfViewer
 *
 * 共享 PDF 内嵌预览组件。优先使用浏览器原生 <iframe> 渲染 PDF，
 * 桌面（Electron）与现代浏览器（Chromium / Edge / Firefox）均原生支持
 * application/pdf；不引入 pdfjs-dist 等重型依赖。
 *
 * 失败兜底：
 *  - src 为空 → 占位提示
 *  - 加载超时 / iframe 报错 → 显示「无法预览，下载原件」按钮
 *
 * 事件：
 *  - load：iframe load 事件触发
 *  - error：超时或 iframe error 事件触发
 *  - pageChange：当 page prop 变化时透传（无 PDF.js 时浏览器原生 iframe 无法
 *    监听内部页码切换，此事件作为受控页码的语义出口）
 */

const props = defineProps({
  src: { type: String, default: '' },
  title: { type: String, default: 'PDF 预览' },
  page: { type: [Number, String], default: null },
  loadTimeoutMs: { type: Number, default: 15000 }
})

const emit = defineEmits(['load', 'error', 'pageChange'])

const loaded = ref(false)
const errored = ref(false)
let timeoutHandle = null

const clearTimer = () => {
  if (timeoutHandle !== null) {
    clearTimeout(timeoutHandle)
    timeoutHandle = null
  }
}

const startTimer = () => {
  clearTimer()
  if (!props.src) return
  if (!props.loadTimeoutMs || props.loadTimeoutMs <= 0) return
  timeoutHandle = setTimeout(() => {
    if (!loaded.value) {
      errored.value = true
      emit('error', new Error('PDF 加载超时'))
    }
  }, props.loadTimeoutMs)
}

const resolvedSrc = computed(() => {
  if (!props.src) return ''
  const pageNum = Number(props.page)
  if (!Number.isFinite(pageNum) || pageNum <= 0) {
    return props.src
  }
  // PDF Open Parameters: #page=N
  const sep = props.src.includes('#') ? '&' : '#'
  return `${props.src}${sep}page=${Math.floor(pageNum)}`
})

const onIframeLoad = () => {
  loaded.value = true
  errored.value = false
  clearTimer()
  emit('load')
}

const onIframeError = () => {
  loaded.value = false
  errored.value = true
  clearTimer()
  emit('error', new Error('PDF 加载失败'))
}

const retry = () => {
  errored.value = false
  loaded.value = false
  startTimer()
}

watch(
  () => props.src,
  () => {
    loaded.value = false
    errored.value = false
    startTimer()
  },
  { immediate: true }
)

watch(
  () => props.page,
  (next, prev) => {
    if (next !== prev) {
      emit('pageChange', next)
    }
  }
)

onBeforeUnmount(() => {
  clearTimer()
})
</script>

<template>
  <div class="fc-pdf-viewer" :aria-label="title">
    <div v-if="!src" class="fc-pdf-placeholder" data-testid="fc-pdf-empty">
      <p>暂无 PDF 可预览</p>
    </div>

    <div v-else-if="errored" class="fc-pdf-error" role="alert" data-testid="fc-pdf-error">
      <p class="fc-pdf-error-msg">无法预览，请尝试下载原件</p>
      <div class="fc-pdf-error-actions">
        <a
          class="fc-pdf-btn fc-pdf-btn--primary"
          :href="resolvedSrc"
          target="_blank"
          rel="noopener noreferrer"
          download
        >下载原件</a>
        <button type="button" class="fc-pdf-btn" @click="retry">重试</button>
      </div>
    </div>

    <template v-else>
      <div v-if="!loaded" class="fc-pdf-loading" data-testid="fc-pdf-loading">
        <p>正在加载 PDF...</p>
      </div>
      <iframe
        class="fc-pdf-frame"
        :class="{ 'fc-pdf-frame--hidden': !loaded }"
        :src="resolvedSrc"
        :title="title"
        type="application/pdf"
        data-testid="fc-pdf-iframe"
        @load="onIframeLoad"
        @error="onIframeError"
      />
    </template>
  </div>
</template>

<style scoped>
.fc-pdf-viewer {
  position: relative;
  width: 100%;
  height: 100%;
  min-height: 320px;
  display: flex;
  flex-direction: column;
  background: var(--color-bg-elevated, #fff);
  border: 1px solid var(--color-border, #e4e7eb);
  border-radius: 6px;
  overflow: hidden;
}

.fc-pdf-frame {
  flex: 1;
  width: 100%;
  height: 100%;
  border: 0;
  background: #f5f5f5;
}

.fc-pdf-frame--hidden {
  visibility: hidden;
  position: absolute;
  inset: 0;
}

.fc-pdf-placeholder,
.fc-pdf-loading,
.fc-pdf-error {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 12px;
  padding: 24px;
  color: var(--color-text-secondary, #6b7280);
  font-size: 14px;
  text-align: center;
}

.fc-pdf-error-msg {
  margin: 0;
  color: var(--color-text-error, #b91c1c);
}

.fc-pdf-error-actions {
  display: flex;
  gap: 8px;
}

.fc-pdf-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 6px 14px;
  border: 1px solid var(--color-border, #d1d5db);
  border-radius: 4px;
  background: var(--color-bg-elevated, #fff);
  color: var(--color-text-primary, #111827);
  font-size: 13px;
  cursor: pointer;
  text-decoration: none;
  transition: background var(--transition-fast, 120ms);
}

.fc-pdf-btn:hover {
  background: var(--color-bg-hover, #f3f4f6);
}

.fc-pdf-btn--primary {
  background: var(--color-primary, #2563eb);
  border-color: var(--color-primary, #2563eb);
  color: #fff;
}

.fc-pdf-btn--primary:hover {
  background: var(--color-primary-hover, #1d4ed8);
}
</style>
