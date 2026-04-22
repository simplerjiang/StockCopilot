<script setup>
import { onBeforeUnmount, watch } from 'vue'

const props = defineProps({
  visible: { type: Boolean, default: false },
  item: { type: Object, default: null }
})

const emit = defineEmits(['close'])

const close = () => emit('close')

const onOverlayClick = (e) => {
  if (e.target === e.currentTarget) close()
}

const onKeydown = (e) => {
  if (e.key === 'Escape' && props.visible) close()
}

watch(
  () => props.visible,
  (v) => {
    if (v) {
      window.addEventListener('keydown', onKeydown)
    } else {
      window.removeEventListener('keydown', onKeydown)
    }
  },
  { immediate: true }
)

onBeforeUnmount(() => {
  window.removeEventListener('keydown', onKeydown)
})

const formatDate = (raw) => {
  if (!raw) return '—'
  const d = new Date(raw)
  if (Number.isNaN(d.getTime())) return String(raw)
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

const getField = (key, alt) => {
  if (!props.item) return ''
  return props.item[key] !== undefined ? props.item[key] : props.item[alt]
}
</script>

<template>
  <Teleport to="body">
    <div
      v-if="visible"
      class="fc-drawer-overlay"
      @click="onOverlayClick"
    >
      <aside
        class="fc-drawer"
        :class="{ 'fc-drawer--open': visible }"
        role="dialog"
        aria-modal="true"
        aria-label="财报详情"
        @click.stop
      >
        <header class="fc-drawer-header">
          <h3 class="fc-drawer-title">财报详情</h3>
          <button type="button" class="fc-drawer-close" @click="close" title="关闭">✕</button>
        </header>
        <div class="fc-drawer-body">
          <dl class="fc-drawer-list">
            <div class="fc-drawer-row">
              <dt>Symbol</dt>
              <dd class="fc-drawer-mono">{{ getField('symbol', 'Symbol') || '—' }}</dd>
            </div>
            <div class="fc-drawer-row">
              <dt>ReportDate</dt>
              <dd>{{ formatDate(getField('reportDate', 'ReportDate')) }}</dd>
            </div>
            <div class="fc-drawer-row">
              <dt>Report ID</dt>
              <dd class="fc-drawer-mono">{{ getField('id', 'Id') ?? '—' }}</dd>
            </div>
          </dl>
          <div class="fc-drawer-callout">
            💡 详情功能由 V040-S5 提供（敬请期待）
          </div>
        </div>
      </aside>
    </div>
  </Teleport>
</template>

<style scoped>
.fc-drawer-overlay {
  position: fixed;
  inset: 0;
  z-index: 500;
  background: var(--color-bg-overlay);
  display: flex;
  justify-content: flex-end;
  animation: fc-drawer-fade var(--transition-normal);
}

@keyframes fc-drawer-fade {
  from { opacity: 0; }
  to { opacity: 1; }
}

.fc-drawer {
  width: 480px;
  max-width: 100vw;
  height: 100vh;
  background: var(--color-bg-surface);
  border-left: 1px solid var(--color-border-light);
  box-shadow: var(--shadow-xl);
  transform: translateX(100%);
  transition: transform var(--transition-normal);
  display: flex;
  flex-direction: column;
}

.fc-drawer--open {
  transform: translateX(0);
}

.fc-drawer-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: var(--space-4) var(--space-5);
  border-bottom: 1px solid var(--color-border-light);
  flex-shrink: 0;
}

.fc-drawer-title {
  margin: 0;
  font-size: var(--text-lg);
  font-weight: 700;
  color: var(--color-text-primary);
}

.fc-drawer-close {
  border: none;
  background: transparent;
  color: var(--color-text-muted);
  font-size: var(--text-lg);
  cursor: pointer;
  padding: var(--space-1) var(--space-2);
  border-radius: var(--radius-sm);
  transition: background var(--transition-fast), color var(--transition-fast);
}

.fc-drawer-close:hover {
  background: var(--color-bg-surface-alt);
  color: var(--color-text-primary);
}

.fc-drawer-body {
  flex: 1;
  overflow-y: auto;
  padding: var(--space-5);
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}

.fc-drawer-list {
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}

.fc-drawer-row {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: var(--space-3);
  padding-bottom: var(--space-2);
  border-bottom: 1px dashed var(--color-border-light);
}

.fc-drawer-row dt {
  margin: 0;
  font-size: var(--text-sm);
  color: var(--color-text-secondary);
  font-weight: 600;
}

.fc-drawer-row dd {
  margin: 0;
  font-size: var(--text-base);
  color: var(--color-text-body);
  text-align: right;
  word-break: break-all;
}

.fc-drawer-mono {
  font-family: var(--font-family-mono);
  color: var(--color-accent-text);
}

.fc-drawer-callout {
  background: var(--color-accent-subtle);
  border: 1px solid var(--color-accent-border);
  border-radius: var(--radius-md);
  padding: var(--space-3) var(--space-4);
  color: var(--color-accent-text);
  font-size: var(--text-sm);
  line-height: var(--leading-relaxed);
}
</style>
