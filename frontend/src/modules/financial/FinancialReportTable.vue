<script setup>
import { computed } from 'vue'
import {
  REPORT_TYPE_LABEL,
  SOURCE_CHANNEL_STYLE,
  FALLBACK_CHANNEL_STYLE,
  PAGE_SIZE_OPTIONS
} from './financialCenterConstants.js'

const props = defineProps({
  items: { type: Array, required: true },
  total: { type: Number, default: 0 },
  loading: { type: Boolean, default: false },
  error: { type: String, default: '' },
  query: { type: Object, required: true },
  symbolNameMap: { type: Object, required: true },
  hasFilter: { type: Boolean, default: false },
  collectTabAvailable: { type: Boolean, default: false }
})

const emit = defineEmits([
  'sort-change',
  'page-change',
  'page-size-change',
  'open-detail',
  'reset',
  'retry',
  'go-collect'
])

/* ── 排序循环 ── */
const onSortClick = (field) => {
  if (props.loading) return
  emit('sort-change', field)
}

const sortIndicator = (field) => {
  if (props.query.sortField !== field) return 'none'
  return props.query.sortDirection
}

/* ── 渠道样式 ── */
const channelStyle = (channel) => {
  if (!channel) return FALLBACK_CHANNEL_STYLE
  const key = String(channel).toLowerCase()
  return SOURCE_CHANNEL_STYLE[key] || FALLBACK_CHANNEL_STYLE
}

const channelLabel = (channel) => {
  if (!channel) return '—'
  const key = String(channel).toLowerCase()
  return SOURCE_CHANNEL_STYLE[key]?.label || channel
}

/* ── 日期格式化（仅 yyyy-MM-dd） ── */
const formatDate = (raw) => {
  if (!raw) return '—'
  const d = new Date(raw)
  if (Number.isNaN(d.getTime())) return String(raw)
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

const formatDateTime = (raw) => {
  if (!raw) return '—'
  const d = new Date(raw)
  if (Number.isNaN(d.getTime())) return String(raw)
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  const hh = String(d.getHours()).padStart(2, '0')
  const mm = String(d.getMinutes()).padStart(2, '0')
  return `${y}-${m}-${day} ${hh}:${mm}`
}

const reportTypeText = (type) => {
  if (!type) return '—'
  const key = String(type).toLowerCase()
  return REPORT_TYPE_LABEL[key] || type
}

const symbolName = (symbol) => {
  if (!symbol) return '—'
  // 用属性访问触发 Vue 3 reactive Proxy 的 get trap，确保懒加载完成后单元格刷新
  const v = props.symbolNameMap[symbol]
  if (v === undefined) return ''
  return v || '—'
}

const getField = (item, lower, upper) => {
  if (!item) return ''
  return item[lower] !== undefined ? item[lower] : item[upper]
}

/* ── 分页 ── */
const totalPages = computed(() => {
  if (!props.total || props.total <= 0) return 1
  return Math.max(1, Math.ceil(props.total / props.query.pageSize))
})

const pageList = computed(() => {
  const total = totalPages.value
  const cur = props.query.page
  if (total <= 7) {
    return Array.from({ length: total }, (_, i) => i + 1)
  }
  const list = [1]
  if (cur - 1 > 2) list.push('...')
  for (let p = Math.max(2, cur - 1); p <= Math.min(total - 1, cur + 1); p++) {
    list.push(p)
  }
  if (cur + 1 < total - 1) list.push('...')
  list.push(total)
  return list
})

const onPageClick = (p) => {
  if (typeof p !== 'number') return
  if (p === props.query.page) return
  if (p < 1 || p > totalPages.value) return
  emit('page-change', p)
}

const goPrev = () => {
  if (props.query.page <= 1) return
  emit('page-change', props.query.page - 1)
}
const goNext = () => {
  if (props.query.page >= totalPages.value) return
  emit('page-change', props.query.page + 1)
}

const jumpInput = (e) => {
  if (e.key !== 'Enter') return
  const v = parseInt(e.target.value, 10)
  if (!Number.isFinite(v)) return
  const clamped = Math.min(Math.max(1, v), totalPages.value)
  emit('page-change', clamped)
  e.target.value = ''
}

const onPageSizeChange = (e) => {
  const v = parseInt(e.target.value, 10)
  if (!Number.isFinite(v)) return
  emit('page-size-change', v)
}

/* ── 状态 ── */
const showEmpty = computed(() => !props.loading && !props.error && props.items.length === 0)
</script>

<template>
  <section class="fc-table-card">
    <!-- 错误 banner -->
    <div v-if="error" class="fc-error-banner" role="alert">
      <span class="fc-error-text">⚠ 加载失败：{{ error }}</span>
      <button type="button" class="fc-btn fc-btn--ghost fc-btn--sm" @click="emit('retry')">重试</button>
    </div>

    <div class="fc-table-wrapper">
      <table class="fc-table">
        <thead>
          <tr>
            <th class="fc-th fc-th--sortable" @click="onSortClick('symbol')">
              <span class="fc-th-inner">
                股票代码
                <span class="fc-sort-icon" :data-state="sortIndicator('symbol')">
                  <span v-if="sortIndicator('symbol') === 'desc'">▼</span>
                  <span v-else-if="sortIndicator('symbol') === 'asc'">▲</span>
                  <span v-else>▲▼</span>
                </span>
              </span>
            </th>
            <th class="fc-th">名称</th>
            <th class="fc-th fc-th--sortable" @click="onSortClick('reportDate')">
              <span class="fc-th-inner">
                报告期
                <span class="fc-sort-icon" :data-state="sortIndicator('reportDate')">
                  <span v-if="sortIndicator('reportDate') === 'desc'">▼</span>
                  <span v-else-if="sortIndicator('reportDate') === 'asc'">▲</span>
                  <span v-else>▲▼</span>
                </span>
              </span>
            </th>
            <th class="fc-th fc-th--sortable fc-th--center" @click="onSortClick('reportType')">
              <span class="fc-th-inner">
                类型
                <span class="fc-sort-icon" :data-state="sortIndicator('reportType')">
                  <span v-if="sortIndicator('reportType') === 'desc'">▼</span>
                  <span v-else-if="sortIndicator('reportType') === 'asc'">▲</span>
                  <span v-else>▲▼</span>
                </span>
              </span>
            </th>
            <th class="fc-th fc-th--sortable fc-th--center" @click="onSortClick('sourceChannel')">
              <span class="fc-th-inner">
                来源渠道
                <span class="fc-sort-icon" :data-state="sortIndicator('sourceChannel')">
                  <span v-if="sortIndicator('sourceChannel') === 'desc'">▼</span>
                  <span v-else-if="sortIndicator('sourceChannel') === 'asc'">▲</span>
                  <span v-else>▲▼</span>
                </span>
              </span>
            </th>
            <th class="fc-th fc-th--sortable" @click="onSortClick('collectedAt')">
              <span class="fc-th-inner">
                采集时间
                <span class="fc-sort-icon" :data-state="sortIndicator('collectedAt')">
                  <span v-if="sortIndicator('collectedAt') === 'desc'">▼</span>
                  <span v-else-if="sortIndicator('collectedAt') === 'asc'">▲</span>
                  <span v-else>▲▼</span>
                </span>
              </span>
            </th>
            <th class="fc-th">操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(item, idx) in items" :key="getField(item, 'id', 'Id') ?? idx" class="fc-tr">
            <td class="fc-td fc-td--symbol">{{ getField(item, 'symbol', 'Symbol') || '—' }}</td>
            <td class="fc-td">{{ symbolName(getField(item, 'symbol', 'Symbol')) || '加载中…' }}</td>
            <td class="fc-td">{{ formatDate(getField(item, 'reportDate', 'ReportDate')) }}</td>
            <td class="fc-td fc-td--center">{{ reportTypeText(getField(item, 'reportType', 'ReportType')) }}</td>
            <td class="fc-td fc-td--center">
              <span
                class="fc-channel-tag"
                :style="{
                  background: channelStyle(getField(item, 'sourceChannel', 'SourceChannel')).bg,
                  color: channelStyle(getField(item, 'sourceChannel', 'SourceChannel')).color,
                  borderColor: channelStyle(getField(item, 'sourceChannel', 'SourceChannel')).border
                }"
              >{{ channelLabel(getField(item, 'sourceChannel', 'SourceChannel')) }}</span>
            </td>
            <td class="fc-td">{{ formatDateTime(getField(item, 'collectedAt', 'CollectedAt')) }}</td>
            <td class="fc-td">
              <button type="button" class="fc-link-btn" @click="emit('open-detail', item)">详情</button>
            </td>
          </tr>
        </tbody>
      </table>

      <!-- 空状态 -->
      <div v-if="showEmpty" class="fc-empty">
        <template v-if="hasFilter">
          <p class="fc-empty-text">当前筛选下没有匹配的财报</p>
          <p class="fc-empty-hint">历史年份数据可能尚未采集，可前往采集面板获取更多数据</p>
          <button type="button" class="fc-btn fc-btn--ghost" @click="emit('reset')">重置筛选</button>
        </template>
        <template v-else>
          <p class="fc-empty-text">还没有财报数据，请先去采集面板抓取</p>
          <button
            type="button"
            class="fc-btn fc-btn--primary"
            :disabled="!collectTabAvailable"
            @click="collectTabAvailable && emit('go-collect')"
          >前往采集面板</button>
          <p v-if="!collectTabAvailable" class="fc-empty-hint">采集面板入口未启用</p>
        </template>
      </div>

      <!-- loading 蒙层 -->
      <div v-if="loading" class="fc-loading-overlay" aria-busy="true">
        <div class="fc-spinner" />
        <span class="fc-loading-text">加载中…</span>
      </div>
    </div>

    <!-- footer 分页 -->
    <footer class="fc-table-footer">
      <div class="fc-footer-left">共 {{ total }} 条</div>
      <div class="fc-footer-right">
        <select
          class="fc-input fc-page-size"
          :value="query.pageSize"
          :disabled="loading"
          @change="onPageSizeChange"
        >
          <option v-for="opt in PAGE_SIZE_OPTIONS" :key="opt" :value="opt">{{ opt }} 条/页</option>
        </select>
        <button
          type="button"
          class="fc-page-btn"
          :disabled="query.page <= 1 || loading"
          @click="goPrev"
        >上一页</button>
        <template v-for="(p, i) in pageList" :key="`p-${i}-${p}`">
          <button
            v-if="typeof p === 'number'"
            type="button"
            class="fc-page-btn"
            :class="{ 'fc-page-btn--active': p === query.page }"
            :disabled="loading"
            @click="onPageClick(p)"
          >{{ p }}</button>
          <span v-else class="fc-page-ellipsis">…</span>
        </template>
        <button
          type="button"
          class="fc-page-btn"
          :disabled="query.page >= totalPages || loading"
          @click="goNext"
        >下一页</button>
        <input
          type="number"
          min="1"
          class="fc-input fc-page-jump"
          :disabled="loading"
          placeholder="跳页"
          @keydown="jumpInput"
        />
      </div>
    </footer>
  </section>
</template>

<style scoped>
.fc-table-card {
  background: var(--color-bg-surface);
  border: 1px solid var(--color-border-light);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-sm);
  display: flex;
  flex-direction: column;
}

.fc-error-banner {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: var(--space-3);
  padding: var(--space-2) var(--space-4);
  background: var(--color-danger-bg);
  border-bottom: 1px solid var(--color-danger-border);
  color: var(--color-danger);
  font-size: var(--text-sm);
}

.fc-error-text {
  font-weight: 600;
}

.fc-table-wrapper {
  position: relative;
  overflow-x: auto;
  min-height: 240px;
}

.fc-table {
  width: 100%;
  border-collapse: collapse;
  font-size: var(--text-base);
  color: var(--color-text-body);
}

.fc-th {
  padding: var(--space-3) var(--space-4);
  background: var(--color-bg-surface-alt);
  color: var(--color-text-secondary);
  font-size: var(--text-sm);
  font-weight: 600;
  text-align: left;
  border-bottom: 1px solid var(--color-border-light);
  user-select: none;
}

.fc-th--center {
  text-align: center;
}

.fc-th--sortable {
  cursor: pointer;
  transition: background var(--transition-fast);
}

.fc-th--sortable:hover {
  background: var(--color-bg-inset);
}

.fc-th-inner {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
}

.fc-sort-icon {
  font-size: var(--text-xs);
  color: var(--color-text-disabled);
  letter-spacing: -1px;
}

.fc-sort-icon[data-state='asc'],
.fc-sort-icon[data-state='desc'] {
  color: var(--color-accent);
}

.fc-tr {
  transition: background var(--transition-fast);
}

.fc-tr:hover {
  background: var(--color-bg-surface-alt);
}

.fc-td {
  padding: var(--space-3) var(--space-4);
  border-bottom: 1px solid var(--color-border-light);
  vertical-align: middle;
}

.fc-td--center {
  text-align: center;
}

.fc-td--symbol {
  font-family: var(--font-family-mono);
  color: var(--color-accent-text);
  cursor: default;
}

.fc-td--symbol:hover {
  text-decoration: underline;
}

.fc-channel-tag {
  display: inline-block;
  padding: var(--space-0-5) var(--space-2);
  border-radius: var(--radius-full);
  border: 1px solid;
  font-size: var(--text-xs);
  font-weight: 600;
  line-height: var(--leading-tight);
}

.fc-link-btn {
  border: none;
  background: transparent;
  color: var(--color-accent-text);
  cursor: pointer;
  font-size: var(--text-base);
  padding: 0;
  font-family: var(--font-family-primary);
}

.fc-link-btn:hover {
  text-decoration: underline;
  color: var(--color-accent-hover);
}

/* ── 空状态 ── */
.fc-empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: var(--space-3);
  padding: var(--space-10) var(--space-4);
  color: var(--color-text-secondary);
}

.fc-empty-text {
  margin: 0;
  font-size: var(--text-md);
}

.fc-empty-hint {
  margin: 0;
  font-size: var(--text-xs);
  color: var(--color-text-muted);
}

/* ── loading 蒙层 ── */
.fc-loading-overlay {
  position: absolute;
  inset: 0;
  background: rgba(255, 255, 255, 0.7);
  display: flex;
  align-items: center;
  justify-content: center;
  flex-direction: column;
  gap: var(--space-2);
  z-index: 1;
}

.fc-spinner {
  width: 28px;
  height: 28px;
  border: 3px solid var(--color-accent-subtle);
  border-top-color: var(--color-accent);
  border-radius: var(--radius-full);
  animation: fc-spin 800ms linear infinite;
}

@keyframes fc-spin {
  to { transform: rotate(360deg); }
}

.fc-loading-text {
  font-size: var(--text-sm);
  color: var(--color-text-secondary);
}

/* ── footer ── */
.fc-table-footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: var(--space-3) var(--space-4);
  border-top: 1px solid var(--color-border-light);
  background: var(--color-bg-surface-alt);
  font-size: var(--text-sm);
  color: var(--color-text-secondary);
  flex-wrap: wrap;
  gap: var(--space-2);
}

.fc-footer-right {
  display: flex;
  align-items: center;
  gap: var(--space-1-5);
  flex-wrap: wrap;
}

.fc-page-btn {
  border: 1px solid var(--color-border-light);
  background: var(--color-bg-surface);
  color: var(--color-text-body);
  border-radius: var(--radius-md);
  padding: var(--space-1) var(--space-2-5, 10px);
  font-size: var(--text-sm);
  cursor: pointer;
  min-width: 32px;
  transition: background var(--transition-fast), border-color var(--transition-fast), color var(--transition-fast);
}

.fc-page-btn:hover:not(:disabled) {
  border-color: var(--color-accent-border);
  color: var(--color-accent-text);
}

.fc-page-btn--active,
.fc-page-btn--active:hover {
  background: var(--color-accent);
  color: var(--color-text-on-dark);
  border-color: var(--color-accent);
}

.fc-page-btn:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.fc-page-ellipsis {
  color: var(--color-text-muted);
  padding: 0 var(--space-1);
}

.fc-page-size {
  padding: var(--space-1) var(--space-2);
  font-size: var(--text-sm);
}

.fc-page-jump {
  width: 64px;
  padding: var(--space-1) var(--space-2);
  font-size: var(--text-sm);
}

/* ── 输入框基础（与 FilterBar 同款 token） ── */
.fc-input {
  border: 1px solid var(--color-border-light);
  border-radius: var(--radius-md);
  padding: var(--space-2) var(--space-3);
  font-size: var(--text-base);
  font-family: var(--font-family-primary);
  color: var(--color-text-body);
  background: var(--color-bg-surface);
}

.fc-input:focus {
  outline: none;
  border-color: var(--color-accent-border);
  box-shadow: var(--shadow-ring-accent);
}

.fc-input:disabled {
  background: var(--color-bg-inset);
  color: var(--color-text-disabled);
  cursor: not-allowed;
}

/* ── 按钮 ── */
.fc-btn {
  border-radius: var(--radius-md);
  padding: var(--space-2) var(--space-4);
  font-size: var(--text-md);
  font-family: var(--font-family-primary);
  cursor: pointer;
  transition: background var(--transition-fast);
  border: 1px solid transparent;
}

.fc-btn--sm {
  padding: var(--space-1) var(--space-3);
  font-size: var(--text-sm);
}

.fc-btn--primary {
  background: var(--color-accent);
  color: var(--color-text-on-dark);
}

.fc-btn--primary:hover:not(:disabled) {
  background: var(--color-accent-hover);
}

.fc-btn--ghost {
  background: var(--color-bg-surface);
  color: var(--color-text-body);
  border-color: var(--color-border-light);
}

.fc-btn--ghost:hover:not(:disabled) {
  border-color: var(--color-border-medium);
  background: var(--color-bg-surface-alt);
}

.fc-btn:disabled {
  cursor: not-allowed;
  opacity: 0.5;
}
</style>
