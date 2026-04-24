<template>
  <div v-if="status" class="rh-status-panel">
    <div class="rh-status-header">
      <span class="rh-status-title">📊 采集状态</span>
      <span class="rh-status-badge" :class="status.inWatchlist ? 'badge-auto' : 'badge-manual'">
        {{ status.inWatchlist ? '自动采集' : '按需采集' }}
      </span>
      <span class="rh-status-coverage">
        覆盖率: {{ status.coveragePercent }}% ({{ status.daysWithData }}/{{ status.totalTradingDays }}天)
      </span>
      <button
        class="rh-collect-btn"
        :disabled="collecting"
        @click="$emit('collect')"
      >
        {{ collecting ? '采集中...' : '立即采集' }}
      </button>
    </div>

    <div class="rh-status-platforms">
      <div v-for="p in status.platforms" :key="p.platform" class="rh-platform-row">
        <span class="rh-platform-icon" :class="'status-' + p.status">
          {{ p.status === 'ok' ? '✅' : p.status === 'stale' ? '⚠️' : '❌' }}
        </span>
        <span class="rh-platform-name">{{ p.platform }}</span>
        <span class="rh-platform-detail">
          <template v-if="p.lastDate">
            最后: {{ p.lastDate }} | 帖数: {{ p.lastPostCount.toLocaleString() }} | 记录: {{ p.totalRecords }}
          </template>
          <template v-else>
            无数据
          </template>
        </span>
      </div>
    </div>

    <div v-if="status.missingDates && status.missingDates.length > 0" class="rh-missing-dates">
      <span class="rh-missing-label">缺失日期 ({{ status.missingDates.length }}天):</span>
      <span class="rh-missing-list">{{ status.missingDates.slice(0, 10).join(', ') }}{{ status.missingDates.length > 10 ? '...' : '' }}</span>
    </div>

    <div v-if="collectResult && !collectResult.error" class="rh-collect-result">
      <span class="rh-result-label">采集结果:</span>
      <span v-for="r in collectResult.results" :key="r.platform" class="rh-result-item" :class="r.success ? 'success' : 'fail'">
        {{ r.platform }}: {{ r.success ? `✅ ${r.postCount?.toLocaleString()}` : `❌ ${r.error}` }}
      </span>
    </div>
  </div>
</template>

<script setup>
defineProps({
  status: { type: Object, default: null },
  collecting: { type: Boolean, default: false },
  collectResult: { type: Object, default: null }
})
defineEmits(['collect'])
</script>

<style scoped>
.rh-status-panel {
  background: var(--bg-secondary, #1a1a2e);
  border: 1px solid var(--border-color, #2a2a4a);
  border-radius: 6px;
  padding: 8px 12px;
  margin-top: 4px;
  font-size: 12px;
  color: var(--text-secondary, #a0a0b0);
}

.rh-status-header {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.rh-status-title {
  font-weight: 600;
  color: var(--text-primary, #e0e0e0);
}

.rh-status-badge {
  padding: 1px 6px;
  border-radius: 4px;
  font-size: 11px;
}

.badge-auto {
  background: #22c55e30;
  color: #22c55e;
}

.badge-manual {
  background: #f59e0b30;
  color: #f59e0b;
}

.rh-status-coverage {
  margin-left: auto;
  font-size: 11px;
}

.rh-collect-btn {
  padding: 2px 10px;
  border: 1px solid #f59e0b;
  border-radius: 4px;
  background: transparent;
  color: #f59e0b;
  cursor: pointer;
  font-size: 11px;
  transition: background 0.2s;
}

.rh-collect-btn:hover:not(:disabled) {
  background: #f59e0b20;
}

.rh-collect-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.rh-status-platforms {
  display: flex;
  gap: 12px;
  margin-top: 6px;
  flex-wrap: wrap;
}

.rh-platform-row {
  display: flex;
  align-items: center;
  gap: 4px;
}

.rh-platform-name {
  font-weight: 500;
  min-width: 70px;
}

.rh-platform-detail {
  font-size: 11px;
  color: var(--text-tertiary, #808090);
}

.rh-missing-dates {
  margin-top: 6px;
  font-size: 11px;
  color: #f87171;
}

.rh-missing-label {
  font-weight: 500;
  margin-right: 4px;
}

.rh-collect-result {
  margin-top: 6px;
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
  font-size: 11px;
}

.rh-result-label {
  font-weight: 500;
}

.rh-result-item.success {
  color: #22c55e;
}

.rh-result-item.fail {
  color: #f87171;
}
</style>
