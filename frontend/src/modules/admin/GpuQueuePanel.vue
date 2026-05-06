<script setup>
import { ref, onMounted, onUnmounted, computed } from 'vue'

const status = ref(null)
const history = ref([])
const loading = ref(false)

async function fetchStatus() {
  try {
    const res = await fetch('/api/gpu-queue/status')
    if (res.ok) status.value = await res.json()
  } catch { /* ignore */ }
}

async function fetchHistory() {
  try {
    const res = await fetch('/api/gpu-queue/history?count=50')
    if (res.ok) history.value = await res.json()
  } catch { /* ignore */ }
}

async function refreshAll() {
  loading.value = true
  try {
    await Promise.all([fetchStatus(), fetchHistory()])
  } finally {
    loading.value = false
  }
}

let statusTimer = null
let historyTimer = null

onMounted(() => {
  refreshAll()
  statusTimer = setInterval(fetchStatus, 3000)
  historyTimer = setInterval(fetchHistory, 10000)
})

onUnmounted(() => {
  if (statusTimer) clearInterval(statusTimer)
  if (historyTimer) clearInterval(historyTimer)
})

const currentTask = computed(() => status.value?.currentTask)
const queuedTasks = computed(() => status.value?.queuedTasks || [])
const isPaused = computed(() => status.value?.isPaused ?? false)

async function pauseQueue() {
  await fetch('/api/gpu-queue/pause', { method: 'POST' })
  await fetchStatus()
}
async function resumeQueue() {
  await fetch('/api/gpu-queue/resume', { method: 'POST' })
  await fetchStatus()
}

async function cancelCurrent() {
  await fetch('/api/gpu-queue/cancel-current', { method: 'POST' })
  await fetchStatus()
}

function priorityLabel(p) {
  if (p === 2) return { text: '高', emoji: '🔴' }
  if (p === 1) return { text: '中', emoji: '🟡' }
  return { text: '低', emoji: '🟢' }
}

function stateLabel(s) {
  const map = {
    0: { text: '排队中', emoji: '⏸' },
    1: { text: '运行中', emoji: '⏳' },
    2: { text: '完成', emoji: '✅' },
    3: { text: '失败', emoji: '❌' },
    4: { text: '取消', emoji: '🚫' }
  }
  return map[s] || { text: '未知', emoji: '❓' }
}

function stateClass(s) {
  return { 0: 'gq-state-queued', 1: 'gq-state-running', 2: 'gq-state-completed', 3: 'gq-state-failed', 4: 'gq-state-cancelled' }[s] || ''
}

function formatDuration(d) {
  if (!d) return '-'
  // TimeSpan string like "00:02:25" or "1.02:03:04"
  const parts = d.split(':')
  if (parts.length < 2) return d
  let hours = 0, minutes = 0, seconds = 0
  if (parts.length === 3) {
    const hourPart = parts[0]
    // could be "1.02" for days
    if (hourPart.includes('.')) {
      const dp = hourPart.split('.')
      hours = parseInt(dp[0]) * 24 + parseInt(dp[1])
    } else {
      hours = parseInt(hourPart)
    }
    minutes = parseInt(parts[1])
    seconds = parseInt(parts[2])
  } else {
    minutes = parseInt(parts[0])
    seconds = parseInt(parts[1])
  }
  const totalMin = hours * 60 + minutes
  if (totalMin > 0) return `${totalMin}m ${seconds}s`
  return `${seconds}s`
}

function elapsedSince(startedAt) {
  if (!startedAt) return '-'
  const ms = Date.now() - new Date(startedAt).getTime()
  if (ms < 0) return '-'
  const totalSec = Math.floor(ms / 1000)
  const m = Math.floor(totalSec / 60)
  const s = totalSec % 60
  if (m > 0) return `${m}m ${s}s`
  return `${s}s`
}

function formatTime(t) {
  if (!t) return '-'
  return new Date(t).toLocaleTimeString('zh-CN', { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit' })
}
</script>

<template>
  <div class="gq-panel">
    <div class="gq-header">
      <h2>GPU 任务队列</h2>
      <div class="gq-header-actions">
        <button v-if="!isPaused" @click="pauseQueue" class="gq-btn gq-btn--warning">
          ⏸ 暂停队列
        </button>
        <button v-else @click="resumeQueue" class="gq-btn gq-btn--success">
          ▶ 恢复队列
        </button>
        <button class="gq-btn gq-btn-refresh" :disabled="loading" @click="refreshAll">
          {{ loading ? '刷新中...' : '🔄 刷新' }}
        </button>
      </div>
    </div>

    <div v-if="isPaused" class="gq-paused-banner">
      ⚠️ 队列已暂停 — 当前任务会完成，但不会启动新任务
    </div>

    <!-- 状态概览 -->
    <div class="gq-section-title">📊 状态概览</div>
    <div class="gq-overview">
      <!-- 当前任务 -->
      <div class="gq-overview-card">
        <div class="gq-card-label">当前任务</div>
        <template v-if="currentTask">
          <div class="gq-current-task">
            <span class="gq-state-indicator gq-indicator-running"></span>
            <span class="gq-task-name">{{ currentTask.taskName }}</span>
          </div>
          <div class="gq-detail-row">
            <span class="gq-detail-label">优先级</span>
            <span>{{ priorityLabel(currentTask.priority).emoji }} {{ priorityLabel(currentTask.priority).text }}</span>
          </div>
          <div class="gq-detail-row" v-if="currentTask.progressStatus">
            <span class="gq-detail-label">状态</span>
            <span>{{ currentTask.progressStatus }}</span>
          </div>
          <div class="gq-detail-row">
            <span class="gq-detail-label">已用时</span>
            <span>{{ elapsedSince(currentTask.startedAt) }}</span>
          </div>
          <button @click="cancelCurrent" class="gq-btn gq-btn--danger gq-cancel-btn">
            ✖ 取消当前任务
          </button>
        </template>
        <div v-else class="gq-idle">💤 GPU 空闲</div>
      </div>

      <!-- 排队中 -->
      <div class="gq-overview-card">
        <div class="gq-card-label">排队中</div>
        <template v-if="queuedTasks.length > 0">
          <div class="gq-queue-count">{{ queuedTasks.length }} 个任务等待</div>
          <div v-for="(t, i) in queuedTasks" :key="t.taskId" class="gq-queue-item">
            <span class="gq-queue-idx">{{ i + 1 }}.</span>
            <span class="gq-task-name">{{ t.taskName }}</span>
            <span class="gq-queue-priority">{{ priorityLabel(t.priority).emoji }}</span>
          </div>
        </template>
        <div v-else class="gq-idle">无等待任务</div>
      </div>
    </div>

    <!-- 历史记录 -->
    <div class="gq-section-title">📋 历史记录</div>
    <div class="gq-history-section">
      <table class="gq-table" v-if="history.length > 0">
        <thead>
          <tr>
            <th>任务名称</th>
            <th>优先级</th>
            <th>状态</th>
            <th>耗时</th>
            <th>完成时间</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="item in history" :key="item.taskId">
            <td class="gq-task-name-cell" :title="item.taskName">{{ item.taskName }}</td>
            <td>{{ priorityLabel(item.priority).emoji }} {{ priorityLabel(item.priority).text }}</td>
            <td :class="stateClass(item.state)">{{ stateLabel(item.state).emoji }} {{ stateLabel(item.state).text }}</td>
            <td>{{ formatDuration(item.duration) }}</td>
            <td>{{ formatTime(item.completedAt) }}</td>
          </tr>
        </tbody>
      </table>
      <div v-else class="gq-empty">暂无历史记录</div>
    </div>
  </div>
</template>

<style scoped>
.gq-panel { padding: 24px; max-width: 900px; }
.gq-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px; }
.gq-header h2 { margin: 0; font-size: 18px; color: #f0f0f0; }
.gq-header-actions { display: flex; gap: 8px; align-items: center; }

.gq-btn {
  padding: 8px 16px;
  border: 1px solid #444;
  border-radius: 6px;
  background: var(--bg-card, #1a1a2e);
  color: #eee;
  cursor: pointer;
  font-size: 13px;
  transition: all 0.2s;
}
.gq-btn:hover:not(:disabled) { background: #333; }
.gq-btn:disabled { opacity: 0.4; cursor: not-allowed; }
.gq-btn-refresh:hover:not(:disabled) { border-color: #3b82f6; }
.gq-btn--warning { border-color: #f59e0b; color: #f59e0b; }
.gq-btn--warning:hover { background: #f59e0b22; }
.gq-btn--success { border-color: #22c55e; color: #22c55e; }
.gq-btn--success:hover { background: #22c55e22; }
.gq-btn--danger { border-color: #ef4444; color: #ef4444; }
.gq-btn--danger:hover { background: #ef444422; }
.gq-cancel-btn { margin-top: 8px; width: 100%; font-size: 12px; padding: 6px 12px; }

.gq-paused-banner {
  background: #f59e0b18;
  border: 1px solid #f59e0b44;
  color: #f59e0b;
  padding: 10px 16px;
  border-radius: 6px;
  margin-bottom: 16px;
  font-size: 13px;
}

.gq-section-title { font-size: 14px; color: #bbb; margin: 16px 0 8px; }

.gq-overview { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-bottom: 16px; }
.gq-overview-card {
  background: var(--bg-card, #1a1a2e);
  border: 1px solid var(--border-color, #333);
  border-radius: 8px;
  padding: 16px;
}
.gq-card-label { font-size: 12px; color: #bbb; margin-bottom: 8px; text-transform: uppercase; letter-spacing: 0.5px; }

.gq-current-task { display: flex; align-items: center; gap: 8px; margin-bottom: 8px; color: #fff; }
.gq-state-indicator { width: 10px; height: 10px; border-radius: 50%; display: inline-block; }
.gq-indicator-running { background: #22c55e; box-shadow: 0 0 6px #22c55e80; animation: gq-pulse 1s infinite; }
@keyframes gq-pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }

.gq-task-name { font-weight: 600; font-size: 14px; color: #f0f0f0; }
.gq-detail-row { display: flex; justify-content: space-between; padding: 2px 0; font-size: 13px; color: #e0e0e0; }
.gq-detail-label { color: #aaa; }

.gq-idle { color: #999; font-size: 14px; padding: 12px 0; text-align: center; }

.gq-queue-count { font-size: 14px; color: #f59e0b; margin-bottom: 8px; }
.gq-queue-item { display: flex; align-items: center; gap: 6px; padding: 3px 0; font-size: 13px; color: #ddd; }
.gq-queue-idx { color: #999; width: 20px; }
.gq-queue-priority { margin-left: auto; }

.gq-history-section {
  border: 1px solid var(--border-color, #333);
  border-radius: 8px;
  overflow: hidden;
}

.gq-table { width: 100%; border-collapse: collapse; font-size: 13px; }
.gq-table thead { background: var(--bg-card, #1a1a2e); }
.gq-table th {
  padding: 10px 12px;
  text-align: left;
  font-weight: 600;
  color: #bbb;
  border-bottom: 1px solid var(--border-color, #333);
  font-size: 12px;
}
.gq-table td {
  padding: 8px 12px;
  border-bottom: 1px solid var(--border-color, #222);
  color: #e0e0e0;
}
.gq-table tbody tr:hover { background: #ffffff08; }

.gq-task-name-cell {
  max-width: 240px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.gq-state-completed { color: #22c55e; }
.gq-state-failed { color: #ef4444; }
.gq-state-cancelled { color: #888; }
.gq-state-running { color: #3b82f6; }
.gq-state-queued { color: #f59e0b; }

.gq-empty { color: #999; text-align: center; padding: 40px; font-size: 14px; }
</style>
