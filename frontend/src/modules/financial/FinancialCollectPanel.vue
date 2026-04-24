<script setup>
import { ref } from 'vue'

const emit = defineEmits(['collect-success'])

const symbol = ref('')
const source = ref('all')
const startDate = ref('')
const collecting = ref(false)
const result = ref(null)  // { success: boolean, data?: any, error?: string, elapsed?: number }

async function startCollect() {
  const sym = symbol.value.trim()
  if (!sym) return
  
  collecting.value = true
  result.value = null
  const t0 = Date.now()
  
  try {
    const params = new URLSearchParams()
    if (source.value !== 'all') params.set('source', source.value)
    if (startDate.value) params.set('startDate', startDate.value)
    
    const url = `/api/stocks/financial/collect/${encodeURIComponent(sym)}${params.toString() ? '?' + params : ''}`
    const res = await fetch(url, { method: 'POST' })
    const elapsed = Date.now() - t0
    
    if (res.ok) {
      const data = await res.json().catch(() => ({}))
      result.value = { success: true, data, elapsed }
      emit('collect-success', { symbol: sym })
    } else {
      const text = await res.text().catch(() => '')
      result.value = { success: false, error: `HTTP ${res.status}: ${text.substring(0, 200)}`, elapsed }
    }
  } catch (e) {
    result.value = { success: false, error: e.message, elapsed: Date.now() - t0 }
  } finally {
    collecting.value = false
  }
}
</script>

<template>
  <div class="fc-collect-panel">
    <div class="collect-input-row">
      <label class="collect-field">
        <span>股票代码</span>
        <input v-model="symbol" type="text" placeholder="输入代码如 600519" @keyup.enter="startCollect" :disabled="collecting" />
      </label>
      <label class="collect-field">
        <span>数据源</span>
        <select v-model="source" :disabled="collecting">
          <option value="all">全部</option>
          <option value="ths">同花顺</option>
          <option value="pdf">PDF</option>
        </select>
      </label>
      <label class="collect-field">
        <span>起始日期</span>
        <input v-model="startDate" type="date" :disabled="collecting" />
      </label>
      <button class="fc-btn fc-btn--primary" @click="startCollect" :disabled="collecting || !symbol.trim()">
        {{ collecting ? '采集中...' : '开始采集' }}
      </button>
    </div>

    <!-- Progress -->
    <div v-if="collecting" class="collect-progress">
      <span class="pulse-dot"></span>
      <span>正在采集 {{ symbol }} ...</span>
      <div class="progress-bar"><div class="progress-bar-indeterminate"></div></div>
    </div>

    <!-- Success -->
    <div v-if="result?.success" class="collect-result collect-result--success">
      <div class="result-header">
        <span>✅ 采集成功</span>
        <span v-if="result.elapsed" class="result-elapsed">耗时 {{ result.elapsed }}ms</span>
      </div>
      <div v-if="result.data" class="result-detail">
        <span v-if="result.data.reportCount">报表: {{ result.data.reportCount }} 条</span>
        <span v-if="result.data.message">{{ result.data.message }}</span>
      </div>
    </div>

    <!-- Error -->
    <div v-if="result && !result.success" class="collect-result collect-result--error">
      <div class="result-header">
        <span>❌ 采集失败</span>
        <button class="fc-btn fc-btn--ghost fc-btn--sm" @click="startCollect">重试</button>
      </div>
      <div class="result-detail">{{ result.error }}</div>
    </div>
  </div>
</template>

<style scoped>
.fc-collect-panel {
  background: var(--color-bg-surface, #1e2a3a);
  border: 1px solid var(--color-border-light, #2a3a4a);
  border-radius: var(--radius-md, 6px);
  padding: 16px 20px;
}

.collect-input-row {
  display: flex;
  gap: 12px;
  align-items: flex-end;
  flex-wrap: wrap;
}

.collect-field {
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: var(--text-xs, 12px);
  color: var(--color-text-secondary, #91a6bc);
}

.collect-field input,
.collect-field select {
  padding: 6px 10px;
  background: var(--color-bg-body, #152030);
  border: 1px solid var(--color-border-medium, #3a4a5a);
  border-radius: var(--radius-sm, 4px);
  color: var(--color-text-primary, #e2e8f0);
  font-size: var(--text-sm, 13px);
  height: 32px;
}

.collect-field input[type="text"] { min-width: 160px; }
.collect-field select { min-width: 120px; }
.collect-field input[type="date"] { min-width: 150px; }
.collect-field input[type="date"]::-webkit-calendar-picker-indicator { filter: invert(0.8); cursor: pointer; }

.fc-btn { padding: 6px 16px; border: none; border-radius: var(--radius-md, 4px); cursor: pointer; font-size: var(--text-sm, 13px); height: 32px; }
.fc-btn--primary { background: var(--color-accent, #3b82f6); color: var(--color-text-on-dark, #fff); }
.fc-btn--primary:disabled { opacity: 0.5; cursor: not-allowed; }
.fc-btn--ghost { background: transparent; color: var(--color-text-secondary, #91a6bc); border: 1px solid var(--color-border-medium, #3a4a5a); }
.fc-btn--sm { padding: 3px 10px; font-size: var(--text-xs, 12px); height: auto; }

.collect-progress {
  margin-top: 12px;
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--color-text-secondary, #91a6bc);
  font-size: var(--text-sm, 13px);
}

.pulse-dot {
  width: 8px; height: 8px; border-radius: 50%; background: var(--color-accent, #3b82f6);
  animation: pulse 1.2s infinite;
}

@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.3; }
}

.progress-bar {
  flex: 1; height: 4px; background: var(--color-border-light, #2a3a4a); border-radius: 2px; overflow: hidden;
}

.progress-bar-indeterminate {
  width: 30%; height: 100%; background: var(--color-accent, #3b82f6); border-radius: 2px;
  animation: indeterminate 1.5s infinite;
}

@keyframes indeterminate {
  0% { transform: translateX(-100%); }
  100% { transform: translateX(400%); }
}

.collect-result {
  margin-top: 12px;
  padding: 10px 14px;
  border-radius: var(--radius-sm, 4px);
  font-size: var(--text-sm, 13px);
}

.collect-result--success { background: rgba(46, 204, 113, 0.08); border: 1px solid rgba(46, 204, 113, 0.2); }
.collect-result--error { background: rgba(231, 76, 60, 0.08); border: 1px solid rgba(231, 76, 60, 0.2); }

.result-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.result-elapsed { color: var(--color-text-secondary, #91a6bc); font-size: var(--text-xs, 12px); }

.result-detail {
  margin-top: 6px;
  color: var(--color-text-secondary, #91a6bc);
  font-size: var(--text-xs, 12px);
}

.collect-result--success .result-header { color: #2ecc71; }
.collect-result--error .result-header { color: #e74c3c; }
.collect-result--error .result-detail { color: #e74c3c; }
</style>
