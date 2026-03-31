<script setup>
import { ref, onMounted, onBeforeUnmount } from 'vue'

const toasts = ref([])
let nextId = 0

const addToast = ({ message, type = 'info', duration = 3000 }) => {
  const id = ++nextId
  toasts.value.push({ id, message, type })
  if (duration > 0) {
    setTimeout(() => removeToast(id), duration)
  }
}

const removeToast = (id) => {
  toasts.value = toasts.value.filter(t => t.id !== id)
}

const handleGlobalToast = (e) => {
  addToast(e.detail)
}

onMounted(() => {
  window.addEventListener('app-toast', handleGlobalToast)
})

onBeforeUnmount(() => {
  window.removeEventListener('app-toast', handleGlobalToast)
})

defineExpose({ addToast })
</script>

<template>
  <Teleport to="body">
    <div class="toast-container" v-if="toasts.length">
      <TransitionGroup name="toast">
        <div
          v-for="toast in toasts"
          :key="toast.id"
          class="toast-item"
          :class="`toast-${toast.type}`"
          @click="removeToast(toast.id)"
        >
          <span class="toast-icon">
            <template v-if="toast.type === 'success'">✓</template>
            <template v-else-if="toast.type === 'error'">✕</template>
            <template v-else-if="toast.type === 'warning'">!</template>
            <template v-else>ℹ</template>
          </span>
          <span class="toast-message">{{ toast.message }}</span>
        </div>
      </TransitionGroup>
    </div>
  </Teleport>
</template>

<style scoped>
.toast-container {
  position: fixed;
  top: 64px;
  right: 20px;
  z-index: 9999;
  display: flex;
  flex-direction: column;
  gap: 8px;
  pointer-events: none;
}

.toast-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 16px;
  border-radius: var(--radius-lg, 14px);
  background: var(--color-bg-surface, #fff);
  border: 1px solid var(--color-border-light, #e5e7eb);
  box-shadow: 0 8px 24px rgba(15, 23, 42, 0.12);
  font-size: var(--text-sm, 13px);
  color: var(--color-text-body, #1f2937);
  pointer-events: auto;
  cursor: pointer;
  min-width: 200px;
  max-width: 380px;
}

.toast-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  border-radius: 50%;
  font-size: 11px;
  font-weight: 700;
  flex-shrink: 0;
}

.toast-success .toast-icon {
  background: var(--color-success-bg, rgba(5, 150, 105, 0.07));
  color: var(--color-success, #059669);
}
.toast-error .toast-icon {
  background: var(--color-danger-bg, rgba(220, 38, 38, 0.07));
  color: var(--color-danger, #dc2626);
}
.toast-warning .toast-icon {
  background: var(--color-warning-bg, rgba(217, 119, 6, 0.07));
  color: var(--color-warning, #d97706);
}
.toast-info .toast-icon {
  background: var(--color-info-bg, rgba(79, 70, 229, 0.06));
  color: var(--color-info, #4f46e5);
}

.toast-message {
  flex: 1;
  min-width: 0;
}

/* Transitions */
.toast-enter-active {
  transition: all 0.3s ease;
}
.toast-leave-active {
  transition: all 0.2s ease;
}
.toast-enter-from {
  opacity: 0;
  transform: translateX(40px);
}
.toast-leave-to {
  opacity: 0;
  transform: translateX(40px);
}
</style>
