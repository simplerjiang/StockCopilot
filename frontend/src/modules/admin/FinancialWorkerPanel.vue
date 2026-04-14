<script setup>
import { ref, computed, onMounted, onUnmounted, nextTick } from 'vue'

const status = ref(null)
const refreshLoading = ref(false)
const actionLoading = ref(false)
const actionResult = ref(null)
let pollTimer = null

function startPolling() {
  stopPolling()
  pollTimer = setInterval(refreshStatus, 10000)
}
function stopPolling() {
  if (pollTimer) { clearInterval(pollTimer); pollTimer = null }
}

async function refreshStatus() {
  refreshLoading.value = true
  try {
    const res = await fetch('/api/stocks/financial/worker/supervisor-status')
    if (res.ok) status.value = await res.json()
  } catch { /* ignore */ }
  finally { refreshLoading.value = false }
}

async function doAction(url, label) {
  actionLoading.value = true
  actionResult.value = null
  try {
    const res = await fetch(url, { method: 'POST' })
    const data = await res.json()
    actionResult.value = { success: res.ok, message: data.message || `${label}完成` }
    await refreshStatus()
  } catch (e) {
    actionResult.value = { success: false, message: `${label}失败: ${e.message}` }
  } finally {
    actionLoading.value = false
    setTimeout(() => { actionResult.value = null }, 5000)
  }
}

const startWorker = async () => { await doAction('/api/stocks/financial/worker/start', '启动'); logEntries.value = []; lastLogId = 0 }
const stopWorker = () => doAction('/api/stocks/financial/worker/stop', '停止')
const restartWorker = async () => { await doAction('/api/stocks/financial/worker/restart', '重启'); logEntries.value = []; lastLogId = 0 }

const statusLabel = computed(() => {
  const s = status.value?.state
  return { running: '运行中', stopped: '已停止', starting: '启动中...', error: '异常' }[s] || '未知'
})

const indicatorClass = computed(() => {
  const s = status.value?.state
  return { running: 'fw-green', stopped: 'fw-gray', starting: 'fw-yellow', error: 'fw-red' }[s] || 'fw-gray'
})

const statusCardClass = computed(() => {
  const s = status.value?.state
  return { running: 'fw-card-running', error: 'fw-card-error' }[s] || ''
})

const lastHeartbeatText = computed(() => {
  const hb = status.value?.lastHeartbeat
  if (!hb) return '无'
  const diff = Math.round((Date.now() - new Date(hb).getTime()) / 1000)
  const prefix = status.value?.state === 'stopped' ? '上次: ' : ''
  if (diff < 10) return `${prefix}刚刚`
  if (diff < 60) return `${prefix}${diff} 秒前`
  return `${prefix}${Math.round(diff / 60)} 分钟前`
})

const uptimeText = computed(() => {
  const start = status.value?.workerStartedAt
  if (!start || status.value?.state !== 'running') return null
  const ms = Date.now() - new Date(start).getTime()
  const mins = Math.floor(ms / 60000)
  if (mins < 1) return '不到 1 分钟'
  if (mins < 60) return `${mins} 分钟`
  const hours = Math.floor(mins / 60)
  const remainMins = mins % 60
  return `${hours} 小时 ${remainMins} 分钟`
})

function formatTime(t) {
  if (!t) return '-'
  return new Date(t).toLocaleString('zh-CN')
}

function formatLogTime(t) {
  if (!t) return ''
  const d = new Date(t)
  return d.toLocaleTimeString('zh-CN', { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit' })
}

// 日志相关 refs
const logEntries = ref([])
const logLoading = ref(false)
const autoScroll = ref(true)
const showLogLevel = ref('ALL')
let logPollTimer = null
let lastLogId = 0
const MAX_DISPLAY_ENTRIES = 500

async function fetchLogs() {
  if (status.value?.state !== 'running') return
  logLoading.value = true
  try {
    const url = lastLogId > 0
      ? `/api/stocks/financial/worker/runtime-logs?afterId=${lastLogId}&count=200`
      : '/api/stocks/financial/worker/runtime-logs?count=200'
    const res = await fetch(url)
    if (res.ok) {
      const data = await res.json()
      const entries = Array.isArray(data) ? data : (data.entries || [])

      // 检测 Worker 重启 — lastLogId>0 但无增量数据
      if (entries.length === 0 && lastLogId > 0) {
        const fullRes = await fetch('/api/stocks/financial/worker/runtime-logs?count=200')
        if (fullRes.ok) {
          const fullData = await fullRes.json()
          const fullEntries = Array.isArray(fullData) ? fullData : (fullData.entries || [])
          if (fullEntries.length > 0 && fullEntries[fullEntries.length - 1].id < lastLogId) {
            // 确认是 ID 重置，清空并重载
            logEntries.value = fullEntries
            lastLogId = fullEntries[fullEntries.length - 1].id
            if (autoScroll.value) nextTick(() => scrollToBottom())
            return
          }
        }
      }

      if (entries.length > 0) {
        logEntries.value.push(...entries)
        lastLogId = entries[entries.length - 1].id
        if (logEntries.value.length > MAX_DISPLAY_ENTRIES) {
          logEntries.value = logEntries.value.slice(-MAX_DISPLAY_ENTRIES)
        }
        if (autoScroll.value) nextTick(() => scrollToBottom())
      }
    }
  } catch { /* ignore */ }
  finally { logLoading.value = false }
}

function scrollToBottom() {
  const el = document.querySelector('.fw-log-body')
  if (el) el.scrollTop = el.scrollHeight
}

const filteredLogs = computed(() => {
  if (showLogLevel.value === 'ALL') return logEntries.value
  return logEntries.value.filter(e => e.level === showLogLevel.value)
})

function startLogPolling() {
  stopLogPolling()
  logPollTimer = setInterval(fetchLogs, 5000)
}
function stopLogPolling() {
  if (logPollTimer) { clearInterval(logPollTimer); logPollTimer = null }
}

function clearLogs() {
  // 保留 lastLogId 不重置，这样轮询只拉取清空后的新日志
  logEntries.value = []
}

onMounted(() => { refreshStatus(); startPolling(); fetchLogs(); startLogPolling() })
onUnmounted(() => { stopPolling(); stopLogPolling() })
</script>

<template>
  <div class="fw-panel">
    <h2>📊 财务数据工作者监控</h2>

    <!-- 状态卡片 -->
    <div class="fw-status-card" :class="statusCardClass">
      <div class="fw-status-header">
        <span class="fw-status-indicator" :class="indicatorClass"></span>
        <span class="fw-status-label">{{ statusLabel }}</span>
        <span class="fw-pid" v-if="status?.processId">PID: {{ status.processId }}</span>
      </div>

      <div class="fw-status-details" v-if="status">
        <div class="fw-detail-row">
          <span class="fw-detail-label">心跳</span>
          <span class="fw-detail-value">{{ lastHeartbeatText }}</span>
        </div>
        <div class="fw-detail-row" v-if="uptimeText">
          <span class="fw-detail-label">运行时长</span>
          <span class="fw-detail-value">{{ uptimeText }}</span>
        </div>
        <div class="fw-detail-row" v-if="status.lastHealthResponse?.currentActivity && status.state !== 'stopped'">
          <span class="fw-detail-label">当前活动</span>
          <span class="fw-detail-value">{{ status.lastHealthResponse.currentActivity }}</span>
        </div>
        <div class="fw-detail-row" v-if="status.lastHealthResponse?.lastCollectionResult">
          <span class="fw-detail-label">上次采集</span>
          <span class="fw-detail-value">{{ status.lastHealthResponse.lastCollectionResult }}</span>
        </div>
        <div class="fw-detail-row" v-if="status.lastHealthResponse?.lastCollectionTime">
          <span class="fw-detail-label">采集时间</span>
          <span class="fw-detail-value">{{ formatTime(status.lastHealthResponse.lastCollectionTime) }}</span>
        </div>
        <div class="fw-detail-row" v-if="status.lastError">
          <span class="fw-detail-label">错误</span>
          <span class="fw-detail-value fw-error-text">{{ status.lastError }}</span>
        </div>
      </div>
    </div>

    <!-- 控制按钮 -->
    <div class="fw-controls">
      <button @click="startWorker" :disabled="actionLoading || status?.state === 'running'" class="fw-btn fw-btn-start">
        ▶️ 启动
      </button>
      <button @click="stopWorker" :disabled="actionLoading || status?.state === 'stopped'" class="fw-btn fw-btn-stop">
        ⏹️ 停止
      </button>
      <button @click="restartWorker" :disabled="actionLoading" class="fw-btn fw-btn-restart">
        🔄 重启
      </button>
      <button @click="refreshStatus" :disabled="refreshLoading" class="fw-btn fw-btn-refresh">
        {{ refreshLoading ? '刷新中...' : '📡 刷新状态' }}
      </button>
    </div>

    <!-- 操作反馈 -->
    <div v-if="actionResult" class="fw-action-result" :class="actionResult.success ? 'fw-success' : 'fw-error'">
      {{ actionResult.message }}
    </div>

    <!-- 数据库 & 路径信息 -->
    <div class="fw-info-section" v-if="status?.lastHealthResponse">
      <h3>系统信息</h3>
      <div class="fw-detail-row" v-if="status.lastHealthResponse.dataRoot">
        <span class="fw-detail-label">数据目录</span>
        <span class="fw-detail-value fw-path">{{ status.lastHealthResponse.dataRoot }}</span>
      </div>
      <div class="fw-detail-row" v-if="status.lastHealthResponse.dbPath">
        <span class="fw-detail-label">数据库</span>
        <span class="fw-detail-value fw-path">{{ status.lastHealthResponse.dbPath }}</span>
      </div>
    </div>

    <!-- 运行时日志 -->
    <div class="fw-log-section">
      <div class="fw-log-header">
        <h3>📋 运行日志</h3>
        <div class="fw-log-controls">
          <select v-model="showLogLevel" class="fw-log-filter">
            <option value="ALL">全部</option>
            <option value="INFO">INFO</option>
            <option value="WARN">WARN</option>
            <option value="ERROR">ERROR</option>
          </select>
          <label class="fw-auto-scroll">
            <input type="checkbox" v-model="autoScroll" /> 自动滚动
          </label>
          <button @click="clearLogs" class="fw-btn-small">清空</button>
          <button @click="fetchLogs" class="fw-btn-small" :disabled="logLoading">刷新</button>
        </div>
      </div>
      <div class="fw-log-body" ref="logContainer">
        <div v-if="filteredLogs.length === 0" class="fw-log-empty">
          {{ status?.state === 'running' ? '暂无日志' : '工作者未运行' }}
        </div>
        <div v-for="entry in filteredLogs" :key="entry.id" class="fw-log-line" :class="'fw-log-' + entry.level.toLowerCase()">
          <span class="fw-log-time">{{ formatLogTime(entry.timestamp) }}</span>
          <span class="fw-log-level">{{ entry.level }}</span>
          <span class="fw-log-cat">{{ entry.category }}</span>
          <span class="fw-log-msg">{{ entry.message }}</span>
        </div>
      </div>
      <div class="fw-log-footer">
        <span>{{ filteredLogs.length }} 条日志</span>
        <span v-if="logEntries.length >= MAX_DISPLAY_ENTRIES" class="fw-log-truncated">（已达显示上限，旧日志已移除）</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.fw-panel { padding: 24px; max-width: 900px; }
.fw-panel h2 { margin: 0 0 16px; font-size: 18px; }
.fw-panel h3 { margin: 16px 0 8px; font-size: 14px; color: #888; }

.fw-status-card {
  background: var(--bg-card, #1a1a2e);
  border: 1px solid var(--border-color, #333);
  border-radius: 8px;
  padding: 16px;
  margin-bottom: 16px;
}
.fw-card-running { border-color: #22c55e; }
.fw-card-error { border-color: #ef4444; }

.fw-status-header { display: flex; align-items: center; gap: 8px; margin-bottom: 12px; }
.fw-status-indicator { width: 10px; height: 10px; border-radius: 50%; display: inline-block; }
.fw-green { background: #22c55e; box-shadow: 0 0 6px #22c55e80; }
.fw-gray { background: #666; }
.fw-yellow { background: #f59e0b; animation: pulse 1s infinite; }
.fw-red { background: #ef4444; }
@keyframes pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }

.fw-status-label { font-weight: 600; font-size: 16px; }
.fw-pid { color: #888; font-size: 12px; margin-left: auto; }

.fw-detail-row { display: flex; justify-content: space-between; padding: 4px 0; font-size: 13px; }
.fw-detail-label { color: #999; }
.fw-detail-value { color: #eee; }
.fw-error-text { color: #ef4444; max-width: 320px; word-break: break-all; }
.fw-path { color: #888; font-family: monospace; font-size: 11px; max-width: 320px; word-break: break-all; }

.fw-controls { display: flex; gap: 8px; margin-bottom: 16px; flex-wrap: wrap; }
.fw-btn {
  padding: 8px 16px;
  border: 1px solid #444;
  border-radius: 6px;
  background: var(--bg-card, #1a1a2e);
  color: #eee;
  cursor: pointer;
  font-size: 13px;
  transition: all 0.2s;
}
.fw-btn:hover:not(:disabled) { background: #333; }
.fw-btn:disabled { opacity: 0.4; cursor: not-allowed; }
.fw-btn-start:hover:not(:disabled) { border-color: #22c55e; }
.fw-btn-stop:hover:not(:disabled) { border-color: #ef4444; }
.fw-btn-restart:hover:not(:disabled) { border-color: #f59e0b; }

.fw-action-result { padding: 8px 12px; border-radius: 6px; font-size: 13px; margin-bottom: 16px; }
.fw-success { background: #22c55e20; color: #22c55e; border: 1px solid #22c55e40; }
.fw-error { background: #ef444420; color: #ef4444; border: 1px solid #ef444440; }

.fw-info-section { background: var(--bg-card, #1a1a2e); border: 1px solid var(--border-color, #333); border-radius: 8px; padding: 12px 16px; }

/* 日志区域 */
.fw-log-section {
  margin-top: 16px;
  border: 1px solid var(--border-color, #333);
  border-radius: 8px;
  overflow: hidden;
}

.fw-log-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 8px 12px;
  background: var(--bg-card, #1a1a2e);
  border-bottom: 1px solid var(--border-color, #333);
}
.fw-log-header h3 { margin: 0; font-size: 14px; color: #ccc; }

.fw-log-controls { display: flex; gap: 8px; align-items: center; }

.fw-log-filter {
  background: #111;
  color: #ccc;
  border: 1px solid #444;
  border-radius: 4px;
  padding: 2px 6px;
  font-size: 12px;
}

.fw-auto-scroll {
  font-size: 12px;
  color: #888;
  display: flex;
  align-items: center;
  gap: 4px;
  cursor: pointer;
}
.fw-auto-scroll input { cursor: pointer; }

.fw-btn-small {
  padding: 2px 8px;
  font-size: 12px;
  background: #222;
  color: #ccc;
  border: 1px solid #444;
  border-radius: 4px;
  cursor: pointer;
}
.fw-btn-small:hover:not(:disabled) { background: #333; }
.fw-btn-small:disabled { opacity: 0.4; }

.fw-log-body {
  background: #0a0a14;
  min-height: 200px;
  max-height: 400px;
  overflow-y: auto;
  font-family: 'Consolas', 'Courier New', monospace;
  font-size: 12px;
  line-height: 1.5;
  padding: 4px 0;
}

.fw-log-empty {
  color: #555;
  text-align: center;
  padding: 40px;
  font-family: inherit;
}

.fw-log-line {
  padding: 1px 12px;
  white-space: pre-wrap;
  word-break: break-all;
}
.fw-log-line:hover { background: #ffffff08; }

.fw-log-time { color: #666; margin-right: 8px; }
.fw-log-level {
  display: inline-block;
  width: 40px;
  text-align: center;
  font-weight: 600;
  margin-right: 8px;
}
.fw-log-cat { color: #6a9fb5; margin-right: 8px; }
.fw-log-msg { color: #ccc; }

/* 日志级别颜色 */
.fw-log-info .fw-log-level { color: #4ec9b0; }
.fw-log-warn .fw-log-level { color: #f59e0b; }
.fw-log-warn .fw-log-msg { color: #f59e0b; }
.fw-log-error .fw-log-level { color: #ef4444; }
.fw-log-error .fw-log-msg { color: #ef4444; }
.fw-log-crit .fw-log-level { color: #ff0000; font-weight: 900; }
.fw-log-crit .fw-log-msg { color: #ff0000; }

.fw-log-footer {
  padding: 4px 12px;
  font-size: 11px;
  color: #555;
  background: var(--bg-card, #1a1a2e);
  border-top: 1px solid var(--border-color, #333);
  display: flex;
  justify-content: space-between;
}
.fw-log-truncated { color: #f59e0b; }
</style>
