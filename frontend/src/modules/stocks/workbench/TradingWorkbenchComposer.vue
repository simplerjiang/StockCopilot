<script setup>
import { ref } from 'vue'

const props = defineProps({
  session: { type: Object, default: null },
  isRunning: { type: Boolean, default: false },
  symbol: { type: String, default: '' }
})
const emit = defineEmits(['submit'])

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
    handleSubmit()
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
        :placeholder="session ? '追问或调整分析方向…' : `输入 ${symbol || '股票'} 研究指令…`"
        rows="1"
        :disabled="isRunning"
        @keydown="handleKeydown"
      />
      <button
        class="wb-send-btn"
        :disabled="!prompt.trim() || isRunning"
        :title="isRunning ? '研究执行中' : '发送'"
        @click="handleSubmit"
      >
        {{ isRunning ? '⏳' : '▶' }}
      </button>
    </div>
  </div>
</template>

<style scoped>
.wb-composer {
  border-top: 1px solid var(--wb-border, #2a2d35);
  background: var(--wb-header-bg, #1e2128);
  padding: 8px 10px;
}

.wb-mode-row {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 6px;
}
.wb-mode-select {
  font-size: 11px;
  padding: 2px 6px;
  border: 1px solid var(--wb-border, #2a2d35);
  border-radius: 4px;
  background: var(--wb-bg, #1a1d23);
  color: var(--wb-text, #e1e4ea);
  outline: none;
}
.wb-mode-desc {
  font-size: 10px;
  color: var(--wb-text-muted, #8b8fa3);
}

.wb-input-row {
  display: flex;
  align-items: flex-end;
  gap: 6px;
}
.wb-input {
  flex: 1;
  resize: none;
  border: 1px solid var(--wb-border, #2a2d35);
  border-radius: 6px;
  background: var(--wb-bg, #1a1d23);
  color: var(--wb-text, #e1e4ea);
  padding: 6px 10px;
  font-size: 12px;
  font-family: inherit;
  line-height: 1.4;
  outline: none;
  min-height: 32px;
  max-height: 80px;
}
.wb-input:focus {
  border-color: var(--wb-accent, #5b9cf6);
}
.wb-input::placeholder {
  color: var(--wb-text-muted, #8b8fa3);
}
.wb-input:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.wb-send-btn {
  width: 32px;
  height: 32px;
  border: 1px solid var(--wb-border, #2a2d35);
  border-radius: 6px;
  background: var(--wb-accent, #5b9cf6);
  color: #fff;
  font-size: 14px;
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
</style>
