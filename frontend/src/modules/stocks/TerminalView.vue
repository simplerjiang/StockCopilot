<script setup>
const props = defineProps({
  quote: {
    type: Object,
    default: null
  },
  monochrome: {
    type: Boolean,
    default: false
  }
})

const emit = defineEmits(['search-focus'])

const getChangeClass = value => {
  const number = Number(value)
  if (Number.isNaN(number)) return ''
  if (number > 0) return 'is-rise'
  if (number < 0) return 'is-fall'
  return ''
}
</script>

<template>
  <section class="terminal-view" :class="{ monochrome }">
    <header class="terminal-view-header">
      <div>
        <p class="terminal-view-label">行情终端</p>
        <h3>{{ quote ? `${quote.name}（${quote.symbol}）` : '专业看盘终端' }}</h3>
      </div>
      <div v-if="quote" class="terminal-view-quote">
        <strong>{{ quote.price }}</strong>
        <span :class="getChangeClass(quote.changePercent)">
          {{ quote.change }} / {{ quote.changePercent }}%
        </span>
      </div>
    </header>

    <div v-if="!quote" class="terminal-empty-state">
      <div class="terminal-empty-icon">
        <svg width="48" height="48" viewBox="0 0 48 48" fill="none"><rect x="4" y="12" width="40" height="28" rx="4" stroke="currentColor" stroke-width="2" fill="none"/><polyline points="10,32 18,24 24,28 34,18 38,22" stroke="currentColor" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round"/><circle cx="24" cy="8" r="3" stroke="currentColor" stroke-width="2" fill="none"/></svg>
      </div>
      <h4>选择标的以加载行情</h4>
      <p>在上方搜索框中输入股票代码、名称或拼音缩写，快速查看实时行情与 K 线图表。</p>
      <button class="terminal-empty-cta" @click="emit('search-focus')">开始搜索</button>
    </div>

    <div v-else class="terminal-view-body">
      <section class="terminal-view-summary">
        <slot name="summary" />
      </section>
      <section class="terminal-view-chart">
        <slot name="chart" />
      </section>
    </div>
  </section>
</template>

<style scoped>
.terminal-view {
  display: grid;
  grid-template-rows: auto 1fr;
  gap: 1rem;
  min-height: calc(100vh - 238px);
  padding: 1.25rem;
  border-radius: 22px;
  border: 1px solid rgba(148, 163, 184, 0.18);
  z-index: 10;
  background:
    radial-gradient(circle at top left, rgba(14, 165, 233, 0.12), transparent 32%),
    linear-gradient(160deg, rgba(15, 23, 42, 0.96), rgba(15, 23, 42, 0.88));
  color: #e2e8f0;
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.04), 0 22px 55px rgba(15, 23, 42, 0.24);
}

.terminal-view.monochrome {
  background:
    radial-gradient(circle at top left, rgba(148, 163, 184, 0.1), transparent 32%),
    linear-gradient(160deg, rgba(23, 23, 23, 0.96), rgba(38, 38, 38, 0.92));
}

.terminal-view-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
}

.terminal-view-label {
  margin: 0 0 0.35rem;
  font-size: 0.72rem;
  letter-spacing: 0.18em;
  text-transform: uppercase;
  color: #7dd3fc;
}

.terminal-view h3 {
  margin: 0;
  font-size: 1.4rem;
  color: #f8fafc;
}

.terminal-view-quote {
  display: grid;
  justify-items: end;
  gap: 0.25rem;
  text-align: right;
}

.terminal-view-quote strong {
  font-size: 1.6rem;
  line-height: 1;
}

.terminal-view-body {
  display: grid;
  grid-template-rows: auto 1fr;
  gap: 1rem;
  min-width: 0;
  min-height: 0;
}

.terminal-view-summary,
.terminal-view-chart {
  border-radius: 18px;
  border: 1px solid rgba(148, 163, 184, 0.14);
  background: rgba(15, 23, 42, 0.38);
  padding: 1rem;
  min-width: 0;
}

.terminal-view-chart {
  min-height: 400px;
}

.is-rise {
  color: var(--color-market-rise);
}

.is-fall {
  color: var(--color-market-fall);
}

.terminal-empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
  min-height: 320px;
  text-align: center;
  color: rgba(148, 163, 184, 0.8);
}
.terminal-empty-icon {
  color: rgba(125, 211, 252, 0.4);
  margin-bottom: 0.5rem;
}
.terminal-empty-state h4 {
  margin: 0;
  font-size: 1.2rem;
  color: #e2e8f0;
}
.terminal-empty-state p {
  margin: 0;
  max-width: 360px;
  font-size: 0.88rem;
  line-height: 1.6;
  color: rgba(148, 163, 184, 0.7);
}
.terminal-empty-cta {
  margin-top: 0.5rem;
  padding: 0.5rem 1.25rem;
  border: 1px solid rgba(125, 211, 252, 0.3);
  border-radius: 10px;
  background: rgba(125, 211, 252, 0.08);
  color: #7dd3fc;
  font-size: 0.85rem;
  cursor: pointer;
  transition: all 0.15s;
}
.terminal-empty-cta:hover {
  background: rgba(125, 211, 252, 0.15);
  border-color: rgba(125, 211, 252, 0.5);
}
</style>