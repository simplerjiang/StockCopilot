<script setup>
const props = defineProps({
  sectors: { type: Array, default: () => [] },
  selectedSectorCode: { type: String, default: '' },
  page: { type: Number, default: 1 },
  totalPages: { type: Number, default: 1 },
  compareWindow: { type: String, default: '10d' },
  compareWindowLabel: { type: String, default: '10日主线' },
  pageOffset: { type: Number, default: 0 },
  emptyTitle: { type: String, default: '当前暂无板块榜单。' },
  emptyBody: { type: String, default: '请稍后重试或切换轮动维度。' }
})

const emit = defineEmits(['select', 'prev', 'next'])

const cnDateTimeFormatter = new Intl.DateTimeFormat('zh-CN', {
  timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit',
  hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false
})
const formatDate = value => {
  if (!value) return '--'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '--'
  return cnDateTimeFormatter.format(date)
}
const formatSignedPercent = value => {
  if (value === null || value === undefined || value === '') return '—'
  const n = Number(value ?? 0)
  if (!Number.isFinite(n)) return '—'
  return `${n >= 0 ? '+' : ''}${n.toFixed(2)}%`
}
const formatOneDecimal = value => {
  if (value === null || value === undefined || value === '') return '—'
  const n = Number(value)
  return Number.isFinite(n) ? n.toFixed(1) : '—'
}
const getWindowStrength = item => {
  if (props.compareWindow === '5d') return item.strengthAvg5d
  if (props.compareWindow === '20d') return item.strengthAvg20d
  return item.strengthAvg10d
}
const getWindowRankChange = item => {
  if (props.compareWindow === '5d') return item.rankChange5d
  if (props.compareWindow === '20d') return item.rankChange20d
  return item.rankChange10d
}
const getWindowRankLabel = item => {
  const value = getWindowRankChange(item)
  if (value === null || value === undefined || value === '') return '—'
  return `${value >= 0 ? '+' : ''}${value}`
}
const strengthColor = value => value > 0 ? '#ff5c5c' : value < 0 ? '#1ee88f' : '#c2cad8'
const isMembersUnavailable = item => Boolean(item?.membersUnavailable)
const getLeaderParts = item => [item?.leaderName, item?.leaderSymbol]
  .map(value => String(value ?? '').trim())
  .filter(Boolean)
const hasLeader = item => getLeaderParts(item).length > 0
const getLeaderText = item => getLeaderParts(item).join(' · ')
</script>

<template>
  <div class="sector-list">
    <div v-if="!sectors.length" class="sector-empty-state">
      <strong>{{ emptyTitle }}</strong>
      <p>{{ emptyBody }}</p>
    </div>
    <template v-else>
      <button
        v-for="(item, index) in sectors"
        :key="`${item.boardType}-${item.sectorCode}`"
        type="button"
        class="sector-card"
        :class="{ active: item.sectorCode === selectedSectorCode, unavailable: isMembersUnavailable(item) }"
        @click="emit('select', item.sectorCode)"
      >
        <div class="sector-head">
          <span class="sector-order">{{ `当前第${pageOffset + index + 1}` }}</span>
          <strong class="sector-name">{{ item.sectorName }}</strong>
          <span class="sector-code" :title="`板块代码：${item.sectorCode}`">{{ item.sectorCode }}</span>
          <span v-if="item.isMainline" class="mainline-badge">主线</span>
          <span v-if="isMembersUnavailable(item)" class="degraded-badge">降级快照</span>
          <span class="sector-change" :class="{ positive: item.changePercent >= 0, negative: item.changePercent < 0, 'market-rise': item.changePercent >= 0, 'market-fall': item.changePercent < 0 }">{{ formatSignedPercent(item.changePercent) }}</span>
          <span v-if="isMembersUnavailable(item)" class="sector-strength unavailable-text">数据不可用</span>
          <span v-else class="sector-strength">★{{ formatOneDecimal(item.strengthScore) }}</span>
        </div>
        <div class="sector-metrics">
          <span v-if="isMembersUnavailable(item)" class="unavailable-text">{{ compareWindowLabel }} 成分股待补齐</span>
          <span v-else :style="{ color: strengthColor(getWindowStrength(item)) }">{{ compareWindowLabel }} {{ formatOneDecimal(getWindowStrength(item)) }}</span>
          <span v-if="isMembersUnavailable(item)" class="unavailable-text">扩散 数据不可用</span>
          <span v-else>扩散 {{ formatOneDecimal(item.diffusionRate) }}</span>
          <span v-if="isMembersUnavailable(item)" class="unavailable-text">成员分布待补齐</span>
          <span v-else>{{ getWindowRankLabel(item) }}</span>
          <span class="sector-ts">{{ formatDate(item.snapshotTime) }}</span>
        </div>
        <div v-if="!isMembersUnavailable(item) && hasLeader(item)" class="sector-leader" :title="`龙头 ${getLeaderText(item)}`">
          <span>龙头</span>
          <strong>{{ getLeaderText(item) }}</strong>
        </div>
      </button>

      <footer class="pagination">
        <button @click="emit('prev')" :disabled="page <= 1">&lt;</button>
        <span>{{ page }} / {{ totalPages }}</span>
        <button @click="emit('next')" :disabled="page >= totalPages">&gt;</button>
      </footer>
    </template>
  </div>
</template>

<style scoped>
.sector-list {
  display: grid;
  gap: 4px;
  align-content: start;
  min-width: 0;
}
.sector-empty-state {
  padding: 10px;
  border: 1px dashed #334155;
  background: #1a2233;
  color: #b2bccf;
}
.sector-empty-state p { margin: 4px 0 0; font-size: 12px; color: #98a6bf; line-height: 1.3; }
.sector-card {
  display: grid;
  gap: 4px;
  width: 100%;
  min-width: 0;
  padding: 7px 10px;
  background: #1a2233;
  border: 1px solid #334155;
  border-left: 3px solid transparent;
  text-align: left;
  cursor: pointer;
  color: #e6eaf2;
  font-family: inherit;
  font-size: 12px;
  transition: all 0.15s;
}
.sector-card:hover { background: #243147; border-color: #8ca0bf; }
.sector-card.active { border-left-color: #1ee88f; background: #243147; }
.sector-card.unavailable { border-color: #4a566d; background: #18202f; }
.sector-head {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  gap: 8px;
  line-height: 1.3;
}
.sector-order {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 18px;
  background: #111827;
  color: #b2bccf;
  border: 1px solid #334155;
  font-size: 11px;
  font-weight: 700;
}
.sector-name { font-weight: 600; flex: 1 1 160px; min-width: 0; overflow-wrap: anywhere; }
.sector-code { font-size: 11px; color: #98a6bf; border: 1px solid #334155; padding: 1px 4px; line-height: 1.3; user-select: all; }
.mainline-badge { font-size: 11px; color: #1ee88f; background: rgba(30,232,143,0.14); border: 1px solid rgba(30,232,143,0.35); padding: 1px 4px; line-height: 1.3; }
.degraded-badge { font-size: 11px; color: #c2cad8; background: rgba(148,163,184,0.14); border: 1px solid rgba(148,163,184,0.35); padding: 1px 4px; line-height: 1.3; }
.sector-change { margin-left: auto; font-weight: 600; font-variant-numeric: tabular-nums; }
.sector-strength { color: #c2cad8; font-size: 11px; line-height: 1.3; }
.unavailable-text { color: #98a6bf; font-style: italic; }
.sector-metrics {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  font-size: 12px;
  color: #b2bccf;
  line-height: 1.35;
}
.sector-metrics > span { min-width: 0; }
.sector-leader {
  display: flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
  font-size: 12px;
  color: #b2bccf;
  line-height: 1.35;
}
.sector-leader span { color: #98a6bf; }
.sector-leader strong { min-width: 0; overflow-wrap: anywhere; font-weight: 600; color: #e6eaf2; }
.sector-ts { margin-left: auto; color: #98a6bf; overflow-wrap: anywhere; }
.positive { color: #ff5c5c; }
.negative { color: #1ee88f; }
.market-rise { color: #ff5c5c; }
.market-fall { color: #1ee88f; }
@media (max-width: 780px) {
  .sector-change { margin-left: 0; }
  .sector-ts { margin-left: 0; }
}

/* pagination */
.pagination {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 12px;
  padding: 6px 0;
  font-size: 12px;
  color: #b2bccf;
  line-height: 1.3;
}
.pagination button {
  width: 28px;
  height: 24px;
  border: 1px solid #334155;
  background: #111827;
  color: #b2bccf;
  cursor: pointer;
  font-size: 13px;
}
.pagination button:hover:not(:disabled) { border-color: #91a4c3; color: #e6eaf2; }
.pagination button:disabled { opacity: 0.45; cursor: not-allowed; }
</style>
