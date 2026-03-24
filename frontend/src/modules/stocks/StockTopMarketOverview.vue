<script setup>
defineProps({
  enabled: { type: Boolean, required: true },
  loading: { type: Boolean, default: false },
  error: { type: String, default: '' },
  overview: { type: Object, default: null },
  detail: { type: Object, default: null },
  currentStockRealtimeQuote: { type: Object, default: null },
  stockRealtimeDomesticIndices: { type: Array, default: () => [] },
  stockRealtimeGlobalIndices: { type: Array, default: () => [] },
  stockRealtimeRelativeStrength: { type: Object, default: null },
  stockRealtimeBreadthBias: { type: Object, default: null },
  formatDate: { type: Function, required: true },
  getChangeClass: { type: Function, required: true },
  formatSignedNumber: { type: Function, required: true },
  formatSignedPercent: { type: Function, required: true },
  formatSignedRealtimeAmount: { type: Function, required: true },
  formatRealtimeMoney: { type: Function, required: true }
})

defineEmits(['refresh', 'toggle'])
</script>

<template>
  <section class="copilot-card page-top-market-overview-belt">
    <div class="market-overview-belt-header">
      <div>
        <p class="market-overview-kicker">Top Market Tape</p>
        <h3>顶部市场总览带</h3>
        <p class="muted">把当前标的、A 股主场、全球指数和资金温度压成一屏，开页先看这里。</p>
      </div>
      <div class="stock-realtime-actions">
        <button @click="$emit('refresh')" :disabled="loading">刷新市场</button>
        <button @click="$emit('toggle')">{{ enabled ? '隐藏' : '显示' }}</button>
      </div>
    </div>

    <div class="market-overview-belt-content">
      <p v-if="!enabled" class="muted">顶部市场总览带已隐藏，可随时重新展开。</p>
      <template v-else>
        <p v-if="error" class="muted error">{{ error }}</p>
        <p v-else-if="loading && !overview" class="muted">加载中...</p>
        <template v-else-if="overview">
          <div class="market-overview-meta">
            <span>{{ formatDate(overview.snapshotTime) }}</span>
            <span v-if="detail?.quote?.symbol">当前 {{ detail.quote.symbol }}</span>
            <span>涨红跌绿</span>
          </div>

          <div class="market-overview-grid">
            <article class="market-overview-hero" :class="{ 'is-placeholder': !currentStockRealtimeQuote }">
              <div class="market-overview-hero-head">
                <div class="market-overview-hero-name">
                  <p class="muted">当前标的</p>
                  <strong>{{ currentStockRealtimeQuote?.name ?? detail?.quote?.name ?? '未选股票' }}</strong>
                  <small>{{ currentStockRealtimeQuote?.symbol ?? detail?.quote?.symbol ?? '先搜索股票即可加入对照' }}</small>
                </div>
                <div v-if="currentStockRealtimeQuote" class="market-overview-hero-price">
                  <strong :class="getChangeClass(currentStockRealtimeQuote.changePercent)">{{ currentStockRealtimeQuote.price.toFixed(2) }}</strong>
                  <small :class="getChangeClass(currentStockRealtimeQuote.changePercent)">
                    {{ formatSignedNumber(currentStockRealtimeQuote.change) }} / {{ formatSignedPercent(currentStockRealtimeQuote.changePercent) }}
                  </small>
                </div>
              </div>
              <p v-if="currentStockRealtimeQuote" class="market-overview-hero-summary">成交额 {{ formatRealtimeMoney(currentStockRealtimeQuote.turnoverAmount) }}</p>
              <p v-else class="market-overview-hero-summary muted">未选个股时，这里会保留市场全局快照，选股后自动补入个股强弱对照。</p>
              <div class="market-overview-tag-row">
                <span v-if="stockRealtimeRelativeStrength" class="market-overview-tag" :class="getChangeClass(stockRealtimeRelativeStrength.spread)">
                  {{ stockRealtimeRelativeStrength.label }} {{ formatSignedPercent(stockRealtimeRelativeStrength.spread) }}
                </span>
                <span class="market-overview-tag" :class="getChangeClass(overview.mainCapitalFlow?.mainNetInflow)">主力 {{ formatSignedRealtimeAmount(overview.mainCapitalFlow?.mainNetInflow) }}</span>
                <span class="market-overview-tag" :class="getChangeClass(overview.northboundFlow?.totalNetInflow)">北向 {{ formatSignedRealtimeAmount(overview.northboundFlow?.totalNetInflow) }}</span>
              </div>
            </article>

            <section class="market-overview-cluster">
              <div class="market-overview-cluster-head">
                <strong>A 股主场</strong>
                <small>三大指数与当前市场底色</small>
              </div>
              <div v-if="stockRealtimeDomesticIndices.length" class="market-overview-quote-list">
                <article v-for="item in stockRealtimeDomesticIndices" :key="`domestic-${item.symbol}`" class="market-overview-quote-card">
                  <div class="market-overview-quote-card-head">
                    <div>
                      <strong>{{ item.name }}</strong>
                      <small>{{ item.symbol }}</small>
                    </div>
                    <strong :class="getChangeClass(item.changePercent)">{{ item.price.toFixed(2) }}</strong>
                  </div>
                  <div class="market-overview-quote-card-foot">
                    <small :class="getChangeClass(item.changePercent)">{{ formatSignedPercent(item.changePercent) }}</small>
                    <small :class="getChangeClass(item.change)">{{ formatSignedNumber(item.change) }}</small>
                  </div>
                </article>
              </div>
              <p v-else class="muted">暂无 A 股指数数据。</p>
            </section>

            <section class="market-overview-cluster market-overview-cluster-global">
              <div class="market-overview-cluster-head">
                <strong>全球指数</strong>
                <small>港股、日股、美股、欧洲与亚洲联动</small>
              </div>
              <div v-if="stockRealtimeGlobalIndices.length" class="market-overview-quote-list market-overview-quote-list-global">
                <article v-for="item in stockRealtimeGlobalIndices" :key="`global-${item.symbol}`" class="market-overview-quote-card">
                  <div class="market-overview-quote-card-head">
                    <div>
                      <strong>{{ item.name }}</strong>
                      <small>{{ item.symbol }}</small>
                    </div>
                    <strong :class="getChangeClass(item.changePercent)">{{ item.price.toFixed(2) }}</strong>
                  </div>
                  <div class="market-overview-quote-card-foot">
                    <small :class="getChangeClass(item.changePercent)">{{ formatSignedPercent(item.changePercent) }}</small>
                    <small :class="getChangeClass(item.change)">{{ formatSignedNumber(item.change) }}</small>
                  </div>
                </article>
              </div>
              <p v-else class="muted">暂无全球指数数据。</p>
            </section>
          </div>

          <div class="market-overview-pulse-grid">
            <article class="market-overview-pulse-card">
              <span>主力净流</span>
              <strong :class="getChangeClass(overview.mainCapitalFlow?.mainNetInflow)">{{ formatSignedRealtimeAmount(overview.mainCapitalFlow?.mainNetInflow) }}</strong>
              <small>超大单 {{ formatSignedRealtimeAmount(overview.mainCapitalFlow?.superLargeOrderNetInflow) }}</small>
            </article>

            <article class="market-overview-pulse-card">
              <span>北向净流</span>
              <strong :class="getChangeClass(overview.northboundFlow?.totalNetInflow)">{{ formatSignedRealtimeAmount(overview.northboundFlow?.totalNetInflow) }}</strong>
              <small>沪 {{ formatSignedRealtimeAmount(overview.northboundFlow?.shanghaiNetInflow) }} / 深 {{ formatSignedRealtimeAmount(overview.northboundFlow?.shenzhenNetInflow) }}</small>
            </article>

            <article class="market-overview-pulse-card">
              <span>市场广度</span>
              <strong :class="getChangeClass(stockRealtimeBreadthBias?.delta)">{{ overview.breadth?.advancers ?? 0 }} / {{ overview.breadth?.decliners ?? 0 }}</strong>
              <small>{{ stockRealtimeBreadthBias?.label ?? '多空拉锯' }} · 平盘 {{ overview.breadth?.flatCount ?? 0 }}</small>
            </article>

            <article class="market-overview-pulse-card">
              <span>封板温度</span>
              <strong :class="getChangeClass((overview.breadth?.limitUpCount ?? 0) - (overview.breadth?.limitDownCount ?? 0))">{{ overview.breadth?.limitUpCount ?? 0 }} / {{ overview.breadth?.limitDownCount ?? 0 }}</strong>
              <small>涨停 / 跌停</small>
            </article>

            <article class="market-overview-pulse-card">
              <span>个股对照</span>
              <strong :class="getChangeClass(stockRealtimeRelativeStrength?.spread)">{{ stockRealtimeRelativeStrength ? `${stockRealtimeRelativeStrength.label} ${formatSignedPercent(stockRealtimeRelativeStrength.spread)}` : '未选标的' }}</strong>
              <small v-if="currentStockRealtimeQuote">{{ currentStockRealtimeQuote.name }} {{ formatSignedPercent(currentStockRealtimeQuote.changePercent) }}</small>
              <small v-else>搜索股票后自动加入比较</small>
            </article>
          </div>
        </template>
        <p v-else class="muted">暂无市场实时总览数据。</p>
      </template>
    </div>
  </section>
</template>

<style scoped>
.page-top-market-overview-belt { display:grid; gap:0.9rem; margin:1rem 0; padding:1rem 1.1rem; border-radius:24px; border:1px solid rgba(251,146,60,.2); background:radial-gradient(circle at top left, rgba(248,113,113,.14), transparent 24%), radial-gradient(circle at top right, rgba(34,197,94,.12), transparent 22%), linear-gradient(145deg, rgba(255,250,245,.98), rgba(248,250,252,.98)); box-shadow:0 18px 48px rgba(15,23,42,.09); }
.market-overview-belt-header,.market-overview-quote-card-head,.market-overview-quote-card-foot,.market-overview-cluster-head,.stock-realtime-actions,.market-overview-meta,.market-overview-tag-row { display:flex; gap:.75rem; flex-wrap:wrap; justify-content:space-between; }
.market-overview-belt-content,.market-overview-grid,.market-overview-hero,.market-overview-cluster,.market-overview-pulse-grid,.market-overview-quote-list { display:grid; gap:.75rem; }
.market-overview-grid { grid-template-columns:minmax(240px,1.05fr) minmax(0,1fr) minmax(0,1fr); }
.market-overview-hero,.market-overview-cluster,.market-overview-pulse-card { border-radius:18px; border:1px solid rgba(148,163,184,.16); background:rgba(255,255,255,.88); }
.market-overview-hero,.market-overview-cluster,.market-overview-pulse-card,.market-overview-quote-card { padding:.85rem; }
.market-overview-quote-list-global,.market-overview-pulse-grid { grid-template-columns:repeat(auto-fit,minmax(160px,1fr)); }
.market-overview-tag { display:inline-flex; align-items:center; padding:.35rem .62rem; border-radius:999px; background:rgba(241,245,249,.95); border:1px solid rgba(148,163,184,.14); }
.market-overview-kicker { margin:0 0 .2rem; font-size:.68rem; letter-spacing:.18em; text-transform:uppercase; color:#b45309; }
.market-overview-belt-header h3,.market-overview-hero-name strong,.market-overview-cluster-head strong,.market-overview-quote-card-head strong:first-child { color:#0f172a; }
.market-overview-hero-name,.market-overview-hero-price { display:grid; gap:.12rem; }
.market-overview-hero-head { display:flex; align-items:flex-start; justify-content:space-between; gap:.85rem; }
.market-overview-hero-price { text-align:right; }
.market-overview-hero-price strong { font-size:1.45rem; }
.market-overview-hero-name small,.market-overview-hero-summary,.market-overview-hero-price small,.market-overview-cluster-head small,.market-overview-quote-card small { color:#64748b; }
.market-overview-hero-summary { margin:0; }
.stock-realtime-actions button { border:none; border-radius:999px; padding:.4rem .8rem; cursor:pointer; background:rgba(15,23,42,.08); color:#0f172a; }
.stock-realtime-actions button:disabled { opacity:.6; cursor:not-allowed; }
.text-rise { color:#dc2626; }
.text-fall { color:#16a34a; }
.error { color:#b91c1c; }
@media (max-width:1100px) { .market-overview-grid { grid-template-columns:1fr; } }
@media (max-width:720px) { .market-overview-belt-header,.market-overview-hero-head,.market-overview-cluster-head,.market-overview-quote-card-head,.market-overview-quote-card-foot { flex-direction:column; } .market-overview-hero-price { text-align:left; } .market-overview-quote-list-global { grid-template-columns:1fr; } }
</style>