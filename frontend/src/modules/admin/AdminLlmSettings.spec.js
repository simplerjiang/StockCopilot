import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import AdminLlmSettings from './AdminLlmSettings.vue'

const makeResponse = ({ ok, status, json, text }) => ({
  ok,
  status,
  json: json || (async () => ({})),
  text: text || (async () => '')
})

const activeProviderResponse = () => makeResponse({
  ok: true,
  status: 200,
  json: async () => ({
    activeProviderKey: 'default',
    providerKeys: ['default', 'gemini_official']
  })
})

const antigravityActiveProviderResponse = () => makeResponse({
  ok: true,
  status: 200,
  json: async () => ({
    activeProviderKey: 'antigravity',
    providerKeys: ['default', 'antigravity']
  })
})

const ollamaActiveProviderResponse = () => makeResponse({
  ok: true,
  status: 200,
  json: async () => ({
    activeProviderKey: 'ollama',
    providerKeys: ['default', 'ollama']
  })
})

const ollamaStatusResponse = (options = {}) => {
  const config = Array.isArray(options)
    ? { status: 'running', installed: true, models: options }
    : { status: 'running', installed: true, models: [], ...options }

  return makeResponse({
    ok: true,
    status: 200,
    json: async () => ({
      status: config.status,
      installed: config.installed,
      models: config.models == null ? null : { models: config.models }
    })
  })
}

const savedSettingsResponse = () => ({
  apiKeyMasked: '****',
  hasApiKey: true,
  tavilyApiKeyMasked: 'tv****',
  hasTavilyApiKey: true
})

describe('AdminLlmSettings', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.restoreAllMocks()
  })

  it('logs out when save returns unauthorized', async () => {
    localStorage.setItem('admin_token', 'token')

    const fetchMock = vi.fn(async (url, options) => {
      if (url.includes('/api/admin/llm/settings/active')) {
        return activeProviderResponse()
      }
      if (options?.method === 'PUT') {
        return makeResponse({ ok: false, status: 401 })
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(AdminLlmSettings)
    await flushPromises()

    const saveButton = wrapper.findAll('button').find(button => button.text().includes('保存设置'))
    expect(saveButton).toBeTruthy()
    await saveButton.trigger('click')
    await flushPromises()

    expect(localStorage.getItem('admin_token')).toBeNull()
    expect(wrapper.text()).toContain('管理员登录')
    expect(wrapper.text()).toContain('登录已过期')
  })

  it('shows neutral Antigravity authorization guidance without account-ban warning', async () => {
    localStorage.setItem('admin_token', 'token')

    const fetchMock = vi.fn(async (url) => {
      if (url.includes('/api/admin/llm/settings/active')) {
        return antigravityActiveProviderResponse()
      }
      if (!url.includes('/api/admin/llm/settings/active') && url.includes('/api/admin/llm/settings/antigravity')) {
        return makeResponse({
          ok: true,
          status: 200,
          json: async () => ({
            model: 'gemini-3-flash',
            enabled: true,
            hasApiKey: true,
            organization: 'user@example.com'
          })
        })
      }
      if (url.includes('/api/admin/antigravity/models')) {
        return makeResponse({ ok: true, status: 200, json: async () => [] })
      }
      if (!url.includes('/api/admin/llm/settings/') && url.includes('/api/admin/llm/news-cleansing')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ provider: 'active', model: '', batchSize: 12 }) })
      }
      if (url.includes('/api/admin/ollama/status')) {
        return ollamaStatusResponse({ status: 'not_running', installed: true, models: [] })
      }
      if (url.includes('/api/stocks/financial/embedding/status')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ available: false }) })
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(AdminLlmSettings)
    await flushPromises()
    await flushPromises()

    expect(wrapper.text()).toContain('Antigravity 通道使用 Google 授权登录')
    expect(wrapper.text()).not.toContain('封号风险')
    expect(wrapper.text()).not.toContain('非主力账号')
  })

  it('includes system prompt when saving', async () => {
    localStorage.setItem('admin_token', 'token')

    const fetchMock = vi.fn(async (url, options) => {
      if (url.includes('/api/admin/llm/settings/active')) {
        return activeProviderResponse()
      }
      if (options?.method === 'PUT') {
        return makeResponse({ ok: true, status: 200, json: async () => savedSettingsResponse() })
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(AdminLlmSettings)
    await flushPromises()

    await wrapper.find('textarea').setValue('你是股票助手')

    const saveButton = wrapper.findAll('button').find(button => button.text().includes('保存设置'))
    await saveButton.trigger('click')
    await flushPromises()

    const call = fetchMock.mock.calls.find(args => args[0].includes('/api/admin/llm/settings') && args[1]?.method === 'PUT')
    expect(call).toBeTruthy()
    expect(JSON.parse(call[1].body).systemPrompt).toBe('你是股票助手')
  })

  it('includes forceChinese when saving', async () => {
    localStorage.setItem('admin_token', 'token')

    const fetchMock = vi.fn(async (url, options) => {
      if (url.includes('/api/admin/llm/settings/active')) {
        return activeProviderResponse()
      }
      if (options?.method === 'PUT') {
        return makeResponse({ ok: true, status: 200, json: async () => savedSettingsResponse() })
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(AdminLlmSettings)
    await flushPromises()

    const checkbox = wrapper.find('input[type="checkbox"]')
    await checkbox.setValue(true)

    const saveButton = wrapper.findAll('button').find(button => button.text().includes('保存设置'))
    await saveButton.trigger('click')
    await flushPromises()

    const call = fetchMock.mock.calls.find(args => args[0].includes('/api/admin/llm/settings') && args[1]?.method === 'PUT')
    expect(JSON.parse(call[1].body).forceChinese).toBe(true)
  })

  it('emits settings-saved after save succeeds', async () => {
    localStorage.setItem('admin_token', 'token')

    const fetchMock = vi.fn(async (url, options) => {
      if (url.includes('/api/admin/llm/settings/active')) {
        return activeProviderResponse()
      }
      if (options?.method === 'PUT') {
        return makeResponse({ ok: true, status: 200, json: async () => savedSettingsResponse() })
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(AdminLlmSettings)
    await flushPromises()

    const saveButton = wrapper.findAll('button').find(button => button.text().includes('保存设置'))
    await saveButton.trigger('click')
    await flushPromises()

    expect(wrapper.emitted('settings-saved')).toBeTruthy()
    expect(wrapper.emitted('settings-saved')[0][0]).toEqual(savedSettingsResponse())
  })

  it('includes Tavily API key when saving', async () => {
    localStorage.setItem('admin_token', 'token')

    const fetchMock = vi.fn(async (url, options) => {
      if (url.includes('/api/admin/llm/settings/active')) {
        return activeProviderResponse()
      }
      if (options?.method === 'PUT') {
        return makeResponse({ ok: true, status: 200, json: async () => savedSettingsResponse() })
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(AdminLlmSettings)
    await flushPromises()

    const tavilyInput = wrapper.findAll('input').find(input => input.attributes('placeholder')?.includes('Tavily Key'))
    expect(tavilyInput).toBeTruthy()
    await tavilyInput.setValue('tv-new-key')

    const saveButton = wrapper.findAll('button').find(button => button.text().includes('保存设置'))
    await saveButton.trigger('click')
    await flushPromises()

    const call = fetchMock.mock.calls.find(args => args[0].includes('/api/admin/llm/settings') && args[1]?.method === 'PUT')
    expect(JSON.parse(call[1].body).tavilyApiKey).toBe('tv-new-key')
    expect(wrapper.text()).toContain('当前已保存 Tavily Key：tv****')
  })

  it('auto-switches the active provider when provider settings are saved', async () => {
    localStorage.setItem('admin_token', 'token')

    const fetchMock = vi.fn(async (url, options) => {
      if (url.includes('/api/admin/llm/settings/active') && options?.method === 'PUT') {
        return makeResponse({ ok: true, status: 200, json: async () => ({ activeProviderKey: 'gemini_official', providerKeys: ['default', 'gemini_official'] }) })
      }
      if (url.includes('/api/admin/llm/settings/active')) {
        return activeProviderResponse()
      }
      if (!options?.method && url.includes('/api/admin/llm/settings/')) {
        return makeResponse({ ok: false, status: 404 })
      }
      if (url.includes('/api/admin/llm/settings/gemini_official') && options?.method === 'PUT') {
        return makeResponse({ ok: true, status: 200, json: async () => savedSettingsResponse() })
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(AdminLlmSettings)
    await flushPromises()

    expect(wrapper.text()).not.toContain('切换激活通道')
    expect(wrapper.text()).toContain('当前主渠道：Default 通道')

    const selects = wrapper.findAll('select')
    await selects[0].setValue('gemini_official')
    await flushPromises()

    const saveButton = wrapper.findAll('button').find(button => button.text().includes('保存设置'))
    await saveButton.trigger('click')
    await flushPromises()

    const call = fetchMock.mock.calls.find(args => args[0].includes('/api/admin/llm/settings/active') && args[1]?.method === 'PUT')
    expect(call).toBeTruthy()
    expect(JSON.parse(call[1].body)).toEqual({ activeProviderKey: 'gemini_official' })
    expect(wrapper.text()).toContain('通道配置已保存，并已自动切换主渠道为「Gemini 官方」')
  })

  it('saving news cleansing with active provider takes effect immediately without changing active provider', async () => {
    localStorage.setItem('admin_token', 'token')

    const fetchMock = vi.fn(async (url, options) => {
      if (url.includes('/api/admin/llm/settings/active')) {
        return activeProviderResponse()
      }
      if (url.includes('/api/admin/llm/news-cleansing') && options?.method === 'PUT') {
        return makeResponse({
          ok: true,
          status: 200,
          json: async () => ({
            provider: 'active',
            model: '',
            batchSize: 12
          })
        })
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(AdminLlmSettings)
    await flushPromises()

    const saveButton = wrapper.findAll('button').find(button => button.text() === '保存')
    expect(saveButton).toBeTruthy()
    await saveButton.trigger('click')
    await flushPromises()

    const activeProviderPut = fetchMock.mock.calls.find(args => args[0].includes('/api/admin/llm/settings/active') && args[1]?.method === 'PUT')
    expect(activeProviderPut).toBeFalsy()
    expect(wrapper.text()).toContain('新闻清洗已改为跟随主渠道「Default 通道」，并立即生效')
    expect(wrapper.text()).toContain('不影响主渠道')
  })

  it('shows the active-provider success message for Ollama without a clamp notice', async () => {
    localStorage.setItem('admin_token', 'token')

    const fetchMock = vi.fn(async (url, options) => {
      if (url.includes('/api/admin/llm/settings/active')) {
        return ollamaActiveProviderResponse()
      }
      if (!options?.method && url.includes('/api/admin/llm/settings/ollama')) {
        return makeResponse({ ok: false, status: 404 })
      }
      if (!options?.method && url.includes('/api/admin/llm/news-cleansing')) {
        return makeResponse({
          ok: true,
          status: 200,
          json: async () => ({ provider: 'active', model: '', batchSize: 12 })
        })
      }
      if (url.includes('/api/admin/ollama/status')) {
        return ollamaStatusResponse([{ name: 'gemma4:latest', size: 1024 }])
      }
      if (url.includes('/api/admin/llm/news-cleansing') && options?.method === 'PUT') {
        return makeResponse({
          ok: true,
          status: 200,
          json: async () => ({ provider: 'active', model: '', batchSize: 12 })
        })
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(AdminLlmSettings)
    await flushPromises()
    await flushPromises()

    const saveButton = wrapper.findAll('button').find(button => button.text() === '保存')
    expect(saveButton).toBeTruthy()
    await saveButton.trigger('click')
    await flushPromises()

    const newsSaveCall = fetchMock.mock.calls.find(args => args[0].includes('/api/admin/llm/news-cleansing') && args[1]?.method === 'PUT')
    expect(newsSaveCall).toBeTruthy()
    expect(JSON.parse(newsSaveCall[1].body)).toEqual({ provider: 'active', model: '', batchSize: 12 })
    expect(wrapper.text()).toContain('新闻清洗已改为跟随主渠道「Ollama 本地模型」')
    expect(wrapper.text()).not.toContain('已按本地 Ollama 上限自动调整为 5')
  })

  it('saving news cleansing with a dedicated provider does not change active provider and shows immediate effect', async () => {
    localStorage.setItem('admin_token', 'token')

    const fetchMock = vi.fn(async (url, options) => {
      if (url.includes('/api/admin/llm/settings/active')) {
        return activeProviderResponse()
      }
      if (url.includes('/api/admin/llm/news-cleansing') && options?.method === 'PUT') {
        return makeResponse({
          ok: true,
          status: 200,
          json: async () => ({
            provider: 'gemini_official',
            model: 'gemini-3.1-flash-lite-preview-thinking-high',
            batchSize: 12
          })
        })
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(AdminLlmSettings)
    await flushPromises()

    const selects = wrapper.findAll('select')
    await selects[1].setValue('gemini_official')

    const saveButton = wrapper.findAll('button').find(button => button.text() === '保存')
    expect(saveButton).toBeTruthy()
    await saveButton.trigger('click')
    await flushPromises()

    const activeProviderPut = fetchMock.mock.calls.find(args => args[0].includes('/api/admin/llm/settings/active') && args[1]?.method === 'PUT')
    expect(activeProviderPut).toBeFalsy()

    const newsSaveCall = fetchMock.mock.calls.find(args => args[0].includes('/api/admin/llm/news-cleansing') && args[1]?.method === 'PUT')
    expect(newsSaveCall).toBeTruthy()
    expect(JSON.parse(newsSaveCall[1].body).provider).toBe('gemini_official')
    expect(wrapper.text()).toContain('新闻清洗渠道已切换为「Gemini 官方」，并立即生效')
    expect(wrapper.text()).toContain('不影响主渠道')
  })

  it('loads Tavily API key metadata and renders masked state', async () => {
    localStorage.setItem('admin_token', 'token')

    const fetchMock = vi.fn(async (url, options) => {
      if (url.includes('/api/admin/llm/settings/active')) {
        return activeProviderResponse()
      }
      if (!options?.method && url.includes('/api/admin/llm/settings/default')) {
        return makeResponse({
          ok: true,
          status: 200,
          json: async () => ({
            baseUrl: 'https://api.bltcy.ai',
            model: 'gemini-3.1-flash-lite-preview-thinking-high',
            enabled: true,
            apiKeyMasked: '****',
            hasApiKey: true,
            tavilyApiKeyMasked: 'tv****',
            hasTavilyApiKey: true
          })
        })
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(AdminLlmSettings)
    await flushPromises()

    expect(wrapper.text()).toContain('Tavily API Key（外部搜索）')
    expect(wrapper.text()).toContain('当前已保存 Tavily Key：tv****')
  })

  it('auto-selects an installed Ollama model for provider save when the saved value is blank', async () => {
    localStorage.setItem('admin_token', 'token')

    const fetchMock = vi.fn(async (url, options) => {
      if (url.includes('/api/admin/llm/settings/active')) {
        return ollamaActiveProviderResponse()
      }
      if (!options?.method && url.includes('/api/admin/llm/settings/ollama')) {
        return makeResponse({
          ok: true,
          status: 200,
          json: async () => ({
            baseUrl: 'http://localhost:11434',
            model: '',
            enabled: true
          })
        })
      }
      if (!options?.method && url.includes('/api/admin/llm/news-cleansing')) {
        return makeResponse({
          ok: true,
          status: 200,
          json: async () => ({ provider: 'active', model: '', batchSize: 12 })
        })
      }
      if (url.includes('/api/admin/ollama/status')) {
        return ollamaStatusResponse([{ name: 'qwen3:8b', size: 1024 }, { name: 'llama3.2:3b', size: 512 }])
      }
      if (url.includes('/api/admin/llm/settings/ollama') && options?.method === 'PUT') {
        return makeResponse({ ok: true, status: 200, json: async () => savedSettingsResponse() })
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(AdminLlmSettings)
    await flushPromises()
    await flushPromises()

    const modelSelect = wrapper.findAll('select').find(select => select.html().includes('qwen3:8b'))
    expect(modelSelect).toBeTruthy()
    expect(modelSelect.element.value).toBe('qwen3:8b')

    const saveButton = wrapper.findAll('button').find(button => button.text().includes('保存设置'))
    await saveButton.trigger('click')
    await flushPromises()

    const call = fetchMock.mock.calls.find(args => args[0].includes('/api/admin/llm/settings/ollama') && args[1]?.method === 'PUT')
    expect(call).toBeTruthy()
    expect(JSON.parse(call[1].body).model).toBe('qwen3:8b')
  })

  it('loads saved Ollama runtime options into the settings form', async () => {
    localStorage.setItem('admin_token', 'token')

    const fetchMock = vi.fn(async (url, options) => {
      if (url.includes('/api/admin/llm/settings/active')) {
        return ollamaActiveProviderResponse()
      }
      if (!options?.method && url.includes('/api/admin/llm/settings/ollama')) {
        return makeResponse({
          ok: true,
          status: 200,
          json: async () => ({
            baseUrl: 'http://localhost:11434',
            model: 'gemma4:e2b',
            enabled: true,
            ollamaNumCtx: 4096,
            ollamaKeepAlive: '10m',
            ollamaNumPredict: -1,
            ollamaTemperature: 0.25,
            ollamaTopK: 40,
            ollamaTopP: 0.9,
            ollamaMinP: 0.05,
            ollamaStop: ['###', 'Observation:'],
            ollamaThink: true
          })
        })
      }
      if (!options?.method && url.includes('/api/admin/llm/news-cleansing')) {
        return makeResponse({
          ok: true,
          status: 200,
          json: async () => ({ provider: 'active', model: '', batchSize: 12 })
        })
      }
      if (url.includes('/api/admin/ollama/status')) {
        return ollamaStatusResponse([{ name: 'gemma4:e2b', size: 1024 }])
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(AdminLlmSettings)
    await flushPromises()
    await flushPromises()

    expect(wrapper.find('input[placeholder="默认 2048"]').element.value).toBe('4096')
    expect(wrapper.find('input[placeholder="默认 5m，0 表示立即卸载"]').element.value).toBe('10m')
    expect(wrapper.find('input[placeholder="默认 256，-1 表示不限"]').element.value).toBe('-1')
    expect(wrapper.find('input[placeholder="默认 0.3，越低越稳"]').element.value).toBe('0.25')
    expect(wrapper.find('input[placeholder="默认 64"]').element.value).toBe('40')
    expect(wrapper.find('input[placeholder="默认 0.95"]').element.value).toBe('0.9')
    expect(wrapper.find('input[placeholder="默认 0，0-1 之间"]').element.value).toBe('0.05')
    expect(wrapper.find('[data-testid="ollama-stop"]').element.value).toBe('###\nObservation:')
    expect(wrapper.find('[data-testid="ollama-think"]').element.checked).toBe(true)
  })

  it('includes Ollama runtime options when saving provider settings', async () => {
    localStorage.setItem('admin_token', 'token')

    const fetchMock = vi.fn(async (url, options) => {
      if (url.includes('/api/admin/llm/settings/active')) {
        return ollamaActiveProviderResponse()
      }
      if (!options?.method && url.includes('/api/admin/llm/settings/ollama')) {
        return makeResponse({
          ok: true,
          status: 200,
          json: async () => ({
            baseUrl: 'http://localhost:11434',
            model: 'gemma4:e2b',
            enabled: true
          })
        })
      }
      if (!options?.method && url.includes('/api/admin/llm/news-cleansing')) {
        return makeResponse({
          ok: true,
          status: 200,
          json: async () => ({ provider: 'active', model: '', batchSize: 12 })
        })
      }
      if (url.includes('/api/admin/ollama/status')) {
        return ollamaStatusResponse([{ name: 'gemma4:e2b', size: 1024 }])
      }
      if (url.includes('/api/admin/llm/settings/ollama') && options?.method === 'PUT') {
        return makeResponse({ ok: true, status: 200, json: async () => savedSettingsResponse() })
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(AdminLlmSettings)
    await flushPromises()
    await flushPromises()

    await wrapper.find('input[placeholder="默认 2048"]').setValue('2048')
    await wrapper.find('input[placeholder="默认 5m，0 表示立即卸载"]').setValue('15m')
    await wrapper.find('input[placeholder="默认 256，-1 表示不限"]').setValue('-1')
    await wrapper.find('input[placeholder="默认 0.3，越低越稳"]').setValue('0.2')
    await wrapper.find('input[placeholder="默认 64"]').setValue('24')
    await wrapper.find('input[placeholder="默认 0.95"]').setValue('0.8')
    await wrapper.find('input[placeholder="默认 0，0-1 之间"]').setValue('0.04')
    await wrapper.find('[data-testid="ollama-stop"]').setValue('###\nEND')
    await wrapper.find('[data-testid="ollama-think"]').setValue(true)

    const saveButton = wrapper.findAll('button').find(button => button.text().includes('保存设置'))
    await saveButton.trigger('click')
    await flushPromises()

    const call = fetchMock.mock.calls.find(args => args[0].includes('/api/admin/llm/settings/ollama') && args[1]?.method === 'PUT')
    expect(call).toBeTruthy()
    expect(JSON.parse(call[1].body)).toMatchObject({
      ollamaNumCtx: 2048,
      ollamaKeepAlive: '15m',
      ollamaNumPredict: -1,
      ollamaTemperature: 0.2,
      ollamaTopK: 24,
      ollamaTopP: 0.8,
      ollamaMinP: 0.04,
      ollamaStop: ['###', 'END'],
      ollamaThink: true
    })
  })

  it('applies and saves explicit Ollama runtime defaults when settings are blank', async () => {
    localStorage.setItem('admin_token', 'token')

    const fetchMock = vi.fn(async (url, options) => {
      if (url.includes('/api/admin/llm/settings/active')) {
        return ollamaActiveProviderResponse()
      }
      if (!options?.method && url.includes('/api/admin/llm/settings/ollama')) {
        return makeResponse({
          ok: true,
          status: 200,
          json: async () => ({
            baseUrl: 'http://localhost:11434',
            model: 'gemma4:e2b',
            enabled: true
          })
        })
      }
      if (!options?.method && url.includes('/api/admin/llm/news-cleansing')) {
        return makeResponse({
          ok: true,
          status: 200,
          json: async () => ({ provider: 'active', model: '', batchSize: 12 })
        })
      }
      if (url.includes('/api/admin/ollama/status')) {
        return ollamaStatusResponse([{ name: 'gemma4:e2b', size: 1024 }])
      }
      if (url.includes('/api/admin/llm/settings/ollama') && options?.method === 'PUT') {
        return makeResponse({ ok: true, status: 200, json: async () => savedSettingsResponse() })
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(AdminLlmSettings)
    await flushPromises()
    await flushPromises()

    expect(wrapper.find('input[placeholder="默认 2048"]').element.value).toBe('2048')
    expect(wrapper.find('input[placeholder="默认 5m，0 表示立即卸载"]').element.value).toBe('5m')
    expect(wrapper.find('input[placeholder="默认 256，-1 表示不限"]').element.value).toBe('256')
    expect(wrapper.find('input[placeholder="默认 0.3，越低越稳"]').element.value).toBe('0.3')
    expect(wrapper.find('input[placeholder="默认 64"]').element.value).toBe('64')
    expect(wrapper.find('input[placeholder="默认 0.95"]').element.value).toBe('0.95')
    expect(wrapper.find('input[placeholder="默认 0，0-1 之间"]').element.value).toBe('0')
    expect(wrapper.find('[data-testid="ollama-stop"]').element.value).toBe('')
    expect(wrapper.find('[data-testid="ollama-think"]').element.checked).toBe(false)

    const saveButton = wrapper.findAll('button').find(button => button.text().includes('保存设置'))
    await saveButton.trigger('click')
    await flushPromises()

    const call = fetchMock.mock.calls.find(args => args[0].includes('/api/admin/llm/settings/ollama') && args[1]?.method === 'PUT')
    expect(call).toBeTruthy()
    expect(JSON.parse(call[1].body)).toMatchObject({
      ollamaNumCtx: 2048,
      ollamaKeepAlive: '5m',
      ollamaNumPredict: 256,
      ollamaTemperature: 0.3,
      ollamaTopK: 64,
      ollamaTopP: 0.95,
      ollamaMinP: 0,
      ollamaStop: [],
      ollamaThink: false
    })
  })

  it('auto-selects an installed Ollama model for news cleansing save when the saved value is stale', async () => {
    localStorage.setItem('admin_token', 'token')

    const fetchMock = vi.fn(async (url, options) => {
      if (url.includes('/api/admin/llm/settings/active')) {
        return activeProviderResponse()
      }
      if (!options?.method && url.includes('/api/admin/llm/settings/default')) {
        return makeResponse({ ok: false, status: 404 })
      }
      if (!options?.method && url.includes('/api/admin/llm/news-cleansing')) {
        return makeResponse({
          ok: true,
          status: 200,
          json: async () => ({ provider: 'ollama', model: 'missing:model', batchSize: 12 })
        })
      }
      if (url.includes('/api/admin/ollama/status')) {
        return ollamaStatusResponse([{ name: 'qwen3:8b', size: 1024 }])
      }
      if (url.includes('/api/admin/llm/news-cleansing') && options?.method === 'PUT') {
        return makeResponse({
          ok: true,
          status: 200,
          json: async () => ({ provider: 'ollama', model: 'qwen3:8b', batchSize: 12 })
        })
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(AdminLlmSettings)
    await flushPromises()
    await flushPromises()

    const modelSelect = wrapper.findAll('select').find(select => select.html().includes('qwen3:8b'))
    expect(modelSelect).toBeTruthy()
    expect(modelSelect.element.value).toBe('qwen3:8b')

    const saveButton = wrapper.findAll('button').find(button => button.text() === '保存')
    await saveButton.trigger('click')
    await flushPromises()

    const call = fetchMock.mock.calls.find(args => args[0].includes('/api/admin/llm/news-cleansing') && args[1]?.method === 'PUT')
    expect(call).toBeTruthy()
    expect(JSON.parse(call[1].body).model).toBe('qwen3:8b')
  })

  it('blocks Ollama provider save clearly when no installed models are available', async () => {
    localStorage.setItem('admin_token', 'token')

    const fetchMock = vi.fn(async (url, options) => {
      if (url.includes('/api/admin/llm/settings/active')) {
        return ollamaActiveProviderResponse()
      }
      if (!options?.method && url.includes('/api/admin/llm/settings/ollama')) {
        return makeResponse({
          ok: true,
          status: 200,
          json: async () => ({
            baseUrl: 'http://localhost:11434',
            model: '',
            enabled: true
          })
        })
      }
      if (!options?.method && url.includes('/api/admin/llm/news-cleansing')) {
        return makeResponse({
          ok: true,
          status: 200,
          json: async () => ({ provider: 'active', model: '', batchSize: 12 })
        })
      }
      if (url.includes('/api/admin/ollama/status')) {
        return ollamaStatusResponse([])
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(AdminLlmSettings)
    await flushPromises()
    await flushPromises()

    expect(wrapper.text()).toContain('服务已运行，但当前未检测到已拉取模型，请先在下方「拉取模型」区域拉取至少一个模型。')
    expect(wrapper.text()).toContain('服务已运行，但当前未检测到已拉取模型，请先到上方「🦙 Ollama 本地模型管理」拉取至少一个模型。')
    expect(wrapper.text()).toContain('主渠道当前不可保存或测试。当前未检测到已拉取模型。请先到上方「🦙 Ollama 本地模型管理」拉取至少一个模型。')

    const saveButton = wrapper.findAll('button').find(button => button.text().includes('保存设置'))
    const testButton = wrapper.findAll('button').find(button => button.text().includes('测试连接'))

    expect(saveButton.attributes('disabled')).toBeDefined()
    expect(testButton.attributes('disabled')).toBeDefined()

    const saveCall = fetchMock.mock.calls.find(args => args[0].includes('/api/admin/llm/settings/ollama') && args[1]?.method === 'PUT')
    expect(saveCall).toBeFalsy()
  })

  it('shows Ollama as not running instead of not installed when the app is installed but stopped', async () => {
    localStorage.setItem('admin_token', 'token')

    const fetchMock = vi.fn(async (url, options) => {
      if (url.includes('/api/admin/llm/settings/active')) {
        return ollamaActiveProviderResponse()
      }
      if (!options?.method && url.includes('/api/admin/llm/settings/ollama')) {
        return makeResponse({
          ok: true,
          status: 200,
          json: async () => ({
            baseUrl: 'http://localhost:11434',
            model: '',
            enabled: true
          })
        })
      }
      if (!options?.method && url.includes('/api/admin/llm/news-cleansing')) {
        return makeResponse({
          ok: true,
          status: 200,
          json: async () => ({ provider: 'active', model: '', batchSize: 12 })
        })
      }
      if (url.includes('/api/admin/ollama/status')) {
        return ollamaStatusResponse({ status: 'not_running', installed: true, models: null })
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(AdminLlmSettings)
    await flushPromises()
    await flushPromises()

    expect(wrapper.text()).toContain('服务状态')
    expect(wrapper.text()).toContain('未运行')
    expect(wrapper.text()).not.toContain('Ollama 未安装。请先安装：')
    expect(wrapper.text()).toContain('请先到上方「🦙 Ollama 本地模型管理」启动并刷新 Ollama，以读取已安装模型。')

    const startButton = wrapper.findAll('button').find(button => button.text().includes('启动'))
    const refreshButton = wrapper.findAll('button').find(button => button.text().includes('刷新'))

    expect(startButton).toBeTruthy()
    expect(refreshButton).toBeTruthy()
  })

  it('blocks Ollama news cleansing actions and shows the local recovery hint when no installed models are available', async () => {
    localStorage.setItem('admin_token', 'token')

    const fetchMock = vi.fn(async (url, options) => {
      if (url.includes('/api/admin/llm/settings/active')) {
        return activeProviderResponse()
      }
      if (!options?.method && url.includes('/api/admin/llm/settings/default')) {
        return makeResponse({ ok: false, status: 404 })
      }
      if (!options?.method && url.includes('/api/admin/llm/news-cleansing')) {
        return makeResponse({
          ok: true,
          status: 200,
          json: async () => ({ provider: 'ollama', model: 'missing:model', batchSize: 12 })
        })
      }
      if (url.includes('/api/admin/ollama/status')) {
        return ollamaStatusResponse([])
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(AdminLlmSettings)
    await flushPromises()
    await flushPromises()

    expect(wrapper.text()).toContain('服务已运行，但当前未检测到已拉取模型，请先在下方「拉取模型」区域拉取至少一个模型。')
    expect(wrapper.text()).toContain('当前保存模型：missing:model（当前机器未拉取）')
    expect(wrapper.text()).toContain('新闻清洗当前不可保存或测试。当前保存模型：missing:model（当前机器未拉取）。请先到上方「🦙 Ollama 本地模型管理」拉取至少一个模型。')

    const saveButton = wrapper.findAll('button').find(button => button.text() === '保存')
    const testButtons = wrapper.findAll('button').filter(button => button.text().includes('测试连接'))

    expect(saveButton.attributes('disabled')).toBeDefined()
    expect(testButtons[1].attributes('disabled')).toBeDefined()

    const saveCall = fetchMock.mock.calls.find(args => args[0].includes('/api/admin/llm/news-cleansing') && args[1]?.method === 'PUT')
    const testCall = fetchMock.mock.calls.find(args => args[0].includes('/api/admin/llm/test/ollama') && args[1]?.method === 'POST')

    expect(saveCall).toBeFalsy()
    expect(testCall).toBeFalsy()
  })
})
