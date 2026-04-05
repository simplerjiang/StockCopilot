<script setup>
import { ref, onMounted, computed } from 'vue'

// Auth token (same as other admin panels)
const token = ref(localStorage.getItem('admin-token') || '')
const isAuthed = ref(false)

// Config state
const config = ref({
  enabled: false,
  scope: 'Watchlist',
  frequency: 'Daily',
  startDate: '',
  watchlistSymbols: []
})
const configLoading = ref(false)
const configMsg = ref('')

// Test collection state
const testSymbol = ref('')
const collecting = ref(false)
const collectResult = ref(null)
const collectError = ref('')

// Logs state
const logs = ref([])
const logsLoading = ref(false)

// Worker health
const workerHealthy = ref(null)

const authHeaders = computed(() => ({
  'Authorization': `Bearer ${token.value}`,
  'Content-Type': 'application/json'
}))

// ---- Helpers ----
function formatTime(ts) {
  if (!ts) return '-'
  try {
    const d = new Date(ts)
    return d.toLocaleString('zh-CN', { hour12: false })
  } catch {
    return ts
  }
}

// ---- Auth ----
async function login() {
  try {
    const res = await fetch('/api/admin/verify-token', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ token: token.value })
    })
    if (res.ok) {
      isAuthed.value = true
      localStorage.setItem('admin-token', token.value)
      await loadAll()
    } else {
      isAuthed.value = false
      configMsg.value = '认证失败'
    }
  } catch {
    isAuthed.value = false
    configMsg.value = '认证请求失败'
  }
}

// ---- Config ----
async function loadConfig() {
  configLoading.value = true
  try {
    const res = await fetch('/api/stocks/financial/config', { headers: authHeaders.value })
    if (res.ok) {
      const data = await res.json()
      config.value = data
    }
  } catch (e) {
    configMsg.value = '加载配置失败: ' + e.message
  } finally {
    configLoading.value = false
  }
}

async function saveConfig() {
  configMsg.value = ''
  try {
    const res = await fetch('/api/stocks/financial/config', {
      method: 'PUT',
      headers: authHeaders.value,
      body: JSON.stringify(config.value)
    })
    configMsg.value = res.ok ? '配置已保存' : '保存失败'
  } catch (e) {
    configMsg.value = '保存失败: ' + e.message
  }
}

// ---- Test Collection ----
async function testCollect() {
  if (!testSymbol.value.trim()) return
  collecting.value = true
  collectResult.value = null
  collectError.value = ''
  try {
    const res = await fetch(`/api/stocks/financial/collect/${testSymbol.value.trim()}`, {
      method: 'POST',
      headers: authHeaders.value
    })
    if (res.ok) {
      collectResult.value = await res.json()
    } else {
      const text = await res.text()
      collectError.value = `采集失败 (${res.status}): ${text}`
    }
  } catch (e) {
    collectError.value = '采集请求失败: ' + e.message
  } finally {
    collecting.value = false
  }
}

// ---- Logs ----
async function loadLogs() {
  logsLoading.value = true
  try {
    const res = await fetch('/api/stocks/financial/logs?limit=50', { headers: authHeaders.value })
    if (res.ok) {
      logs.value = await res.json()
    }
  } catch (e) {
    console.error('Load logs error:', e)
  } finally {
    logsLoading.value = false
  }
}

// ---- Worker Health ----
async function checkWorkerHealth() {
  try {
    const res = await fetch('http://localhost:5120/health', { signal: AbortSignal.timeout(3000) })
    workerHealthy.value = res.ok
  } catch {
    workerHealthy.value = false
  }
}

async function loadAll() {
  await Promise.all([loadConfig(), loadLogs(), checkWorkerHealth()])
}

onMounted(() => {
  isAuthed.value = true
  loadAll()
})
</script>

<template>
  <div class="financial-data-panel">
      <!-- Worker Status -->
      <div class="status-bar">
        <span class="status-dot" :class="workerHealthy === true ? 'healthy' : workerHealthy === false ? 'unhealthy' : 'unknown'"></span>
        <span>Worker 状态: {{ workerHealthy === true ? '运行中 (5120)' : workerHealthy === false ? '未启动' : '检测中...' }}</span>
        <button class="btn-small" @click="checkWorkerHealth">刷新</button>
      </div>

      <!-- Config Section -->
      <div class="panel-section">
        <h3>采集配置</h3>
        <div class="form-grid">
          <label>
            <input type="checkbox" v-model="config.enabled" />
            启用自动采集
          </label>
          <label>频率:
            <select v-model="config.frequency">
              <option value="Daily">每日</option>
              <option value="Weekly">每周</option>
              <option value="Manual">仅手动</option>
            </select>
          </label>
          <label>范围:
            <select v-model="config.scope">
              <option value="Watchlist">自选股</option>
              <option value="All">全部</option>
            </select>
          </label>
          <label>起始日期:
            <input type="date" v-model="config.startDate" />
          </label>
        </div>
        <div class="form-actions">
          <button @click="saveConfig" :disabled="configLoading">保存配置</button>
          <span v-if="configMsg" class="msg" :class="configMsg.includes('失败') ? 'error' : 'success'">{{ configMsg }}</span>
        </div>
      </div>

      <!-- Test Collection Section -->
      <div class="panel-section">
        <h3>手动测试采集</h3>
        <div class="test-row">
          <input v-model="testSymbol" placeholder="输入股票代码 (如 600519)" @keyup.enter="testCollect" />
          <button @click="testCollect" :disabled="collecting || !testSymbol.trim()">
            {{ collecting ? '采集中...' : '开始采集' }}
          </button>
        </div>
        <div v-if="collectResult" class="collect-result">
          <div class="result-header" :class="collectResult.success ? 'success' : 'error'">
            {{ collectResult.success ? '✅ 采集成功' : '❌ 采集失败' }}
            <span v-if="collectResult.channel"> | 渠道: {{ collectResult.channel }}</span>
            <span v-if="collectResult.durationMs"> | 耗时: {{ collectResult.durationMs }}ms</span>
          </div>
          <div class="result-detail" v-if="collectResult.success">
            报表: {{ collectResult.reportCount ?? 0 }} 条 |
            指标: {{ collectResult.indicatorCount ?? 0 }} 条 |
            分红: {{ collectResult.dividendCount ?? 0 }} 条 |
            融资融券: {{ collectResult.marginTradingCount ?? 0 }} 条
          </div>
          <div class="result-detail error" v-if="collectResult.isDegraded">
            ⚠️ 降级: {{ collectResult.degradeReason }}
          </div>
          <div class="result-detail error" v-if="collectResult.errorMessage">
            {{ collectResult.errorMessage }}
          </div>
        </div>
        <div v-if="collectError" class="msg error">{{ collectError }}</div>
      </div>

      <!-- Logs Section -->
      <div class="panel-section">
        <h3>
          采集日志
          <button class="btn-small" @click="loadLogs" :disabled="logsLoading">刷新</button>
        </h3>
        <div class="logs-container">
          <table v-if="logs.length > 0">
            <thead>
              <tr>
                <th>时间</th>
                <th>股票</th>
                <th>渠道</th>
                <th>结果</th>
                <th>记录数</th>
                <th>耗时</th>
                <th>备注</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="log in logs" :key="log.id">
                <td>{{ formatTime(log.timestamp) }}</td>
                <td>{{ log.symbol }}</td>
                <td>{{ log.channel }}</td>
                <td :class="log.success ? 'text-success' : 'text-error'">
                  {{ log.success ? '成功' : '失败' }}
                </td>
                <td>{{ log.recordCount ?? '-' }}</td>
                <td>{{ log.durationMs ? log.durationMs + 'ms' : '-' }}</td>
                <td class="log-note">{{ log.isDegraded ? '降级: ' + log.degradeReason : log.errorMessage || '' }}</td>
              </tr>
            </tbody>
          </table>
          <div v-else class="empty-logs">暂无采集日志</div>
        </div>
      </div>
  </div>
</template>


<style scoped>
.financial-data-panel {
  padding: 16px;
  color: #e0e0e0;
  font-size: 13px;
  max-width: 900px;
}

.auth-section { text-align: center; padding: 40px 0; }
.auth-row { display: flex; gap: 8px; justify-content: center; margin-top: 12px; }
.auth-row input { padding: 6px 12px; background: #1e2a3a; border: 1px solid #3a4a5a; color: #fff; border-radius: 4px; width: 260px; }

h3 { font-size: 15px; margin: 0 0 10px; color: #ccc; }

.status-bar {
  display: flex; align-items: center; gap: 8px;
  padding: 8px 12px; background: #1a2535; border-radius: 6px; margin-bottom: 16px;
}
.status-dot { width: 8px; height: 8px; border-radius: 50%; display: inline-block; }
.status-dot.healthy { background: #2ecc71; }
.status-dot.unhealthy { background: #e74c3c; }
.status-dot.unknown { background: #888; }

.panel-section {
  background: #1e2a3a; border-radius: 6px; padding: 14px; margin-bottom: 14px;
  border: 1px solid #2a3a4a;
}

.form-grid { display: flex; flex-wrap: wrap; gap: 12px 24px; margin-bottom: 10px; }
.form-grid label { display: flex; align-items: center; gap: 6px; color: #bbb; font-size: 12px; }
.form-grid select, .form-grid input[type="date"] {
  padding: 3px 8px; background: #152030; border: 1px solid #3a4a5a; color: #fff; border-radius: 3px;
}

.form-actions { display: flex; align-items: center; gap: 10px; }

button {
  padding: 5px 14px; background: #2a6ccf; color: #fff; border: none; border-radius: 4px;
  cursor: pointer; font-size: 12px;
}
button:hover { background: #3578df; }
button:disabled { opacity: 0.5; cursor: not-allowed; }

.btn-small { padding: 2px 8px; font-size: 11px; background: #3a4a5a; }

.test-row { display: flex; gap: 8px; margin-bottom: 10px; }
.test-row input {
  flex: 1; padding: 6px 10px; background: #152030; border: 1px solid #3a4a5a;
  color: #fff; border-radius: 4px;
}

.collect-result { padding: 10px; background: #152030; border-radius: 4px; margin-top: 8px; }
.result-header { font-weight: bold; margin-bottom: 6px; }
.result-header.success { color: #2ecc71; }
.result-header.error { color: #e74c3c; }
.result-detail { font-size: 12px; color: #aaa; margin-top: 4px; }

.msg { font-size: 12px; margin-top: 6px; }
.msg.success { color: #2ecc71; }
.msg.error { color: #e74c3c; }
.text-success { color: #2ecc71; }
.text-error { color: #e74c3c; }

.logs-container { overflow-x: auto; max-height: 400px; overflow-y: auto; }
table { width: 100%; border-collapse: collapse; font-size: 12px; }
th, td { padding: 5px 8px; border-bottom: 1px solid #2a3a4a; text-align: left; white-space: nowrap; }
th { color: #999; background: #1a2535; position: sticky; top: 0; }
.log-note { max-width: 200px; overflow: hidden; text-overflow: ellipsis; color: #888; }

.empty-logs { text-align: center; padding: 20px; color: #666; }
</style>
