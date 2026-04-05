<script setup>
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'

const emit = defineEmits(['settings-saved'])

const providerPresets = {
  default: {
    label: 'Default 通道',
    baseUrl: 'https://api.bltcy.ai',
    model: 'gemini-3.1-flash-lite-preview-thinking-high'
  },
  gemini_official: {
    label: 'Gemini 官方',
    baseUrl: 'https://generativelanguage.googleapis.com/v1beta/openai/',
    model: 'gemini-3.1-flash-lite-preview-thinking-high'
  },
  antigravity: {
    label: 'Antigravity（免费 Claude/Gemini）',
    baseUrl: '',
    model: 'gemini-3-flash',
    providerType: 'antigravity'
  },
  ollama: {
    label: 'Ollama 本地模型',
    baseUrl: 'http://localhost:11434',
    model: 'gemma4:e4b',
    providerType: 'ollama'
  }
}

const providerOptions = Object.entries(providerPresets).map(([value, preset]) => ({
  value,
  label: preset.label
}))

const username = ref('')
const password = ref('')
const token = ref('local-bypass')
const loginError = ref('')
const loginLoading = ref(false)

const activeProviderKey = ref('default')
const provider = ref('default')
const apiKey = ref('')
const tavilyApiKey = ref('')
const baseUrl = ref(providerPresets.default.baseUrl)
const model = ref(providerPresets.default.model)
const organization = ref('')
const project = ref('')
const enabled = ref(true)
const apiKeyMasked = ref('')
const hasApiKey = ref(false)
const tavilyApiKeyMasked = ref('')
const hasTavilyApiKey = ref(false)
const settingsLoading = ref(false)
const settingsError = ref('')
const saveMessage = ref('')
const systemPrompt = ref('')
const forceChinese = ref(false)

// Antigravity OAuth 状态
const antigravityModels = ref([])
const antigravityAuthStatus = ref('idle')
const antigravityAuthLoading = ref(false)
const antigravityAuthError = ref('')
const antigravityEmail = ref('')

// Ollama 服务状态
const ollamaStatus = ref('unknown')
const ollamaModels = ref([])
const ollamaMsg = ref('')
const keepAlive = ref(localStorage.getItem('ollama-keepalive') === 'true')
const pullModelName = ref('')
const pulling = ref(false)
const pullMsg = ref('')
let keepAliveTimer = null

const isLoggedIn = computed(() => Boolean(token.value))
const isAntigravity = computed(() => provider.value === 'antigravity')
const isOllamaProvider = computed(() => provider.value === 'ollama')
const modelPlaceholder = computed(() => isOllamaProvider.value ? '例如: gemma4:latest' : 'gpt-4o-mini')
const providerKeys = Object.keys(providerPresets)

const authHeaders = () => ({
  Authorization: `Bearer ${token.value}`
})

const handleUnauthorized = message => {
  logout()
  loginError.value = message || '登录已过期，请重新登录'
}

const login = async () => {
  loginLoading.value = true
  loginError.value = ''
  saveMessage.value = ''

  try {
    const response = await fetch('/api/admin/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username: username.value, password: password.value })
    })

    if (!response.ok) {
      throw new Error('登录失败')
    }

    const data = await response.json()
    token.value = data.token
    localStorage.setItem('admin_token', token.value)
    const loaded = await loadActiveProvider()
    if (loaded) {
      provider.value = activeProviderKey.value
      await loadSettings()
      await loadNewsCleansing()
      checkOllama()
    }
  } catch (error) {
    loginError.value = error.message || '登录失败'
  } finally {
    loginLoading.value = false
  }
}

const logout = () => {
  token.value = ''
  localStorage.removeItem('admin_token')
}

const loadAntigravityModels = async () => {
  try {
    const response = await fetch('/api/admin/antigravity/models', {
      headers: authHeaders()
    })
    if (response.ok) {
      antigravityModels.value = await response.json()
    }
  } catch {
    // 静默失败
  }
}

const startAntigravityAuth = async () => {
  antigravityAuthLoading.value = true
  antigravityAuthError.value = ''

  try {
    const startResponse = await fetch('/api/admin/antigravity/auth-start', {
      method: 'POST',
      headers: authHeaders()
    })

    if (!startResponse.ok) {
      throw new Error('启动 Google 授权失败')
    }

    const { authUrl, port } = await startResponse.json()

    window.open(authUrl, '_blank')
    antigravityAuthStatus.value = 'waiting'

    const completeResponse = await fetch('/api/admin/antigravity/auth-complete', {
      method: 'POST',
      headers: { ...authHeaders(), 'Content-Type': 'application/json' },
      body: JSON.stringify({ port })
    })

    if (!completeResponse.ok) {
      const errorData = await completeResponse.json().catch(() => ({}))
      throw new Error(errorData.message || '授权完成失败')
    }

    const result = await completeResponse.json()

    antigravityEmail.value = result.email || ''
    apiKey.value = result.refreshToken
    project.value = result.projectId || 'rising-fact-p41fc'
    organization.value = result.email || ''
    antigravityAuthStatus.value = 'completed'

    await saveSettings()
  } catch (error) {
    antigravityAuthError.value = error.message || '授权失败'
    antigravityAuthStatus.value = 'error'
  } finally {
    antigravityAuthLoading.value = false
  }
}

const applyProviderPreset = selectedProvider => {
  const preset = providerPresets[selectedProvider] || providerPresets.default
  baseUrl.value = preset.baseUrl
  model.value = preset.model
  systemPrompt.value = ''
  forceChinese.value = true
  organization.value = ''
  project.value = ''
  enabled.value = true
  apiKeyMasked.value = ''
  hasApiKey.value = false
  // Tavily API Key is global — do not clear on provider switch

  if (selectedProvider === 'antigravity') {
    project.value = 'rising-fact-p41fc'
    loadAntigravityModels()
  }
}

async function checkOllama() {
  ollamaStatus.value = 'checking'
  ollamaMsg.value = ''
  try {
    const resp = await fetch('/api/admin/ollama/status', { headers: authHeaders() })
    if (resp.status === 401 || resp.status === 403) {
      handleUnauthorized()
      return
    }
    const data = await resp.json()
    if (data.status === 'not_running' && data.installed === false) {
      ollamaStatus.value = 'not_installed'
    } else {
      ollamaStatus.value = data.status === 'running' ? 'running' : 'not_running'
    }
    if (data.models?.models && data.status === 'running') {
      ollamaModels.value = data.models.models
    } else {
      ollamaModels.value = []
    }
  } catch (e) {
    ollamaStatus.value = 'error'
    ollamaMsg.value = e.message
  }
}

async function startOllama() {
  ollamaStatus.value = 'starting'
  ollamaMsg.value = '正在启动 Ollama...'
  try {
    const resp = await fetch('/api/admin/ollama/start', { method: 'POST', headers: authHeaders() })
    if (resp.status === 401 || resp.status === 403) {
      handleUnauthorized()
      return
    }
    const data = await resp.json()
    ollamaMsg.value = data.message
    if (data.success) {
      ollamaStatus.value = 'running'
      await checkOllama()
    } else {
      ollamaStatus.value = 'not_running'
    }
  } catch (e) {
    ollamaStatus.value = 'error'
    ollamaMsg.value = `启动请求失败: ${e.message}`
  }
}

async function stopOllama() {
  ollamaStatus.value = 'stopping'
  ollamaMsg.value = '正在停止 Ollama...'
  try {
    const resp = await fetch('/api/admin/ollama/stop', { method: 'POST', headers: authHeaders() })
    if (resp.status === 401 || resp.status === 403) { handleUnauthorized(); return }
    const data = await resp.json()
    ollamaMsg.value = data.message
    ollamaStatus.value = data.success ? 'not_running' : 'running'
    ollamaModels.value = []
  } catch (e) {
    ollamaMsg.value = `停止失败: ${e.message}`
    ollamaStatus.value = 'error'
  }
}

async function pullModel() {
  const name = pullModelName.value.trim()
  if (!name || pulling.value) return
  pulling.value = true
  pullMsg.value = `正在拉取 ${name}，大模型首次拉取可能需要数分钟...`
  try {
    const resp = await fetch('/api/admin/ollama/pull', {
      method: 'POST',
      headers: { ...authHeaders(), 'Content-Type': 'application/json' },
      body: JSON.stringify({ model: name })
    })
    if (resp.status === 401 || resp.status === 403) { handleUnauthorized(); return }
    const data = await resp.json()
    pullMsg.value = data.message
    if (data.success) {
      pullModelName.value = ''
      await checkOllama()
    }
  } catch (e) {
    pullMsg.value = `拉取异常: ${e.message}`
  } finally {
    pulling.value = false
  }
}

function formatSize(bytes) {
  if (!bytes) return ''
  const gb = bytes / (1024 * 1024 * 1024)
  return gb >= 1 ? `${gb.toFixed(1)} GB` : `${(bytes / (1024 * 1024)).toFixed(0)} MB`
}

function onKeepAliveChange() {
  localStorage.setItem('ollama-keepalive', keepAlive.value)
  if (keepAlive.value) {
    startKeepAlive()
  } else {
    stopKeepAlive()
  }
}

function startKeepAlive() {
  stopKeepAlive()
  keepAliveTimer = setInterval(async () => {
    try {
      const resp = await fetch('/api/admin/ollama/status', { headers: authHeaders() })
      const data = await resp.json()
      if (data.status !== 'running' && keepAlive.value) {
        ollamaMsg.value = '检测到 Ollama 离线，自动重启中...'
        await startOllama()
      }
    } catch { /* ignore check errors */ }
  }, 30000)
}

function stopKeepAlive() {
  if (keepAliveTimer) { clearInterval(keepAliveTimer); keepAliveTimer = null }
}

const loadActiveProvider = async () => {
  const response = await fetch('/api/admin/llm/settings/active', {
    headers: authHeaders()
  })

  if (response.status === 401 || response.status === 403) {
    handleUnauthorized()
    return false
  }

  if (!response.ok) {
    const message = await response.text()
    throw new Error(message || '获取激活通道失败')
  }

  const data = await response.json()
  activeProviderKey.value = data.activeProviderKey || 'default'
  return true
}

const loadSettings = async () => {
  if (!token.value) return
  settingsLoading.value = true
  settingsError.value = ''

  try {
    applyProviderPreset(provider.value)
    const response = await fetch(`/api/admin/llm/settings/${provider.value}`, {
      headers: authHeaders()
    })

    if (response.status === 401 || response.status === 403) {
      handleUnauthorized()
      return
    }

    if (response.status === 404) {
      applyProviderPreset(provider.value)
      settingsLoading.value = false
      return
    }

    if (!response.ok) {
      const message = await response.text()
      throw new Error(message || '获取配置失败')
    }

    const data = await response.json()
    baseUrl.value = data.baseUrl || baseUrl.value
    model.value = data.model || model.value
    systemPrompt.value = data.systemPrompt || ''
    forceChinese.value = data.forceChinese ?? false
    organization.value = data.organization || ''
    project.value = data.project || ''
    enabled.value = data.enabled ?? true
    apiKeyMasked.value = data.apiKeyMasked || ''
    hasApiKey.value = data.hasApiKey || false
    // Tavily is global — only overwrite display if this provider actually has one
    if (data.hasTavilyApiKey) {
      tavilyApiKeyMasked.value = data.tavilyApiKeyMasked || ''
      hasTavilyApiKey.value = true
    }

    if (provider.value === 'antigravity') {
      await loadAntigravityModels()
      if (data.organization) {
        antigravityEmail.value = data.organization
      }
    }
  } catch (error) {
    settingsError.value = error.message || '获取配置失败'
  } finally {
    settingsLoading.value = false
  }
}

const saveSettings = async () => {
  if (!token.value) return
  settingsLoading.value = true
  settingsError.value = ''
  saveMessage.value = ''

  try {
    const response = await fetch(`/api/admin/llm/settings/${provider.value}`, {
      method: 'PUT',
      headers: {
        ...authHeaders(),
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        apiKey: apiKey.value,
        tavilyApiKey: tavilyApiKey.value,
        baseUrl: baseUrl.value,
        model: model.value,
        systemPrompt: systemPrompt.value,
        forceChinese: forceChinese.value,
        organization: organization.value,
        project: project.value,
        enabled: enabled.value,
        providerType: providerPresets[provider.value]?.providerType || undefined
      })
    })

    if (response.status === 401 || response.status === 403) {
      handleUnauthorized()
      return
    }

    if (!response.ok) {
      const message = await response.text()
      throw new Error(message || '保存失败')
    }

    const data = await response.json()
    apiKeyMasked.value = data.apiKeyMasked || ''
    hasApiKey.value = data.hasApiKey || false
    tavilyApiKeyMasked.value = data.tavilyApiKeyMasked || ''
    hasTavilyApiKey.value = data.hasTavilyApiKey || false
    apiKey.value = ''
    tavilyApiKey.value = ''

    // 自动激活刚保存的通道（如果与当前激活通道不同）
    if (provider.value !== activeProviderKey.value) {
      activeProviderKey.value = provider.value
      await saveActiveProvider()
      saveMessage.value = `✅ 配置已保存并已切换激活通道为「${providerPresets[provider.value]?.label || provider.value}」`
    } else {
      saveMessage.value = '✅ 配置已保存'
    }
    emit('settings-saved', data)
  } catch (error) {
    settingsError.value = error.message || '保存失败'
  } finally {
    settingsLoading.value = false
  }
}

const saveActiveProvider = async () => {
  if (!token.value) return

  settingsLoading.value = true
  settingsError.value = ''
  saveMessage.value = ''

  try {
    const response = await fetch('/api/admin/llm/settings/active', {
      method: 'PUT',
      headers: {
        ...authHeaders(),
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({ activeProviderKey: activeProviderKey.value })
    })

    if (response.status === 401 || response.status === 403) {
      handleUnauthorized()
      return
    }

    if (!response.ok) {
      const message = await response.text()
      throw new Error(message || '切换激活通道失败')
    }

    const data = await response.json()
    activeProviderKey.value = data.activeProviderKey || activeProviderKey.value
    saveMessage.value = '激活通道已切换'
  } catch (error) {
    settingsError.value = error.message || '切换激活通道失败'
  } finally {
    settingsLoading.value = false
  }
}

const testLoading = ref(false)
const testResult = ref('')
const testError = ref('')
const testAbortController = ref(null)

const testConnection = async () => {
  if (!token.value) return
  testLoading.value = true
  testResult.value = ''
  testError.value = ''

  const controller = new AbortController()
  testAbortController.value = controller
  const timeoutMs = isOllamaProvider.value ? 60000 : 30000
  const timeoutId = setTimeout(() => controller.abort(), timeoutMs)

  try {
    const testProvider = provider.value === activeProviderKey.value ? 'active' : provider.value
    const response = await fetch(`/api/admin/llm/test/${testProvider}`, {
      method: 'POST',
      headers: {
        ...authHeaders(),
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        prompt: '你好，请用一句话回复确认连接正常。'
      }),
      signal: controller.signal
    })
    clearTimeout(timeoutId)

    if (response.status === 401 || response.status === 403) {
      handleUnauthorized()
      return
    }

    if (!response.ok) {
      const message = await response.text()
      throw new Error(message || '测试失败')
    }

    const data = await response.json()
    testResult.value = data.content || '连接成功，但未返回内容'
  } catch (error) {
    clearTimeout(timeoutId)
    if (error.name === 'AbortError') {
      testError.value = isOllamaProvider.value
        ? '⏳ 请求超时 — 本地模型首次加载可能需要数分钟，请确认 Ollama 已运行'
        : '⏳ 请求超时'
    } else {
      testError.value = error.message || '连接测试失败'
    }
  } finally {
    testAbortController.value = null
    testLoading.value = false
  }
}

function cancelTest() {
  testAbortController.value?.abort()
}

// 新闻清洗渠道
const newsProvider = ref('active')
const newsModel = ref('')
const newsBatchSize = ref(12)
const newsLoading = ref(false)
const newsSaveMsg = ref('')
const newsTestMsg = ref('')
const newsTesting = ref(false)
const newsTestAbortController = ref(null)

async function loadNewsCleansing() {
  try {
    const res = await fetch('/api/admin/llm/news-cleansing', {
      headers: { 'Authorization': `Bearer ${token.value}` }
    })
    if (res.ok) {
      const data = await res.json()
      newsProvider.value = data.provider || 'active'
      newsModel.value = data.model || ''
      newsBatchSize.value = data.batchSize || 12
    }
  } catch (e) { /* ignore load errors */ }
}

async function saveNewsCleansing() {
  newsLoading.value = true
  newsSaveMsg.value = ''
  try {
    const res = await fetch('/api/admin/llm/news-cleansing', {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${token.value}`
      },
      body: JSON.stringify({
        provider: newsProvider.value,
        model: newsModel.value,
        batchSize: newsBatchSize.value
      })
    })
    if (res.ok) {
      const data = await res.json()
      newsProvider.value = data.provider
      newsModel.value = data.model
      newsBatchSize.value = data.batchSize
      newsSaveMsg.value = '保存成功'
    } else {
      newsSaveMsg.value = '保存失败'
    }
  } catch (e) {
    newsSaveMsg.value = '保存失败: ' + e.message
  } finally {
    newsLoading.value = false
    setTimeout(() => { newsSaveMsg.value = '' }, 3000)
  }
}
async function testNewsCleansing() {
  if (newsProvider.value === 'active') {
    newsTestMsg.value = '请先选择具体渠道'
    setTimeout(() => { newsTestMsg.value = '' }, 3000)
    return
  }
  newsTesting.value = true
  newsTestMsg.value = ''

  const controller = new AbortController()
  newsTestAbortController.value = controller
  const isLocalModel = newsProvider.value === 'ollama'
  const timeoutMs = isLocalModel ? 60000 : 30000
  const timeoutId = setTimeout(() => controller.abort(), timeoutMs)

  try {
    const res = await fetch(`/api/admin/llm/test/${newsProvider.value}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${token.value}`
      },
      body: JSON.stringify({ prompt: '你好，请用一句话回复' }),
      signal: controller.signal
    })
    clearTimeout(timeoutId)
    if (res.ok) {
      newsTestMsg.value = '✅ 连接正常'
    } else {
      const data = await res.json().catch(() => null)
      newsTestMsg.value = '❌ ' + (data?.message || `HTTP ${res.status}`)
    }
  } catch (e) {
    clearTimeout(timeoutId)
    if (e.name === 'AbortError') {
      newsTestMsg.value = isLocalModel
        ? '⏳ 请求超时 — 本地模型首次加载可能需要数分钟，请确认 Ollama 已运行'
        : '⏳ 请求超时'
    } else {
      newsTestMsg.value = '❌ ' + e.message
    }
  } finally {
    newsTestAbortController.value = null
    newsTesting.value = false
    setTimeout(() => { newsTestMsg.value = '' }, 8000)
  }
}

function cancelNewsTest() {
  newsTestAbortController.value?.abort()
}

watch(newsProvider, (val) => {
  if (val === 'active') {
    newsModel.value = ''
  }
})
if (token.value) {
  loadActiveProvider().then(loaded => {
    if (loaded) {
      provider.value = activeProviderKey.value
      loadSettings()
      loadNewsCleansing()
      checkOllama()
    }
  })
}

onMounted(() => {
  if (keepAlive.value) startKeepAlive()
})

onUnmounted(() => {
  stopKeepAlive()
})
</script>

<template>
  <!-- ── 未登录：居中登录卡片 ── -->
  <div v-if="!isLoggedIn" class="login-wrapper">
    <div class="login-card">
      <div class="login-header">
        <div class="login-icon">
          <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
            <rect x="3" y="11" width="18" height="11" rx="2" ry="2"/>
            <path d="M7 11V7a5 5 0 0 1 10 0v4"/>
          </svg>
        </div>
        <h2 class="login-title">管理员登录</h2>
        <p class="login-subtitle">登录后可管理 LLM 接口配置</p>
      </div>
      <div class="form-field">
        <label class="form-label">账号</label>
        <input class="form-input" v-model="username" placeholder="管理员账号" @keyup.enter="login" />
      </div>
      <div class="form-field">
        <label class="form-label">密码</label>
        <input class="form-input" v-model="password" type="password" placeholder="管理员密码" @keyup.enter="login" />
      </div>
      <button class="btn-primary-lg" @click="login" :disabled="loginLoading">
        {{ loginLoading ? '登录中...' : '登   录' }}
      </button>
      <p v-if="loginError" class="form-error">{{ loginError }}</p>
    </div>
  </div>

  <!-- ── 已登录：设置面板 ── -->
  <div v-else class="settings-root">
    <!-- 页面头 -->
    <div class="page-header">
      <div>
        <h2 class="page-title">LLM 接口设置</h2>
        <p class="page-desc">管理 AI 模型通道与密钥</p>
      </div>
      <div class="page-header-actions">
        <span class="user-badge">🟢 已登录</span>
        <button class="btn-ghost-sm" @click="logout">退出登录</button>
      </div>
    </div>

    <!-- Ollama 本地模型管理 -->
    <div class="ollama-panel">
      <h3 class="panel-title">🦙 Ollama 本地模型管理</h3>

      <!-- 状态行 -->
      <div class="status-row">
        <span class="status-label">服务状态</span>
        <span :class="['ollama-dot', ollamaStatus === 'running' ? 'green' : 'red']"></span>
        <span class="ollama-status-text">
          {{ ollamaStatus === 'running' ? '运行中' :
             ollamaStatus === 'starting' ? '启动中...' :
             ollamaStatus === 'stopping' ? '停止中...' :
             ollamaStatus === 'checking' ? '检查中...' :
             ollamaStatus === 'not_running' ? '未运行' :
             ollamaStatus === 'not_installed' ? '未安装' : '未知' }}
        </span>
        <div class="status-actions">
          <button v-if="ollamaStatus === 'not_running' || ollamaStatus === 'error'"
                  class="ollama-action-btn start" @click="startOllama" :disabled="ollamaStatus === 'starting'">
            ▶ 启动
          </button>
          <button v-if="ollamaStatus === 'running'"
                  class="ollama-action-btn stop" @click="stopOllama" :disabled="ollamaStatus === 'stopping'">
            ⏹ 停止
          </button>
          <button class="ollama-action-btn" @click="checkOllama" :disabled="ollamaStatus === 'checking'">
            🔄 刷新
          </button>
        </div>
      </div>

      <!-- 未安装提示 -->
      <div v-if="ollamaStatus === 'not_installed'" class="install-hint">
        <p>Ollama 未安装。请先安装：</p>
        <code>winget install Ollama.Ollama</code>
        <span class="or-text">或访问</span>
        <a href="https://ollama.com/download" target="_blank" rel="noopener">ollama.com/download</a>
      </div>

      <!-- 保持活跃 -->
      <div class="keepalive-row" v-if="ollamaStatus !== 'not_installed'">
        <label class="keepalive-label">
          <input type="checkbox" v-model="keepAlive" @change="onKeepAliveChange" />
          保持活跃（掉线自动重启）
        </label>
      </div>

      <!-- 消息 -->
      <p v-if="ollamaMsg" class="ollama-msg" :class="{ error: ollamaMsg.includes('失败') || ollamaMsg.includes('异常') }">
        {{ ollamaMsg }}
      </p>

      <!-- 已安装模型 -->
      <div v-if="ollamaModels.length" class="models-section">
        <h4>已安装模型 ({{ ollamaModels.length }})</h4>
        <div class="models-list">
          <div v-for="m in ollamaModels" :key="m.name" class="model-item">
            <span class="model-name">{{ m.name }}</span>
            <span class="model-size">{{ formatSize(m.size) }}</span>
          </div>
        </div>
      </div>

      <!-- 拉取新模型 -->
      <div v-if="ollamaStatus === 'running'" class="pull-section">
        <h4>拉取模型</h4>
        <div class="pull-row">
          <input type="text" v-model="pullModelName" placeholder="例如：qwen2.5:7b, llama3.2"
                 @keyup.enter="pullModel" class="pull-input" />
          <button class="ollama-action-btn" @click="pullModel" :disabled="pulling || !pullModelName.trim()">
            {{ pulling ? '拉取中...' : '拉取' }}
          </button>
        </div>
        <p v-if="pullMsg" class="pull-msg">{{ pullMsg }}</p>
      </div>
    </div>

    <!-- 激活通道卡片 -->
    <div class="settings-card">
      <div class="card-section-title">
        <span class="section-dot section-dot--active"></span>
        激活通道
      </div>
      <div class="row-fields">
        <div class="form-field grow">
          <select class="form-input" v-model="activeProviderKey">
            <option v-for="option in providerOptions" :key="option.value" :value="option.value">{{ option.label }}</option>
          </select>
        </div>
        <button class="btn-secondary" @click="saveActiveProvider" :disabled="settingsLoading">
          {{ settingsLoading ? '切换中...' : '切换激活通道' }}
        </button>
      </div>
    </div>

    <!-- 配置编辑卡片 -->
    <div class="settings-card">
      <div class="card-section-title">
        <span class="section-dot section-dot--edit"></span>
        通道配置
      </div>

      <div class="form-field">
        <label class="form-label">编辑 Provider</label>
        <select class="form-input" v-model="provider" @change="loadSettings">
          <option v-for="option in providerOptions" :key="option.value" :value="option.value">{{ option.label }}</option>
        </select>
      </div>

      <!-- 密钥组 -->
      <div class="field-group">
        <div class="field-group-header">{{ isAntigravity ? 'Google 账号认证' : '密钥配置' }}</div>

        <!-- Antigravity: Google 登录 -->
        <template v-if="isAntigravity">
          <div class="form-field">
            <div v-if="antigravityEmail" class="antigravity-status">
              <span class="status-dot status-dot--ok"></span>
              已授权：{{ antigravityEmail }}
            </div>
            <div v-else-if="hasApiKey" class="antigravity-status">
              <span class="status-dot status-dot--ok"></span>
              已配置 Google 账号（Token 已保存）
            </div>
            <div v-else class="antigravity-status">
              <span class="status-dot status-dot--warn"></span>
              未登录 Google 账号
            </div>

            <button class="btn-secondary" @click="startAntigravityAuth" :disabled="antigravityAuthLoading" style="margin-top: 8px;">
              {{ antigravityAuthLoading ? '等待 Google 授权中...' : (hasApiKey ? '重新登录 Google' : '登录 Google 账号') }}
            </button>

            <p v-if="antigravityAuthError" class="form-error" style="margin-top: 8px;">{{ antigravityAuthError }}</p>
            <p class="form-hint">⚠️ 使用 Antigravity 通道有 Google 封号风险，建议使用非主力账号。</p>
          </div>
        </template>

        <!-- 其他 Provider: API Key 输入 -->
        <template v-else>
          <div class="form-field" v-if="!isOllamaProvider">
            <label class="form-label">主 LLM API Key</label>
            <input class="form-input" v-model="apiKey" placeholder="填写新的主模型 Key（留空保持不变）" />
            <p v-if="hasApiKey" class="form-hint">当前已保存：{{ apiKeyMasked }}</p>
          </div>
          <div class="form-field">
            <label class="form-label">Tavily API Key（外部搜索）</label>
            <input class="form-input" v-model="tavilyApiKey" placeholder="填写 Tavily Key（留空保持不变）" />
            <p class="form-hint">仅用于外部搜索工具，不影响主 LLM API Key。</p>
            <p v-if="hasTavilyApiKey" class="form-hint">当前已保存 Tavily Key：{{ tavilyApiKeyMasked }}</p>
          </div>
        </template>
      </div>

      <!-- 模型组 -->
      <div class="field-group">
        <div class="field-group-header">模型设置</div>
        <div class="form-field" v-if="!isAntigravity">
          <label class="form-label">Base URL</label>
          <input class="form-input" v-model="baseUrl" placeholder="https://api.openai.com/v1" />
        </div>
        <div class="form-field">
          <label class="form-label">模型</label>
          <select v-if="isAntigravity && antigravityModels.length > 0" class="form-input" v-model="model">
            <option v-for="m in antigravityModels" :key="m" :value="m">{{ m }}</option>
          </select>
          <input v-else class="form-input" v-model="model" :placeholder="modelPlaceholder" />
        </div>
        <div class="form-field">
          <label class="form-label">预设提示词</label>
          <textarea class="form-input form-textarea" v-model="systemPrompt" rows="4" placeholder="用于引导模型的系统提示词"></textarea>
        </div>
        <div v-if="isOllamaProvider" style="background: var(--color-success-bg); padding: 8px 12px; border-radius: var(--radius-sm); margin-top: 4px; font-size: var(--text-sm); color: var(--color-success);">
          💡 Ollama 本地模型无需 API Key，确保 Ollama 服务已启动 (默认端口 11434)。首次加载模型可能需要数分钟。
        </div>
      </div>

      <!-- 高级组 -->
      <div class="field-group">
        <div class="field-group-header">高级选项</div>
        <div class="row-fields" v-if="!isOllamaProvider">
          <div class="form-field grow">
            <label class="form-label">Organization</label>
            <input class="form-input" v-model="organization" placeholder="可选" />
          </div>
          <div class="form-field grow">
            <label class="form-label">Project</label>
            <input class="form-input" v-model="project" placeholder="可选" />
          </div>
        </div>
        <div class="toggle-row">
          <label class="toggle-label"><input type="checkbox" v-model="forceChinese" /> 强制中文回复</label>
          <label class="toggle-label"><input type="checkbox" v-model="enabled" /> 启用该 Provider</label>
        </div>
      </div>

      <!-- 操作栏 -->
      <div class="form-actions">
        <div class="action-buttons">
          <button class="btn-primary-lg" @click="saveSettings" :disabled="settingsLoading" style="flex:1;">
            {{ settingsLoading ? '保存中...' : '保存设置' }}
          </button>
          <button class="btn-secondary" @click="testConnection" :disabled="testLoading" style="margin-left: 8px; height: 44px;">
            {{ testLoading ? '测试中...' : '🔗 测试连接' }}
          </button>
          <span v-if="testLoading" @click="cancelTest" style="margin-left: 8px; color: #ff9800; cursor: pointer; font-size: 12px; text-decoration: underline;">取消</span>
        </div>
        <div v-if="testResult" class="form-success" style="margin-top: 8px;">✅ {{ testResult }}</div>
        <div v-if="testError" class="form-error" style="margin-top: 8px;">❌ {{ testError }}</div>
        <p v-if="saveMessage" class="form-success">{{ saveMessage }}</p>
        <p v-if="settingsError" class="form-error">{{ settingsError }}</p>
      </div>
    </div>

    <!-- 新闻清洗渠道设置 -->
    <div class="settings-card">
      <div class="card-section-title">
        <span class="section-dot" style="background: #ff9800;"></span>
        新闻清洗渠道设置
      </div>
      <p class="form-hint" style="margin-bottom: var(--space-4);">
        新闻清洗可以使用独立渠道（如本地 Ollama），不影响其他 LLM 功能
      </p>

      <div class="form-field">
        <label class="form-label">清洗渠道</label>
        <select class="form-input" v-model="newsProvider">
          <option value="active">跟随主渠道 (active)</option>
          <option v-for="key in providerKeys" :key="key" :value="key">{{ providerPresets[key]?.label || key }}</option>
        </select>
      </div>

      <div class="form-field">
        <label class="form-label">模型 (留空则跟随渠道默认)</label>
        <input class="form-input" v-model="newsModel" type="text" placeholder="例如: gemma4:e4b"
               :disabled="newsProvider === 'active'"
               :style="{ opacity: newsProvider === 'active' ? 0.5 : 1 }" />
      </div>

      <div class="form-field">
        <label class="form-label">批次大小 (5-20)</label>
        <input class="form-input" v-model.number="newsBatchSize" type="number" min="5" max="20"
               :disabled="newsProvider === 'active'"
               :style="{ width: '120px', opacity: newsProvider === 'active' ? 0.5 : 1 }" />
      </div>

      <div style="display: flex; align-items: center; gap: var(--space-3); margin-top: var(--space-3); flex-wrap: wrap;">
        <button class="btn-secondary" @click="saveNewsCleansing" :disabled="newsLoading">
          {{ newsLoading ? '保存中...' : '保存' }}
        </button>
        <button class="btn-secondary" @click="testNewsCleansing" :disabled="newsTesting || newsProvider === 'active'">
          {{ newsTesting ? '测试中...' : '🔗 测试连接' }}
        </button>
        <span v-if="newsTesting" @click="cancelNewsTest" style="color: #ff9800; cursor: pointer; font-size: 12px; text-decoration: underline;">取消</span>
        <span v-if="newsSaveMsg" style="font-size: var(--text-sm); color: var(--color-success);">{{ newsSaveMsg }}</span>
        <span v-if="newsTestMsg" style="font-size: var(--text-sm);"
              :style="{ color: newsTestMsg.startsWith('✅') ? 'var(--color-success)' : newsTestMsg.startsWith('❌') ? 'var(--color-danger)' : '#ff9800' }">
          {{ newsTestMsg }}
        </span>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* ── 登录面板（居中卡片） ── */
.login-wrapper {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: calc(100vh - 100px);
}
.login-card {
  width: 100%;
  max-width: 400px;
  background: var(--color-bg-surface);
  border-radius: var(--radius-xl);
  padding: var(--space-8) var(--space-6);
  box-shadow: var(--shadow-lg);
  border: 1px solid var(--color-border-light);
}
.login-header {
  text-align: center;
  margin-bottom: var(--space-6);
}
.login-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 56px;
  height: 56px;
  border-radius: var(--radius-lg);
  background: var(--color-accent-subtle);
  color: var(--color-accent);
  margin-bottom: var(--space-4);
}
.login-title {
  margin: 0;
  font-size: var(--text-2xl);
  font-weight: 700;
  color: var(--color-text-primary);
}
.login-subtitle {
  margin: var(--space-1) 0 0;
  font-size: var(--text-base);
  color: var(--color-text-secondary);
}

/* ── 设置页根布局 ── */
.settings-root {
  max-width: 720px;
  margin: 0 auto;
  display: flex;
  flex-direction: column;
  gap: var(--space-5);
}

/* ── 页面头 ── */
.page-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--space-4);
}
.page-title {
  margin: 0;
  font-size: var(--text-2xl);
  font-weight: 700;
  color: var(--color-text-primary);
}
.page-desc {
  margin: var(--space-1) 0 0;
  color: var(--color-text-secondary);
  font-size: var(--text-base);
}
.page-header-actions {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  flex-shrink: 0;
}
.user-badge {
  font-size: var(--text-sm);
  color: var(--color-success);
  font-weight: 500;
}

/* ── 设置卡片 ── */
.settings-card {
  background: var(--color-bg-surface);
  border: 1px solid var(--color-border-light);
  border-radius: var(--radius-lg);
  padding: var(--space-6);
  box-shadow: var(--shadow-sm);
}
.card-section-title {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  font-size: var(--text-lg);
  font-weight: 600;
  color: var(--color-text-primary);
  margin-bottom: var(--space-5);
}
.section-dot {
  width: 8px;
  height: 8px;
  border-radius: var(--radius-full);
}
.section-dot--active { background: var(--color-success); }
.section-dot--edit   { background: var(--color-accent); }

/* ── 字段组 ── */
.field-group {
  border: 1px solid var(--color-border-light);
  border-radius: var(--radius-md);
  padding: var(--space-4) var(--space-5);
  margin-bottom: var(--space-4);
  background: var(--color-bg-surface-alt);
}
.field-group-header {
  font-size: var(--text-sm);
  font-weight: 600;
  color: var(--color-text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.06em;
  margin-bottom: var(--space-4);
  padding-bottom: var(--space-2);
  border-bottom: 1px solid var(--color-border-light);
}

/* ── 表单字段 ── */
.form-field {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  margin-bottom: var(--space-4);
}
.form-field:last-child { margin-bottom: 0; }

.form-label {
  font-size: var(--text-sm);
  font-weight: 600;
  color: var(--color-text-secondary);
}
.form-input {
  width: 100%;
  height: 40px;
  padding: 0 var(--space-3);
  font-family: var(--font-family-primary);
  font-size: var(--text-base);
  color: var(--color-text-body);
  background: var(--color-bg-surface);
  border: 1px solid var(--color-border-light);
  border-radius: var(--radius-md);
  transition: border-color var(--transition-fast), box-shadow var(--transition-fast);
}
.form-input:hover { border-color: var(--color-border-medium); }
.form-input:focus {
  outline: none;
  border-color: var(--color-accent);
  box-shadow: var(--shadow-ring-accent);
}
.form-input::placeholder { color: var(--color-text-muted); }
.form-textarea {
  height: auto;
  min-height: 90px;
  padding: var(--space-2) var(--space-3);
  resize: vertical;
  line-height: var(--leading-normal);
}
.form-hint {
  margin: 0;
  font-size: var(--text-sm);
  color: var(--color-text-muted);
}

/* ── 行内排列 ── */
.row-fields {
  display: flex;
  gap: var(--space-3);
  align-items: flex-end;
}
.grow { flex: 1; }

/* ── Toggle 行 ── */
.toggle-row {
  display: flex;
  gap: var(--space-6);
  flex-wrap: wrap;
}
.toggle-label {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  font-size: var(--text-base);
  color: var(--color-text-body);
  cursor: pointer;
}
.toggle-label input[type="checkbox"] {
  width: 16px;
  height: 16px;
  accent-color: var(--color-accent);
  cursor: pointer;
}

/* ── 按钮 ── */
.btn-primary-lg {
  width: 100%;
  height: 44px;
  border: none;
  border-radius: var(--radius-md);
  background: var(--color-accent);
  color: #fff;
  font-size: var(--text-md);
  font-weight: 600;
  cursor: pointer;
  transition: background var(--transition-fast), transform var(--transition-fast);
}
.btn-primary-lg:hover { background: var(--color-accent-hover); }
.btn-primary-lg:active { background: var(--color-accent-active); transform: scale(0.99); }
.btn-primary-lg:disabled {
  opacity: 0.55;
  cursor: not-allowed;
  transform: none;
}

.btn-secondary {
  height: 40px;
  padding: 0 var(--space-4);
  border: 1px solid var(--color-border-light);
  border-radius: var(--radius-md);
  background: var(--color-bg-surface);
  color: var(--color-text-body);
  font-size: var(--text-base);
  font-weight: 500;
  cursor: pointer;
  white-space: nowrap;
  transition: all var(--transition-fast);
}
.btn-secondary:hover {
  border-color: var(--color-border-medium);
  background: var(--color-bg-surface-alt);
}

.btn-ghost-sm {
  height: 32px;
  padding: 0 var(--space-3);
  border: 1px solid var(--color-border-light);
  border-radius: var(--radius-md);
  background: transparent;
  color: var(--color-text-secondary);
  font-size: var(--text-sm);
  cursor: pointer;
  transition: all var(--transition-fast);
}
.btn-ghost-sm:hover {
  background: var(--color-bg-surface-alt);
  color: var(--color-text-body);
}

/* ── 反馈消息 ── */
.form-error {
  margin: var(--space-3) 0 0;
  padding: var(--space-2) var(--space-3);
  border-radius: var(--radius-sm);
  background: var(--color-danger-bg);
  color: var(--color-danger);
  font-size: var(--text-sm);
  text-align: center;
}
.form-success {
  margin: var(--space-3) 0 0;
  padding: var(--space-2) var(--space-3);
  border-radius: var(--radius-sm);
  background: var(--color-success-bg);
  color: var(--color-success);
  font-size: var(--text-sm);
  text-align: center;
}

/* ── 操作栏 ── */
.form-actions {
  margin-top: var(--space-5);
  padding-top: var(--space-5);
  border-top: 1px solid var(--color-border-light);
}
.action-buttons {
  display: flex;
  align-items: center;
}

/* ── Antigravity 状态 ── */
.antigravity-status {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  font-size: var(--text-base);
  color: var(--color-text-body);
}
.status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex-shrink: 0;
}
.status-dot--ok { background: var(--color-success); }
.status-dot--warn { background: #f59e0b; }

/* ── Ollama 管理面板 ── */
.ollama-panel {
  background: var(--color-bg-surface);
  border: 1px solid var(--color-border-light);
  border-radius: var(--radius-lg);
  padding: var(--space-5);
  box-shadow: var(--shadow-sm);
}
.ollama-panel .panel-title {
  margin: 0 0 var(--space-3) 0;
  font-size: var(--text-lg);
  font-weight: 600;
  color: var(--color-text-primary);
}
.status-row {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  margin-bottom: var(--space-3);
}
.status-label {
  font-size: var(--text-sm);
  color: var(--color-text-secondary);
}
.ollama-dot {
  width: 8px; height: 8px; border-radius: 50%; flex-shrink: 0;
}
.ollama-dot.green { background: var(--color-success); box-shadow: 0 0 6px var(--color-success); }
.ollama-dot.red { background: var(--color-danger); }
.ollama-status-text { font-size: var(--text-sm); color: var(--color-text-primary); min-width: 60px; }
.status-actions { display: flex; gap: 6px; margin-left: auto; }
.ollama-action-btn {
  padding: 4px 10px; border: 1px solid var(--color-border-light); border-radius: var(--radius-sm);
  background: var(--color-bg-surface); color: var(--color-text-primary);
  cursor: pointer; font-size: var(--text-sm); white-space: nowrap;
  transition: all var(--transition-fast);
}
.ollama-action-btn.start { border-color: var(--color-success); color: var(--color-success); }
.ollama-action-btn.stop { border-color: var(--color-danger); color: var(--color-danger); }
.ollama-action-btn:hover:not(:disabled) { background: var(--color-bg-surface-alt); }
.ollama-action-btn:disabled { opacity: 0.5; cursor: not-allowed; }
.install-hint {
  padding: var(--space-3); background: var(--color-bg-surface-alt); border-radius: var(--radius-md);
  margin-bottom: var(--space-3); font-size: var(--text-sm); color: var(--color-text-secondary);
}
.install-hint code {
  display: inline-block; padding: 2px 8px; background: var(--color-bg-surface); border-radius: var(--radius-sm);
  font-family: 'Consolas', monospace; color: var(--color-success); margin: 4px 0;
}
.install-hint a { color: var(--color-accent); text-decoration: underline; }
.or-text { margin: 0 6px; }
.keepalive-row { margin-bottom: var(--space-3); }
.keepalive-label {
  font-size: var(--text-sm); color: var(--color-text-secondary); cursor: pointer;
  display: flex; align-items: center; gap: 6px;
}
.keepalive-label input[type="checkbox"] { accent-color: var(--color-success); }
.ollama-msg { font-size: var(--text-sm); color: var(--color-text-secondary); margin: 6px 0; }
.ollama-msg.error { color: var(--color-danger); }
.models-section { margin-top: var(--space-3); }
.models-section h4 { margin: 0 0 var(--space-2) 0; font-size: var(--text-sm); color: var(--color-text-secondary); }
.models-list { display: flex; flex-direction: column; gap: 4px; }
.model-item {
  display: flex; justify-content: space-between; padding: 4px 8px;
  background: var(--color-bg-surface-alt); border-radius: var(--radius-sm); font-size: var(--text-sm);
}
.model-name { color: var(--color-text-primary); }
.model-size { color: var(--color-text-muted); }
.pull-section { margin-top: var(--space-3); }
.pull-section h4 { margin: 0 0 var(--space-2) 0; font-size: var(--text-sm); color: var(--color-text-secondary); }
.pull-row { display: flex; gap: 8px; }
.pull-input {
  flex: 1; padding: 4px 8px; background: var(--color-bg-surface-alt);
  border: 1px solid var(--color-border-light); border-radius: var(--radius-sm);
  color: var(--color-text-primary); font-size: var(--text-sm);
}
.pull-msg { font-size: var(--text-sm); color: var(--color-text-secondary); margin-top: 6px; }
</style>
