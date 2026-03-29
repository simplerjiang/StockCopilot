<script setup>
import { ref, computed, nextTick, watch } from 'vue'
import DOMPurify from 'dompurify'
import { marked } from 'marked'
import { ensureMarkdown, markdownToSafeHtml } from '../../../utils/jsonMarkdownService.js'

const props = defineProps({
  items: { type: Array, default: () => [] },
  activeTurn: { type: Object, default: null },
  isRunning: { type: Boolean, default: false },
  currentStage: { type: String, default: null }
})

const feedEnd = ref(null)
const collapsedItems = ref(new Set())
const expandedTools = ref(new Set())

const toggleCollapse = idx => {
  const s = new Set(collapsedItems.value)
  s.has(idx) ? s.delete(idx) : s.add(idx)
  collapsedItems.value = s
}

const toggleToolExpand = id => {
  const s = new Set(expandedTools.value)
  s.has(id) ? s.delete(id) : s.add(id)
  expandedTools.value = s
}

const parseDetailJson = item => {
  const raw = item.metadataJson || item.detailJson
  if (!raw) return null
  try { return JSON.parse(raw) } catch { return null }
}

const formatToolDetail = item => {
  const data = parseDetailJson(item)
  if (!data) return null
  const sections = []
  if (data.toolName) sections.push({ label: '工具', value: MCP_TOOL_LABELS[data.toolName] || data.toolName })
  if (data.status) sections.push({ label: '状态', value: data.status === 'Completed' ? '已完成' : data.status === 'Running' ? '执行中' : data.status === 'Failed' ? '失败' : data.status })
  if (data.symbol) sections.push({ label: '标的', value: data.symbol })
  if (data.summary) sections.push({ label: '摘要', value: data.summary })
  if (data.resultPreview) {
    const readable = ensureMarkdown(data.resultPreview)
    sections.push({ label: '返回数据', value: readable, isLarge: true, isHtml: true })
  }
  if (sections.length === 0) {
    for (const [key, val] of Object.entries(data)) {
      sections.push({ label: key, value: typeof val === 'object' ? ensureMarkdown(val) : String(val), isHtml: typeof val === 'object' })
    }
  }
  return sections
}

const isPlaceholderSummary = item => {
  const c = getContent(item)
  return c && c.includes('分析完成') && c.includes('详见研究报告') && !parseRoleReport(item)
}

const parseRoleReport = item => {
  const raw = item.metadataJson || item.detailJson
  if (!raw) return null
  const content = getContent(item)
  if (content && !content.includes('分析完成') && !content.includes('详见研究报告') && content.length > 100) return null
  try {
    const parsed = JSON.parse(raw)
    if (typeof parsed === 'object') return ensureMarkdown(parsed)
    return String(parsed)
  } catch {
    return raw
  }
}

const safeHtml = md => {
  if (!md) return ''
  return DOMPurify.sanitize(marked.parse(md, { breaks: true }))
}

/** Role visual config: avatar icon, bubble color, alignment. */
const roleConfig = roleId => {
  const id = (roleId || '').toLowerCase().replace(/_/g, '')
  if (id.includes('market')) return { avatar: '📈', color: '#2b4a7a', name: '市场分析师' }
  if (id.includes('social') || id.includes('sentiment')) return { avatar: '💬', color: '#2b4a6a', name: '情绪分析师' }
  if (id.includes('news')) return { avatar: '📰', color: '#2b4a5a', name: '新闻分析师' }
  if (id.includes('fundamental')) return { avatar: '📊', color: '#2b3a6a', name: '基本面分析师' }
  if (id.includes('shareholder')) return { avatar: '👥', color: '#2b3a5a', name: '股东分析师' }
  if (id.includes('product')) return { avatar: '🏭', color: '#2b4a4a', name: '产品分析师' }
  if (id.includes('companyoverview')) return { avatar: '🏢', color: '#2b3a4a', name: '公司概览' }
  if (id.includes('bull')) return { avatar: '🐂', color: '#1a4a2a', name: '多方研究员' }
  if (id.includes('bear')) return { avatar: '🐻', color: '#5a1a1a', name: '空方研究员' }
  if (id.includes('researchmanager')) return { avatar: '👔', color: '#4a3a1a', name: '研究经理' }
  if (id.includes('trader')) return { avatar: '💹', color: '#3a1a4a', name: '交易员' }
  if (id.includes('aggressive')) return { avatar: '🔥', color: '#4a2a1a', name: '激进风控' }
  if (id.includes('neutral')) return { avatar: '⚖️', color: '#2a3a4a', name: '中性风控' }
  if (id.includes('conservative')) return { avatar: '🛡️', color: '#1a2a4a', name: '保守风控' }
  if (id.includes('portfolio')) return { avatar: '🎯', color: '#4a3a0a', name: '组合经理' }
  return { avatar: '🤖', color: '#2a2d35', name: roleId || '系统' }
}

/** Classify item for rendering. */
const itemKind = item => {
  const t = (item.type || item.feedType || item.itemType || '').toLowerCase()
  if (t.includes('stagetransition') || t.includes('stagestarted') || t.includes('stagecompleted') || t.includes('stagefailed')) return 'divider'
  if (t.includes('tooldispatched') || t.includes('toolcompleted') || t.includes('toolprogress') || t.includes('toolevent')) return 'tool'
  if (t.includes('userfollowup') || t.includes('turnstarted')) return 'user'
  if (t.includes('system') || t.includes('degraded') || t.includes('retryattempt')) return 'system'

  // Demote lifecycle status messages (Started/Completed) to compact style
  const rawContent = item.summary || item.message || item.content || ''
  if (/^Role \S+ (started|Completed|Degraded|Running|LLM ready)$/i.test(rawContent)) return 'lifecycle'

  return 'role'
}

const formatTime = ts => {
  if (!ts) return ''
  const d = new Date(ts)
  return d.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
}

const stageLabel = summary => {
  if (!summary) return ''
  const map = {
    CompanyOverviewPreflight: '🏢 公司概览',
    AnalystTeam: '📊 分析师团队',
    ResearchDebate: '⚔️ 研究辩论',
    TraderProposal: '💹 交易方案',
    RiskDebate: '🛡️ 风险评估',
    PortfolioDecision: '🎯 投资决策'
  }
  for (const [k, v] of Object.entries(map)) {
    if (summary.includes(k)) return v
  }
  return translateMcpNames(summary)
}

const getRawContent = item => item.summary || item.message || item.content || ''

const getContent = item => {
  let text = item.summary || item.message || item.content || ''
  // Translate MCP tool names in content text
  return translateMcpNames(text)
}

/** Map of MCP tool names to Chinese labels. */
const MCP_TOOL_LABELS = {
  CompanyOverviewMcp: '公司概况工具',
  MarketContextMcp: '市场背景工具',
  TechnicalMcp: '技术分析工具',
  StockFundamentalsMcp: '基本面工具',
  FundamentalsMcp: '基本面工具',
  NewsMcp: '新闻工具',
  SocialMcp: '社交舆情工具',
  SocialSentimentMcp: '社交情绪工具',
  StockShareholderMcp: '股东分析工具',
  ShareholderMcp: '股东分析工具',
  StockAnnouncementMcp: '公告分析工具',
  AnnouncementMcp: '公告分析工具',
  StockProductMcp: '产品分析工具',
  StockKlineMcp: 'K线数据工具',
  StockMinuteMcp: '分时数据工具',
  StockNewsMcp: '个股新闻工具',
  StockSearchMcp: '股票搜索工具',
  StockDetailMcp: '股票详情工具',
  StockStrategyMcp: '策略分析工具',
  SectorRotationMcp: '板块轮动工具'
}
/** Map of role IDs (snake_case) to Chinese labels for content text replacement. */
const ROLE_ID_LABELS = {
  company_overview_analyst: '公司概览',
  market_analyst: '市场分析师',
  social_sentiment_analyst: '情绪分析师',
  news_analyst: '新闻分析师',
  fundamentals_analyst: '基本面分析师',
  shareholder_analyst: '股东分析师',
  product_analyst: '产品分析师',
  bull_researcher: '多方研究员',
  bear_researcher: '空方研究员',
  research_manager: '研究经理',
  trader: '交易员',
  aggressive_risk_analyst: '激进风控',
  neutral_risk_analyst: '中性风控',
  conservative_risk_analyst: '保守风控',
  portfolio_manager: '组合经理'
}

const _mcpPattern = new RegExp(Object.keys(MCP_TOOL_LABELS).join('|'), 'g')
const _rolePattern = new RegExp(Object.keys(ROLE_ID_LABELS).filter(k => k.length > 5).join('|'), 'g')

/** Common English patterns in feed content → Chinese. */
const FEED_TEXT_PATTERNS = [
  [/\bRetrying\b/gi, '重试'],
  [/\battempt\s+(\d+)/gi, '第$1次'],
  [/\bRole\s+/gi, ''],
  [/\bstarted\b/gi, '开始'],
  [/\bCompleted\b/gi, '完成'],
  [/\bDegraded\b/gi, '降级完成'],
  [/\bRunning\b/gi, '执行中'],
  [/\bfailed after retries\b/gi, '重试后失败'],
  [/\bFailed\b/gi, '失败'],
  [/\bLLM ready\b/gi, 'LLM就绪']
]

/** Replace MCP tool names, role IDs, and common English patterns with Chinese. */
const translateMcpNames = text => {
  if (!text) return text
  let result = text.replace(_mcpPattern, m => MCP_TOOL_LABELS[m] || m)
  result = result.replace(_rolePattern, m => ROLE_ID_LABELS[m] || m)
  for (const [pattern, replacement] of FEED_TEXT_PATTERNS) {
    result = result.replace(pattern, replacement)
  }
  return result
}

const isLongContent = content => content.length > 800

/** Group feed items by turnId. */
const groupByTurn = computed(() => {
  const groups = []
  let currentTurn = null
  let currentGroup = null
  for (const item of props.items) {
    const tid = item.turnId ?? item.turn_id ?? 0
    if (tid !== currentTurn) {
      currentTurn = tid
      currentGroup = { turnId: tid, items: [] }
      groups.push(currentGroup)
    }
    currentGroup.items.push(item)
  }
  return groups
})

// Auto-scroll to bottom when new items arrive
watch(() => props.items.length, () => {
  nextTick(() => { feedEnd.value?.scrollIntoView({ behavior: 'smooth', block: 'end' }) })
})
</script>

<template>
  <div class="wb-feed-chat">
    <template v-if="items.length > 0">
      <div
        v-for="group in groupByTurn"
        :key="group.turnId"
        class="feed-turn-group"
      >
        <!-- Turn header card -->
        <div class="feed-turn-header">
          <span class="turn-badge">Turn {{ group.turnId }}</span>
        </div>

        <template v-for="(item, idx) in group.items" :key="idx">
          <!-- Stage divider -->
          <div v-if="itemKind(item) === 'divider'" class="feed-divider">
            <span class="feed-divider-line" />
            <span class="feed-divider-text">{{ stageLabel(getContent(item)) || getContent(item) }}</span>
            <span class="feed-divider-line" />
          </div>

          <!-- Tool event (compact system-style, expandable) -->
          <div v-else-if="itemKind(item) === 'tool'" class="feed-tool-wrap">
            <div class="feed-tool" :class="{ 'feed-tool-expandable': !!parseDetailJson(item) }" @click="parseDetailJson(item) && toggleToolExpand(item.id || idx)">
              <span class="feed-tool-icon">🔧</span>
              <span class="feed-tool-text">{{ getContent(item) }}</span>
              <span v-if="parseDetailJson(item)" class="feed-tool-chevron">{{ expandedTools.has(item.id || idx) ? '▾' : '▸' }}</span>
              <span class="feed-tool-time">{{ formatTime(item.timestamp || item.createdAt) }}</span>
            </div>
            <div v-if="expandedTools.has(item.id || idx) && formatToolDetail(item)" class="feed-tool-detail">
              <template v-for="(sec, si) in formatToolDetail(item)" :key="si">
                <div v-if="!sec.isLarge" class="feed-tool-detail-row">
                  <span class="feed-tool-detail-key">{{ sec.label }}:</span>
                  <span v-if="sec.isHtml" class="feed-tool-detail-val" v-html="markdownToSafeHtml(sec.value)" />
                  <span v-else class="feed-tool-detail-val">{{ sec.value }}</span>
                </div>
                <div v-else class="feed-tool-detail-large">
                  <div class="feed-tool-detail-key">{{ sec.label }}:</div>
                  <div v-if="sec.isHtml" class="feed-tool-detail-rendered" v-html="markdownToSafeHtml(sec.value)" />
                  <pre v-else class="feed-tool-detail-pre">{{ sec.value }}</pre>
                </div>
              </template>
            </div>
          </div>

          <!-- System / retry / degraded notice -->
          <div v-else-if="itemKind(item) === 'system'" class="feed-system">
            <span class="feed-system-icon">{{ (item.type || item.feedType || '').toLowerCase().includes('retry') ? '🔄' : 'ℹ️' }}</span>
            <span class="feed-system-text">{{ getContent(item) }}</span>
            <span class="feed-system-time">{{ formatTime(item.timestamp || item.createdAt) }}</span>
          </div>

          <!-- Lifecycle status (compact, dimmed) -->
          <div v-else-if="itemKind(item) === 'lifecycle'" class="feed-lifecycle">
            <span class="feed-lifecycle-dot">•</span>
            <span class="feed-lifecycle-text">{{ roleConfig(item.roleId || item.role_id).name }} {{ getRawContent(item).includes('started') ? '开始分析' : getRawContent(item).includes('Completed') ? '分析完成' : getRawContent(item).includes('Degraded') ? '降级完成' : '' }}</span>
            <span class="feed-lifecycle-time">{{ formatTime(item.timestamp || item.createdAt) }}</span>
          </div>

          <!-- User follow-up (right aligned) -->
          <div v-else-if="itemKind(item) === 'user'" class="feed-msg feed-msg-user">
            <div class="feed-bubble feed-bubble-user">
              <div class="feed-bubble-content" v-html="safeHtml(getContent(item))" />
              <div class="feed-bubble-time">{{ formatTime(item.timestamp || item.createdAt) }}</div>
            </div>
            <div class="feed-avatar feed-avatar-user">👤</div>
          </div>

          <!-- Role message (left aligned chat bubble) -->
          <div v-else class="feed-msg feed-msg-role">
            <div class="feed-avatar" :style="{ background: roleConfig(item.roleId || item.role_id).color }">
              {{ roleConfig(item.roleId || item.role_id).avatar }}
            </div>
            <div class="feed-bubble-wrap">
              <div class="feed-bubble-name">
                {{ roleConfig(item.roleId || item.role_id).name }}
                <span class="feed-bubble-time-inline">{{ formatTime(item.timestamp || item.createdAt) }}</span>
              </div>
              <div class="feed-bubble feed-bubble-role" :style="{ '--bubble-bg': roleConfig(item.roleId || item.role_id).color }">
                <div v-if="isPlaceholderSummary(item)" class="feed-bubble-content feed-placeholder-notice">
                  ✅ 分析完成
                </div>
                <template v-else>
                  <div
                    :class="['feed-bubble-content', { collapsed: isLongContent(getContent(item)) && !collapsedItems.has(`${group.turnId}-${idx}`) }]"
                    v-html="safeHtml(getContent(item))"
                  />
                  <button
                    v-if="isLongContent(getContent(item))"
                    class="feed-collapse-btn"
                    @click="toggleCollapse(`${group.turnId}-${idx}`)"
                  >
                    {{ collapsedItems.has(`${group.turnId}-${idx}`) ? '收起' : '展开全部' }}
                  </button>
                  <!-- Expandable full report when summary is a placeholder -->
                  <div v-if="parseRoleReport(item)" class="feed-report-expand">
                    <button class="feed-report-toggle" @click="toggleCollapse(`report-${group.turnId}-${idx}`)">
                      {{ collapsedItems.has(`report-${group.turnId}-${idx}`) ? '收起研究报告 ▾' : '查看研究报告 ▸' }}
                    </button>
                    <div v-if="collapsedItems.has(`report-${group.turnId}-${idx}`)" class="feed-report-content" v-html="safeHtml(parseRoleReport(item))" />
                  </div>
                </template>
              </div>
            </div>
          </div>
        </template>
      </div>
    </template>

    <div v-else class="feed-empty">
      <p>暂无讨论动态</p>
    </div>

    <!-- Typing indicator -->
    <div v-if="isRunning" class="feed-typing">
      <span class="feed-typing-dots"><span /><span /><span /></span>
      <span class="feed-typing-text">{{ currentStage ? `${currentStage} 分析中...` : '研究进行中...' }}</span>
    </div>

    <div ref="feedEnd" />
  </div>
</template>

<style scoped>
.wb-feed-chat {
  padding: 10px 12px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

/* ── Turn header ──────────────────────────────── */
.feed-turn-group { display: flex; flex-direction: column; gap: 4px; }
.feed-turn-header { text-align: center; margin: 8px 0 4px; }
.turn-badge {
  font-size: 12px; font-weight: 600; color: #b09cf6;
  background: rgba(176,156,246,0.1); padding: 2px 10px;
  border-radius: 10px; font-family: 'Consolas', monospace;
}

/* ── Stage divider ────────────────────────────── */
.feed-divider {
  display: flex; align-items: center; gap: 8px;
  margin: 8px 0 4px; font-size: 13px; color: var(--wb-text-muted, #8b8fa3);
}
.feed-divider-line { flex: 1; height: 1px; background: var(--wb-border, #2a2d35); }
.feed-divider-text { white-space: nowrap; font-weight: 500; }

/* ── Tool event (compact) ─────────────────────── */
.feed-tool-wrap { padding: 0; }
.feed-tool, .feed-system {
  display: flex; align-items: center; gap: 4px;
  padding: 2px 12px; font-size: 13px;
  color: var(--wb-text-muted, #8b8fa3);
}
.feed-tool-expandable { cursor: pointer; }
.feed-tool-expandable:hover { color: var(--wb-text, #e1e4ea); }
.feed-tool-icon, .feed-system-icon { font-size: 12px; flex-shrink: 0; }
.feed-tool-text, .feed-system-text { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.feed-tool-chevron { font-size: 10px; opacity: 0.6; flex-shrink: 0; }
.feed-tool-time, .feed-system-time { font-size: 12px; flex-shrink: 0; opacity: 0.6; }
.feed-tool-detail {
  margin: 2px 24px 4px;
  padding: 4px 8px;
  background: rgba(255,255,255,0.03);
  border-left: 2px solid var(--wb-accent, #5b9cf6);
  border-radius: 0 4px 4px 0;
  font-size: 12px;
  color: var(--wb-text-muted, #8b8fa3);
}
.feed-tool-detail-row { display: flex; gap: 6px; line-height: 1.5; }
.feed-tool-detail-key { color: var(--wb-accent, #5b9cf6); font-weight: 500; white-space: nowrap; }
.feed-tool-detail-val { word-break: break-all; }
.feed-tool-detail-large { margin-top: 4px; }
.feed-tool-detail-pre {
  margin: 4px 0 0;
  padding: 6px 8px;
  background: rgba(0,0,0,0.2);
  border-radius: 4px;
  font-size: 11px;
  color: var(--wb-text, #e1e4ea);
  white-space: pre-wrap;
  word-break: break-all;
  max-height: 300px;
  overflow-y: auto;
  font-family: 'Consolas', 'Monaco', monospace;
}
.feed-tool-detail-rendered {
  font-size: 12px;
  color: #c8ccd4;
  padding: 6px 8px;
  background: rgba(0,0,0,0.15);
  border-radius: 4px;
  max-height: 300px;
  overflow-y: auto;
  line-height: 1.5;
}
.feed-tool-detail-rendered :deep(ul) { padding-left: 16px; margin: 4px 0; }
.feed-tool-detail-rendered :deep(li) { margin: 2px 0; }
.feed-tool-detail-rendered :deep(strong) { color: #82aaff; }

/* ── Lifecycle (dimmed compact) ───────────────── */
.feed-lifecycle {
  display: flex; align-items: center; gap: 4px;
  padding: 1px 12px; font-size: 12px;
  color: var(--wb-text-muted, #8b8fa3);
  opacity: 0.5;
}
.feed-lifecycle-dot { font-size: 10px; flex-shrink: 0; }
.feed-lifecycle-text { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.feed-lifecycle-time { font-size: 11px; flex-shrink: 0; opacity: 0.5; }

/* ── Message row ──────────────────────────────── */
.feed-msg { display: flex; gap: 8px; margin: 3px 0; align-items: flex-start; }
.feed-msg-user { flex-direction: row-reverse; }
.feed-msg-role { flex-direction: row; }

/* ── Avatar ───────────────────────────────────── */
.feed-avatar {
  width: 30px; height: 30px; border-radius: 50%;
  display: flex; align-items: center; justify-content: center;
  font-size: 15px; flex-shrink: 0;
  background: var(--wb-card-bg, #2a2d35);
}
.feed-avatar-user { background: #3a4a5a; }

/* ── Bubble ───────────────────────────────────── */
.feed-bubble-wrap { flex: 1; min-width: 0; max-width: 85%; }
.feed-bubble-name {
  font-size: 13px; font-weight: 600; margin-bottom: 2px;
  color: var(--wb-text-muted, #8b8fa3);
}
.feed-bubble-time-inline { font-weight: 400; font-size: 12px; opacity: 0.6; margin-left: 6px; }

.feed-bubble {
  padding: 8px 12px; border-radius: 12px;
  font-size: 15px; line-height: 1.6; word-break: break-word;
}
.feed-bubble-role {
  background: var(--bubble-bg, #2a2d35);
  color: var(--wb-text, #e1e4ea);
  border-top-left-radius: 4px;
}
.feed-bubble-user {
  background: #2a4a6a;
  color: #fff;
  border-top-right-radius: 4px;
  margin-left: auto; max-width: 85%;
}

.feed-bubble-content { overflow: hidden; }
.feed-bubble-content.collapsed { max-height: 200px; -webkit-mask-image: linear-gradient(to bottom, #000 60%, transparent 100%); mask-image: linear-gradient(to bottom, #000 60%, transparent 100%); }
.feed-bubble-content :deep(p) { margin: 0 0 4px; }
.feed-bubble-content :deep(ul), .feed-bubble-content :deep(ol) { margin: 4px 0; padding-left: 16px; }
.feed-bubble-content :deep(li) { margin: 2px 0; }
.feed-bubble-content :deep(strong) { color: #fff; }
.feed-bubble-content :deep(code) { background: rgba(0,0,0,0.3); padding: 1px 4px; border-radius: 3px; font-size: 14px; }

.feed-bubble-time { font-size: 12px; color: rgba(255,255,255,0.4); margin-top: 4px; text-align: right; }

.feed-collapse-btn {
  background: none; border: none; color: var(--wb-accent, #5b9cf6);
  font-size: 13px; cursor: pointer; padding: 2px 0; margin-top: 4px;
}
.feed-collapse-btn:hover { text-decoration: underline; }

.feed-placeholder-notice {
  color: #9ea3b5;
  font-size: 13px;
  padding: 4px 0;
}
.feed-report-expand { margin-top: 6px; border-top: 1px solid rgba(255,255,255,0.06); padding-top: 4px; }
.feed-report-toggle {
  background: transparent; border: none; color: var(--wb-accent, #5b9cf6);
  font-size: 12px; cursor: pointer; padding: 2px 0;
  transition: opacity 0.15s;
}
.feed-report-toggle:hover { opacity: 0.8; }
.feed-report-content {
  margin-top: 4px; padding: 8px;
  background: rgba(0,0,0,0.15);
  border-radius: 6px;
  font-size: 12px;
  line-height: 1.6;
  max-height: 400px;
  overflow-y: auto;
  color: var(--wb-text, #e1e4ea);
}
.feed-report-content :deep(pre) {
  background: rgba(0,0,0,0.2);
  padding: 8px;
  border-radius: 4px;
  overflow-x: auto;
  font-size: 11px;
}

/* ── Typing indicator ─────────────────────────── */
.feed-typing {
  display: flex; align-items: center; gap: 8px;
  padding: 6px 12px; font-size: 13px;
  color: var(--wb-text-muted, #8b8fa3);
}
.feed-typing-dots { display: flex; gap: 3px; }
.feed-typing-dots span {
  width: 5px; height: 5px; border-radius: 50%;
  background: var(--wb-accent, #5b9cf6);
  animation: typing-bounce 1.4s infinite ease-in-out;
}
.feed-typing-dots span:nth-child(2) { animation-delay: 0.2s; }
.feed-typing-dots span:nth-child(3) { animation-delay: 0.4s; }
@keyframes typing-bounce {
  0%, 80%, 100% { opacity: 0.3; transform: scale(0.8); }
  40% { opacity: 1; transform: scale(1); }
}

/* ── Empty state ──────────────────────────────── */
.feed-empty {
  text-align: center; padding: 24px 12px;
  color: var(--wb-text-muted, #8b8fa3); font-size: 14px;
}
</style>
