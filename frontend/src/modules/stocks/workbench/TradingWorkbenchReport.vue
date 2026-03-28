<script setup>
import { computed } from 'vue'
import DOMPurify from 'dompurify'
import { marked } from 'marked'

const props = defineProps({
  blocks: { type: Array, default: () => [] },
  decision: { type: Object, default: null },
  nextActions: { type: Array, default: () => [] },
  loading: { type: Boolean, default: false }
})
defineEmits(['action'])

const blockTypeLabel = type => {
  const map = {
    Market: '市场分析',
    Social: '社交情绪',
    News: '新闻动态',
    Fundamentals: '基本面',
    ResearchDebate: '研究辩论',
    TraderProposal: '交易方案',
    RiskReview: '风险评估',
    PortfolioDecision: '投资决策'
  }
  return map[type] ?? type
}

const blockTypeIcon = type => {
  const map = {
    Market: '📈', Social: '💬', News: '📰', Fundamentals: '📊',
    ResearchDebate: '⚔️', TraderProposal: '💹', RiskReview: '🛡️', PortfolioDecision: '🎯'
  }
  return map[type] ?? '📋'
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
  if (!conf && conf !== 0) return '#8b8fa3'
  const n = typeof conf === 'number' ? conf : parseFloat(conf)
  if (n >= 0.7) return '#66bb6a'
  if (n >= 0.4) return '#f0b429'
  return '#ef5350'
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
</script>

<template>
  <div class="wb-report">
    <!-- Loading -->
    <div v-if="loading && blocks.length === 0" class="wb-report-loading">
      <span class="pulse-dot" />
      <span>加载研究报告…</span>
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

    <!-- Report blocks -->
    <div v-for="block in blocks" :key="block.id" :class="['wb-block', statusCls(block.status)]">
      <div class="wb-block-header">
        <span class="wb-block-icon">{{ blockTypeIcon(block.blockType) }}</span>
        <span class="wb-block-type">{{ blockTypeLabel(block.blockType) }}</span>
        <span v-if="block.status === 'Degraded'" class="wb-block-badge degraded">降级</span>
        <span v-if="block.status === 'Failed'" class="wb-block-badge failed">失败</span>
      </div>

      <div v-if="block.headline" class="wb-block-headline">{{ block.headline }}</div>
      <div v-if="block.summary" class="wb-block-summary" v-html="safeHtml(block.summary)" />

      <!-- Key points -->
      <div v-if="parseJsonArray(block.keyPointsJson).length" class="wb-block-section">
        <div class="wb-section-label">关键要点</div>
        <ul class="wb-point-list">
          <li v-for="(pt, i) in parseJsonArray(block.keyPointsJson)" :key="i">{{ typeof pt === 'string' ? pt : JSON.stringify(pt) }}</li>
        </ul>
      </div>

      <!-- Risk limits -->
      <div v-if="parseJsonArray(block.riskLimitsJson).length" class="wb-block-section">
        <div class="wb-section-label">风险限制</div>
        <ul class="wb-point-list risk">
          <li v-for="(rl, i) in parseJsonArray(block.riskLimitsJson)" :key="i">{{ typeof rl === 'string' ? rl : JSON.stringify(rl) }}</li>
        </ul>
      </div>

      <!-- Invalidation conditions -->
      <div v-if="parseJsonArray(block.invalidationsJson).length" class="wb-block-section">
        <div class="wb-section-label">失效条件</div>
        <ul class="wb-point-list warn">
          <li v-for="(inv, i) in parseJsonArray(block.invalidationsJson)" :key="i">{{ typeof inv === 'string' ? inv : JSON.stringify(inv) }}</li>
        </ul>
      </div>

      <!-- Evidence / Counter-evidence -->
      <div v-if="parseJsonArray(block.evidenceRefsJson).length" class="wb-block-section">
        <div class="wb-section-label">支撑证据</div>
        <div class="wb-evidence-tags">
          <span v-for="(ev, i) in parseJsonArray(block.evidenceRefsJson)" :key="i" class="wb-evidence-tag positive">{{ typeof ev === 'string' ? ev : JSON.stringify(ev) }}</span>
        </div>
      </div>
      <div v-if="parseJsonArray(block.counterEvidenceRefsJson).length" class="wb-block-section">
        <div class="wb-section-label">反面证据</div>
        <div class="wb-evidence-tags">
          <span v-for="(ev, i) in parseJsonArray(block.counterEvidenceRefsJson)" :key="i" class="wb-evidence-tag negative">{{ typeof ev === 'string' ? ev : JSON.stringify(ev) }}</span>
        </div>
      </div>
    </div>

    <!-- Empty state -->
    <div v-if="!loading && blocks.length === 0 && !decision" class="wb-report-empty">
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

/* ── Decision ──────────────────────────────────── */
.wb-decision {
  background: var(--wb-card-bg, rgba(255,255,255,0.03));
  border: 1px solid var(--wb-border, #2a2d35);
  border-radius: 6px;
  padding: 10px 12px;
}
.wb-decision-header {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 6px;
}
.wb-decision-icon { font-size: 14px; }
.wb-decision-title { font-size: 13px; font-weight: 600; color: var(--wb-text, #e1e4ea); }
.wb-rating {
  font-size: 11px;
  font-weight: 700;
  padding: 1px 8px;
  border-radius: 3px;
  margin-left: auto;
}
.rating-strong-buy { color: #fff; background: #2e7d32; }
.rating-buy { color: #66bb6a; background: rgba(102,187,106,0.15); }
.rating-hold { color: #f0b429; background: rgba(240,180,41,0.12); }
.rating-sell { color: #ef5350; background: rgba(239,83,80,0.12); }
.rating-strong-sell { color: #fff; background: #c62828; }

.wb-decision-summary {
  font-size: 12px;
  color: var(--wb-text, #e1e4ea);
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
.wb-conf-label { font-size: 11px; color: var(--wb-text-muted, #8b8fa3); }
.wb-conf-bar {
  flex: 1;
  height: 4px;
  background: rgba(255,255,255,0.06);
  border-radius: 2px;
  overflow: hidden;
}
.wb-conf-fill {
  height: 100%;
  border-radius: 2px;
  transition: width 0.4s ease;
}
.wb-conf-value { font-size: 12px; font-weight: 600; min-width: 36px; text-align: right; }

.wb-decision-explain {
  font-size: 11px;
  color: var(--wb-text-muted, #8b8fa3);
  line-height: 1.4;
  font-style: italic;
}

/* ── NextActions ───────────────────────────────── */
.wb-actions {
  padding: 4px 0;
}
.wb-actions-title {
  font-size: 11px;
  font-weight: 600;
  color: var(--wb-text-muted, #8b8fa3);
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
  border: 1px solid var(--wb-border, #2a2d35);
  border-radius: 5px;
  background: var(--wb-card-bg, rgba(255,255,255,0.03));
  color: var(--wb-text, #e1e4ea);
  font-size: 11px;
  cursor: pointer;
  transition: background 0.15s, border-color 0.15s;
}
.wb-action-btn:hover {
  background: rgba(91, 156, 246, 0.08);
  border-color: var(--wb-accent, #5b9cf6);
}
.wb-action-icon { font-size: 12px; }

/* ── Report blocks ─────────────────────────────── */
.wb-block {
  background: var(--wb-card-bg, rgba(255,255,255,0.02));
  border: 1px solid var(--wb-border, #2a2d35);
  border-radius: 6px;
  padding: 8px 10px;
}
.wb-block.block-degraded { border-left: 3px solid #f0b429; }
.wb-block.block-failed { border-left: 3px solid #ef5350; }

.wb-block-header {
  display: flex;
  align-items: center;
  gap: 5px;
  margin-bottom: 4px;
}
.wb-block-icon { font-size: 12px; }
.wb-block-type {
  font-size: 10px;
  font-weight: 600;
  color: var(--wb-text-muted, #8b8fa3);
  text-transform: uppercase;
  letter-spacing: 0.5px;
}
.wb-block-badge {
  font-size: 9px;
  padding: 0 5px;
  border-radius: 3px;
  font-weight: 700;
  margin-left: auto;
}
.wb-block-badge.degraded { color: #f0b429; background: rgba(240,180,41,0.12); }
.wb-block-badge.failed { color: #ef5350; background: rgba(239,83,80,0.12); }

.wb-block-headline {
  font-size: 13px;
  font-weight: 600;
  color: var(--wb-text, #e1e4ea);
  margin-bottom: 4px;
}
.wb-block-summary {
  font-size: 12px;
  color: var(--wb-text, #e1e4ea);
  line-height: 1.5;
}
.wb-block-summary :deep(p) { margin: 0 0 4px; }

/* ── Sections ──────────────────────────────────── */
.wb-block-section {
  margin-top: 6px;
}
.wb-section-label {
  font-size: 10px;
  font-weight: 600;
  color: var(--wb-text-muted, #8b8fa3);
  margin-bottom: 3px;
}
.wb-point-list {
  margin: 0;
  padding-left: 16px;
  font-size: 11px;
  color: var(--wb-text, #e1e4ea);
  line-height: 1.5;
}
.wb-point-list.risk { color: #f0b429; }
.wb-point-list.warn { color: #ef5350; }

.wb-evidence-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}
.wb-evidence-tag {
  font-size: 10px;
  padding: 1px 6px;
  border-radius: 3px;
}
.wb-evidence-tag.positive { background: rgba(102,187,106,0.1); color: #66bb6a; }
.wb-evidence-tag.negative { background: rgba(239,83,80,0.1); color: #ef5350; }

/* ── States ────────────────────────────────────── */
.wb-report-loading {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 32px 12px;
  color: var(--wb-text-muted, #8b8fa3);
  font-size: 12px;
}
.pulse-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--wb-accent, #5b9cf6);
  animation: pulse 1.4s infinite ease-in-out;
}
@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.3; }
}

.wb-report-empty {
  text-align: center;
  padding: 24px 12px;
  color: var(--wb-text-muted, #8b8fa3);
}
.wb-report-empty p { font-size: 12px; margin: 0 0 4px; }
.wb-report-empty-hint { font-size: 11px; }
</style>
