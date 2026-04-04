<script setup>
import { computed, onMounted, ref } from 'vue'
import { useToast } from '../../composables/useToast.js'

const toast = useToast()

const sourceTagDisplayNames = {
  'sina-roll-market': '新浪财经', 'cls-telegraph': '财联社',
  'eastmoney-market-news': '东方财富·全球', 'eastmoney-ashare-news': '东方财富·A股',
  'gnews-cn-stocks': 'Google·A股', 'gnews-cn-finance': 'Google·金融',
  'gnews-cn-macro': 'Google·宏观', 'gnews-us-stocks': 'Google·美股',
  'gnews-us-macro': 'Google·美宏观', 'gnews-global-macro': 'Google·全球',
  'gnews-reuters': 'Reuters', 'gnews-bloomberg': 'Bloomberg',
  'gnews-ft': 'FT金融时报', 'gnews-wsj': 'WSJ华尔街日报',
  'cnbc-finance-rss': 'CNBC金融', 'cnbc-us-markets-rss': 'CNBC美股',
  'cnbc-economy-rss': 'CNBC经济', 'cnbc-world-rss': 'CNBC国际',
  'marketwatch-top-rss': 'MarketWatch', 'marketwatch-pulse-rss': 'MW脉搏',
  'bbc-business-rss': 'BBC商业', 'nyt-business-rss': 'NYT商业',
  'seeking-alpha-rss': 'Seeking Alpha', 'investing-com-rss': 'Investing.com',
  'sky-business-rss': 'Sky商业', 'cointelegraph-rss': 'CoinTelegraph'
}
function formatSourceTag(tag) { return sourceTagDisplayNames[tag] || tag }

const keyword = ref('')
const level = ref('')
const sentiment = ref('')
const page = ref(1)
const pageSize = ref(20)
const total = ref(0)
const loading = ref(false)
const error = ref('')
const items = ref([])

const cnDateTimeFormatter = new Intl.DateTimeFormat('zh-CN', {
  timeZone: 'Asia/Shanghai',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hour12: false
})

const levelOptions = [
  { value: '', label: '全部层级' },
  { value: 'market', label: '大盘' },
  { value: 'sector', label: '板块' },
  { value: 'stock', label: '个股' }
]

const sentimentOptions = [
  { value: '', label: '全部情绪' },
  { value: '利好', label: '利好' },
  { value: '中性', label: '中性' },
  { value: '利空', label: '利空' }
]

const totalPages = computed(() => Math.max(1, Math.ceil((total.value || 0) / pageSize.value)))
const pageSummary = computed(() => `第 ${page.value} / ${totalPages.value} 页`)
const resultSummary = computed(() => `共 ${total.value} 条资讯`)

const visiblePages = computed(() => {
  const tp = totalPages.value
  const cp = page.value
  if (tp <= 7) return Array.from({ length: tp }, (_, i) => i + 1)
  const pages = [1]
  let start = Math.max(2, cp - 1)
  let end = Math.min(tp - 1, cp + 1)
  if (start > 2) pages.push('...')
  for (let i = start; i <= end; i++) pages.push(i)
  if (end < tp - 1) pages.push('...')
  pages.push(tp)
  return pages
})

const normalizeArchiveItem = item => ({
  level: item?.level ?? item?.Level ?? '',
  symbol: item?.symbol ?? item?.Symbol ?? '',
  name: item?.name ?? item?.Name ?? '',
  sectorName: item?.sectorName ?? item?.SectorName ?? '',
  title: item?.title ?? item?.Title ?? '',
  translatedTitle: item?.translatedTitle ?? item?.TranslatedTitle ?? '',
  source: item?.source ?? item?.Source ?? '',
  sourceTag: item?.sourceTag ?? item?.SourceTag ?? '',
  category: item?.category ?? item?.Category ?? '',
  sentiment: item?.sentiment ?? item?.Sentiment ?? '中性',
  publishTime: item?.publishTime ?? item?.PublishTime ?? '',
  crawledAt: item?.crawledAt ?? item?.CrawledAt ?? '',
  url: item?.url ?? item?.Url ?? '',
  aiTarget: item?.aiTarget ?? item?.AiTarget ?? '',
  aiTags: Array.isArray(item?.aiTags ?? item?.AiTags) ? (item.aiTags ?? item.AiTags) : [],
  isAiProcessed: item?.isAiProcessed ?? item?.IsAiProcessed ?? false,
})

const formatDate = value => {
  if (!value) return ''
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''
  return cnDateTimeFormatter.format(date)
}

const getHeadline = item => item.translatedTitle || item.title || ''

const getLevelLabel = value => {
  if (value === 'market') return '大盘'
  if (value === 'sector') return '板块'
  if (value === 'stock') return '个股'
  return '未知'
}

const getSentimentClass = value => {
  if (value === '利好') return 'is-positive'
  if (value === '利空') return 'is-negative'
  return 'is-neutral'
}

const getLevelClass = value => {
  if (value === 'market') return 'is-market'
  if (value === 'sector') return 'is-sector'
  return 'is-stock'
}

const buildMetaLine = item => {
  if (item.level === 'stock') {
    return [item.name, item.symbol, item.sectorName].filter(Boolean).join(' / ')
  }

  return [item.symbol, item.sectorName || item.name].filter(Boolean).join(' / ')
}

const openExternal = url => {
  if (!url) return
  window.open(url, '_blank', 'noopener,noreferrer')
}

const fetchArchive = async ({ resetPage = false } = {}) => {
  if (resetPage) {
    page.value = 1
  }

  loading.value = true
  error.value = ''
  try {
    const params = new URLSearchParams({
      page: String(page.value),
      pageSize: String(pageSize.value)
    })

    if (keyword.value.trim()) params.set('keyword', keyword.value.trim())
    if (level.value) params.set('level', level.value)
    if (sentiment.value) params.set('sentiment', sentiment.value)

    const response = await fetch(`/api/news/archive?${params.toString()}`)
    if (!response.ok) {
      const payload = await response.json().catch(() => null)
      throw new Error(payload?.message || '资讯库加载失败')
    }

    const text = await response.text()
    const payload = (text && text.trim()) ? JSON.parse(text) : null
    total.value = Number(payload?.total ?? payload?.Total ?? 0)
    items.value = Array.isArray(payload?.items ?? payload?.Items) ? (payload.items ?? payload.Items).map(normalizeArchiveItem) : []
  } catch (err) {
    items.value = []
    total.value = 0
    error.value = err.message || '资讯库加载失败'
  } finally {
    loading.value = false
  }
}

const processing = ref(false)
async function processPending() {
  processing.value = true
  try {
    const res = await fetch('/api/news/archive/process-pending', { method: 'POST' })
    if (res.ok) {
      toast.success('批量清洗完成')
      fetchArchive()
    } else {
      toast.error('清洗失败: ' + await res.text())
    }
  } catch (e) {
    toast.error('清洗失败: ' + e.message)
  } finally {
    processing.value = false
  }
}

const submitSearch = () => fetchArchive({ resetPage: true })
const goToPage = p => {
  if (p < 1 || p > totalPages.value || p === page.value) return
  page.value = p
  fetchArchive()
}
const goPrev = () => goToPage(page.value - 1)
const goNext = () => goToPage(page.value + 1)

onMounted(() => {
  fetchArchive()
})
</script>

<template>
  <section class="archive-shell">
    <header class="archive-hero">
      <div>
        <p class="archive-kicker">News Archive Console</p>
        <h2>全量资讯库</h2>
        <p class="archive-subtitle">统一检索本地事实库中已清洗的个股、板块与大盘资讯，支持译题优先、情绪筛选与原文跳转。</p>
      </div>
      <div class="archive-stats">
        <strong>{{ resultSummary }}</strong>
        <span>{{ pageSummary }}</span>
      </div>
    </header>

    <section class="archive-toolbar">
      <label class="archive-field archive-search">
        <span>搜索</span>
        <input
          v-model="keyword"
          type="text"
          placeholder="标题 / 译题 / 代码 / 板块 / 靶点"
          @keydown.enter="submitSearch"
        />
      </label>

      <label class="archive-field">
        <span>层级</span>
        <select v-model="level" @change="submitSearch">
          <option v-for="option in levelOptions" :key="option.value || 'all-level'" :value="option.value">
            {{ option.label }}
          </option>
        </select>
      </label>

      <label class="archive-field">
        <span>情绪</span>
        <select v-model="sentiment" @change="submitSearch">
          <option v-for="option in sentimentOptions" :key="option.value || 'all-sentiment'" :value="option.value">
            {{ option.label }}
          </option>
        </select>
      </label>

      <div class="archive-actions">
        <button class="primary-button" @click="submitSearch" :disabled="loading">检索</button>
        <button class="primary-button" style="margin-left:8px" @click="processPending" :disabled="processing">{{ processing ? '清洗中...' : '🧹 批量清洗待处理' }}</button>
      </div>
    </section>

    <div v-if="error" class="archive-feedback error">{{ error }}</div>
    <div v-else-if="loading" class="archive-feedback">正在加载资讯库...</div>
    <div v-else-if="!items.length" class="archive-feedback">当前筛选条件下没有可展示的资讯。</div>

    <section v-else class="archive-list">
      <article v-for="item in items" :key="`${item.level}-${item.symbol}-${item.title}-${item.publishTime}`" class="archive-card">
        <div class="archive-card-top">
          <div class="archive-badges">
            <span class="archive-badge level-badge" :class="getLevelClass(item.level)">{{ getLevelLabel(item.level) }}</span>
            <span class="archive-badge sentiment-badge" :class="getSentimentClass(item.sentiment)">{{ item.sentiment }}</span>
            <span v-if="item.sourceTag" class="archive-badge source-tag-badge">📡 {{ formatSourceTag(item.sourceTag) }}</span>
            <span v-if="item.aiTarget" class="archive-badge target-badge">{{ item.aiTarget }}</span>
            <span v-for="tag in item.aiTags" :key="`${item.level}-${item.title}-${tag}`" class="archive-badge tag-badge">{{ tag }}</span>
          </div>
          <button v-if="item.url" class="link-button link-ghost" @click="openExternal(item.url)">
            <span class="link-ghost-icon">↗</span> 查看原文
          </button>
        </div>

        <h3>{{ getHeadline(item) }}</h3>
        <p v-if="item.translatedTitle && item.translatedTitle !== item.title" class="archive-raw-title">原题：{{ item.title }}</p>
        <p v-else class="archive-raw-title">{{ buildMetaLine(item) || item.source }}</p>

        <div class="archive-meta-row">
          <span>{{ buildMetaLine(item) || '未标注实体' }}</span>
          <span>{{ item.source }}</span>
          <span>{{ formatDate(item.publishTime) }}</span>
          <span v-if="item.isAiProcessed" class="ai-processed-badge" title="已 AI 清洗">🤖 已清洗</span>
          <span v-else class="ai-unprocessed-badge" title="待 AI 清洗">⏳ 待清洗</span>
        </div>
      </article>
    </section>

    <footer class="archive-pagination">
      <button @click="goToPage(1)" :disabled="loading || page <= 1">首页</button>
      <button @click="goPrev" :disabled="loading || page <= 1">上一页</button>
      <template v-for="p in visiblePages" :key="'page-' + p">
        <span v-if="p === '...'" class="pagination-ellipsis">…</span>
        <button v-else class="pagination-page" :class="{ 'pagination-current': p === page }" @click="goToPage(p)" :disabled="loading">{{ p }}</button>
      </template>
      <button @click="goNext" :disabled="loading || page >= totalPages">下一页</button>
      <button @click="goToPage(totalPages)" :disabled="loading || page >= totalPages">末页</button>
    </footer>
  </section>
</template>

<style scoped>
.archive-shell {
  display: grid;
  gap: var(--space-4);
  padding: var(--space-6);
  color: var(--color-text-body);
}

.archive-hero {
  display: flex;
  justify-content: space-between;
  gap: var(--space-5);
  padding: var(--space-6);
  border-radius: var(--radius-xl);
  background:
    radial-gradient(circle at top left, color-mix(in srgb, var(--color-accent) 14%, transparent), transparent 35%),
    radial-gradient(circle at bottom right, color-mix(in srgb, var(--color-warning) 10%, transparent), transparent 40%),
    var(--color-bg-surface-alt);
  border: 1px solid var(--color-border-light);
  box-shadow: var(--shadow-md);
}

.archive-kicker {
  margin: 0 0 8px;
  font-size: 12px;
  letter-spacing: 0.18em;
  text-transform: uppercase;
  color: var(--color-accent);
}

.archive-hero h2 {
  margin: 0;
  font-size: 34px;
  line-height: 1.05;
}

.archive-subtitle {
  margin: 12px 0 0;
  max-width: 720px;
  color: var(--color-text-secondary);
  line-height: 1.6;
}

.archive-stats {
  display: grid;
  align-content: start;
  gap: 6px;
  min-width: 140px;
  padding: var(--space-4) var(--space-4);
  border-radius: var(--radius-lg);
  background: var(--color-bg-surface-alt);
  border: 1px solid var(--color-border-light);
  color: var(--color-text-heading);
}

.archive-stats strong {
  font-size: 20px;
}

.archive-stats span {
  color: var(--color-text-secondary);
}

.archive-toolbar {
  display: grid;
  grid-template-columns: minmax(0, 2.1fr) repeat(2, minmax(140px, 0.8fr)) auto;
  gap: var(--space-3);
  align-items: end;
  padding: var(--space-4);
  border-radius: var(--radius-lg);
  background: var(--color-bg-surface);
  border: 1px solid var(--color-border-light);
  box-shadow: var(--shadow-sm);
}

.archive-field {
  display: grid;
  gap: 8px;
}

.archive-field span {
  font-size: 12px;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--color-text-secondary);
}

.archive-field input,
.archive-field select {
  height: 34px;
  padding: 0 var(--space-3);
  border-radius: var(--radius-md);
  border: 1px solid var(--color-border-light);
  background: var(--color-bg-surface-alt);
  color: var(--color-text-heading);
}

.archive-actions {
  display: flex;
  justify-content: flex-end;
}

.primary-button,
.archive-pagination button,
.link-button {
  height: 34px;
  border: 0;
  border-radius: var(--radius-full);
  padding: 0 var(--space-4);
  font-weight: 600;
  cursor: pointer;
}

.primary-button {
  background: var(--color-accent);
  color: #fff;
  box-shadow: none;
}

.primary-button:disabled,
.archive-pagination button:disabled,
.link-button:disabled {
  opacity: 0.45;
  cursor: not-allowed;
  box-shadow: none;
}

.archive-feedback {
  padding: var(--space-5) var(--space-5);
  border-radius: var(--radius-lg);
  background: var(--color-bg-surface);
  border: 1px solid var(--color-border-light);
  color: var(--color-text-secondary);
}

.archive-feedback.error {
  border-color: var(--color-danger-border);
  background: var(--color-danger-bg);
  color: var(--color-danger);
}

.archive-list {
  display: grid;
  gap: 14px;
}

.archive-card {
  display: grid;
  gap: 12px;
  padding: var(--space-5);
  border-radius: var(--radius-lg);
  background: var(--color-bg-surface);
  border: 1px solid var(--color-border-light);
  box-shadow: var(--shadow-sm);
  transition: border-color 0.15s ease;
}

.archive-card:hover {
  border-color: var(--color-accent-border);
}

.archive-card-top {
  display: flex;
  justify-content: space-between;
  gap: 16px;
  align-items: start;
}

.archive-badges {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.archive-badge {
  display: inline-flex;
  align-items: center;
  min-height: 28px;
  padding: 0 10px;
  border-radius: 999px;
  font-size: 12px;
  font-weight: 700;
}

.level-badge.is-market {
  background: var(--color-info-bg);
  color: var(--color-info);
}

.level-badge.is-sector {
  background: var(--color-tag-sector-bg);
  color: var(--color-tag-sector);
}

.level-badge.is-stock {
  background: var(--color-success-bg);
  color: var(--color-success);
}

.sentiment-badge.is-positive {
  background: var(--color-market-rise-bg);
  color: var(--color-market-rise);
}

.sentiment-badge.is-neutral {
  background: var(--color-bg-surface-alt);
  color: var(--color-text-secondary);
}

.sentiment-badge.is-negative {
  background: var(--color-market-fall-bg);
  color: var(--color-market-fall);
}

.target-badge {
  background: var(--color-danger-bg);
  color: var(--color-danger);
}

.source-tag-badge {
  background: rgba(14, 165, 233, .12);
  color: #38bdf8;
  border: 1px solid rgba(14, 165, 233, .2);
}

.tag-badge {
  background: var(--color-warning-bg);
  color: var(--color-warning);
}

.link-button {
  background: var(--color-accent-subtle);
  color: var(--color-accent);
  white-space: nowrap;
}

.link-ghost {
  background: transparent;
  border: 1px solid var(--color-border-light);
  color: var(--color-accent);
  font-weight: 500;
  font-size: 13px;
  display: inline-flex;
  align-items: center;
  gap: 4px;
  transition: background 0.15s, border-color 0.15s;
}

.link-ghost:hover {
  background: var(--color-accent-subtle);
  border-color: var(--color-accent);
}

.link-ghost-icon {
  font-size: 15px;
  line-height: 1;
}

.archive-card h3 {
  margin: 0;
  font-size: 22px;
  line-height: 1.35;
}

.archive-raw-title {
  margin: 0;
  color: var(--color-text-secondary);
  line-height: 1.5;
}

.archive-meta-row {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  color: var(--color-text-secondary);
  font-size: 13px;
}

.archive-pagination {
  display: flex;
  justify-content: flex-end;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}

.archive-pagination button {
  background: var(--color-bg-surface);
  color: var(--color-text-heading);
  border: 1px solid var(--color-border-light);
}

.pagination-page {
  min-width: 34px;
  text-align: center;
}

.pagination-current {
  background: var(--color-accent) !important;
  color: #fff !important;
  border-color: var(--color-accent) !important;
}

.pagination-ellipsis {
  padding: 0 4px;
  color: var(--color-text-secondary);
  user-select: none;
}

@media (max-width: 960px) {
  .archive-shell {
    padding: var(--space-4);
  }

  .archive-hero,
  .archive-toolbar,
  .archive-card-top,
  .archive-pagination {
    grid-template-columns: 1fr;
    flex-direction: column;
    align-items: stretch;
  }

  .archive-toolbar {
    display: grid;
  }

  .archive-actions,
  .archive-pagination {
    justify-content: stretch;
  }
}
.ai-processed-badge { display:inline-flex; align-items:center; gap:.2rem; padding:.1rem .4rem; border-radius:999px; font-size:.68rem; font-weight:600; background:rgba(34,197,94,.12); color:#16a34a; }
.ai-unprocessed-badge { display:inline-flex; align-items:center; gap:.2rem; padding:.1rem .4rem; border-radius:999px; font-size:.68rem; font-weight:600; background:rgba(234,179,8,.12); color:#ca8a04; }
</style>