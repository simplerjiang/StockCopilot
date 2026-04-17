<script setup>
defineProps({
  workspace: {
    type: Object,
    default: null
  },
  formatPlanScale: {
    type: Function,
    required: true
  }
})

defineEmits(['close', 'save'])

function formatPercent(v) {
  return v == null ? '-' : (Number(v) * 100).toFixed(1) + '%'
}

function winRateColorClass(rate) {
  if (rate == null) return ''
  const pct = Number(rate)
  if (pct > 0.6) return 'winrate-green'
  if (pct >= 0.4) return 'winrate-yellow'
  return 'winrate-red'
}

function saveButtonClass(mode) {
  if (!mode) return ''
  switch (mode.confirmationLevel) {
    case 'confirm': return 'save-confirm'
    case 'strong-confirm': return 'save-strong-confirm'
    case 'discouraged': return 'save-discouraged'
    default: return ''
  }
}

function shouldShowMarketContextSection(form) {
  return Boolean(
    form?.marketContext
    || form?.marketContextLoading
    || form?.marketContextMessage
    || (!form?.id && form?.sourceAgent === 'manual')
  )
}

function formatSnapshotTime(value) {
  if (!value) return '--'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return String(value)
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  const hh = String(date.getHours()).padStart(2, '0')
  const mm = String(date.getMinutes()).padStart(2, '0')
  const ss = String(date.getSeconds()).padStart(2, '0')
  return `${y}-${m}-${d} ${hh}:${mm}:${ss}`
}
</script>

<template>
  <div v-if="workspace?.planModalOpen && workspace?.planForm" class="plan-modal-backdrop" @click.self="$emit('close')">
    <section class="plan-modal" role="dialog" aria-modal="true" aria-label="交易计划草稿">
      <div class="search-modal-header">
        <div>
          <strong>{{ workspace.planForm.id ? '编辑交易计划' : (workspace.planForm.sourceAgent === 'manual' ? '手动新建计划' : '交易计划草稿') }}</strong>
          <p class="muted">{{ workspace.planForm.sourceAgent === 'manual' ? '手动录入，确认后保存为 Pending 状态。' : '后端基于 commander 历史生成，用户确认后才会入库。' }}</p>
        </div>
        <button class="market-news-button" @click="$emit('close')">关闭</button>
      </div>

      <p v-if="workspace.planError" class="muted error">{{ workspace.planError }}</p>

      <section v-if="shouldShowMarketContextSection(workspace.planForm)" class="plan-market-box">
        <strong>市场上下文</strong>
        <template v-if="workspace.planForm.marketContext">
          <div class="plan-pill-row">
            <span class="plan-pill">快照时间 {{ formatSnapshotTime(workspace.planForm.marketContext.snapshotTime) }}</span>
            <span class="plan-pill">阶段 {{ workspace.planForm.marketContext.stageLabel }}</span>
            <span class="plan-pill">置信 {{ Number(workspace.planForm.marketContext.stageConfidence || 0).toFixed(0) }}</span>
            <span class="plan-pill">主线 {{ workspace.planForm.marketContext.mainlineSectorName || '暂无' }}</span>
            <span class="plan-pill">建议仓位 {{ formatPlanScale(workspace.planForm.marketContext.suggestedPositionScale) }}</span>
            <span class="plan-pill">节奏 {{ workspace.planForm.marketContext.executionFrequencyLabel || '中性' }}</span>
          </div>
          <p class="muted">
            {{ workspace.planForm.marketContext.isMainlineAligned ? '当前股票与主线方向一致。' : '当前股票未明显对齐主线。' }}
            <span v-if="workspace.planForm.marketContext.counterTrendWarning"> 存在逆势提示，建议降低执行频率。</span>
          </p>
        </template>
        <p v-else-if="workspace.planForm.marketContextLoading" class="muted plan-market-status">正在获取当前市场上下文，不影响保存计划。</p>
        <p v-else class="muted plan-market-status">{{ workspace.planForm.marketContextMessage || '暂未获取到市场上下文，可继续保存计划。' }}</p>
      </section>

      <!-- 执行模式提示条 -->
      <div v-if="workspace.planForm.executionMode" class="execution-mode-bar" :class="'mode-' + workspace.planForm.executionMode.confirmationLevel">
        <span class="mode-label">{{ workspace.planForm.executionMode.executionMode }}模式</span>
        <span class="mode-stage">市场阶段：{{ workspace.planForm.executionMode.marketStage }}</span>
        <span class="mode-scale">建议仓位 × {{ workspace.planForm.executionMode.positionScale }}</span>
        <span class="mode-warning" v-if="workspace.planForm.executionMode.warningMessage">⚠️ {{ workspace.planForm.executionMode.warningMessage }}</span>
      </div>

      <div class="plan-form-grid">
        <label class="plan-field">
          <span>股票</span>
          <input v-model="workspace.planForm.symbol" disabled>
        </label>
        <label class="plan-field">
          <span>名称</span>
          <input v-model="workspace.planForm.name">
        </label>
        <label class="plan-field">
          <span>方向</span>
          <select v-model="workspace.planForm.direction">
            <option value="Long">Long</option>
            <option value="Short">Short</option>
          </select>
        </label>
        <label class="plan-field">
          <span>触发价</span>
          <input v-model="workspace.planForm.triggerPrice" type="number" step="0.01" placeholder="可为空，后补录">
        </label>
        <label class="plan-field">
          <span>失效价</span>
          <input v-model="workspace.planForm.invalidPrice" type="number" step="0.01" placeholder="可为空，后补录">
        </label>
        <label class="plan-field">
          <span>止损价</span>
          <input v-model="workspace.planForm.stopLossPrice" type="number" step="0.01" placeholder="可为空">
        </label>
        <label class="plan-field">
          <span>止盈价</span>
          <input v-model="workspace.planForm.takeProfitPrice" type="number" step="0.01" placeholder="优先取指挥/机构目标">
        </label>
        <label class="plan-field">
          <span>目标价</span>
          <input v-model="workspace.planForm.targetPrice" type="number" step="0.01" placeholder="优先取指挥/趋势目标">
        </label>
        <label class="plan-field plan-field-wide">
          <span>预期催化</span>
          <textarea v-model="workspace.planForm.expectedCatalyst" rows="2"></textarea>
        </label>
        <label class="plan-field plan-field-wide">
          <span>失效条件</span>
          <textarea v-model="workspace.planForm.invalidConditions" rows="2"></textarea>
        </label>
        <label class="plan-field plan-field-wide">
          <span>风险上限</span>
          <textarea v-model="workspace.planForm.riskLimits" rows="2"></textarea>
        </label>
        <label class="plan-field plan-field-wide">
          <span>分析摘要</span>
          <textarea v-model="workspace.planForm.analysisSummary" rows="3"></textarea>
        </label>
        <label class="plan-field plan-field-wide">
          <span>用户备注</span>
          <textarea v-model="workspace.planForm.userNote" rows="2" placeholder="可补充执行纪律、仓位、观察点"></textarea>
        </label>
      </div>

      <section class="signal-winrate-section" v-if="workspace.planForm.metricsLoading || workspace.planForm.signalMetrics || workspace.planForm.realTradeMetrics">
        <strong>📊 双线胜率</strong>
        <p v-if="workspace.planForm.metricsLoading" class="muted">加载中...</p>
        <div v-else class="winrate-dual">
          <div class="winrate-card" v-if="workspace.planForm.signalMetrics">
            <div class="winrate-label">AI 纸面命中率</div>
            <div class="winrate-value" :class="winRateColorClass(workspace.planForm.signalMetrics.hitRate5Day)">
              {{ formatPercent(workspace.planForm.signalMetrics.hitRate5Day) }}
            </div>
            <div class="winrate-detail">
              过去 {{ workspace.planForm.signalMetrics.sampleCount }} 次"{{ workspace.planForm.signalMetrics.direction }}"建议，5日命中率
            </div>
            <div class="winrate-caveat" v-if="workspace.planForm.signalMetrics.caveat">{{ workspace.planForm.signalMetrics.caveat }}</div>
          </div>
          <div class="winrate-card" v-if="workspace.planForm.realTradeMetrics">
            <div class="winrate-label">你的实盘胜率</div>
            <div class="winrate-value" :class="winRateColorClass(workspace.planForm.realTradeMetrics.winRate)">
              {{ formatPercent(workspace.planForm.realTradeMetrics.winRate) }}
            </div>
            <div class="winrate-detail">
              过去 {{ workspace.planForm.realTradeMetrics.totalTrades }} 次交易，盈利 {{ workspace.planForm.realTradeMetrics.winCount }} 次
            </div>
            <div class="winrate-caveat" v-if="workspace.planForm.realTradeMetrics.caveat">{{ workspace.planForm.realTradeMetrics.caveat }}</div>
          </div>
        </div>
      </section>

      <div class="plan-modal-actions">
        <span class="muted">{{ workspace.planForm.id ? `编辑计划 #${workspace.planForm.id}` : (workspace.planForm.analysisHistoryId ? `AnalysisHistory #${workspace.planForm.analysisHistoryId}` : '手动新建') }}</span>
        <div class="plan-modal-buttons">
          <button class="market-news-button" @click="$emit('close')">取消</button>
          <button class="plan-save-button" :class="saveButtonClass(workspace.planForm.executionMode)" @click="$emit('save')" :disabled="workspace.planSaving || workspace.planDraftLoading">
            {{ workspace.planSaving ? '保存中...' : (workspace.planForm.id ? '保存修改' : '保存为 Pending 计划') }}
          </button>
        </div>
      </div>
    </section>
  </div>
</template>

<style scoped>
.plan-modal-backdrop {
  position: fixed;
  inset: 0;
  z-index: 70;
  display: grid;
  place-items: center;
  padding: 1rem;
  background: rgba(15, 23, 42, 0.58);
  backdrop-filter: blur(10px);
}

.plan-modal {
  display: grid;
  gap: 1rem;
  width: min(920px, 100%);
  max-height: min(82vh, 900px);
  overflow-y: auto;
  padding: 1.2rem;
  border-radius: 24px;
  border: 1px solid rgba(148, 163, 184, 0.18);
  background: linear-gradient(160deg, rgba(255, 255, 255, 0.98), rgba(241, 245, 249, 0.96));
}

.search-modal-header,
.plan-modal-actions,
.plan-modal-buttons {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 0.75rem;
}

.search-modal-header {
  color: #0f172a;
}

.plan-market-box {
  display: grid;
  gap: 0.65rem;
  padding: 0.9rem;
  border-radius: 16px;
  background: rgba(248, 250, 252, 0.92);
  border: 1px solid rgba(148, 163, 184, 0.18);
}

.plan-pill-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.45rem;
}

.plan-pill {
  display: inline-flex;
  align-items: center;
  border-radius: 999px;
  padding: 0.18rem 0.55rem;
  background: rgba(37, 99, 235, 0.08);
  color: #1d4ed8;
  font-size: 0.78rem;
}

.plan-market-status {
  color: #475569;
}

.plan-form-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 0.85rem;
}

.plan-field {
  display: grid;
  gap: 0.35rem;
}

.plan-field span {
  color: #334155;
  font-size: 0.82rem;
}

.plan-field input,
.plan-field select,
.plan-field textarea {
  border-radius: 12px;
  border: 1px solid rgba(148, 163, 184, 0.3);
  padding: 0.65rem 0.75rem;
  background: rgba(255, 255, 255, 0.95);
  color: #0f172a;
}

.plan-field-wide {
  grid-column: 1 / -1;
}

.market-news-button,
.plan-save-button {
  border: none;
  border-radius: 999px;
  cursor: pointer;
}

.market-news-button {
  padding: 0.35rem 0.75rem;
  background: rgba(15, 23, 42, 0.08);
  color: #0f172a;
}

.plan-save-button {
  padding: 0.55rem 0.95rem;
  background: linear-gradient(135deg, #0f766e, #0891b2);
  color: #f8fafc;
}

.market-news-button:disabled,
.plan-save-button:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.error {
  color: #b91c1c;
}

@media (max-width: 900px) {
  .plan-form-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 720px) {
  .search-modal-header,
  .plan-modal-actions,
  .plan-modal-buttons {
    flex-direction: column;
  }

  .plan-form-grid {
    grid-template-columns: 1fr;
  }
}

.signal-winrate-section {
  display: grid;
  gap: 0.65rem;
  padding: 0.9rem;
  border-radius: 16px;
  background: rgba(248, 250, 252, 0.92);
  border: 1px solid rgba(148, 163, 184, 0.18);
}

.signal-winrate-section strong {
  color: #0f172a;
  font-size: 0.9rem;
}

.winrate-dual {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 0.75rem;
}

.winrate-card {
  display: grid;
  gap: 0.35rem;
  padding: 0.75rem;
  border-radius: 12px;
  background: rgba(255, 255, 255, 0.95);
  border: 1px solid rgba(148, 163, 184, 0.15);
}

.winrate-label {
  font-size: 0.78rem;
  color: #64748b;
  font-weight: 500;
}

.winrate-value {
  font-size: 1.5rem;
  font-weight: 700;
  line-height: 1.2;
}

.winrate-green { color: #15803d; }
.winrate-yellow { color: #a16207; }
.winrate-red { color: #b91c1c; }

.winrate-detail {
  font-size: 0.75rem;
  color: #475569;
}

.winrate-caveat {
  font-size: 0.72rem;
  color: #94a3b8;
  font-style: italic;
}

/* 执行模式提示条 */
.execution-mode-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.65rem;
  padding: 0.7rem 0.9rem;
  border-radius: 12px;
  font-size: 0.82rem;
  border: 1px solid rgba(148, 163, 184, 0.18);
  background: rgba(248, 250, 252, 0.92);
}

.execution-mode-bar.mode-normal {
  border-color: rgba(34, 197, 94, 0.3);
  background: rgba(240, 253, 244, 0.92);
}

.execution-mode-bar.mode-confirm {
  border-color: rgba(234, 179, 8, 0.35);
  background: rgba(254, 252, 232, 0.92);
}

.execution-mode-bar.mode-strong-confirm {
  border-color: rgba(234, 179, 8, 0.5);
  background: rgba(254, 249, 195, 0.92);
}

.execution-mode-bar.mode-discouraged {
  border-color: rgba(239, 68, 68, 0.4);
  background: rgba(254, 242, 242, 0.92);
}

.mode-label {
  font-weight: 600;
  color: #0f172a;
}

.mode-stage {
  color: #475569;
}

.mode-scale {
  color: #1d4ed8;
  font-weight: 500;
}

.mode-warning {
  color: #b91c1c;
  font-weight: 500;
}

/* 保存按钮 - 根据 confirmationLevel 变色 */
.plan-save-button.save-confirm {
  border: 2px solid #eab308;
}

.plan-save-button.save-strong-confirm {
  background: linear-gradient(135deg, #ca8a04, #eab308);
  color: #fff;
}

.plan-save-button.save-discouraged {
  background: linear-gradient(135deg, #dc2626, #ef4444);
  color: #fff;
}
</style>