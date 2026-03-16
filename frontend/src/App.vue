<script setup>
import { ref } from 'vue'
import StockInfoTab from './modules/stocks/StockInfoTab.vue'
import NewsArchiveTab from './modules/stocks/NewsArchiveTab.vue'
import StockRecommendTab from './modules/stocks/StockRecommendTab.vue'
import MarketSentimentTab from './modules/market/MarketSentimentTab.vue'
import SocialOptimizeTab from './modules/social/SocialOptimizeTab.vue'
import SocialCrawlerTab from './modules/social/SocialCrawlerTab.vue'
import AdminLlmSettings from './modules/admin/AdminLlmSettings.vue'
import SourceGovernanceDeveloperMode from './modules/admin/SourceGovernanceDeveloperMode.vue'

const tabs = [
  { key: 'stock-info', name: '股票信息', component: StockInfoTab },
  { key: 'market-sentiment', name: '情绪轮动', component: MarketSentimentTab },
  { key: 'news-archive', name: '全量资讯库', component: NewsArchiveTab },
  { key: 'stock-recommend', name: '股票推荐', component: StockRecommendTab },
  { key: 'social-optimize', name: '社媒优化', component: SocialOptimizeTab },
  { key: 'social-crawler', name: '社媒爬虫', component: SocialCrawlerTab },
  { key: 'admin-llm', name: 'LLM 设置', component: AdminLlmSettings },
  { key: 'source-governance-dev', name: '治理开发者模式', component: SourceGovernanceDeveloperMode }
]

const activeTab = ref(tabs[0].key)
</script>

<template>
  <div class="app">
    <header class="app-header">
      <div class="brand">SimplerJiang AI Agent</div>
      <nav class="tabs">
        <button
          v-for="tab in tabs"
          :key="tab.key"
          class="tab"
          :class="{ active: tab.key === activeTab }"
          @click="activeTab = tab.key"
        >
          {{ tab.name }}
        </button>
      </nav>
    </header>

    <main class="app-content">
      <component
        :is="tabs.find(tab => tab.key === activeTab)?.component"
      />
    </main>
  </div>
</template>
