<script setup>
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'

const props = defineProps({
  enabled: { type: Boolean, required: true },
  loading: { type: Boolean, default: false },
  error: { type: String, default: '' },
  overview: { type: Object, default: null },
  detail: { type: Object, default: null },
  currentStockRealtimeQuote: { type: Object, default: null },
  stockRealtimeDomesticIndices: { type: Array, default: () => [] },
  stockRealtimeGlobalIndices: { type: Array, default: () => [] },
  stockRealtimeRelativeStrength: { type: Object, default: null },
  stockRealtimeBreadthBias: { type: Object, default: null },
  formatDate: { type: Function, required: true },
  getChangeClass: { type: Function, required: true },
  formatSignedNumber: { type: Function, required: true },
  formatSignedPercent: { type: Function, required: true },
  formatSignedRealtimeAmount: { type: Function, required: true },
  formatRealtimeMoney: { type: Function, required: true }
})

const emit = defineEmits(['refresh', 'toggle', 'open-chart'])

const GLOBAL_INDEX_FALLBACK_NAMES = {
  hsi: '恒生指数', hstech: '恒生科技', n225: '日经225',
  ndx: '纳斯达克', spx: '标普500', ftse: '富时100', ks11: '韩国KOSPI'
}

const resolveIndexName = (item) => {
  if (item.name && item.name !== item.symbol && !/^[A-Za-z0-9_.]+$/.test(item.name)) return item.name
  const key = (item.symbol || '').replace(/^[a-z]+_/i, '').toLowerCase()
  return GLOBAL_INDEX_FALLBACK_NAMES[key] || item.name || item.symbol || '未知指数'
}

const isLikelyTradingHours = computed(() => {
  const now = new Date()
  const day = now.getDay()
  if (day === 0 || day === 6) return false
  const h = now.getHours(), m = now.getMinutes()
  const t = h * 60 + m
  return t >= 9 * 60 + 15 && t <= 15 * 60 + 5
})

const AUTO_REFRESH_SECONDS = 30
const expanded = ref(localStorage.getItem('market_bar_expanded') === 'true')
const countdown = ref(AUTO_REFRESH_SECONDS)
let countdownTimer = null

const startCountdown = () => {
  stopCountdown()
  countdown.value = AUTO_REFRESH_SECONDS
  countdownTimer = setInterval(() => {
    countdown.value--
    if (countdown.value <= 0) {
      emit('refresh')
      countdown.value = AUTO_REFRESH_SECONDS
    }
  }, 1000)
}

const stopCountdown = () => {
  if (countdownTimer) { clearInterval(countdownTimer); countdownTimer = null }
}

const manualRefresh = () => {
  emit('refresh')
  countdown.value = AUTO_REFRESH_SECONDS
}

const toggleExpand = () => {
  expanded.value = !expanded.value
  localStorage.setItem('market_bar_expanded', String(expanded.value))
}

const openChart = (item, icon) => {
  emit('open-chart', { ...item, icon })
}

const ARC_CIRCUMFERENCE = 2 * Math.PI * 10
const countdownArcOffset = computed(() => {
  const progress = countdown.value / AUTO_REFRESH_SECONDS
  return ARC_CIRCUMFERENCE * (1 - progress)
})

watch(() => props.enabled, val => {
  if (val) startCountdown(); else stopCountdown()
})

onMounted(() => { if (props.enabled) startCountdown() })
onUnmounted(() => { stopCountdown() })
</script>

<template>
  <section v-if="!enabled" class="market-bar market-bar-hidden">
    <div class="bar-hidden-row">
      <span class="bar-hidden-label">📊 市场总览已隐藏</span>
      <button class="expand-toggle" @click="$emit('toggle')">显示</button>
    </div>
  </section>

  <section v-else class="market-bar" :class="{ 'is-expanded': expanded }">
    <p v-if="error" class="bar-error">{{ error }}</p>
    <p v-else-if="loading && !overview" class="bar-loading">加载市场数据中...</p>

    <template v-else-if="overview">
      <div class="bar-main">
        <!-- ZONE A: Market Indices -->
        <div class="bar-indices">
          <div class="idx-row">
            <article
              v-for="item in stockRealtimeDomesticIndices"
              :key="item.symbol"
              class="idx-card"
              @click="openChart(item, '📈')"
            >
              <div class="idx-card-name"><span>📈</span><span>{{ resolveIndexName(item) }}</span></div>
              <div class="idx-card-data">
                <strong class="idx-card-price">{{ item.price.toFixed(2) }}</strong>
                <span class="idx-card-change" :class="getChangeClass(item.changePercent)">{{ formatSignedPercent(item.changePercent) }}</span>
              </div>
              <div class="idx-card-vol">成交 {{ item.turnoverAmount ? formatRealtimeMoney(item.turnoverAmount) : '-' }}</div>
            </article>
          </div>
          <div class="idx-row idx-row--global">
            <article
              v-for="item in stockRealtimeGlobalIndices"
              :key="item.symbol"
              class="idx-card"
              @click="openChart(item, '🌏')"
            >
              <div class="idx-card-name"><span>🌏</span><span>{{ resolveIndexName(item) }}</span></div>
              <div class="idx-card-data">
                <strong class="idx-card-price">{{ item.price.toFixed(2) }}</strong>
                <span class="idx-card-change" :class="getChangeClass(item.changePercent)">{{ formatSignedPercent(item.changePercent) }}</span>
              </div>
              <div class="idx-card-vol">成交 {{ item.turnoverAmount ? formatRealtimeMoney(item.turnoverAmount) : '-' }}</div>
            </article>
          </div>
        </div>

        <!-- ZONE B: Pulse Metrics -->
        <div class="bar-pulse">
          <div class="pulse-chip">
            <span class="pulse-chip-label">💰 主力</span>
            <strong class="pulse-chip-value" :class="overview.mainCapitalFlow?.mainNetInflow ? getChangeClass(overview.mainCapitalFlow.mainNetInflow) : ''">
              {{ !overview.mainCapitalFlow?.mainNetInflow && !isLikelyTradingHours ? '休市' : formatSignedRealtimeAmount(overview.mainCapitalFlow?.mainNetInflow) }}
            </strong>
          </div>
          <div class="pulse-chip">
            <span class="pulse-chip-label">🌏 北向</span>
            <strong class="pulse-chip-value" :class="overview.northboundFlow?.totalNetInflow ? getChangeClass(overview.northboundFlow.totalNetInflow) : ''">
              {{ !overview.northboundFlow?.totalNetInflow && !isLikelyTradingHours ? '休市' : formatSignedRealtimeAmount(overview.northboundFlow?.totalNetInflow) }}
            </strong>
          </div>
          <div class="pulse-chip">
            <span class="pulse-chip-label">📊 广度</span>
            <strong class="pulse-chip-value">{{ overview.breadth?.advancers ?? 0 }}<span class="text-rise-sm">↑</span> / {{ overview.breadth?.decliners ?? 0 }}<span class="text-fall-sm">↓</span></strong>
          </div>
          <div class="pulse-chip">
            <span class="pulse-chip-label">🔥 封板</span>
            <strong class="pulse-chip-value">{{ overview.breadth?.limitUpCount ?? 0 }}<span class="text-rise-sm">↑</span> / {{ overview.breadth?.limitDownCount ?? 0 }}<span class="text-fall-sm">↓</span></strong>
          </div>
        </div>

        <!-- ZONE C: Status Strip -->
        <div class="bar-status">
          <div class="refresh-indicator" @click="manualRefresh" title="点击立即刷新">
            <svg class="refresh-arc" viewBox="0 0 24 24" width="28" height="28">
              <circle cx="12" cy="12" r="10" fill="none" stroke="var(--color-border-light, #e5e7eb)" stroke-width="2" />
              <circle
                cx="12" cy="12" r="10" fill="none"
                stroke="var(--color-accent, #2563eb)" stroke-width="2"
                stroke-linecap="round"
                :stroke-dasharray="ARC_CIRCUMFERENCE"
                :stroke-dashoffset="countdownArcOffset"
                transform="rotate(-90 12 12)"
              />
            </svg>
            <span class="refresh-seconds" :class="{ 'is-loading': loading }">{{ loading ? '⟳' : countdown }}</span>
          </div>
          <small class="snapshot-time">{{ formatDate(overview.snapshotTime) }}</small>
          <div v-if="currentStockRealtimeQuote" class="cur-stk-badge">
            <strong>{{ currentStockRealtimeQuote.name }}</strong>
            <span :class="getChangeClass(currentStockRealtimeQuote.changePercent)">{{ formatSignedPercent(currentStockRealtimeQuote.changePercent) }}</span>
          </div>
          <button class="expand-toggle" @click="toggleExpand">{{ expanded ? '▲ 收起' : '▼ 展开' }}</button>
          <button class="hide-toggle" @click="$emit('toggle')">隐藏</button>
        </div>
      </div>

      <!-- Detail Tray (expanded) -->
      <div v-if="expanded" class="bar-detail-tray">
        <article class="detail-chip">
          <div class="detail-chip-title">💰 主力资金</div>
          <div class="detail-chip-row">
            <span>净流入</span>
            <strong :class="getChangeClass(overview.mainCapitalFlow?.mainNetInflow)">{{ formatSignedRealtimeAmount(overview.mainCapitalFlow?.mainNetInflow) }}</strong>
          </div>
          <div class="detail-chip-row">
            <span>超大单</span>
            <strong :class="getChangeClass(overview.mainCapitalFlow?.superLargeOrderNetInflow)">{{ formatSignedRealtimeAmount(overview.mainCapitalFlow?.superLargeOrderNetInflow) }}</strong>
          </div>
        </article>

        <article class="detail-chip">
          <div class="detail-chip-title">🌏 北向资金</div>
          <div class="detail-chip-row">
            <span>总计</span>
            <strong :class="getChangeClass(overview.northboundFlow?.totalNetInflow)">{{ formatSignedRealtimeAmount(overview.northboundFlow?.totalNetInflow) }}</strong>
          </div>
          <div class="detail-chip-row">
            <span>沪通</span>
            <strong :class="getChangeClass(overview.northboundFlow?.shanghaiNetInflow)">{{ formatSignedRealtimeAmount(overview.northboundFlow?.shanghaiNetInflow) }}</strong>
          </div>
          <div class="detail-chip-row">
            <span>深通</span>
            <strong :class="getChangeClass(overview.northboundFlow?.shenzhenNetInflow)">{{ formatSignedRealtimeAmount(overview.northboundFlow?.shenzhenNetInflow) }}</strong>
          </div>
        </article>

        <article class="detail-chip">
          <div class="detail-chip-title">📊 市场广度</div>
          <div class="detail-chip-row">
            <span>上涨</span>
            <strong class="text-rise">{{ overview.breadth?.advancers ?? 0 }}</strong>
          </div>
          <div class="detail-chip-row">
            <span>下跌</span>
            <strong class="text-fall">{{ overview.breadth?.decliners ?? 0 }}</strong>
          </div>
          <div class="detail-chip-row">
            <span>平盘</span>
            <strong>{{ overview.breadth?.flatCount ?? 0 }}</strong>
          </div>
          <div class="detail-chip-row">
            <span>{{ stockRealtimeBreadthBias?.label ?? '多空拉锯' }}</span>
          </div>
        </article>

        <article class="detail-chip">
          <div class="detail-chip-title">🔥 封板温度</div>
          <div class="detail-chip-row">
            <span>涨停</span>
            <strong class="text-rise">{{ overview.breadth?.limitUpCount ?? 0 }}</strong>
          </div>
          <div class="detail-chip-row">
            <span>跌停</span>
            <strong class="text-fall">{{ overview.breadth?.limitDownCount ?? 0 }}</strong>
          </div>
        </article>

        <article v-if="currentStockRealtimeQuote" class="detail-chip">
          <div class="detail-chip-title">⚡ 个股对照</div>
          <div class="detail-chip-row">
            <span>{{ currentStockRealtimeQuote.name }}</span>
            <strong :class="getChangeClass(currentStockRealtimeQuote.changePercent)">{{ formatSignedPercent(currentStockRealtimeQuote.changePercent) }}</strong>
          </div>
          <div v-if="stockRealtimeRelativeStrength" class="detail-chip-row">
            <span>{{ stockRealtimeRelativeStrength.label }}</span>
            <strong :class="getChangeClass(stockRealtimeRelativeStrength.spread)">{{ formatSignedPercent(stockRealtimeRelativeStrength.spread) }}</strong>
          </div>
          <div class="detail-chip-row">
            <span>成交额</span>
            <strong>{{ formatRealtimeMoney(currentStockRealtimeQuote.turnoverAmount) }}</strong>
          </div>
        </article>
      </div>
    </template>

    <p v-else class="bar-empty">暂无市场数据</p>
  </section>
</template>

<style scoped>
/* ── Outer Bar ──────────────────────────────── */
.market-bar {
  display: grid;
  gap: var(--space-2, 8px);
  padding: var(--space-3, 12px) var(--space-4, 16px);
  background: var(--color-bg-surface, #fff);
  border: 1px solid var(--color-border-light, #e5e7eb);
  border-radius: var(--radius-lg, 12px);
  box-shadow: 0 2px 6px rgba(15, 23, 42, 0.06);
}
.market-bar-hidden { padding: var(--space-2, 8px) var(--space-4, 16px); }

.bar-hidden-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.bar-hidden-label {
  font-size: var(--text-sm, 12px);
  color: var(--color-text-muted, #94a3b8);
}

/* ── Main 3-Zone Row ────────────────────────── */
.bar-main {
  display: grid;
  grid-template-columns: 1fr auto auto;
  gap: var(--space-3, 12px);
  align-items: start;
}

/* ── ZONE A: Indices ────────────────────────── */
.bar-indices { display: grid; gap: var(--space-2, 8px); }
.idx-row {
  display: grid;
  grid-template-columns: repeat(3, minmax(150px, 1fr));
  gap: var(--space-2, 8px);
}
.idx-row--global {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-1-5, 6px);
}

.idx-card {
  display: grid;
  gap: var(--space-0-5, 2px);
  padding: var(--space-2, 8px);
  background: var(--color-bg-surface-alt, #f8f9fb);
  border: 1px solid var(--color-border-light, #e5e7eb);
  border-radius: var(--radius-md, 8px);
  cursor: pointer;
  transition: all 150ms;
}
.idx-card:hover {
  border-color: var(--color-accent-border, rgba(37, 99, 235, 0.30));
  background: var(--color-accent-subtle, rgba(37, 99, 235, 0.08));
  box-shadow: 0 2px 6px rgba(15, 23, 42, 0.06);
  transform: translateY(-1px);
}
.idx-card:active { transform: translateY(0); box-shadow: none; }

/* Domestic indices: larger cards */
.idx-row:not(.idx-row--global) .idx-card {
  padding: var(--space-3, 12px);
}
.idx-row:not(.idx-row--global) .idx-card-price {
  font-size: var(--text-lg, 16px);
}
.idx-row:not(.idx-row--global) .idx-card-change {
  font-size: var(--text-base, 14px);
}

/* Global indices: compact inline style */
.idx-row--global .idx-card {
  display: flex;
  align-items: center;
  gap: var(--space-2, 8px);
  padding: var(--space-1, 4px) var(--space-2, 8px);
  min-width: 0;
}
.idx-row--global .idx-card-name {
  font-size: 10px;
}
.idx-row--global .idx-card-data {
  gap: var(--space-1, 4px);
}
.idx-row--global .idx-card-price {
  font-size: var(--text-sm, 12px);
}
.idx-row--global .idx-card-change {
  font-size: 10px;
}
.idx-row--global .idx-card-vol {
  display: none;
}

.idx-card-name {
  display: flex;
  align-items: center;
  gap: var(--space-1, 4px);
  font-size: var(--text-xs, 11px);
  color: var(--color-text-secondary, #475569);
}
.idx-card-data {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: var(--space-2, 8px);
}
.idx-card-price {
  font-size: var(--text-base, 13px);
  font-weight: 700;
  font-variant-numeric: tabular-nums;
  color: var(--color-text-primary, #0f172a);
}
.idx-card-change {
  font-size: var(--text-sm, 12px);
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}
.idx-card-vol {
  font-size: var(--text-xs, 11px);
  color: var(--color-text-muted, #94a3b8);
}

/* ── ZONE B: Pulse Chips ────────────────────── */
.bar-pulse {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: var(--space-1-5, 6px);
  align-content: center;
}
.pulse-chip {
  display: grid;
  gap: var(--space-0-5, 2px);
  padding: var(--space-1-5, 6px) var(--space-2, 8px);
  background: var(--color-bg-surface-alt, #f8f9fb);
  border: 1px solid var(--color-border-light, #e5e7eb);
  border-radius: var(--radius-md, 8px);
  min-width: 88px;
  transition: border-color 150ms;
}
.pulse-chip:hover { border-color: var(--color-border-medium, #cbd5e1); }
.pulse-chip-label {
  font-size: var(--text-xs, 11px);
  color: var(--color-text-muted, #94a3b8);
}
.pulse-chip-value {
  font-size: var(--text-sm, 12px);
  font-weight: 700;
  font-variant-numeric: tabular-nums;
}
.text-rise-sm { color: var(--color-market-rise, #ef4444); }
.text-fall-sm { color: var(--color-market-fall, #16a34a); }

/* ── ZONE C: Status Strip ───────────────────── */
.bar-status {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--space-1-5, 6px);
  min-width: 80px;
}

.refresh-indicator {
  position: relative;
  width: 28px; height: 28px;
  cursor: pointer;
}
.refresh-arc { display: block; }
.refresh-seconds {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 9px;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
  color: var(--color-accent, #2563eb);
}
.refresh-seconds.is-loading { animation: spin 1s linear infinite; }
.snapshot-time {
  font-size: var(--text-xs, 11px);
  color: var(--color-text-muted, #94a3b8);
  text-align: center;
}

.cur-stk-badge {
  display: flex;
  align-items: center;
  gap: var(--space-1, 4px);
  padding: var(--space-1, 4px) var(--space-2, 8px);
  background: var(--color-bg-inset, #f0f2f5);
  border: 1px solid var(--color-border-light, #e5e7eb);
  border-radius: var(--radius-sm, 4px);
  font-size: var(--text-xs, 11px);
  max-width: 140px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.cur-stk-badge strong {
  font-weight: 600;
  color: var(--color-text-primary, #0f172a);
}

.expand-toggle, .hide-toggle {
  border: none;
  background: none;
  font-size: var(--text-xs, 11px);
  color: var(--color-accent, #2563eb);
  cursor: pointer;
  padding: 0;
}
.expand-toggle:hover, .hide-toggle:hover {
  color: var(--color-accent-hover, #1d4ed8);
  text-decoration: underline;
}

/* ── Detail Tray ────────────────────────────── */
.bar-detail-tray {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: var(--space-2, 8px);
  padding-top: var(--space-3, 12px);
  border-top: 1px solid var(--color-border-light, #e5e7eb);
  animation: slideDown 250ms ease;
}
.detail-chip {
  display: grid;
  gap: var(--space-1, 4px);
  padding: var(--space-3, 12px);
  background: var(--color-bg-surface-alt, #f8f9fb);
  border: 1px solid var(--color-border-light, #e5e7eb);
  border-radius: var(--radius-md, 8px);
}
.detail-chip-title {
  font-size: var(--text-xs, 11px);
  font-weight: 600;
  color: var(--color-text-secondary, #475569);
  margin-bottom: var(--space-0-5, 2px);
}
.detail-chip-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: var(--text-sm, 12px);
}
.detail-chip-row span { color: var(--color-text-secondary, #475569); }
.detail-chip-row strong { font-variant-numeric: tabular-nums; }

/* ── State classes ──────────────────────────── */
.text-rise { color: var(--color-market-rise, #ef4444); }
.text-fall { color: var(--color-market-fall, #16a34a); }

.bar-error {
  color: var(--color-danger, #dc2626);
  font-size: var(--text-sm, 12px);
  margin: 0;
  padding: var(--space-2, 8px) 0;
}
.bar-loading, .bar-empty {
  color: var(--color-text-muted, #94a3b8);
  font-size: var(--text-sm, 12px);
  margin: 0;
  padding: var(--space-2, 8px) 0;
}

/* ── Animations ─────────────────────────────── */
@keyframes slideDown {
  from { opacity: 0; transform: translateY(-4px); }
  to { opacity: 1; transform: translateY(0); }
}
@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

/* ── Responsive ─────────────────────────────── */
@media (max-width: 1100px) {
  .bar-main { grid-template-columns: 1fr auto; }
  .bar-pulse {
    grid-template-columns: repeat(4, 1fr);
    grid-column: 1 / -1;
  }
  .bar-detail-tray { grid-template-columns: repeat(2, 1fr); }
}
@media (max-width: 720px) {
  .bar-main { grid-template-columns: 1fr; }
  .bar-status {
    order: -1;
    flex-direction: row;
    justify-content: space-between;
    width: 100%;
  }
  .idx-row { grid-template-columns: repeat(2, 1fr); }
  .bar-pulse { grid-template-columns: repeat(2, 1fr); }
  .bar-detail-tray { grid-template-columns: 1fr; }
}
</style>