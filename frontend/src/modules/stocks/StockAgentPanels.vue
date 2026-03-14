<script setup>
import { computed, ref } from 'vue'
import StockAgentChart from './StockAgentChart.vue'
import { buildMetricRows, formatMetricLabel, formatMetricValue } from './agentFormat'

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
const getAgentName = agent => agent?.agentName ?? agent?.AgentName ?? ''
const getAgentSuccess = agent => agent?.success ?? agent?.Success ?? false
const getAgentError = agent => agent?.error ?? agent?.Error ?? ''
const getAgentData = agent => agent?.data ?? agent?.Data ?? null
const getAgentRaw = agent => agent?.rawContent ?? agent?.RawContent ?? ''

const orderedAgents = computed(() => {
  const order = ['commander', 'stock_news', 'sector_news', 'financial_analysis', 'trend_analysis']
  const list = Array.isArray(props.agents) ? props.agents : []
  return order
    .map(id => list.find(agent => getAgentId(agent) === id) || { agentId: id, agentName: id })
})

const toggleRaw = id => {
  rawVisible.value = { ...rawVisible.value, [id]: !rawVisible.value[id] }
}

const buildListSections = data => {
  if (!data) return []
  const configs = [
    { key: 'evidence', title: '证据来源', columns: ['point', 'source', 'publishedAt'] },
    { key: 'events', title: '资讯列表', columns: ['title', 'category', 'publishedAt', 'source', 'impact'] },
    { key: 'topMovers', title: '板块龙头', columns: ['symbol', 'name', 'changePercent', 'reason'] },
    { key: 'forecast', title: '价格预测', columns: ['label', 'price', 'confidence'] },
    { key: 'timeframeSignals', title: '周期信号', columns: ['timeframe', 'trend', 'confidence'] }
  ]
  return configs
    .map(config => {
      const rows = Array.isArray(data[config.key]) ? data[config.key] : []
      return rows.length ? { ...config, rows } : null
    })
    .filter(Boolean)
}

const buildMetrics = data => {
  if (!data) return []
  return buildMetricRows(data.metrics, data.sentiment)
}

const getConfidence = data => {
  if (!data || typeof data !== 'object') return null
  if (data.confidence_score != null) {
    return data.confidence_score
  }
  return data.confidence ?? null
}

const getNarrativeRows = data => {
  if (!data || typeof data !== 'object') return []
  const keys = ['analysis_opinion', 'trigger_conditions', 'invalid_conditions', 'risk_warning']
  return keys
    .filter(key => data[key] !== undefined && data[key] !== null && data[key] !== '')
    .map(key => ({ key, value: data[key] }))
}

const buildTagList = (data, key) => {
  if (!data || typeof data !== 'object') return []
  return Array.isArray(data[key]) ? data[key] : []
}

const parsePublishedAt = value => {
  if (!value) return null
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? null : date
}

const isEvidencePublishedAtColumn = (section, col) =>
  (section?.key === 'evidence' || section?.key === 'events') && col === 'publishedAt'

const isExpiredPublishedAt = value => {
  const publishedAt = parsePublishedAt(value)
  if (!publishedAt) return true
  const ageHours = (Date.now() - publishedAt.getTime()) / 36e5
  return ageHours > 72
}

const getCellClass = (section, row, col) => {
  if (!isEvidencePublishedAtColumn(section, col)) return ''
  return isExpiredPublishedAt(row?.[col]) ? 'cell-expired' : 'cell-fresh'
}

const getCellText = (section, row, col) => {
  const value = formatMetricValue(row?.[col], col)
  if (!isEvidencePublishedAtColumn(section, col)) return value
  return isExpiredPublishedAt(row?.[col]) ? `${value || '未知'}（过期风险）` : value
}

const canDraftPlan = agent => getAgentId(agent) === 'commander' && getAgentSuccess(agent) && !!getAgentData(agent)
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
      <article v-for="agent in orderedAgents" :key="getAgentId(agent)" class="agent-card">
        <header>
          <div>
            <h4>{{ getAgentName(agent) || getAgentId(agent) }}</h4>
            <p v-if="!getAgentSuccess(agent) && getAgentError(agent)" class="muted error">
              {{ getAgentError(agent) }}
            </p>
          </div>
          <div class="header-actions">
            <span v-if="getAgentData(agent) && getConfidence(getAgentData(agent)) != null" class="confidence-badge">
              置信度 {{ formatMetricValue(getConfidence(getAgentData(agent)), 'confidence') }}
            </span>
            <button class="raw-toggle" @click="toggleRaw(getAgentId(agent))">JSON</button>
          </div>
        </header>

        <div v-if="getAgentData(agent)" class="agent-content">
          <p class="summary">{{ getAgentData(agent).summary }}</p>

          <div v-if="getNarrativeRows(getAgentData(agent)).length" class="recommendation-block">
            <h5>核心判断</h5>
            <div class="metrics">
              <div v-for="row in getNarrativeRows(getAgentData(agent))" :key="`rec-${row.key}`" class="metric-row narrative-row">
                <span class="metric-key">{{ formatMetricLabel(row.key) }}</span>
                <span class="metric-value">{{ formatMetricValue(row.value, row.key) }}</span>
              </div>
            </div>
          </div>

          <div v-if="buildMetrics(getAgentData(agent)).length" class="metrics">
            <div v-for="row in buildMetrics(getAgentData(agent))" :key="row.key" class="metric-row">
              <span class="metric-key">{{ formatMetricLabel(row.key) }}</span>
              <span class="metric-value">{{ formatMetricValue(row.value, row.key) }}</span>
            </div>
          </div>

          <StockAgentChart v-if="getAgentData(agent).chart" :chart="getAgentData(agent).chart" />

          <div v-for="section in buildListSections(getAgentData(agent))" :key="section.key" class="list-section">
            <h5>{{ section.title }}</h5>
            <table>
              <thead>
                <tr>
                  <th v-for="col in section.columns" :key="col">{{ formatMetricLabel(col) }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(row, idx) in section.rows.slice(0, 6)" :key="idx">
                  <td v-for="col in section.columns" :key="col" :class="getCellClass(section, row, col)">
                    {{ getCellText(section, row, col) }}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <div v-if="Array.isArray(getAgentData(agent).signals) && getAgentData(agent).signals.length" class="pill-list">
            <h5>信号</h5>
            <div class="pills">
              <span v-for="(item, idx) in getAgentData(agent).signals" :key="idx" class="pill">{{ item }}</span>
            </div>
          </div>

          <div v-if="buildTagList(getAgentData(agent), 'triggers').length" class="pill-list">
            <h5>触发条件</h5>
            <div class="pills">
              <span v-for="(item, idx) in buildTagList(getAgentData(agent), 'triggers')" :key="`trigger-${idx}`" class="pill trigger">{{ item }}</span>
            </div>
          </div>

          <div v-if="buildTagList(getAgentData(agent), 'invalidations').length" class="pill-list">
            <h5>失效条件</h5>
            <div class="pills">
              <span v-for="(item, idx) in buildTagList(getAgentData(agent), 'invalidations')" :key="`invalid-${idx}`" class="pill invalid">{{ item }}</span>
            </div>
          </div>

          <div v-if="buildTagList(getAgentData(agent), 'riskLimits').length" class="pill-list">
            <h5>风险上限</h5>
            <div class="pills">
              <span v-for="(item, idx) in buildTagList(getAgentData(agent), 'riskLimits')" :key="`limit-${idx}`" class="pill risk-limit">{{ item }}</span>
            </div>
          </div>

          <div v-if="Array.isArray(getAgentData(agent).risks) && getAgentData(agent).risks.length" class="pill-list">
            <h5>风险</h5>
            <div class="pills">
              <span v-for="(item, idx) in getAgentData(agent).risks" :key="idx" class="pill risk">{{ item }}</span>
            </div>
          </div>

          <button v-if="canDraftPlan(agent)" class="draft-plan-button" @click="emit('draft-plan')">
            基于此分析起草交易计划
          </button>
        </div>

        <p v-else class="muted empty">暂无数据</p>

        <pre v-if="rawVisible[getAgentId(agent)]" class="raw-json">{{ getAgentData(agent) ? JSON.stringify(getAgentData(agent), null, 2) : getAgentRaw(agent) }}</pre>
      </article>
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

.agent-card {
  background: #ffffff;
  border-radius: 14px;
  padding: 1rem;
  border: 1px solid rgba(226, 232, 240, 0.9);
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.agent-card header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 0.5rem;
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 0.4rem;
}

.confidence-badge {
  font-size: 0.72rem;
  color: #1e3a8a;
  background: rgba(59, 130, 246, 0.12);
  border-radius: 999px;
  padding: 0.2rem 0.55rem;
}

.narrative-row {
  grid-template-columns: 92px 1fr;
  align-items: flex-start;
}

.raw-toggle {
  border-radius: 999px;
  border: 1px solid rgba(148, 163, 184, 0.4);
  background: #f8fafc;
  padding: 0.2rem 0.6rem;
  cursor: pointer;
  font-size: 0.75rem;
}

.summary {
  color: #0f172a;
  font-weight: 500;
}

.recommendation-block h5 {
  margin: 0 0 0.35rem;
}

.metrics {
  display: grid;
  gap: 0.4rem;
}

.metric-row {
  display: flex;
  justify-content: space-between;
  gap: 0.5rem;
  font-size: 0.85rem;
  border-bottom: 1px dashed rgba(148, 163, 184, 0.3);
  padding-bottom: 0.35rem;
}

.metric-key {
  color: #64748b;
}

.metric-value {
  color: #0f172a;
  font-weight: 600;
}

.list-section table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.8rem;
}

.list-section th,
.list-section td {
  border-bottom: 1px solid #e2e8f0;
  padding: 0.3rem 0.25rem;
  text-align: left;
}

.list-section td.cell-fresh {
  color: #15803d;
  font-weight: 600;
}

.list-section td.cell-expired {
  color: #b45309;
  font-weight: 600;
}

.pill-list h5 {
  margin: 0 0 0.4rem;
}

.pills {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
}

.pill {
  background: rgba(34, 197, 94, 0.12);
  color: #15803d;
  padding: 0.2rem 0.6rem;
  border-radius: 999px;
  font-size: 0.75rem;
}

.pill.risk {
  background: rgba(239, 68, 68, 0.12);
  color: #b91c1c;
}

.pill.trigger {
  background: rgba(16, 185, 129, 0.12);
  color: #047857;
}

.pill.invalid {
  background: rgba(245, 158, 11, 0.16);
  color: #b45309;
}

.pill.risk-limit {
  background: rgba(14, 165, 233, 0.14);
  color: #0369a1;
}

.raw-json {
  background: #0f172a;
  color: #e2e8f0;
  font-size: 0.75rem;
  padding: 0.6rem;
  border-radius: 10px;
  overflow-x: auto;
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
