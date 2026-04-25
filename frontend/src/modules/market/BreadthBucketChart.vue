<script setup>
import { computed } from 'vue'

const props = defineProps({
  buckets: { type: Array, default: () => [] },
  advancers: { type: Number, default: null },
  decliners: { type: Number, default: null },
  flatCount: { type: Number, default: null },
  timestamp: { type: String, default: '' },
  unavailable: { type: Boolean, default: false }
})

const maxCount = computed(() => {
  if (!props.buckets.length) return 1
  return Math.max(1, ...props.buckets.map(b => b.count || 0))
})
const fmtCount = value => Number.isFinite(Number(value)) ? Number(value) : '—'

const barColor = bucket => {
  const lbl = bucket.label || bucket.changeBucket || ''
  if (lbl.startsWith('-') || lbl.startsWith('跌')) return '#1ee88f'
  if (lbl === '0' || lbl === '平' || lbl.includes('0%')) return '#c2cad8'
  return '#ff5c5c'
}

const timeHHmmss = computed(() => {
  if (!props.timestamp) return ''
  const d = new Date(props.timestamp)
  if (Number.isNaN(d.getTime())) return props.timestamp
  return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}:${String(d.getSeconds()).padStart(2, '0')}`
})
</script>

<template>
  <div class="bc">
    <div class="bc-hdr">
      <span class="bc-title">涨跌分布</span>
      <span v-if="timeHHmmss" class="bc-ts">{{ timeHHmmss }}</span>
    </div>
    <div class="bc-sum">
      <template v-if="unavailable">
        <span class="bc-na">数据不可用</span>
      </template>
      <template v-else>
        <span class="bc-up market-rise">涨 {{ fmtCount(advancers) }}</span>
        <span class="bc-sep">|</span>
        <span class="bc-fl">平 {{ fmtCount(flatCount) }}</span>
        <span class="bc-sep">|</span>
        <span class="bc-dn market-fall">跌 {{ fmtCount(decliners) }}</span>
      </template>
    </div>
    <div v-if="unavailable" class="bc-empty">数据不可用</div>
    <div v-else-if="!buckets.length || buckets.every(b => !b.count)" class="bc-empty">暂无涨跌分布数据</div>
    <div v-else class="bc-rows">
      <div v-for="(b, i) in buckets" :key="i" class="bc-row">
        <span class="bc-lbl">{{ b.label || b.changeBucket }}</span>
        <div class="bc-track">
          <div class="bc-bar" :style="{ width: (b.count / maxCount * 100) + '%', backgroundColor: barColor(b) }"></div>
        </div>
        <span class="bc-cnt">{{ b.count }}</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.bc { padding: 6px 8px; background: #1a2233; border: 1px solid #334155; min-width: 0; }
.bc-hdr { display: flex; flex-wrap: wrap; justify-content: space-between; align-items: center; gap: 4px 8px; margin-bottom: 3px; }
.bc-title { font-size: 12px; color: #b2bccf; font-weight: 600; line-height: 1.3; }
.bc-ts { font-size: 12px; color: #98a6bf; line-height: 1.3; }
.bc-empty { font-size: 11px; color: #98a6bf; font-style: italic; padding: 8px 0; text-align: center; line-height: 1.3; }
.bc-sum { display: flex; flex-wrap: wrap; gap: 2px 0; font-size: 12px; font-family: Consolas, Monaco, 'Courier New', monospace; font-variant-numeric: tabular-nums; margin-bottom: 4px; }
.bc-na { color: #98a6bf; font-size: 11px; line-height: 1.3; }
.bc-up { color: #ff5c5c; }
.bc-fl { color: #c2cad8; }
.bc-dn { color: #1ee88f; }
.bc-sep { color: #98a6bf; margin: 0 6px; }
.bc-rows { display: flex; flex-direction: column; gap: 3px; }
.bc-row { display: flex; align-items: center; gap: 6px; min-height: 20px; min-width: 0; }
.bc-lbl { font-size: 11px; color: #b2bccf; flex: 0 1 56px; min-width: 0; text-align: right; font-family: Consolas, Monaco, 'Courier New', monospace; line-height: 1.3; }
.bc-track { flex: 1; min-width: 0; height: 12px; background: #111827; border: 1px solid rgba(51,65,85,0.75); border-radius: 2px; overflow: hidden; }
.bc-bar { height: 100%; border-radius: 2px; max-width: 100%; transition: width 0.3s ease; }
.bc-cnt { font-size: 11px; color: #b2bccf; flex: 0 0 32px; text-align: right; font-family: Consolas, Monaco, 'Courier New', monospace; font-variant-numeric: tabular-nums; line-height: 1.3; }
.market-rise { color: #ff5c5c; }
.market-fall { color: #1ee88f; }
</style>
