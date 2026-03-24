<script setup>
defineProps({
  workspace: {
    type: Object,
    required: true
  },
  formatTradingPlanStatus: {
    type: Function,
    required: true
  },
  getTradingPlanStatusClass: {
    type: Function,
    required: true
  },
  formatPlanScale: {
    type: Function,
    required: true
  },
  formatPlanPrice: {
    type: Function,
    required: true
  },
  getLatestPlanAlert: {
    type: Function,
    required: true
  },
  getPlanAlertClass: {
    type: Function,
    required: true
  },
  formatPlanAlertSummary: {
    type: Function,
    required: true
  },
  getPlanReviewHeadline: {
    type: Function,
    required: true
  },
  getPlanReviewText: {
    type: Function,
    required: true
  }
})

defineEmits(['refresh', 'jump'])
</script>

<template>
  <section class="copilot-card trading-plan-card trading-plan-board-card">
    <div class="trading-plan-header">
      <div>
        <h3>交易计划总览</h3>
        <p class="muted">不选股也能直接查看最近交易计划，并快速跳转到对应标的。</p>
      </div>
      <button class="board-refresh-button plan-refresh-button" @click="$emit('refresh')" :disabled="workspace.planListLoading || workspace.planAlertsLoading">
        刷新
      </button>
    </div>

    <p v-if="workspace.planError" class="muted error">{{ workspace.planError }}</p>
    <p v-else-if="workspace.planListLoading && !workspace.planList.length" class="muted">加载中...</p>
    <ul v-else-if="workspace.planList.length" class="plan-list plan-board-list">
      <li v-for="item in workspace.planList" :key="`plan-board-${item.id}`" class="plan-item plan-item-compact">
        <div class="plan-item-header">
          <div class="plan-item-title">
            <strong>{{ item.name }} · {{ formatTradingPlanStatus(item.status) }}</strong>
            <small>{{ item.symbol }}</small>
          </div>
          <button class="plan-link-button" @click="$emit('jump', item.symbol)">查看股票</button>
        </div>
        <div class="plan-status-row">
          <span class="plan-status-badge" :class="getTradingPlanStatusClass(item.status)">{{ formatTradingPlanStatus(item.status) }}</span>
          <span v-if="item.marketContextAtCreation" class="plan-pill">建立时 {{ item.marketContextAtCreation.stageLabel }}</span>
          <span v-if="item.currentMarketContext" class="plan-pill">当前 {{ item.currentMarketContext.stageLabel }}</span>
        </div>
        <p>{{ item.analysisSummary || item.expectedCatalyst || '等待补充计划摘要' }}</p>
        <div v-if="item.marketContextAtCreation || item.currentMarketContext" class="plan-market-context">
          <span v-if="item.marketContextAtCreation">建立时：{{ item.marketContextAtCreation.mainlineSectorName || '无主线' }} / 建议仓位 {{ formatPlanScale(item.marketContextAtCreation.suggestedPositionScale) }}</span>
          <span v-if="item.currentMarketContext">当前：{{ item.currentMarketContext.executionFrequencyLabel || '中性' }} / {{ item.currentMarketContext.mainlineSectorName || '无主线' }}</span>
        </div>
        <div
          v-if="getLatestPlanAlert(workspace, item.id)"
          class="plan-alert"
          :class="getPlanAlertClass(getLatestPlanAlert(workspace, item.id).severity)"
        >
          <strong>{{ getLatestPlanAlert(workspace, item.id).eventType }}</strong>
          <span>{{ formatPlanAlertSummary(getLatestPlanAlert(workspace, item.id)) }}</span>
          <small v-if="getPlanReviewHeadline(getLatestPlanAlert(workspace, item.id))">关联新闻：{{ getPlanReviewHeadline(getLatestPlanAlert(workspace, item.id)) }}</small>
          <small v-if="getPlanReviewText(getLatestPlanAlert(workspace, item.id))">复核结论：{{ getPlanReviewText(getLatestPlanAlert(workspace, item.id)) }}</small>
        </div>
        <div class="plan-pill-row">
          <span class="plan-pill">方向 {{ item.direction }}</span>
          <span class="plan-pill">触发 {{ formatPlanPrice(item.triggerPrice) }}</span>
          <span class="plan-pill">止损 {{ formatPlanPrice(item.stopLossPrice) }}</span>
          <span class="plan-pill">止盈 {{ formatPlanPrice(item.takeProfitPrice) }}</span>
          <span class="plan-pill">目标 {{ formatPlanPrice(item.targetPrice) }}</span>
        </div>
      </li>
    </ul>
    <p v-else class="muted">暂无交易计划，可从 commander 分析一键起草。</p>
  </section>
</template>

<style scoped>
.trading-plan-card {
  display: grid;
  gap: 0.85rem;
}

.trading-plan-board-card {
  margin-bottom: 1rem;
}

.trading-plan-header,
.plan-item-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 0.75rem;
}

.board-refresh-button,
.plan-link-button {
  border: none;
  border-radius: 999px;
  padding: 0.35rem 0.75rem;
  cursor: pointer;
}

.board-refresh-button {
  color: #0f172a;
  background: rgba(15, 23, 42, 0.08);
}

.board-refresh-button:disabled,
.plan-link-button:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.plan-list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: grid;
  gap: 0.75rem;
}

.plan-board-list {
  max-height: 20rem;
  overflow-y: auto;
  padding-right: 0.2rem;
}

.plan-item {
  display: grid;
  gap: 0.45rem;
  padding: 0.9rem 1rem;
  border-radius: 16px;
  background: rgba(248, 250, 252, 0.92);
  border: 1px solid rgba(148, 163, 184, 0.18);
}

.plan-item-compact {
  gap: 0.35rem;
}

.plan-item-title {
  display: grid;
  gap: 0.12rem;
}

.plan-item p,
.plan-item small {
  margin: 0;
}

.plan-status-row,
.plan-pill-row,
.plan-market-context {
  display: flex;
  align-items: center;
  gap: 0.45rem;
  flex-wrap: wrap;
}

.plan-status-badge,
.plan-pill {
  display: inline-flex;
  align-items: center;
  border-radius: 999px;
}

.plan-status-badge {
  padding: 0.14rem 0.55rem;
  font-size: 0.74rem;
  font-weight: 700;
  letter-spacing: 0.03em;
}

.plan-status-pending {
  background: rgba(37, 99, 235, 0.12);
  color: #1d4ed8;
}

.plan-status-triggered {
  background: rgba(22, 163, 74, 0.12);
  color: #15803d;
}

.plan-status-invalid {
  background: rgba(100, 116, 139, 0.16);
  color: #475569;
}

.plan-status-review-required {
  background: rgba(239, 68, 68, 0.12);
  color: #b91c1c;
}

.plan-pill {
  padding: 0.18rem 0.55rem;
  background: rgba(37, 99, 235, 0.08);
  color: #1d4ed8;
  font-size: 0.78rem;
}

.plan-link-button {
  background: rgba(37, 99, 235, 0.12);
  color: #1d4ed8;
}

.error {
  color: #b91c1c;
}

.plan-board-list::-webkit-scrollbar {
  width: 8px;
}

.plan-board-list::-webkit-scrollbar-thumb {
  border-radius: 999px;
  background: rgba(148, 163, 184, 0.45);
}

.plan-alert {
  display: grid;
  gap: 0.18rem;
  margin: 0.35rem 0 0.15rem;
  padding: 0.55rem 0.7rem;
  border-radius: 12px;
  border: 1px solid rgba(148, 163, 184, 0.2);
  font-size: 0.82rem;
}

.plan-alert strong {
  font-size: 0.74rem;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

.plan-alert small {
  font-size: 0.76rem;
}

.plan-alert-warning {
  background: rgba(245, 158, 11, 0.12);
  border-color: rgba(245, 158, 11, 0.24);
  color: #92400e;
}

.plan-alert-critical {
  background: rgba(239, 68, 68, 0.12);
  border-color: rgba(239, 68, 68, 0.24);
  color: #991b1b;
}

.plan-alert-info {
  background: rgba(14, 165, 233, 0.12);
  border-color: rgba(14, 165, 233, 0.24);
  color: #0c4a6e;
}

@media (max-width: 720px) {
  .trading-plan-header,
  .plan-item-header {
    flex-direction: column;
  }
}
</style>