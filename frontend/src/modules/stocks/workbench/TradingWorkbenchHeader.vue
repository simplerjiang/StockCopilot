<script setup>
defineProps({
  session: { type: Object, default: null },
  activeTurn: { type: Object, default: null },
  sessionStatus: { type: Object, default: () => ({ label: '空闲', cls: 'status-idle' }) },
  currentStage: { type: String, default: null },
  isRunning: { type: Boolean, default: false },
  error: { type: String, default: null }
})
defineEmits(['refresh'])
</script>

<template>
  <div class="wb-header">
    <div class="wb-header-top">
      <div class="wb-header-left">
        <span v-if="session" class="wb-session-badge" :title="`Session #${session.id}`">
          S{{ session.id }}
        </span>
        <span v-if="activeTurn" class="wb-turn-badge" :title="`Turn #${activeTurn.id}`">
          T{{ activeTurn.turnIndex ?? 0 }}
        </span>
        <span :class="['wb-status', sessionStatus.cls]">
          <span v-if="isRunning" class="pulse-dot" />
          {{ sessionStatus.label }}
        </span>
      </div>
      <button class="wb-refresh-btn" title="刷新" @click="$emit('refresh')">↻</button>
    </div>

    <div v-if="currentStage" class="wb-stage-indicator">
      <span class="wb-stage-label">当前阶段:</span>
      <span class="wb-stage-name">{{ currentStage }}</span>
    </div>

    <div v-if="error" class="wb-error">
      <span class="wb-error-icon">⚠️</span>
      <span class="wb-error-text">{{ error }}</span>
    </div>
  </div>
</template>

<style scoped>
.wb-header {
  padding: 10px 12px 8px;
  background: var(--wb-header-bg, #1e2128);
  border-bottom: 1px solid var(--wb-border, #2a2d35);
}
.wb-header-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 6px;
}
.wb-header-left {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}

.wb-session-badge, .wb-turn-badge {
  font-size: 10px;
  font-weight: 600;
  padding: 2px 6px;
  border-radius: 4px;
  font-family: 'Consolas', monospace;
}
.wb-session-badge {
  background: var(--wb-badge-session, #2a3a5a);
  color: var(--wb-accent, #5b9cf6);
}
.wb-turn-badge {
  background: var(--wb-badge-turn, #3a2a5a);
  color: #b09cf6;
}

.wb-status {
  font-size: 11px;
  font-weight: 500;
  display: flex;
  align-items: center;
  gap: 4px;
}
.status-idle { color: #8b8fa3; }
.status-queued { color: #f0b429; }
.status-running { color: #4fc3f7; }
.status-completed { color: #66bb6a; }
.status-failed { color: #ef5350; }
.status-cancelled { color: #bdbdbd; }

.pulse-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: currentColor;
  animation: pulse 1.4s infinite ease-in-out;
}
@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.3; }
}

.wb-stage-indicator {
  margin-top: 6px;
  font-size: 11px;
  color: var(--wb-text-muted, #8b8fa3);
}
.wb-stage-label { margin-right: 4px; }
.wb-stage-name {
  color: var(--wb-accent, #5b9cf6);
  font-weight: 500;
}

.wb-refresh-btn {
  background: transparent;
  border: 1px solid var(--wb-border, #2a2d35);
  color: var(--wb-text-muted, #8b8fa3);
  border-radius: 4px;
  padding: 2px 8px;
  cursor: pointer;
  font-size: 14px;
}
.wb-refresh-btn:hover { color: var(--wb-text, #e1e4ea); }

.wb-error {
  margin-top: 6px;
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: 11px;
  color: #ef5350;
  background: rgba(239, 83, 80, 0.1);
  padding: 4px 8px;
  border-radius: 4px;
}
.wb-error-icon { font-size: 12px; }
.wb-error-text { line-height: 1.3; }
</style>
