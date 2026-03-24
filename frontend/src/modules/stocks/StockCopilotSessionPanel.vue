<script setup>
import { computed } from 'vue'

const props = defineProps({
  currentStockLabel: {
    type: String,
    default: '未选择股票'
  },
  question: {
    type: String,
    default: ''
  },
  loading: {
    type: Boolean,
    default: false
  },
  error: {
    type: String,
    default: ''
  },
  allowExternalSearch: {
    type: Boolean,
    default: false
  },
  turn: {
    type: Object,
    default: null
  },
  sessionTitle: {
    type: String,
    default: ''
  },
  replayTurns: {
    type: Array,
    default: () => []
  },
  acceptanceBaseline: {
    type: Object,
    default: null
  },
  acceptanceLoading: {
    type: Boolean,
    default: false
  },
  acceptanceError: {
    type: String,
    default: ''
  },
  busyCallId: {
    type: String,
    default: ''
  }
})

const emit = defineEmits([
  'update:question',
  'toggle-external-search',
  'submit',
  'select-replay',
  'execute-tool',
  'activate-action'
])

const getValue = (item, camelKey, pascalKey = camelKey.charAt(0).toUpperCase() + camelKey.slice(1), fallback = '') => {
  if (!item) {
    return fallback
  }

  const value = item[camelKey] ?? item[pascalKey]
  return value ?? fallback
}

const getListValue = (item, camelKey, pascalKey = camelKey.charAt(0).toUpperCase() + camelKey.slice(1)) => {
  const value = getValue(item, camelKey, pascalKey, [])
  return Array.isArray(value) ? value : []
}

const NAVIGATION_NOISE_KEYWORDS = new Set([
  '财经', '焦点', '股票', '新股', '期指', '期权', '行情', '数据', '全球', '美股', '港股',
  '基金', '外汇', '黄金', '债券', '理财', '期货', '直播', '专题', '博客', '股吧', '研报',
  '公告', '个股', '板块', '市场', '滚动', '新闻', '首页', '下载', 'app', '客户端', '更多'
])

const normalizeSnippetText = value => String(value || '')
  .replace(/[\u00a0\u3000]/g, ' ')
  .replace(/\s+/g, ' ')
  .trim()

const stripLeadingNavigationTerms = value => {
  const tokens = normalizeSnippetText(value).split(' ').filter(Boolean)
  if (tokens.length < 6) {
    return normalizeSnippetText(value)
  }

  let index = 0
  while (index < tokens.length && NAVIGATION_NOISE_KEYWORDS.has(tokens[index].toLowerCase())) {
    index += 1
  }

  return index >= 6 && index < tokens.length
    ? tokens.slice(index).join(' ')
    : normalizeSnippetText(value)
}

const isNavigationNoise = value => {
  const tokens = normalizeSnippetText(value).split(' ').filter(Boolean)
  if (tokens.length < 6) {
    return false
  }

  const navigationCount = tokens.filter(token => NAVIGATION_NOISE_KEYWORDS.has(token.toLowerCase())).length
  const shortTokenCount = tokens.filter(token => token.length <= 4).length
  const punctuationCount = (value.match(/[。！？；;.!?]/g) || []).length
  return navigationCount >= 5 && shortTokenCount * 2 >= tokens.length && punctuationCount === 0
}

const trimSnippet = value => {
  if (!value) {
    return ''
  }

  if (value.length <= 120) {
    return value
  }

  const truncated = value.slice(0, 120).trimEnd()
  const boundary = Math.max(
    truncated.lastIndexOf('。'),
    truncated.lastIndexOf('；'),
    truncated.lastIndexOf(';'),
    truncated.lastIndexOf('，'),
    truncated.lastIndexOf(','),
    truncated.lastIndexOf(' ')
  )

  return `${(boundary >= 40 ? truncated.slice(0, boundary) : truncated).trimEnd()}...`
}

const sanitizeEvidenceText = value => {
  const normalized = stripLeadingNavigationTerms(value)
  if (!normalized || isNavigationNoise(normalized)) {
    return ''
  }

  const segments = normalized
    .split(/(?<=[。！？；;.!?])\s+|\r?\n+/)
    .map(item => item.trim())
    .filter(Boolean)
    .filter(item => !isNavigationNoise(item))
    .slice(0, 2)

  return trimSnippet(segments.join(' ') || normalized)
}

const getEvidenceSnippet = item => {
  const summary = sanitizeEvidenceText(getValue(item, 'summary'))
  const excerpt = sanitizeEvidenceText(getValue(item, 'excerpt'))
  const point = sanitizeEvidenceText(getValue(item, 'point'))
  const title = getValue(item, 'title')
  return summary || excerpt || (point && point !== title ? point : '')
}

const cnDateTimeFormatter = new Intl.DateTimeFormat('zh-CN', {
  timeZone: 'Asia/Shanghai',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hour12: false
})

const formatDate = value => {
  if (!value) {
    return ''
  }

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return ''
  }

  return cnDateTimeFormatter.format(date)
}

const currentTurn = computed(() => props.turn || null)

const toolResultsByCallId = computed(() => {
  const entries = getListValue(currentTurn.value, 'toolResults')
  return Object.fromEntries(entries.map(item => [getValue(item, 'callId'), item]))
})

const aggregatedEvidence = computed(() => {
  const toolPayloads = currentTurn.value?.toolPayloads || {}
  const dedup = new Map()

  Object.values(toolPayloads).forEach(payload => {
    const evidenceList = Array.isArray(payload?.evidence ?? payload?.Evidence) ? (payload.evidence ?? payload.Evidence) : []
    evidenceList.forEach(item => {
      const url = getValue(item, 'url')
      const title = getValue(item, 'title')
      const snippet = getEvidenceSnippet(item)
      const key = `${url}::${title}`
      if (!dedup.has(key)) {
        dedup.set(key, { ...item, snippet })
      }
    })
  })

  return Array.from(dedup.values())
})

const replayItems = computed(() => props.replayTurns || [])
const acceptanceMetrics = computed(() => getListValue(props.acceptanceBaseline, 'metrics'))
const replayBaseline = computed(() => getValue(props.acceptanceBaseline, 'replayBaseline', 'ReplayBaseline', null))
const replayHorizons = computed(() => getListValue(replayBaseline.value, 'horizons'))
const replayPrimaryHorizon = computed(() => replayHorizons.value.find(item => Number(getValue(item, 'horizonDays')) === 5) || replayHorizons.value[0] || null)
const acceptanceHighlights = computed(() => getListValue(props.acceptanceBaseline, 'highlights'))

const isTurnSelected = turnId => getValue(currentTurn.value, 'turnId') === turnId

const getPlanStepTone = status => {
  if (status === 'approved' || status === 'planned') {
    return 'ok'
  }
  if (status === 'blocked') {
    return 'blocked'
  }
  if (status === 'pending_tool_results') {
    return 'pending'
  }
  return 'idle'
}

const getToolTone = call => {
  const callId = getValue(call, 'callId')
  const result = toolResultsByCallId.value[callId]
  if (result) {
    return getValue(result, 'status') === 'completed' ? 'completed' : 'warn'
  }

  return getValue(call, 'approvalStatus') === 'approved' ? 'ready' : 'blocked'
}

const getActionTone = action => {
  return getValue(action, 'enabled', 'Enabled', false) ? 'enabled' : 'blocked'
}

const formatFixed = (value, digits = 0) => {
  const numeric = Number(value)
  if (!Number.isFinite(numeric)) {
    return '--'
  }

  return numeric.toFixed(digits)
}

const formatPercentValue = value => {
  const numeric = Number(value)
  if (!Number.isFinite(numeric)) {
    return '--'
  }

  return `${numeric.toFixed(0)}%`
}
</script>

<template>
  <section class="copilot-session-card">
    <div class="copilot-session-header">
      <div>
        <p class="copilot-session-label">Stock Copilot</p>
        <h3>会话化协驾</h3>
        <p class="muted">当前标的：{{ currentStockLabel }}</p>
      </div>
      <div v-if="sessionTitle" class="session-badge">
        <span class="muted">当前会话</span>
        <strong>{{ sessionTitle }}</strong>
      </div>
    </div>

    <form class="copilot-session-form" @submit.prevent="emit('submit')">
      <label class="copilot-question-field">
        <span>问题输入</span>
        <textarea
          :value="question"
          rows="3"
          placeholder="例如：先看这只股票 60 日结构，再核对本地公告有没有新的风险点。"
          @input="emit('update:question', $event.target.value)"
        />
      </label>

      <div class="copilot-session-actions">
        <label class="copilot-checkbox">
          <input type="checkbox" :checked="allowExternalSearch" @change="emit('toggle-external-search', $event.target.checked)">
          <span>允许外部搜索兜底</span>
        </label>
        <button class="copilot-submit-button" type="submit" :disabled="loading || !question.trim()">
          {{ loading ? '生成草案中...' : '生成 Copilot 草案' }}
        </button>
      </div>
    </form>

    <p v-if="error" class="muted error">{{ error }}</p>

    <div v-if="replayItems.length" class="copilot-replay-strip">
      <div class="copilot-section-head">
        <strong>最近一轮回放</strong>
        <span class="muted">点击切换到上一轮 Copilot turn</span>
      </div>
      <div class="copilot-replay-list">
        <button
          v-for="item in replayItems"
          :key="getValue(item, 'turnId')"
          class="copilot-replay-chip"
          :class="{ active: isTurnSelected(getValue(item, 'turnId')) }"
          @click="emit('select-replay', getValue(item, 'turnId'))"
        >
          <strong>{{ getValue(item, 'userQuestion') }}</strong>
          <span>{{ formatDate(getValue(item, 'createdAt')) || getValue(item, 'status') }}</span>
        </button>
      </div>
    </div>

    <div v-if="currentTurn" class="copilot-session-body">
      <article class="copilot-status-card">
        <div class="copilot-section-head">
          <strong>本轮草案</strong>
          <span class="status-pill">{{ getValue(currentTurn, 'status') }}</span>
        </div>
        <p class="copilot-question-preview">{{ getValue(currentTurn, 'userQuestion') }}</p>
        <div class="copilot-summary-grid">
          <div>
            <span class="muted summary-label">Planner</span>
            <p>{{ getValue(currentTurn, 'plannerSummary') }}</p>
          </div>
          <div>
            <span class="muted summary-label">Governor</span>
            <p>{{ getValue(currentTurn, 'governorSummary') }}</p>
          </div>
        </div>
      </article>

      <article class="copilot-timeline-card">
        <div class="copilot-section-head">
          <strong>计划时间线</strong>
          <span class="muted">提问 -> 看计划 -> 看工具 -> 看受控回答</span>
        </div>
        <ol class="copilot-timeline-list">
          <li
            v-for="step in getListValue(currentTurn, 'planSteps')"
            :key="getValue(step, 'stepId')"
            class="copilot-timeline-item"
            :class="`tone-${getPlanStepTone(getValue(step, 'status'))}`"
          >
            <div class="timeline-row">
              <strong>{{ getValue(step, 'title') }}</strong>
              <span class="timeline-meta">{{ getValue(step, 'owner') }} · {{ getValue(step, 'status') }}</span>
            </div>
            <p>{{ getValue(step, 'description') }}</p>
            <small v-if="getValue(step, 'toolName')" class="muted">工具：{{ getValue(step, 'toolName') }}</small>
          </li>
        </ol>
      </article>

      <article class="copilot-tools-card">
        <div class="copilot-section-head">
          <strong>工具调用卡片</strong>
          <span class="muted">先看审批状态，再决定是否执行。</span>
        </div>
        <div class="copilot-tool-grid">
          <article
            v-for="call in getListValue(currentTurn, 'toolCalls')"
            :key="getValue(call, 'callId')"
            class="copilot-tool-card"
            :class="`tone-${getToolTone(call)}`"
          >
            <div class="timeline-row">
              <strong>{{ getValue(call, 'toolName') }}</strong>
              <span class="tool-pill">{{ getValue(call, 'approvalStatus') }}</span>
            </div>
            <p>{{ getValue(call, 'purpose') }}</p>
            <small class="muted">policy={{ getValue(call, 'policyClass') }}</small>
            <small class="muted">input={{ getValue(call, 'inputSummary') }}</small>

            <p v-if="getValue(call, 'blockedReason')" class="muted error">{{ getValue(call, 'blockedReason') }}</p>

            <div v-if="toolResultsByCallId[getValue(call, 'callId')]" class="tool-result-box">
              <strong>结果摘要</strong>
              <p>{{ getValue(toolResultsByCallId[getValue(call, 'callId')], 'summary') }}</p>
              <div class="tool-result-meta">
                <span>evidence {{ getValue(toolResultsByCallId[getValue(call, 'callId')], 'evidenceCount', 'EvidenceCount', 0) }}</span>
                <span>features {{ getValue(toolResultsByCallId[getValue(call, 'callId')], 'featureCount', 'FeatureCount', 0) }}</span>
                <span v-if="getValue(toolResultsByCallId[getValue(call, 'callId')], 'traceId', 'TraceId')">trace {{ getValue(toolResultsByCallId[getValue(call, 'callId')], 'traceId', 'TraceId') }}</span>
              </div>
            </div>

            <button
              v-if="getValue(call, 'approvalStatus') === 'approved'"
              class="tool-run-button"
              :disabled="busyCallId === getValue(call, 'callId')"
              @click="emit('execute-tool', getValue(call, 'callId'))"
            >
              {{ busyCallId === getValue(call, 'callId') ? '执行中...' : '执行工具' }}
            </button>
          </article>
        </div>
      </article>

      <article class="copilot-evidence-card">
        <div class="copilot-section-head">
          <strong>Evidence / Source</strong>
          <span class="muted">执行工具后，这里展示来源、时间和摘要。</span>
        </div>
        <ul v-if="aggregatedEvidence.length" class="copilot-evidence-list">
          <li v-for="item in aggregatedEvidence" :key="`${getValue(item, 'url')}::${getValue(item, 'title')}`">
            <div class="timeline-row">
              <a v-if="getValue(item, 'url')" :href="getValue(item, 'url')" target="_blank" rel="noreferrer">
                {{ getValue(item, 'title') || getValue(item, 'url') }}
              </a>
              <strong v-else>{{ getValue(item, 'title') }}</strong>
              <span class="muted">{{ getValue(item, 'source') }}</span>
            </div>
            <p v-if="getValue(item, 'snippet')">{{ getValue(item, 'snippet') }}</p>
            <small class="muted">
              {{ formatDate(getValue(item, 'publishedAt')) }}
              <span v-if="getValue(item, 'readMode')"> · {{ getValue(item, 'readMode') }}</span>
              <span v-if="getValue(item, 'readStatus')"> · {{ getValue(item, 'readStatus') }}</span>
            </small>
          </li>
        </ul>
        <p v-else class="muted">执行本地新闻、K 线或策略工具后，这里会显示来源和证据摘要。</p>
      </article>

      <article class="copilot-final-card">
        <div class="copilot-section-head">
          <strong>受控回答状态</strong>
          <span class="status-pill">{{ getValue(getValue(currentTurn, 'finalAnswer'), 'status') }}</span>
        </div>
        <p>{{ getValue(getValue(currentTurn, 'finalAnswer'), 'summary') }}</p>
        <ul class="copilot-constraints">
          <li v-for="item in getListValue(getValue(currentTurn, 'finalAnswer'), 'constraints')" :key="item">{{ item }}</li>
        </ul>
      </article>

      <article class="copilot-acceptance-card">
        <div class="copilot-section-head">
          <strong>Copilot 质量基线</strong>
          <span class="status-pill">{{ acceptanceLoading ? 'loading' : `${formatFixed(getValue(acceptanceBaseline, 'overallScore'), 0)}/100` }}</span>
        </div>
        <p v-if="acceptanceError" class="muted error">{{ acceptanceError }}</p>
        <p v-else-if="acceptanceLoading" class="muted">正在根据当前 turn、工具执行和 replay 基线生成验收指标。</p>
        <template v-else-if="acceptanceBaseline">
          <div class="copilot-metric-grid">
            <article
              v-for="metric in acceptanceMetrics"
              :key="getValue(metric, 'key')"
              class="copilot-metric-chip"
              :class="`tone-${getValue(metric, 'status')}`"
            >
              <span class="muted">{{ getValue(metric, 'label') }}</span>
              <strong>{{ formatFixed(getValue(metric, 'value'), 0) }}{{ getValue(metric, 'unit') }}</strong>
              <small class="muted">{{ getValue(metric, 'description') }}</small>
            </article>
          </div>
          <ul v-if="acceptanceHighlights.length" class="copilot-acceptance-list">
            <li v-for="item in acceptanceHighlights" :key="item">{{ item }}</li>
          </ul>
        </template>
        <p v-else class="muted">生成草案后，这里会显示工具效率、证据覆盖、Local-First 命中等验收指标。</p>
      </article>

      <article class="copilot-replay-card">
        <div class="copilot-section-head">
          <strong>Replay 基线</strong>
          <span class="status-pill">{{ getValue(replayBaseline, 'sampleCount', 'SampleCount', 0) }} 条样本</span>
        </div>
        <div v-if="replayBaseline" class="copilot-replay-baseline-grid">
          <div>
            <span class="muted summary-label">Evidence Traceability</span>
            <p>{{ formatPercentValue(getValue(replayBaseline, 'traceableEvidenceRate', 'TraceableEvidenceRate', 0)) }}</p>
          </div>
          <div>
            <span class="muted summary-label">Parse Repair</span>
            <p>{{ formatPercentValue(getValue(replayBaseline, 'parseRepairRate', 'ParseRepairRate', 0)) }}</p>
          </div>
          <div>
            <span class="muted summary-label">5D Hit Rate</span>
            <p>{{ formatPercentValue(getValue(replayPrimaryHorizon, 'hitRate', 'HitRate', 0)) }}</p>
          </div>
          <div>
            <span class="muted summary-label">5D Brier</span>
            <p>{{ formatFixed(getValue(replayPrimaryHorizon, 'brierScore', 'BrierScore', 0), 2) }}</p>
          </div>
        </div>
        <p v-else class="muted">验收基线会复用现有 replay calibration 样本，展示当前 symbol 的历史命中质量。</p>
      </article>

      <article class="copilot-actions-card">
        <div class="copilot-section-head">
          <strong>下一步动作</strong>
          <span class="muted">优先进入已批准工具，再进入计划流。</span>
        </div>
        <div class="copilot-action-list">
          <button
            v-for="action in getListValue(currentTurn, 'followUpActions')"
            :key="getValue(action, 'actionId')"
            class="copilot-action-chip"
            :data-action-id="getValue(action, 'actionId')"
            :class="`tone-${getActionTone(action)}`"
            :disabled="!getValue(action, 'enabled', 'Enabled', false)"
            :title="getValue(action, 'blockedReason')"
            @click="emit('activate-action', getValue(action, 'actionId'))"
          >
            <strong>{{ getValue(action, 'label') }}</strong>
            <span>{{ getValue(action, 'description') }}</span>
          </button>
        </div>
      </article>
    </div>
    <div v-else class="copilot-empty-state">
      <h4>Copilot 已待命</h4>
      <p>先输入一个和当前股票相关的问题，生成第一轮 plan/timeline 草案。</p>
    </div>
  </section>
</template>

<style scoped>
.copilot-session-card {
  display: grid;
  gap: 1rem;
  padding: 1rem;
  border-radius: 18px;
  background: linear-gradient(180deg, rgba(255, 255, 255, 0.96), rgba(248, 250, 252, 0.92));
  border: 1px solid rgba(148, 163, 184, 0.18);
}

.copilot-session-header,
.copilot-section-head,
.timeline-row,
.copilot-session-actions {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 0.75rem;
}

.copilot-session-label {
  margin: 0 0 0.35rem;
  font-size: 0.72rem;
  letter-spacing: 0.18em;
  text-transform: uppercase;
  color: #0f766e;
}

.copilot-session-card h3,
.copilot-empty-state h4 {
  margin: 0;
  color: #0f172a;
}

.copilot-session-form,
.copilot-session-body,
.copilot-timeline-list,
.copilot-evidence-list,
.copilot-constraints,
.copilot-empty-state,
.session-badge,
.tool-result-box,
.copilot-summary-grid,
.copilot-replay-strip,
.copilot-metric-grid,
.copilot-replay-baseline-grid,
.copilot-acceptance-list {
  display: grid;
  gap: 0.75rem;
}

.copilot-question-field,
.copilot-question-field textarea {
  width: 100%;
}

.copilot-question-field {
  display: grid;
  gap: 0.45rem;
}

.copilot-question-field textarea {
  resize: vertical;
  min-height: 88px;
  border-radius: 14px;
  border: 1px solid rgba(148, 163, 184, 0.35);
  padding: 0.8rem 0.9rem;
  font: inherit;
  background: rgba(255, 255, 255, 0.9);
}

.copilot-checkbox {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  color: #334155;
}

.copilot-submit-button,
.tool-run-button,
.copilot-replay-chip,
.copilot-action-chip {
  border: none;
  cursor: pointer;
}

.copilot-submit-button,
.tool-run-button {
  border-radius: 999px;
  padding: 0.65rem 1rem;
  background: linear-gradient(135deg, #0f766e, #0ea5e9);
  color: #f8fafc;
}

.copilot-submit-button:disabled,
.tool-run-button:disabled,
.copilot-action-chip:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.copilot-replay-list,
.copilot-tool-grid,
.copilot-action-list,
.copilot-summary-grid {
  display: grid;
  gap: 0.75rem;
}

.copilot-replay-list,
.copilot-action-list {
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
}

.copilot-tool-grid,
.copilot-summary-grid {
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
}

.copilot-metric-grid,
.copilot-replay-baseline-grid {
  grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
}

.copilot-replay-chip,
.copilot-tool-card,
.copilot-status-card,
.copilot-timeline-card,
.copilot-evidence-card,
.copilot-final-card,
.copilot-actions-card,
.copilot-acceptance-card,
.copilot-replay-card {
  text-align: left;
  padding: 0.85rem 0.95rem;
  border-radius: 14px;
  background: rgba(248, 250, 252, 0.82);
  border: 1px solid rgba(148, 163, 184, 0.16);
}

.copilot-replay-chip {
  display: grid;
  gap: 0.35rem;
  background: rgba(255, 255, 255, 0.92);
}

.copilot-replay-chip.active {
  border-color: rgba(14, 165, 233, 0.55);
  box-shadow: inset 0 0 0 1px rgba(14, 165, 233, 0.3);
}

.tool-pill,
.status-pill {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 0.2rem 0.55rem;
  border-radius: 999px;
  font-size: 0.72rem;
  background: rgba(15, 23, 42, 0.08);
  color: #0f172a;
}

.copilot-tool-card,
.copilot-action-chip {
  display: grid;
  gap: 0.45rem;
}

.copilot-metric-chip {
  display: grid;
  gap: 0.35rem;
  padding: 0.7rem 0.75rem;
  border-radius: 12px;
  background: rgba(255, 255, 255, 0.92);
  border: 1px solid rgba(148, 163, 184, 0.2);
}

.copilot-tool-card.tone-ready,
.copilot-timeline-item.tone-ok {
  border-color: rgba(20, 184, 166, 0.28);
}

.copilot-tool-card.tone-completed {
  border-color: rgba(14, 165, 233, 0.35);
  background: rgba(240, 249, 255, 0.96);
}

.copilot-tool-card.tone-blocked,
.copilot-timeline-item.tone-blocked,
.copilot-action-chip.tone-blocked {
  border-color: rgba(239, 68, 68, 0.24);
  background: rgba(254, 242, 242, 0.92);
}

.copilot-metric-chip.tone-good {
  border-color: rgba(20, 184, 166, 0.28);
  background: rgba(240, 253, 250, 0.96);
}

.copilot-metric-chip.tone-watch {
  border-color: rgba(245, 158, 11, 0.28);
  background: rgba(255, 251, 235, 0.96);
}

.copilot-metric-chip.tone-risk {
  border-color: rgba(239, 68, 68, 0.24);
  background: rgba(254, 242, 242, 0.96);
}

.copilot-timeline-list,
.copilot-evidence-list,
.copilot-constraints,
.copilot-acceptance-list {
  margin: 0;
  padding-left: 1rem;
}

.copilot-timeline-item,
.copilot-evidence-list li,
.copilot-constraints li,
.copilot-acceptance-list li {
  display: grid;
  gap: 0.25rem;
}

.copilot-question-preview,
.copilot-tool-card p,
.tool-result-box p,
.copilot-empty-state p,
.copilot-summary-grid p,
.copilot-evidence-list p,
.session-badge,
.copilot-session-card .muted {
  margin: 0;
}

.tool-result-box {
  padding: 0.65rem 0.7rem;
  border-radius: 12px;
  background: rgba(255, 255, 255, 0.88);
}

.tool-result-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  color: #475569;
  font-size: 0.82rem;
}

.copilot-action-chip {
  border-radius: 14px;
  padding: 0.75rem 0.85rem;
  background: rgba(255, 255, 255, 0.95);
}

.copilot-action-chip.tone-enabled {
  border: 1px solid rgba(14, 165, 233, 0.28);
}

.summary-label {
  display: inline-block;
  margin-bottom: 0.25rem;
}

.error {
  color: #dc2626;
}

.muted {
  color: #64748b;
  font-size: 0.85rem;
}

@media (max-width: 900px) {
  .copilot-session-header,
  .copilot-section-head,
  .timeline-row,
  .copilot-session-actions {
    flex-direction: column;
  }
}
</style>