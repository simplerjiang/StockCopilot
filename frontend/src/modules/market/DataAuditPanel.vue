<script setup>
import { ref } from 'vue'

defineProps({
  dataSources: { type: Array, default: () => [] },
  formula: { type: String, default: '' },
  computedAt: { type: String, default: '' },
  computeDurationMs: { type: Number, default: 0 }
})

const expanded = ref(false)

const statusIcon = status => {
  if (status === 'ok') return '✅'
  if (status === 'warning') return '⚠️'
  return '❌'
}

const cnDateTimeFormatter = new Intl.DateTimeFormat('zh-CN', {
  timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit',
  hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false
})
const formatDate = value => {
  if (!value) return '--'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '--'
  return cnDateTimeFormatter.format(date)
}
</script>

<template>
  <section class="audit-panel">
    <button class="audit-toggle" type="button" @click="expanded = !expanded">
      <span class="audit-icon">{{ expanded ? '▼' : '▶' }}</span>
      <span>数据审计面板</span>
    </button>

    <div v-if="expanded" class="audit-body">
      <!-- 数据源状态表 -->
      <div class="audit-section">
        <h4>数据源状态</h4>
        <div v-if="!dataSources.length" class="audit-empty">暂无数据源信息</div>
        <table v-else class="audit-table">
          <thead>
            <tr>
              <th>数据源</th>
              <th>状态</th>
              <th>最后成功</th>
              <th>连续失败</th>
              <th>延迟</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="src in dataSources" :key="src.name">
              <td>{{ src.name }}</td>
              <td>{{ statusIcon(src.status) }}</td>
              <td class="mono">{{ formatDate(src.lastSuccessTime) }}</td>
              <td class="mono" :class="{ 'err-count': src.consecutiveFailures > 0 }">{{ src.consecutiveFailures }}</td>
              <td class="mono">{{ src.latency != null ? `${src.latency}ms` : '--' }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- 算法公式 -->
      <div v-if="formula" class="audit-section">
        <h4>算法公式说明</h4>
        <pre class="audit-formula">{{ formula }}</pre>
      </div>

      <!-- 计算信息 -->
      <div v-if="computedAt" class="audit-section">
        <h4>计算信息</h4>
        <div class="audit-meta">
          <span>计算时间：{{ formatDate(computedAt) }}</span>
          <span>耗时：{{ computeDurationMs }}ms</span>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.audit-panel {
  background: #1a2233;
  border: 1px solid #334155;
}
.audit-toggle {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  padding: 8px 12px;
  background: transparent;
  border: none;
  color: #b2bccf;
  font-size: 12px;
  font-family: inherit;
  cursor: pointer;
  text-align: left;
  line-height: 1.3;
}
.audit-toggle:hover { color: #e6eaf2; background: #243147; }
.audit-icon { font-size: 11px; width: 12px; color: #98a6bf; }
.audit-body {
  display: grid;
  gap: 12px;
  padding: 0 12px 12px;
}
.audit-section h4 {
  margin: 0 0 6px;
  font-size: 11px;
  color: #b2bccf;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  line-height: 1.3;
}
.audit-empty {
  font-size: 12px;
  color: #98a6bf;
  font-style: italic;
  line-height: 1.3;
}
.audit-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 12px;
}
.audit-table th {
  text-align: left;
  padding: 4px 8px;
  color: #b2bccf;
  border-bottom: 1px solid #334155;
  font-weight: 600;
  line-height: 1.3;
}
.audit-table td {
  padding: 4px 8px;
  color: #e6eaf2;
  border-bottom: 1px solid rgba(51,65,85,0.6);
  line-height: 1.35;
}
.mono {
  font-family: Consolas, Monaco, 'Courier New', monospace;
  font-variant-numeric: tabular-nums;
}
.err-count { color: #ff5c5c; }
.audit-formula {
  margin: 0;
  padding: 8px;
  background: #111827;
  border: 1px solid #334155;
  color: #b2bccf;
  font-size: 12px;
  font-family: Consolas, Monaco, 'Courier New', monospace;
  white-space: pre-wrap;
  word-break: break-word;
  line-height: 1.5;
}
.audit-meta {
  display: flex;
  gap: 20px;
  font-size: 12px;
  color: #b2bccf;
  font-family: Consolas, Monaco, 'Courier New', monospace;
  line-height: 1.3;
}
</style>
