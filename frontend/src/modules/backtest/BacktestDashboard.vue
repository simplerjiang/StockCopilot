<script setup>
import { ref, onMounted } from 'vue'
import { fetchBackendGet } from '../stocks/stockInfoTabRequestUtils.js'

const stats = ref(null)
const results = ref([])
const totalResults = ref(0)
const loading = ref(false)
const batchRunning = ref(false)
const batchResult = ref(null)
const filterSymbol = ref('')
const filterStatus = ref('')
const page = ref(1)
const pageSize = 20
const symbolList = ref([])

async function loadStats() {
  loading.value = true
  try {
    const resp = await fetchBackendGet('/api/backtest/stats')
    stats.value = resp
  } catch (e) { console.error(e) }
  loading.value = false
}

async function loadResults() {
  const params = new URLSearchParams()
  params.set('page', page.value)
  params.set('size', pageSize)
  if (filterSymbol.value) params.set('symbol', filterSymbol.value)
  if (filterStatus.value) params.set('status', filterStatus.value)
  try {
    const resp = await fetchBackendGet(`/api/backtest/results?${params}`)
    results.value = resp.items || []
    totalResults.value = resp.total || 0
    if (!symbolList.value.length && results.value.length) {
      symbolList.value = [...new Set(results.value.map(r => r.symbol))].sort()
    }
  } catch (e) { console.error(e) }
}

async function runBatch() {
  batchRunning.value = true
  batchResult.value = null
  try {
    const resp = await fetch('/api/backtest/run-batch', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: '{}'
    })
    batchResult.value = await resp.json()
    await loadStats()
    await loadResults()
  } catch (e) { console.error(e) }
  batchRunning.value = false
}

function prevPage() { if (page.value > 1) { page.value--; loadResults() } }
function nextPage() { page.value++; loadResults() }

function formatReturn(val) { return val != null ? `${val >= 0 ? '+' : ''}${val.toFixed(2)}%` : '-' }
function formatPct(val) { return val != null ? `${(val * 100).toFixed(1)}%` : '-' }
function accuracyClass(v) { return v >= 0.6 ? 'good' : v >= 0.4 ? 'neutral' : 'poor' }
function directionClass(d) { return d?.includes('多') ? 'bull' : d?.includes('空') ? 'bear' : '' }
function correctClass(v) { return v === true ? 'correct' : v === false ? 'wrong' : '' }

onMounted(() => { loadStats(); loadResults() })
</script>

<template>
  <div class="backtest-dashboard">
    <h2>AI 分析回测验证</h2>

    <div class="backtest-actions">
      <button class="btn-primary" @click="runBatch" :disabled="batchRunning">
        {{ batchRunning ? '回测中...' : '批量回测' }}
      </button>
      <span v-if="batchResult" class="batch-result">
        处理 {{ batchResult.total }} 条: 成功 {{ batchResult.success }},
        跳过 {{ batchResult.skipped }}, 失败 {{ batchResult.failed }}
      </span>
    </div>

    <div class="scorecard-grid" v-if="stats">
      <div class="scorecard-item" v-for="(w, key) in stats.windows" :key="key">
        <div class="scorecard-label">{{ key }} 窗口</div>
        <div class="scorecard-value" :class="accuracyClass(w.accuracy)">
          {{ (w.accuracy * 100).toFixed(1) }}%
        </div>
        <div class="scorecard-sub">{{ w.count }} 条样本</div>
      </div>
      <div class="scorecard-item">
        <div class="scorecard-label">目标价触达</div>
        <div class="scorecard-value">{{ (stats.targetHitRate * 100).toFixed(1) }}%</div>
      </div>
      <div class="scorecard-item">
        <div class="scorecard-label">止损触发</div>
        <div class="scorecard-value warning">{{ (stats.stopTriggerRate * 100).toFixed(1) }}%</div>
      </div>
    </div>

    <div v-else-if="loading" class="loading">加载中...</div>
    <div v-else class="empty">暂无回测数据，请先执行批量回测</div>

    <div class="direction-table" v-if="stats?.byDirection">
      <h3>按方向统计</h3>
      <table>
        <thead>
          <tr><th>方向</th><th>样本数</th><th>1日</th><th>3日</th><th>5日</th><th>10日</th></tr>
        </thead>
        <tbody>
          <tr v-for="(d, dir) in stats.byDirection" :key="dir">
            <td>{{ dir }}</td>
            <td>{{ d.count }}</td>
            <td>{{ formatPct(d.accuracy1d) }}</td>
            <td>{{ formatPct(d.accuracy3d) }}</td>
            <td>{{ formatPct(d.accuracy5d) }}</td>
            <td>{{ formatPct(d.accuracy10d) }}</td>
          </tr>
        </tbody>
      </table>
    </div>

    <div class="results-section">
      <h3>回测明细</h3>
      <div class="results-filter">
        <select v-model="filterSymbol" @change="page = 1; loadResults()">
          <option value="">全部股票</option>
          <option v-for="s in symbolList" :key="s" :value="s">{{ s }}</option>
        </select>
        <select v-model="filterStatus" @change="page = 1; loadResults()">
          <option value="">全部状态</option>
          <option value="calculated">已计算</option>
          <option value="insufficient_data">数据不足</option>
        </select>
      </div>

      <table class="results-table" v-if="results.length">
        <thead>
          <tr>
            <th>股票</th><th>分析日期</th><th>预测方向</th><th>置信度</th>
            <th>1日</th><th>3日</th><th>5日</th><th>10日</th>
            <th>目标价</th><th>止损</th><th>状态</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="r in results" :key="r.id">
            <td>{{ r.name || r.symbol }}</td>
            <td>{{ r.analysisDate }}</td>
            <td :class="directionClass(r.predictedDirection)">{{ r.predictedDirection }}</td>
            <td>{{ r.confidence }}%</td>
            <td :class="correctClass(r.isCorrect1d)">{{ formatReturn(r.window1dActual) }}</td>
            <td :class="correctClass(r.isCorrect3d)">{{ formatReturn(r.window3dActual) }}</td>
            <td :class="correctClass(r.isCorrect5d)">{{ formatReturn(r.window5dActual) }}</td>
            <td :class="correctClass(r.isCorrect10d)">{{ formatReturn(r.window10dActual) }}</td>
            <td>{{ r.targetHit ? '✅' : r.targetHit === false ? '❌' : '-' }}</td>
            <td>{{ r.stopTriggered ? '⚠️' : r.stopTriggered === false ? '✅' : '-' }}</td>
            <td>{{ r.calcStatus }}</td>
          </tr>
        </tbody>
      </table>

      <div class="pagination" v-if="totalResults > pageSize">
        <button @click="prevPage" :disabled="page <= 1">上一页</button>
        <span>{{ page }} / {{ Math.ceil(totalResults / pageSize) }}</span>
        <button @click="nextPage" :disabled="page * pageSize >= totalResults">下一页</button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.backtest-dashboard {
  padding: 20px 24px;
  max-width: 1200px;
}
.backtest-dashboard h2 {
  margin: 0 0 16px;
  font-size: 18px;
  color: var(--color-text-primary);
}
.backtest-dashboard h3 {
  margin: 20px 0 10px;
  font-size: 15px;
  color: var(--color-text-primary);
}

/* 操作栏 */
.backtest-actions {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 18px;
}
.btn-primary {
  padding: 7px 18px;
  border: none;
  border-radius: 6px;
  background: var(--color-accent);
  color: #fff;
  font-size: 13px;
  cursor: pointer;
}
.btn-primary:hover:not(:disabled) { background: var(--color-accent-hover); }
.btn-primary:disabled { opacity: 0.55; cursor: not-allowed; }
.batch-result { font-size: 13px; color: var(--color-text-secondary); }

/* 记分卡 */
.scorecard-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  margin-bottom: 18px;
}
.scorecard-item {
  flex: 1 1 140px;
  max-width: 180px;
  padding: 14px;
  border-radius: 8px;
  background: var(--color-bg-surface);
  border: 1px solid var(--color-border-light);
  text-align: center;
}
.scorecard-label { font-size: 12px; color: var(--color-text-secondary); margin-bottom: 4px; }
.scorecard-value { font-size: 22px; font-weight: 700; color: var(--color-text-primary); }
.scorecard-sub { font-size: 11px; color: var(--color-text-muted); margin-top: 2px; }
.scorecard-value.good { color: var(--color-success); }
.scorecard-value.neutral { color: var(--color-warning); }
.scorecard-value.poor { color: var(--color-danger); }
.scorecard-value.warning { color: var(--color-warning); }

/* 空态 / 加载 */
.loading, .empty {
  padding: 32px;
  text-align: center;
  color: var(--color-text-muted);
  font-size: 14px;
}

/* 筛选 */
.results-filter {
  display: flex;
  gap: 8px;
  margin-bottom: 10px;
}
.results-filter select {
  padding: 5px 8px;
  border-radius: 5px;
  border: 1px solid var(--color-border-light);
  background: var(--color-bg-surface);
  color: var(--color-text-body);
  font-size: 13px;
}

/* 表格 */
table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
}
th {
  text-align: left;
  padding: 8px 6px;
  border-bottom: 2px solid var(--color-border-medium);
  color: var(--color-text-secondary);
  font-weight: 600;
  white-space: nowrap;
}
td {
  padding: 7px 6px;
  border-bottom: 1px solid var(--color-border-light);
  color: var(--color-text-body);
}
.bull { color: var(--color-market-rise); font-weight: 600; }
.bear { color: var(--color-market-fall); font-weight: 600; }
.correct { color: var(--color-success); }
.wrong { color: var(--color-danger); }

/* 分页 */
.pagination {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 12px;
  margin-top: 12px;
  font-size: 13px;
  color: var(--color-text-secondary);
}
.pagination button {
  padding: 4px 12px;
  border: 1px solid var(--color-border-light);
  border-radius: 5px;
  background: var(--color-bg-surface);
  color: var(--color-text-body);
  cursor: pointer;
}
.pagination button:disabled { opacity: 0.4; cursor: not-allowed; }
</style>
