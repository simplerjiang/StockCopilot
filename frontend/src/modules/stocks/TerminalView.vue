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
        <p class="terminal-view-label">TerminalView</p>
        <h3>{{ quote ? `${quote.name}（${quote.symbol}）` : '专业看盘终端' }}</h3>
      </div>
      <div v-if="quote" class="terminal-view-quote">
        <strong>{{ quote.price }}</strong>
        <span :class="getChangeClass(quote.changePercent)">
          {{ quote.change }} / {{ quote.changePercent }}%
        </span>
      </div>
    </header>

    <div class="terminal-view-body">
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
  height: max-content;
  min-height: calc(100vh - 238px);
  padding: 1.25rem;
  border-radius: 22px;
  border: 1px solid rgba(148, 163, 184, 0.18);
  position: sticky;
  top: 1.5rem;
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
  min-height: 0;
}

.is-rise {
  color: #f87171;
}

.is-fall {
  color: #4ade80;
}
</style>