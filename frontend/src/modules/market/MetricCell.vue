<script setup>
import { computed } from 'vue'

const props = defineProps({
  label: { type: String, required: true },
  value: { type: [String, Number], default: '--' },
  subValue: { type: String, default: '' },
  timestamp: { type: String, default: '' },
  status: { type: String, default: 'ok', validator: v => ['ok', 'degraded', 'unavailable', 'loading'].includes(v) },
  trend: { type: String, default: null }
})

const trendIcon = computed(() => ({ up: '▲', down: '▼', flat: '─' })[props.trend] ?? '')

const timeHHmmss = computed(() => {
  if (!props.timestamp) return ''
  const d = new Date(props.timestamp)
  if (Number.isNaN(d.getTime())) return props.timestamp
  return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}:${String(d.getSeconds()).padStart(2, '0')}`
})

const freshness = computed(() => {
  if (!props.timestamp) return ''
  const age = Date.now() - new Date(props.timestamp).getTime()
  if (!Number.isFinite(age) || age < 0) return ''
  if (age < 30_000) return 'fresh'
  if (age < 300_000) return ''
  if (age < 3_600_000) return 'stale'
  return 'very-stale'
})
</script>

<template>
  <div class="mc" :class="[`s-${status}`]">
    <span class="mc-l">{{ label }}</span>
    <template v-if="status === 'unavailable'"><span class="mc-na">数据不可用</span></template>
    <template v-else-if="status === 'loading'"><span class="mc-v mc-pulse">--</span></template>
    <template v-else>
      <span class="mc-v" :class="{ 'mc-up': trend === 'up', 'mc-dn': trend === 'down', 'market-rise': trend === 'up', 'market-fall': trend === 'down' }">
        <span v-if="trendIcon" class="mc-arrow">{{ trendIcon }}</span>{{ value ?? '--' }}
      </span>
    </template>
    <span v-if="subValue" class="mc-sub">{{ subValue }}</span>
    <span v-if="timeHHmmss" class="mc-ts" :class="freshness">{{ timeHHmmss }}</span>
  </div>
</template>

<style scoped>
.mc { display: flex; flex-direction: column; padding: 4px 8px; min-width: 0; }
.mc-l { font-size: 12px; color: #b2bccf; line-height: 1.3; overflow-wrap: anywhere; }
.mc-v { font-size: 15px; color: #e6eaf2; font-weight: 600; font-family: Consolas, Monaco, 'Courier New', monospace; font-variant-numeric: tabular-nums; line-height: 1.35; overflow-wrap: anywhere; }
.mc-up { color: #ff5c5c; }
.mc-dn { color: #1ee88f; }
.mc-arrow { font-size: 11px; margin-right: 2px; }
.mc-sub { font-size: 12px; color: #b2bccf; line-height: 1.3; overflow-wrap: anywhere; }
.mc-ts { font-size: 12px; color: #98a6bf; line-height: 1.3; overflow-wrap: anywhere; }
.mc-ts.fresh { color: #ff5c5c; }
.mc-ts.stale { color: #c2cad8; }
.mc-ts.very-stale { color: #1ee88f; }
.mc-na { font-size: 12px; color: #98a6bf; font-style: italic; line-height: 1.3; }
.s-unavailable { opacity: 0.6; }
.s-degraded { opacity: 0.7; }
.mc-pulse { animation: pulse 1.2s ease-in-out infinite; }
.market-rise { color: #ff5c5c; }
.market-fall { color: #1ee88f; }
@keyframes pulse { 0%,100% { opacity: .3; } 50% { opacity: 1; } }
</style>
