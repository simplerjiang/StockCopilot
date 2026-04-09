<script setup>
import { ref } from 'vue'

const props = defineProps({
  session: { type: Object, default: null },
  isRunning: { type: Boolean, default: false },
  symbol: { type: String, default: '' }
})
const emit = defineEmits(['submit', 'cancel'])

const prompt = ref('')
const continuationMode = ref('ContinueSession')

const modes = [
  { value: 'ContinueSession', label: '延续当前会话', desc: '基于已有分析继续深入' },
  { value: 'NewSession', label: '新建会话', desc: '从零开始全新分析' },
  { value: 'RefreshNews', label: '仅刷新新闻', desc: '保留其他分析，仅更新新闻' },
  { value: 'RerunRisk', label: '重跑风险评估', desc: '保留交易方案，重新评估风险' }
]

function handleSubmit() {
  if (!prompt.value.trim() || props.isRunning) return
  emit('submit', {
    prompt: prompt.value.trim(),
    options: { continuationMode: continuationMode.value }
  })
  prompt.value = ''
}

function handleKeydown(e) {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault()
    if (!props.isRunning) handleSubmit()
  }
}
</script>

<template>
  <div class="wb-composer">
    <!-- Continuation mode selector -->
    <div v-if="session" class="wb-mode-row">
      <select v-model="continuationMode" class="wb-mode-select">
        <option v-for="m in modes" :key="m.value" :value="m.value">{{ m.label }}</option>
      </select>
      <span class="wb-mode-desc">{{ modes.find(m => m.value === continuationMode)?.desc }}</span>
    </div>

    <!-- Input area -->
    <div class="wb-input-row">
      <textarea
        v-model="prompt"
        class="wb-input"
        :placeholder="isRunning ? '分析进行中…点击右侧按钮可取消' : (session ? '追问或调整分析方向…' : `输入 ${symbol || '股票'} 研究指令…`)"
        rows="1"
        @keydown="handleKeydown"
      />
      <button
        v-if="isRunning"
        class="wb-cancel-btn"
        title="取消分析"
        @click="$emit('cancel')"
      >
        ■
      </button>
      <button
        v-else
        class="wb-send-btn"
        :disabled="!prompt.trim()"
        title="发送"
        @click="handleSubmit"
      >
        ▶
      </button>
    </div>
  </div>
</template>

<style scoped>
.wb-composer {
  border-top: 1px solid var(--color-border-light);
  background: var(--color-bg-surface-alt);
  padding: 8px 10px;
}

.wb-mode-row {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 6px;
}
.wb-mode-select {
  font-size: 13px;
  padding: 2px 6px;
  border: 1px solid var(--color-border-light);
  border-radius: 4px;
  background: var(--color-bg-surface);
  color: var(--color-text-body);
  outline: none;
}
.wb-mode-desc {
  font-size: 12px;
  color: var(--color-text-secondary);
}

.wb-input-row {
  display: flex;
  align-items: flex-end;
  gap: 6px;
}
.wb-input {
  flex: 1;
  resize: none;
  border: 1px solid var(--color-border-light);
  border-radius: 6px;
  background: var(--color-bg-surface);
  color: var(--color-text-body);
  padding: 6px 10px;
  font-size: 14px;
  font-family: inherit;
  line-height: 1.4;
  outline: none;
  min-height: 32px;
  max-height: 80px;
}
.wb-input:focus {
  border-color: var(--color-accent);
}
.wb-input::placeholder {
  color: var(--color-text-secondary);
}
.wb-input:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.wb-send-btn {
  width: 32px;
  height: 32px;
  border: 1px solid var(--color-border-light);
  border-radius: 6px;
  background: var(--color-accent);
  color: #fff;
  font-size: 16px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: opacity 0.15s;
  flex-shrink: 0;
}
.wb-send-btn:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}
.wb-send-btn:not(:disabled):hover {
  opacity: 0.85;
}

.wb-cancel-btn {
  width: 32px;
  height: 32px;
  border: 1px solid #ef4444;
  border-radius: 6px;
  background: #ef4444;
  color: #fff;
  font-size: 14px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: opacity 0.15s;
  flex-shrink: 0;
}
.wb-cancel-btn:hover {
  opacity: 0.85;
}
</style>
