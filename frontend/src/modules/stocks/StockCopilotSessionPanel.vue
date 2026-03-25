<script setup>
import { computed, reactive } from 'vue'

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

const sanitizeEvidenceText = (value, { trim = true } = {}) => {
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

  const readable = segments.join(' ') || normalized
  return trim ? trimSnippet(readable) : readable
}

const getEvidenceSnippet = item => {
  const summary = sanitizeEvidenceText(getValue(item, 'summary'))
  const excerpt = sanitizeEvidenceText(getValue(item, 'excerpt'))
  const point = sanitizeEvidenceText(getValue(item, 'point'))
  const title = getValue(item, 'title')
  return summary || excerpt || (point && point !== title ? point : '')
}

const getEvidenceDetail = item => {
  const summary = sanitizeEvidenceText(getValue(item, 'summary'), { trim: false })
  const excerpt = sanitizeEvidenceText(getValue(item, 'excerpt'), { trim: false })
  const point = sanitizeEvidenceText(getValue(item, 'point'), { trim: false })

  return [summary, excerpt, point]
    .filter(Boolean)
    .filter((value, index, list) => list.indexOf(value) === index)
    .join('\n\n')
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
      const detail = getEvidenceDetail(item)
      const key = `${url}::${title}`
      if (!dedup.has(key)) {
        dedup.set(key, { ...item, evidenceKey: key, snippet, detail })
        return
      }

      const current = dedup.get(key)
      dedup.set(key, {
        ...current,
        ...item,
        evidenceKey: key,
        snippet: current?.snippet || snippet,
        detail: current?.detail || detail,
        url: current?.url || url,
        title: current?.title || title
      })
    })
  })

  return Array.from(dedup.values())
})

const expandedEvidence = reactive({})

const isEvidenceExpanded = item => Boolean(expandedEvidence[getValue(item, 'evidenceKey')])

const toggleEvidenceExpanded = item => {
  const key = getValue(item, 'evidenceKey')
  if (!key) {
    return
  }

  expandedEvidence[key] = !expandedEvidence[key]
}

const canExpandEvidence = item => {
  const snippet = getValue(item, 'snippet')
  const detail = getValue(item, 'detail')
  return Boolean(detail) && detail !== snippet
}

const replayItems = computed(() => props.replayTurns || [])
const acceptanceMetrics = computed(() => getListValue(props.acceptanceBaseline, 'metrics'))
const replayBaseline = computed(() => getValue(props.acceptanceBaseline, 'replayBaseline', 'ReplayBaseline', null))
const replayHorizons = computed(() => getListValue(replayBaseline.value, 'horizons'))
const replayPrimaryHorizon = computed(() => replayHorizons.value.find(item => Number(getValue(item, 'horizonDays')) === 5) || replayHorizons.value[0] || null)
const acceptanceHighlights = computed(() => getListValue(props.acceptanceBaseline, 'highlights'))
const finalAnswer = computed(() => getValue(currentTurn.value, 'finalAnswer', 'FinalAnswer', null))

const reasoningSummaryItems = computed(() => {
  const bullets = []
  const plannerSummary = getValue(currentTurn.value, 'plannerSummary').trim()
  const governorSummary = getValue(currentTurn.value, 'governorSummary').trim()

  if (plannerSummary) {
    bullets.push(plannerSummary)
  }

  if (governorSummary && governorSummary !== plannerSummary) {
    bullets.push(governorSummary)
  }

  getListValue(currentTurn.value, 'planSteps')
    .map(step => getValue(step, 'description').trim() || getValue(step, 'title').trim())
    .filter(Boolean)
    .forEach(item => {
      if (!bullets.includes(item)) {
        bullets.push(item)
      }
    })

  return bullets.slice(0, 4)
})

const toolTimelineItems = computed(() => getListValue(currentTurn.value, 'toolCalls').map(call => {
  const callId = getValue(call, 'callId')
  const result = toolResultsByCallId.value[callId] || null
  return {
    call,
    result,
    callId,
    toolName: getValue(call, 'toolName'),
    approvalStatus: getValue(call, 'approvalStatus'),
    purpose: getValue(call, 'purpose'),
    inputSummary: getValue(call, 'inputSummary'),
    blockedReason: getValue(call, 'blockedReason'),
    traceId: getValue(result, 'traceId', 'TraceId'),
    resultSummary: getValue(result, 'summary'),
    evidenceCount: getValue(result, 'evidenceCount', 'EvidenceCount', 0),
    featureCount: getValue(result, 'featureCount', 'FeatureCount', 0)
  }
}))

const getTurnStatusLabel = status => {
  const normalized = String(status || '').toLowerCase()
  if (normalized === 'drafting') return 'thinking'
  if (normalized.startsWith('calling_tools_round')) return 'calling tools'
  if (normalized === 'finalizing_answer') return 'answering'
  if (normalized === 'done') return 'done'
  if (normalized === 'done_with_gaps') return 'done_with_gaps'
  if (normalized === 'failed') return 'failed'
  return status || 'drafting'
}

const getTimelineTone = item => {
  if (item.result) {
    return getValue(item.result, 'status') === 'completed' ? 'completed' : 'warn'
  }

  return item.approvalStatus === 'approved' ? 'ready' : 'blocked'
}

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
          <span>{{ formatDate(getValue(item, 'createdAt')) || getTurnStatusLabel(getValue(item, 'status')) }}</span>
        </button>
      </div>
    </div>

    <div v-if="currentTurn" class="copilot-turn-stream">
      <article class="copilot-message copilot-message-user">
        <div class="copilot-message-role">你</div>
        <div class="copilot-message-body">
          <p class="copilot-message-caption">本轮问题</p>
          <p class="copilot-message-text">{{ getValue(currentTurn, 'userQuestion') }}</p>
        </div>
      </article>

      <article class="copilot-message copilot-message-assistant">
        <div class="copilot-message-role">Copilot</div>
        <div class="copilot-message-body assistant-body">
          <header class="copilot-turn-header">
            <div>
              <p class="copilot-message-caption">当前回复</p>
              <h4>同一轮内收口为最终回答</h4>
            </div>
            <div class="copilot-status-row">
              <span class="status-pill">{{ getTurnStatusLabel(getValue(currentTurn, 'status')) }}</span>
              <span class="status-pill subtle">{{ getValue(finalAnswer, 'status') || 'drafting' }}</span>
            </div>
          </header>

          <section class="copilot-turn-section">
            <div class="copilot-section-head compact">
              <strong>结构化思路摘要</strong>
              <span class="muted">保留 reasoning summary，不暴露原始 chain-of-thought</span>
            </div>
            <ul v-if="reasoningSummaryItems.length" class="copilot-bullet-list">
              <li v-for="item in reasoningSummaryItems" :key="item">{{ item }}</li>
            </ul>
            <p v-else class="muted">本轮尚未形成可展示的结构化摘要。</p>
          </section>

          <section class="copilot-turn-section">
            <div class="copilot-section-head compact">
              <strong>MCP 调用轨迹</strong>
              <span class="muted">紧凑时间线，工具结果直接挂在消息流里</span>
            </div>
            <ol v-if="toolTimelineItems.length" class="copilot-tool-timeline">
              <li
                v-for="item in toolTimelineItems"
                :key="item.callId"
                class="copilot-tool-timeline-item"
                :class="`tone-${getTimelineTone(item)}`"
              >
                <div class="timeline-row">
                  <strong>{{ item.toolName }}</strong>
                  <span class="tool-pill">{{ item.result ? getValue(item.result, 'status') : item.approvalStatus }}</span>
                </div>
                <p>{{ item.purpose }}</p>
                <small class="muted">input={{ item.inputSummary }}</small>
                <p v-if="item.resultSummary" class="timeline-result-summary">{{ item.resultSummary }}</p>
                <div v-if="item.result" class="tool-result-meta">
                  <span>evidence {{ item.evidenceCount }}</span>
                  <span>features {{ item.featureCount }}</span>
                  <span v-if="item.traceId">trace {{ item.traceId }}</span>
                </div>
                <p v-if="item.blockedReason" class="muted error">{{ item.blockedReason }}</p>
                <button
                  v-if="item.approvalStatus === 'approved' && !item.result"
                  class="tool-run-button"
                  :disabled="busyCallId === item.callId"
                  @click="emit('execute-tool', item.callId)"
                >
                  {{ busyCallId === item.callId ? '执行中...' : '执行工具' }}
                </button>
              </li>
            </ol>
            <p v-else class="muted">本轮暂无 MCP 调用轨迹。</p>
          </section>

          <section class="copilot-turn-section">
            <div class="copilot-section-head compact">
              <strong>证据摘要卡</strong>
              <span class="muted">默认只显示 clean summary，全文细节按需展开</span>
            </div>
            <ul v-if="aggregatedEvidence.length" class="copilot-evidence-list copilot-evidence-cards">
              <li v-for="item in aggregatedEvidence" :key="`${getValue(item, 'url')}::${getValue(item, 'title')}`" class="copilot-evidence-item">
                <div class="timeline-row">
                  <a v-if="getValue(item, 'url')" :href="getValue(item, 'url')" target="_blank" rel="noreferrer">
                    {{ getValue(item, 'title') || getValue(item, 'url') }}
                  </a>
                  <strong v-else>{{ getValue(item, 'title') }}</strong>
                  <span class="muted">{{ getValue(item, 'source') }}</span>
                </div>
                <p v-if="getValue(item, 'snippet')">{{ getValue(item, 'snippet') }}</p>
                <button
                  v-if="canExpandEvidence(item)"
                  type="button"
                  class="copilot-evidence-toggle"
                  @click="toggleEvidenceExpanded(item)"
                >
                  {{ isEvidenceExpanded(item) ? '收起详情' : '展开查看更多' }}
                </button>
                <div v-if="isEvidenceExpanded(item)" class="copilot-evidence-detail">
                  <p>{{ getValue(item, 'detail') }}</p>
                  <a
                    v-if="getValue(item, 'url')"
                    class="copilot-evidence-source-link"
                    :href="getValue(item, 'url')"
                    target="_blank"
                    rel="noreferrer"
                  >
                    查看原文
                  </a>
                </div>
                <small class="muted">
                  {{ formatDate(getValue(item, 'publishedAt')) }}
                  <span v-if="getValue(item, 'readMode')"> · {{ getValue(item, 'readMode') }}</span>
                  <span v-if="getValue(item, 'readStatus')"> · {{ getValue(item, 'readStatus') }}</span>
                </small>
              </li>
            </ul>
            <p v-else class="muted">执行本地新闻、K 线或策略工具后，这里会显示来源和证据摘要。</p>
          </section>

          <section class="copilot-turn-section copilot-final-answer-block">
            <div class="copilot-section-head compact">
              <strong>最终回答</strong>
              <span class="status-pill">{{ getValue(finalAnswer, 'status') || 'drafting' }}</span>
            </div>
            <p class="copilot-final-summary">{{ getValue(finalAnswer, 'summary') || '本轮回答还在收口中。' }}</p>
            <ul v-if="getListValue(finalAnswer, 'constraints').length" class="copilot-constraints">
              <li v-for="item in getListValue(finalAnswer, 'constraints')" :key="item">{{ item }}</li>
            </ul>
          </section>

          <section class="copilot-turn-section">
            <div class="copilot-section-head compact">
              <strong>下一步动作</strong>
              <span class="muted">动作继续挂在本轮回答下方，而不是单独做一堵卡片墙</span>
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
          </section>

          <details class="copilot-secondary-details">
            <summary>会话详情与质量指标</summary>
            <div class="copilot-secondary-details-body">
              <article v-if="getListValue(currentTurn, 'planSteps').length" class="copilot-timeline-card secondary-card">
                <div class="copilot-section-head compact">
                  <strong>计划时间线</strong>
                  <span class="muted">保留审计能力，但退出默认主舞台</span>
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

              <article class="copilot-acceptance-card secondary-card">
                <div class="copilot-section-head compact">
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

              <article class="copilot-replay-card secondary-card">
                <div class="copilot-section-head compact">
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
            </div>
          </details>
        </div>
      </article>
    </div>

    <form class="copilot-session-form copilot-input-dock" @submit.prevent="emit('submit')">
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
          {{ loading ? '生成草案中...' : '发送给 Copilot' }}
        </button>
      </div>
    </form>

    <p v-if="error" class="muted error">{{ error }}</p>

    <div v-if="!currentTurn" class="copilot-empty-state">
      <h4>Copilot 已待命</h4>
      <p>先输入一个和当前股票相关的问题，生成第一轮 grounded 回答。</p>
    </div>
  </section>
</template>

<style scoped>
.copilot-session-card {
  display: grid;
  gap: 1rem;
  padding: 1rem;
  border-radius: 22px;
  background:
    radial-gradient(circle at top right, rgba(14, 165, 233, 0.12), transparent 32%),
    linear-gradient(180deg, rgba(255, 255, 255, 0.98), rgba(248, 250, 252, 0.95));
  border: 1px solid rgba(148, 163, 184, 0.18);
}

.copilot-session-header,
.copilot-section-head,
.timeline-row,
.copilot-session-actions,
.copilot-turn-header,
.copilot-status-row {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 0.75rem;
}

.copilot-session-label,
.copilot-message-caption {
  margin: 0 0 0.35rem;
  font-size: 0.72rem;
  letter-spacing: 0.18em;
  text-transform: uppercase;
  color: #0f766e;
}

.copilot-session-card h3,
.copilot-empty-state h4,
.copilot-turn-header h4 {
  margin: 0;
  color: #0f172a;
}

.copilot-turn-stream,
.copilot-message,
.copilot-message-body,
.assistant-body,
.copilot-turn-section,
.copilot-session-form,
.copilot-empty-state,
.session-badge,
.copilot-replay-strip,
.copilot-replay-list,
.copilot-secondary-details-body,
.copilot-metric-grid,
.copilot-replay-baseline-grid,
.copilot-acceptance-list,
.copilot-timeline-list,
.copilot-tool-timeline,
.copilot-bullet-list,
.copilot-evidence-list,
.copilot-constraints,
.tool-result-box {
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
  border-radius: 16px;
  border: 1px solid rgba(148, 163, 184, 0.28);
  padding: 0.85rem 0.95rem;
  font: inherit;
  background: rgba(255, 255, 255, 0.92);
}

.copilot-input-dock {
  padding: 0.9rem 1rem;
  border-radius: 18px;
  background: rgba(255, 255, 255, 0.88);
  border: 1px solid rgba(148, 163, 184, 0.18);
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
.copilot-action-chip,
.copilot-evidence-toggle {
  border: none;
  cursor: pointer;
}

.copilot-submit-button,
.tool-run-button {
  border-radius: 999px;
  padding: 0.7rem 1rem;
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
.copilot-action-list,
.copilot-secondary-details-body {
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
}

.copilot-metric-grid,
.copilot-replay-baseline-grid {
  grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
}

.copilot-message {
  grid-template-columns: 60px minmax(0, 1fr);
  align-items: flex-start;
}

.copilot-message-role {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: 42px;
  border-radius: 14px;
  font-size: 0.82rem;
  font-weight: 700;
  background: rgba(15, 118, 110, 0.1);
  color: #0f766e;
}

.copilot-message-user .copilot-message-role {
  background: rgba(14, 165, 233, 0.12);
  color: #0369a1;
}

.copilot-message-body {
  padding: 1rem 1.05rem;
  border-radius: 18px;
  border: 1px solid rgba(148, 163, 184, 0.18);
  background: rgba(255, 255, 255, 0.92);
}

.copilot-message-user .copilot-message-body {
  background: linear-gradient(135deg, rgba(14, 165, 233, 0.12), rgba(255, 255, 255, 0.96));
}

.copilot-message-assistant .copilot-message-body {
  background: linear-gradient(180deg, rgba(255, 255, 255, 0.98), rgba(244, 247, 251, 0.95));
}

.copilot-message-text,
.copilot-final-summary,
.copilot-tool-timeline-item p,
.copilot-evidence-item p,
.copilot-empty-state p,
.session-badge,
.copilot-session-card .muted,
.copilot-turn-header p,
.copilot-bullet-list,
.copilot-constraints {
  margin: 0;
}

.copilot-turn-section {
  padding-top: 0.2rem;
}

.copilot-section-head.compact {
  align-items: baseline;
}

.copilot-status-row {
  flex-wrap: wrap;
  justify-content: flex-end;
}

.status-pill,
.tool-pill {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 0.25rem 0.6rem;
  border-radius: 999px;
  font-size: 0.72rem;
  background: rgba(15, 23, 42, 0.08);
  color: #0f172a;
}

.status-pill.subtle {
  background: rgba(14, 165, 233, 0.12);
  color: #0369a1;
}

.copilot-bullet-list,
.copilot-constraints,
.copilot-timeline-list,
.copilot-tool-timeline,
.copilot-evidence-list,
.copilot-acceptance-list {
  padding-left: 1rem;
}

.copilot-tool-timeline-item,
.copilot-evidence-item,
.copilot-timeline-item,
.copilot-action-chip,
.copilot-replay-chip,
.secondary-card {
  text-align: left;
  padding: 0.85rem 0.95rem;
  border-radius: 16px;
  border: 1px solid rgba(148, 163, 184, 0.16);
  background: rgba(255, 255, 255, 0.92);
}

.copilot-tool-timeline-item,
.copilot-evidence-item,
.copilot-timeline-item,
.copilot-action-chip,
.copilot-replay-chip,
.copilot-metric-chip {
  display: grid;
  gap: 0.45rem;
}

.copilot-tool-timeline-item.tone-ready,
.copilot-timeline-item.tone-ok {
  border-color: rgba(20, 184, 166, 0.28);
}

.copilot-tool-timeline-item.tone-completed {
  border-color: rgba(14, 165, 233, 0.3);
  background: rgba(240, 249, 255, 0.96);
}

.copilot-tool-timeline-item.tone-blocked,
.copilot-timeline-item.tone-blocked,
.copilot-action-chip.tone-blocked {
  border-color: rgba(239, 68, 68, 0.24);
  background: rgba(254, 242, 242, 0.94);
}

.timeline-result-summary {
  color: #0f172a;
  font-weight: 600;
}

.tool-result-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  color: #475569;
  font-size: 0.82rem;
}

.copilot-evidence-cards {
  padding-left: 0;
  list-style: none;
}

.copilot-evidence-toggle,
.copilot-evidence-source-link {
  align-self: flex-start;
  padding: 0;
  background: transparent;
  color: #0f766e;
  font: inherit;
  font-size: 0.84rem;
  font-weight: 600;
  text-decoration: none;
}

.copilot-evidence-toggle:hover,
.copilot-evidence-source-link:hover {
  color: #0c4a6e;
}

.copilot-evidence-detail {
  display: grid;
  gap: 0.5rem;
  padding: 0.85rem 0.95rem;
  border-radius: 12px;
  background: rgba(15, 118, 110, 0.06);
  border: 1px solid rgba(15, 118, 110, 0.14);
}

.copilot-evidence-detail p {
  margin: 0;
  white-space: pre-line;
  color: #0f172a;
}

.copilot-final-answer-block {
  padding: 0.95rem 1rem;
  border-radius: 16px;
  background: linear-gradient(135deg, rgba(15, 118, 110, 0.08), rgba(14, 165, 233, 0.08));
  border: 1px solid rgba(15, 118, 110, 0.18);
}

.copilot-action-list {
  display: grid;
  gap: 0.75rem;
  grid-template-columns: repeat(auto-fit, minmax(190px, 1fr));
}

.copilot-action-chip.tone-enabled {
  border: 1px solid rgba(14, 165, 233, 0.28);
  background: rgba(255, 255, 255, 0.96);
}

.copilot-secondary-details {
  margin-top: 0.35rem;
  border-top: 1px solid rgba(148, 163, 184, 0.18);
  padding-top: 0.8rem;
}

.copilot-secondary-details summary {
  cursor: pointer;
  font-weight: 700;
  color: #0f172a;
}

.copilot-secondary-details-body {
  margin-top: 0.85rem;
}

.copilot-metric-chip {
  padding: 0.7rem 0.75rem;
  border-radius: 12px;
  background: rgba(255, 255, 255, 0.92);
  border: 1px solid rgba(148, 163, 184, 0.2);
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
  .copilot-session-actions,
  .copilot-turn-header,
  .copilot-status-row {
    flex-direction: column;
  }

  .copilot-message {
    grid-template-columns: 1fr;
  }

  .copilot-message-role {
    width: fit-content;
    min-width: 56px;
  }
}
</style>