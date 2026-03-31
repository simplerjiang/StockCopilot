<script setup>
defineProps({
  workspace: { type: Object, required: true },
  sidebarNewsSections: { type: Array, default: () => [] },
  getImpactClass: { type: Function, required: true },
  getLocalNewsHeadline: { type: Function, required: true },
  formatDate: { type: Function, required: true },
  getHeadlineNewsImpactEvents: { type: Function, required: true },
  getImpactCategoryValue: { type: Function, required: true },
  formatImpactScore: { type: Function, required: true }
})

defineEmits(['refresh'])
</script>

<template>
  <section class="copilot-card news-impact stock-news-impact-section" :class="{ 'copilot-section-active': workspace.copilotFocusSection === 'news' }">
    <div class="news-impact-header">
      <div>
        <h3>资讯影响</h3>
        <p class="muted">事件信号在右侧集中查看，不遮挡 K 线。</p>
      </div>
      <button @click="$emit('refresh')" :disabled="workspace.newsImpactLoading || !workspace.detail">刷新</button>
    </div>

    <div v-if="workspace.detail" class="news-impact-content">
      <p v-if="workspace.newsImpactError" class="muted error">{{ workspace.newsImpactError }}</p>
      <p v-else-if="workspace.newsImpactLoading" class="muted">分析中...</p>
      <div v-else-if="workspace.newsImpact" class="news-impact-summary">
        <span>利好 {{ workspace.newsImpact.summary.positive }}</span>
        <span>中性 {{ workspace.newsImpact.summary.neutral }}</span>
        <span>利空 {{ workspace.newsImpact.summary.negative }}</span>
        <span class="overall">总体：{{ workspace.newsImpact.summary.overall }}</span>
      </div>
      <p v-else class="muted">资讯影响待生成。</p>

      <div class="news-buckets">
        <article v-for="section in sidebarNewsSections" :key="section.key" class="news-bucket-card">
          <div class="news-bucket-header">
            <strong>{{ section.title }}</strong>
            <span v-if="section.key === 'sector' && workspace.localNewsBuckets[section.key]?.sectorName" class="muted">{{ workspace.localNewsBuckets[section.key]?.sectorName }}</span>
          </div>
          <p v-if="workspace.localNewsLoading" class="muted">加载中...</p>
          <ul v-else-if="workspace.localNewsBuckets[section.key]?.items?.length" class="news-bucket-list">
            <li v-for="item in workspace.localNewsBuckets[section.key].items" :key="`${section.key}-${item.title}-${item.publishTime}`">
              <span class="impact-tag" :class="getImpactClass(item.sentiment)">{{ item.sentiment }}</span>
              <a v-if="item.url ?? item.Url" :href="item.url ?? item.Url" target="_blank" rel="noreferrer">{{ getLocalNewsHeadline(item) }}</a>
              <span v-else>{{ getLocalNewsHeadline(item) }}</span>
              <small v-if="item.translatedTitle && item.translatedTitle !== item.title">原题：{{ item.title }}</small>
              <div v-if="item.aiTags?.length || item.aiTarget" class="local-news-meta-row">
                <span v-if="item.aiTarget" class="local-news-target">{{ item.aiTarget }}</span>
                <span v-for="tag in item.aiTags" :key="`${section.key}-${item.title}-${tag}`" class="local-news-tag">{{ tag }}</span>
              </div>
              <small>{{ item.source }} · {{ formatDate(item.publishTime) }}</small>
            </li>
          </ul>
          <p v-else class="muted">暂无匹配资讯。</p>
        </article>
      </div>

      <p v-if="workspace.localNewsError" class="muted error">{{ workspace.localNewsError }}</p>

      <ul v-if="getHeadlineNewsImpactEvents(workspace.newsImpact).length" class="news-impact-list">
        <li v-for="item in getHeadlineNewsImpactEvents(workspace.newsImpact)" :key="`${item.title ?? item.Title}-${item.publishedAt ?? item.PublishedAt ?? ''}`">
          <span class="impact-tag" :class="getImpactClass(getImpactCategoryValue(item))">{{ getImpactCategoryValue(item) }}</span>
          <span class="impact-title">{{ item.title ?? item.Title }}</span>
          <span class="impact-score">{{ formatImpactScore(item.impactScore ?? item.ImpactScore) }}</span>
        </li>
      </ul>
      <p v-else-if="!workspace.newsImpactLoading" class="muted">暂无资讯影响数据。</p>
    </div>

    <p v-else class="muted">选择股票后在此查看事件影响。</p>
  </section>
</template>

<style scoped>
.news-impact-header,.news-bucket-header,.local-news-meta-row { display:flex; gap:.75rem; flex-wrap:wrap; justify-content:space-between; }
.news-impact-content,.news-impact-summary,.news-buckets,.news-bucket-card,.news-impact-list,.news-bucket-list { display:grid; gap:.75rem; }
.news-impact-summary { grid-template-columns:repeat(auto-fit,minmax(120px,1fr)); }
.news-bucket-card { padding:.85rem; border-radius:16px; background:rgba(248,250,252,.92); border:1px solid rgba(148,163,184,.16); }
.news-impact-list,.news-bucket-list { list-style:none; padding:0; margin:0; }
.news-bucket-list { max-height:18rem; overflow-y:auto; padding-right:.25rem; }
.news-bucket-list li { display:grid; gap:.15rem; }
.news-bucket-list a,.news-bucket-list span { color:var(--color-text-primary); font-weight:600; text-decoration:none; }
.news-bucket-list small { color:var(--color-text-secondary); }
.news-impact-list li { display:grid; grid-template-columns:auto 1fr auto; gap:.75rem; align-items:start; }
.news-impact-header button { border:none; border-radius:999px; padding:.35rem .75rem; cursor:pointer; background:rgba(37,99,235,.12); color:#1d4ed8; }
.news-impact-header button:disabled { opacity:.6; cursor:not-allowed; }
.local-news-tag,.local-news-target { display:inline-flex; align-items:center; border-radius:999px; padding:.1rem .45rem; font-size:.72rem; background:rgba(148,163,184,.16); color:var(--color-text-secondary); }
.impact-positive { color:#dc2626; background:rgba(220,38,38,.1); }
.impact-negative { color:#16a34a; background:rgba(22,163,106,.1); }
.impact-neutral { color:#64748b; background:rgba(100,116,139,.1); }
.impact-tag { display:inline-flex; align-items:center; padding:.18rem .55rem; border-radius:999px; font-size:.75rem; font-weight:700; letter-spacing:.02em; white-space:nowrap; }
.news-impact-summary span { display:inline-flex; align-items:center; padding:.35rem .7rem; border-radius:var(--radius-md,10px); font-weight:600; font-size:.85rem; background:rgba(248,250,252,.96); border:1px solid rgba(148,163,184,.16); }
.news-impact-summary .overall { background:rgba(37,99,235,.08); border-color:rgba(37,99,235,.2); color:#1d4ed8; }
.news-bucket-list::-webkit-scrollbar { width:8px; }
.news-bucket-list::-webkit-scrollbar-thumb { border-radius:999px; background:rgba(148,163,184,.45); }
@media (max-width:720px) { .news-impact-header { flex-direction:column; } .news-impact-list li { grid-template-columns:1fr; } }
</style>