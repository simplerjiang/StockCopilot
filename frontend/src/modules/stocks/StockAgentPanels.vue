<script setup>
import { computed, ref } from 'vue'
import StockAgentCard from './StockAgentCard.vue'

const props = defineProps({
  agents: { type: Array, default: () => [] },
  loading: { type: Boolean, default: false },
  error: { type: String, default: '' },
  lastUpdated: { type: String, default: '' },
  historyOptions: { type: Array, default: () => [] },
  selectedHistoryId: { type: [String, Number], default: '' },
  historyLoading: { type: Boolean, default: false },
  historyError: { type: String, default: '' }
})

const emit = defineEmits(['run', 'select-history', 'draft-plan'])
const rawVisible = ref({})

const getAgentId = agent => agent?.agentId ?? agent?.AgentId ?? ''

const orderedAgents = computed(() => {
  const order = ['commander', 'stock_news', 'sector_news', 'financial_analysis', 'trend_analysis']
  const list = Array.isArray(props.agents) ? props.agents : []
  return order
    .map(id => list.find(agent => getAgentId(agent) === id) || { agentId: id, agentName: id })
})

const toggleRaw = id => {
  rawVisible.value = { ...rawVisible.value, [id]: !rawVisible.value[id] }
}
</script>

<template>
  <section class="agent-panel">
    <div class="agent-panel-header">
      <div>
        <h3>多Agent分析</h3>
        <p class="muted">选择股票后点击按钮，自动汇总5个Agent的输出。</p>
      </div>
      <div class="agent-panel-actions">
        <div class="history-select">
          <label class="muted">历史记录</label>
          <select
            :value="selectedHistoryId"
            :disabled="historyLoading || !historyOptions.length"
            @change="emit('select-history', $event.target.value)"
          >
            <option value="">最新分析</option>
            <option v-for="item in historyOptions" :key="item.value" :value="item.value">
              {{ item.label }}
            </option>
          </select>
        </div>
        <button class="run-standard-button" @click="emit('run', false)" :disabled="loading">
          {{ loading ? '分析中...' : '启动多Agent' }}
        </button>
        <button class="run-pro-button" @click="emit('run', true)" :disabled="loading">
          {{ loading ? '分析中...' : 'Pro 深度分析' }}
        </button>
        <span v-if="lastUpdated" class="muted">更新时间：{{ lastUpdated }}</span>
      </div>
    </div>

    <p v-if="error" class="muted error">{{ error }}</p>
    <p v-if="historyError" class="muted error">{{ historyError }}</p>

    <div class="agent-grid">
      <StockAgentCard
        v-for="agent in orderedAgents"
        :key="getAgentId(agent)"
        :agent="agent"
        :raw-visible="!!rawVisible[getAgentId(agent)]"
        @toggle-raw="toggleRaw(getAgentId(agent))"
        @draft-plan="emit('draft-plan')"
      />
    </div>
  </section>
</template>

<style scoped>
.agent-panel {
  margin-top: 1.5rem;
  padding: 1.25rem;
  border-radius: 16px;
  border: 1px solid rgba(148, 163, 184, 0.25);
  background: rgba(248, 250, 252, 0.9);
}

.agent-panel-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
}

.agent-panel-actions {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 0.35rem;
}

.history-select {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.history-select select {
  border-radius: 10px;
  border: 1px solid rgba(148, 163, 184, 0.4);
  padding: 0.35rem 0.6rem;
  background: rgba(255, 255, 255, 0.9);
}

.agent-panel-actions button {
  border-radius: 999px;
  border: none;
  padding: 0.45rem 1rem;
  background: linear-gradient(135deg, #1d4ed8, #38bdf8);
  color: #ffffff;
  cursor: pointer;
}

.agent-panel-actions button:disabled {
  background: #94a3b8;
  cursor: not-allowed;
}

.draft-plan-button {
  border-radius: 12px;
  border: 1px solid rgba(14, 165, 233, 0.28);
  padding: 0.6rem 0.85rem;
  background: linear-gradient(135deg, rgba(12, 74, 110, 0.96), rgba(8, 145, 178, 0.92));
  color: #f8fafc;
  font-weight: 600;
  cursor: pointer;
}

.agent-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
  gap: 1rem;
}

.muted {
  color: #94a3b8;
  font-size: 0.85rem;
}

.error {
  color: #ef4444;
}

.empty {
  text-align: center;
}
</style>
