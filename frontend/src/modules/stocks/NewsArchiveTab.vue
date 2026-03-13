<script setup>
import { computed, onMounted, ref } from 'vue'

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
  aiTags: Array.isArray(item?.aiTags ?? item?.AiTags) ? (item.aiTags ?? item.AiTags) : []
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

    const payload = await response.json()
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

const submitSearch = () => fetchArchive({ resetPage: true })
const goPrev = () => {
  if (page.value <= 1) return
  page.value -= 1
  fetchArchive()
}
const goNext = () => {
  if (page.value >= totalPages.value) return
  page.value += 1
  fetchArchive()
}

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
            <span v-if="item.aiTarget" class="archive-badge target-badge">{{ item.aiTarget }}</span>
            <span v-for="tag in item.aiTags" :key="`${item.level}-${item.title}-${tag}`" class="archive-badge tag-badge">{{ tag }}</span>
          </div>
          <button v-if="item.url" class="link-button" @click="openExternal(item.url)">查看原文</button>
        </div>

        <h3>{{ getHeadline(item) }}</h3>
        <p v-if="item.translatedTitle && item.translatedTitle !== item.title" class="archive-raw-title">原题：{{ item.title }}</p>
        <p v-else class="archive-raw-title">{{ buildMetaLine(item) || item.source }}</p>

        <div class="archive-meta-row">
          <span>{{ buildMetaLine(item) || '未标注实体' }}</span>
          <span>{{ item.source }}</span>
          <span>{{ formatDate(item.publishTime) }}</span>
        </div>
      </article>
    </section>

    <footer class="archive-pagination">
      <button @click="goPrev" :disabled="loading || page <= 1">上一页</button>
      <span>{{ pageSummary }}</span>
      <button @click="goNext" :disabled="loading || page >= totalPages">下一页</button>
    </footer>
  </section>
</template>

<style scoped>
.archive-shell {
  display: grid;
  gap: 18px;
  padding: 24px;
  color: #1f2937;
}

.archive-hero {
  display: flex;
  justify-content: space-between;
  gap: 20px;
  padding: 24px;
  border-radius: 24px;
  background:
    radial-gradient(circle at top left, rgba(245, 158, 11, 0.18), transparent 35%),
    linear-gradient(135deg, #fff8ef 0%, #fff 60%, #f7fbff 100%);
  border: 1px solid rgba(148, 163, 184, 0.22);
  box-shadow: 0 18px 45px rgba(15, 23, 42, 0.08);
}

.archive-kicker {
  margin: 0 0 8px;
  font-size: 12px;
  letter-spacing: 0.18em;
  text-transform: uppercase;
  color: #b45309;
}

.archive-hero h2 {
  margin: 0;
  font-size: 34px;
  line-height: 1.05;
}

.archive-subtitle {
  margin: 12px 0 0;
  max-width: 720px;
  color: #475569;
  line-height: 1.6;
}

.archive-stats {
  display: grid;
  align-content: start;
  gap: 6px;
  min-width: 140px;
  padding: 16px 18px;
  border-radius: 18px;
  background: rgba(255, 255, 255, 0.72);
  color: #0f172a;
}

.archive-stats strong {
  font-size: 20px;
}

.archive-stats span {
  color: #64748b;
}

.archive-toolbar {
  display: grid;
  grid-template-columns: minmax(0, 2.1fr) repeat(2, minmax(140px, 0.8fr)) auto;
  gap: 14px;
  align-items: end;
  padding: 18px;
  border-radius: 20px;
  background: #ffffff;
  border: 1px solid rgba(226, 232, 240, 0.9);
  box-shadow: 0 12px 32px rgba(15, 23, 42, 0.06);
}

.archive-field {
  display: grid;
  gap: 8px;
}

.archive-field span {
  font-size: 12px;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: #64748b;
}

.archive-field input,
.archive-field select {
  height: 44px;
  padding: 0 14px;
  border-radius: 14px;
  border: 1px solid #dbe2ea;
  background: #f8fafc;
  color: #0f172a;
}

.archive-actions {
  display: flex;
  justify-content: flex-end;
}

.primary-button,
.archive-pagination button,
.link-button {
  height: 44px;
  border: 0;
  border-radius: 14px;
  padding: 0 16px;
  font-weight: 600;
  cursor: pointer;
}

.primary-button {
  background: linear-gradient(135deg, #d97706 0%, #ea580c 100%);
  color: #fff;
  box-shadow: 0 10px 22px rgba(217, 119, 6, 0.28);
}

.primary-button:disabled,
.archive-pagination button:disabled,
.link-button:disabled {
  opacity: 0.45;
  cursor: not-allowed;
  box-shadow: none;
}

.archive-feedback {
  padding: 20px 22px;
  border-radius: 18px;
  background: #ffffff;
  border: 1px solid rgba(226, 232, 240, 0.9);
  color: #475569;
}

.archive-feedback.error {
  border-color: rgba(248, 113, 113, 0.28);
  background: #fff7f7;
  color: #b91c1c;
}

.archive-list {
  display: grid;
  gap: 14px;
}

.archive-card {
  display: grid;
  gap: 12px;
  padding: 20px;
  border-radius: 20px;
  background: #ffffff;
  border: 1px solid rgba(226, 232, 240, 0.94);
  box-shadow: 0 16px 32px rgba(15, 23, 42, 0.05);
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
  background: #dbeafe;
  color: #1d4ed8;
}

.level-badge.is-sector {
  background: #ede9fe;
  color: #6d28d9;
}

.level-badge.is-stock {
  background: #dcfce7;
  color: #15803d;
}

.sentiment-badge.is-positive {
  background: #fee2e2;
  color: #b91c1c;
}

.sentiment-badge.is-neutral {
  background: #e2e8f0;
  color: #475569;
}

.sentiment-badge.is-negative {
  background: #dbeafe;
  color: #1d4ed8;
}

.target-badge {
  background: #fff1f2;
  color: #be123c;
}

.tag-badge {
  background: #fef3c7;
  color: #b45309;
}

.link-button {
  background: #eff6ff;
  color: #1d4ed8;
  white-space: nowrap;
}

.archive-card h3 {
  margin: 0;
  font-size: 22px;
  line-height: 1.35;
}

.archive-raw-title {
  margin: 0;
  color: #64748b;
  line-height: 1.5;
}

.archive-meta-row {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  color: #475569;
  font-size: 13px;
}

.archive-pagination {
  display: flex;
  justify-content: flex-end;
  align-items: center;
  gap: 10px;
}

.archive-pagination button {
  background: #ffffff;
  color: #0f172a;
  border: 1px solid #dbe2ea;
}

@media (max-width: 960px) {
  .archive-shell {
    padding: 16px;
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
</style>