<script setup>
const props = defineProps({
  open: {
    type: Boolean,
    default: true
  },
  currentStockLabel: {
    type: String,
    default: '未选择股票'
  }
})

const emit = defineEmits(['toggle'])
</script>

<template>
  <aside class="copilot-panel" :class="{ collapsed: !open }">
    <header class="copilot-panel-header">
      <div>
        <p class="copilot-panel-label">CopilotPanel</p>
        <h3>AI 协驾侧栏</h3>
        <p class="muted">当前标的：{{ currentStockLabel }}</p>
      </div>
      <button class="copilot-toggle" @click="emit('toggle')">
        {{ open ? '隐藏 AI' : '展开 AI' }}
      </button>
    </header>

    <div v-if="open" class="copilot-panel-body">
      <slot />
    </div>
    <div v-else class="copilot-panel-collapsed">
      <p>AI 对话、事件信号和多 Agent 分析已收拢到侧栏。</p>
      <p class="muted">不需要时保持收起，主视区只保留行情终端。</p>
    </div>
  </aside>
</template>

<style scoped>
.copilot-panel {
  display: grid;
  align-content: start;
  gap: 1rem;
  height: 100%;
  min-width: 0;
  position: relative;
  z-index: 20;
  padding: 1.25rem;
  border-radius: 22px;
  border: 1px solid rgba(148, 163, 184, 0.22);
  background:
    radial-gradient(circle at top right, rgba(249, 115, 22, 0.14), transparent 32%),
    linear-gradient(180deg, rgba(255, 255, 255, 0.94), rgba(248, 250, 252, 0.92));
  box-shadow: 0 18px 45px rgba(15, 23, 42, 0.12);
}

.copilot-panel.collapsed {
  align-self: start;
}

.copilot-panel-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 0.9rem;
}

.copilot-panel-label {
  margin: 0 0 0.35rem;
  font-size: 0.72rem;
  letter-spacing: 0.18em;
  text-transform: uppercase;
  color: #ea580c;
}

.copilot-panel h3 {
  margin: 0;
  color: #0f172a;
}

.copilot-panel-body {
  display: grid;
  gap: 1rem;
}

.copilot-toggle {
  border: none;
  border-radius: 999px;
  padding: 0.55rem 0.95rem;
  background: #0f172a;
  color: #f8fafc;
}

.copilot-panel-collapsed {
  display: grid;
  gap: 0.35rem;
  padding: 0.85rem 0.95rem;
  border-radius: 16px;
  background: rgba(241, 245, 249, 0.85);
}

.copilot-panel-collapsed p {
  margin: 0;
}
</style>