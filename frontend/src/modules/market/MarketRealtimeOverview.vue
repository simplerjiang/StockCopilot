<script setup>
import { computed } from 'vue'

const props = defineProps({
  overview: { type: Object, default: null },
  loading: { type: Boolean, default: false },
  error: { type: String, default: '' },
  formatDate: { type: Function, required: true },
  formatMoney: { type: Function, required: true },
  formatSignedPercent: { type: Function, required: true },
  formatSignedAmount: { type: Function, required: true }
})

const realtimeIndices = computed(() => props.overview?.indices ?? [])
const realtimeBreadthBuckets = computed(() => {
  const buckets = props.overview?.breadth?.buckets ?? []
  return buckets.filter(item => Number(item?.count ?? 0) > 0)
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
      <p v-else-if="props.loading && !realtimeIndices.length" class="feedback compact">实时总览加载中...</p>
      <div v-else class="ticker-grid">
        <article v-for="item in realtimeIndices" :key="item.symbol" class="ticker-card">
          <span>{{ item.name }}</span>
          <strong>{{ item.price.toFixed(2) }}</strong>
          <small :class="{ positive: item.changePercent >= 0, negative: item.changePercent < 0 }">{{ props.formatSignedPercent(item.changePercent) }}</small>
          <small>成交额 {{ props.formatMoney(item.turnoverAmount) }}</small>
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
          <strong :class="{ positive: (props.overview?.mainCapitalFlow?.mainNetInflow ?? 0) >= 0, negative: (props.overview?.mainCapitalFlow?.mainNetInflow ?? 0) < 0 }">
            {{ props.formatSignedAmount(props.overview?.mainCapitalFlow?.mainNetInflow) }}
          </strong>
          <small>超大单 {{ props.formatSignedAmount(props.overview?.mainCapitalFlow?.superLargeOrderNetInflow) }}</small>
        </div>
        <div class="flow-metric">
          <span>北向总净流入</span>
          <strong :class="{ positive: (props.overview?.northboundFlow?.totalNetInflow ?? 0) >= 0, negative: (props.overview?.northboundFlow?.totalNetInflow ?? 0) < 0 }">
            {{ props.formatSignedAmount(props.overview?.northboundFlow?.totalNetInflow) }}
          </strong>
          <small>沪股通 {{ props.formatSignedAmount(props.overview?.northboundFlow?.shanghaiNetInflow) }} / 深股通 {{ props.formatSignedAmount(props.overview?.northboundFlow?.shenzhenNetInflow) }}</small>
        </div>
        <div class="flow-metric">
          <span>涨跌分布</span>
          <strong>{{ props.overview?.breadth?.advancers ?? 0 }} / {{ props.overview?.breadth?.decliners ?? 0 }}</strong>
          <small>涨停 {{ props.overview?.breadth?.limitUpCount ?? 0 }} / 跌停 {{ props.overview?.breadth?.limitDownCount ?? 0 }} / 平盘 {{ props.overview?.breadth?.flatCount ?? 0 }}</small>
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
      <p v-else class="feedback compact">暂无涨跌分布数据。</p>
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
  border-radius: 22px;
  border: 1px solid rgba(226, 232, 240, 0.92);
  background:
    linear-gradient(180deg, rgba(255, 255, 255, 0.96), rgba(248, 250, 252, 0.98)),
    linear-gradient(135deg, rgba(194, 65, 12, 0.04), rgba(14, 165, 233, 0.05));
  box-shadow: 0 12px 30px rgba(15, 23, 42, 0.05);
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
  border-radius: 16px;
  background: rgba(255, 255, 255, 0.88);
  border: 1px solid rgba(226, 232, 240, 0.85);
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
  background: rgba(226, 232, 240, 0.92);
  overflow: hidden;
}

.breadth-bar-fill {
  display: block;
  height: 100%;
  border-radius: inherit;
  background: linear-gradient(90deg, #fb7185 0%, #f59e0b 50%, #0ea5e9 100%);
}

.positive {
  color: #b91c1c;
}

.negative {
  color: #166534;
}

.feedback.compact {
  padding: 12px 0;
  background: transparent;
  border: 0;
  box-shadow: none;
}

@media (max-width: 1100px) {
  .realtime-deck {
    grid-template-columns: 1fr;
  }

  .ticker-grid {
    grid-template-columns: 1fr;
  }
}
</style>