<script setup>
import { toRef } from 'vue'
import { useTradingWorkbench } from './useTradingWorkbench.js'
import TradingWorkbenchHeader from './TradingWorkbenchHeader.vue'
import TradingWorkbenchProgress from './TradingWorkbenchProgress.vue'
import TradingWorkbenchFeed from './TradingWorkbenchFeed.vue'
import TradingWorkbenchReport from './TradingWorkbenchReport.vue'
import TradingWorkbenchComposer from './TradingWorkbenchComposer.vue'

const props = defineProps({
  symbol: { type: String, default: '' }
})

const emit = defineEmits(['navigate-chart', 'navigate-plan'])

const symbolRef = toRef(props, 'symbol')
const wb = useTradingWorkbench(symbolRef)

function handleNextAction(action) {
  if (action.actionType === 'ViewDailyChart' || action.actionType === 'ViewMinuteChart') {
    emit('navigate-chart', action)
  } else if (action.actionType === 'DraftTradingPlan') {
    emit('navigate-plan', action)
  }
}
</script>

<template>
  <div class="trading-workbench">
    <!-- Session header -->
    <TradingWorkbenchHeader
      :session="wb.session.value"
      :active-turn="wb.activeTurn.value"
      :session-status="wb.sessionStatus.value"
      :current-stage="wb.currentStageName.value"
      :is-running="wb.isRunning.value"
      :error="wb.error.value"
      @refresh="wb.loadActiveSession()"
    />

    <!-- Tab navigation -->
    <div class="wb-tabs">
      <button
        v-for="tab in [
          { key: 'report', label: '研究报告', icon: '📋' },
          { key: 'progress', label: '团队进度', icon: '⏱️' },
          { key: 'feed', label: '讨论动态', icon: '💬' }
        ]"
        :key="tab.key"
        :class="['wb-tab', { active: wb.activeTab.value === tab.key }]"
        @click="wb.activeTab.value = tab.key"
      >
        <span class="tab-icon">{{ tab.icon }}</span>
        <span class="tab-label">{{ tab.label }}</span>
      </button>
    </div>

    <!-- Tab content -->
    <div class="wb-content">
      <!-- Report panel (main reading area) -->
      <TradingWorkbenchReport
        v-show="wb.activeTab.value === 'report'"
        :blocks="wb.reportBlocks.value"
        :decision="wb.decision.value"
        :next-actions="wb.nextActions.value"
        :loading="wb.loading.value"
        @action="handleNextAction"
      />

      <!-- Progress panel -->
      <TradingWorkbenchProgress
        v-show="wb.activeTab.value === 'progress'"
        :stages="wb.stageSnapshots.value"
        :is-running="wb.isRunning.value"
      />

      <!-- Feed panel -->
      <TradingWorkbenchFeed
        v-show="wb.activeTab.value === 'feed'"
        :items="wb.feedItems.value"
        :active-turn="wb.activeTurn.value"
      />
    </div>

    <!-- Follow-up composer -->
    <TradingWorkbenchComposer
      :session="wb.session.value"
      :is-running="wb.isRunning.value"
      :symbol="symbol"
      @submit="wb.submitFollowUp($event.prompt, $event.options)"
    />

    <!-- Empty state -->
    <div v-if="!wb.session.value && !wb.loading.value && symbol" class="wb-empty">
      <div class="wb-empty-icon">🔬</div>
      <h4>多角色研究工作台</h4>
      <p>这不是普通聊天助手——输入研究指令后，多个专业角色将协同分析该股票。</p>
    </div>
  </div>
</template>

<style scoped>
.trading-workbench {
  display: flex;
  flex-direction: column;
  gap: 0;
  background: var(--wb-bg, #1a1d23);
  border-radius: 8px;
  border: 1px solid var(--wb-border, #2a2d35);
  overflow: hidden;
  min-height: 420px;
  max-height: calc(100vh - 260px);
}

/* ── Tabs ──────────────────────────────────────── */
.wb-tabs {
  display: flex;
  border-bottom: 1px solid var(--wb-border, #2a2d35);
  background: var(--wb-header-bg, #1e2128);
  padding: 0;
}
.wb-tab {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 4px;
  padding: 8px 4px;
  border: none;
  background: transparent;
  color: var(--wb-text-muted, #8b8fa3);
  cursor: pointer;
  font-size: 12px;
  transition: color 0.15s, border-bottom 0.15s;
  border-bottom: 2px solid transparent;
}
.wb-tab:hover {
  color: var(--wb-text, #e1e4ea);
}
.wb-tab.active {
  color: var(--wb-accent, #5b9cf6);
  border-bottom-color: var(--wb-accent, #5b9cf6);
}
.tab-icon { font-size: 13px; }
.tab-label { font-size: 11px; font-weight: 500; }

/* ── Content area ──────────────────────────────── */
.wb-content {
  flex: 1;
  overflow-y: auto;
  min-height: 0;
}

/* ── Empty state ───────────────────────────────── */
.wb-empty {
  text-align: center;
  padding: 32px 20px;
  color: var(--wb-text-muted, #8b8fa3);
}
.wb-empty-icon { font-size: 32px; margin-bottom: 8px; }
.wb-empty h4 {
  font-size: 14px;
  color: var(--wb-text, #e1e4ea);
  margin: 0 0 8px;
}
.wb-empty p {
  font-size: 12px;
  line-height: 1.5;
  margin: 0;
}
</style>
