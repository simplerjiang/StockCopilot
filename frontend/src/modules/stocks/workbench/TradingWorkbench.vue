<script setup>
import { toRef, ref, watch, onMounted, onUnmounted } from 'vue'
import { useTradingWorkbench } from './useTradingWorkbench.js'
import TradingWorkbenchHeader from './TradingWorkbenchHeader.vue'
import TradingWorkbenchProgress from './TradingWorkbenchProgress.vue'
import TradingWorkbenchFeed from './TradingWorkbenchFeed.vue'
import TradingWorkbenchReport from './TradingWorkbenchReport.vue'
import TradingWorkbenchComposer from './TradingWorkbenchComposer.vue'
import TradingWorkbenchHistory from './TradingWorkbenchHistory.vue'

const props = defineProps({
  symbol: { type: String, default: '' }
})

const emit = defineEmits(['navigate-chart', 'navigate-plan'])

const symbolRef = toRef(props, 'symbol')
const wb = useTradingWorkbench(symbolRef)

const isFullscreen = ref(false)
const workbenchEl = ref(null)

function toggleFullscreen() {
  isFullscreen.value = !isFullscreen.value
}

function onKeydown(e) {
  if (e.key === 'Escape' && isFullscreen.value) {
    isFullscreen.value = false
  }
}

onMounted(() => {
  document.addEventListener('keydown', onKeydown)
  // Load history sessions for the history tab
  if (symbolRef.value) wb.loadSessions()
})
onUnmounted(() => { document.removeEventListener('keydown', onKeydown) })

// P1-3: Auto-switch tab based on running state (skip if user manually switched recently)
const lastManualTabSwitch = ref(0)

// Reload sessions when symbol changes
watch(symbolRef, (sym) => {
  if (sym) wb.loadSessions()
})

watch(() => wb.isRunning.value, (running, wasRunning) => {
  const recentManual = Date.now() - lastManualTabSwitch.value < 5000
  if (recentManual) return
  if (running && !wasRunning) {
    wb.activeTab.value = 'feed'
  } else if (!running && wasRunning) {
    wb.activeTab.value = 'report'
  }
})

function handleNextAction(action) {
  if (action.actionType === 'ViewDailyChart' || action.actionType === 'ViewMinuteChart') {
    emit('navigate-chart', action)
  } else if (action.actionType === 'DraftTradingPlan') {
    emit('navigate-plan', action)
  } else if (action.actionType === 'RefreshNews') {
    wb.submitFollowUp('请重新获取最新新闻，并基于新数据更新分析结论')
  } else if (action.actionType === 'DeepAnalysis') {
    wb.submitFollowUp('请对当前分析进行更深入的研究，补充更多数据和论据')
  } else if (action.description) {
    wb.submitFollowUp(action.description)
  }
}
</script>

<template>
  <div ref="workbenchEl" :class="['trading-workbench', { 'wb-fullscreen': isFullscreen }]">
    <!-- Session header -->
    <TradingWorkbenchHeader
      :session="wb.session.value"
      :active-turn="wb.activeTurn.value"
      :session-status="wb.sessionStatus.value"
      :current-stage="wb.currentStageName.value"
      :is-running="wb.isRunning.value"
      :error="wb.error.value"
      :is-fullscreen="isFullscreen"
      :symbol="symbol"
      @refresh="wb.loadActiveSession()"
      @toggle-fullscreen="toggleFullscreen"
    />

    <!-- Fullscreen: multi-panel layout -->
    <template v-if="isFullscreen">
      <div class="wb-fullscreen-body">
        <div class="wb-fs-sidebar">
          <div class="wb-fs-panel-title">⏱️ 团队进度</div>
          <TradingWorkbenchProgress
            :stages="wb.stageSnapshots.value"
            :is-running="wb.isRunning.value"
            :error="wb.error.value"
            @rerun-from-stage="wb.rerunFromStage($event)"
          />
        </div>
        <div class="wb-fs-main">
          <div class="wb-fs-feed">
            <div class="wb-fs-panel-title">💬 讨论动态</div>
            <TradingWorkbenchFeed
              :items="wb.feedItems.value"
              :active-turn="wb.activeTurn.value"
              :is-running="wb.isRunning.value"
              :current-stage="wb.currentStageName.value"
              :session-name="wb.sessionDetail.value?.name || wb.session.value?.name || ''"
            />
          </div>
          <div class="wb-fs-report">
            <div class="wb-fs-panel-title">📋 研究报告</div>
            <TradingWorkbenchReport
              :blocks="wb.reportBlocks.value"
              :decision="wb.decision.value"
              :next-actions="wb.nextActions.value"
              :rag-citations="wb.ragCitations.value"
              :loading="wb.loading.value"
              :error="wb.error.value"
              @action="handleNextAction"
            />
          </div>
        </div>
      </div>
    </template>

    <!-- Normal: tab layout -->
    <template v-else>
      <!-- Replay mode banner -->
      <div v-if="wb.replayTurnId.value" class="wb-replay-banner">
        <span>🔄 正在查看历史记录 Turn #{{ wb.activeTurn.value?.turnIndex ?? '?' }}</span>
        <button class="wb-replay-back-btn" @click="wb.exitReplay()">返回最新 →</button>
      </div>

      <!-- Tab navigation -->
      <div class="wb-tabs">
        <button
          v-for="tab in [
            { key: 'report', label: '研究报告', icon: '📋' },
            { key: 'progress', label: '团队进度', icon: '⏱️' },
            { key: 'feed', label: '讨论动态', icon: '💬' },
            { key: 'history', label: '历史记录', icon: '📚' }
          ]"
          :key="tab.key"
          :class="['wb-tab', { active: wb.activeTab.value === tab.key }]"
          @click="wb.activeTab.value = tab.key; lastManualTabSwitch = Date.now()"
        >
          <span class="tab-icon">{{ tab.icon }}</span>
          <span class="tab-label">{{ tab.label }}</span>
        </button>
      </div>

      <!-- Tab content -->
      <div class="wb-content">
        <TradingWorkbenchReport
          v-show="wb.activeTab.value === 'report'"
          :blocks="wb.reportBlocks.value"
          :decision="wb.decision.value"
          :next-actions="wb.nextActions.value"
          :rag-citations="wb.ragCitations.value"
          :loading="wb.loading.value"
          :error="wb.error.value"
          @action="handleNextAction"
        />
        <TradingWorkbenchProgress
          v-show="wb.activeTab.value === 'progress'"
          :stages="wb.stageSnapshots.value"
          :is-running="wb.isRunning.value"
          :error="wb.error.value"
          @rerun-from-stage="wb.rerunFromStage($event)"
        />
        <TradingWorkbenchFeed
          v-show="wb.activeTab.value === 'feed'"
          :items="wb.feedItems.value"
          :active-turn="wb.activeTurn.value"
          :is-running="wb.isRunning.value"
          :current-stage="wb.currentStageName.value"
          :session-name="wb.sessionDetail.value?.name || wb.session.value?.name || ''"
        />
        <TradingWorkbenchHistory
          v-show="wb.activeTab.value === 'history'"
          :sessions="wb.sessions.value"
          :active-session-id="wb.session.value?.id"
          :replay-turn-id="wb.replayTurnId.value"
          :expanded-session-id="wb.expandedHistorySessionId.value"
          :expanded-turns="wb.expandedTurns.value"
          :history-loading-session-id="wb.historyLoadingSessionId.value"
          :history-session-error-id="wb.historySessionErrorId.value"
          :history-session-error-message="wb.historySessionErrorMessage.value"
          :loading="wb.loading.value"
          @select-session="wb.expandHistorySession($event)"
          @select-turn="wb.enterReplay($event.sessionId, $event.turnId)"
          @back-to-live="wb.exitReplay()"
        />
      </div>
    </template>

    <!-- Follow-up composer -->
    <TradingWorkbenchComposer
      :session="wb.session.value"
      :is-running="wb.isRunning.value"
      :symbol="symbol"
      @submit="wb.submitFollowUp($event.prompt, $event.options)"
      @cancel="wb.cancelAnalysis()"
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
  background: var(--color-bg-surface);
  border-radius: 8px;
  border: 1px solid var(--color-border-light);
  overflow: hidden;
  min-height: 420px;
  max-height: calc(100vh - 260px);
}

/* ── Fullscreen mode ───────────────────────────── */
.wb-fullscreen {
  position: fixed;
  inset: 0;
  z-index: 9999;
  max-height: none;
  min-height: 100vh;
  border-radius: 0;
  border: none;
}
.wb-fullscreen-body {
  display: flex;
  flex: 1;
  min-height: 0;
  overflow: hidden;
}
.wb-fs-sidebar {
  width: 280px;
  min-width: 220px;
  border-right: 1px solid var(--color-border-light);
  overflow-y: auto;
  display: flex;
  flex-direction: column;
}
.wb-fs-main {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-width: 0;
  overflow: hidden;
}
.wb-fs-feed {
  flex: 1;
  overflow-y: auto;
  border-bottom: 1px solid var(--color-border-light);
  min-height: 0;
}
.wb-fs-report {
  flex: 1;
  overflow-y: auto;
  min-height: 0;
}
.wb-fs-panel-title {
  position: sticky;
  top: 0;
  z-index: 1;
  background: var(--color-bg-surface-alt);
  padding: 6px 12px;
  font-size: 14px;
  font-weight: 600;
  color: var(--color-text-secondary);
  border-bottom: 1px solid var(--color-border-light);
}

/* ── Tabs ──────────────────────────────────────── */
.wb-tabs {
  display: flex;
  border-bottom: 1px solid var(--color-border-light);
  background: var(--color-bg-surface-alt);
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
  color: var(--color-text-secondary);
  cursor: pointer;
  font-size: 14px;
  transition: color 0.15s, border-bottom 0.15s;
  border-bottom: 2px solid transparent;
}
.wb-tab:hover {
  color: var(--color-text-body);
}
.wb-tab.active {
  color: var(--color-accent);
  border-bottom-color: var(--color-accent);
}
.tab-icon { font-size: 15px; }
.tab-label { font-size: 13px; font-weight: 500; }

/* ── Replay banner ─────────────────────────────── */
.wb-replay-banner {
  display: flex; align-items: center; justify-content: space-between;
  padding: 4px 12px;
  background: color-mix(in srgb, var(--color-accent) 8%, transparent);
  border-bottom: 1px solid rgba(91, 156, 246, 0.2);
  font-size: 13px; color: var(--color-accent);
}
.wb-replay-back-btn {
  background: transparent; border: 1px solid var(--color-accent);
  color: var(--color-accent); border-radius: 4px;
  padding: 1px 8px; font-size: 12px; cursor: pointer;
}
.wb-replay-back-btn:hover { background: color-mix(in srgb, var(--color-accent) 12%, transparent); }

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
  color: var(--color-text-secondary);
}
.wb-empty-icon { font-size: 32px; margin-bottom: 8px; }
.wb-empty h4 {
  font-size: 16px;
  color: var(--color-text-body);
  margin: 0 0 8px;
}
.wb-empty p {
  font-size: 14px;
  line-height: 1.5;
  margin: 0;
}
</style>
