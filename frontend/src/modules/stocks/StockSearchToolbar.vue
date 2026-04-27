<script setup>
import { ref, watch } from 'vue'

const advancedOpen = ref(false)
const selectedSearchIndex = ref(-1)

const props = defineProps({
  symbol: { type: String, required: true },
  selectedSource: { type: String, required: true },
  refreshSeconds: { type: Number, required: true },
  autoRefresh: { type: Boolean, required: true },
  sources: { type: Array, default: () => [] },
  searchOpen: { type: Boolean, default: false },
  searchResults: { type: Array, default: () => [] },
  searchLoading: { type: Boolean, default: false },
  searchError: { type: String, default: '' },
  isBlockingQuoteLoad: { type: Boolean, default: false },
  isBackgroundQuoteRefresh: { type: Boolean, default: false },
  error: { type: String, default: '' },
  historyAutoRefresh: { type: Boolean, required: true },
  historyRefreshSeconds: { type: Number, required: true },
  historyLoading: { type: Boolean, default: false },
  historyError: { type: String, default: '' },
  historyList: { type: Array, default: () => [] },
  sortedHistoryList: { type: Array, default: () => [] },
  invalidHistoryCount: { type: Number, default: 0 },
  contextMenu: { type: Object, required: true },
  getChangeClass: { type: Function, required: true },
  formatPercent: { type: Function, required: true }
})

const emit = defineEmits([
  'update:symbol',
  'update:selectedSource',
  'update:refreshSeconds',
  'update:autoRefresh',
  'update:historyRefreshSeconds',
  'update:historyAutoRefresh',
  'symbol-input',
  'symbol-enter',
  'fetch-quote',
  'close-search',
  'select-search-result',
  'refresh-history',
  'apply-history-symbol',
  'open-context-menu',
  'delete-history-item',
  'cleanup-invalid-history'
])

watch(() => props.searchOpen, open => { if (!open) selectedSearchIndex.value = -1 })

const handleSearchKeydown = event => {
  if (!props.searchOpen || !props.searchResults.length) return
  const max = props.searchResults.length
  if (event.key === 'ArrowDown') {
    event.preventDefault()
    selectedSearchIndex.value = (selectedSearchIndex.value + 1) % max
  } else if (event.key === 'ArrowUp') {
    event.preventDefault()
    selectedSearchIndex.value = (selectedSearchIndex.value - 1 + max) % max
  } else if (event.key === 'Enter' && selectedSearchIndex.value >= 0) {
    event.preventDefault()
    emit('select-search-result', props.searchResults[selectedSearchIndex.value])
  } else if (event.key === 'Escape') {
    event.preventDefault()
    emit('close-search')
  }
}
</script>

<template>
  <div class="compact-toolbar">
    <section class="toolbar-shell sticky-toolbar">
      <div class="toolbar-main-row">
        <div class="toolbar-title">
          <strong>标的查询</strong>
        </div>

        <div class="field search-field toolbar-search-field">
          <input
            :value="symbol"
            placeholder="输入股票代码/名称/拼音缩写"
            @input="$emit('update:symbol', $event.target.value); $emit('symbol-input')"
            @keydown.enter.prevent="$emit('symbol-enter')"
            @keydown="handleSearchKeydown"
          />
          <button @click="$emit('fetch-quote')" :disabled="isBlockingQuoteLoad">查询</button>
          <div v-if="searchOpen" class="search-dropdown">
            <div class="search-modal-header">
              <span>搜索结果</span>
              <button class="close-btn" @click="$emit('close-search')">关闭</button>
            </div>
            <p v-if="searchError" class="muted">{{ searchError }}</p>
            <p v-else-if="searchLoading" class="muted">搜索中...</p>
            <ul v-else class="search-list">
              <li
                v-for="(item, idx) in searchResults"
                :key="item.symbol || item.Symbol"
                :class="{ 'search-item-selected': idx === selectedSearchIndex }"
                @click="$emit('select-search-result', item)"
                @mouseenter="selectedSearchIndex = idx"
              >
                <div class="result-name">{{ item.name ?? item.Name }}</div>
                <div class="result-code">{{ item.symbol ?? item.Symbol }}</div>
              </li>
            </ul>
            <p v-if="!searchLoading && !searchError && !searchResults.length" class="muted">暂无匹配结果</p>
          </div>
        </div>

        <button class="toolbar-advanced-toggle" @click="advancedOpen = !advancedOpen">
          ⚙ {{ advancedOpen ? '收起' : '高级' }}
        </button>

        <slot name="actions" />
      </div>

      <template v-if="advancedOpen">
        <div class="toolbar-advanced-row">
          <div class="toolbar-settings">
            <div class="toolbar-select-group">
              <label class="muted">来源</label>
              <select :value="selectedSource" @change="$emit('update:selectedSource', $event.target.value)">
                <option value="">自动</option>
                <option v-for="item in sources" :key="item" :value="item">{{ item }}</option>
              </select>
            </div>

            <div class="toolbar-select-group narrow-group">
              <label class="muted">刷新</label>
              <input type="number" min="5" :value="refreshSeconds" @input="$emit('update:refreshSeconds', Number($event.target.value))" />
            </div>

            <label class="muted inline-check toolbar-check">
              <input type="checkbox" :checked="autoRefresh" @change="$emit('update:autoRefresh', $event.target.checked)" /> 自动
            </label>
          </div>
        </div>

        <div class="toolbar-sub-row">
        <p class="muted toolbar-status">
          数据刷新：{{ autoRefresh ? `每 ${refreshSeconds} 秒` : '手动刷新' }}
        </p>
        <p v-if="error" class="muted toolbar-status error-text">{{ error }}</p>
        <p v-else-if="isBlockingQuoteLoad" class="muted toolbar-status">查询中...</p>
        <p v-else-if="isBackgroundQuoteRefresh" class="muted toolbar-status">后台刷新中...</p>

        <div class="toolbar-history-actions">
          <span class="muted">历史：{{ historyAutoRefresh ? `每 ${historyRefreshSeconds} 秒` : '手动' }}</span>
          <select :value="historyRefreshSeconds" @change="$emit('update:historyRefreshSeconds', Number($event.target.value))">
            <option :value="10">10 秒</option>
            <option :value="15">15 秒</option>
            <option :value="30">30 秒</option>
            <option :value="60">60 秒</option>
          </select>
          <label class="muted inline-check toolbar-check">
            <input type="checkbox" :checked="historyAutoRefresh" @change="$emit('update:historyAutoRefresh', $event.target.checked)" /> 自动更新
          </label>
          <button @click="$emit('refresh-history')" :disabled="historyLoading">刷新历史</button>
        </div>
      </div>
      </template>

      <div class="toolbar-compact-status" v-if="!advancedOpen">
        <p v-if="autoRefresh" class="muted toolbar-status">
          自动刷新 {{ refreshSeconds }}s
        </p>
        <button v-else class="toolbar-manual-refresh" @click="$emit('fetch-quote')" :disabled="isBlockingQuoteLoad">
          ↻ 手动刷新
        </button>
        <p v-if="error" class="muted toolbar-status error-text">{{ error }}</p>
        <p v-else-if="isBlockingQuoteLoad" class="muted toolbar-status">查询中...</p>
        <p v-else-if="isBackgroundQuoteRefresh" class="muted toolbar-status">后台刷新中...</p>
      </div>

      <div class="history-ribbon">
        <p class="muted history-ribbon-title">最近查询</p>
        <div v-if="historyList.length" class="history-chip-row">
          <button
            v-for="item in sortedHistoryList"
            :key="item.id || item.Id"
            class="history-chip"
            @click="$emit('apply-history-symbol', item)"
            @contextmenu.prevent="$emit('open-context-menu', $event, item)"
          >
            <span>{{ (item.name ?? item.Name) || (item.symbol ?? item.Symbol) }}</span>
            <strong>{{ item.symbol ?? item.Symbol }}</strong>
            <small :class="getChangeClass(item.changePercent ?? item.ChangePercent)">
              {{ (item.name ?? item.Name) ? formatPercent(item.changePercent ?? item.ChangePercent) : '—' }}
            </small>
          </button>
        </div>
        <p v-else class="muted">暂无历史数据。</p>
        <p v-if="invalidHistoryCount" class="muted history-cleanup-note">
          已自动隐藏 {{ invalidHistoryCount }} 条无效历史。
          <button type="button" class="history-cleanup-button" @click="$emit('cleanup-invalid-history')">清理无效记录</button>
        </p>
        <p v-if="historyError" class="muted error-text">{{ historyError }}</p>
        <p v-if="historyLoading && !historyList.length" class="muted">历史数据刷新中...</p>
      </div>

      <div
        v-if="contextMenu.visible"
        class="context-menu"
        :style="{ left: `${contextMenu.x}px`, top: `${contextMenu.y}px` }"
      >
        <button @click="$emit('delete-history-item')">删除</button>
      </div>
    </section>
  </div>
</template>

<style scoped>
.compact-toolbar {
  position: relative;
}

.toolbar-shell {
  display: grid;
  gap: 0.75rem;
}

.sticky-toolbar {
  position: sticky;
  top: 0;
  z-index: 30;
  padding: 0.85rem 1rem;
  border-radius: 18px;
  border: 1px solid rgba(148, 163, 184, 0.18);
  background: rgba(255, 255, 255, 0.88);
  backdrop-filter: blur(16px);
  box-shadow: 0 12px 28px rgba(15, 23, 42, 0.08);
}

.toolbar-main-row {
  display: grid;
  grid-template-columns: auto minmax(320px, 1fr) auto auto;
  gap: 0.75rem;
  align-items: center;
}

.toolbar-sub-row {
  display: grid;
  grid-template-columns: auto 1fr auto;
  gap: 0.75rem;
  align-items: center;
}

.toolbar-advanced-toggle {
  border: none;
  border-radius: 10px;
  padding: 0.4rem 0.7rem;
  background: rgba(15, 23, 42, 0.06);
  color: var(--color-text-secondary, #64748b);
  font-size: 0.82rem;
  cursor: pointer;
  white-space: nowrap;
  transition: background 0.15s;
}
.toolbar-advanced-toggle:hover {
  background: rgba(15, 23, 42, 0.12);
}

.toolbar-advanced-row {
  padding-top: 0.25rem;
}

.toolbar-compact-status {
  display: flex;
  gap: 0.75rem;
  align-items: center;
}

.toolbar-manual-refresh {
  border: 1px solid rgba(148, 163, 184, 0.3);
  border-radius: 8px;
  padding: 0.25rem 0.6rem;
  background: rgba(37, 99, 235, 0.08);
  color: var(--color-accent, #2563eb);
  font-size: 0.82rem;
  cursor: pointer;
  transition: background 0.15s, border-color 0.15s;
  white-space: nowrap;
}

.toolbar-manual-refresh:hover:not(:disabled) {
  background: rgba(37, 99, 235, 0.16);
  border-color: var(--color-accent, #2563eb);
}

.toolbar-manual-refresh:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.toolbar-title {
  display: grid;
  gap: 0.15rem;
  min-width: 0;
}

.toolbar-title strong {
  color: var(--color-text-primary);
}

.toolbar-search-field {
  margin-bottom: 0;
  position: relative;
}

.toolbar-search-field input {
  min-width: 0;
  flex: 1 1 260px;
}

.toolbar-settings,
.toolbar-history-actions,
.toolbar-select-group,
.field {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.toolbar-settings,
.toolbar-history-actions {
  justify-content: flex-end;
}

.toolbar-sub-row {
  grid-template-columns: auto 1fr auto;
}

.history-ribbon {
  display: grid;
  gap: 0.35rem;
  position: relative;

.history-cleanup-note {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.history-cleanup-button {
  border: none;
  border-radius: 999px;
  padding: 0.15rem 0.55rem;
  background: var(--color-bg-surface-alt);
  color: var(--color-text-heading);
  cursor: pointer;
  font-size: var(--text-sm);
}
}

.history-chip-row {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(124px, 1fr));
  gap: 0.5rem;
  max-height: 10.5rem;
  overflow-y: auto;
  padding-right: 0.25rem;
  mask-image: linear-gradient(to bottom, transparent, black 0.5rem, black calc(100% - 0.5rem), transparent);
  -webkit-mask-image: linear-gradient(to bottom, transparent, black 0.5rem, black calc(100% - 0.5rem), transparent);
}

.history-chip {
  display: grid;
  gap: 0.1rem;
  min-width: 124px;
  padding: 0.5rem 0.65rem;
  border-radius: 14px;
  border: 1px solid rgba(148, 163, 184, 0.2);
  background: rgba(248, 250, 252, 0.96);
  text-align: left;
}

.history-chip span,
.history-chip strong,
.history-chip small {
  display: block;
}

.history-chip strong {
  color: var(--color-text-primary);
}

.search-dropdown,
.context-menu {
  border-radius: 14px;
  background: rgba(255, 255, 255, 0.98);
  border: 1px solid rgba(148, 163, 184, 0.18);
  box-shadow: 0 14px 40px rgba(15, 23, 42, 0.14);
}

.search-dropdown {
  position: absolute;
  z-index: 20;
  top: calc(100% + 0.45rem);
  left: 0;
  width: min(480px, 100%);
  max-height: min(26rem, calc(100vh - 7rem));
  padding: 0.8rem;
  overflow: hidden;
}

.search-modal-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  margin-bottom: 0.75rem;
  color: #0f172a;
  font-weight: 600;
}

.search-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: grid;
  gap: 0.35rem;
  max-height: min(18rem, calc(100vh - 12rem));
  overflow-y: auto;
  padding-right: 0.2rem;
}

.search-list li {
  padding: 0.6rem 0.7rem;
  border-radius: 12px;
  cursor: pointer;
  background: rgba(248, 250, 252, 0.96);
}

.search-list li:hover {
  background: rgba(226, 232, 240, 0.8);
}

.search-list li.search-item-selected {
  background: rgba(37, 99, 235, 0.1);
  outline: 2px solid rgba(37, 99, 235, 0.35);
  outline-offset: -2px;
}

.context-menu {
  position: fixed;
  z-index: 80;
  padding: 0.25rem;
}

.result-name {
  color: var(--color-text-primary);
  font-weight: 600;
}

.result-code {
  color: var(--color-text-secondary);
  font-size: 0.86rem;
}

.close-btn,
.field button,
.context-menu button {
  border: none;
  border-radius: 10px;
  cursor: pointer;
}

.field input,
.field select,
.field button,
.context-menu button,
.close-btn {
  padding: 0.55rem 0.75rem;
}

.field input,
.field select {
  border: 1px solid rgba(148, 163, 184, 0.4);
  background: rgba(255, 255, 255, 0.9);
}

.field button,
.context-menu button,
.close-btn {
  background: linear-gradient(135deg, #2563eb, #38bdf8);
  color: #fff;
}

.error-text {
  color: #b91c1c;
}

.history-chip-row::-webkit-scrollbar,
.search-list::-webkit-scrollbar {
  width: 8px;
}

.history-chip-row::-webkit-scrollbar-thumb,
.search-list::-webkit-scrollbar-thumb {
  border-radius: 999px;
  background: rgba(148, 163, 184, 0.45);
}

.text-rise { color: #ef4444 !important; }
.text-fall { color: #22c55e !important; }

@media (max-width: 960px) {
  .toolbar-main-row,
  .toolbar-sub-row {
    grid-template-columns: 1fr;
  }

  .toolbar-settings,
  .toolbar-history-actions {
    justify-content: flex-start;
  }

  .search-dropdown {
    width: 100%;
  }
}
</style>