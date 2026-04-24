<script setup>
import { onMounted, ref, computed } from 'vue'
import { fetchFinancialConfig, updateFinancialConfig, collectPdfFiles } from './financialApi.js'

/* ── 状态 ── */
const loading = ref(false)
const saving = ref(false)
const error = ref('')
const saveMsg = ref('')

const config = ref({
  enabled: true,
  scope: 'Watchlist',
  startDate: '',
  frequency: 'Daily',
  watchlistSymbols: [],
  updatedAt: ''
})

/* ── Watchlist 输入 ── */
const newSymbol = ref('')
const symbolError = ref('')
const symbolPattern = /^\d{6}$/

function validateSymbol(val) {
  if (!val) return ''
  if (!symbolPattern.test(val)) return '请输入6位数字股票代码'
  if (config.value.watchlistSymbols.includes(val)) return '该代码已存在'
  return ''
}

function addSymbol() {
  const sym = newSymbol.value.trim()
  const err = validateSymbol(sym)
  if (err) { symbolError.value = err; return }
  config.value.watchlistSymbols.push(sym)
  newSymbol.value = ''
  symbolError.value = ''
}

function removeSymbol(idx) {
  config.value.watchlistSymbols.splice(idx, 1)
}

/* ── 手动收集 ── */
const collectingSymbol = ref('')
const collectResult = ref(null) // { symbol, success, message }

async function triggerCollect(symbol) {
  collectingSymbol.value = symbol
  collectResult.value = null
  try {
    const res = await collectPdfFiles(symbol)
    collectResult.value = { symbol, success: true, message: res.message || '触发成功' }
  } catch (e) {
    collectResult.value = { symbol, success: false, message: e.message }
  } finally {
    collectingSymbol.value = ''
  }
}

/* ── 加载 ── */
async function loadConfig() {
  loading.value = true
  error.value = ''
  try {
    const data = await fetchFinancialConfig()
    config.value = {
      enabled: data.enabled ?? true,
      scope: data.scope || 'Watchlist',
      startDate: data.startDate || '',
      frequency: data.frequency || 'Daily',
      watchlistSymbols: Array.isArray(data.watchlistSymbols) ? [...data.watchlistSymbols] : [],
      updatedAt: data.updatedAt || ''
    }
  } catch (e) {
    error.value = e.message
  } finally {
    loading.value = false
  }
}

/* ── 保存 ── */
async function saveConfig() {
  saving.value = true
  saveMsg.value = ''
  try {
    await updateFinancialConfig(config.value)
    saveMsg.value = '保存成功'
    setTimeout(() => { saveMsg.value = '' }, 3000)
  } catch (e) {
    saveMsg.value = '保存失败: ' + e.message
  } finally {
    saving.value = false
  }
}

const formattedUpdatedAt = computed(() => {
  if (!config.value.updatedAt) return '—'
  try {
    return new Date(config.value.updatedAt).toLocaleString('zh-CN')
  } catch { return config.value.updatedAt }
})

onMounted(loadConfig)
</script>

<template>
  <div class="config-page">
    <h2 class="page-title">财报采集设置</h2>

    <!-- Loading -->
    <div v-if="loading" class="status-msg">加载中...</div>

    <!-- Error -->
    <div v-else-if="error" class="status-msg status-msg--error">
      <span>{{ error }}</span>
      <button class="fc-btn fc-btn--ghost fc-btn--sm" @click="loadConfig">重试</button>
    </div>

    <template v-else>
      <!-- 基础设置 -->
      <section class="config-card">
        <h3 class="card-title">基础设置</h3>
        <div class="setting-row">
          <label class="setting-label">启用自动收集</label>
          <label class="toggle-switch">
            <input type="checkbox" v-model="config.enabled" id="toggle-enabled" aria-label="启用自动收集" />
            <span class="toggle-slider"></span>
          </label>
          <span class="setting-hint">{{ config.enabled ? '已启用' : '已禁用' }}</span>
        </div>
        <div class="setting-row">
          <label class="setting-label">采集频率</label>
          <select v-model="config.frequency" class="setting-select">
            <option value="Daily">每日</option>
            <option value="Weekly">每周</option>
            <option value="Manual">手动</option>
          </select>
        </div>
        <div class="setting-row">
          <label class="setting-label">采集范围</label>
          <select v-model="config.scope" class="setting-select">
            <option value="Watchlist">Watchlist</option>
            <option value="All">全部</option>
          </select>
        </div>
        <div class="setting-row">
          <label class="setting-label">最后更新</label>
          <span class="setting-value">{{ formattedUpdatedAt }}</span>
        </div>
      </section>

      <!-- Watchlist 管理 -->
      <section class="config-card">
        <h3 class="card-title">Watchlist 管理</h3>
        <div class="watchlist-add-row">
          <input
            v-model="newSymbol"
            type="text"
            class="watchlist-input"
            placeholder="输入6位股票代码，如 600519"
            maxlength="6"
            @input="symbolError = ''"
            @keyup.enter="addSymbol"
          />
          <button class="fc-btn fc-btn--primary" @click="addSymbol" :disabled="!newSymbol.trim()">添加</button>
        </div>
        <div v-if="symbolError" class="field-error">{{ symbolError }}</div>

        <div v-if="config.watchlistSymbols.length === 0" class="empty-hint">
          暂无 Watchlist 股票
        </div>
        <ul v-else class="watchlist-list">
          <li v-for="(sym, idx) in config.watchlistSymbols" :key="sym" class="watchlist-item">
            <span class="symbol-code">{{ sym }}</span>
            <div class="watchlist-actions">
              <button
                class="fc-btn fc-btn--ghost fc-btn--sm"
                :disabled="collectingSymbol === sym"
                @click="triggerCollect(sym)"
              >
                {{ collectingSymbol === sym ? '采集中...' : '采集' }}
              </button>
              <button class="fc-btn fc-btn--danger fc-btn--sm" @click="removeSymbol(idx)">删除</button>
            </div>
          </li>
        </ul>

        <!-- 收集结果 -->
        <div v-if="collectResult" class="collect-feedback" :class="collectResult.success ? 'collect-feedback--ok' : 'collect-feedback--err'">
          {{ collectResult.symbol }}: {{ collectResult.message }}
        </div>
      </section>

      <!-- 操作栏 -->
      <div class="action-bar">
        <button class="fc-btn fc-btn--primary" @click="saveConfig" :disabled="saving">
          {{ saving ? '保存中...' : '保存配置' }}
        </button>
        <span v-if="saveMsg" class="save-msg" :class="{ 'save-msg--ok': saveMsg === '保存成功' }">{{ saveMsg }}</span>
      </div>
    </template>
  </div>
</template>

<style scoped>
.config-page {
  padding: 20px 24px;
  max-width: 720px;
}

.page-title {
  font-size: var(--text-lg, 16px);
  font-weight: 600;
  color: var(--color-text-primary, #e2e8f0);
  margin: 0 0 16px;
}

/* ── Status ── */
.status-msg {
  color: var(--color-text-secondary, #91a6bc);
  font-size: var(--text-sm, 13px);
  display: flex;
  align-items: center;
  gap: 8px;
}
.status-msg--error { color: var(--color-danger, #dc2626); }

/* ── Card ── */
.config-card {
  background: var(--color-bg-surface, #1e2a3a);
  border: 1px solid var(--color-border-light, #2a3a4a);
  border-radius: var(--radius-md, 6px);
  padding: 16px 20px;
  margin-bottom: 16px;
}

.card-title {
  font-size: var(--text-base, 14px);
  font-weight: 600;
  color: var(--color-text-primary, #e2e8f0);
  margin: 0 0 12px;
}

/* ── Settings Row ── */
.setting-row {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 6px 0;
}

.setting-label {
  font-size: var(--text-sm, 13px);
  color: var(--color-text-secondary, #91a6bc);
  min-width: 110px;
}

.setting-select {
  padding: 5px 10px;
  background: var(--color-bg-body, #152030);
  border: 1px solid var(--color-border-medium, #3a4a5a);
  border-radius: var(--radius-sm, 4px);
  color: var(--color-text-primary, #e2e8f0);
  font-size: var(--text-sm, 13px);
  height: 32px;
}

.setting-value {
  font-size: var(--text-sm, 13px);
  color: var(--color-text-primary, #e2e8f0);
}

.setting-hint {
  font-size: var(--text-xs, 12px);
  color: var(--color-text-secondary, #91a6bc);
}

/* ── Toggle Switch ── */
.toggle-switch {
  position: relative;
  display: inline-block;
  width: 36px;
  height: 20px;
}
.toggle-switch input { opacity: 0; width: 0; height: 0; }
.toggle-slider {
  position: absolute;
  inset: 0;
  background: var(--color-border-medium, #3a4a5a);
  border-radius: 10px;
  cursor: pointer;
  transition: background 0.2s;
}
.toggle-slider::before {
  content: '';
  position: absolute;
  width: 16px;
  height: 16px;
  left: 2px;
  top: 2px;
  background: #fff;
  border-radius: 50%;
  transition: transform 0.2s;
}
.toggle-switch input:checked + .toggle-slider { background: var(--color-accent, #4f46e5); }
.toggle-switch input:checked + .toggle-slider::before { transform: translateX(16px); }

/* ── Watchlist ── */
.watchlist-add-row {
  display: flex;
  gap: 8px;
  align-items: center;
  margin-bottom: 8px;
}

.watchlist-input {
  flex: 1;
  max-width: 260px;
  padding: 6px 10px;
  background: var(--color-bg-body, #152030);
  border: 1px solid var(--color-border-medium, #3a4a5a);
  border-radius: var(--radius-sm, 4px);
  color: var(--color-text-primary, #e2e8f0);
  font-size: var(--text-sm, 13px);
  height: 32px;
}

.field-error {
  font-size: var(--text-xs, 12px);
  color: var(--color-danger, #dc2626);
  margin-bottom: 6px;
}

.empty-hint {
  font-size: var(--text-sm, 13px);
  color: var(--color-text-secondary, #91a6bc);
  padding: 8px 0;
}

.watchlist-list {
  list-style: none;
  margin: 0;
  padding: 0;
}

.watchlist-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 6px 8px;
  border-bottom: 1px solid var(--color-border-light, #2a3a4a);
}
.watchlist-item:last-child { border-bottom: none; }

.symbol-code {
  font-size: var(--text-sm, 13px);
  font-weight: 500;
  color: var(--color-text-primary, #e2e8f0);
  font-family: 'Consolas', 'Monaco', monospace;
}

.watchlist-actions {
  display: flex;
  gap: 6px;
}

/* ── Buttons ── */
.fc-btn {
  padding: 6px 16px;
  border: none;
  border-radius: var(--radius-md, 4px);
  cursor: pointer;
  font-size: var(--text-sm, 13px);
  height: 32px;
  white-space: nowrap;
}
.fc-btn--primary { background: var(--color-accent, #4f46e5); color: var(--color-text-on-dark, #fff); }
.fc-btn--primary:hover { filter: brightness(1.1); }
.fc-btn--primary:disabled { opacity: 0.5; cursor: not-allowed; }
.fc-btn--ghost { background: transparent; color: var(--color-text-secondary, #91a6bc); border: 1px solid var(--color-border-medium, #3a4a5a); }
.fc-btn--ghost:hover { background: rgba(255,255,255,0.04); }
.fc-btn--danger { background: transparent; color: var(--color-danger, #dc2626); border: 1px solid var(--color-danger-border, rgba(220,38,38,0.20)); }
.fc-btn--danger:hover { background: var(--color-danger-bg, rgba(220,38,38,0.07)); }
.fc-btn--sm { padding: 3px 10px; font-size: var(--text-xs, 12px); height: auto; }

/* ── Collect Feedback ── */
.collect-feedback {
  margin-top: 8px;
  padding: 6px 10px;
  border-radius: var(--radius-sm, 4px);
  font-size: var(--text-xs, 12px);
}
.collect-feedback--ok { background: var(--color-success-bg, rgba(5,150,105,0.07)); border: 1px solid var(--color-success-border, rgba(5,150,105,0.20)); color: var(--color-success, #059669); }
.collect-feedback--err { background: var(--color-danger-bg, rgba(220,38,38,0.07)); border: 1px solid var(--color-danger-border, rgba(220,38,38,0.20)); color: var(--color-danger, #dc2626); }

/* ── Action Bar ── */
.action-bar {
  display: flex;
  align-items: center;
  gap: 12px;
  padding-top: 4px;
}

.save-msg {
  font-size: var(--text-xs, 12px);
  color: var(--color-danger, #dc2626);
}
.save-msg--ok { color: var(--color-success, #059669); }
</style>
