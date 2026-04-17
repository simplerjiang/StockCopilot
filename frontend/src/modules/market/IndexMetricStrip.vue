<script setup>
const props = defineProps({
  indices: { type: Array, default: () => [] },
  limitUpCount: { type: Number, default: 0 },
  limitDownCount: { type: Number, default: 0 },
  brokenBoardCount: { type: Number, default: 0 },
  brokenBoardRate: { type: Number, default: 0 },
  maxLimitUpStreak: { type: Number, default: 0 },
  limitUpCount5dAvg: { type: Number, default: 0 },
  brokenBoardRate5dAvg: { type: Number, default: 0 },
  limitTimestamp: { type: String, default: '' },
  brokenBoardUnavailable: { type: Boolean, default: false },
  limitUpUnavailable: { type: Boolean, default: false },
  maxStreakUnavailable: { type: Boolean, default: false }
})

const chgColor = v => (typeof v === 'number' && Number.isFinite(v)) ? (v > 0 ? '#ff5c5c' : v < 0 ? '#1ee88f' : '#c2cad8') : '#c2cad8'
const fmtPct = v => { const n = Number(v ?? 0); return `${n >= 0 ? '+' : ''}${n.toFixed(2)}%` }
const fmtPrice = v => v != null ? Number(v).toLocaleString(undefined, { minimumFractionDigits: 1, maximumFractionDigits: 1 }) : '--'
const fmtOneDecimal = v => {
  const n = Number(v ?? 0)
  return Number.isFinite(n) ? n.toFixed(1) : '--'
}
</script>

<template>
  <div class="is">
    <!-- Zone 1: Indices -->
    <div v-for="idx in indices" :key="idx.symbol" class="is-cell">
      <span class="is-lbl">{{ idx.name }}</span>
      <span class="is-price">{{ fmtPrice(idx.price) }}</span>
      <span class="is-chg" :style="{ color: chgColor(idx.changePercent) }">{{ fmtPct(idx.changePercent) }}</span>
    </div>

    <div class="is-div"></div>

    <!-- Zone 2: Limit stats -->
    <div class="is-cell">
      <span class="is-lbl">涨停</span>
      <span class="is-val rise">{{ limitUpCount }}</span>
      <span class="is-sub">5日均 {{ limitUpUnavailable ? '--' : fmtOneDecimal(limitUpCount5dAvg) }}</span>
    </div>
    <div class="is-cell">
      <span class="is-lbl">跌停</span>
      <span class="is-val fall">{{ limitDownCount }}</span>
      <span class="is-sub">连板王 {{ maxStreakUnavailable ? '--' : maxLimitUpStreak }}</span>
    </div>
    <div class="is-cell">
      <span class="is-lbl">炸板率</span>
      <span class="is-val">{{ brokenBoardUnavailable ? '--' : `${fmtOneDecimal(brokenBoardRate)}%` }}</span>
      <span class="is-sub">5日均 {{ brokenBoardUnavailable ? '--' : `${fmtOneDecimal(brokenBoardRate5dAvg)}%` }}</span>
    </div>
  </div>
</template>

<style scoped>
.is { display: flex; flex-wrap: wrap; align-items: stretch; padding: 4px 6px; background: #1a2233; border-bottom: 1px solid #334155; overflow: hidden; }
.is-cell { display: flex; flex: 1 1 108px; flex-direction: column; min-width: 0; padding: 4px 10px; border-right: 1px solid rgba(51,65,85,0.85); }
.is-cell:last-child { border-right: none; }
.is-lbl { font-size: 12px; color: #b2bccf; line-height: 1.3; }
.is-price { font-size: 15px; color: #e6eaf2; font-weight: 600; font-family: Consolas, Monaco, 'Courier New', monospace; font-variant-numeric: tabular-nums; line-height: 1.35; }
.is-chg { font-size: 13px; font-family: Consolas, Monaco, 'Courier New', monospace; font-variant-numeric: tabular-nums; font-weight: 500; line-height: 1.3; }
.is-val { font-size: 15px; color: #e6eaf2; font-weight: 600; font-family: Consolas, Monaco, 'Courier New', monospace; font-variant-numeric: tabular-nums; line-height: 1.35; }
.is-val.rise { color: #ff5c5c; }
.is-val.fall { color: #1ee88f; }
.is-sub { font-size: 11px; color: #98a6bf; line-height: 1.3; }
.is-div { display: none; }
</style>
