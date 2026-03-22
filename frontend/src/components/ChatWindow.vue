<script setup>
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { marked } from 'marked'
import DOMPurify from 'dompurify'

defineOptions({ name: 'ChatWindow' })

const props = defineProps({
  title: { type: String, default: '聊天助手' },
  subtitle: { type: String, default: '' },
  placeholder: { type: String, default: '请输入你的问题' },
  endpoint: { type: String, default: '/api/llm/chat/stream/openai' },
  useInternet: { type: Boolean, default: true },
  presets: { type: Array, default: () => [] },
  buildPrompt: { type: Function, default: content => content },
  historyKey: { type: String, default: '' },
  enableHistory: { type: Boolean, default: false },
  historyStorageKey: { type: String, default: 'chat_history_map' },
  historyAdapter: { type: Object, default: null },
  showNewChat: { type: Boolean, default: false },
  expandable: { type: Boolean, default: false },
  expandedStorageKey: { type: String, default: 'chat_expanded' },
  maxHeight: { type: String, default: '320px' },
  expandedHeight: { type: String, default: '600px' },
  emptyText: { type: String, default: '可以直接提问，或使用上方快捷按钮。' }
})

const chatInput = ref('')
const chatMessages = ref([])
const chatLoading = ref(false)
const chatError = ref('')
const chatMessagesRef = ref(null)
const chatExpanded = ref(props.expandable ? localStorage.getItem(props.expandedStorageKey) !== 'false' : true)
const chatHistoryMap = ref({})
let historySaveQueue = Promise.resolve()

const isExpanded = computed(() => (props.expandable ? chatExpanded.value : true))
const messageStyle = computed(() => ({
  '--chat-max-height': props.maxHeight,
  '--chat-expanded-height': props.expandedHeight
}))

marked.setOptions({ breaks: true })

const renderMarkdown = content => {
  const source = content || ''
  return DOMPurify.sanitize(marked.parse(source))
}

const THINK_BLOCK_PATTERN = /<think>[\s\S]*?<\/think>/gi
const REASONING_SECTION_PATTERN = /(^|\n)#{0,6}\s*(思考过程|推理过程|reasoning|analysis|chain of thought|chain-of-thought)[^\n]*(\n[\s\S]*)?$/i

const sanitizeAssistantContent = content => {
  const source = String(content || '')
    .replace(THINK_BLOCK_PATTERN, '')
    .replace(REASONING_SECTION_PATTERN, '')
    .replace(/\n{3,}/g, '\n\n')
    .trim()

  return source
}

const sanitizeStreamingAssistantContent = content => {
  const source = String(content || '').replace(THINK_BLOCK_PATTERN, '')
  return source.trimStart()
}

const cloneMessages = messages =>
  (Array.isArray(messages) ? messages : []).map(item => ({
    role: item.role,
    content: item.role === 'assistant' ? sanitizeAssistantContent(item.content) : String(item.content || ''),
    timestamp: item.timestamp || new Date().toISOString()
  }))

const scrollChatToBottom = () => {
  const el = chatMessagesRef.value
  if (!el) return
  el.scrollTop = el.scrollHeight
}

const normalizedHistoryKey = computed(() => String(props.historyKey || '').trim().toLowerCase())
const hasHistoryAdapter = computed(() => props.historyAdapter && typeof props.historyAdapter.load === 'function')

const loadChatHistoryMap = () => {
  if (hasHistoryAdapter.value) return
  try {
    const raw = localStorage.getItem(props.historyStorageKey)
    chatHistoryMap.value = raw ? JSON.parse(raw) : {}
  } catch {
    chatHistoryMap.value = {}
  }
}

const persistChatHistoryMap = () => {
  if (hasHistoryAdapter.value) return
  localStorage.setItem(props.historyStorageKey, JSON.stringify(chatHistoryMap.value))
}

const loadChatHistory = async key => {
  if (!key) return []
  if (hasHistoryAdapter.value) {
    try {
      const saved = await props.historyAdapter.load(key)
      return Array.isArray(saved) ? saved : []
    } catch {
      return []
    }
  }
  const saved = chatHistoryMap.value?.[key]
  return Array.isArray(saved) ? saved : []
}

const saveChatHistory = async (key, messages) => {
  if (!key) return
  if (hasHistoryAdapter.value) {
    await props.historyAdapter.save(key, messages)
    return
  }
  chatHistoryMap.value = {
    ...chatHistoryMap.value,
    [key]: messages
  }
  persistChatHistoryMap()
}

const enqueueHistorySave = (key, messages, { silent = true } = {}) => {
  if (!key) return Promise.resolve()

  const snapshot = cloneMessages(messages)
  historySaveQueue = historySaveQueue
    .catch(() => undefined)
    .then(async () => {
      try {
        await saveChatHistory(key, snapshot)
      } catch (error) {
        if (!silent) {
          chatError.value = error.message || '对话保存失败'
        }
      }
    })

  return historySaveQueue
}

const createNewChat = () => {
  const key = normalizedHistoryKey.value
  chatMessages.value = []
  if (props.enableHistory && key) {
    enqueueHistorySave(key, [], { silent: false })
  }
}

const handleKeydown = event => {
  if (event.key !== 'Enter') return
  if (event.shiftKey || event.isComposing) return
  event.preventDefault()
  sendChat()
}

const sendChat = async (presetPrompt = '') => {
  const promptOverride = typeof presetPrompt === 'string' ? presetPrompt : ''
  const content = (promptOverride || chatInput.value).trim()
  if (!content || chatLoading.value) return
  chatError.value = ''
  chatLoading.value = true

  let prompt = content
  if (props.buildPrompt) {
    try {
      prompt = props.buildPrompt(content)
    } catch {
      prompt = content
    }
  }
  if (typeof prompt !== 'string' || !prompt.trim()) {
    prompt = content
  }

  chatMessages.value.push({ role: 'user', content, timestamp: new Date().toISOString() })
  if (props.enableHistory) {
    const key = normalizedHistoryKey.value
    if (key) {
      enqueueHistorySave(key, chatMessages.value, { silent: false })
    }
  }
  chatInput.value = ''

  try {
    const response = await fetch(props.endpoint, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ prompt, useInternet: props.useInternet })
    })

    if (!response.ok) {
      const message = await response.text()
      throw new Error(message || '请求失败')
    }

    const assistantMessage = { role: 'assistant', content: '', timestamp: new Date().toISOString() }
    chatMessages.value.push(assistantMessage)

    if (!response.body) {
      const data = await response.json()
      assistantMessage.content = sanitizeAssistantContent(data.content || '')
      if (props.enableHistory) {
        const key = normalizedHistoryKey.value
        if (key) {
          await enqueueHistorySave(key, chatMessages.value, { silent: false })
        }
      }
      return
    }

    const reader = response.body.getReader()
    const decoder = new TextDecoder('utf-8')
    let buffer = ''
    let rawAssistantContent = ''
    while (true) {
      const { value, done } = await reader.read()
      if (done) break
      buffer += decoder.decode(value, { stream: true })
      const parts = buffer.split('\n\n')
      buffer = parts.pop() || ''
      for (const part of parts) {
        const line = part.trim()
        if (!line.startsWith('data:')) continue
        const payload = line.slice(5).trim()
        if (!payload || payload === '[DONE]') continue
        rawAssistantContent += payload
        assistantMessage.content = sanitizeStreamingAssistantContent(rawAssistantContent)
      }
    }

    assistantMessage.content = sanitizeAssistantContent(rawAssistantContent)
    if (props.enableHistory) {
      const key = normalizedHistoryKey.value
      if (key) {
        await enqueueHistorySave(key, chatMessages.value, { silent: false })
      }
    }
  } catch (err) {
    chatError.value = err.message || '请求失败'
  } finally {
    chatLoading.value = false
  }
}

watch(
  chatMessages,
  async value => {
    if (props.enableHistory && !chatLoading.value) {
      const key = normalizedHistoryKey.value
      if (key) {
        enqueueHistorySave(key, value)
      }
    }
    await nextTick()
    scrollChatToBottom()
  },
  { deep: true }
)

watch(
  () => normalizedHistoryKey.value,
  async (newKey, oldKey) => {
    if (!props.enableHistory) return
    if (oldKey) {
      await enqueueHistorySave(oldKey, chatMessages.value)
    }
    chatMessages.value = newKey ? await loadChatHistory(newKey) : []
  }
)

watch(chatExpanded, async value => {
  if (!props.expandable) return
  localStorage.setItem(props.expandedStorageKey, String(value))
  await nextTick()
  scrollChatToBottom()
})

onMounted(() => {
  if (props.enableHistory) {
    loadChatHistoryMap()
    const key = normalizedHistoryKey.value
    if (key) {
      loadChatHistory(key).then(messages => {
        chatMessages.value = messages
        scrollChatToBottom()
      })
      return
    }
  }
  scrollChatToBottom()
})

defineExpose({
  chatInput,
  chatMessages,
  chatLoading,
  chatError,
  sendChat,
  createNewChat
})
</script>

<template>
  <div class="chat-window">
    <div class="chat-header">
      <div>
        <h3>{{ title }}</h3>
        <p v-if="subtitle" class="muted">{{ subtitle }}</p>
      </div>
      <div class="chat-header-actions">
        <slot name="header-extra" />
        <button v-if="showNewChat" class="chat-new" @click="createNewChat">新建对话</button>
        <button v-if="expandable" class="chat-toggle" @click="chatExpanded = !chatExpanded">
          {{ chatExpanded ? '收起' : '展开' }}
        </button>
      </div>
    </div>

    <div v-if="presets.length" class="preset-actions">
      <button
        v-for="item in presets"
        :key="item.label"
        class="preset-button"
        @click="sendChat(item.prompt)"
        :disabled="chatLoading"
      >
        {{ item.label }}
      </button>
    </div>

    <div class="chat-panel">
      <div
        class="chat-messages"
        :class="{ expanded: isExpanded }"
        :style="messageStyle"
        ref="chatMessagesRef"
      >
        <div v-if="!chatMessages.length" class="muted">{{ emptyText }}</div>
        <div
          v-for="(item, idx) in chatMessages"
          :key="`${item.role}-${idx}`"
          class="chat-message"
          :class="item.role"
        >
          <div class="chat-role">{{ item.role === 'user' ? '你' : '助手' }}</div>
          <div class="chat-content" v-html="renderMarkdown(item.content)" />
        </div>
      </div>

      <div v-if="chatLoading" class="chat-loading" aria-live="polite">
        <span class="dot" />
        <span class="dot" />
        <span class="dot" />
        <span>助手正在思考...</span>
      </div>

      <div class="chat-input">
        <textarea
          v-model="chatInput"
          rows="4"
          :placeholder="placeholder"
          @keydown="handleKeydown"
        />
        <button class="chat-send" @click="sendChat()" :disabled="chatLoading || !chatInput.trim()">
          {{ chatLoading ? '发送中...' : '发送' }}
        </button>
      </div>

      <p v-if="chatError" class="muted">{{ chatError }}</p>
    </div>
  </div>
</template>

<style scoped>
.chat-window {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.chat-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}

.chat-header-actions {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.preset-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
}

.preset-button {
  border-radius: 999px;
  border: none;
  padding: 0.5rem 1rem;
  background: #e2e8f0;
  color: #0f172a;
  cursor: pointer;
}

.preset-button:disabled {
  background: #94a3b8;
  color: #f8fafc;
  cursor: not-allowed;
}

.chat-panel {
  padding: 1rem;
  border-radius: 12px;
  background: rgba(248, 250, 252, 0.9);
  border: 1px solid rgba(148, 163, 184, 0.2);
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.chat-messages {
  display: grid;
  gap: 0.75rem;
  max-height: var(--chat-max-height);
  overflow: auto;
  transition: max-height 0.2s ease;
}

.chat-messages.expanded {
  max-height: var(--chat-expanded-height);
}

.chat-message {
  padding: 0.6rem 0.75rem;
  border-radius: 10px;
  background: #ffffff;
  border: 1px solid rgba(226, 232, 240, 0.9);
}

.chat-message.user {
  border-left: 3px solid #2563eb;
}

.chat-message.assistant {
  border-left: 3px solid #22c55e;
}

.chat-role {
  font-size: 0.75rem;
  color: #64748b;
  margin-bottom: 0.2rem;
}

.chat-content {
  white-space: pre-wrap;
  color: #0f172a;
}

.chat-content :deep(p) {
  margin: 0 0 0.5rem;
}

.chat-content :deep(ul),
.chat-content :deep(ol) {
  margin: 0 0 0.5rem 1.25rem;
  padding: 0;
}

.chat-content :deep(code) {
  font-family: 'JetBrains Mono', 'Fira Code', Consolas, monospace;
  background: rgba(148, 163, 184, 0.2);
  padding: 0.1rem 0.25rem;
  border-radius: 4px;
}

.chat-content :deep(pre) {
  background: rgba(15, 23, 42, 0.08);
  padding: 0.6rem;
  border-radius: 8px;
  overflow-x: auto;
}

.chat-loading {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  color: #64748b;
  font-size: 0.85rem;
}

.chat-loading .dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: #2563eb;
  display: inline-block;
  animation: chat-bounce 0.9s infinite ease-in-out;
}

.chat-loading .dot:nth-child(2) {
  animation-delay: 0.15s;
}

.chat-loading .dot:nth-child(3) {
  animation-delay: 0.3s;
}

@keyframes chat-bounce {
  0%,
  80%,
  100% {
    transform: translateY(0);
    opacity: 0.5;
  }
  40% {
    transform: translateY(-4px);
    opacity: 1;
  }
}

.chat-input {
  display: grid;
  gap: 0.5rem;
}

.chat-input textarea {
  resize: vertical;
  border-radius: 10px;
  border: 1px solid rgba(148, 163, 184, 0.4);
  padding: 0.6rem 0.75rem;
  font-family: inherit;
}

.chat-send {
  align-self: flex-end;
  border-radius: 10px;
  border: none;
  padding: 0.5rem 1rem;
  background: linear-gradient(135deg, #2563eb, #38bdf8);
  color: #ffffff;
  cursor: pointer;
}

.chat-send:disabled {
  background: #94a3b8;
  cursor: not-allowed;
}

.chat-new,
.chat-toggle {
  background: #e2e8f0;
  color: #1f2937;
  border: none;
  padding: 0.3rem 0.75rem;
  border-radius: 999px;
  cursor: pointer;
}
</style>
