<script setup>
import { computed } from 'vue'
import { ensureMarkdown, markdownToSafeHtml, valueToSafeHtml } from '../../../utils/jsonMarkdownService.js'

const props = defineProps({
  session: { type: Object, default: null }
})

const emit = defineEmits(['view-stock', 'deep-analyze'])

const STAGE_LABELS = {
  MarketScan: '市场扫描',
  SectorDebate: '板块辩论',
  StockPicking: '选股精选',
  StockDebate: '个股辩论',
  FinalDecision: '推荐决策'
}

const getSessionTurns = session => Array.isArray(session?.turns)
  ? session.turns
  : Array.isArray(session?.Turns)
    ? session.Turns
    : []

const getTurnStageSnapshots = turn => Array.isArray(turn?.stageSnapshots)
  ? turn.stageSnapshots
  : Array.isArray(turn?.StageSnapshots)
    ? turn.StageSnapshots
    : []

const TURN_TERMINAL_STATUSES = new Set(['Completed', 'Failed', 'Cancelled'])
const SESSION_TERMINAL_STATUSES = new Set(['Completed', 'Degraded', 'Failed', 'Closed', 'TimedOut'])

const getSessionStatus = session => session?.status ?? session?.Status ?? ''
const getTurnStatus = turn => turn?.status ?? turn?.Status ?? ''

const getTurnSortValue = turn => {
  const turnIndex = Number(turn?.turnIndex ?? turn?.TurnIndex)
  if (Number.isFinite(turnIndex)) return turnIndex

  const requestedAt = Date.parse(turn?.requestedAt ?? turn?.RequestedAt ?? '')
  if (Number.isFinite(requestedAt)) return requestedAt

  const turnId = Number(turn?.id ?? turn?.Id)
  return Number.isFinite(turnId) ? turnId : -1
}

const getActiveTurn = session => {
  const turns = getSessionTurns(session)
    .slice()
    .sort((left, right) => getTurnSortValue(left) - getTurnSortValue(right))
  if (!turns.length) return null

  const activeTurnId = session?.activeTurnId ?? session?.ActiveTurnId ?? null
  return turns.find(turn => (turn.id ?? turn.Id) === activeTurnId)
    ?? turns[turns.length - 1]
}

const getSnapshotRoleStates = snapshot => Array.isArray(snapshot?.roleStates)
  ? snapshot.roleStates
  : Array.isArray(snapshot?.RoleStates)
    ? snapshot.RoleStates
    : []

/** Strip markdown code fences (```json ... ```) from LLM output strings */
const stripCodeFence = (str) => {
  if (typeof str !== 'string') return str
  // 先尝试匹配整体被代码块包裹的情况（宽松正则）
  const m = str.match(/^\s*```(?:\w+)?\s*\n?([\s\S]*?)\n?\s*```\s*$/)
  if (m) return m[1].trim()
  // 处理 "内容: ```json\n{...}\n```" 格式
  const m2 = str.match(/^[^`]*```(?:\w+)?\s*\n([\s\S]*?)\n\s*```\s*$/)
  if (m2) return m2[1].trim()
  return str
}

const parseDirectorOutput = turn => {
  const finalStage = getTurnStageSnapshots(turn)
    .find(snapshot => snapshot?.stageType === 'FinalDecision' || snapshot?.StageType === 'FinalDecision' || snapshot?.stageType === 4 || snapshot?.StageType === 4)
  if (!finalStage) return null

  const director = getSnapshotRoleStates(finalStage)
    .find(roleState => roleState?.roleId === 'recommend_director' || roleState?.RoleId === 'recommend_director')
  const outputContentJson = director?.outputContentJson ?? director?.OutputContentJson
  if (!outputContentJson) return null

  const cleaned = stripCodeFence(outputContentJson)
  return typeof cleaned === 'string'
    ? JSON.parse(cleaned)
    : cleaned
}

const selectedTurn = computed(() => getActiveTurn(props.session))

const isSelectedTurnTerminal = computed(() => {
  const turnStatus = getTurnStatus(selectedTurn.value)
  if (TURN_TERMINAL_STATUSES.has(turnStatus)) return true
  return SESSION_TERMINAL_STATUSES.has(getSessionStatus(props.session))
})

const reportSourceTurns = computed(() => {
  const turns = getSessionTurns(props.session)
    .slice()
    .sort((left, right) => getTurnSortValue(left) - getTurnSortValue(right))
  if (!turns.length) return []

  const activeTurn = selectedTurn.value
  if (!activeTurn) return [...turns].reverse()

  const activeTurnId = activeTurn.id ?? activeTurn.Id
  const ordered = [activeTurn]
  if (isSelectedTurnTerminal.value) return ordered

  return ordered.concat([...turns].reverse().filter(turn => (turn.id ?? turn.Id) !== activeTurnId))
})

const directorOutput = computed(() => {
  for (const turn of reportSourceTurns.value) {
    try {
      const parsed = parseDirectorOutput(turn)
      if (parsed) return parsed
    } catch {
      continue
    }
  }

  return null
})

const report = computed(() => directorOutput.value)

const STAGE_KEYS = ['MarketScan', 'SectorDebate', 'StockPicking', 'StockDebate', 'FinalDecision']

const degradedReport = computed(() => {
  if (report.value) return null
  const turns = reportSourceTurns.value
  if (!turns.length) return null

  const result = { marketSummaries: [], sectorSummaries: [], stockSummaries: [], debateSummaries: [] }
  const stageMap = { MarketScan: 'marketSummaries', SectorDebate: 'sectorSummaries', StockPicking: 'stockSummaries', StockDebate: 'debateSummaries', FinalDecision: 'debateSummaries' }
  const selectedTurnId = selectedTurn.value?.id ?? selectedTurn.value?.Id ?? null

  const ROLE_NAMES = {
    recommend_macro_analyst: '宏观分析师', recommend_sector_hunter: '板块猎手', recommend_smart_money: '资金分析师',
    recommend_sector_bull: '板块多头', recommend_sector_bear: '板块空头', recommend_sector_judge: '板块裁决官',
    recommend_leader_picker: '龙头猎手', recommend_growth_picker: '潜力猎手', recommend_chart_validator: '技术验证师',
    recommend_stock_bull: '个股多头', recommend_stock_bear: '个股空头', recommend_risk_reviewer: '风控审查',
    recommend_director: '推荐总监'
  }

  for (const turn of turns) {
    const snapshots = getTurnStageSnapshots(turn)
    for (const snapshot of snapshots) {
      const stageType = snapshot?.stageType ?? snapshot?.StageType ?? ''
      const bucket = stageMap[stageType] ?? stageMap[STAGE_KEYS[stageType]] ?? null
      if (!bucket) continue

      const roleStates = getSnapshotRoleStates(snapshot)
      for (const rs of roleStates) {
        const roleId = rs?.roleId ?? rs?.RoleId ?? ''
        const outputJson = rs?.outputContentJson ?? rs?.OutputContentJson
        if (!outputJson) continue

        const isFinalDirector = roleId === 'recommend_director'
          && (stageType === 'FinalDecision' || stageType === 4)
        const isCurrentTerminalDirector = isSelectedTurnTerminal.value
          && selectedTurnId != null
          && (turn?.id ?? turn?.Id) === selectedTurnId
          && isFinalDirector

        let summary = ''
        try {
          const cleaned = stripCodeFence(outputJson)
          const parsed = typeof cleaned === 'string' ? JSON.parse(cleaned) : cleaned
          if (typeof parsed === 'object' && parsed !== null) {
            summary = parsed.summary ?? parsed.conclusion ?? parsed.verdict ?? parsed.recommendation ?? ''
            if (!summary) {
              const parts = []
              if (parsed.marketSentiment) parts.push(`市场情绪: ${parsed.marketSentiment}`)
              if (parsed.direction) parts.push(`方向: ${parsed.direction}`)
              if (Array.isArray(parsed.picks) && parsed.picks.length) {
                const pickNames = parsed.picks.map(p => p.name || p.symbol || '').filter(Boolean).join('、')
                if (pickNames) parts.push(`推荐: ${pickNames}`)
              }
              if (Array.isArray(parsed.sectors) && parsed.sectors.length) {
                const sectorNames = parsed.sectors.map(s => s.name || s.sectorName || '').filter(Boolean).join('、')
                if (sectorNames) parts.push(`板块: ${sectorNames}`)
              }
              if (parts.length) {
                summary = parts.join('；')
              } else {
                const firstStr = Object.values(parsed).find(v => typeof v === 'string' && v.length > 10)
                if (firstStr) summary = firstStr
              }
            }
          } else if (typeof parsed === 'string') {
            summary = parsed
          }
        } catch {
          if (isCurrentTerminalDirector) {
            continue
          }

          // outputJson may be "Chinese text + JSON object" mix; extract meaningful content
          const plain = stripCodeFence(typeof outputJson === 'string' ? outputJson : '')
          if (typeof plain === 'string' && plain.length > 0) {
            const firstBrace = plain.indexOf('{')
            const lastBrace = plain.lastIndexOf('}')
            if (firstBrace >= 0 && lastBrace > firstBrace) {
              const textBefore = plain.substring(0, firstBrace).trim()
              const jsonPart = plain.substring(firstBrace, lastBrace + 1)
              try {
                const parsed = JSON.parse(jsonPart)
                const parts = []
                if (parsed.summary) parts.push(parsed.summary)
                if (parsed.conclusion) parts.push(parsed.conclusion)
                if (parsed.verdict) parts.push(parsed.verdict)
                if (parsed.recommendation) parts.push(parsed.recommendation)
                if (parsed.marketSentiment) parts.push(`市场情绪: ${parsed.marketSentiment}`)
                if (parsed.direction) parts.push(`方向: ${parsed.direction}`)
                if (Array.isArray(parsed.picks) && parsed.picks.length) {
                  const pickNames = parsed.picks.map(p => p.name || p.symbol || '').filter(Boolean).join('、')
                  if (pickNames) parts.push(`推荐: ${pickNames}`)
                }
                if (Array.isArray(parsed.candidateSectors) && parsed.candidateSectors.length) {
                  const names = parsed.candidateSectors.map(s => s.name || '').filter(Boolean).join('、')
                  if (names) parts.push(`板块: ${names}`)
                }
                if (Array.isArray(parsed.sectors) && parsed.sectors.length) {
                  const sectorNames = parsed.sectors.map(s => s.name || s.sectorName || '').filter(Boolean).join('、')
                  if (sectorNames) parts.push(`板块: ${sectorNames}`)
                }
                summary = [textBefore, ...parts].filter(Boolean).join('\n')
              } catch {
                // Embedded JSON also invalid, keep only text before JSON
                summary = textBefore || plain.replace(/\{[\s\S]*\}/g, '').trim()
              }
            } else {
              summary = plain
            }
          }
        }

        if (summary) {
          const truncated = summary.length > 200 ? summary.slice(0, 200) + '...' : summary
          result[bucket].push({ role: ROLE_NAMES[roleId] || roleId, summary: truncated, stageLabel: STAGE_LABELS[stageType] || stageType })
        }
      }
    }
    // If we found any data from this turn, stop looking at older turns
    if (result.marketSummaries.length || result.sectorSummaries.length || result.stockSummaries.length || result.debateSummaries.length) break
  }

  const hasContent = result.marketSummaries.length || result.sectorSummaries.length || result.stockSummaries.length || result.debateSummaries.length
  return hasContent ? result : null
})

const sessionStatus = computed(() => {
  const s = props.session
  return s?.status ?? s?.Status ?? ''
})

const isSessionTerminal = computed(() => {
  if (isSelectedTurnTerminal.value) return true
  const status = sessionStatus.value.toLowerCase()
  return ['completed', 'failed', 'degraded', 'closed', 'timedout'].includes(status)
})

const terminalFallbackMessage = computed(() => {
  const turnStatus = getTurnStatus(selectedTurn.value)
  const currentSessionStatus = sessionStatus.value

  if (turnStatus === 'Failed' || turnStatus === 'Cancelled' || currentSessionStatus === 'Failed' || currentSessionStatus === 'TimedOut') {
    return '⚠️ 分析已结束，未能生成可解析的推荐报告，可查看辩论过程或重新推荐。'
  }

  return '⚠️ 分析已完成，但推荐总监报告缺失或无法解析，可重新推荐。'
})

const sentimentClass = computed(() => {
  const s = report.value?.marketSentiment?.toLowerCase?.()
  if (s === 'bullish') return 'sentiment-bull'
  if (s === 'cautious' || s === 'bearish') return 'sentiment-bear'
  return 'sentiment-neutral'
})

const sentimentLabel = computed(() => {
  const s = report.value?.marketSentiment?.toLowerCase?.()
  if (s === 'bullish') return '偏多'
  if (s === 'cautious') return '谨慎'
  if (s === 'bearish') return '偏空'
  return '中性'
})

const confidencePercent = computed(() => {
  const c = Number(report.value?.overallConfidence ?? report.value?.confidence ?? 0)
  return c > 0 && c <= 1 ? Math.round(c * 100) : c > 1 ? Math.round(c) : 0
})

const sectorCards = computed(() => {
  return report.value?.selectedSectors || report.value?.sectorCards || []
})

const stockCards = computed(() => {
  return report.value?.stockCards || report.value?.picks || []
})

const riskNotes = computed(() => {
  return report.value?.riskNotes || report.value?.risks || report.value?.riskWarnings || []
})

const renderHtml = value => valueToSafeHtml(value)

const getPickTypeLabel = type => {
  if (type === 'leader') return '龙头'
  if (type === 'growth') return '潜力'
  if (type === 'buy') return '买入'
  if (type === 'watch') return '观望'
  if (type === 'sell') return '卖出'
  return type || ''
}

const getPickTypeClass = type => {
  if (type === 'leader') return 'pick-leader'
  if (type === 'growth' || type === 'buy') return 'pick-growth'
  if (type === 'watch') return 'pick-watch'
  return ''
}

const getRiskClass = level => {
  const l = (level || '').toLowerCase()
  if (l === 'high') return 'risk-high'
  if (l === 'medium') return 'risk-medium'
  return 'risk-low'
}

const formatValidity = raw => {
  if (!raw) return ''
  const parsed = Date.parse(raw)
  if (!Number.isFinite(parsed)) return `有效期: ${raw}`
  const date = new Date(parsed)
  if (date < new Date()) return `有效期: ${raw} (已过期)`
  return `有效期: ${raw}`
}
</script>

<template>
  <div class="report-card">
    <div v-if="!report && (!degradedReport || !isSessionTerminal)" class="report-empty">
      <p v-if="isSessionTerminal" class="report-error-hint">
        {{ terminalFallbackMessage }}
      </p>
      <p v-else class="muted">推荐报告尚未生成，请等待分析完成。</p>
    </div>
    <!-- Degraded report when Director output unavailable but stage data exists (only shown for terminal sessions) -->
    <div v-else-if="!report && degradedReport && isSessionTerminal" class="degraded-report">
      <div class="degraded-banner">
        ⚠️ 推荐总监未产出结构化报告，以下是各阶段分析摘要
      </div>
      <div v-if="degradedReport.marketSummaries.length" class="degraded-section">
        <h4>市场扫描摘要</h4>
        <div v-for="(item, i) in degradedReport.marketSummaries" :key="'m' + i" class="degraded-item">
          <span class="degraded-role">{{ item.role }}</span>
          <p class="degraded-summary" v-html="renderHtml(item.summary)"></p>
        </div>
      </div>
      <div v-if="degradedReport.sectorSummaries.length" class="degraded-section">
        <h4>板块分析摘要</h4>
        <div v-for="(item, i) in degradedReport.sectorSummaries" :key="'s' + i" class="degraded-item">
          <span class="degraded-role">{{ item.role }}</span>
          <p class="degraded-summary" v-html="renderHtml(item.summary)"></p>
        </div>
      </div>
      <div v-if="degradedReport.stockSummaries.length" class="degraded-section">
        <h4>选股摘要</h4>
        <div v-for="(item, i) in degradedReport.stockSummaries" :key="'p' + i" class="degraded-item">
          <span class="degraded-role">{{ item.role }}</span>
          <p class="degraded-summary" v-html="renderHtml(item.summary)"></p>
        </div>
      </div>
      <div v-if="degradedReport.debateSummaries.length" class="degraded-section">
        <h4>辩论摘要</h4>
        <div v-for="(item, i) in degradedReport.debateSummaries" :key="'d' + i" class="degraded-item">
          <span class="degraded-role">{{ item.role }}</span>
          <p class="degraded-summary" v-html="renderHtml(item.summary)"></p>
        </div>
      </div>
    </div>
    <template v-else>
      <!-- Header: sentiment + confidence + validity -->
      <div class="report-header">
        <span class="report-badge" :class="sentimentClass">{{ sentimentLabel }}</span>
        <span class="report-confidence">置信度 {{ confidencePercent }}%</span>
        <span v-if="report.validityWindow || report.validUntil" class="report-validity">
          {{ formatValidity(report.validityWindow || report.validUntil) }}
        </span>
      </div>

      <!-- Summary -->
      <div v-if="report.summary" class="report-section">
        <p class="report-summary" v-html="renderHtml(report.summary)"></p>
      </div>

      <!-- Sector cards -->
      <div v-if="sectorCards.length" class="report-section">
        <h4>入选板块</h4>
        <div class="sector-grid">
          <article v-for="(sec, i) in sectorCards" :key="i" class="sector-card">
            <div class="sector-card-head">
              <strong>{{ sec.name || sec.sectorName }}</strong>
              <span v-if="sec.changePercent != null" :class="sec.changePercent >= 0 ? 'positive' : 'negative'">
                {{ sec.changePercent >= 0 ? '+' : '' }}{{ Number(sec.changePercent).toFixed(2) }}%
              </span>
              <span v-if="sec.confidence" class="sector-confidence">置信度 {{ sec.confidence }}%</span>
            </div>
            <p v-if="sec.trend" class="sector-trend">趋势: {{ sec.trend }}</p>
            <p v-if="sec.reason || sec.verdictReason" class="sector-reason" v-html="renderHtml(sec.reason || sec.verdictReason)"></p>
            <p v-if="sec.keyRisk || sec.risk" class="sector-risk">风险: {{ sec.keyRisk || sec.risk }}</p>
          </article>
        </div>
      </div>

      <!-- Stock cards -->
      <div v-if="stockCards.length" class="report-section">
        <h4>推荐个股</h4>
        <div class="stock-grid">
          <article v-for="(stk, i) in stockCards" :key="i" class="stock-card">
            <div class="stock-card-head">
              <span v-if="stk.pickType || stk.direction" class="pick-badge" :class="getPickTypeClass(stk.pickType || stk.direction)">
                {{ getPickTypeLabel(stk.pickType || stk.direction) }}
              </span>
              <strong>{{ stk.name }}</strong>
              <small class="muted">{{ stk.symbol }}</small>
              <small v-if="stk.sector" class="muted">· {{ stk.sector }}</small>
            </div>
            <div class="stock-card-metrics">
              <span v-if="stk.technicalScore != null">技术 {{ stk.technicalScore }}</span>
              <span v-if="stk.targetPrice != null">目标价 {{ stk.targetPrice ? stk.targetPrice : '暂无' }}</span>
              <span v-if="stk.stopLoss != null">止损 {{ stk.stopLoss ? stk.stopLoss : '暂无' }}</span>
              <span v-if="stk.supportLevel">支撑 {{ stk.supportLevel }}</span>
              <span v-if="stk.resistanceLevel">压力 {{ stk.resistanceLevel }}</span>
              <span v-if="stk.confidence">置信度 {{ stk.confidence }}%</span>
              <span v-if="stk.riskLevel" :class="getRiskClass(stk.riskLevel)">{{ stk.riskLevel }}</span>
            </div>
            <p v-if="stk.reason || stk.buyLogic" class="stock-reason" v-html="renderHtml(stk.reason || stk.buyLogic)"></p>
            <p v-if="stk.mainRisk" class="stock-risk">风险: {{ stk.mainRisk }}</p>
            <div v-if="stk.triggerCondition" class="stock-conditions">
              <small>触发: {{ stk.triggerCondition }}</small>
            </div>
            <div v-if="stk.invalidCondition" class="stock-conditions">
              <small>失效: {{ stk.invalidCondition }}</small>
            </div>
            <div class="stock-actions">
              <button class="action-btn" @click="emit('view-stock', stk.symbol)">看K线</button>
              <button class="action-btn" @click="emit('deep-analyze', stk.symbol)">深度分析</button>
            </div>
          </article>
        </div>
      </div>

      <!-- Risk notes -->
      <div v-if="riskNotes.length" class="report-section risk-section">
        <h4>风险提示</h4>
        <ul>
          <li v-for="(note, i) in riskNotes" :key="i">{{ typeof note === 'string' ? note : note.description || note.risk || JSON.stringify(note) }}</li>
        </ul>
      </div>

      <!-- Tool usage summary -->
      <div v-if="report.toolUsageSummary" class="report-section">
        <h4>工具调用统计</h4>
        <p v-html="renderHtml(report.toolUsageSummary)"></p>
      </div>
    </template>
  </div>
</template>

<style scoped>
.report-card { display: flex; flex-direction: column; gap: 1rem; }
.report-empty { padding: 2rem; text-align: center; }
.report-error-hint {
  color: var(--color-market-fall, #ef4444);
  font-size: 0.9rem;
  padding: 1rem;
  text-align: center;
  background: rgba(239, 68, 68, 0.08);
  border-radius: var(--radius-md, 8px);
  border: 1px solid rgba(239, 68, 68, 0.2);
}
.report-summary {
  margin: 0;
  font-size: 0.9rem;
  line-height: 1.6;
  color: var(--color-text-primary);
}
.report-header { display: flex; align-items: center; gap: 0.75rem; flex-wrap: wrap; }
.report-badge {
  padding: 0.25rem 0.6rem; border-radius: var(--radius-full);
  font-size: 0.8rem; font-weight: 600;
}
.sentiment-bull { background: var(--color-market-rise-bg, #e6f9e6); color: var(--color-market-rise); }
.sentiment-bear { background: var(--color-market-fall-bg, #fde8e8); color: var(--color-market-fall); }
.sentiment-neutral { background: var(--color-bg-surface-alt); color: var(--color-text-secondary); }
.report-confidence { font-weight: 600; }
.report-validity { color: var(--color-text-secondary); font-size: 0.85rem; }
.report-section { display: flex; flex-direction: column; gap: 0.5rem; }
.report-section h4 { margin: 0; font-size: 0.95rem; }
.sector-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 0.75rem; }
.sector-card {
  padding: 0.75rem; border-radius: var(--radius-md);
  background: var(--color-bg-surface-alt); border: 1px solid var(--color-border-light);
  display: flex; flex-direction: column; gap: 0.35rem;
}
.sector-card-head { display: flex; justify-content: space-between; align-items: center; }
.sector-reason { margin: 0; font-size: 0.85rem; color: var(--color-text-secondary); }
.sector-risk { margin: 0; font-size: 0.8rem; color: var(--color-market-fall); }
.sector-trend { margin: 0; font-size: 0.8rem; color: var(--color-text-secondary); }
.sector-confidence { font-size: 0.8rem; color: var(--color-text-secondary); }
.stock-risk { margin: 0; font-size: 0.8rem; color: var(--color-market-fall); }
.stock-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); gap: 0.75rem; }
.stock-card {
  padding: 0.75rem; border-radius: var(--radius-md);
  background: var(--color-bg-surface-alt); border: 1px solid var(--color-border-light);
  display: flex; flex-direction: column; gap: 0.35rem;
}
.stock-card-head { display: flex; align-items: center; gap: 0.5rem; }
.pick-badge {
  padding: 0.15rem 0.4rem; border-radius: var(--radius-sm);
  font-size: 0.75rem; font-weight: 600;
}
.pick-leader { background: #fef3cd; color: #856404; }
.pick-growth { background: #d4edda; color: #155724; }
.pick-watch { background: #d1ecf1; color: #0c5460; }
.stock-card-metrics { display: flex; flex-wrap: wrap; gap: 0.5rem; font-size: 0.8rem; color: var(--color-text-secondary); }
.stock-reason { margin: 0; font-size: 0.85rem; color: var(--color-text-secondary); }
.stock-conditions { font-size: 0.8rem; color: var(--color-text-secondary); }
.stock-actions { display: flex; gap: 0.5rem; margin-top: 0.25rem; }
.action-btn {
  padding: 0.25rem 0.6rem; border-radius: var(--radius-sm);
  border: 1px solid var(--color-border-light); background: var(--color-bg-surface);
  cursor: pointer; font-size: 0.8rem;
}
.action-btn:hover { background: var(--color-bg-surface-alt); }
.risk-section ul { margin: 0; padding-left: 1.2rem; font-size: 0.85rem; color: var(--color-market-fall); }
.positive { color: var(--color-market-rise); }
.negative { color: var(--color-market-fall); }
.risk-high { color: var(--color-market-fall); font-weight: 600; }
.risk-medium { color: var(--color-accent); }
.risk-low { color: var(--color-market-rise); }

.degraded-report { display: flex; flex-direction: column; gap: 0.75rem; }
.degraded-banner {
  color: #856404;
  font-size: 0.9rem;
  padding: 0.75rem 1rem;
  background: #fff3cd;
  border-radius: var(--radius-md, 8px);
  border: 1px solid #ffc107;
}
.degraded-section { display: flex; flex-direction: column; gap: 0.4rem; }
.degraded-section h4 { margin: 0; font-size: 0.9rem; }
.degraded-item {
  padding: 0.5rem 0.75rem;
  border-radius: var(--radius-sm, 4px);
  background: var(--color-bg-surface-alt);
  border: 1px solid var(--color-border-light);
}
.degraded-role {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--color-text-heading);
}
.degraded-summary {
  margin: 0.25rem 0 0;
  font-size: 0.85rem;
  color: var(--color-text-secondary);
  line-height: 1.5;
}
</style>
