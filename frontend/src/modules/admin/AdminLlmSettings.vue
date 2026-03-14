<script setup>
import { computed, ref } from 'vue'

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
  }
}

const providerOptions = Object.entries(providerPresets).map(([value, preset]) => ({
  value,
  label: preset.label
}))

const username = ref('')
const password = ref('')
const token = ref(localStorage.getItem('admin_token') || '')
const loginError = ref('')
const loginLoading = ref(false)

const activeProviderKey = ref('default')
const provider = ref('default')
const apiKey = ref('')
const baseUrl = ref(providerPresets.default.baseUrl)
const model = ref(providerPresets.default.model)
const organization = ref('')
const project = ref('')
const enabled = ref(true)
const apiKeyMasked = ref('')
const hasApiKey = ref(false)
const settingsLoading = ref(false)
const settingsError = ref('')
const saveMessage = ref('')
const systemPrompt = ref('')
const forceChinese = ref(false)

const isLoggedIn = computed(() => Boolean(token.value))

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
      await loadSettings()
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
        baseUrl: baseUrl.value,
        model: model.value,
        systemPrompt: systemPrompt.value,
        forceChinese: forceChinese.value,
        organization: organization.value,
        project: project.value,
        enabled: enabled.value
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
    apiKey.value = ''
    saveMessage.value = '已保存'
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

if (token.value) {
  loadActiveProvider().then(loaded => {
    if (loaded) {
      loadSettings()
    }
  })
}
</script>

<template>
  <section class="panel">
    <h2>LLM 接口设置</h2>

    <div v-if="!isLoggedIn" class="login-panel">
      <h3>管理员登录</h3>
      <div class="field">
        <label>账号</label>
        <input v-model="username" placeholder="管理员账号" />
      </div>
      <div class="field">
        <label>密码</label>
        <input v-model="password" type="password" placeholder="管理员密码" />
      </div>
      <button @click="login" :disabled="loginLoading">{{ loginLoading ? '登录中...' : '登录' }}</button>
      <p v-if="loginError" class="muted">{{ loginError }}</p>
    </div>

    <div v-else class="settings-panel">
      <div class="panel-actions">
        <p class="muted">已登录管理员</p>
        <button class="secondary" @click="logout">退出登录</button>
      </div>

      <div class="field compact-row">
        <div class="field grow">
          <label>激活通道</label>
          <select v-model="activeProviderKey">
            <option v-for="option in providerOptions" :key="option.value" :value="option.value">{{ option.label }}</option>
          </select>
        </div>
        <button class="secondary compact-action" @click="saveActiveProvider" :disabled="settingsLoading">{{ settingsLoading ? '切换中...' : '切换激活通道' }}</button>
      </div>

      <div class="field">
        <label>编辑 Provider</label>
        <select v-model="provider" @change="loadSettings">
          <option v-for="option in providerOptions" :key="option.value" :value="option.value">{{ option.label }}</option>
        </select>
      </div>

      <div class="field">
        <label>API Key</label>
        <input v-model="apiKey" placeholder="填写新 Key（留空保持不变）" />
        <p v-if="hasApiKey" class="muted">当前已保存：{{ apiKeyMasked }}</p>
      </div>

      <div class="field">
        <label>Base URL</label>
        <input v-model="baseUrl" placeholder="https://api.openai.com/v1" />
      </div>

      <div class="field">
        <label>模型</label>
        <input v-model="model" placeholder="gpt-4o-mini" />
      </div>

      <div class="field">
        <label>预设提示词</label>
        <textarea v-model="systemPrompt" rows="4" placeholder="用于引导模型的系统提示词" />
      </div>

      <div class="field">
        <label class="inline">
          <input type="checkbox" v-model="forceChinese" /> 强制中文回复
        </label>
      </div>

      <div class="field">
        <label>Organization</label>
        <input v-model="organization" placeholder="可选" />
      </div>

      <div class="field">
        <label>Project</label>
        <input v-model="project" placeholder="可选" />
      </div>

      <div class="field">
        <label class="inline">
          <input type="checkbox" v-model="enabled" /> 启用该 Provider
        </label>
      </div>

      <button @click="saveSettings" :disabled="settingsLoading">{{ settingsLoading ? '保存中...' : '保存设置' }}</button>
      <p v-if="saveMessage" class="muted">{{ saveMessage }}</p>
      <p v-if="settingsError" class="muted">{{ settingsError }}</p>
    </div>
  </section>
</template>

<style scoped>
.panel {
  background: #fff;
  border-radius: 16px;
  padding: 1.5rem;
  box-shadow: 0 10px 30px rgba(15, 23, 42, 0.08);
}

.field {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
  margin-bottom: 0.8rem;
}

.compact-row {
  flex-direction: row;
  align-items: flex-end;
  gap: 0.75rem;
}

.grow {
  flex: 1;
  margin-bottom: 0;
}

.field input,
.field select,
.field textarea {
  padding: 0.6rem 0.75rem;
  border-radius: 10px;
  border: 1px solid #e2e8f0;
  font-size: 0.95rem;
}

.field textarea {
  resize: vertical;
}

.field label.inline {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

button {
  padding: 0.6rem 1.2rem;
  border-radius: 999px;
  border: none;
  background: #2563eb;
  color: #fff;
  cursor: pointer;
}

button.secondary {
  background: #e2e8f0;
  color: #1f2937;
}

.compact-action {
  white-space: nowrap;
}

button:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.muted {
  color: #94a3b8;
  margin-top: 0.4rem;
}

.panel-actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 1rem;
}

.login-panel,
.settings-panel {
  margin-top: 1rem;
}
</style>
