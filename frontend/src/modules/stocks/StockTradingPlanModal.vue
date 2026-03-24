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
</script>

<template>
  <div v-if="workspace?.planModalOpen && workspace?.planForm" class="plan-modal-backdrop" @click.self="$emit('close')">
    <section class="plan-modal" role="dialog" aria-modal="true" aria-label="交易计划草稿">
      <div class="search-modal-header">
        <div>
          <strong>交易计划草稿</strong>
          <p class="muted">后端基于 commander 历史生成，用户确认后才会入库。</p>
        </div>
        <button class="market-news-button" @click="$emit('close')">关闭</button>
      </div>

      <p v-if="workspace.planError" class="muted error">{{ workspace.planError }}</p>

      <section v-if="workspace.planForm.marketContext" class="plan-market-box">
        <strong>市场上下文</strong>
        <div class="plan-pill-row">
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
      </section>

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

      <div class="plan-modal-actions">
        <span class="muted">{{ workspace.planForm.id ? `编辑计划 #${workspace.planForm.id}` : `AnalysisHistory #${workspace.planForm.analysisHistoryId}` }}</span>
        <div class="plan-modal-buttons">
          <button class="market-news-button" @click="$emit('close')">取消</button>
          <button class="plan-save-button" @click="$emit('save')" :disabled="workspace.planSaving || workspace.planDraftLoading">
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
</style>