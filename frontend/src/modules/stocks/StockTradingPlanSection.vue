<script setup>
import { ref } from 'vue'

const props = defineProps({
  workspace: {
    type: Object,
    required: true
  },
  deletingPlanId: {
    type: String,
    default: ''
  },
  resumingPlanId: {
    type: String,
    default: ''
  },
  formatTradingPlanStatus: {
    type: Function,
    required: true
  },
  formatDate: {
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
  },
  canEditTradingPlan: {
    type: Function,
    required: true
  },
  canResumeTradingPlan: {
    type: Function,
    required: true
  },
  canCancelTradingPlan: {
    type: Function,
    required: true
  }
})

const emit = defineEmits(['refresh', 'edit', 'resume', 'cancel', 'create'])

const confirmingCancelId = ref(null)

const requestCancel = (item) => {
  confirmingCancelId.value = String(item.id)
}

const confirmCancel = (item) => {
  confirmingCancelId.value = null
  emit('cancel', item)
}

const dismissCancel = () => {
  confirmingCancelId.value = null
}
</script>

<template>
  <section class="copilot-card trading-plan-card stock-plan-section" :class="{ 'copilot-section-active': workspace.copilotFocusSection === 'plan' || workspace.planModalOpen }">
    <div class="trading-plan-header">
      <div>
        <h3>当前交易计划</h3>
        <p class="muted">当前股票的全部交易计划都可在这里编辑或删除。</p>
      </div>
      <div class="plan-header-actions">
        <button class="market-news-button" @click="$emit('create')" :disabled="!workspace.detail">新建计划</button>
        <button class="market-news-button plan-refresh-button" @click="$emit('refresh')" :disabled="workspace.planListLoading || workspace.planAlertsLoading || !workspace.detail">
          刷新
        </button>
      </div>
    </div>

    <p v-if="workspace.planError" class="muted error">{{ workspace.planError }}</p>
    <p v-else-if="workspace.planListLoading && !workspace.planList.length" class="muted">加载中...</p>
    <ul v-else-if="workspace.planList.length" class="plan-list">
      <li v-for="item in workspace.planList" :key="`plan-${item.id}`" class="plan-item">
        <div class="plan-item-header">
          <div class="plan-item-title">
            <strong>{{ formatTradingPlanStatus(item.status) ? `${item.name} · ${formatTradingPlanStatus(item.status)}` : item.name }}</strong>
            <small class="muted">{{ formatDate(item.updatedAt || item.createdAt) }}</small>
          </div>
          <div class="plan-item-actions">
            <button v-if="canEditTradingPlan(item)" class="plan-link-button" @click="$emit('edit', item)">编辑</button>
            <button
              v-if="canResumeTradingPlan(item)"
              class="plan-link-button"
              @click="$emit('resume', item)"
              :disabled="resumingPlanId === String(item.id)"
            >
              {{ resumingPlanId === String(item.id) ? '恢复中...' : '恢复观察' }}
            </button>
            <button
              v-if="canCancelTradingPlan(item)"
              class="plan-danger-button"
              data-testid="cancel-plan-btn"
              @click="requestCancel(item)"
              :disabled="deletingPlanId === String(item.id) || confirmingCancelId === String(item.id)"
            >
              {{ deletingPlanId === String(item.id) ? '取消中...' : '取消' }}
            </button>
            <div v-if="confirmingCancelId === String(item.id)" class="confirm-popover">
              <span>确定取消此计划？</span>
              <div class="confirm-actions">
                <button class="confirm-yes" @click="confirmCancel(item)">确认</button>
                <button class="confirm-no" @click="dismissCancel">取消</button>
              </div>
            </div>
          </div>
        </div>
        <div class="plan-status-row">
          <span class="plan-status-badge" :class="getTradingPlanStatusClass(item.status)">{{ formatTradingPlanStatus(item.status) }}</span>
          <span v-if="item.marketContextAtCreation" class="plan-pill">建立时 {{ item.marketContextAtCreation.stageLabel }}</span>
          <span v-if="item.currentMarketContext" class="plan-pill">当前 {{ item.currentMarketContext.stageLabel }}</span>
        </div>
        <p>{{ item.analysisSummary || item.expectedCatalyst || '等待补充计划摘要' }}</p>
        <div v-if="item.marketContextAtCreation || item.currentMarketContext" class="plan-market-context">
          <span v-if="item.marketContextAtCreation">建立时：{{ item.marketContextAtCreation.mainlineSectorName || '无主线' }} / 仓位 {{ formatPlanScale(item.marketContextAtCreation.suggestedPositionScale) }}</span>
          <span v-if="item.currentMarketContext">当前：{{ item.currentMarketContext.executionFrequencyLabel || '中性' }} / {{ item.currentMarketContext.counterTrendWarning ? '逆势提示' : '顺势观察' }}</span>
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
          <span class="plan-pill">失效 {{ formatPlanPrice(item.invalidPrice) }}</span>
          <span class="plan-pill">止损 {{ formatPlanPrice(item.stopLossPrice) }}</span>
          <span class="plan-pill">止盈 {{ formatPlanPrice(item.takeProfitPrice) }}</span>
          <span class="plan-pill">目标 {{ formatPlanPrice(item.targetPrice) }}</span>
        </div>
        <small>{{ item.riskLimits || '风险摘要待补充' }}</small>
      </li>
    </ul>
    <p v-else class="muted">暂无交易计划，可点击「新建计划」手动录入，或从 commander 分析一键起草。</p>
  </section>
</template>

<style scoped>
.trading-plan-card {
  display: grid;
  gap: 0.85rem;
}

.trading-plan-header,
.plan-item-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 0.75rem;
}

.plan-header-actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-shrink: 0;
}

.market-news-button,
.plan-link-button,
.plan-danger-button {
  border: none;
  border-radius: 999px;
  padding: 0.35rem 0.75rem;
  cursor: pointer;
}

.market-news-button {
  color: #0f172a;
  background: rgba(15, 23, 42, 0.08);
}

.market-news-button:disabled,
.plan-danger-button:disabled,
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

.plan-item {
  display: grid;
  gap: 0.45rem;
  padding: 0.9rem 1rem;
  border-radius: 16px;
  background: rgba(248, 250, 252, 0.92);
  border: 1px solid rgba(148, 163, 184, 0.18);
}

.plan-item-title {
  display: grid;
  gap: 0.12rem;
}

.plan-item-actions,
.plan-status-row,
.plan-pill-row,
.plan-market-context {
  display: flex;
  align-items: center;
  gap: 0.45rem;
  flex-wrap: wrap;
}

.plan-item p,
.plan-item small {
  margin: 0;
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

.plan-danger-button {
  background: rgba(220, 38, 38, 0.12);
  color: #b91c1c;
}

.plan-item-actions {
  position: relative;
}

.confirm-popover {
  position: absolute;
  top: calc(100% + 6px);
  right: 0;
  z-index: 20;
  display: grid;
  gap: 0.5rem;
  padding: 0.75rem 1rem;
  min-width: 160px;
  background: var(--color-bg-surface, #fff);
  border: 1px solid var(--color-border-light, #e5e7eb);
  border-radius: 12px;
  box-shadow: 0 8px 24px rgba(15, 23, 42, 0.14);
  font-size: 0.85rem;
  color: var(--color-text-body, #334155);
}

.confirm-actions {
  display: flex;
  gap: 0.4rem;
  justify-content: flex-end;
}

.confirm-yes,
.confirm-no {
  border: none;
  border-radius: 8px;
  padding: 0.3rem 0.65rem;
  font-size: 0.8rem;
  cursor: pointer;
}

.confirm-yes {
  background: rgba(220, 38, 38, 0.14);
  color: #b91c1c;
  font-weight: 600;
}

.confirm-no {
  background: rgba(15, 23, 42, 0.06);
  color: var(--color-text-secondary, #64748b);
}

.error {
  color: #b91c1c;
}

@media (max-width: 720px) {
  .trading-plan-header,
  .plan-item-header {
    flex-direction: column;
  }
}
</style>