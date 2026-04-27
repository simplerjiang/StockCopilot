<script setup>
const props = defineProps({
  detail: { type: Object, default: null },
  detailLoading: { type: Boolean, default: false },
  detailError: { type: String, default: '' },
  compareWindow: { type: String, default: '10d' },
  compareWindowLabel: { type: String, default: '10日主线' }
})

const emit = defineEmits(['navigate-stock'])

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
  const n = Number(value)
  return Number.isFinite(n) ? n.toFixed(1) : '—'
}
const isSparseSectorDetail = payload => {
  if (!payload?.snapshot) return false
  const memberTotal = Number(payload.snapshot.advancerCount ?? 0) + Number(payload.snapshot.declinerCount ?? 0) + Number(payload.snapshot.flatMemberCount ?? 0)
  return memberTotal === 0 && !payload.snapshot.leaderSymbol && (!payload.leaders?.length) && (!payload.news?.length)
}
const getLeaderEmptyHint = payload => (isSparseSectorDetail(payload) ? '数据不可用，龙头股明细尚未同步。' : '暂无龙头股数据。')
const getWindowStrength = item => {
  if (props.compareWindow === '5d') return item.strengthAvg5d
  if (props.compareWindow === '20d') return item.strengthAvg20d
  return item.strengthAvg10d
}
const strengthColor = value => value > 0 ? '#ff5c5c' : value < 0 ? '#1ee88f' : '#c2cad8'
const allBreadthZero = snapshot => {
  if ([snapshot.advancerCount, snapshot.declinerCount, snapshot.flatMemberCount].some(value => value == null)) return true
  return Number(snapshot.advancerCount ?? 0) === 0 && Number(snapshot.declinerCount ?? 0) === 0 && Number(snapshot.flatMemberCount ?? 0) === 0
}

const navigateToLeader = leader => {
  const symbol = String(leader?.symbol ?? '').trim()
  if (!symbol) return
  emit('navigate-stock', { symbol, name: leader?.name ?? '' })
}
</script>

<template>
  <aside v-if="detail || detailLoading || detailError" class="detail-panel">
    <div v-if="detailError" class="feedback error compact">{{ detailError }}</div>
    <div v-else-if="detailLoading" class="feedback compact">加载中...</div>
    <template v-else-if="detail">
      <header class="detail-head">
        <strong>{{ detail.snapshot.sectorName }}</strong>
        <span class="detail-code" :title="`板块代码：${detail.snapshot.sectorCode || '--'}`">{{ detail.snapshot.sectorCode || '--' }}</span>
        <span class="detail-change" :class="{ positive: detail.snapshot.changePercent >= 0, negative: detail.snapshot.changePercent < 0, 'market-rise': detail.snapshot.changePercent >= 0, 'market-fall': detail.snapshot.changePercent < 0 }">{{ formatSignedPercent(detail.snapshot.changePercent) }}</span>
      </header>

      <!-- 龙头股 TOP5 -->
      <section class="detail-block">
        <h4>龙头 TOP5</h4>
        <p v-if="isSparseSectorDetail(detail)" class="empty-hint">数据不可用，当前仅有有限快照。</p>
        <div v-if="!detail.leaders.length" class="empty-hint">{{ getLeaderEmptyHint(detail) }}</div>
        <div v-else class="leader-list">
          <button
            v-for="leader in detail.leaders.slice(0, 5)"
            :key="leader.symbol"
            type="button"
            class="leader-item leader-item-button"
            @click="navigateToLeader(leader)"
          >
            <span>#{{ leader.rankInSector }} {{ leader.name }} · {{ leader.symbol }}</span>
            <strong :class="{ positive: leader.changePercent >= 0, negative: leader.changePercent < 0, 'market-rise': leader.changePercent >= 0, 'market-fall': leader.changePercent < 0 }">{{ formatSignedPercent(leader.changePercent) }}</strong>
          </button>
        </div>
      </section>

      <!-- 板块内涨跌分布 -->
      <section class="detail-block">
        <h4>板块内涨跌</h4>
        <div v-if="allBreadthZero(detail.snapshot)" class="empty-hint">暂无涨跌分布数据</div>
        <div v-else class="breadth-bar">
          <span class="bar-up market-rise" :style="{ flex: detail.snapshot.advancerCount }">涨 {{ detail.snapshot.advancerCount }}</span>
          <span class="bar-flat" :style="{ flex: detail.snapshot.flatMemberCount || 1 }">平 {{ detail.snapshot.flatMemberCount }}</span>
          <span class="bar-down market-fall" :style="{ flex: detail.snapshot.declinerCount }">跌 {{ detail.snapshot.declinerCount }}</span>
        </div>
      </section>

      <!-- 近5日趋势 -->
      <section class="detail-block">
        <h4>近期趋势</h4>
        <div v-if="!detail.history.length" class="empty-hint">暂无历史数据</div>
        <div v-else class="trend-list">
          <div v-for="item in detail.history.slice(0, 5)" :key="item.tradingDate" class="trend-item">
            <span>{{ formatDate(item.tradingDate).slice(5, 10) }}</span>
            <span :class="{ positive: item.changePercent >= 0, negative: item.changePercent < 0, 'market-rise': item.changePercent >= 0, 'market-fall': item.changePercent < 0 }">{{ formatSignedPercent(item.changePercent) }}</span>
            <span :style="{ color: strengthColor(getWindowStrength(item)) }">强度 {{ formatOneDecimal(getWindowStrength(item)) }}</span>
          </div>
        </div>
      </section>

      <!-- 相关新闻 -->
      <section class="detail-block">
        <h4>相关新闻</h4>
        <div v-if="!detail.news.length" class="empty-hint">暂无相关新闻</div>
        <template v-else>
          <article v-for="item in detail.news.slice(0, 3)" :key="item.title" class="news-item">
            <span>{{ item.translatedTitle || item.title }}</span>
            <small>{{ item.source }} {{ formatDate(item.publishTime).slice(5, 16) }}</small>
          </article>
        </template>
      </section>
    </template>
  </aside>
</template>

<style scoped>
.detail-panel {
  display: grid;
  gap: 8px;
  padding: 10px;
  background: #1a2233;
  border: 1px solid #334155;
  color: #e6eaf2;
  align-content: start;
  min-width: 0;
}
.detail-head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 4px 10px;
  justify-content: space-between;
  padding-bottom: 6px;
  border-bottom: 1px solid #334155;
}
.detail-head strong { font-size: 14px; line-height: 1.3; min-width: 0; overflow-wrap: anywhere; }
.detail-code { font-size: 11px; color: #98a6bf; border: 1px solid #334155; padding: 1px 4px; line-height: 1.3; user-select: all; }
.detail-change { font-size: 14px; font-weight: 700; font-variant-numeric: tabular-nums; }
.detail-block { display: grid; gap: 4px; }
.detail-block h4 { margin: 0; font-size: 12px; color: #b2bccf; text-transform: uppercase; letter-spacing: 0.05em; line-height: 1.3; }
.empty-hint { font-size: 12px; color: #98a6bf; font-style: italic; line-height: 1.3; }
.leader-list, .trend-list { display: grid; gap: 2px; }
.leader-item, .trend-item {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  gap: 8px;
  padding: 2px 0;
  min-width: 0;
  font-size: 12px;
  line-height: 1.35;
}
.leader-item-button {
  width: 100%;
  border: 1px solid transparent;
  background: transparent;
  color: inherit;
  cursor: pointer;
  text-align: left;
}
.leader-item-button:hover {
  border-color: #334155;
  background: rgba(36, 49, 71, 0.45);
}
.leader-item > span,
.trend-item > span { min-width: 0; overflow-wrap: anywhere; }
.leader-item strong { font-variant-numeric: tabular-nums; }
.breadth-bar {
  display: flex;
  height: 20px;
  border: 1px solid #334155;
  overflow: hidden;
  font-size: 11px;
  line-height: 1.3;
}
.bar-up { background: rgba(255, 92, 92, 0.18); color: #ff5c5c; display: flex; align-items: center; justify-content: center; min-width: 0; padding: 0 4px; white-space: nowrap; }
.bar-flat { background: rgba(194, 202, 216, 0.2); color: #c2cad8; display: flex; align-items: center; justify-content: center; }
.bar-down { background: rgba(30, 232, 143, 0.18); color: #1ee88f; display: flex; align-items: center; justify-content: center; min-width: 0; padding: 0 4px; white-space: nowrap; }
.news-item {
  display: grid;
  gap: 2px;
  padding: 4px 0;
  border-top: 1px solid rgba(51,65,85,0.75);
  font-size: 12px;
  line-height: 1.35;
}
.news-item small { color: #98a6bf; font-size: 12px; line-height: 1.3; }
.positive { color: #ff5c5c; }
.negative { color: #1ee88f; }
.market-rise { color: #ff5c5c; }
.market-fall { color: #1ee88f; }
.feedback {
  padding: 6px 10px;
  border: 1px solid #334155;
  background: #1a2233;
  font-size: 12px;
  color: #b2bccf;
  line-height: 1.35;
}
.feedback.error { color: #ff5c5c; border-color: #6a2a2a; background: #251417; }
.feedback.compact { padding: 4px 8px; }
</style>
