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

    if (handlers.handle) {
      const handled = await handlers.handle(url, options)
      if (handled) return handled
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
      messages: []
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
    const chatWindow = wrapper.findComponent({ name: 'ChatWindow' })
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
    expect(chatWindow.vm.chatMessages[0].content).toBe('A')
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
    expect(localNewsCalls.length).toBeGreaterThanOrEqual(4)
    expect(localNewsCalls.some(args => String(args[0]).includes('symbol=sh600000') && String(args[0]).includes('level=stock'))).toBe(true)
    expect(localNewsCalls.some(args => String(args[0]).includes('symbol=sh600000') && String(args[0]).includes('level=sector'))).toBe(true)
    expect(localNewsCalls.some(args => !String(args[0]).includes('symbol=sh600000') && String(args[0]).includes('level=market'))).toBe(true)
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

  it('keeps market news visible in the floating banner when the AI sidebar is collapsed', async () => {
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

    expect(wrapper.find('.terminal-market-banner').text()).toContain('全球宏观信号仍在左侧显示')
    expect(wrapper.find('.workspace-grid').element.previousElementSibling?.classList.contains('terminal-market-banner')).toBe(true)
    expect(wrapper.findComponent({ name: 'ChatWindow' }).exists()).toBe(false)
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
})
