import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import StockInfoTab from './StockInfoTab.vue'

const makeResponse = ({ ok, status, json, text }) => ({
  ok,
  status,
  json: json || (async () => ([])),
  text: text || (async () => '')
})

const flushPromises = () => new Promise(resolve => setTimeout(resolve, 0))
const findVisibleChatWindow = wrapper => wrapper.findAllComponents({ name: 'ChatWindow' }).find(component => component.isVisible())
const findChatWindowForSymbol = (wrapper, symbolKey) =>
  wrapper.findAllComponents({ name: 'ChatWindow' }).find(component => String(component.props('historyKey') || '').startsWith(symbolKey))

const createDeferred = () => {
  let resolve
  let reject
  const promise = new Promise((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

const createAbortError = () => Object.assign(new Error('Aborted'), { name: 'AbortError' })

const createAbortableResponse = (deferred, factory, signal, onAbort) => new Promise((resolve, reject) => {
  if (signal?.aborted) {
    onAbort?.()
    reject(createAbortError())
    return
  }

  const abortHandler = () => {
    onAbort?.()
    reject(createAbortError())
  }

  signal?.addEventListener('abort', abortHandler, { once: true })
  deferred.promise.then(
    () => {
      signal?.removeEventListener?.('abort', abortHandler)
      resolve(factory())
    },
    error => {
      signal?.removeEventListener?.('abort', abortHandler)
      reject(error)
    }
  )
})

const createChatFetchMock = (handlers = {}) => {
  const sessionsBySymbol = {}
  const messagesBySession = {}

  const ensureSessions = symbol => {
    if (!sessionsBySymbol[symbol]) {
      sessionsBySymbol[symbol] = [
        { sessionKey: `${symbol}-1`, title: '默认会话' }
      ]
    }
    return sessionsBySymbol[symbol]
  }

  const fetchMock = vi.fn(async (url, options = {}) => {
    if (handlers.handle) {
      const handled = await handlers.handle(url, options)
      if (handled) return handled
    }

    if (url.startsWith('/api/stocks/chat/sessions?')) {
      const params = new URLSearchParams(url.split('?')[1])
      const symbol = params.get('symbol') || ''
      const list = ensureSessions(symbol)
      return makeResponse({ ok: true, status: 200, json: async () => list })
    }

    if (url === '/api/stocks/chat/sessions' && options.method === 'POST') {
      const body = JSON.parse(options.body)
      const symbol = body.symbol
      const title = body.title || '默认会话'
      const key = `${symbol}-${Date.now()}`
      const entry = { sessionKey: key, title }
      sessionsBySymbol[symbol] = [entry, ...(sessionsBySymbol[symbol] || [])]
      return makeResponse({ ok: true, status: 200, json: async () => entry })
    }

    if (url.includes('/api/stocks/chat/sessions/') && url.endsWith('/messages')) {
      const sessionKey = url.split('/api/stocks/chat/sessions/')[1].replace('/messages', '')
      if (options.method === 'PUT') {
        const body = JSON.parse(options.body)
        messagesBySession[sessionKey] = body.messages || []
        return makeResponse({ ok: true, status: 200, json: async () => ({ status: 'ok' }) })
      }
      return makeResponse({ ok: true, status: 200, json: async () => messagesBySession[sessionKey] || [] })
    }

    if (url === '/api/stocks/sources') {
      return makeResponse({ ok: true, status: 200, json: async () => ([]) })
    }
    if (url === '/api/stocks/history') {
      return makeResponse({ ok: true, status: 200, json: async () => ([]) })
    }
    if (url.startsWith('/api/stocks/agents/history')) {
      return makeResponse({ ok: true, status: 200, json: async () => ([]) })
    }

    if (url.startsWith('/api/stocks/news/impact')) {
      return makeResponse({
        ok: true,
        status: 200,
        json: async () => ({
          summary: { positive: 0, neutral: 0, negative: 0, overall: '中性' },
          events: []
        })
      })
    }

    if (url.startsWith('/api/stocks/plans/alerts?')) {
      return makeResponse({ ok: true, status: 200, json: async () => ([]) })
    }

    if (url.startsWith('/api/news?')) {
      const params = new URLSearchParams(url.split('?')[1])
      const level = params.get('level') || 'stock'
      return makeResponse({
        ok: true,
        status: 200,
        json: async () => ({
          symbol: params.get('symbol') || '',
          level,
          sectorName: level === 'sector' ? '银行' : null,
          items: []
        })
      })
    }

    return makeResponse({ ok: false, status: 404 })
  })

  return { fetchMock, messagesBySession, sessionsBySymbol }
}

beforeEach(() => {
  vi.restoreAllMocks()
  localStorage.clear()
})

describe('StockInfoTab', () => {
  it('renders search input and button', () => {
    const wrapper = mount(StockInfoTab)
    const input = wrapper.find('input')
    const button = wrapper.find('button')

    expect(input.exists()).toBe(true)
    expect(button.exists()).toBe(true)
  })

  it('renders terminal workspace with copilot sidebar', () => {
    const wrapper = mount(StockInfoTab)

    expect(wrapper.text()).toContain('TerminalView')
    expect(wrapper.text()).toContain('CopilotPanel')
    expect(wrapper.find('.workspace-grid').exists()).toBe(true)
    expect(wrapper.find('.sticky-toolbar').exists()).toBe(true)
  })

  it('starts search when pressing Enter in symbol input', async () => {
    const { fetchMock } = createChatFetchMock({
      handle: async (url) => {
        if (url.startsWith('/api/stocks/search?')) {
          return makeResponse({ ok: true, status: 200, json: async () => ([]) })
        }
        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    const searchInput = wrapper.find('.search-field input')

    await searchInput.setValue('平安银行')
    await searchInput.trigger('keydown.enter')
    await flushPromises()

    const searchCall = fetchMock.mock.calls.find(args => String(args[0]).startsWith('/api/stocks/search?'))
    expect(searchCall).toBeTruthy()
    expect(searchCall[0]).toContain('q=%E5%B9%B3%E5%AE%89%E9%93%B6%E8%A1%8C')
  })

  it('sends chat prompt with selected stock context', async () => {
    const { fetchMock } = createChatFetchMock({
      handle: async (url) => {
        if (url === '/api/llm/chat/stream/openai') {
          return makeResponse({ ok: true, status: 200, json: async () => ({ content: 'ok' }) })
        }
        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: {
        name: '深科技',
        symbol: 'sz000021',
        price: 31.1,
        change: -0.72,
        changePercent: -2.26,
        high: 32,
        low: 30,
        timestamp: '2026-01-29T00:00:00Z'
      },
      kLines: [],
      minuteLines: [],
      messages: [],
      fundamentalSnapshot: {
        updatedAt: '2026-01-29T08:30:00Z',
        facts: [
          { label: '公司全称', value: '深圳长城开发科技股份有限公司', source: '东方财富公司概况' },
          { label: '上市交易所', value: '深圳证券交易所', source: '东方财富公司概况' }
        ]
      }
    }
    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()
    const chatWindow = wrapper.findComponent({ name: 'ChatWindow' })
    chatWindow.vm.chatInput = '今天走势如何？'

    await chatWindow.vm.sendChat()
    await wrapper.vm.$nextTick()

    const call = fetchMock.mock.calls.find(args => args[0] === '/api/llm/chat/stream/openai')
    expect(call).toBeTruthy()
    const body = JSON.parse(call[1].body)
    expect(body.prompt).toContain('sz000021')
    expect(body.prompt).toContain('今天走势如何？')
    expect(body.useInternet).toBe(true)
  })

  it('shows loading indicator while chatting', async () => {
    const { fetchMock } = createChatFetchMock({
      handle: async (url) => {
        if (url === '/api/llm/chat/stream/openai') {
          return makeResponse({ ok: true, status: 200, json: async () => ({ content: 'ok' }) })
        }
        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '深科技', symbol: 'sz000021', price: 31.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()
    await flushPromises()
    const chatWindow = wrapper.findComponent({ name: 'ChatWindow' })

    chatWindow.vm.chatInput = '测试'
    const pending = chatWindow.vm.sendChat()

    expect(chatWindow.vm.chatLoading).toBe(true)
    await pending
  })

  it('includes time-check question in chat prompt', async () => {
    const { fetchMock } = createChatFetchMock({
      handle: async (url) => {
        if (url === '/api/llm/chat/stream/openai') {
          return makeResponse({ ok: true, status: 200, json: async () => ({ content: 'ok' }) })
        }
        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '深科技', symbol: 'sz000021', price: 31.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()
    await flushPromises()

    const question = '询问今日时间是不是2026年1月29号'
    const chatWindow = wrapper.findComponent({ name: 'ChatWindow' })
    chatWindow.vm.chatInput = question
    await chatWindow.vm.sendChat()

    const call = fetchMock.mock.calls.find(args => args[0] === '/api/llm/chat/stream/openai')
    const body = JSON.parse(call[1].body)
    expect(body.prompt).toContain(question)
    expect(body.useInternet).toBe(true)
  })

  it('renders Step3 fundamentals in terminal summary', async () => {
    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: {
        name: '深科技',
        symbol: 'sz000021',
        price: 31.1,
        change: -0.72,
        changePercent: -2.26,
        high: 32,
        low: 30,
        peRatio: 18.3,
        floatMarketCap: 12340000000,
        volumeRatio: 1.42,
        shareholderCount: 56000,
        sectorName: '半导体',
        timestamp: '2026-01-29T00:00:00Z'
      },
      kLines: [],
      minuteLines: [],
      messages: [],
      fundamentalSnapshot: {
        updatedAt: '2026-01-29T08:30:00Z',
        facts: [
          { label: '公司全称', value: '深圳长城开发科技股份有限公司', source: '东方财富公司概况' },
          { label: '上市交易所', value: '深圳证券交易所', source: '东方财富公司概况' }
        ]
      }
    }

    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('基本面快照')
    expect(wrapper.text()).toContain('市盈率：18.3')
    expect(wrapper.text()).toContain('量比：1.42')
    expect(wrapper.text()).toContain('所属板块：半导体')
    expect(wrapper.text()).toContain('公司全称：深圳长城开发科技股份有限公司')
    expect(wrapper.text()).toContain('上市交易所：深圳证券交易所')
  })

  it('sends pro flag when triggering Pro analysis', async () => {
    const agentCalls = []
    const { fetchMock } = createChatFetchMock({
      handle: async (url, options = {}) => {
        if (url === '/api/stocks/agents/single') {
          const body = JSON.parse(options.body)
          agentCalls.push(body)
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({ agentId: body.agentId, agentName: body.agentId, success: true, data: { summary: body.agentId } })
          })
        }

        if (url === '/api/stocks/agents/history' && options.method === 'POST') {
          return makeResponse({ ok: true, status: 200, json: async () => ({ id: 1 }) })
        }

        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '深科技', symbol: 'sz000021', price: 31.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()
    await flushPromises()

    await wrapper.find('.run-pro-button').trigger('click')
    await flushPromises()
    await flushPromises()

    expect(agentCalls).toHaveLength(5)
    expect(agentCalls.every(item => item.isPro === true)).toBe(true)
  })

  it('sends standard flag when triggering regular analysis', async () => {
    const agentCalls = []
    const { fetchMock } = createChatFetchMock({
      handle: async (url, options = {}) => {
        if (url === '/api/stocks/agents/single') {
          const body = JSON.parse(options.body)
          agentCalls.push(body)
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({ agentId: body.agentId, agentName: body.agentId, success: true, data: { summary: body.agentId } })
          })
        }

        if (url === '/api/stocks/agents/history' && options.method === 'POST') {
          return makeResponse({ ok: true, status: 200, json: async () => ({ id: 1 }) })
        }

        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '深科技', symbol: 'sz000021', price: 31.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()
    await flushPromises()

    await wrapper.find('.run-standard-button').trigger('click')
    await flushPromises()
    await flushPromises()

    expect(agentCalls).toHaveLength(5)
    expect(agentCalls.every(item => item.isPro === false)).toBe(true)
  })

  it('streams assistant response chunks and persists history per stock', async () => {
    const encoder = new TextEncoder()
    const stream = new ReadableStream({
      start(controller) {
        controller.enqueue(encoder.encode('data: 你好\n\n'))
        controller.enqueue(encoder.encode('data: 世界\n\n'))
        controller.enqueue(encoder.encode('data: [DONE]\n\n'))
        controller.close()
      }
    })

    const { fetchMock, messagesBySession } = createChatFetchMock({
      handle: async (url) => {
        if (url === '/api/llm/chat/stream/openai') {
          return {
            ok: true,
            status: 200,
            body: stream,
            text: async () => '',
            json: async () => ({})
          }
        }
        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '深科技', symbol: 'sz000021', price: 31.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()
    await flushPromises()
    const chatWindow = wrapper.findComponent({ name: 'ChatWindow' })

    chatWindow.vm.chatInput = '测试流式'
    await chatWindow.vm.sendChat()
    await flushPromises()

    const assistant = chatWindow.vm.chatMessages.find(item => item.role === 'assistant')
    expect(assistant.content).toBe('你好世界')

    const sessionKey = wrapper.vm.selectedChatSession
    expect(messagesBySession[sessionKey]?.some(item => item.content === '你好世界')).toBe(true)

    wrapper.vm.detail = {
      quote: { name: '平安银行', symbol: 'sz000001', price: 12, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()

    wrapper.vm.detail = {
      quote: { name: '深科技', symbol: 'sz000021', price: 31.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()
    await flushPromises()
    const restored = chatWindow.vm.chatMessages.find(item => item.role === 'assistant')
    expect(restored?.content).toBe('你好世界')
  })

  it('keeps chat history per stock and switches on symbol change', async () => {
    const { fetchMock, messagesBySession } = createChatFetchMock()

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '深科技', symbol: 'sz000021', price: 31.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()
    let chatWindow = findChatWindowForSymbol(wrapper, 'sz000021')
    chatWindow.vm.chatMessages = [{ role: 'assistant', content: 'A', timestamp: '2026-01-29T00:00:00Z' }]
    await wrapper.vm.$nextTick()
    messagesBySession[wrapper.vm.selectedChatSession] = chatWindow.vm.chatMessages

    wrapper.vm.detail = {
      quote: { name: '平安银行', symbol: 'sz000001', price: 12, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()
    chatWindow = findChatWindowForSymbol(wrapper, 'sz000001')
    wrapper.vm.selectedChatSession = 'sz000001-1'
    await wrapper.vm.$nextTick()
    await flushPromises()

    chatWindow.vm.chatMessages = [{ role: 'assistant', content: 'B', timestamp: '2026-01-29T00:00:00Z' }]
    await wrapper.vm.$nextTick()
    messagesBySession[wrapper.vm.selectedChatSession] = chatWindow.vm.chatMessages

    wrapper.vm.detail = {
      quote: { name: '深科技', symbol: 'sz000021', price: 31.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()
    chatWindow = findChatWindowForSymbol(wrapper, 'sz000021')
    expect(chatWindow.vm.chatMessages[0].content).toBe('A')
  })

  it('keeps prior stock sidebar cards mounted and only hides them on symbol switch', async () => {
    const { fetchMock } = createChatFetchMock()
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)

    wrapper.vm.detail = {
      quote: { name: '浦发银行', symbol: 'sh600000', price: 10.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()

    wrapper.vm.detail = {
      quote: { name: '贵州茅台', symbol: 'sh600519', price: 20.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()

    const chatWindows = wrapper.findAllComponents({ name: 'ChatWindow' })
    expect(chatWindows).toHaveLength(2)
    expect(chatWindows.filter(component => component.isVisible())).toHaveLength(1)
    expect(wrapper.text()).toContain('贵州茅台')
  })

  it('allows creating a new chat for current stock', async () => {
    const { fetchMock } = createChatFetchMock()

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '深科技', symbol: 'sz000021', price: 31.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()
    const chatWindow = wrapper.findComponent({ name: 'ChatWindow' })
    chatWindow.vm.chatMessages = [{ role: 'assistant', content: '旧记录', timestamp: '2026-01-29T00:00:00Z' }]
    await wrapper.vm.$nextTick()

    await wrapper.find('.chat-session-new').trigger('click')
    await wrapper.vm.$nextTick()

    expect(chatWindow.vm.chatMessages.length).toBe(0)
  })

  it('renders markdown in chat content', async () => {
    const { fetchMock } = createChatFetchMock()

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '深科技', symbol: 'sz000021', price: 31.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()
    const chatWindow = wrapper.findComponent({ name: 'ChatWindow' })
    chatWindow.vm.chatMessages = [
      {
        role: 'assistant',
        content: '**加粗**\n\n- 列表项',
        timestamp: '2026-01-29T00:00:00Z'
      }
    ]

    await wrapper.vm.$nextTick()

    const html = wrapper.find('.chat-content').html()
    expect(html).toContain('<strong>加粗</strong>')
    expect(html).toContain('<li>列表项</li>')
  })

  it('allows switching between chat sessions for same stock', async () => {
    const { fetchMock, messagesBySession } = createChatFetchMock()

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '深科技', symbol: 'sz000021', price: 31.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()

    const chatWindow = wrapper.findComponent({ name: 'ChatWindow' })
    chatWindow.vm.chatMessages = [{ role: 'assistant', content: '历史A', timestamp: '2026-01-29T00:00:00Z' }]
    await wrapper.vm.$nextTick()
    await wrapper.vm.$nextTick()
    messagesBySession[wrapper.vm.selectedChatSession] = chatWindow.vm.chatMessages

    const oldSessionKey = wrapper.vm.selectedChatSession

    await wrapper.find('.chat-session-new').trigger('click')
    await wrapper.vm.$nextTick()

    chatWindow.vm.chatMessages = [{ role: 'assistant', content: '历史B', timestamp: '2026-01-29T00:00:00Z' }]
    await wrapper.vm.$nextTick()
    await wrapper.vm.$nextTick()
    messagesBySession[wrapper.vm.selectedChatSession] = chatWindow.vm.chatMessages

    const selector = wrapper.find('.chat-session select')
    const options = selector.findAll('option')
    expect(options.length).toBeGreaterThan(1)

    await selector.setValue(oldSessionKey)
    await wrapper.vm.$nextTick()
    await wrapper.vm.$nextTick()

    const restored = chatWindow.vm.chatMessages.find(item => item.role === 'assistant')
    expect(restored?.content).toBe('历史A')
  })

  it('renders news impact summary when data is available', async () => {
    const { fetchMock } = createChatFetchMock({
      handle: async (url) => {
        if (url.startsWith('/api/stocks/news/impact')) {
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              summary: { positive: 2, neutral: 1, negative: 0, overall: '利好偏多' },
              events: [{ title: '公司宣布回购', category: '利好', impactScore: 60 }]
            })
          })
        }
        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '深科技', symbol: 'sz000021', price: 31.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()

    expect(wrapper.text()).toContain('资讯影响')
    expect(wrapper.text()).toContain('利好偏多')
  })

  it('requests local news buckets for the active symbol', async () => {
    const { fetchMock } = createChatFetchMock()
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '浦发银行', symbol: 'sh600000', price: 10.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }

    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()

    const localNewsCalls = fetchMock.mock.calls.filter(args => String(args[0]).startsWith('/api/news?'))
    expect(localNewsCalls.length).toBeGreaterThanOrEqual(3)
    expect(localNewsCalls.some(args => String(args[0]).includes('symbol=sh600000') && String(args[0]).includes('level=stock'))).toBe(true)
    expect(localNewsCalls.some(args => String(args[0]).includes('symbol=sh600000') && String(args[0]).includes('level=sector'))).toBe(true)
    expect(localNewsCalls.some(args => !String(args[0]).includes('symbol=sh600000') && String(args[0]).includes('level=market'))).toBe(true)
  })

  it('does not reload market news when switching stocks', async () => {
    const { fetchMock } = createChatFetchMock()
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    await flushPromises()
    await flushPromises()

    wrapper.vm.detail = {
      quote: { name: '浦发银行', symbol: 'sh600000', price: 10.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }

    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()

    wrapper.vm.detail = {
      quote: { name: '贵州茅台', symbol: 'sh600519', price: 20.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }

    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()

    const marketCalls = fetchMock.mock.calls.filter(args => String(args[0]).startsWith('/api/news?') && String(args[0]).includes('level=market'))
    expect(marketCalls).toHaveLength(1)
  })

  it('reuses sidebar data when switching back to a previously loaded stock', async () => {
    const counters = {
      impact: {},
      news: {},
      chatSessions: {},
      agentHistory: {}
    }

    const bump = (bucket, key) => {
      bucket[key] = (bucket[key] || 0) + 1
    }

    const { fetchMock } = createChatFetchMock({
      handle: async url => {
        const text = String(url)

        if (text.startsWith('/api/stocks/news/impact?')) {
          const params = new URLSearchParams(text.split('?')[1])
          const currentSymbol = params.get('symbol') || ''
          bump(counters.impact, currentSymbol)
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              summary: { positive: 0, neutral: 0, negative: 0, overall: '中性' },
              events: []
            })
          })
        }

        if (text.startsWith('/api/news?')) {
          const params = new URLSearchParams(text.split('?')[1])
          const level = params.get('level') || 'stock'
          const currentSymbol = params.get('symbol') || level
          bump(counters.news, `${currentSymbol}:${level}`)
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              symbol: params.get('symbol') || '',
              level,
              sectorName: level === 'sector' ? '银行' : null,
              items: []
            })
          })
        }

        if (text.startsWith('/api/stocks/chat/sessions?')) {
          const params = new URLSearchParams(text.split('?')[1])
          const currentSymbol = params.get('symbol') || ''
          bump(counters.chatSessions, currentSymbol)
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ([{ sessionKey: `${currentSymbol}-1`, title: '默认会话' }])
          })
        }

        if (text.startsWith('/api/stocks/agents/history?')) {
          const params = new URLSearchParams(text.split('?')[1])
          const currentSymbol = params.get('symbol') || ''
          bump(counters.agentHistory, currentSymbol)
          return makeResponse({ ok: true, status: 200, json: async () => ([]) })
        }

        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)

    wrapper.vm.detail = {
      quote: { name: '浦发银行', symbol: 'sh600000', price: 10.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()

    wrapper.vm.detail = {
      quote: { name: '贵州茅台', symbol: 'sh600519', price: 20.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()

    wrapper.vm.detail = {
      quote: { name: '浦发银行', symbol: 'sh600000', price: 10.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()

    expect(counters.impact.sh600000).toBe(1)
    expect(counters.news['sh600000:stock']).toBe(1)
    expect(counters.news['sh600000:sector']).toBe(1)
    expect(counters.chatSessions.sh600000).toBe(1)
    expect(counters.agentHistory.sh600000).toBe(1)
    expect(counters.impact.sh600519).toBe(1)
    expect(counters.news['sh600519:stock']).toBe(1)
    expect(counters.news['sh600519:sector']).toBe(1)
    expect(counters.chatSessions.sh600519).toBe(1)
    expect(counters.agentHistory.sh600519).toBe(1)
  })

  it('allows manual news-impact refresh even after sidebar data is cached', async () => {
    let impactCalls = 0

    const { fetchMock } = createChatFetchMock({
      handle: async url => {
        const text = String(url)
        if (text.startsWith('/api/stocks/news/impact?')) {
          impactCalls += 1
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              summary: { positive: impactCalls, neutral: 0, negative: 0, overall: '利好偏多' },
              events: []
            })
          })
        }
        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '浦发银行', symbol: 'sh600000', price: 10.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }

    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()

    expect(impactCalls).toBe(1)

    const refreshButton = wrapper.find('.news-impact-header button')
    await refreshButton.trigger('click')
    await flushPromises()
    await flushPromises()

    expect(impactCalls).toBe(2)
    expect(wrapper.text()).toContain('利好偏多')
  })

  it('loads market news without requiring a selected stock', async () => {
    const { fetchMock } = createChatFetchMock()
    vi.stubGlobal('fetch', fetchMock)

    mount(StockInfoTab)
    await flushPromises()
    await flushPromises()

    const marketCall = fetchMock.mock.calls.find(args => String(args[0]).startsWith('/api/news?') && String(args[0]).includes('level=market'))
    expect(marketCall).toBeTruthy()
    expect(String(marketCall[0])).not.toContain('symbol=')
  })

  it('renders full history list without slicing to 10 items', async () => {
    const histories = Array.from({ length: 12 }, (_, index) => ({
      id: index + 1,
      symbol: `sh6000${String(index).padStart(2, '0')}`,
      name: `标的${index + 1}`,
      changePercent: index,
      updatedAt: '2026-03-13T00:00:00Z'
    }))

    const { fetchMock } = createChatFetchMock({
      handle: async url => {
        if (url === '/api/stocks/history') {
          return makeResponse({ ok: true, status: 200, json: async () => histories })
        }
        return null
      }
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    await flushPromises()
    await flushPromises()

    expect(wrapper.findAll('.history-chip')).toHaveLength(12)
    expect(wrapper.text()).toContain('标的12')
  })

  it('loads stock detail immediately when clicking a recent-history item', async () => {
    const liveDetail = createDeferred()
    const { fetchMock } = createChatFetchMock({
      handle: async url => {
        if (url === '/api/stocks/history') {
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ([
              {
                id: 1,
                symbol: '600000',
                name: '浦发银行',
                changePercent: 1.2,
                updatedAt: '2026-03-13T00:00:00Z'
              }
            ])
          })
        }

        if (String(url).startsWith('/api/stocks/detail/cache?')) {
          const params = new URLSearchParams(String(url).split('?')[1])
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              quote: {
                name: '浦发银行',
                symbol: params.get('symbol') || '',
                price: 9.9,
                change: 0,
                changePercent: 0
              },
              kLines: [],
              minuteLines: [],
              messages: []
            })
          })
        }

        if (String(url).startsWith('/api/stocks/detail?')) {
          const params = new URLSearchParams(String(url).split('?')[1])
          return liveDetail.promise.then(() => makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              quote: {
                name: '浦发银行',
                symbol: params.get('symbol') || '',
                price: 10.1,
                change: 0,
                changePercent: 0
              },
              kLines: [],
              minuteLines: [],
              messages: []
            })
          }))
        }

        return null
      }
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    await flushPromises()
    await flushPromises()

    await wrapper.find('.history-chip').trigger('click')
    await flushPromises()
    await flushPromises()

    const cacheCall = fetchMock.mock.calls.find(args => String(args[0]).startsWith('/api/stocks/detail/cache?'))
    const detailCall = fetchMock.mock.calls.find(args => String(args[0]).startsWith('/api/stocks/detail?'))
    expect(cacheCall).toBeTruthy()
    expect(detailCall).toBeTruthy()
    expect(String(detailCall[0])).toContain('symbol=sh600000')
    expect(wrapper.vm.detail?.quote?.price).toBe(9.9)
    expect(wrapper.vm.loading).toBe(true)
    expect(wrapper.find('.search-field button').attributes('disabled')).toBeUndefined()
    expect(wrapper.text()).toContain('后台刷新中...')

    liveDetail.resolve()
    await flushPromises()
    await flushPromises()

    expect(wrapper.vm.detail?.quote?.symbol).toBe('sh600000')
    expect(wrapper.vm.detail?.quote?.price).toBe(10.1)
    expect(wrapper.vm.loading).toBe(false)
  })

  it('keeps stock switching interactive while live refresh is still pending', async () => {
    const firstLiveDetail = createDeferred()
    const secondLiveDetail = createDeferred()

    const buildDetail = (symbol, price) => ({
      quote: {
        name: symbol === 'sh600519' ? '贵州茅台' : '浦发银行',
        symbol,
        price,
        change: 0,
        changePercent: 0
      },
      kLines: [],
      minuteLines: [],
      messages: []
    })

    const { fetchMock } = createChatFetchMock({
      handle: async url => {
        const text = String(url)

        if (text.startsWith('/api/stocks/detail/cache?')) {
          const params = new URLSearchParams(text.split('?')[1])
          const currentSymbol = params.get('symbol') || ''
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => buildDetail(currentSymbol, currentSymbol === 'sh600519' ? 20 : 10)
          })
        }

        if (text.startsWith('/api/stocks/detail?')) {
          const params = new URLSearchParams(text.split('?')[1])
          const currentSymbol = params.get('symbol') || ''
          if (currentSymbol === 'sh600000') {
            return firstLiveDetail.promise.then(() => makeResponse({ ok: true, status: 200, json: async () => buildDetail(currentSymbol, 10.1) }))
          }
          if (currentSymbol === 'sh600519') {
            return secondLiveDetail.promise.then(() => makeResponse({ ok: true, status: 200, json: async () => buildDetail(currentSymbol, 20.1) }))
          }
        }

        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    await flushPromises()
    await flushPromises()

    const input = wrapper.find('.search-field input')
    const button = wrapper.find('.search-field button')

    await input.setValue('600000')
    await button.trigger('click')
    await flushPromises()
    await flushPromises()

    expect(wrapper.vm.detail?.quote?.symbol).toBe('sh600000')
    expect(wrapper.vm.loading).toBe(true)
    expect(button.attributes('disabled')).toBeUndefined()

    await input.setValue('600519')
    await button.trigger('click')
    await flushPromises()
    await flushPromises()

    expect(wrapper.vm.detail?.quote?.symbol).toBe('sh600519')
    expect(button.attributes('disabled')).toBeUndefined()

    secondLiveDetail.resolve()
    firstLiveDetail.resolve()
    await flushPromises()
    await flushPromises()

    expect(wrapper.vm.detail?.quote?.symbol).toBe('sh600519')
    expect(wrapper.vm.loading).toBe(false)
  })

  it('ignores stale detail responses when switching stocks quickly', async () => {
    const firstLiveDetail = createDeferred()
    const { fetchMock } = createChatFetchMock({
      handle: async url => {
        if (String(url).startsWith('/api/stocks/detail/cache?')) {
          const params = new URLSearchParams(String(url).split('?')[1])
          const currentSymbol = params.get('symbol') || ''
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              quote: {
                name: currentSymbol === 'sh600519' ? '贵州茅台' : '浦发银行',
                symbol: currentSymbol,
                price: currentSymbol === 'sh600519' ? 20 : 10,
                change: 0,
                changePercent: 0
              },
              kLines: [],
              minuteLines: [],
              messages: []
            })
          })
        }

        if (String(url).startsWith('/api/stocks/detail?')) {
          const params = new URLSearchParams(String(url).split('?')[1])
          const currentSymbol = params.get('symbol') || ''
          if (currentSymbol === 'sh600000') {
            return firstLiveDetail.promise.then(() => makeResponse({
              ok: true,
              status: 200,
              json: async () => ({
                quote: {
                  name: '浦发银行',
                  symbol: currentSymbol,
                  price: 10.1,
                  change: 0,
                  changePercent: 0
                },
                kLines: [],
                minuteLines: [],
                messages: []
              })
            }))
          }

          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              quote: {
                name: '贵州茅台',
                symbol: currentSymbol,
                price: 20.1,
                change: 0,
                changePercent: 0
              },
              kLines: [],
              minuteLines: [],
              messages: []
            })
          })
        }

        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    await flushPromises()
    await flushPromises()

    const input = wrapper.find('.search-field input')
    await input.setValue('600000')
    await input.trigger('keydown.enter')
    await flushPromises()

    expect(wrapper.vm.detail?.quote?.symbol).toBe('sh600000')

    await input.setValue('600519')
    await input.trigger('keydown.enter')
    await flushPromises()
    await flushPromises()

    expect(wrapper.vm.detail?.quote?.symbol).toBe('sh600519')
    expect(wrapper.vm.detail?.quote?.price).toBe(20.1)

    firstLiveDetail.resolve()
    await flushPromises()
    await flushPromises()

    expect(wrapper.vm.detail?.quote?.symbol).toBe('sh600519')
    expect(wrapper.vm.detail?.quote?.price).toBe(20.1)
  })

  it('keeps prior stock requests running when switching stocks quickly', async () => {
    const firstLiveDetail = createDeferred()
    const firstNewsImpact = createDeferred()
    const firstLocalNews = createDeferred()
    const firstChatSessions = createDeferred()
    const firstAgentHistory = createDeferred()
    const aborted = {
      detail: false,
      newsImpact: false,
      localNews: false,
      chatSessions: false,
      agentHistory: false
    }

    const buildDetailPayload = (symbol, price) => ({
      quote: {
        name: symbol === 'sh600519' ? '贵州茅台' : '浦发银行',
        symbol,
        price,
        change: 0,
        changePercent: 0
      },
      kLines: [],
      minuteLines: [],
      messages: []
    })

    const { fetchMock } = createChatFetchMock({
      handle: async (url, options = {}) => {
        const text = String(url)

        if (text.startsWith('/api/stocks/detail/cache?')) {
          const params = new URLSearchParams(text.split('?')[1])
          const currentSymbol = params.get('symbol') || ''
          return makeResponse({ ok: true, status: 200, json: async () => buildDetailPayload(currentSymbol, currentSymbol === 'sh600519' ? 20 : 10) })
        }

        if (text.startsWith('/api/stocks/detail?')) {
          const params = new URLSearchParams(text.split('?')[1])
          const currentSymbol = params.get('symbol') || ''
          if (currentSymbol === 'sh600000') {
            return createAbortableResponse(
              firstLiveDetail,
              () => makeResponse({ ok: true, status: 200, json: async () => buildDetailPayload(currentSymbol, 10.1) }),
              options.signal,
              () => { aborted.detail = true }
            )
          }
          return makeResponse({ ok: true, status: 200, json: async () => buildDetailPayload(currentSymbol, 20.1) })
        }

        if (text.startsWith('/api/stocks/news/impact?')) {
          const params = new URLSearchParams(text.split('?')[1])
          const currentSymbol = params.get('symbol') || ''
          if (currentSymbol === 'sh600000') {
            return createAbortableResponse(
              firstNewsImpact,
              () => makeResponse({ ok: true, status: 200, json: async () => ({ summary: { positive: 0, neutral: 0, negative: 0, overall: '中性' }, events: [] }) }),
              options.signal,
              () => { aborted.newsImpact = true }
            )
          }
        }

        if (text.startsWith('/api/news?')) {
          const params = new URLSearchParams(text.split('?')[1])
          if (params.get('symbol') === 'sh600000' && params.get('level') === 'stock') {
            return createAbortableResponse(
              firstLocalNews,
              () => makeResponse({ ok: true, status: 200, json: async () => ({ symbol: 'sh600000', level: 'stock', items: [] }) }),
              options.signal,
              () => { aborted.localNews = true }
            )
          }
        }

        if (text.startsWith('/api/stocks/chat/sessions?')) {
          const params = new URLSearchParams(text.split('?')[1])
          const currentSymbol = params.get('symbol') || ''
          if (currentSymbol === 'sh600000') {
            return createAbortableResponse(
              firstChatSessions,
              () => makeResponse({ ok: true, status: 200, json: async () => ([{ sessionKey: 'sh600000-1', title: '默认会话' }]) }),
              options.signal,
              () => { aborted.chatSessions = true }
            )
          }
        }

        if (text.startsWith('/api/stocks/agents/history?')) {
          const params = new URLSearchParams(text.split('?')[1])
          const currentSymbol = params.get('symbol') || ''
          if (currentSymbol === 'sh600000') {
            return createAbortableResponse(
              firstAgentHistory,
              () => makeResponse({ ok: true, status: 200, json: async () => ([]) }),
              options.signal,
              () => { aborted.agentHistory = true }
            )
          }
        }

        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    await flushPromises()
    await flushPromises()

    const input = wrapper.find('.search-field input')
    await input.setValue('600000')
    await input.trigger('keydown.enter')
    await flushPromises()
    await flushPromises()

    await input.setValue('600519')
    await input.trigger('keydown.enter')
    await flushPromises()
    await flushPromises()

    expect(aborted.detail).toBe(false)
    expect(aborted.newsImpact).toBe(false)
    expect(aborted.localNews).toBe(false)
    expect(aborted.chatSessions).toBe(false)
    expect(aborted.agentHistory).toBe(false)
    expect(wrapper.vm.detail?.quote?.symbol).toBe('sh600519')
    expect(wrapper.vm.error).toBe('')

    firstLiveDetail.resolve()
    firstNewsImpact.resolve()
    firstLocalNews.resolve()
    firstChatSessions.resolve()
    firstAgentHistory.resolve()
  })

  it('preserves in-progress agent tasks when switching away and back to a stock', async () => {
    const firstAgentDeferred = createDeferred()
    const buildDetail = (symbol, price) => ({
      quote: {
        name: symbol === 'sh600519' ? '贵州茅台' : '浦发银行',
        symbol,
        price,
        change: 0,
        changePercent: 0
      },
      kLines: [{ date: '2026-03-14', open: price, close: price, low: price, high: price, volume: 10 }],
      minuteLines: [],
      messages: []
    })

    const agentCalls = []
    const { fetchMock } = createChatFetchMock({
      handle: async (url, options = {}) => {
        const text = String(url)

        if (text.startsWith('/api/stocks/detail/cache?') || text.startsWith('/api/stocks/detail?')) {
          const params = new URLSearchParams(text.split('?')[1])
          const currentSymbol = params.get('symbol') || ''
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => buildDetail(currentSymbol, currentSymbol === 'sh600519' ? 20 : 10)
          })
        }

        if (text === '/api/stocks/agents/single') {
          const body = JSON.parse(options.body)
          agentCalls.push(body)
          const responseFactory = () => makeResponse({
            ok: true,
            status: 200,
            json: async () => ({ agentId: body.agentId, agentName: body.agentId, success: true, data: { summary: `${body.symbol}-${body.agentId}` } })
          })

          if (body.symbol === 'sh600000' && body.agentId === 'stock_news') {
            return firstAgentDeferred.promise.then(responseFactory)
          }

          return responseFactory()
        }

        if (text === '/api/stocks/agents/history' && options.method === 'POST') {
          return makeResponse({ ok: true, status: 200, json: async () => ({ id: 1 }) })
        }

        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    await flushPromises()
    await flushPromises()

    const input = wrapper.find('.search-field input')
    const queryButton = wrapper.find('.search-field button')

    await input.setValue('600000')
    await queryButton.trigger('click')
    await flushPromises()
    await flushPromises()

    await wrapper.find('.run-standard-button').trigger('click')
    await flushPromises()

    expect(wrapper.vm.agentLoading).toBe(true)

    await input.setValue('600519')
    await queryButton.trigger('click')
    await flushPromises()
    await flushPromises()

    expect(wrapper.vm.detail?.quote?.symbol).toBe('sh600519')
    expect(wrapper.vm.agentLoading).toBe(false)

    firstAgentDeferred.resolve()
    await flushPromises()
    await flushPromises()
    await flushPromises()

    await input.setValue('600000')
    await queryButton.trigger('click')
    await flushPromises()
    await flushPromises()

    expect(wrapper.vm.detail?.quote?.symbol).toBe('sh600000')
    expect(wrapper.vm.agentLoading).toBe(false)
    expect(wrapper.vm.agentResults).toHaveLength(5)
    expect(wrapper.text()).toContain('sh600000-stock_news')
    expect(agentCalls.filter(item => item.symbol === 'sh600000')).toHaveLength(5)
  })

  it('keeps market news visible in the embedded panel when the AI sidebar is collapsed', async () => {
    const { fetchMock } = createChatFetchMock({
      handle: async (url) => {
        if (url.startsWith('/api/news?')) {
          const params = new URLSearchParams(url.split('?')[1])
          const level = params.get('level') || 'stock'
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              symbol: 'sh600000',
              level,
              sectorName: level === 'sector' ? '银行' : null,
              items: level === 'market'
                ? [{ title: '全球宏观信号仍在左侧显示', source: 'WSJ US Business', sentiment: '中性', publishTime: '2026-03-12T09:00:00Z', url: 'https://example.com/market' }]
                : []
            })
          })
        }

        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '浦发银行', symbol: 'sh600000', price: 10.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }

    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()

    await wrapper.find('.focus-toggle').trigger('click')
    await wrapper.vm.$nextTick()

    expect(wrapper.find('.market-news-panel').text()).toContain('全球宏观信号仍在左侧显示')
    expect(wrapper.findComponent({ name: 'ChatWindow' }).exists()).toBe(false)
  })

  it('opens market news in a modal for dense reading', async () => {
    const { fetchMock } = createChatFetchMock({
      handle: async (url) => {
        if (url.startsWith('/api/news?')) {
          const params = new URLSearchParams(url.split('?')[1])
          const level = params.get('level') || 'stock'
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              symbol: 'sh600000',
              level,
              sectorName: level === 'sector' ? '银行' : null,
              items: level === 'market'
                ? [
                    { title: '第一条市场资讯', source: 'WSJ US Business', sentiment: '中性', publishTime: '2026-03-12T09:00:00Z' },
                    { title: '第二条市场资讯', source: 'NYT Business', sentiment: '利好', publishTime: '2026-03-12T09:10:00Z' },
                    { title: '第三条市场资讯', source: '新浪', sentiment: '中性', publishTime: '2026-03-12T09:20:00Z' },
                    { title: '第四条市场资讯', source: '新浪', sentiment: '利空', publishTime: '2026-03-12T09:30:00Z' }
                  ]
                : []
            })
          })
        }

        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    await flushPromises()
    await flushPromises()

    expect(wrapper.find('.market-news-modal').exists()).toBe(false)

    await wrapper.findAll('.market-news-button')[1].trigger('click')
    await wrapper.vm.$nextTick()

    expect(wrapper.find('.market-news-modal').exists()).toBe(true)
    expect(wrapper.find('.market-news-modal').text()).toContain('第四条市场资讯')
  })

  it('renders all local news items with sentiment tags instead of truncating the list', async () => {
    const { fetchMock } = createChatFetchMock({
      handle: async (url) => {
        if (url.startsWith('/api/news?')) {
          const params = new URLSearchParams(url.split('?')[1])
          const level = params.get('level') || 'stock'
          if (level === 'stock') {
            return makeResponse({
              ok: true,
              status: 200,
              json: async () => ({
                symbol: 'sh600000',
                level,
                sectorName: '银行',
                items: [
                  { title: 'Share buyback plan announced', translatedTitle: '回购计划公告', source: '东方财富公告', sentiment: '利好', aiTarget: '个股:浦发银行', aiTags: ['回购', '资本运作'], publishTime: '2026-03-12T09:00:00Z', url: 'https://example.com/1' },
                  { title: '年度分红预案', source: '东方财富公告', sentiment: '利好', publishTime: '2026-03-12T09:10:00Z', url: 'https://example.com/2' },
                  { title: '行业景气跟踪', source: '新浪', sentiment: '中性', publishTime: '2026-03-12T09:20:00Z', url: 'https://example.com/3' },
                  { title: '第四条完整显示', source: '新浪', sentiment: '利空', publishTime: '2026-03-12T09:30:00Z', url: 'https://example.com/4' }
                ]
              })
            })
          }

          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              symbol: 'sh600000',
              level,
              sectorName: level === 'sector' ? '银行' : null,
              items: []
            })
          })
        }

        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '浦发银行', symbol: 'sh600000', price: 10.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }

    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()

    expect(wrapper.text()).toContain('第四条完整显示')
    expect(wrapper.text()).toContain('回购计划公告')
    expect(wrapper.text()).toContain('原题')
    expect(wrapper.text()).toContain('Share buyback plan announced')
    expect(wrapper.text()).toContain('个股:浦发银行')
    expect(wrapper.text()).toContain('回购')
    expect(wrapper.text()).toContain('利好')
    expect(wrapper.text()).toContain('利空')
  })

  it('keeps local news visible when the news impact API fails', async () => {
    const { fetchMock } = createChatFetchMock({
      handle: async (url) => {
        if (url.startsWith('/api/stocks/news/impact')) {
          return makeResponse({ ok: false, status: 500, text: async () => 'error' })
        }

        if (url.startsWith('/api/news?')) {
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              symbol: 'sh600000',
              level: 'stock',
              sectorName: '银行',
              items: [
                { title: '本地事实仍应显示', source: '东方财富公告', sentiment: '中性', publishTime: '2026-03-12T09:00:00Z' }
              ]
            })
          })
        }

        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '浦发银行', symbol: 'sh600000', price: 10.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }

    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()

    expect(wrapper.text()).toContain('资讯影响加载失败')
    expect(wrapper.text()).toContain('本地事实仍应显示')
  })

  it('opens intraday message links in a new window when clicked', async () => {
    const { fetchMock } = createChatFetchMock()
    vi.stubGlobal('fetch', fetchMock)
    const openSpy = vi.spyOn(window, 'open').mockImplementation(() => null)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '深科技', symbol: 'sz000021', price: 31.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: [
        { title: '盘中快讯', source: '新浪', publishedAt: '2026-03-12T09:30:00Z', url: 'https://example.com/message' }
      ]
    }

    await wrapper.vm.$nextTick()

    await wrapper.find('.messages li').trigger('click')

    expect(openSpy).toHaveBeenCalledWith('https://example.com/message', '_blank', 'noopener,noreferrer')
  })

  it('supports focus mode by collapsing the copilot sidebar', async () => {
    const { fetchMock } = createChatFetchMock()
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '深科技', symbol: 'sz000021', price: 31.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()
    await flushPromises()

    expect(wrapper.findComponent({ name: 'ChatWindow' }).exists()).toBe(true)

    await wrapper.find('.focus-toggle').trigger('click')
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('AI 对话、事件信号和多 Agent 分析已收拢到侧栏。')
    expect(wrapper.findComponent({ name: 'ChatWindow' }).exists()).toBe(false)
  })

  it('builds draft from saved history and saves a pending trading plan', async () => {
    let stockPlanListCalls = 0
    let boardPlanListCalls = 0
    const { fetchMock } = createChatFetchMock({
      handle: async (url, options = {}) => {
        if (url === '/api/stocks/agents/history' && options.method === 'POST') {
          return makeResponse({ ok: true, status: 200, json: async () => ({ id: 42 }) })
        }

        if (url === '/api/stocks/plans/draft' && options.method === 'POST') {
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              symbol: 'sz000021',
              name: '深科技',
              direction: 'Long',
              status: 'Pending',
              triggerPrice: 12.6,
              invalidPrice: null,
              stopLossPrice: 11.5,
              takeProfitPrice: 13.4,
              targetPrice: 14.2,
              expectedCatalyst: '突破前高',
              invalidConditions: '跌破支撑',
              riskLimits: '单笔亏损不超过 2%',
              analysisSummary: '等待突破确认',
              analysisHistoryId: 42,
              sourceAgent: 'commander',
              userNote: null
            })
          })
        }

        if (url === '/api/stocks/plans' && options.method === 'POST') {
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              id: 7,
              symbol: 'sz000021',
              name: '深科技',
              direction: 'Long',
              status: 'Pending',
              triggerPrice: 12.6,
              invalidPrice: 11.9,
              stopLossPrice: 11.5,
              takeProfitPrice: 13.4,
              targetPrice: 14.2,
              expectedCatalyst: '突破前高',
              invalidConditions: '跌破支撑',
              riskLimits: '单笔亏损不超过 2%',
              analysisSummary: '等待突破确认',
              analysisHistoryId: 42,
              sourceAgent: 'commander',
              userNote: '控制仓位',
              updatedAt: '2026-03-14T08:30:00Z',
              createdAt: '2026-03-14T08:30:00Z',
              watchlistEnsured: true
            })
          })
        }

        if (url.startsWith('/api/stocks/plans?symbol=sz000021')) {
          stockPlanListCalls += 1
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => (stockPlanListCalls >= 2
              ? [{
                  id: 7,
                  symbol: 'sz000021',
                  name: '深科技',
                  direction: 'Long',
                  status: 'Pending',
                  triggerPrice: 12.6,
                  invalidPrice: 11.9,
                  stopLossPrice: 11.5,
                  takeProfitPrice: 13.4,
                  targetPrice: 14.2,
                  expectedCatalyst: '突破前高',
                  invalidConditions: '跌破支撑',
                  riskLimits: '单笔亏损不超过 2%',
                  analysisSummary: '等待突破确认',
                  analysisHistoryId: 42,
                  sourceAgent: 'commander',
                  userNote: '控制仓位',
                  updatedAt: '2026-03-14T08:30:00Z',
                  createdAt: '2026-03-14T08:30:00Z'
                }]
              : [])
          })
        }

        if (url.startsWith('/api/stocks/plans?take=20')) {
          boardPlanListCalls += 1
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => (boardPlanListCalls >= 2
              ? [{
                  id: 7,
                  symbol: 'sz000021',
                  name: '深科技',
                  direction: 'Long',
                  status: 'Pending',
                  triggerPrice: 12.6,
                  invalidPrice: 11.9,
                  stopLossPrice: 11.5,
                  takeProfitPrice: 13.4,
                  targetPrice: 14.2,
                  expectedCatalyst: '突破前高',
                  invalidConditions: '跌破支撑',
                  riskLimits: '单笔亏损不超过 2%',
                  analysisSummary: '等待突破确认',
                  analysisHistoryId: 42,
                  sourceAgent: 'commander',
                  userNote: '控制仓位',
                  updatedAt: '2026-03-14T08:30:00Z',
                  createdAt: '2026-03-14T08:30:00Z'
                }]
              : [])
          })
        }

        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '深科技', symbol: 'sz000021', price: 31.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    wrapper.vm.agentResults = [
      {
        agentId: 'commander',
        agentName: '指挥Agent',
        success: true,
        data: {
          summary: '偏多',
          analysis_opinion: '等待突破确认',
          triggers: [],
          invalidations: [],
          riskLimits: []
        }
      }
    ]

    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()

    await wrapper.find('.draft-plan-button').trigger('click')
    await flushPromises()
    await flushPromises()

    expect(wrapper.find('.plan-modal').exists()).toBe(true)
    expect(fetchMock.mock.calls.some(args => args[0] === '/api/stocks/plans/draft')).toBe(true)

    const noteField = wrapper.findAll('.plan-field textarea').at(-1)
    await noteField.setValue('控制仓位')

    await wrapper.find('.plan-save-button').trigger('click')
    await flushPromises()
    await flushPromises()

    expect(wrapper.find('.plan-modal').exists()).toBe(false)
    expect(wrapper.text()).toContain('交易计划总览')
    expect(wrapper.text()).toContain('当前交易计划')
    expect(wrapper.text()).toContain('深科技 · Pending')
    expect(wrapper.text()).toContain('触发 12.60')
    expect(wrapper.text()).toContain('止盈 13.40')
    expect(wrapper.text()).toContain('目标 14.20')

    const saveCall = fetchMock.mock.calls.find(args => args[0] === '/api/stocks/plans' && args[1]?.method === 'POST')
    expect(saveCall).toBeTruthy()
    const request = JSON.parse(saveCall[1].body)
    expect(request.analysisHistoryId).toBe(42)
    expect(request.userNote).toBe('控制仓位')
    expect(request.takeProfitPrice).toBe(13.4)
    expect(request.targetPrice).toBe(14.2)
  })

  it('supports editing and deleting pending trading plans', async () => {
    let stockPlanListCalls = 0
    let boardPlanListCalls = 0
    let planDeleted = false
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true)
    const { fetchMock } = createChatFetchMock({
      handle: async (url, options = {}) => {
        if (url === '/api/stocks/plans/7' && options.method === 'PUT') {
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              id: 7,
              symbol: 'sz000021',
              name: '深科技',
              direction: 'Long',
              status: 'Pending',
              triggerPrice: 12.6,
              invalidPrice: 11.9,
              stopLossPrice: 11.3,
              takeProfitPrice: 13.9,
              targetPrice: 14.8,
              expectedCatalyst: '突破前高',
              invalidConditions: '跌破支撑',
              riskLimits: '单笔亏损不超过 2%',
              analysisSummary: '等待突破确认',
              analysisHistoryId: 42,
              sourceAgent: 'commander',
              userNote: '上调目标',
              updatedAt: '2026-03-14T09:00:00Z',
              createdAt: '2026-03-14T08:30:00Z'
            })
          })
        }

        if (url === '/api/stocks/plans/7' && options.method === 'DELETE') {
          planDeleted = true
          return makeResponse({ ok: true, status: 204, json: async () => ({}) })
        }

        if (url.startsWith('/api/stocks/plans?symbol=sz000021')) {
          stockPlanListCalls += 1
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => {
              if (planDeleted) {
                return []
              }

              return [{
                id: 7,
                symbol: 'sz000021',
                name: '深科技',
                direction: 'Long',
                status: 'Pending',
                triggerPrice: 12.6,
                invalidPrice: 11.9,
                stopLossPrice: stockPlanListCalls >= 2 ? 11.3 : 11.5,
                takeProfitPrice: stockPlanListCalls >= 2 ? 13.9 : 13.4,
                targetPrice: stockPlanListCalls >= 2 ? 14.8 : 14.2,
                expectedCatalyst: '突破前高',
                invalidConditions: '跌破支撑',
                riskLimits: '单笔亏损不超过 2%',
                analysisSummary: '等待突破确认',
                analysisHistoryId: 42,
                sourceAgent: 'commander',
                userNote: stockPlanListCalls >= 2 ? '上调目标' : '控制仓位',
                updatedAt: '2026-03-14T09:00:00Z',
                createdAt: '2026-03-14T08:30:00Z'
              }]
            }
          })
        }

        if (url.startsWith('/api/stocks/plans?take=20')) {
          boardPlanListCalls += 1
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => {
              if (planDeleted) {
                return []
              }

              return [{
                id: 7,
                symbol: 'sz000021',
                name: '深科技',
                direction: 'Long',
                status: 'Pending',
                triggerPrice: 12.6,
                invalidPrice: 11.9,
                stopLossPrice: boardPlanListCalls >= 2 ? 11.3 : 11.5,
                takeProfitPrice: boardPlanListCalls >= 2 ? 13.9 : 13.4,
                targetPrice: boardPlanListCalls >= 2 ? 14.8 : 14.2,
                expectedCatalyst: '突破前高',
                invalidConditions: '跌破支撑',
                riskLimits: '单笔亏损不超过 2%',
                analysisSummary: '等待突破确认',
                analysisHistoryId: 42,
                sourceAgent: 'commander',
                userNote: boardPlanListCalls >= 2 ? '上调目标' : '控制仓位',
                updatedAt: '2026-03-14T09:00:00Z',
                createdAt: '2026-03-14T08:30:00Z'
              }]
            }
          })
        }

        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '深科技', symbol: 'sz000021', price: 31.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }

    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()

    expect(wrapper.text()).toContain('止盈 13.40')
    expect(wrapper.text()).toContain('目标 14.20')

    await wrapper.find('.plan-item-actions .plan-link-button').trigger('click')
    await flushPromises()

    expect(wrapper.find('.plan-modal').exists()).toBe(true)
    expect(wrapper.find('input[placeholder="优先取指挥/机构目标"]').element.value).toBe('13.4')
    expect(wrapper.find('input[placeholder="优先取指挥/趋势目标"]').element.value).toBe('14.2')

    await wrapper.find('input[placeholder="优先取指挥/机构目标"]').setValue('13.9')
    await wrapper.find('input[placeholder="优先取指挥/趋势目标"]').setValue('14.8')
    await wrapper.findAll('.plan-field textarea').at(-1).setValue('上调目标')

    await wrapper.find('.plan-save-button').trigger('click')
    await flushPromises()
    await flushPromises()

    const updateCall = fetchMock.mock.calls.find(args => args[0] === '/api/stocks/plans/7' && args[1]?.method === 'PUT')
    expect(updateCall).toBeTruthy()
    expect(JSON.parse(updateCall[1].body)).toMatchObject({
      takeProfitPrice: 13.9,
      targetPrice: 14.8,
      userNote: '上调目标'
    })
    expect(wrapper.text()).toContain('止盈 13.90')
    expect(wrapper.text()).toContain('目标 14.80')

    await wrapper.find('.plan-item-actions .plan-danger-button').trigger('click')
    await flushPromises()
    await flushPromises()

    expect(confirmSpy).toHaveBeenCalled()
    expect(fetchMock.mock.calls.some(args => args[0] === '/api/stocks/plans/7' && args[1]?.method === 'DELETE')).toBe(true)
    expect(wrapper.text()).toContain('暂无交易计划，可从 commander 分析一键起草。')
  })

  it('renders trading plan alerts and refreshes board summaries', async () => {
    let boardAlertCalls = 0
    let stockAlertCalls = 0
    const planList = [{
      id: 7,
      symbol: 'sz000021',
      name: '深科技',
      direction: 'Long',
      status: 'Pending',
      triggerPrice: 12.6,
      invalidPrice: 11.9,
      stopLossPrice: 11.5,
      takeProfitPrice: 13.4,
      targetPrice: 14.2,
      expectedCatalyst: '突破前高',
      invalidConditions: '跌破支撑',
      riskLimits: '单笔亏损不超过 2%',
      analysisSummary: '等待突破确认',
      analysisHistoryId: 42,
      sourceAgent: 'commander',
      userNote: '控制仓位',
      updatedAt: '2026-03-14T09:00:00Z',
      createdAt: '2026-03-14T08:30:00Z'
    }]

    const { fetchMock } = createChatFetchMock({
      handle: async url => {
        if (url.startsWith('/api/stocks/plans?symbol=sz000021')) {
          return makeResponse({ ok: true, status: 200, json: async () => planList })
        }

        if (url.startsWith('/api/stocks/plans?take=20')) {
          return makeResponse({ ok: true, status: 200, json: async () => planList })
        }

        if (url.startsWith('/api/stocks/plans/alerts?symbol=sz000021')) {
          stockAlertCalls += 1
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ([{
              id: stockAlertCalls,
              planId: 7,
              symbol: 'sz000021',
              eventType: 'Warning',
              severity: 'Warning',
              message: '价格接近触发位',
              snapshotPrice: 12.48,
              occurredAt: '2026-03-14T09:30:00Z'
            }])
          })
        }

        if (url.startsWith('/api/stocks/plans/alerts?take=20')) {
          boardAlertCalls += 1
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ([{
              id: boardAlertCalls + 10,
              planId: 7,
              symbol: 'sz000021',
              eventType: boardAlertCalls >= 2 ? 'Invalidated' : 'Warning',
              severity: boardAlertCalls >= 2 ? 'Critical' : 'Info',
              message: boardAlertCalls >= 2 ? '价格跌破失效位' : '计划进入重点盯盘',
              snapshotPrice: boardAlertCalls >= 2 ? 11.88 : 12.4,
              occurredAt: '2026-03-14T09:35:00Z'
            }])
          })
        }

        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    await flushPromises()
    await flushPromises()

    const boardCard = wrapper.find('.trading-plan-board-card')
    expect(boardCard.text()).toContain('Warning')
    expect(boardCard.text()).toContain('计划进入重点盯盘')

    wrapper.vm.detail = {
      quote: { name: '深科技', symbol: 'sz000021', price: 31.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }

    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()

    let currentPlanCard = wrapper.findAll('section').find(section => section.text().includes('当前交易计划'))
    expect(currentPlanCard.text()).toContain('Warning')
    expect(currentPlanCard.text()).toContain('价格接近触发位')

    expect(stockAlertCalls).toBeGreaterThanOrEqual(1)

    await boardCard.find('.plan-refresh-button').trigger('click')
    await flushPromises()
    await flushPromises()

    expect(boardAlertCalls).toBeGreaterThanOrEqual(2)
    expect(boardCard.text()).toContain('Invalidated')
    expect(boardCard.text()).toContain('价格跌破失效位')
  })

  it('polls the trading plan board even when no stock is active', async () => {
    vi.useFakeTimers()
    let boardPlanCalls = 0
    let boardAlertCalls = 0

    const { fetchMock } = createChatFetchMock({
      handle: async url => {
        if (url.startsWith('/api/stocks/plans?take=20')) {
          boardPlanCalls += 1
          return makeResponse({ ok: true, status: 200, json: async () => ([]) })
        }

        if (url.startsWith('/api/stocks/plans/alerts?take=20')) {
          boardAlertCalls += 1
          return makeResponse({ ok: true, status: 200, json: async () => ([]) })
        }

        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    try {
      mount(StockInfoTab)
      await Promise.resolve()
      await vi.advanceTimersByTimeAsync(0)
      expect(boardPlanCalls).toBe(1)
      expect(boardAlertCalls).toBe(1)

      await vi.advanceTimersByTimeAsync(30000)
      await Promise.resolve()
      await vi.advanceTimersByTimeAsync(0)

      expect(boardPlanCalls).toBeGreaterThanOrEqual(2)
      expect(boardAlertCalls).toBeGreaterThanOrEqual(2)
    } finally {
      vi.useRealTimers()
    }
  })
})
