<script setup>
import { onActivated } from 'vue'
import { usePersistedRef, readLayoutValue } from './useLayoutPersistence'

const props = defineProps({
  hasUnreadAlerts: { type: Boolean, default: false }
})

const tabs = [
  { key: 'plans', label: '交易计划', icon: '📋' },
  { key: 'news',  label: '新闻影响', icon: '📰' },
  { key: 'ai',    label: 'AI 分析',  icon: '🤖' },
  { key: 'board', label: '全局总览', icon: '🌐' },
  { key: 'financial', label: '财务报表', icon: '📊' }
]

const activeTab = usePersistedRef('sidebar_active_tab', 'plans')

// Bug #58: keep-alive re-activation — restore sidebar tab from localStorage
onActivated(() => {
  const stored = readLayoutValue('sidebar_active_tab', 'plans')
  if (stored !== activeTab.value) {
    activeTab.value = stored
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
        :aria-selected="activeTab === tab.key"
        class="sc-tabs__item"
        :class="{ 'sc-tabs__item--active': activeTab === tab.key }"
        @click="activeTab = tab.key"
      >
        <span class="sc-tabs__icon">{{ tab.icon }}</span>
        <span class="sc-tabs__label">{{ tab.label }}</span>
        <span v-if="tab.key === 'plans' && hasUnreadAlerts" class="sc-tabs__badge" />
      </button>
    </div>
    <div class="sc-tabs__panel">
      <div v-show="activeTab === 'plans'"><slot name="plans" /></div>
      <div v-show="activeTab === 'news'"><slot name="news" /></div>
      <div v-show="activeTab === 'ai'"><slot name="ai" /></div>
      <div v-show="activeTab === 'board'"><slot name="board" /></div>
      <div v-show="activeTab === 'financial'"><slot name="financial" :active="activeTab === 'financial'" /></div>
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
