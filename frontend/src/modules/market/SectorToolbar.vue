<script setup>
const boardType = defineModel('boardType', { type: String, default: 'concept' })
const sort = defineModel('sort', { type: String, default: 'strength' })
const compareWindow = defineModel('compareWindow', { type: String, default: '10d' })

const props = defineProps({
  boardOptions: { type: Array, default: () => [] },
  sortOptions: { type: Array, default: () => [] },
  compareWindowOptions: { type: Array, default: () => [] },
  boardCountText: { type: String, default: '' },
  snapshotTime: { type: String, default: '' },
  refreshLoading: { type: Boolean, default: false }
})

const emit = defineEmits(['board-change', 'sort-change', 'window-change', 'refresh'])

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

const onBoardChange = e => { boardType.value = e.target.value; emit('board-change') }
const onSortChange = e => { sort.value = e.target.value; emit('sort-change') }
const onWindowChange = e => { compareWindow.value = e.target.value; emit('window-change') }
</script>

<template>
  <section class="board-toolbar">
    <label class="toolbar-field">
      <span>维度</span>
      <select :value="boardType" @change="onBoardChange">
        <option v-for="option in boardOptions" :key="option.value" :value="option.value">{{ option.label }}</option>
      </select>
    </label>
    <label class="toolbar-field">
      <span>排序</span>
      <select :value="sort" @change="onSortChange">
        <option v-for="option in sortOptions" :key="option.value" :value="option.value">{{ option.label }}</option>
      </select>
    </label>
    <label class="toolbar-field">
      <span>窗口</span>
      <select :value="compareWindow" @change="onWindowChange">
        <option v-for="option in compareWindowOptions" :key="option.value" :value="option.value">{{ option.label }}</option>
      </select>
    </label>
    <div class="toolbar-meta">
      <span>{{ boardCountText }}</span>
      <span class="toolbar-ts">{{ formatDate(snapshotTime) }}</span>
      <button class="toolbar-btn" type="button" @click="emit('refresh')" :disabled="refreshLoading">⟳</button>
    </div>
  </section>
</template>

<style scoped>
.board-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px;
  padding: 6px 10px;
  background: #1a2233;
  border: 1px solid #334155;
}
.toolbar-field {
  display: flex;
  flex: 0 1 auto;
  align-items: center;
  min-width: 0;
  gap: 4px;
}
.toolbar-field span {
  font-size: 11px;
  color: #b2bccf;
  line-height: 1.3;
  white-space: nowrap;
}
.toolbar-field select {
  height: 26px;
  min-width: 92px;
  max-width: 100%;
  padding: 0 6px;
  border: 1px solid #334155;
  background: #111827;
  color: #e6eaf2;
  font-size: 12px;
  font-family: inherit;
  line-height: 1.3;
}
.toolbar-field select:hover,
.toolbar-field select:focus { border-color: #91a4c3; outline: none; }
.toolbar-meta {
  margin-left: auto;
  display: flex;
  flex: 1 1 280px;
  flex-wrap: wrap;
  align-items: center;
  justify-content: flex-end;
  min-width: 0;
  gap: 12px;
  font-size: 11px;
  color: #b2bccf;
  line-height: 1.3;
}
.toolbar-ts { color: #98a6bf; overflow-wrap: anywhere; }
.toolbar-btn {
  width: 26px;
  height: 26px;
  border: 1px solid #334155;
  background: #111827;
  color: #b2bccf;
  cursor: pointer;
  font-size: 14px;
  display: flex;
  align-items: center;
  justify-content: center;
}
.toolbar-btn:hover:not(:disabled) { border-color: #91a4c3; color: #e6eaf2; }
.toolbar-btn:disabled { opacity: 0.45; cursor: not-allowed; }
@media (max-width: 900px) {
  .toolbar-meta {
    margin-left: 0;
    justify-content: flex-start;
    flex-basis: 100%;
  }
}
</style>
