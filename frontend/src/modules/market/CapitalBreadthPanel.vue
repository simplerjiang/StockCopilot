<script setup>
import { computed } from 'vue'
import MetricCell from './MetricCell.vue'

const props = defineProps({
  mainCapitalFlow: { type: Object, default: null },
  northboundFlow: { type: Object, default: null },
  diffusionScore: { type: Number, default: null },
  continuationScore: { type: Number, default: null },
  top3SectorTurnoverShare: { type: Number, default: null },
  top10SectorTurnoverShare: { type: Number, default: null },
  top3SectorTurnoverShare5dAvg: { type: Number, default: null },
  top10SectorTurnoverShare5dAvg: { type: Number, default: null },
  totalTurnover: { type: Number, default: 0 },
  totalTurnoverUnit: { type: String, default: '' },
  totalTurnoverUnitLabel: { type: String, default: '' },
  advancers: { type: Number, default: null },
  decliners: { type: Number, default: null },
  mainFlowUnavailable: { type: Boolean, default: false },
  northboundUnavailable: { type: Boolean, default: false },
  diffusionUnavailable: { type: Boolean, default: false },
  continuationUnavailable: { type: Boolean, default: false },
  turnoverShareUnavailable: { type: Boolean, default: false }
})

const fmtYi = (v, unit = '亿元') => {
  if (v == null || !Number.isFinite(Number(v))) return '--'
  const n = String(unit).includes('亿') ? Number(v) : Number(v) / 100_000_000
  return `${n >= 0 ? '+' : ''}${n.toFixed(1)}亿`
}
const fmtTurnover = v => {
  const numericValue = Number(v ?? 0)
  if (!Number.isFinite(numericValue) || numericValue <= 0) return '--'
  const unit = String(props.totalTurnoverUnit || '').trim().toLowerCase()
  const unitLabel = String(props.totalTurnoverUnitLabel || '').trim()
  if (unitLabel.includes('亿')) return `${numericValue.toFixed(0)}亿`
  if (unit === 'cny' || unitLabel === '元' || unitLabel.includes('人民币')) return `${(numericValue / 100_000_000).toFixed(0)}亿`
  if (unitLabel) return `${numericValue.toFixed(0)}${unitLabel}`
  if (props.totalTurnoverUnit) return `${numericValue.toFixed(0)}${props.totalTurnoverUnit}`
  return `${(numericValue / 100_000_000).toFixed(0)}亿`
}

const mainFlowVal = computed(() => {
  if (!props.mainCapitalFlow || props.mainFlowUnavailable) return null
  return fmtYi(props.mainCapitalFlow.mainNetInflow, props.mainCapitalFlow.amountUnit)
})
const mainFlowTrend = computed(() => {
  if (!props.mainCapitalFlow || props.mainFlowUnavailable) return null
  const v = props.mainCapitalFlow.mainNetInflow
  return v > 0 ? 'up' : v < 0 ? 'down' : 'flat'
})
const northVal = computed(() => {
  if (!props.northboundFlow || props.northboundUnavailable) return null
  return fmtYi(props.northboundFlow.totalNetInflow, props.northboundFlow.amountUnit)
})
const northTrend = computed(() => {
  if (!props.northboundFlow || props.northboundUnavailable) return null
  const v = props.northboundFlow.totalNetInflow
  return v > 0 ? 'up' : v < 0 ? 'down' : 'flat'
})
const fmtCount = value => Number.isFinite(Number(value)) ? Number(value) : '—'
const adRatio = computed(() => `${fmtCount(props.advancers)}:${fmtCount(props.decliners)}`)
const scoreColor = v => v > 60 ? '#ff5c5c' : v >= 40 ? '#c2cad8' : '#1ee88f'
const fmtScore = v => Number.isFinite(Number(v)) ? Number(v).toFixed(1) : '—'
</script>

<template>
  <div class="cp">
    <div class="cp-grid">
      <!-- Row 1 -->
      <MetricCell
        label="主力净流入"
        :value="mainFlowVal ?? '数据不可用'"
        :status="mainFlowVal ? 'ok' : 'unavailable'"
        :trend="mainFlowTrend"
        :timestamp="mainFlowVal ? mainCapitalFlow?.snapshotTime : ''"
      />
      <MetricCell
        label="北向资金"
        :value="northVal ?? '数据不可用'"
        :status="northVal ? 'ok' : 'unavailable'"
        :trend="northTrend"
        :timestamp="northVal ? northboundFlow?.snapshotTime : ''"
      />
      <!-- Row 2 -->
      <MetricCell label="涨跌比" :value="adRatio" />
      <MetricCell label="总成交额" :value="fmtTurnover(totalTurnover)" />
      <!-- Row 3 -->
      <MetricCell
        label="扩散指数"
        :value="diffusionUnavailable ? '数据不可用' : fmtScore(diffusionScore)"
        :status="diffusionUnavailable ? 'unavailable' : 'ok'"
        :style="diffusionUnavailable ? null : { color: scoreColor(diffusionScore) }"
      />
      <MetricCell
        label="持续指数"
        :value="continuationUnavailable ? '数据不可用' : fmtScore(continuationScore)"
        :status="continuationUnavailable ? 'unavailable' : 'ok'"
        :style="continuationUnavailable ? null : { color: scoreColor(continuationScore) }"
      />
    </div>
    <div class="cp-foot">
      <template v-if="turnoverShareUnavailable">
        热门板块TOP3占比 数据不可用
        <span class="cp-avg">(5日均 数据不可用)</span>
      </template>
      <template v-else>
        热门板块TOP3占比 {{ fmtScore(top3SectorTurnoverShare) }}%
        <span class="cp-avg">(5日均 {{ fmtScore(top3SectorTurnoverShare5dAvg) }}%)</span>
      </template>
    </div>
  </div>
</template>

<style scoped>
.cp { padding: 6px 8px; background: #1a2233; border: 1px solid #334155; min-width: 0; }
.cp-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0; }
.cp-grid > * { min-width: 0; }
.cp-grid > * { border-bottom: 1px solid rgba(51,65,85,0.75); border-right: 1px solid rgba(51,65,85,0.75); }
.cp-grid > *:nth-child(2n) { border-right: none; }
.cp-grid > *:nth-last-child(-n+2) { border-bottom: none; }
.cp-foot { font-size: 12px; color: #b2bccf; padding: 4px 8px 0; font-family: Consolas, Monaco, 'Courier New', monospace; font-variant-numeric: tabular-nums; line-height: 1.4; overflow-wrap: anywhere; }
.cp-avg { color: #98a6bf; font-size: 12px; }
@media (max-width: 720px) {
  .cp-grid { grid-template-columns: minmax(0, 1fr); }
  .cp-grid > * { border-right: none; }
  .cp-grid > *:nth-last-child(-n+2) { border-bottom: 1px solid rgba(51,65,85,0.75); }
  .cp-grid > *:last-child { border-bottom: none; }
}
</style>
