<script setup>
import StockAgentChart from './StockAgentChart.vue'
import { buildMetricRows, formatMetricLabel, formatMetricValue } from './agentFormat'

const props = defineProps({
  agent: { type: Object, default: null },
  rawVisible: { type: Boolean, default: false }
})

const emit = defineEmits(['toggle-raw', 'draft-plan'])

const getAgentId = agent => agent?.agentId ?? agent?.AgentId ?? ''
const getAgentName = agent => agent?.agentName ?? agent?.AgentName ?? ''
const getAgentSuccess = agent => agent?.success ?? agent?.Success ?? false
const getAgentError = agent => agent?.error ?? agent?.Error ?? ''
const getAgentData = agent => agent?.data ?? agent?.Data ?? null
const getAgentRaw = agent => agent?.rawContent ?? agent?.RawContent ?? ''

const getObjectValue = (item, keys) => {
  for (const key of keys) {
    const value = item?.[key]
    if (value !== undefined && value !== null && value !== '') {
      return value
    }
  }
  return ''
}

const getEvidenceItems = data => {
  const list = Array.isArray(data?.evidence ?? data?.Evidence) ? (data.evidence ?? data.Evidence) : []
  return list
    .map((item, index) => {
      const title = getObjectValue(item, ['title', 'Title', 'point', 'Point', 'url', 'Url']) || `证据 ${index + 1}`
      const point = getObjectValue(item, ['point', 'Point'])
      const excerpt = getObjectValue(item, ['excerpt', 'Excerpt', 'summary', 'Summary'])
      const fallbackExcerpt = point && point !== title ? point : ''
      const source = getObjectValue(item, ['source', 'Source'])
      const publishedAt = getObjectValue(item, ['publishedAt', 'PublishedAt'])
      const ingestedAt = getObjectValue(item, ['ingestedAt', 'IngestedAt'])
      const url = getObjectValue(item, ['url', 'Url'])
      const readStatus = getObjectValue(item, ['readStatus', 'ReadStatus'])
      const readMode = getObjectValue(item, ['readMode', 'ReadMode'])
      const localFactId = getObjectValue(item, ['localFactId', 'LocalFactId'])
      const sourceRecordId = getObjectValue(item, ['sourceRecordId', 'SourceRecordId'])

      return {
        title,
        excerpt: excerpt || fallbackExcerpt,
        source,
        publishedAt,
        ingestedAt,
        url,
        readStatus,
        readMode,
        localFactId,
        sourceRecordId
      }
    })
    .filter(item => item.title || item.source || item.publishedAt || item.url)
}

const getEvidenceStatusClass = status => {
  if (status === 'full_text_read' || status === 'summary_only') return 'evidence-pill-strong'
  if (status === 'title_only') return 'evidence-pill-medium'
  return 'evidence-pill-weak'
}

const getEvidenceModeClass = mode => {
  if (mode === 'local_fact') return 'evidence-mode-local'
  if (mode === 'url_fetched') return 'evidence-mode-url'
  return 'evidence-mode-muted'
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

const getEvidencePublishedText = value => {
  const text = formatMetricValue(value, 'publishedAt')
  if (!value) return text
  return isExpiredPublishedAt(value) ? `${text || '未知'} · 超72h` : text
}

const getEvidenceRecordText = item => {
  const tokens = []
  if (item.localFactId) tokens.push(`事实#${item.localFactId}`)
  if (item.sourceRecordId) tokens.push(`源#${item.sourceRecordId}`)
  return tokens.join(' / ')
}

const buildListSections = data => {
  if (!data) return []
  const configs = [
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
  const extraMetrics = {}
  const recommendation = data.recommendation && typeof data.recommendation === 'object'
    ? data.recommendation
    : null
  const rootMetrics = ['entryScore', 'valuationScore', 'positionPercent', 'targetPrice', 'takeProfitPrice', 'stopLossPrice']
  rootMetrics.forEach(key => {
    if (data[key] !== undefined && data[key] !== null && data[key] !== '') {
      extraMetrics[key] = data[key]
    }
    if (extraMetrics[key] == null && recommendation?.[key] !== undefined && recommendation[key] !== null && recommendation[key] !== '') {
      extraMetrics[key] = recommendation[key]
    }
  })

  const probabilitySources = [data.probabilities, data.probability_analysis, data.analysis]
  probabilitySources.forEach(source => {
    if (!source || typeof source !== 'object') return
    if (extraMetrics.riseProbability == null) {
      extraMetrics.riseProbability = source.rise_probability ?? source.up_probability ?? source.probability_up ?? null
    }
    if (extraMetrics.fallProbability == null) {
      extraMetrics.fallProbability = source.fall_probability ?? source.down_probability ?? source.probability_down ?? null
    }
  })

  return buildMetricRows(data.metrics, extraMetrics, data.sentiment)
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
  <article class="agent-card">
    <header>
      <div>
        <h4>{{ getAgentName(props.agent) || getAgentId(props.agent) }}</h4>
        <p v-if="!getAgentSuccess(props.agent) && getAgentError(props.agent)" class="muted error">
          {{ getAgentError(props.agent) }}
        </p>
      </div>
      <div class="header-actions">
        <span v-if="getAgentData(props.agent) && getConfidence(getAgentData(props.agent)) != null" class="confidence-badge">
          置信度 {{ formatMetricValue(getConfidence(getAgentData(props.agent)), 'confidence') }}
        </span>
        <button class="raw-toggle" @click="emit('toggle-raw')">JSON</button>
      </div>
    </header>

    <div v-if="getAgentData(props.agent)" class="agent-content">
      <p class="summary">{{ getAgentData(props.agent).summary }}</p>

      <div v-if="getNarrativeRows(getAgentData(props.agent)).length" class="recommendation-block">
        <h5>核心判断</h5>
        <div class="metrics">
          <div v-for="row in getNarrativeRows(getAgentData(props.agent))" :key="`rec-${row.key}`" class="metric-row narrative-row">
            <span class="metric-key">{{ formatMetricLabel(row.key) }}</span>
            <span class="metric-value">{{ formatMetricValue(row.value, row.key) }}</span>
          </div>
        </div>
      </div>

      <div v-if="buildMetrics(getAgentData(props.agent)).length" class="metrics">
        <div v-for="row in buildMetrics(getAgentData(props.agent))" :key="row.key" class="metric-row">
          <span class="metric-key">{{ formatMetricLabel(row.key) }}</span>
          <span class="metric-value">{{ formatMetricValue(row.value, row.key) }}</span>
        </div>
      </div>

      <StockAgentChart v-if="getAgentData(props.agent).chart" :chart="getAgentData(props.agent).chart" />

      <div v-if="getEvidenceItems(getAgentData(props.agent)).length" class="evidence-section">
        <h5>证据来源</h5>
        <div class="evidence-list">
          <article
            v-for="(item, idx) in getEvidenceItems(getAgentData(props.agent))"
            :key="`${getAgentId(props.agent)}-evidence-${idx}`"
            class="evidence-card"
          >
            <div class="evidence-heading">
              <a v-if="item.url" class="evidence-link" :href="item.url" target="_blank" rel="noreferrer">
                {{ item.title }}
              </a>
              <span v-else class="evidence-title">{{ item.title }}</span>
              <span
                v-if="item.publishedAt"
                :class="['evidence-time', isExpiredPublishedAt(item.publishedAt) ? 'expired' : 'fresh']"
              >
                {{ getEvidencePublishedText(item.publishedAt) }}
              </span>
            </div>

            <p v-if="item.excerpt" class="evidence-excerpt">{{ item.excerpt }}</p>

            <div class="evidence-meta">
              <span>{{ item.source || '来源未知' }}</span>
              <span v-if="item.ingestedAt">入库 {{ formatMetricValue(item.ingestedAt, 'ingestedAt') }}</span>
              <span v-if="getEvidenceRecordText(item)">{{ getEvidenceRecordText(item) }}</span>
            </div>

            <div v-if="item.readStatus || item.readMode" class="evidence-pill-row">
              <span
                v-if="item.readStatus"
                :class="['evidence-pill', getEvidenceStatusClass(item.readStatus)]"
              >
                {{ formatMetricValue(item.readStatus, 'readStatus') }}
              </span>
              <span
                v-if="item.readMode"
                :class="['evidence-pill', getEvidenceModeClass(item.readMode)]"
              >
                {{ formatMetricValue(item.readMode, 'readMode') }}
              </span>
            </div>
          </article>
        </div>
      </div>

      <div v-for="section in buildListSections(getAgentData(props.agent))" :key="section.key" class="list-section">
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

      <div v-if="Array.isArray(getAgentData(props.agent).signals) && getAgentData(props.agent).signals.length" class="pill-list">
        <h5>信号</h5>
        <div class="pills">
          <span v-for="(item, idx) in getAgentData(props.agent).signals" :key="idx" class="pill">{{ item }}</span>
        </div>
      </div>

      <div v-if="buildTagList(getAgentData(props.agent), 'triggers').length" class="pill-list">
        <h5>触发条件</h5>
        <div class="pills">
          <span v-for="(item, idx) in buildTagList(getAgentData(props.agent), 'triggers')" :key="`trigger-${idx}`" class="pill trigger">{{ item }}</span>
        </div>
      </div>

      <div v-if="buildTagList(getAgentData(props.agent), 'invalidations').length" class="pill-list">
        <h5>失效条件</h5>
        <div class="pills">
          <span v-for="(item, idx) in buildTagList(getAgentData(props.agent), 'invalidations')" :key="`invalid-${idx}`" class="pill invalid">{{ item }}</span>
        </div>
      </div>

      <div v-if="buildTagList(getAgentData(props.agent), 'riskLimits').length" class="pill-list">
        <h5>风险上限</h5>
        <div class="pills">
          <span v-for="(item, idx) in buildTagList(getAgentData(props.agent), 'riskLimits')" :key="`limit-${idx}`" class="pill risk-limit">{{ item }}</span>
        </div>
      </div>

      <div v-if="Array.isArray(getAgentData(props.agent).risks) && getAgentData(props.agent).risks.length" class="pill-list">
        <h5>风险</h5>
        <div class="pills">
          <span v-for="(item, idx) in getAgentData(props.agent).risks" :key="idx" class="pill risk">{{ item }}</span>
        </div>
      </div>

      <button v-if="canDraftPlan(props.agent)" class="draft-plan-button" @click="emit('draft-plan')">
        基于此分析起草交易计划
      </button>
    </div>

    <p v-else class="muted empty">暂无数据</p>

    <pre v-if="props.rawVisible" class="raw-json">{{ getAgentData(props.agent) ? JSON.stringify(getAgentData(props.agent), null, 2) : getAgentRaw(props.agent) }}</pre>
  </article>
</template>

<style scoped>
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

.evidence-section h5 {
  margin: 0 0 0.45rem;
}

.evidence-list {
  display: grid;
  gap: 0.6rem;
}

.evidence-card {
  border: 1px solid rgba(191, 219, 254, 0.85);
  border-radius: 12px;
  padding: 0.7rem 0.8rem;
  background: linear-gradient(180deg, rgba(239, 246, 255, 0.8), rgba(255, 255, 255, 0.96));
}

.evidence-heading {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 0.5rem;
}

.evidence-link,
.evidence-title {
  color: #0f172a;
  font-size: 0.86rem;
  font-weight: 600;
  line-height: 1.45;
}

.evidence-link {
  text-decoration: none;
}

.evidence-link:hover {
  text-decoration: underline;
}

.evidence-time {
  flex-shrink: 0;
  font-size: 0.72rem;
  font-weight: 600;
}

.evidence-time.fresh {
  color: #15803d;
}

.evidence-time.expired {
  color: #b45309;
}

.evidence-excerpt {
  margin: 0.45rem 0 0;
  color: #334155;
  font-size: 0.8rem;
  line-height: 1.5;
}

.evidence-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem 0.7rem;
  margin-top: 0.45rem;
  color: #64748b;
  font-size: 0.75rem;
}

.evidence-pill-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
  margin-top: 0.45rem;
}

.evidence-pill {
  border-radius: 999px;
  padding: 0.2rem 0.55rem;
  font-size: 0.72rem;
  font-weight: 600;
}

.evidence-pill-strong {
  background: rgba(22, 163, 74, 0.14);
  color: #166534;
}

.evidence-pill-medium {
  background: rgba(2, 132, 199, 0.14);
  color: #075985;
}

.evidence-pill-weak {
  background: rgba(245, 158, 11, 0.16);
  color: #b45309;
}

.evidence-mode-local {
  background: rgba(30, 64, 175, 0.12);
  color: #1d4ed8;
}

.evidence-mode-url {
  background: rgba(8, 145, 178, 0.14);
  color: #0f766e;
}

.evidence-mode-muted {
  background: rgba(148, 163, 184, 0.16);
  color: #475569;
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

.draft-plan-button {
  align-self: flex-start;
}

.raw-json {
  background: #0f172a;
  color: #e2e8f0;
  font-size: 0.75rem;
  padding: 0.6rem;
  border-radius: 10px;
  overflow-x: auto;
}
</style>