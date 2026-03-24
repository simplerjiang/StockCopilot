<script setup>
defineProps({
  detail: { type: Object, default: null },
  loading: { type: Boolean, default: false },
  error: { type: String, default: '' },
  items: { type: Array, default: () => [] },
  previewItems: { type: Array, default: () => [] },
  modalOpen: { type: Boolean, default: false },
  getImpactClass: { type: Function, required: true },
  getLocalNewsHeadline: { type: Function, required: true },
  formatDate: { type: Function, required: true }
})

defineEmits(['refresh', 'open-modal', 'close-modal'])
</script>

<template>
  <section class="market-news-panel" :class="{ empty: !detail && !loading && !error && !items.length }">
    <div class="market-news-header">
      <div>
        <p class="market-news-kicker">Market Wire</p>
        <h3>大盘资讯</h3>
      </div>
      <div class="market-news-actions">
        <button class="market-news-button" @click="$emit('refresh')" :disabled="loading">刷新</button>
        <button class="market-news-button" @click="$emit('open-modal')" :disabled="!items.length">展开阅读</button>
      </div>
    </div>

    <p v-if="error" class="muted error-text">{{ error }}</p>
    <p v-else-if="loading" class="muted">大盘资讯加载中...</p>
    <div v-else-if="items.length" class="market-news-preview-list">
      <article v-for="item in previewItems" :key="`market-${item.title}-${item.publishTime}`" class="market-news-item">
        <span class="impact-tag" :class="getImpactClass(item.sentiment)">{{ item.sentiment }}</span>
        <a v-if="item.url" :href="item.url" target="_blank" rel="noreferrer">{{ getLocalNewsHeadline(item) }}</a>
        <span v-else>{{ getLocalNewsHeadline(item) }}</span>
        <small v-if="item.translatedTitle && item.translatedTitle !== item.title">原题：{{ item.title }}</small>
        <div v-if="item.aiTags?.length || item.aiTarget" class="local-news-meta-row">
          <span v-if="item.aiTarget" class="local-news-target">{{ item.aiTarget }}</span>
          <span v-for="tag in item.aiTags" :key="`market-tag-${item.title}-${tag}`" class="local-news-tag">{{ tag }}</span>
        </div>
        <small>{{ item.source }} · {{ formatDate(item.publishTime) }}</small>
      </article>
    </div>
    <p v-else class="muted">暂无可展示的大盘资讯。</p>
  </section>

  <div v-if="modalOpen" class="market-news-modal-backdrop" @click.self="$emit('close-modal')">
    <section class="market-news-modal" role="dialog" aria-modal="true" aria-label="大盘资讯详情">
      <div class="market-news-header">
        <div>
          <p class="market-news-kicker">Expanded Reader</p>
          <h3>大盘资讯详情</h3>
        </div>
        <button class="market-news-button" @click="$emit('close-modal')">关闭</button>
      </div>
      <div class="market-news-modal-list">
        <article v-for="item in items" :key="`market-modal-${item.title}-${item.publishTime}`" class="market-news-item">
          <span class="impact-tag" :class="getImpactClass(item.sentiment)">{{ item.sentiment }}</span>
          <a v-if="item.url" :href="item.url" target="_blank" rel="noreferrer">{{ getLocalNewsHeadline(item) }}</a>
          <span v-else>{{ getLocalNewsHeadline(item) }}</span>
          <small v-if="item.translatedTitle && item.translatedTitle !== item.title">原题：{{ item.title }}</small>
          <div v-if="item.aiTags?.length || item.aiTarget" class="local-news-meta-row">
            <span v-if="item.aiTarget" class="local-news-target">{{ item.aiTarget }}</span>
            <span v-for="tag in item.aiTags" :key="`market-modal-tag-${item.title}-${tag}`" class="local-news-tag">{{ tag }}</span>
          </div>
          <small>{{ item.source }} · {{ formatDate(item.publishTime) }}</small>
        </article>
      </div>
    </section>
  </div>
</template>

<style scoped>
.market-news-panel { display:grid; gap:.85rem; padding:.95rem 1.1rem; border-radius:20px; border:1px solid rgba(14,165,233,.18); background:radial-gradient(circle at top left, rgba(14,165,233,.14), transparent 28%), linear-gradient(135deg, rgba(15,23,42,.97), rgba(15,23,42,.9)); box-shadow:0 18px 42px rgba(15,23,42,.14); }
.market-news-header,.market-news-actions,.local-news-meta-row { display:flex; gap:.55rem; flex-wrap:wrap; }
.market-news-header { justify-content:space-between; align-items:flex-start; }
.market-news-kicker { margin:0 0 .2rem; font-size:.7rem; letter-spacing:.16em; text-transform:uppercase; color:#7dd3fc; }
.market-news-item,.market-news-preview-list,.market-news-modal-list { display:grid; gap:.18rem; }
.market-news-item { padding:.8rem .9rem; border-radius:16px; background:rgba(255,255,255,.06); border:1px solid rgba(148,163,184,.12); }
.market-news-item a,.market-news-item span,h3 { color:#f8fafc; }
.market-news-item small { color:#94a3b8; }
.market-news-button { border:1px solid rgba(148,163,184,.3); border-radius:999px; padding:.35rem .75rem; background:rgba(255,255,255,.1); color:#e2e8f0; cursor:pointer; }
.market-news-modal-backdrop { position:fixed; inset:0; z-index:60; display:grid; place-items:center; padding:1rem; background:rgba(15,23,42,.62); backdrop-filter:blur(10px); }
.market-news-modal { display:grid; grid-template-rows:auto minmax(0,1fr); gap:1rem; width:min(960px,100%); height:min(78vh,820px); max-height:min(78vh,820px); padding:1.2rem; border-radius:24px; border:1px solid rgba(148,163,184,.18); background:linear-gradient(160deg, rgba(15,23,42,.98), rgba(15,23,42,.94)); overflow:hidden; }
.market-news-modal-list { min-height:0; max-height:100%; overflow-y:auto; padding-right:.35rem; }
.market-news-modal-list::-webkit-scrollbar { width:8px; }
.market-news-modal-list::-webkit-scrollbar-thumb { border-radius:999px; background:rgba(148,163,184,.45); }
.market-news-modal-list::-webkit-scrollbar-track { background:rgba(15,23,42,.12); }
.local-news-tag,.local-news-target { display:inline-flex; align-items:center; border-radius:999px; padding:.1rem .45rem; font-size:.72rem; background:rgba(148,163,184,.16); color:#cbd5e1; }
.impact-positive { color:#fca5a5; }
.impact-negative { color:#86efac; }
.impact-neutral { color:#f8fafc; }
.error-text { color:#fecaca; }
</style>