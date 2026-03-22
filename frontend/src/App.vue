<script setup>
import { computed, onMounted, ref, watch } from 'vue'
import StockInfoTab from './modules/stocks/StockInfoTab.vue'
import NewsArchiveTab from './modules/stocks/NewsArchiveTab.vue'
import StockRecommendTab from './modules/stocks/StockRecommendTab.vue'
import MarketSentimentTab from './modules/market/MarketSentimentTab.vue'
import AdminLlmSettings from './modules/admin/AdminLlmSettings.vue'
import SourceGovernanceDeveloperMode from './modules/admin/SourceGovernanceDeveloperMode.vue'
import StockCopilotDeveloperMode from './modules/admin/StockCopilotDeveloperMode.vue'

const tabs = [
  { key: 'stock-info', name: '股票信息', component: StockInfoTab },
  { key: 'market-sentiment', name: '情绪轮动', component: MarketSentimentTab },
  { key: 'news-archive', name: '全量资讯库', component: NewsArchiveTab },
  { key: 'stock-recommend', name: '股票推荐', component: StockRecommendTab },
  { key: 'admin-llm', name: 'LLM 设置', component: AdminLlmSettings },
  { key: 'source-governance-dev', name: '治理开发者模式', component: SourceGovernanceDeveloperMode },
  { key: 'stock-copilot-dev', name: '股票 Copilot 开发模式', component: StockCopilotDeveloperMode }
]

const defaultTabKey = tabs[0].key
const validTabKeys = new Set(tabs.map(tab => tab.key))
const initialSearchParams = new URLSearchParams(window.location.search)
const initialRequestedTab = initialSearchParams.get('tab')
const initialHasExplicitTab = validTabKeys.has(initialRequestedTab)
const initialForcedOnboarding = initialSearchParams.get('onboarding') === '1'

const onboardingStatus = ref({
  loading: true,
  requiresOnboarding: false,
  activeProviderKey: 'default',
  recommendedTabKey: 'admin-llm'
})
const appVersion = ref('')

const getTabFromLocation = () => {
  const tab = new URLSearchParams(window.location.search).get('tab')
  return validTabKeys.has(tab) ? tab : defaultTabKey
}

const activeTab = ref(getTabFromLocation())

const activeComponent = computed(() => tabs.find(tab => tab.key === activeTab.value)?.component)

const setActiveTab = tabKey => {
  if (validTabKeys.has(tabKey)) {
    activeTab.value = tabKey
  }
}

const syncLocation = () => {
  const nextUrl = new URL(window.location.href)
  nextUrl.searchParams.set('tab', activeTab.value)
  if (onboardingStatus.value.requiresOnboarding) {
    nextUrl.searchParams.set('onboarding', '1')
  } else {
    nextUrl.searchParams.delete('onboarding')
  }

  const nextLocation = `${nextUrl.pathname}${nextUrl.search}${nextUrl.hash}`
  window.history.replaceState({}, '', nextLocation)
}

watch(activeTab, syncLocation, { immediate: true })

const openOnboardingTab = () => {
  setActiveTab(onboardingStatus.value.recommendedTabKey || 'admin-llm')
}

const loadOnboardingStatus = async ({ allowAutoRedirect = false } = {}) => {
  try {
    const response = await fetch('/api/llm/onboarding-status')
    if (!response.ok) {
      onboardingStatus.value.loading = false
      return
    }

    const data = await response.json()
    onboardingStatus.value = {
      loading: false,
      requiresOnboarding: Boolean(data.requiresOnboarding),
      activeProviderKey: data.activeProviderKey || 'default',
      recommendedTabKey: data.recommendedTabKey || 'admin-llm'
    }

    if (allowAutoRedirect && onboardingStatus.value.requiresOnboarding && (!initialHasExplicitTab || initialForcedOnboarding)) {
      openOnboardingTab()
      return
    }
  } catch {
    onboardingStatus.value.loading = false
    return
  }

  syncLocation()
}

onMounted(async () => {
  try {
    const versionResponse = await fetch('/api/app/version')
    if (versionResponse.ok) {
      const versionData = await versionResponse.json()
      appVersion.value = versionData.version || ''
    }
  } catch {
    appVersion.value = ''
  }

  await loadOnboardingStatus({ allowAutoRedirect: true })
})
</script>

<template>
  <div class="app">
    <header class="app-header">
      <div class="brand">
        <span>SimplerJiang AI Agent</span>
        <span v-if="appVersion" class="version-badge">v{{ appVersion }}</span>
      </div>
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
      <section v-if="onboardingStatus.requiresOnboarding" class="onboarding-banner">
        <div>
          <strong>首次启动还没有 LLM Key</strong>
          <p>
            先进入 LLM 设置页，用管理员账号保存一个可用通道的 API Key。
            当前安装包不会内置任何用户密钥。
          </p>
        </div>
        <button class="tab onboarding-action" @click="openOnboardingTab">去配置</button>
      </section>

      <component
        :is="activeComponent"
        @settings-saved="loadOnboardingStatus()"
      />
    </main>
  </div>
</template>

<style scoped>
.brand {
  display: flex;
  align-items: center;
  gap: 10px;
}

.version-badge {
  display: inline-flex;
  align-items: center;
  padding: 2px 8px;
  border-radius: 999px;
  background: #1d3b53;
  color: #f4f8fb;
  font-size: 12px;
  letter-spacing: 0.04em;
}

.app-content {
  padding: 16px;
}

.onboarding-banner {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 16px;
  padding: 16px 18px;
  border: 1px solid #d6b56d;
  border-radius: 12px;
  background: linear-gradient(135deg, #fff8e6, #fff2cc);
}

.onboarding-banner p {
  margin: 6px 0 0;
}

.onboarding-action {
  flex: 0 0 auto;
}
</style>
