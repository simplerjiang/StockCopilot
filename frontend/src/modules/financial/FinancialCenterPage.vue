<script setup>
import { computed, onMounted, ref } from 'vue'
import FinancialFilterBar from './FinancialFilterBar.vue'
import FinancialReportTable from './FinancialReportTable.vue'
import FinancialDetailDrawer from './FinancialDetailDrawer.vue'
import FinancialCollectPanel from './FinancialCollectPanel.vue'
import { useFinancialCenterQuery } from './useFinancialCenterQuery.js'
import { DEFAULT_QUERY } from './financialCenterConstants.js'
import EmbeddingDegradedBanner from '../../components/EmbeddingDegradedBanner.vue'
import { useEmbeddingStatus } from '../../composables/useEmbeddingStatus.js'

const COLLECT_TAB_KEY = 'financial-data-test'

const {
  query,
  items,
  total,
  loading,
  error,
  symbolNameMap,
  fetchReports,
  resetQuery
} = useFinancialCenterQuery()

const drawerVisible = ref(false)
const drawerItem = ref(null)
const collectPanelOpen = ref(false)
const {
  status: embeddingStatus,
  loading: embeddingStatusLoading,
  error: embeddingStatusError,
  refreshEmbeddingStatus
} = useEmbeddingStatus()

const collectTabAvailable = computed(() => {
  if (typeof window === 'undefined') return false
  // 同一文档下的 mainTabs/adminTabs 来自 App.vue；这里只能通过 URL 探测，简化为 true 由父级 setActiveTab 校验。
  // App.vue 的 setActiveTab 内部已校验 validTabKeys，无效 key 不会切换。
  return true
})

/* ── 排序循环 ── */
const onSortChange = (field) => {
  // 如果当前不是该字段：第一次进入 → desc
  if (query.sortField !== field) {
    query.sortField = field
    query.sortDirection = 'desc'
  } else if (query.sortDirection === 'desc') {
    query.sortDirection = 'asc'
  } else {
    // 当前已是 asc → 重置回默认
    query.sortField = DEFAULT_QUERY.sortField
    query.sortDirection = DEFAULT_QUERY.sortDirection
  }
  query.page = 1
  fetchReports()
}

const onPageChange = (p) => {
  query.page = p
  fetchReports()
}

const onPageSizeChange = (size) => {
  query.pageSize = size
  query.page = 1
  fetchReports()
}

const onFilterSubmit = () => {
  query.page = 1
  fetchReports()
}

const onFilterChange = () => {
  fetchReports()
}

const onFilterReset = () => {
  resetQuery()
  fetchReports()
}

const onOpenDetail = (item) => {
  drawerItem.value = item
  drawerVisible.value = true
}

const onCloseDetail = () => {
  drawerVisible.value = false
}

const onRetry = () => fetchReports()

const onGoCollect = () => {
  // 通过自定义事件请求父级切换 tab；App.vue 的 setActiveTab 会校验 key 合法性
  window.dispatchEvent(new CustomEvent('navigate-tab', { detail: { tab: COLLECT_TAB_KEY } }))
  // 兼容：直接用 URL hashless query 触发也无意义；这里采用 emit 风格事件
}

function handleCollectSuccess() {
  fetchReports()
}

const hasFilter = computed(() => {
  if (query.symbols.length > 0) return true
  if (query.keyword && query.keyword.trim()) return true
  if (query.startDate !== DEFAULT_QUERY.startDate) return true
  if (query.endDate !== DEFAULT_QUERY.endDate) return true
  if (query.reportTypes.length !== DEFAULT_QUERY.reportTypes.length) return true
  return false
})

const refresh = () => fetchReports()

onMounted(() => {
  fetchReports()
  refreshEmbeddingStatus()
})
</script>

<template>
  <div class="fc-page">
    <header class="fc-page-header">
      <div class="fc-page-header-text">
        <h2 class="fc-page-title">财报中心</h2>
        <p class="fc-page-subtitle">统一查看与采集所有股票的财报数据</p>
      </div>
      <div class="fc-page-actions">
        <button
          type="button"
          class="fc-btn fc-btn--ghost"
          :disabled="loading"
          @click="refresh"
        >刷新</button>
        <button
          type="button"
          class="fc-btn"
          :class="collectPanelOpen ? 'fc-btn--active' : 'fc-btn--primary'"
          @click="collectPanelOpen = !collectPanelOpen"
        >{{ collectPanelOpen ? '▼ 收起采集' : '▶ 采集' }}</button>
      </div>
    </header>

    <EmbeddingDegradedBanner
      :status="embeddingStatus"
      :loading="embeddingStatusLoading"
      :error="embeddingStatusError"
      @refresh="refreshEmbeddingStatus"
    />

    <FinancialCollectPanel v-if="collectPanelOpen" @collect-success="handleCollectSuccess" />

    <FinancialFilterBar
      :query="query"
      :loading="loading"
      @submit="onFilterSubmit"
      @reset="onFilterReset"
      @change="onFilterChange"
    />

    <FinancialReportTable
      :items="items"
      :total="total"
      :loading="loading"
      :error="error"
      :query="query"
      :symbol-name-map="symbolNameMap"
      :has-filter="hasFilter"
      :collect-tab-available="collectTabAvailable"
      @sort-change="onSortChange"
      @page-change="onPageChange"
      @page-size-change="onPageSizeChange"
      @open-detail="onOpenDetail"
      @reset="onFilterReset"
      @retry="onRetry"
      @go-collect="onGoCollect"
    />

    <FinancialDetailDrawer
      :visible="drawerVisible"
      :item="drawerItem"
      :report-id="drawerItem?.id ?? null"
      :embedding-status="embeddingStatus"
      :embedding-loading="embeddingStatusLoading"
      :embedding-error="embeddingStatusError"
      :embedding-refresh="refreshEmbeddingStatus"
      @close="onCloseDetail"
    />
  </div>
</template>

<style scoped>
.fc-page {
  padding: var(--space-5) var(--space-6);
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
  background: var(--color-bg-body);
  min-height: 100%;
}

.fc-page-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: var(--space-4);
  flex-wrap: wrap;
}

.fc-page-header-text {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
}

.fc-page-title {
  margin: 0;
  font-size: var(--text-2xl);
  font-weight: 700;
  color: var(--color-text-primary);
}

.fc-page-subtitle {
  margin: 0;
  font-size: var(--text-sm);
  color: var(--color-text-secondary);
}

.fc-page-actions {
  display: flex;
  align-items: center;
  gap: var(--space-2);
}

.fc-btn {
  border-radius: var(--radius-md);
  padding: var(--space-2) var(--space-4);
  font-size: var(--text-md);
  font-family: var(--font-family-primary);
  cursor: pointer;
  transition: background var(--transition-fast), border-color var(--transition-fast);
  border: 1px solid transparent;
}

.fc-btn--primary {
  background: var(--color-accent);
  color: var(--color-text-on-dark);
}

.fc-btn--primary:hover:not(:disabled) {
  background: var(--color-accent-hover);
}

.fc-btn--ghost {
  background: var(--color-bg-surface);
  color: var(--color-text-body);
  border-color: var(--color-border-light);
}

.fc-btn--ghost:hover:not(:disabled) {
  border-color: var(--color-border-medium);
  background: var(--color-bg-surface-alt);
}

.fc-btn:disabled {
  cursor: not-allowed;
  opacity: 0.5;
}

.fc-btn--active {
  background: var(--color-bg-surface-alt, #2a3a4a);
  color: var(--color-text-body);
  border-color: var(--color-accent, #3b82f6);
}

.fc-btn--active:hover {
  background: var(--color-bg-surface, #1e2a3a);
}
</style>
