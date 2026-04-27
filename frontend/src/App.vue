<script setup>
import { computed, nextTick, onBeforeUnmount, onErrorCaptured, onMounted, ref, watch } from 'vue'
import StockInfoTab from './modules/stocks/StockInfoTab.vue'
import NewsArchiveTab from './modules/stocks/NewsArchiveTab.vue'
import StockRecommendTab from './modules/stocks/StockRecommendTab.vue'
import TradeLogTab from './modules/stocks/TradeLogTab.vue'
import MarketSentimentTab from './modules/market/MarketSentimentTab.vue'
import AdminLlmSettings from './modules/admin/AdminLlmSettings.vue'
import SourceGovernanceDeveloperMode from './modules/admin/SourceGovernanceDeveloperMode.vue'
import FinancialDataTestPanel from './modules/admin/FinancialDataTestPanel.vue'
import FinancialWorkerPanel from './modules/admin/FinancialWorkerPanel.vue'
import FinancialConfigPage from './modules/financial/FinancialConfigPage.vue'
import FinancialCenterPage from './modules/financial/FinancialCenterPage.vue'
import AppToast from './components/AppToast.vue'
import ConfirmDialog from './components/ConfirmDialog.vue'

const mainTabs = [
  { key: 'stock-info', name: '股票信息', shortName: '股票', component: StockInfoTab },
  { key: 'market-sentiment', name: '情绪轮动', shortName: '情绪', component: MarketSentimentTab },
  { key: 'news-archive', name: '全量资讯库', shortName: '资讯', component: NewsArchiveTab },
  { key: 'financial-center', name: '财报中心', shortName: '财报', component: FinancialCenterPage },
  { key: 'trade-log', name: '交易日志', shortName: '交易', component: TradeLogTab },
  { key: 'stock-recommend', name: '股票推荐', shortName: '推荐', component: StockRecommendTab }
]

const adminTabs = [
  { key: 'admin-llm', name: 'LLM 设置', shortName: 'LLM', component: AdminLlmSettings },
  { key: 'source-governance-dev', name: '治理开发者模式', shortName: '治理', component: SourceGovernanceDeveloperMode },
  { key: 'financial-data-test', name: '财务数据测试', shortName: '财务', component: FinancialDataTestPanel },
  { key: 'financial-worker', name: '财务工作者监控', shortName: '工作者', component: FinancialWorkerPanel },
  { key: 'financial-config', name: '财报采集设置', shortName: '采集设置', component: FinancialConfigPage }
]

const tabs = [...mainTabs, ...adminTabs]

/* ── 设置下拉 ── */
const settingsOpen = ref(false)
const settingsRef = ref(null)
const toggleSettings = () => { settingsOpen.value = !settingsOpen.value }
const closeSettings = (e) => {
  if (settingsRef.value && !settingsRef.value.contains(e.target)) {
    settingsOpen.value = false
  }
}
const selectAdminTab = (tabKey) => {
  setActiveTab(tabKey)
  settingsOpen.value = false
}

/* ── 时钟 ── */
const clockText = ref('')
const tradingSessionLabel = ref('')
const tradingSessionClass = ref('')
let clockTimer = null
const HEALTH_CHECK_INTERVAL_MS = 45000
const HEALTH_CHECK_INTERVAL_OFFHOURS_MS = 120000

const getChinaAStockTradingSession = () => {
  const now = new Date()
  // Convert to China time (UTC+8)
  const chinaOffset = 8 * 60
  const localOffset = now.getTimezoneOffset()
  const chinaTime = new Date(now.getTime() + (chinaOffset + localOffset) * 60000)
  const day = chinaTime.getDay()
  const hour = chinaTime.getHours()
  const minute = chinaTime.getMinutes()
  const timeMinutes = hour * 60 + minute

  if (day === 0 || day === 6) {
    return { label: '非交易日', cls: 'session-closed' }
  }

  // 9:15-9:30 集合竞价
  if (timeMinutes >= 555 && timeMinutes < 570) {
    return { label: '集合竞价', cls: 'session-auction' }
  }
  // 9:30-11:30 上午交易
  if (timeMinutes >= 570 && timeMinutes < 690) {
    return { label: '交易中', cls: 'session-open' }
  }
  // 11:30-13:00 午间休市
  if (timeMinutes >= 690 && timeMinutes < 780) {
    return { label: '午间休市', cls: 'session-break' }
  }
  // 13:00-15:00 下午交易
  if (timeMinutes >= 780 && timeMinutes < 900) {
    return { label: '交易中', cls: 'session-open' }
  }
  // 其他时间已收盘
  return { label: '已收盘', cls: 'session-closed' }
}

const isMarketOpen = computed(() => tradingSessionClass.value === 'session-open' || tradingSessionClass.value === 'session-auction')

const updateClock = () => {
  const now = new Date()
  const pad2 = n => String(n).padStart(2, '0')
  const dateText = `${pad2(now.getMonth() + 1)}-${pad2(now.getDate())}`
  const timeText = [now.getHours(), now.getMinutes(), now.getSeconds()]
    .map(pad2).join(':')
  clockText.value = `${dateText} ${timeText}`
  const session = getChinaAStockTradingSession()
  tradingSessionLabel.value = session.label
  tradingSessionClass.value = session.cls
}

/* ── 连接状态 ── */
const backendOnline = ref(null) // null=unknown, true=online, false=offline
const lastHealthCheckedAt = ref('')
const lastHealthError = ref('')
let healthTimer = null
const formatHealthCheckedAt = value => {
  if (!value) return '尚未检查'
  return new Date(value).toLocaleString('zh-CN', { hour12: false })
}
const checkHealth = async () => {
  lastHealthCheckedAt.value = new Date().toISOString()
  try {
    const r = await fetch('/api/app/version', { signal: AbortSignal.timeout(5000) })
    if (!r.ok) throw new Error(`版本接口返回 ${r.status}`)
    const data = await r.json()
    if (typeof data?.version !== 'string' || !data.version.trim()) throw new Error('版本接口未返回有效版本号')
    appVersion.value = data.version
    backendOnline.value = true
    lastHealthError.value = ''
  } catch (err) {
    appVersion.value = ''
    backendOnline.value = false
    lastHealthError.value = err?.name === 'TimeoutError' ? '版本检查超时' : (err?.message || '版本检查失败')
  }
}
const connectionLabel = computed(() => backendOnline.value === null ? '检测中' : backendOnline.value ? '已连接' : '离线')
const connectionTitle = computed(() => {
  const parts = [`状态：${connectionLabel.value}`, `最近检查：${formatHealthCheckedAt(lastHealthCheckedAt.value)}`]
  if (lastHealthError.value) parts.push(`错误：${lastHealthError.value}`)
  return parts.join('\n')
})

/* ── Tab 指示线 ── */
const tabNavRef = ref(null)
const indicatorStyle = ref({ left: '0px', width: '0px' })
const updateIndicator = () => {
  if (!tabNavRef.value) return
  const activeEl = tabNavRef.value.querySelector('.nav-tab.active')
  if (activeEl) {
    const navRect = tabNavRef.value.getBoundingClientRect()
    const tabRect = activeEl.getBoundingClientRect()
    indicatorStyle.value = {
      left: `${tabRect.left - navRect.left}px`,
      width: `${tabRect.width}px`
    }
  }
}

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
const versionBadgeTitle = computed(() => {
  const parts = [`版本：${appVersion.value || '未知'}`, `连接：${connectionLabel.value}`, `最近版本检查：${formatHealthCheckedAt(lastHealthCheckedAt.value)}`]
  if (lastHealthError.value) parts.push(`错误：${lastHealthError.value}`)
  return parts.join('\n')
})

const getTabFromLocation = () => {
  const tab = new URLSearchParams(window.location.search).get('tab')
  return validTabKeys.has(tab) ? tab : defaultTabKey
}

const activeTab = ref(getTabFromLocation())

const activeComponent = computed(() => tabs.find(tab => tab.key === activeTab.value)?.component)

// Only keep-alive lightweight tabs to avoid memory bloat (#114)
// Excluded: StockRecommendTab, TradeLogTab, NewsArchiveTab, FinancialCenterPage (heavy data/SSE)
const keepAliveWhitelist = ['StockInfoTab', 'MarketSentimentTab', 'AdminLlmSettings', 'FinancialConfigPage']

const appViewError = ref(null)
const appViewErrorMessage = computed(() => appViewError.value?.message || '当前页面组件出现错误。')

const resetAppViewError = () => {
  appViewError.value = null
}

onErrorCaptured((err, instance, info) => {
  console.error('[App error boundary]', err, info)
  appViewError.value = {
    message: err?.message || '当前页面组件出现错误。',
    info: info || ''
  }
  return false
})

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

// Adjust health check frequency based on market hours
watch(isMarketOpen, (open) => {
  if (healthTimer) clearInterval(healthTimer)
  healthTimer = setInterval(checkHealth, open ? HEALTH_CHECK_INTERVAL_MS : HEALTH_CHECK_INTERVAL_OFFHOURS_MS)
})

watch(activeTab, () => {
  resetAppViewError()
  nextTick(updateIndicator)
})

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

const handleNavigateStock = (e) => {
  const detail = e?.detail
  if (detail?.symbol) {
    window.__pendingNavigateStock = { symbol: detail.symbol, name: detail.name, tab: detail.tab }
  }
  setActiveTab('stock-info')
  nextTick(() => {
    if (detail?.symbol) {
      window.dispatchEvent(new CustomEvent('navigate-stock-load', { detail }))
    }
    delete window.__pendingNavigateStock
  })
}

const handleNavigateTradeLog = (e) => {
  const detail = e?.detail
  if (detail?.plan) {
    window.__pendingNavigateTradeLog = detail
  }
  setActiveTab('trade-log')
}

const handleNavigateTab = (e) => {
  const tabKey = e?.detail?.tab
  if (tabKey && validTabKeys.has(tabKey)) {
    setActiveTab(tabKey)
  }
}

onMounted(async () => {
  updateClock()
  clockTimer = setInterval(updateClock, 1000)
  await checkHealth()
  healthTimer = setInterval(checkHealth, isMarketOpen.value ? HEALTH_CHECK_INTERVAL_MS : HEALTH_CHECK_INTERVAL_OFFHOURS_MS)
  document.addEventListener('click', closeSettings)
  window.addEventListener('navigate-stock', handleNavigateStock)
  window.addEventListener('navigate-trade-log', handleNavigateTradeLog)
  window.addEventListener('navigate-tab', handleNavigateTab)

  await loadOnboardingStatus({ allowAutoRedirect: true })
  nextTick(updateIndicator)
})

onBeforeUnmount(() => {
  if (clockTimer) clearInterval(clockTimer)
  if (healthTimer) clearInterval(healthTimer)
  document.removeEventListener('click', closeSettings)
  window.removeEventListener('navigate-stock', handleNavigateStock)
  window.removeEventListener('navigate-trade-log', handleNavigateTradeLog)
  window.removeEventListener('navigate-tab', handleNavigateTab)
})
</script>

<template>
  <div class="app">
    <header class="app-header">
      <div class="brand">
        <svg class="brand-icon" width="16" height="16" viewBox="0 0 16 16" fill="none" xmlns="http://www.w3.org/2000/svg">
          <path d="M8 1L14.5 8L8 15L1.5 8L8 1Z" fill="currentColor"/>
        </svg>
        <span class="brand-text">SimplerJiang AI Agent</span>
        <span v-if="appVersion" class="version-badge" :title="versionBadgeTitle">v{{ appVersion }}</span>
      </div>
      <nav ref="tabNavRef" class="nav-tabs">
        <button
          v-for="tab in mainTabs"
          :key="tab.key"
          class="nav-tab"
          :class="{ active: tab.key === activeTab }"
          @click="activeTab = tab.key"
        >
          <span class="nav-tab-full">{{ tab.name }}</span>
          <span class="nav-tab-short">{{ tab.shortName }}</span>
        </button>
        <div class="nav-indicator" :style="indicatorStyle" />
      </nav>
      <div class="header-status">
        <span class="connection-indicator" :class="{ online: backendOnline === true, offline: backendOnline === false }" :title="connectionTitle">
          <span class="connection-dot" />
          <span class="connection-label">{{ connectionLabel }}</span>
        </span>
        <span class="trading-session-badge" :class="tradingSessionClass">{{ tradingSessionLabel }}</span>
        <span class="header-clock">{{ clockText }}</span>
        <div ref="settingsRef" class="settings-dropdown-wrap">
          <button class="settings-trigger" :class="{ active: settingsOpen || adminTabs.some(t => t.key === activeTab) }" @click.stop="toggleSettings" title="管理设置">
            <svg width="16" height="16" viewBox="0 0 16 16" fill="none"><path d="M6.5 1.75a.75.75 0 0 1 .75-.75h1.5a.75.75 0 0 1 .75.75v.3a5.28 5.28 0 0 1 1.46.6l.21-.21a.75.75 0 0 1 1.06 0l1.06 1.06a.75.75 0 0 1 0 1.06l-.21.21c.26.45.46.94.6 1.46h.3a.75.75 0 0 1 .75.75v1.5a.75.75 0 0 1-.75.75h-.3c-.14.52-.34 1.01-.6 1.46l.21.21a.75.75 0 0 1 0 1.06l-1.06 1.06a.75.75 0 0 1-1.06 0l-.21-.21c-.45.26-.94.46-1.46.6v.3a.75.75 0 0 1-.75.75h-1.5a.75.75 0 0 1-.75-.75v-.3a5.28 5.28 0 0 1-1.46-.6l-.21.21a.75.75 0 0 1-1.06 0L2.71 12.4a.75.75 0 0 1 0-1.06l.21-.21a5.28 5.28 0 0 1-.6-1.46h-.3a.75.75 0 0 1-.75-.75v-1.5a.75.75 0 0 1 .75-.75h.3c.14-.52.34-1.01.6-1.46l-.21-.21a.75.75 0 0 1 0-1.06L3.77 2.88a.75.75 0 0 1 1.06 0l.21.21c.45-.26.94-.46 1.46-.6v-.3ZM8 10.5a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5Z" fill="currentColor"/></svg>
          </button>
          <div v-if="settingsOpen" class="settings-dropdown">
            <button
              v-for="tab in adminTabs"
              :key="tab.key"
              class="settings-item"
              :class="{ active: tab.key === activeTab }"
              @click="selectAdminTab(tab.key)"
            >{{ tab.name }}</button>
          </div>
        </div>
      </div>
    </header>

    <main class="app-content">
      <section v-if="onboardingStatus.requiresOnboarding" class="onboarding-banner">
        <div class="onboarding-body">
          <span class="onboarding-icon">⚠</span>
          <div>
            <strong>首次启动还未配置 LLM Key</strong>
            <p>先进入 LLM 设置页保存可用通道的 API Key。安装包不内置用户密钥。</p>
          </div>
        </div>
        <button class="btn btn-sm btn-warning btn-pill" @click="openOnboardingTab">去配置</button>
      </section>

      <section v-if="appViewError" class="app-error-boundary" role="alert">
        <strong>页面组件已进入可恢复错误态</strong>
        <p>{{ appViewErrorMessage }}</p>
        <p v-if="appViewError.info" class="muted">位置：{{ appViewError.info }}</p>
        <button class="btn btn-sm btn-primary btn-pill" @click="resetAppViewError">重试当前页面</button>
      </section>
      <keep-alive :include="keepAliveWhitelist" v-else>
        <component
          :is="activeComponent"
          @settings-saved="loadOnboardingStatus()"
        />
      </keep-alive>
    </main>
    <AppToast />
    <ConfirmDialog />
  </div>
</template>

<style scoped>
/* ── 顶栏 ── */
.app-header {
  display: flex;
  align-items: center;
  height: 52px;
  padding: 0 var(--space-6);
  background: linear-gradient(135deg, #111827 0%, #1e293b 100%);
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  position: sticky;
  top: 0;
  z-index: var(--z-sticky);
}

/* ── 品牌区 ── */
.brand {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex-shrink: 0;
  margin-right: var(--space-6);
}
.brand-icon {
  color: var(--color-accent);
  width: 16px;
  height: 16px;
  flex-shrink: 0;
}
.brand-text {
  font-size: var(--text-lg);
  font-weight: 600;
  color: var(--color-text-on-dark);
  letter-spacing: 0.01em;
  white-space: nowrap;
}
.version-badge {
  display: inline-flex;
  align-items: center;
  padding: 2px 8px;
  border-radius: var(--radius-full);
  background: rgba(255, 255, 255, 0.08);
  color: var(--color-text-on-dark-muted);
  font-size: var(--text-xs);
  font-family: var(--font-family-mono);
  letter-spacing: 0.04em;
}

/* ── 导航 Tab ── */
.nav-tabs {
  display: flex;
  align-items: stretch;
  position: relative;
  overflow-x: auto;
  scrollbar-width: none;
  -webkit-overflow-scrolling: touch;
  gap: var(--space-0-5);
  flex: 1;
  min-width: 0;
}
.nav-tabs::-webkit-scrollbar { display: none; }

.nav-tab {
  display: inline-flex;
  align-items: center;
  height: 52px;
  padding: 0 var(--space-4);
  background: transparent;
  border: none;
  border-radius: 0;
  color: var(--color-text-on-dark-muted);
  font-size: var(--text-base);
  font-weight: 500;
  cursor: pointer;
  white-space: nowrap;
  transition: color var(--transition-fast), background var(--transition-fast);
  position: relative;
}
.nav-tab:hover {
  color: var(--color-text-on-dark);
  background: var(--color-bg-header-hover);
}
.nav-tab.active {
  color: #ffffff;
  font-weight: 600;
}
.nav-tab-short { display: none; }

/* 滑动指示线 */
.nav-indicator {
  position: absolute;
  bottom: 0;
  height: 2px;
  background: var(--color-accent);
  border-radius: 1px 1px 0 0;
  transition: left var(--transition-normal), width var(--transition-normal);
  pointer-events: none;
}

/* ── 右侧状态区 ── */
.header-status {
  flex-shrink: 0;
  margin-left: var(--space-4);
  display: flex;
  align-items: center;
  gap: var(--space-3);
}
.header-clock {
  font-size: var(--text-sm);
  color: var(--color-text-on-dark-muted);
  font-family: var(--font-family-mono);
}

.trading-session-badge {
  font-size: 11px;
  padding: 1px 6px;
  border-radius: 3px;
  font-weight: 500;
  line-height: 1.4;
}
.trading-session-badge.session-open {
  color: #22c55e;
  background: rgba(34, 197, 94, 0.15);
}
.trading-session-badge.session-auction {
  color: #f59e0b;
  background: rgba(245, 158, 11, 0.15);
}
.trading-session-badge.session-break {
  color: #f59e0b;
  background: rgba(245, 158, 11, 0.12);
}
.trading-session-badge.session-closed {
  color: var(--color-text-on-dark-muted);
  background: rgba(148, 163, 184, 0.12);
}

.connection-indicator {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  font-size: 11px;
  color: var(--color-text-on-dark-muted);
  cursor: default;
}

.connection-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--color-text-on-dark-muted);
  transition: background 0.3s;
}

.connection-indicator.online .connection-dot {
  background: #22c55e;
  box-shadow: 0 0 6px rgba(34, 197, 94, 0.5);
}

.connection-indicator.offline .connection-dot {
  background: #ef4444;
  box-shadow: 0 0 6px rgba(239, 68, 68, 0.5);
}

.connection-label {
  opacity: 0.8;
}

/* ── 设置下拉 ── */
.settings-dropdown-wrap {
  position: relative;
}
.settings-trigger {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  border: none;
  border-radius: var(--radius-md);
  background: transparent;
  color: var(--color-text-on-dark-muted);
  cursor: pointer;
  transition: color var(--transition-fast), background var(--transition-fast);
}
.settings-trigger:hover,
.settings-trigger.active {
  color: #ffffff;
  background: rgba(255, 255, 255, 0.1);
}
.settings-dropdown {
  position: absolute;
  top: calc(100% + 8px);
  right: 0;
  min-width: 180px;
  padding: var(--space-1);
  background: var(--color-bg-surface);
  border: 1px solid var(--color-border-light);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-lg);
  z-index: calc(var(--z-sticky) + 10);
}
.settings-item {
  display: block;
  width: 100%;
  padding: var(--space-2) var(--space-3);
  border: none;
  border-radius: var(--radius-md);
  background: transparent;
  color: var(--color-text-body);
  font-size: var(--text-sm);
  text-align: left;
  cursor: pointer;
  transition: background var(--transition-fast);
}
.settings-item:hover {
  background: var(--color-bg-surface-alt);
}
.settings-item.active {
  color: var(--color-accent);
  font-weight: 600;
}

/* ── 内容区 ── */
.app-content {
  flex: 1;
  padding: var(--space-5);
  overflow-y: auto;
}

.app-error-boundary {
  display: grid;
  gap: var(--space-3);
  padding: var(--space-5);
  border: 1px solid var(--color-danger-border, rgba(239, 68, 68, 0.35));
  border-radius: var(--radius-lg);
  background: var(--color-danger-bg, rgba(254, 242, 242, 0.95));
  color: var(--color-text-primary);
}

.app-error-boundary p {
  margin: 0;
}

.app-error-boundary .btn {
  justify-self: start;
}

/* ── Onboarding Banner ── */
.onboarding-banner {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-4);
  margin-bottom: var(--space-4);
  padding: var(--space-4) var(--space-5);
  background: var(--color-warning-bg);
  border: 1px solid var(--color-warning-border);
  border-radius: var(--radius-lg);
}
.onboarding-body {
  display: flex;
  align-items: flex-start;
  gap: var(--space-3);
}
.onboarding-icon {
  color: var(--color-warning);
  font-size: var(--text-xl);
  flex-shrink: 0;
  margin-top: 1px;
}
.onboarding-banner strong {
  color: var(--color-text-primary);
}
.onboarding-banner p {
  margin: var(--space-1) 0 0;
  color: var(--color-text-secondary);
  font-size: var(--text-base);
}

/* ── 响应式 ── */
@media (max-width: 1200px) {
  .nav-tab-full { display: none; }
  .nav-tab-short { display: inline; }
}
@media (min-width: 1201px) {
  .nav-tab-short { display: none; }
  .nav-tab-full { display: inline; }
}
@media (max-width: 800px) {
  .brand-text { display: none; }
  .nav-tab { padding: 0 var(--space-3); }
}
</style>
