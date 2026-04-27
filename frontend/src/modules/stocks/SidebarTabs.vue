<script setup>
import { computed, onActivated, watch } from 'vue'
import { usePersistedRef, readLayoutValue } from './useLayoutPersistence'

const props = defineProps({
  hasUnreadAlerts: { type: Boolean, default: false },
  hasStock: { type: Boolean, default: true }
})

const tabs = [
  { key: 'plans', label: '交易计划', icon: '📋', requiresStock: true },
  { key: 'news',  label: '新闻影响', icon: '📰', requiresStock: true },
  { key: 'ai',    label: 'AI 分析',  icon: '🤖', requiresStock: true },
  { key: 'board', label: '全局总览', icon: '🌐' },
  { key: 'financial', label: '财务报表', icon: '📊', requiresStock: true }
]

const activeTab = usePersistedRef('sidebar_active_tab', 'plans')
const fallbackTabKey = 'board'
const isTabDisabled = tab => Boolean(tab?.requiresStock && !props.hasStock)
const canUseTabKey = tabKey => {
  const tab = tabs.find(item => item.key === tabKey)
  return Boolean(tab) && !isTabDisabled(tab)
}
const resolvedActiveTab = computed(() => canUseTabKey(activeTab.value) ? activeTab.value : fallbackTabKey)

const selectTab = tab => {
  if (isTabDisabled(tab)) return
  activeTab.value = tab.key
}

watch(() => props.hasStock, hasStock => {
  if (!hasStock && !canUseTabKey(activeTab.value)) {
    activeTab.value = fallbackTabKey
  }
})

// Bug #58: keep-alive re-activation — restore sidebar tab from localStorage
onActivated(() => {
  const stored = readLayoutValue('sidebar_active_tab', 'plans')
  if (canUseTabKey(stored) && stored !== activeTab.value) {
    activeTab.value = stored
  } else if (!canUseTabKey(activeTab.value)) {
    activeTab.value = fallbackTabKey
  }
})
</script>

<template>
  <div class="sc-tabs">
    <div class="sc-tabs__bar" role="tablist">
      <button
        v-for="tab in tabs"
        :key="tab.key"
        role="tab"
        :aria-selected="resolvedActiveTab === tab.key"
        :aria-disabled="isTabDisabled(tab)"
        :disabled="isTabDisabled(tab)"
        :title="isTabDisabled(tab) ? '选择股票后可用' : tab.label"
        class="sc-tabs__item"
        :class="{ 'sc-tabs__item--active': resolvedActiveTab === tab.key, 'sc-tabs__item--disabled': isTabDisabled(tab) }"
        @click="selectTab(tab)"
      >
        <span class="sc-tabs__icon">{{ tab.icon }}</span>
        <span class="sc-tabs__label">{{ tab.label }}</span>
        <span v-if="tab.key === 'plans' && hasUnreadAlerts" class="sc-tabs__badge" />
      </button>
    </div>
    <div class="sc-tabs__panel">
      <div v-show="resolvedActiveTab === 'plans'"><slot name="plans" /></div>
      <div v-show="resolvedActiveTab === 'news'"><slot name="news" /></div>
      <div v-show="resolvedActiveTab === 'ai'"><slot name="ai" /></div>
      <div v-show="resolvedActiveTab === 'board'"><slot name="board" /></div>
      <div v-show="resolvedActiveTab === 'financial'"><slot name="financial" :active="resolvedActiveTab === 'financial'" /></div>
    </div>
  </div>
</template>

<style scoped>
.sc-tabs {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
  border-radius: var(--radius-lg);
  background: var(--color-bg-surface);
  border: 1px solid var(--color-border-light);
  overflow: hidden;
}

.sc-tabs__bar {
  display: flex;
  border-bottom: 1px solid var(--color-border-light);
  background: var(--color-bg-surface-alt);
  flex-shrink: 0;
}

.sc-tabs__item {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: var(--space-1);
  padding: var(--space-2) var(--space-3);
  border: none;
  background: none;
  cursor: pointer;
  font-size: var(--text-base);
  color: var(--color-text-secondary);
  position: relative;
  transition: color var(--transition-fast), background var(--transition-fast);
  white-space: nowrap;
}

.sc-tabs__item:hover {
  color: var(--color-text-primary);
  background: var(--color-accent-subtle);
}

.sc-tabs__item--active {
  color: var(--color-accent);
  font-weight: 600;
}

.sc-tabs__item--disabled {
  opacity: 0.45;
  cursor: not-allowed;
}

.sc-tabs__item--disabled:hover {
  color: var(--color-text-secondary);
  background: transparent;
}

.sc-tabs__item--active::after {
  content: '';
  position: absolute;
  bottom: 0;
  left: var(--space-3);
  right: var(--space-3);
  height: 2px;
  background: var(--color-accent);
  border-radius: 1px;
}

.sc-tabs__icon {
  font-size: var(--text-md);
}

.sc-tabs__badge {
  position: absolute;
  top: var(--space-1);
  right: var(--space-2);
  width: 6px;
  height: 6px;
  border-radius: var(--radius-full);
  background: var(--color-danger);
}

.sc-tabs__panel {
  flex: 1;
  overflow-y: auto;
  padding: var(--space-3);
  min-height: 0;
}

.sc-tabs__label {
  /* hide on mobile, show on larger */
}

@media (max-width: 719px) {
  .sc-tabs__label {
    display: none;
  }
  .sc-tabs__item {
    padding: var(--space-2) var(--space-2);
  }
}
</style>
