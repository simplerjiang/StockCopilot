import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import SourceGovernanceDeveloperMode from './SourceGovernanceDeveloperMode.vue'

const makeResponse = ({ ok, status, json, text }) => ({
  ok,
  status,
  json: json || (async () => ({})),
  text: text || (async () => '')
})

describe('SourceGovernanceDeveloperMode', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.restoreAllMocks()
  })

  it('renders main content directly without login', () => {
    const wrapper = mount(SourceGovernanceDeveloperMode)
    expect(wrapper.text()).toContain('来源治理 Developer Mode')
    expect(wrapper.text()).not.toContain('管理员登录')
  })

  it('loads dashboard after enabling developer mode', async () => {

    const fetchMock = vi.fn(async (url) => {
      const target = String(url)
      if (target.includes('/source-governance/overview')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ activeSources: 1, quarantinedSources: 0, pendingCandidates: 0, pendingChanges: 0, rollbackCount7d: 0, recentErrorCount24h: 0 }) })
      }
      if (target.includes('/source-governance/sources')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ items: [{ id: 1, domain: 'a.test', status: 'active', tier: 'preferred' }] }) })
      }
      if (target.includes('/source-governance/candidates')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ items: [] }) })
      }
      if (target.includes('/source-governance/changes')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ items: [] }) })
      }
      if (target.includes('/source-governance/llm-logs')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ items: [] }) })
      }
      if (target.includes('/source-governance/errors')) {
        return makeResponse({ ok: true, status: 200, json: async () => ([]) })
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(SourceGovernanceDeveloperMode)
    await flushPromises()

    const checkbox = wrapper.find('input[type="checkbox"]')
    await checkbox.setValue(true)
    await flushPromises()

    expect(fetchMock).toHaveBeenCalledWith('/api/admin/source-governance/overview')
    expect(wrapper.text()).toContain('a.test')
    expect(wrapper.text()).toContain('参数说明')
  })

  it('keeps developer mode checked after remount', async () => {

    const fetchMock = vi.fn(async (url) => {
      const target = String(url)
      if (target.includes('/source-governance/overview')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ activeSources: 1, quarantinedSources: 0, pendingCandidates: 0, pendingChanges: 0, rollbackCount7d: 0, recentErrorCount24h: 0 }) })
      }
      if (target.includes('/source-governance/sources') || target.includes('/source-governance/candidates') || target.includes('/source-governance/changes')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ items: [] }) })
      }
      if (target.includes('/source-governance/llm-logs')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ items: [] }) })
      }
      if (target.includes('/source-governance/errors')) {
        return makeResponse({ ok: true, status: 200, json: async () => ([]) })
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(SourceGovernanceDeveloperMode)
    await flushPromises()

    const checkbox = wrapper.find('input[type="checkbox"]')
    await checkbox.setValue(true)
    await flushPromises()
    wrapper.unmount()

    const remounted = mount(SourceGovernanceDeveloperMode)
    await flushPromises()
    const remountedCheckbox = remounted.find('input[type="checkbox"]')
    expect(remountedCheckbox.element.checked).toBe(true)
  })

  it('searches trace logs by trace id', async () => {

    const fetchMock = vi.fn(async (url) => {
      const target = String(url)
      if (target.includes('/source-governance/trace/')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ lines: ['trace line'] }) })
      }
      if (target.includes('/source-governance/overview')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ activeSources: 0, quarantinedSources: 0, pendingCandidates: 0, pendingChanges: 0, rollbackCount7d: 0, recentErrorCount24h: 0 }) })
      }
      if (target.includes('/source-governance/sources') || target.includes('/source-governance/candidates') || target.includes('/source-governance/changes')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ items: [] }) })
      }
      if (target.includes('/source-governance/llm-logs')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ items: [] }) })
      }
      if (target.includes('/source-governance/errors')) {
        return makeResponse({ ok: true, status: 200, json: async () => ([]) })
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(SourceGovernanceDeveloperMode)
    await flushPromises()

    await wrapper.find('input[type="checkbox"]').setValue(true)
    await flushPromises()

    const traceInput = wrapper.find('.trace-box input')
    await traceInput.setValue('abc12345')
    const traceButton = wrapper.findAll('button').find(button => button.text().includes('检索 Trace'))
    await traceButton.trigger('click')
    await flushPromises()

    const traceCall = fetchMock.mock.calls.find(args => String(args[0]).includes('/source-governance/trace/abc12345'))
    expect(traceCall).toBeTruthy()
    expect(wrapper.text()).toContain('trace line')
  })

  it('loads change detail and jumps to trace from queue item', async () => {

    const fetchMock = vi.fn(async (url) => {
      const target = String(url)
      if (target.includes('/source-governance/overview')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ activeSources: 0, quarantinedSources: 0, pendingCandidates: 0, pendingChanges: 0, rollbackCount7d: 0, recentErrorCount24h: 0 }) })
      }
      if (target.includes('/source-governance/sources') || target.includes('/source-governance/candidates')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ items: [] }) })
      }
      if (target.includes('/source-governance/changes/1')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ id: 1, status: 'generated', triggerReason: 'x', patchCount: 1, proposedReplayCommand: 'replay.cmd', proposedTestCommand: 'dotnet test', targetFiles: ['a.cs'], runs: [{ id: 2, traceId: 'trace-run-1', result: 'deployed', note: 'ok', executedAt: '2026-03-12' }] }) })
      }
      if (target.includes('/source-governance/changes')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ items: [{ id: 1, domain: 'a.test', status: 'generated', traceId: 'trace-change-1', latestRunResult: 'ok' }] }) })
      }
      if (target.includes('/source-governance/errors')) {
        return makeResponse({ ok: true, status: 200, json: async () => ([]) })
      }
      if (target.includes('/source-governance/llm-logs')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ items: [{ timestamp: '2026-03-12', level: 'LLM-AUDIT', status: 'response', provider: 'openai', model: 'gpt', traceId: 'trace-change-1', requestText: 'hello', responseText: 'world', errorText: '', requestRaw: 'request raw', responseRaw: 'response raw', errorRaw: '', lines: ['request raw', 'response raw'], raw: 'request raw\nresponse raw', stages: ['request', 'response'] }] }) })
      }
      if (target.includes('/source-governance/trace/trace-change-1')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ lines: ['line-a'], timeline: [] }) })
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(SourceGovernanceDeveloperMode)
    await flushPromises()

    await wrapper.find('input[type="checkbox"]').setValue(true)
    await flushPromises()

    const buttons = wrapper.findAll('button')
    const detailButton = buttons.find(button => button.text() === '详情')
    expect(detailButton).toBeTruthy()
    await detailButton.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('变更详情 #1')
    expect(wrapper.text()).toContain('replay.cmd')

    const traceJumpButton = wrapper.findAll('button').find(button => button.text() === '跳转 Trace')
    expect(traceJumpButton).toBeTruthy()
    await traceJumpButton.trigger('click')
    await flushPromises()

    const traceCall = fetchMock.mock.calls.find(args => String(args[0]).includes('/source-governance/trace/trace-change-1'))
    expect(traceCall).toBeTruthy()
    expect(wrapper.text()).toContain('LLM 对话过程日志')
  })

  it('opens full-screen log viewer and prettifies embedded json', async () => {

    const fetchMock = vi.fn(async (url) => {
      const target = String(url)
      if (target.includes('/source-governance/overview')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ activeSources: 0, quarantinedSources: 0, pendingCandidates: 0, pendingChanges: 0, rollbackCount7d: 0, recentErrorCount24h: 0 }) })
      }
      if (target.includes('/source-governance/sources') || target.includes('/source-governance/candidates') || target.includes('/source-governance/changes')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ items: [] }) })
      }
      if (target.includes('/source-governance/errors')) {
        return makeResponse({ ok: true, status: 200, json: async () => ([]) })
      }
      if (target.includes('/source-governance/llm-logs')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ items: [{ timestamp: '2026-03-12 10:00:00.000', level: 'LLM-AUDIT', status: 'response', provider: 'openai', model: 'gpt', traceId: 'trace-json-1', requestText: '{"prompt":"hello"}', responseText: '{"foo":1,"nested":{"bar":2}}', errorText: '', requestRaw: 'request raw', responseRaw: 'response raw', errorRaw: '', lines: ['request raw', 'response raw'], raw: 'request raw\nresponse raw', stages: ['request', 'response'] }] }) })
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(SourceGovernanceDeveloperMode)
    await flushPromises()

    await wrapper.find('input[type="checkbox"]').setValue(true)
    await flushPromises()

    const logItem = wrapper.find('.llm-log-item')
    expect(logItem.exists()).toBe(true)
    await logItem.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('LLM 日志详情')
    const jsonViews = wrapper.findAll('.log-viewer-json')
    expect(jsonViews[1].text()).toContain('"foo": 1')
    expect(jsonViews[1].text()).toContain('"bar": 2')
  })

  it('shows paired request and response content with prettified json', async () => {

    const fetchMock = vi.fn(async (url) => {
      const target = String(url)
      if (target.includes('/source-governance/overview')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ activeSources: 0, quarantinedSources: 0, pendingCandidates: 0, pendingChanges: 0, rollbackCount7d: 0, recentErrorCount24h: 0 }) })
      }
      if (target.includes('/source-governance/sources') || target.includes('/source-governance/candidates') || target.includes('/source-governance/changes')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ items: [] }) })
      }
      if (target.includes('/source-governance/errors')) {
        return makeResponse({ ok: true, status: 200, json: async () => ([]) })
      }
      if (target.includes('/source-governance/llm-logs')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ items: [{ timestamp: '2026-03-12 10:00:00.000', level: 'LLM-AUDIT', status: 'response', provider: 'openai', model: 'gpt', traceId: 'trace-request-1', requestText: '{"symbol":"600000","messages":[{"role":"user","content":"hello"}]}', responseText: '**Interpreting the Data** **Formulating the Response** {"answer":"ok","confidence":0.9}', errorText: '', requestRaw: 'request raw', responseRaw: 'response raw', errorRaw: '', lines: ['request raw', 'response raw'], raw: 'request raw\nresponse raw', stages: ['request', 'response'] }] }) })
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(SourceGovernanceDeveloperMode)
    await flushPromises()

    await wrapper.find('input[type="checkbox"]').setValue(true)
    await flushPromises()

    await wrapper.find('.llm-log-item').trigger('click')
    await flushPromises()

    const sections = wrapper.findAll('.log-viewer-section')
    expect(sections[0].text()).toContain('请求摘要')
    expect(sections[0].text()).toContain('"symbol":"600000"')
    expect(sections[2].text()).toContain('返回摘要')
    expect(sections[2].text()).toContain('{"answer":"ok","confidence":0.9}')

    const logListText = wrapper.find('.llm-log-list').text()
    expect(logListText).not.toContain('Interpreting the Data')
    expect(logListText).not.toContain('Formulating the Response')

    const jsonViews = wrapper.findAll('.log-viewer-json')
    expect(jsonViews[0].text()).toContain('"symbol": "600000"')
    expect(jsonViews[0].text()).toContain('"content": "hello"')
    expect(jsonViews[1].text()).toContain('"answer": "ok"')
    expect(jsonViews[1].text()).toContain('"confidence": 0.9')
  })

  it('redacts historical mixed-language non-json response summaries', async () => {

    const fetchMock = vi.fn(async (url) => {
      const target = String(url)
      if (target.includes('/source-governance/overview')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ activeSources: 0, quarantinedSources: 0, pendingCandidates: 0, pendingChanges: 0, rollbackCount7d: 0, recentErrorCount24h: 0 }) })
      }
      if (target.includes('/source-governance/sources') || target.includes('/source-governance/candidates') || target.includes('/source-governance/changes')) {
        return makeResponse({ ok: true, status: 200, json: async () => ({ items: [] }) })
      }
      if (target.includes('/source-governance/errors')) {
        return makeResponse({ ok: true, status: 200, json: async () => ([]) })
      }
      if (target.includes('/source-governance/llm-logs')) {
        return makeResponse({
          ok: true,
          status: 200,
          json: async () => ({
            items: [{
              timestamp: '2026-03-23 13:08:53.708',
              level: 'LLM-AUDIT',
              status: 'response',
              provider: 'default',
              model: 'gpt',
              traceId: 'trace-dirty-1',
              requestText: '请求内容已脱敏；界面仅保留必要元数据与结构化 JSON。',
              responseText: '中国最权威的财经媒体之一，深度报道和调查新闻质量高。" (One of the most authoritative financial media in China, with high-quality in-depth reports and investigative journalism.) For Eastmoney, it\'s `eastmoney`.',
              errorText: '',
              requestRaw: 'request raw',
              responseRaw: 'response raw',
              errorRaw: '',
              lines: ['request raw', 'response raw'],
              raw: 'request raw\nresponse raw',
              stages: ['request', 'response']
            }]
          })
        })
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(SourceGovernanceDeveloperMode)
    await flushPromises()

    await wrapper.find('input[type="checkbox"]').setValue(true)
    await flushPromises()

    const logListText = wrapper.find('.llm-log-list').text()
    expect(logListText).toContain('返回内容不是结构化 JSON，已按安全摘要收口。')
    expect(logListText).not.toContain('One of the most authoritative financial media in China')

    await wrapper.find('.llm-log-item').trigger('click')
    await flushPromises()

    const sections = wrapper.findAll('.log-viewer-section')
    expect(sections[1].text()).toContain('返回摘要')
    expect(sections[1].text()).toContain('返回内容不是结构化 JSON，已按安全摘要收口。')
    expect(wrapper.text()).not.toContain('One of the most authoritative financial media in China')
  })
})