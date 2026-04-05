<script setup>
import { computed, ref, toRef, watch } from 'vue'

const props = defineProps({
  symbol: { type: String, default: '' },
  active: { type: Boolean, default: false }
})

const symbolRef = toRef(props, 'symbol')
const loading = ref(false)
const error = ref('')
const trend = ref(null)
const summary = ref(null)
const activeStatement = ref('income')
const collecting = ref(false)
const collectResult = ref(null)
const collectError = ref('')

async function fetchData() {
  if (!symbolRef.value || !props.active) return
  loading.value = true
  error.value = ''
  collectError.value = ''
  collectResult.value = null
  try {
    const [trendRes, summaryRes] = await Promise.all([
      fetch(`/api/stocks/financial/trend/${symbolRef.value}`),
      fetch(`/api/stocks/financial/summary/${symbolRef.value}`)
    ])
    if (trendRes.ok) trend.value = await trendRes.json()
    if (summaryRes.ok) summary.value = await summaryRes.json()
  } catch (e) {
    error.value = '加载失败: ' + e.message
  } finally {
    loading.value = false
  }
}

async function collectData() {
  if (!symbolRef.value || collecting.value) return
  collecting.value = true
  collectError.value = ''
  collectResult.value = null
  try {
    const resp = await fetch(`/api/stocks/financial/collect/${encodeURIComponent(symbolRef.value)}`, { method: 'POST' })
    if (!resp.ok) {
      const body = await resp.json().catch(() => null)
      throw new Error(body?.error || `采集失败 (${resp.status})`)
    }
    const result = await resp.json()
    collectResult.value = result
    if (result.Success) {
      await fetchData()
    } else {
      collectError.value = result.ErrorMessage || '采集未成功'
    }
  } catch (e) {
    collectError.value = e.message || '采集请求异常'
  } finally {
    collecting.value = false
  }
}

watch([symbolRef, () => props.active], () => { fetchData() }, { immediate: true })

const statementTypes = [
  { key: 'income', label: '利润表' },
  { key: 'balance', label: '资产负债表' },
  { key: 'cashflow', label: '现金流量表' }
]

const topMetrics = computed(() => {
  if (!trend.value) return []
  const metrics = []
  const rev = trend.value.revenue
  const np = trend.value.netProfit
  const ta = trend.value.totalAssets

  if (rev?.length > 0) {
    const latest = rev[0]
    metrics.push({
      label: '营业收入',
      value: formatLargeNumber(latest.value),
      yoyText: formatYoY(latest.yoY),
      yoyClass: getYoYClass(latest.yoY)
    })
  }
  if (np?.length > 0) {
    const latest = np[0]
    metrics.push({
      label: '净利润',
      value: formatLargeNumber(latest.value),
      yoyText: formatYoY(latest.yoY),
      yoyClass: getYoYClass(latest.yoY)
    })
  }
  if (ta?.length > 0) {
    const latest = ta[0]
    metrics.push({
      label: '总资产',
      value: formatLargeNumber(latest.value),
      yoyText: formatYoY(latest.yoY),
      yoyClass: getYoYClass(latest.yoY)
    })
  }
  return metrics
})

const trendRows = computed(() => {
  if (!trend.value) return []
  const rev = trend.value.revenue || []
  const np = trend.value.netProfit || []
  const ta = trend.value.totalAssets || []
  const maxLen = Math.max(rev.length, np.length, ta.length)
  const rows = []
  for (let i = 0; i < Math.min(maxLen, 8); i++) {
    rows.push({
      period: rev[i]?.period || np[i]?.period || ta[i]?.period || '-',
      revenue: formatCellValue(rev[i]?.value, rev[i]?.yoY),
      netProfit: formatCellValue(np[i]?.value, np[i]?.yoY),
      totalAssets: formatCellValue(ta[i]?.value, ta[i]?.yoY)
    })
  }
  return rows
})

const summaryPeriods = computed(() => {
  if (!summary.value?.periods) return []
  return summary.value.periods.map(p => p.reportDate).slice(0, 6)
})

const statementFieldMap = {
  income: [
    { key: 'Revenue', label: '营业收入' },
    { key: 'NetProfit', label: '净利润' },
    { key: 'GrossProfit', label: '毛利润' },
    { key: 'OperatingProfit', label: '营业利润' },
    { key: 'TotalRevenue', label: '营业总收入' },
    { key: 'TotalCost', label: '营业总成本' }
  ],
  balance: [
    { key: 'TotalAssets', label: '总资产' },
    { key: 'TotalLiabilities', label: '总负债' },
    { key: 'TotalEquity', label: '所有者权益' },
    { key: 'DebtToAssetRatio', label: '资产负债率', isRatio: true },
    { key: 'CurrentAssets', label: '流动资产' },
    { key: 'CurrentLiabilities', label: '流动负债' }
  ],
  cashflow: [
    { key: 'OperatingCashFlow', label: '经营活动现金流' },
    { key: 'InvestingCashFlow', label: '投资活动现金流' },
    { key: 'FinancingCashFlow', label: '筹资活动现金流' },
    { key: 'NetCashFlow', label: '现金净增加额' }
  ]
}

const summaryRows = computed(() => {
  if (!summary.value?.periods?.length) return []
  const fields = statementFieldMap[activeStatement.value] || []
  return fields.map(f => {
    const values = {}
    for (const p of summary.value.periods) {
      const km = p.keyMetrics || {}
      values[p.reportDate] = formatMetricValue(km[f.key] ?? km[f.label], f.isRatio)
    }
    return { key: f.key, label: f.label, values }
  })
})

const dividendRows = computed(() => {
  if (!trend.value?.recentDividends) return []
  return trend.value.recentDividends.map(d => ({
    plan: d.plan || '-',
    amount: d.dividendPerShare != null ? `¥${d.dividendPerShare.toFixed(4)}` : '-'
  }))
})

function formatLargeNumber(val) {
  if (val == null) return '-'
  const num = Number(val)
  if (isNaN(num)) return '-'
  if (Math.abs(num) >= 1e8) return (num / 1e8).toFixed(2) + '亿'
  if (Math.abs(num) >= 1e4) return (num / 1e4).toFixed(2) + '万'
  return num.toFixed(2)
}

function formatYoY(yoy) {
  if (yoy == null) return ''
  return (yoy >= 0 ? '+' : '') + yoy.toFixed(2) + '%'
}

function getYoYClass(yoy) {
  if (yoy == null) return ''
  return yoy >= 0 ? 'yoy-positive' : 'yoy-negative'
}

function formatCellValue(val, yoy) {
  const numStr = formatLargeNumber(val)
  const yoyStr = yoy != null ? ` (${formatYoY(yoy)})` : ''
  return numStr + yoyStr
}

function formatMetricValue(val, isRatio = false) {
  if (val == null) return '-'
  const num = Number(val)
  if (isNaN(num)) return String(val)
  if (isRatio) return (num * 100).toFixed(2) + '%'
  if (Math.abs(num) >= 1e8) return (num / 1e8).toFixed(2) + '亿'
  if (Math.abs(num) >= 1e4) return (num / 1e4).toFixed(2) + '万'
  return num.toFixed(2)
}
</script>

<template>
  <div class="financial-report-tab">
    <div v-if="loading" class="loading-state">加载中...</div>
    <div v-else-if="error" class="error-state">{{ error }}</div>
    <div v-else-if="!symbolRef" class="empty-state">
      <p>请先选择一只股票</p>
    </div>
    <div v-else-if="!trend && !summary" class="empty-state">
      <p>暂无财务数据</p>
      <button class="collect-btn" @click="collectData" :disabled="collecting">
        {{ collecting ? '获取中...' : '获取财务数据' }}
      </button>
      <p v-if="collectError" class="error-msg">{{ collectError }}</p>
    </div>
    <template v-else>

      <div class="report-header">
        <span class="header-title">财务报表</span>
        <button class="refresh-btn" @click="collectData" :disabled="collecting">
          {{ collecting ? '刷新中...' : '🔄 刷新数据' }}
        </button>
      </div>
      <p v-if="collectResult && collectResult.Success" class="collect-info">
        ✅ 已通过 {{ collectResult.Channel }} 获取 {{ collectResult.ReportCount }} 期报表，耗时 {{ (collectResult.DurationMs / 1000).toFixed(1) }}s
        <span v-if="collectResult.IsDegraded">（降级：{{ collectResult.DegradeReason }}）</span>
      </p>
      <p v-if="collectError" class="error-msg">{{ collectError }}</p>

      <!-- 区域 1: 核心指标卡片 -->
      <div class="metric-cards">
        <div class="metric-card" v-for="metric in topMetrics" :key="metric.label">
          <div class="metric-label">{{ metric.label }}</div>
          <div class="metric-value">{{ metric.value }}</div>
          <div class="metric-yoy" :class="metric.yoyClass">{{ metric.yoyText }}</div>
        </div>
      </div>

      <!-- 区域 2: 财务趋势表格 -->
      <div class="section-title">📈 财务趋势</div>
      <div class="trend-table-container">
        <table class="trend-table">
          <thead>
            <tr>
              <th>期间</th>
              <th>营业收入</th>
              <th>净利润</th>
              <th>总资产</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(item, idx) in trendRows" :key="idx">
              <td>{{ item.period }}</td>
              <td>{{ item.revenue }}</td>
              <td>{{ item.netProfit }}</td>
              <td>{{ item.totalAssets }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- 区域 3: 报表摘要 -->
      <div class="section-title">📋 报表摘要</div>
      <div class="statement-tabs">
        <button v-for="st in statementTypes" :key="st.key"
                :class="{ active: activeStatement === st.key }"
                @click="activeStatement = st.key">
          {{ st.label }}
        </button>
      </div>
      <div class="summary-table-container" v-if="summaryRows.length > 0">
        <table class="summary-table">
          <thead>
            <tr>
              <th>指标</th>
              <th v-for="period in summaryPeriods" :key="period">{{ period }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="row in summaryRows" :key="row.key">
              <td class="metric-name">{{ row.label }}</td>
              <td v-for="period in summaryPeriods" :key="period">{{ row.values[period] ?? '-' }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <div v-else class="empty-section">暂无报表数据</div>

      <!-- 区域 4: 分红记录 -->
      <div class="section-title">💰 近期分红</div>
      <div v-if="dividendRows.length > 0">
        <table class="dividend-table">
          <thead>
            <tr><th>方案</th><th>每股现金分红</th></tr>
          </thead>
          <tbody>
            <tr v-for="d in dividendRows" :key="d.plan">
              <td>{{ d.plan }}</td>
              <td>{{ d.amount }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <div v-else class="empty-section">暂无分红数据</div>

    </template>
  </div>
</template>

<style scoped>
.financial-report-tab {
  padding: 12px;
  color: #e0e0e0;
  font-size: 13px;
  overflow-y: auto;
  max-height: calc(100vh - 120px);
}

.loading-state, .error-state, .empty-state {
  text-align: center;
  padding: 40px 0;
  color: #888;
}
.error-state { color: #e74c3c; }

.metric-cards {
  display: flex;
  gap: 10px;
  margin-bottom: 16px;
  flex-wrap: wrap;
}
.metric-card {
  flex: 1;
  min-width: 120px;
  background: #1e2a3a;
  border-radius: 6px;
  padding: 10px 12px;
  border: 1px solid #2a3a4a;
}
.metric-label { color: #888; font-size: 11px; margin-bottom: 4px; }
.metric-value { font-size: 18px; font-weight: bold; color: #fff; }
.metric-yoy { font-size: 11px; margin-top: 2px; }
.yoy-positive { color: #e74c3c; }
.yoy-negative { color: #2ecc71; }

.section-title {
  font-size: 14px;
  font-weight: bold;
  margin: 16px 0 8px;
  color: #ccc;
}

.statement-tabs {
  display: flex;
  gap: 6px;
  margin-bottom: 8px;
}
.statement-tabs button {
  padding: 4px 12px;
  border: 1px solid #3a4a5a;
  background: transparent;
  color: #aaa;
  border-radius: 4px;
  cursor: pointer;
  font-size: 12px;
}
.statement-tabs button.active {
  background: #2a6ccf;
  color: #fff;
  border-color: #2a6ccf;
}

.trend-table-container, .summary-table-container {
  overflow-x: auto;
}

table {
  width: 100%;
  border-collapse: collapse;
  font-size: 12px;
}
th, td {
  padding: 6px 8px;
  border-bottom: 1px solid #2a3a4a;
  text-align: right;
  white-space: nowrap;
}
th { color: #999; font-weight: normal; background: #1a2535; }
td:first-child, th:first-child { text-align: left; }
.metric-name { color: #bbb; }

.dividend-table { max-width: 400px; }

.empty-section { color: #666; font-size: 12px; padding: 8px 0; }

.report-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}
.header-title {
  font-size: 15px;
  font-weight: 600;
  color: var(--text-primary, #e0e0e0);
}
.collect-btn, .refresh-btn {
  padding: 6px 16px;
  border: 1px solid var(--border-color, #444);
  border-radius: 6px;
  background: var(--bg-secondary, #2a2a2e);
  color: var(--text-primary, #e0e0e0);
  cursor: pointer;
  font-size: 13px;
  transition: background 0.2s;
}
.collect-btn:hover:not(:disabled), .refresh-btn:hover:not(:disabled) {
  background: var(--bg-hover, #3a3a3e);
}
.collect-btn:disabled, .refresh-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}
.collect-btn {
  margin-top: 12px;
  padding: 8px 24px;
  font-size: 14px;
}
.collect-info {
  font-size: 12px;
  color: var(--text-secondary, #aaa);
  margin-top: 6px;
}
.error-msg {
  color: #e74c3c;
  font-size: 12px;
  margin-top: 6px;
}
</style>
