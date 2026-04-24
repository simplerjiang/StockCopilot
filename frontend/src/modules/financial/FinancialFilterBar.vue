<script setup>
import { computed, ref, watch } from 'vue'
import { REPORT_TYPE_OPTIONS } from './financialCenterConstants.js'

const props = defineProps({
  query: { type: Object, required: true },
  loading: { type: Boolean, default: false }
})

const emit = defineEmits(['submit', 'reset', 'change'])

/* ── chip 输入：股票代码 ── */
const symbolDraft = ref('')

const addSymbolsFromDraft = () => {
  const raw = symbolDraft.value
  if (!raw) return
  const tokens = raw
    .split(/[,，\s]+/)
    .map(t => t.trim())
    .filter(Boolean)
  if (tokens.length === 0) {
    symbolDraft.value = ''
    return
  }
  const exist = new Set(props.query.symbols)
  for (const t of tokens) {
    if (!exist.has(t)) {
      props.query.symbols.push(t)
      exist.add(t)
    }
  }
  symbolDraft.value = ''
}

const onSymbolKeydown = (e) => {
  if (e.key === 'Enter') {
    e.preventDefault()
    addSymbolsFromDraft()
  } else if (e.key === ',' || e.key === '，') {
    e.preventDefault()
    addSymbolsFromDraft()
  } else if (e.key === 'Backspace' && !symbolDraft.value && props.query.symbols.length > 0) {
    props.query.symbols.pop()
  }
}

const removeSymbol = (sym) => {
  const idx = props.query.symbols.indexOf(sym)
  if (idx >= 0) props.query.symbols.splice(idx, 1)
}

/* ── 报告期 ── */
const onStartChange = (e) => {
  props.query.startDate = e.target.value
  props.query.page = 1
  emit('change', { reason: 'date' })
}
const onEndChange = (e) => {
  props.query.endDate = e.target.value
  props.query.page = 1
  emit('change', { reason: 'date' })
}

/* ── 报告类型 pill ── */
const isTypeActive = (key) => props.query.reportTypes.includes(key)

const toggleType = (key) => {
  if (props.loading) return
  const arr = props.query.reportTypes
  const idx = arr.indexOf(key)
  if (idx >= 0) {
    if (arr.length === 1) return // 至少保留一个
    arr.splice(idx, 1)
  } else {
    arr.push(key)
  }
  props.query.page = 1
  emit('change', { reason: 'type' })
}

/* ── 关键词 防抖 + Enter ── */
const keywordDraft = ref(props.query.keyword || '')
let keywordTimer = null

watch(
  () => props.query.keyword,
  (val) => {
    if (val !== keywordDraft.value) keywordDraft.value = val || ''
  }
)

const onKeywordInput = (e) => {
  keywordDraft.value = e.target.value
  if (keywordTimer) clearTimeout(keywordTimer)
  keywordTimer = setTimeout(() => {
    props.query.keyword = keywordDraft.value
    props.query.page = 1
    emit('change', { reason: 'keyword' })
    keywordTimer = null
  }, 300)
}

const onKeywordEnter = () => {
  if (keywordTimer) {
    clearTimeout(keywordTimer)
    keywordTimer = null
  }
  props.query.keyword = keywordDraft.value
  props.query.page = 1
  emit('change', { reason: 'keyword-enter' })
}

/* ── 操作按钮 ── */
const onReset = () => {
  symbolDraft.value = ''
  keywordDraft.value = ''
  emit('reset')
}

const onSubmit = () => {
  // 把 chip 输入框未提交的内容也并进去
  if (symbolDraft.value) addSymbolsFromDraft()
  // 同步未防抖的关键词
  if (keywordTimer) {
    clearTimeout(keywordTimer)
    keywordTimer = null
    props.query.keyword = keywordDraft.value
  }
  props.query.page = 1
  emit('submit')
}

const reportTypes = computed(() => REPORT_TYPE_OPTIONS)
</script>

<template>
  <section class="fc-filter-bar" :class="{ 'fc-filter-bar--disabled': loading }">
    <div class="fc-filter-grid">
      <!-- 股票多选 -->
      <div class="fc-filter-cell">
        <label class="fc-filter-label">股票代码</label>
        <div class="fc-chip-input" :class="{ 'fc-chip-input--disabled': loading }">
          <span
            v-for="sym in query.symbols"
            :key="sym"
            class="fc-chip"
          >
            {{ sym }}
            <button
              type="button"
              class="fc-chip-remove"
              :disabled="loading"
              @click="removeSymbol(sym)"
              :aria-label="`移除 ${sym}`"
            >×</button>
          </span>
          <input
            type="text"
            class="fc-chip-input-field"
            :value="symbolDraft"
            :disabled="loading"
            placeholder="600519, 000001…"
            @input="symbolDraft = $event.target.value"
            @keydown="onSymbolKeydown"
            @blur="addSymbolsFromDraft"
          />
        </div>
        <p class="fc-filter-hint">按回车或逗号添加多个代码，点"查询"应用</p>
      </div>

      <!-- 报告期 -->
      <div class="fc-filter-cell">
        <label class="fc-filter-label">报告期</label>
        <div class="fc-date-range">
          <input
            type="date"
            class="fc-input"
            :value="query.startDate"
            :disabled="loading"
            @change="onStartChange"
          />
          <span class="fc-date-sep">~</span>
          <input
            type="date"
            class="fc-input"
            :value="query.endDate"
            :disabled="loading"
            @change="onEndChange"
          />
        </div>
        <p class="fc-filter-hint">默认显示近一年，修改后立即生效</p>
      </div>

      <!-- 报告类型 -->
      <div class="fc-filter-cell">
        <label class="fc-filter-label">报告类型</label>
        <div class="fc-pill-group">
          <button
            v-for="opt in reportTypes"
            :key="opt.key"
            type="button"
            class="fc-pill"
            :class="{ 'fc-pill--active': isTypeActive(opt.key) }"
            :disabled="loading"
            @click="toggleType(opt.key)"
          >{{ opt.label }}</button>
        </div>
        <p class="fc-filter-hint">修改后立即生效，至少保留 1 项</p>
      </div>

      <!-- 关键词 -->
      <div class="fc-filter-cell">
        <label class="fc-filter-label">关键词</label>
        <input
          type="text"
          class="fc-input fc-input--full"
          :value="keywordDraft"
          :disabled="loading"
          placeholder="输入股票代码或公司名（前端过滤）"
          @input="onKeywordInput"
          @keydown.enter.prevent="onKeywordEnter"
        />
        <p class="fc-filter-hint">关键词在前端二次过滤，仅展示当前页匹配项；如需精确请用股票代码筛选</p>
      </div>
    </div>

    <div class="fc-filter-actions">
      <button
        type="button"
        class="fc-btn fc-btn--ghost"
        :disabled="loading"
        @click="onReset"
      >重置</button>
      <button
        type="button"
        class="fc-btn fc-btn--primary"
        :disabled="loading"
        @click="onSubmit"
      >查询</button>
    </div>
  </section>
</template>

<style scoped>
.fc-filter-bar {
  position: sticky;
  top: 0;
  z-index: 5;
  background: var(--color-bg-surface);
  border: 1px solid var(--color-border-light);
  border-radius: var(--radius-lg);
  padding: var(--space-4) var(--space-5);
  box-shadow: var(--shadow-sm);
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}

.fc-filter-bar--disabled {
  opacity: 0.85;
}

.fc-filter-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: var(--space-4);
}

@media (max-width: 1080px) {
  .fc-filter-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 640px) {
  .fc-filter-grid {
    grid-template-columns: 1fr;
  }
}

.fc-filter-cell {
  display: flex;
  flex-direction: column;
  gap: var(--space-1-5);
  min-width: 0;
}

.fc-filter-label {
  font-size: var(--text-sm);
  font-weight: 600;
  color: var(--color-text-secondary);
}

.fc-filter-hint {
  margin: 0;
  font-size: var(--text-xs);
  color: var(--color-text-muted);
  line-height: var(--leading-tight);
}

/* ── 输入框 ── */
.fc-input {
  border: 1px solid var(--color-border-light);
  border-radius: var(--radius-md);
  padding: var(--space-2) var(--space-3);
  font-size: var(--text-base);
  font-family: var(--font-family-primary);
  color: var(--color-text-body);
  background: var(--color-bg-surface);
  transition: border-color var(--transition-fast), box-shadow var(--transition-fast);
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

.fc-input--full {
  width: 100%;
}

/* ── chip 输入 ── */
.fc-chip-input {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-1-5);
  border: 1px solid var(--color-border-light);
  border-radius: var(--radius-md);
  padding: var(--space-1-5) var(--space-2);
  background: var(--color-bg-surface);
  min-height: 36px;
  transition: border-color var(--transition-fast), box-shadow var(--transition-fast);
}

.fc-chip-input:focus-within {
  border-color: var(--color-accent-border);
  box-shadow: var(--shadow-ring-accent);
}

.fc-chip-input--disabled {
  background: var(--color-bg-inset);
  cursor: not-allowed;
}

.fc-chip {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
  padding: var(--space-0-5) var(--space-2);
  background: var(--color-accent-subtle);
  color: var(--color-accent-text);
  border: 1px solid var(--color-accent-border);
  border-radius: var(--radius-full);
  font-size: var(--text-sm);
  font-family: var(--font-family-mono);
  line-height: var(--leading-tight);
}

.fc-chip-remove {
  border: none;
  background: transparent;
  color: var(--color-accent-text);
  cursor: pointer;
  font-size: var(--text-base);
  line-height: 1;
  padding: 0;
}

.fc-chip-remove:disabled {
  cursor: not-allowed;
  opacity: 0.5;
}

.fc-chip-input-field {
  flex: 1;
  min-width: 80px;
  border: none;
  outline: none;
  background: transparent;
  font-size: var(--text-base);
  font-family: var(--font-family-primary);
  color: var(--color-text-body);
}

.fc-chip-input-field:disabled {
  cursor: not-allowed;
}

/* ── 日期范围 ── */
.fc-date-range {
  display: flex;
  align-items: center;
  gap: var(--space-2);
}

.fc-date-range .fc-input {
  flex: 1;
  min-width: 0;
}

.fc-date-sep {
  color: var(--color-text-muted);
  font-size: var(--text-base);
}

/* ── pill 组 ── */
.fc-pill-group {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-1-5);
}

.fc-pill {
  border: 1px solid var(--color-border-light);
  border-radius: var(--radius-full);
  background: var(--color-bg-surface);
  color: var(--color-text-body);
  padding: var(--space-1) var(--space-3);
  font-size: var(--text-sm);
  font-family: var(--font-family-primary);
  cursor: pointer;
  transition: background var(--transition-fast), color var(--transition-fast), border-color var(--transition-fast);
}

.fc-pill:hover:not(:disabled) {
  border-color: var(--color-accent-border);
  color: var(--color-accent-text);
}

.fc-pill--active {
  background: var(--color-accent);
  color: var(--color-text-on-dark);
  border-color: var(--color-accent);
}

.fc-pill--active:hover:not(:disabled) {
  background: var(--color-accent-hover);
  color: var(--color-text-on-dark);
  border-color: var(--color-accent-hover);
}

.fc-pill:disabled {
  cursor: not-allowed;
  opacity: 0.5;
}

/* ── 操作按钮 ── */
.fc-filter-actions {
  display: flex;
  justify-content: flex-end;
  gap: var(--space-2);
}

.fc-btn {
  border-radius: var(--radius-md);
  padding: var(--space-2) var(--space-4);
  font-size: var(--text-md);
  font-family: var(--font-family-primary);
  cursor: pointer;
  transition: background var(--transition-fast), color var(--transition-fast), border-color var(--transition-fast);
  border: 1px solid transparent;
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
