<script setup>
import { ref, onMounted, computed } from 'vue'
import { getSourceChannelTag, sourceChannelTagStyle } from '../financial/sourceChannelTag.js'

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
const pdfSummaryOpen = ref(false)

// Logs state
const logs = ref([])
const logsLoading = ref(false)

// Worker health
const workerHealthy = ref(null)
const workerStatusText = ref('检测中...')
const workerStatusDetail = ref('')

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

async function readResponsePayload(res) {
  const contentType = res.headers.get('content-type') || ''
  if (contentType.includes('application/json')) {
    return await res.json().catch(() => null)
  }

  const text = await res.text().catch(() => '')
  if (!text) return null

  try {
    return JSON.parse(text)
  } catch {
    return { message: text }
  }
}

function buildErrorMessage(prefix, status, payload) {
  const message = payload?.error || payload?.message || prefix
  const detail = payload?.detail ? ` | ${payload.detail}` : ''
  return `${prefix} (${status}): ${message}${detail}`
}

function normalizeDateInput(value) {
  if (!value || typeof value !== 'string') return ''

  const matched = value.match(/^\d{4}-\d{2}-\d{2}/)
  if (matched) return matched[0]

  const parsed = new Date(value)
  if (Number.isNaN(parsed.getTime())) return ''

  return parsed.toISOString().slice(0, 10)
}

function formatLogNote(log) {
  if (log?.isDegraded && log?.degradeReason) {
    return `降级: ${log.degradeReason}`
  }

  return log?.errorMessage || '-'
}

// ---- V040-S4 采集结果透明化字段 ----
function pickCollectField(result, ...keys) {
  if (!result || typeof result !== 'object') return ''
  for (const k of keys) {
    const v = result[k]
    if (v != null && v !== '') return v
  }
  return ''
}

function firstOfArrayField(result, ...keys) {
  if (!result || typeof result !== 'object') return ''
  for (const k of keys) {
    const v = result[k]
    if (Array.isArray(v) && v.length > 0) {
      const first = v[0]
      if (first != null && first !== '') return String(first)
    }
  }
  return ''
}

const collectReportPeriod = computed(() => {
  const r = collectResult.value
  return pickCollectField(r, 'reportPeriod') || firstOfArrayField(r, 'reportPeriods')
})

const collectReportTitle = computed(() => {
  const r = collectResult.value
  return pickCollectField(r, 'reportTitle') || firstOfArrayField(r, 'reportTitles')
})

const collectSourceChannel = computed(() => {
  const r = collectResult.value
  const v = pickCollectField(r, 'sourceChannel', 'mainSourceChannel', 'channel', 'Channel')
  return v ? String(v) : ''
})

const collectFallbackReason = computed(() => {
  const r = collectResult.value
  const v = pickCollectField(r, 'fallbackReason', 'degradeReason', 'DegradeReason')
  return v ? String(v) : ''
})

const collectPdfSummary = computed(() => {
  const r = collectResult.value
  const v = pickCollectField(r, 'pdfSummary', 'pdfSummarySupplement', 'PdfSummarySupplement')
  return v ? String(v) : ''
})

const collectChannelTag = computed(() => getSourceChannelTag(collectSourceChannel.value))

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
      config.value = {
        ...data,
        startDate: normalizeDateInput(data?.startDate)
      }
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
    if (res.ok) {
      configMsg.value = '配置已保存'
    } else {
      const payload = await readResponsePayload(res)
      configMsg.value = buildErrorMessage('保存失败', res.status, payload)
    }
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
  pdfSummaryOpen.value = false
  try {
    const res = await fetch(`/api/stocks/financial/collect/${testSymbol.value.trim()}`, {
      method: 'POST',
      headers: authHeaders.value
    })
    if (res.ok) {
      collectResult.value = await res.json()
      if (collectResult.value?.success) {
        await loadLogs()
      }
    } else {
      const payload = await readResponsePayload(res)
      collectError.value = buildErrorMessage('采集失败', res.status, payload)
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
    const res = await fetch('/api/stocks/financial/worker/status', {
      headers: authHeaders.value,
      signal: AbortSignal.timeout(3000)
    })
    const payload = await readResponsePayload(res)

    if (res.ok && payload?.reachable !== false) {
      workerHealthy.value = true
      workerStatusText.value = '运行中'
      workerStatusDetail.value = payload?.baseUrl ? `经主 API 代理 -> ${payload.baseUrl}` : ''
      return
    }

    workerHealthy.value = false
    workerStatusText.value = payload?.status === 'timeout' ? '连接超时' : '未启动'
    workerStatusDetail.value = [payload?.error, payload?.detail].filter(Boolean).join(' | ') || `状态码 ${res.status}`
  } catch (e) {
    workerHealthy.value = false
    workerStatusText.value = '检测失败'
    workerStatusDetail.value = e.message || ''
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
        <span>Worker 状态: {{ workerStatusText }}</span>
        <span v-if="workerStatusDetail" class="status-detail">{{ workerStatusDetail }}</span>
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
            <span v-if="collectResult.channel"> | 主渠道: {{ collectResult.channel }}</span>
            <span v-if="collectResult.durationMs"> | 耗时: {{ collectResult.durationMs }}ms</span>
          </div>
          <div class="result-detail" v-if="collectResult.success">
            报表: {{ collectResult.reportCount ?? 0 }} 条 |
            指标: {{ collectResult.indicatorCount ?? 0 }} 条 |
            分红: {{ collectResult.dividendCount ?? 0 }} 条 |
            融资融券: {{ collectResult.marginTradingCount ?? 0 }} 条
          </div>
          <!-- V040-S4 采集结果透明化新字段 -->
          <dl class="collect-meta">
            <div v-if="collectReportPeriod" class="collect-meta-row" data-field="reportPeriod">
              <dt>报告期</dt>
              <dd>{{ collectReportPeriod }}</dd>
            </div>
            <div v-if="collectReportTitle" class="collect-meta-row" data-field="reportTitle">
              <dt>报告标题</dt>
              <dd>{{ collectReportTitle }}</dd>
            </div>
            <div v-if="collectSourceChannel" class="collect-meta-row" data-field="sourceChannel">
              <dt>来源渠道</dt>
              <dd>
                <span
                  class="source-channel-tag"
                  :data-channel-key="collectChannelTag.key"
                  :style="sourceChannelTagStyle(collectChannelTag)"
                >{{ collectChannelTag.label }}</span>
              </dd>
            </div>
            <div v-if="collectFallbackReason" class="collect-meta-row" data-field="fallbackReason">
              <dt>降级原因</dt>
              <dd class="collect-meta-fallback">{{ collectFallbackReason }}</dd>
            </div>
            <div v-if="collectPdfSummary" class="collect-meta-row" data-field="pdfSummary">
              <dt>PDF 摘要</dt>
              <dd>
                <button
                  type="button"
                  class="btn-small pdf-summary-toggle"
                  @click="pdfSummaryOpen = !pdfSummaryOpen"
                >{{ pdfSummaryOpen ? '收起' : '展开' }}</button>
                <pre v-if="pdfSummaryOpen" class="pdf-summary-content">{{ collectPdfSummary }}</pre>
              </dd>
            </div>
          </dl>
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
                <td class="log-note">{{ formatLogNote(log) }}</td>
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
  display: flex; align-items: center; gap: 8px; flex-wrap: wrap;
  padding: 8px 12px; background: #1a2535; border-radius: 6px; margin-bottom: 16px;
}
.status-dot { width: 8px; height: 8px; border-radius: 50%; display: inline-block; }
.status-dot.healthy { background: #2ecc71; }
.status-dot.unhealthy { background: #e74c3c; }
.status-dot.unknown { background: #888; }
.status-detail { color: #91a6bc; font-size: 12px; }

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

.collect-meta { margin: 8px 0 0; padding: 0; display: flex; flex-direction: column; gap: 4px; font-size: 12px; }
.collect-meta-row { display: flex; gap: 8px; align-items: flex-start; margin: 0; }
.collect-meta-row dt { color: #91a6bc; min-width: 70px; flex-shrink: 0; margin: 0; }
.collect-meta-row dd { color: #ddd; margin: 0; word-break: break-word; flex: 1; }
.collect-meta-fallback { color: #d97706; }
.source-channel-tag {
  display: inline-block; padding: 1px 8px; font-size: 11px; line-height: 1.5;
  border-radius: 10px; border: 1px solid transparent; font-weight: 500;
}
.pdf-summary-toggle { margin-right: 6px; }
.pdf-summary-content {
  margin: 6px 0 0; padding: 8px; background: #0d1622; border-radius: 4px;
  color: #c8d3e0; font-size: 11px; max-height: 220px; overflow: auto; white-space: pre-wrap;
}

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
