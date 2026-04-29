<script setup>
import { ref, computed, onMounted } from 'vue'

const macro = ref(null)
const loading = ref(true)

const fetchMacro = async () => {
  try {
    const res = await fetch('/api/macro/summary')
    if (res.ok) {
      const data = await res.json()
      if (data.available) macro.value = data
    }
  } catch {
    // silent fail
  } finally {
    loading.value = false
  }
}

onMounted(fetchMacro)

const policyColor = computed(() => {
  if (!macro.value) return '#94a3b8'
  switch (macro.value.policySignal) {
    case '偏宽松': return '#22c55e'
    case '偏紧缩': return '#ef4444'
    default: return '#f59e0b'
  }
})
</script>

<template>
  <div class="macro-card" v-if="!loading">
    <div v-if="macro" class="macro-content">
      <span class="macro-label">宏观环境</span>
      <span class="macro-policy" :style="{ color: policyColor }">
        {{ macro.policySignal }}
      </span>
      <span class="macro-divider">|</span>
      <span class="macro-item">
        存款利率 {{ macro.depositRate1Y != null ? macro.depositRate1Y + '%' : '-' }}
      </span>
      <span class="macro-divider">|</span>
      <span class="macro-item">
        M2同比 {{ macro.m2YoY != null ? macro.m2YoY + '%' : '-' }}
        <span v-if="macro.m2Trend" class="macro-trend">{{ macro.m2Trend }}</span>
      </span>
      <span v-if="macro.hasRecentChange" class="macro-alert">
        ⚡ 近期变动
      </span>
    </div>
    <div v-else class="macro-empty">
      <span class="macro-label">宏观环境</span>
      <span class="macro-na">数据采集中...</span>
    </div>
  </div>
</template>

<style scoped>
.macro-card {
  padding: 8px 16px;
  border-radius: 8px;
  background: var(--color-bg-surface, #1e293b);
  border: 1px solid var(--color-border-light, rgba(148, 163, 184, 0.15));
}
.macro-content {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  font-size: 13px;
  color: var(--color-text-secondary, #94a3b8);
}
.macro-label {
  font-weight: 600;
  color: var(--color-text-primary, #e2e8f0);
}
.macro-policy {
  font-weight: 700;
}
.macro-divider {
  opacity: 0.3;
}
.macro-item {
  white-space: nowrap;
}
.macro-trend {
  margin-left: 2px;
  font-size: 11px;
  opacity: 0.7;
}
.macro-alert {
  color: #f59e0b;
  font-size: 12px;
  font-weight: 600;
}
.macro-empty {
  display: flex;
  gap: 8px;
  font-size: 13px;
  color: var(--color-text-secondary, #64748b);
}
.macro-na {
  font-style: italic;
  opacity: 0.6;
}
</style>
