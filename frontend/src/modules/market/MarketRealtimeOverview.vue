<script setup>
import { computed } from 'vue'

const props = defineProps({
  overview: { type: Object, default: null },
  loading: { type: Boolean, default: false },
  pageLoading: { type: Boolean, default: false },
  degraded: { type: Boolean, default: false },
  error: { type: String, default: '' },
  formatDate: { type: Function, required: true },
  formatMoney: { type: Function, required: true },
  formatSignedPercent: { type: Function, required: true },
  formatSignedAmount: { type: Function, required: true }
})

const hasNumericValue = value => typeof value === 'number' && Number.isFinite(value)
const isPositiveNumber = value => hasNumericValue(value) && value >= 0
const isNegativeNumber = value => hasNumericValue(value) && value < 0
const isGuardedRealtimeState = computed(() => props.loading || props.pageLoading || props.degraded)

const isLikelyTradingHours = computed(() => {
  const now = new Date()
  const dayOfWeek = now.getDay()
  if (dayOfWeek === 0 || dayOfWeek === 6) return false
  const utcH = now.getUTCHours(), utcM = now.getUTCMinutes()
  const chinaMinutes = (utcH * 60 + utcM + 480) % 1440
  return chinaMinutes >= 555 && chinaMinutes <= 905
})

const isNorthboundClosed = computed(() => {
  const flow = props.overview?.northboundFlow
  if (!flow) return false
  return flow.totalNetInflow === 0 && flow.shanghaiNetInflow === 0 && flow.shenzhenNetInflow === 0 && !isLikelyTradingHours.value
})
const needsGuardedPlaceholder = value => isGuardedRealtimeState.value && !hasNumericValue(value)
const needsGuardedTurnoverPlaceholder = value => isGuardedRealtimeState.value && (!hasNumericValue(value) || value <= 0)
const formatValue = (value, formatter, fallback = '--') => (hasNumericValue(value) ? formatter(value) : fallback)
const formatIndexPrice = value => formatValue(value, current => current.toFixed(2))
const formatIndexChangePercent = value => (
  needsGuardedPlaceholder(value)
    ? '--'
    : formatValue(value, current => props.formatSignedPercent(current))
)
const formatTurnoverText = value => (
  needsGuardedTurnoverPlaceholder(value)
    ? '成交额 实时补充中'
    : `成交额 ${formatValue(value, current => props.formatMoney(current))}`
)
const formatSignedAmountText = (value, placeholder = '数据不可用') => (
  needsGuardedPlaceholder(value)
    ? placeholder
    : formatValue(value, current => props.formatSignedAmount(current))
)
const formatLabeledSignedAmount = (label, value, placeholder = '实时补充中') => (
  `${label} ${needsGuardedPlaceholder(value) ? placeholder : formatValue(value, current => props.formatSignedAmount(current))}`
)
const formatLabeledCount = (label, value, placeholder = '暂无数据') => (
  `${label} ${needsGuardedPlaceholder(value) ? placeholder : formatValue(value, current => String(current))}`
)

const realtimeIndices = computed(() => props.overview?.indices ?? [])
const realtimeBreadthBuckets = computed(() => {
  const buckets = props.overview?.breadth?.buckets ?? []
  return buckets.filter(item => hasNumericValue(item?.count) && item.count > 0)
})
const mainFlowSecondaryText = computed(() => formatLabeledSignedAmount('超大单', props.overview?.mainCapitalFlow?.superLargeOrderNetInflow))
const northboundBreakdownText = computed(() => {
  if (isNorthboundClosed.value) return '沪股通 休市 / 深股通 休市'
  return [
    formatLabeledSignedAmount('沪股通', props.overview?.northboundFlow?.shanghaiNetInflow, '数据不可用'),
    formatLabeledSignedAmount('深股通', props.overview?.northboundFlow?.shenzhenNetInflow, '数据不可用')
  ].join(' / ')
})
const breadthSummaryText = computed(() => {
  const advancers = props.overview?.breadth?.advancers
  const decliners = props.overview?.breadth?.decliners

  if (needsGuardedPlaceholder(advancers) || needsGuardedPlaceholder(decliners)) {
    return '数据不可用'
  }

  return `${formatValue(advancers, current => String(current))} / ${formatValue(decliners, current => String(current))}`
})
const breadthDetailText = computed(() => ([
  formatLabeledCount('涨停', props.overview?.breadth?.limitUpCount),
  formatLabeledCount('跌停', props.overview?.breadth?.limitDownCount),
  formatLabeledCount('平盘', props.overview?.breadth?.flatCount)
].join(' / ')))
const indexEmptyFeedback = computed(() => {
  if (props.loading || props.pageLoading) return '实时总览加载中...'
  if (props.degraded) return '实时补充中'
  return '暂无指数快照。'
})
const breadthEmptyFeedback = computed(() => {
  if (isGuardedRealtimeState.value) return '实时补充中'
  return '暂无涨跌分布数据。'
})

const formatBucketWidth = count => {
  const maxCount = realtimeBreadthBuckets.value.reduce((current, item) => Math.max(current, Number(item.count ?? 0)), 0)
  if (!maxCount) return '0%'
  return `${Math.max(8, Math.round((Number(count ?? 0) / maxCount) * 100))}%`
}
</script>

<template>
  <section class="realtime-deck">
    <article class="realtime-card realtime-index-card">
      <div class="realtime-card-head">
        <div>
          <p class="market-kicker realtime-kicker">Realtime Tape</p>
          <h3>指数快照</h3>
        </div>
        <small>{{ props.formatDate(props.overview?.snapshotTime) }}</small>
      </div>
      <p v-if="props.error" class="feedback error compact">{{ props.error }}</p>
      <p v-else-if="!realtimeIndices.length" class="feedback compact">{{ indexEmptyFeedback }}</p>
      <div v-else class="ticker-grid">
        <article v-for="item in realtimeIndices" :key="item.symbol" class="ticker-card">
          <span>{{ item.name }}</span>
          <strong>{{ formatIndexPrice(item.price) }}</strong>
          <small :class="{ positive: isPositiveNumber(item.changePercent), negative: isNegativeNumber(item.changePercent) }">{{ formatIndexChangePercent(item.changePercent) }}</small>
          <small>{{ formatTurnoverText(item.turnoverAmount) }}</small>
        </article>
      </div>
    </article>

    <article class="realtime-card realtime-flow-card">
      <div class="realtime-card-head">
        <div>
          <p class="market-kicker realtime-kicker">Capital Flow</p>
          <h3>资金与广度</h3>
        </div>
        <small>{{ props.formatDate(props.overview?.mainCapitalFlow?.snapshotTime || props.overview?.northboundFlow?.snapshotTime) }}</small>
      </div>
      <div class="flow-grid">
        <div class="flow-metric">
          <span>主力净流入</span>
          <strong :class="{ positive: isPositiveNumber(props.overview?.mainCapitalFlow?.mainNetInflow), negative: isNegativeNumber(props.overview?.mainCapitalFlow?.mainNetInflow) }">
            {{ formatSignedAmountText(props.overview?.mainCapitalFlow?.mainNetInflow) }}
          </strong>
          <small>{{ mainFlowSecondaryText }}</small>
        </div>
        <div class="flow-metric">
          <span>北向总净流入</span>
          <strong :class="{ positive: !isNorthboundClosed && isPositiveNumber(props.overview?.northboundFlow?.totalNetInflow), negative: !isNorthboundClosed && isNegativeNumber(props.overview?.northboundFlow?.totalNetInflow) }">
            {{ isNorthboundClosed ? '休市' : formatSignedAmountText(props.overview?.northboundFlow?.totalNetInflow) }}
          </strong>
          <small>{{ northboundBreakdownText }}</small>
        </div>
        <div class="flow-metric">
          <span>涨跌分布</span>
          <strong>{{ breadthSummaryText }}</strong>
          <small>{{ breadthDetailText }}</small>
        </div>
      </div>
    </article>

    <article class="realtime-card realtime-breadth-card">
      <div class="realtime-card-head">
        <div>
          <p class="market-kicker realtime-kicker">Breadth Map</p>
          <h3>涨跌分布桶</h3>
        </div>
        <small>{{ props.formatDate(props.overview?.breadth?.tradingDate) }}</small>
      </div>
      <div v-if="realtimeBreadthBuckets.length" class="breadth-buckets">
        <div v-for="item in realtimeBreadthBuckets" :key="item.label" class="breadth-bucket">
          <span>{{ item.label }}</span>
          <div class="breadth-bar-track">
            <span class="breadth-bar-fill" :style="{ width: formatBucketWidth(item.count) }"></span>
          </div>
          <strong>{{ item.count }}</strong>
        </div>
      </div>
      <p v-else class="feedback compact">{{ breadthEmptyFeedback }}</p>
    </article>
  </section>
</template>

<style scoped>
.realtime-deck {
  display: grid;
  grid-template-columns: 1.1fr 1fr 1fr;
  gap: 14px;
}

.realtime-card {
  display: grid;
  gap: 14px;
  padding: 18px;
  border-radius: var(--radius-xl);
  border: 1px solid var(--color-border-light);
  background: var(--color-bg-surface);
  box-shadow: var(--shadow-sm);
}

.realtime-card-head {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  align-items: start;
}

.realtime-card-head h3 {
  margin: 0;
}

.realtime-kicker {
  margin-bottom: 6px;
}

.ticker-grid,
.flow-grid,
.breadth-buckets {
  display: grid;
  gap: 10px;
}

.ticker-grid {
  grid-template-columns: repeat(3, minmax(0, 1fr));
}

.ticker-card,
.flow-metric {
  display: grid;
  gap: 6px;
  padding: 14px;
  border-radius: var(--radius-lg);
  background: var(--color-bg-surface-alt);
  border: 1px solid var(--color-border-light);
}

.ticker-card strong,
.flow-metric strong {
  font-size: 24px;
}

.breadth-bucket {
  display: grid;
  grid-template-columns: 56px minmax(0, 1fr) 40px;
  gap: 10px;
  align-items: center;
}

.breadth-bar-track {
  height: 10px;
  border-radius: 999px;
  background: var(--color-bg-surface-alt);
  overflow: hidden;
}

.breadth-bar-fill {
  display: block;
  height: 100%;
  border-radius: inherit;
  background: linear-gradient(90deg, #fb7185 0%, #f59e0b 50%, #0ea5e9 100%);
}

.positive {
  color: var(--color-market-rise);
}

.negative {
  color: var(--color-market-fall);
}

.feedback.compact {
  padding: 12px 0;
  background: transparent;
  border: 0;
  box-shadow: none;
}

@media (max-width: 1100px) {
  .realtime-deck {
    grid-template-columns: 1fr 1fr;
  }

  .realtime-breadth-card {
    grid-column: 1 / -1;
  }

  .ticker-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 720px) {
  .realtime-deck {
    grid-template-columns: 1fr;
  }

  .ticker-grid {
    grid-template-columns: 1fr;
  }
}
</style>