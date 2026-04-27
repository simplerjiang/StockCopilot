<script setup>
import { computed } from 'vue'
import DOMPurify from 'dompurify'
import { marked } from 'marked'
import { valueToSafeHtml, translateSignal } from '../../../utils/jsonMarkdownService.js'
import RagCitationList from '../../financial/RagCitationList.vue'

const props = defineProps({
  blocks: { type: Array, default: () => [] },
  decision: { type: Object, default: null },
  nextActions: { type: Array, default: () => [] },
  ragCitations: { type: Array, default: () => [] },
  loading: { type: Boolean, default: false },
  error: { type: String, default: null },
  turnSummary: { type: Object, default: null }
})
defineEmits(['action'])

const STAGE_LABELS = ['公司概览', '分析师团队', '研究辩论', '交易方案', '风险评估', '投资决策']

const blockTypeLabel = type => {
  const map = {
    CompanyOverview: '公司概览',
    Market: '市场分析',
    Social: '社交情绪',
    News: '新闻动态',
    Fundamentals: '基本面',
    ResearchDebate: '研究辩论',
    TraderProposal: '交易方案',
    RiskReview: '风险评估',
    PortfolioDecision: '投资决策',
    Product: '产品与业务',
    Shareholder: '股东分析'
  }
  // case-insensitive lookup fallback
  return map[type] ?? map[Object.keys(map).find(k => k.toLowerCase() === (type || '').toLowerCase())] ?? type
}

const blockTypeIcon = type => {
  const map = {
    CompanyOverview: '🏢', Market: '📈', Social: '💬', News: '📰', Fundamentals: '📊',
    ResearchDebate: '⚔️', TraderProposal: '💹', RiskReview: '🛡️', PortfolioDecision: '🎯',
    Product: '🏭', Shareholder: '👥'
  }
  return map[type] ?? map[Object.keys(map).find(k => k.toLowerCase() === (type || '').toLowerCase())] ?? '📋'
}

const statusCls = status => {
  switch (status) {
    case 'Complete': return 'block-complete'
    case 'Degraded': return 'block-degraded'
    case 'Failed': return 'block-failed'
    default: return 'block-pending'
  }
}

const safeHtml = md => {
  if (!md) return ''
  return DOMPurify.sanitize(marked.parse(md, { breaks: true }))
}

const parseJsonArray = json => {
  if (!json) return []
  try {
    const arr = JSON.parse(json)
    return Array.isArray(arr) ? arr : []
  } catch (e) { console.warn('[workbench] JSON parse error:', e); return [] }
}

const confidenceColor = conf => {
  if (!conf && conf !== 0) return 'var(--color-text-secondary)'
  const n = typeof conf === 'number' ? conf : parseFloat(conf)
  if (n >= 0.7) return 'var(--color-success)'
  if (n >= 0.4) return 'var(--color-warning)'
  return 'var(--color-danger)'
}

const ratingLabel = computed(() => {
  if (!props.decision?.rating) return null
  const map = {
    strong_buy: { label: '强烈看好', cls: 'rating-strong-buy' },
    buy: { label: '看好', cls: 'rating-buy' },
    hold: { label: '持有观望', cls: 'rating-hold' },
    sell: { label: '看空', cls: 'rating-sell' },
    strong_sell: { label: '强烈看空', cls: 'rating-strong-sell' }
  }
  return map[props.decision.rating] ?? { label: props.decision.rating, cls: 'rating-hold' }
})

const actionIcon = type => {
  const map = {
    ViewDailyChart: '📊', ViewMinuteChart: '⏱️',
    ViewEvidence: '🔍', ViewLocalFacts: '📂',
    DraftTradingPlan: '📝', FollowUpDispute: '🔁'
  }
  return map[type] ?? '➡️'
}

const routingSummary = computed(() => {
  const summary = props.turnSummary
  if (!summary?.routingDecision && !summary?.continuationMode) return null
  const mode = summary.routingDecision || summary.continuationMode
  const modeLabel = mode === 'PartialRerun' ? '局部重跑' : mode === 'FullRerun' ? '全量重跑' : mode === 'ContinueSession' ? '继续会话' : mode
  const stageIndex = Number(summary.routingStageIndex)
  const stageLabel = Number.isInteger(stageIndex) && stageIndex >= 0 && stageIndex < STAGE_LABELS.length
    ? STAGE_LABELS[stageIndex]
    : ''
  const confidence = summary.routingConfidence != null
    ? `${Math.round(Number(summary.routingConfidence) * 100)}%`
    : ''
  return {
    modeLabel,
    stageText: stageLabel ? `从 ${stageLabel} 开始` : '',
    confidence,
    reason: summary.routingReasoning || ''
  }
})

const SOURCE_LABELS = {
  StockProductMcp: '产品分析',
  CompanyOverviewMcp: '公司概况',
  MarketContextMcp: '市场背景',
  TechnicalMcp: '技术分析',
  StockFundamentalsMcp: '基本面分析',
  FundamentalsMcp: '基本面分析',
  NewsMcp: '新闻工具',
  SocialMcp: '社交舆情',
  SocialSentimentMcp: '社交情绪',
  StockShareholderMcp: '股东分析',
  ShareholderMcp: '股东分析',
  StockAnnouncementMcp: '公告分析',
  AnnouncementMcp: '公告分析',
  StockKlineMcp: 'K线数据',
  StockMinuteMcp: '分时数据',
  StockNewsMcp: '个股新闻',
  StockSearchMcp: '股票搜索',
  StockDetailMcp: '股票详情',
  StockStrategyMcp: '策略分析',
  SectorRotationMcp: '板块轮动'
}

const evidenceLabel = ev => {
  if (typeof ev === 'string') return SOURCE_LABELS[ev] || ev
  if (typeof ev === 'object' && ev !== null) {
    const parts = []
    const rawName = ev.metric || ev.indicator || ev.title || ev.name || ev.label || ev.source || ''
    const name = SOURCE_LABELS[rawName] || rawName
    if (name) parts.push(name)
    const val = ev.value || ev.currentValue || ''
    if (val) parts.push(String(val))
    const assessment = ev.assessment || ev.signal || ev.significance || ''
    if (assessment) parts.push(translateSignal(assessment))
    // Translate source field separately if it wasn't used as the primary name
    if (ev.source && ev.source !== rawName) {
      const translatedSource = SOURCE_LABELS[ev.source] || ev.source
      parts.push(`来源: ${translatedSource}`)
    }
    if (parts.length > 0) return parts.join(' · ')
    return Object.values(ev).filter(v => typeof v === 'string' || typeof v === 'number').join(' · ') || '证据'
  }
  return String(ev)
}
</script>

<template>
  <div class="wb-report">
    <!-- Loading -->
    <div v-if="loading && blocks.length === 0" class="wb-report-loading">
      <span class="pulse-dot" />
      <span>加载研究报告…</span>
    </div>

    <div v-else-if="props.error && blocks.length === 0 && !decision" class="wb-report-empty wb-report-failure">
      <p>研究报告加载失败</p>
      <p class="wb-report-empty-hint">当前会话报告暂时不可用，请点击顶部刷新后重试</p>
    </div>

    <!-- Decision summary (always on top when available) -->
    <div v-if="decision" class="wb-decision">
      <div class="wb-decision-header">
        <span class="wb-decision-icon">🎯</span>
        <span class="wb-decision-title">投资决策</span>
        <span v-if="ratingLabel" :class="['wb-rating', ratingLabel.cls]">
          {{ ratingLabel.label }}
        </span>
      </div>

      <div v-if="decision.executiveSummary" class="wb-decision-summary"
           v-html="safeHtml(decision.executiveSummary)" />

      <div v-if="decision.confidence != null" class="wb-decision-confidence">
        <span class="wb-conf-label">置信度</span>
        <div class="wb-conf-bar">
          <div class="wb-conf-fill"
               :style="{ width: (decision.confidence * 100) + '%', background: confidenceColor(decision.confidence) }" />
        </div>
        <span class="wb-conf-value" :style="{ color: confidenceColor(decision.confidence) }">
          {{ Math.round(decision.confidence * 100) }}%
        </span>
      </div>

      <div v-if="decision.confidenceExplanation" class="wb-decision-explain">
        {{ decision.confidenceExplanation }}
      </div>
    </div>

    <!-- NextActions -->
    <div v-if="nextActions.length > 0" class="wb-actions">
      <div class="wb-actions-title">下一步操作</div>
      <div class="wb-actions-list">
        <button
          v-for="(action, i) in nextActions"
          :key="i"
          class="wb-action-btn"
          :title="action.reasonSummary"
          @click="$emit('action', action)"
        >
          <span class="wb-action-icon">{{ actionIcon(action.actionType) }}</span>
          <span class="wb-action-label">{{ action.label }}</span>
        </button>
      </div>
    </div>

    <div v-if="routingSummary" class="wb-routing-summary">
      <div class="wb-routing-title">追问路由</div>
      <div class="wb-routing-line">
        <span>{{ routingSummary.modeLabel }}</span>
        <span v-if="routingSummary.stageText">{{ routingSummary.stageText }}</span>
        <span v-if="routingSummary.confidence">{{ routingSummary.confidence }}</span>
      </div>
      <div v-if="routingSummary.reason" class="wb-routing-reason">{{ routingSummary.reason }}</div>
    </div>

    <!-- Report blocks -->
    <div v-for="block in blocks" :key="block.id" :class="['wb-block', statusCls(block.status)]">
      <div class="wb-block-header">
        <span class="wb-block-icon">{{ blockTypeIcon(block.blockType) }}</span>
        <span class="wb-block-type">{{ blockTypeLabel(block.blockType) }}</span>
        <span v-if="block.status === 'Degraded'" class="wb-block-badge degraded">降级</span>
        <span v-if="block.status === 'Failed'" class="wb-block-badge failed">失败</span>
      </div>

      <div v-if="block.headline" class="wb-block-headline">{{ block.headline }}</div>
      <div v-if="block.summary" class="wb-block-summary" v-html="valueToSafeHtml(block.summary)" />

      <!-- Key points -->
      <div v-if="parseJsonArray(block.keyPointsJson).length" class="wb-block-section">
        <div class="wb-section-label">关键要点</div>
        <ul class="wb-point-list">
          <li v-for="(pt, i) in parseJsonArray(block.keyPointsJson)" :key="i">
            <template v-if="typeof pt === 'string'">{{ pt }}</template>
            <span v-else v-html="valueToSafeHtml(pt)" />
          </li>
        </ul>
      </div>

      <!-- Risk limits -->
      <div v-if="parseJsonArray(block.riskLimitsJson).length" class="wb-block-section">
        <div class="wb-section-label">风险限制</div>
        <ul class="wb-point-list risk">
          <li v-for="(rl, i) in parseJsonArray(block.riskLimitsJson)" :key="i">
            <template v-if="typeof rl === 'string'">{{ rl }}</template>
            <span v-else v-html="valueToSafeHtml(rl)" />
          </li>
        </ul>
      </div>

      <!-- Invalidation conditions -->
      <div v-if="parseJsonArray(block.invalidationsJson).length" class="wb-block-section">
        <div class="wb-section-label">失效条件</div>
        <ul class="wb-point-list warn">
          <li v-for="(inv, i) in parseJsonArray(block.invalidationsJson)" :key="i">
            <template v-if="typeof inv === 'string'">{{ inv }}</template>
            <span v-else v-html="valueToSafeHtml(inv)" />
          </li>
        </ul>
      </div>

      <!-- Evidence / Counter-evidence -->
      <div v-if="parseJsonArray(block.evidenceRefsJson).length" class="wb-block-section">
        <div class="wb-section-label">支撑证据</div>
        <div class="wb-evidence-tags">
          <span v-for="(ev, i) in parseJsonArray(block.evidenceRefsJson)" :key="i" class="wb-evidence-tag positive">{{ evidenceLabel(ev) }}</span>
        </div>
      </div>
      <div v-if="parseJsonArray(block.counterEvidenceRefsJson).length" class="wb-block-section">
        <div class="wb-section-label">反面证据</div>
        <div class="wb-evidence-tags">
          <span v-for="(ev, i) in parseJsonArray(block.counterEvidenceRefsJson)" :key="i" class="wb-evidence-tag negative">{{ evidenceLabel(ev) }}</span>
        </div>
      </div>
    </div>

    <!-- RAG Citations -->
    <RagCitationList
      v-if="ragCitations.length > 0"
      :citations="ragCitations"
      :show-details="true"
    />

    <!-- Empty state -->
    <div v-if="!loading && !props.error && blocks.length === 0 && !decision" class="wb-report-empty">
      <p>暂无研究报告</p>
      <p class="wb-report-empty-hint">发起研究后，多角色分析结果将在此呈现</p>
    </div>
  </div>
</template>

<style scoped>
.wb-report {
  padding: 8px 12px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.wb-routing-summary {
  border: 1px solid var(--color-border-light);
  border-radius: 6px;
  padding: 8px 10px;
  background: var(--color-bg-surface-alt);
}
.wb-routing-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--color-text-body);
  margin-bottom: 4px;
}
.wb-routing-line {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  font-size: 13px;
  color: var(--color-text-secondary);
}
.wb-routing-reason {
  margin-top: 4px;
  font-size: 12px;
  color: var(--color-text-tertiary);
}

/* ── Decision ──────────────────────────────────── */
.wb-decision {
  background: var(--color-bg-surface-alt);
  border: 1px solid var(--color-border-light);
  border-radius: 6px;
  padding: 10px 12px;
}
.wb-decision-header {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 6px;
}
.wb-decision-icon { font-size: 16px; }
.wb-decision-title { font-size: 15px; font-weight: 600; color: var(--color-text-body); }
.wb-rating {
  font-size: 13px;
  font-weight: 700;
  padding: 1px 8px;
  border-radius: 3px;
  margin-left: auto;
}
.rating-strong-buy { color: #fff; background: var(--color-success); }
.rating-buy { color: var(--color-success); background: var(--color-success-bg); }
.rating-hold { color: var(--color-warning); background: var(--color-warning-bg); }
.rating-sell { color: var(--color-danger); background: var(--color-danger-bg); }
.rating-strong-sell { color: #fff; background: var(--color-danger); }

.wb-decision-summary {
  font-size: 14px;
  color: var(--color-text-body);
  line-height: 1.6;
  margin-bottom: 6px;
}
.wb-decision-summary :deep(p) { margin: 0 0 4px; }

.wb-decision-confidence {
  display: flex;
  align-items: center;
  gap: 8px;
  margin: 6px 0;
}
.wb-conf-label { font-size: 13px; color: var(--color-text-secondary); }
.wb-conf-bar {
  flex: 1;
  height: 4px;
  background: var(--color-bg-surface-alt);
  border-radius: 2px;
  overflow: hidden;
}
.wb-conf-fill {
  height: 100%;
  border-radius: 2px;
  transition: width 0.4s ease;
}
.wb-conf-value { font-size: 14px; font-weight: 600; min-width: 36px; text-align: right; }

.wb-decision-explain {
  font-size: 13px;
  color: var(--color-text-secondary);
  line-height: 1.4;
  font-style: italic;
}

/* ── NextActions ───────────────────────────────── */
.wb-actions {
  padding: 4px 0;
}
.wb-actions-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--color-text-secondary);
  margin-bottom: 6px;
}
.wb-actions-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.wb-action-btn {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 5px 10px;
  border: 1px solid var(--color-border-light);
  border-radius: 5px;
  background: var(--color-bg-surface-alt);
  color: var(--color-text-body);
  font-size: 13px;
  cursor: pointer;
  transition: background 0.15s, border-color 0.15s;
}
.wb-action-btn:hover {
  background: color-mix(in srgb, var(--color-accent) 8%, transparent);
  border-color: var(--color-accent);
}
.wb-action-icon { font-size: 14px; }

/* ── Report blocks ─────────────────────────────── */
.wb-block {
  background: var(--color-bg-surface-alt);
  border: 1px solid var(--color-border-light);
  border-radius: 6px;
  padding: 8px 10px;
}
.wb-block.block-degraded { border-left: 3px solid var(--color-warning); }
.wb-block.block-failed { border-left: 3px solid var(--color-danger); }

.wb-block-header {
  display: flex;
  align-items: center;
  gap: 5px;
  margin-bottom: 4px;
}
.wb-block-icon { font-size: 14px; }
.wb-block-type {
  font-size: 12px;
  font-weight: 600;
  color: var(--color-text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.5px;
}
.wb-block-badge {
  font-size: 11px;
  padding: 0 5px;
  border-radius: 3px;
  font-weight: 700;
  margin-left: auto;
}
.wb-block-badge.degraded { color: var(--color-warning); background: var(--color-warning-bg); }
.wb-block-badge.failed { color: var(--color-danger); background: var(--color-danger-bg); }

.wb-block-headline {
  font-size: 15px;
  font-weight: 600;
  color: var(--color-text-body);
  margin-bottom: 4px;
}
.wb-block-summary {
  font-size: 14px;
  color: var(--color-text-body);
  line-height: 1.5;
  word-wrap: break-word;
  overflow-wrap: break-word;
  white-space: normal;
}
.wb-block-summary :deep(p) { margin: 0 0 4px; }

/* ── Sections ──────────────────────────────────── */
.wb-block-section {
  margin-top: 6px;
}
.wb-section-label {
  font-size: 12px;
  font-weight: 600;
  color: var(--color-text-secondary);
  margin-bottom: 3px;
}
.wb-point-list {
  margin: 0;
  padding-left: 16px;
  font-size: 13px;
  color: var(--color-text-body);
  line-height: 1.5;
}
.wb-point-list.risk { color: var(--color-warning); }
.wb-point-list.warn { color: var(--color-danger); }

.wb-evidence-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}
.wb-evidence-tag {
  font-size: 12px;
  padding: 1px 6px;
  border-radius: 3px;
}
.wb-evidence-tag.positive { background: var(--color-success-bg); color: var(--color-success); }
.wb-evidence-tag.negative { background: var(--color-danger-bg); color: var(--color-danger); }

/* ── States ────────────────────────────────────── */
.wb-report-loading {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 32px 12px;
  color: var(--color-text-secondary);
  font-size: 14px;
}
.pulse-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--color-accent);
  animation: pulse 1.4s infinite ease-in-out;
}
@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.3; }
}

.wb-report-empty {
  text-align: center;
  padding: 24px 12px;
  color: var(--color-text-secondary);
}
.wb-report-empty p { font-size: 14px; margin: 0 0 4px; }
.wb-report-empty-hint { font-size: 13px; }
.wb-report-failure { color: var(--color-danger); }
</style>
