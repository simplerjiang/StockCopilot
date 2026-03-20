<script setup>
const props = defineProps({
  title: { type: String, default: '' },
  progressPercent: { type: Number, default: 0 },
  stages: { type: Array, default: () => [] },
  empty: { type: Boolean, default: false }
})
</script>

<template>
  <div class="source-progress-panel" :class="{ 'empty-progress-panel': props.empty }">
    <div class="source-progress-header">
      <strong>{{ props.title }}</strong>
      <span>{{ props.progressPercent }}%</span>
    </div>
    <div class="source-progress-track">
      <span :style="{ width: `${props.progressPercent}%` }"></span>
    </div>
    <div class="source-progress-list">
      <div
        v-for="stage in props.stages"
        :key="stage.key"
        class="source-progress-item"
        :class="`status-${stage.status}`"
      >
        <span>{{ stage.label }}</span>
        <small>{{ stage.message }}</small>
      </div>
    </div>
  </div>
</template>

<style scoped>
.source-progress-panel {
  display: grid;
  gap: 0.55rem;
  margin-top: 0.9rem;
  padding: 0.8rem 0.85rem;
  border-radius: 14px;
  background: rgba(148, 163, 184, 0.1);
  border: 1px solid rgba(148, 163, 184, 0.18);
}

.empty-progress-panel {
  margin-top: 1rem;
  width: min(520px, 100%);
}

.source-progress-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
}

.source-progress-header strong {
  color: #f8fafc;
  font-size: 0.92rem;
}

.source-progress-header span {
  color: #cbd5e1;
  font-size: 0.82rem;
}

.source-progress-track {
  position: relative;
  overflow: hidden;
  height: 8px;
  border-radius: 999px;
  background: rgba(15, 23, 42, 0.36);
}

.source-progress-track span {
  display: block;
  height: 100%;
  border-radius: inherit;
  background: linear-gradient(90deg, #38bdf8, #f59e0b);
  transition: width 0.22s ease;
}

.source-progress-list {
  display: grid;
  gap: 0.45rem;
}

.source-progress-item {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 0.75rem;
  font-size: 0.84rem;
}

.source-progress-item span {
  color: #f8fafc;
  font-weight: 600;
}

.source-progress-item small {
  color: #cbd5e1;
  text-align: right;
}

.source-progress-item.status-pending span,
.source-progress-item.status-pending small {
  color: #fde68a;
}

.source-progress-item.status-success span,
.source-progress-item.status-success small {
  color: #86efac;
}

.source-progress-item.status-error span,
.source-progress-item.status-error small {
  color: #fca5a5;
}
</style>