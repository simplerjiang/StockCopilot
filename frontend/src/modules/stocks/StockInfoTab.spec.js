import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import StockInfoTab from './StockInfoTab.vue'

const makeResponse = ({ ok, status, json, text }) => ({
  ok,
  status,
  json: json || (async () => ([])),
  text: text || (async () => '')
})

const createRealtimeOverviewPayload = (symbol = 'sz000021', name = '深科技') => ({
  snapshotTime: '2026-03-19T07:00:00Z',
  indices: [
    {
      symbol,
      name,
      price: 31.1,
      change: 0.6,
      changePercent: 1.97,
      turnoverAmount: 456700000,
      timestamp: '2026-03-19T07:00:00Z'
    },
    {
      symbol: 'sh000001',
      name: '上证指数',
      price: 4006.55,
      change: -56.43,
      changePercent: -1.39,
      turnoverAmount: 935265000000,
      timestamp: '2026-03-19T07:00:00Z'
    },
    {
      symbol: 'sz399001',
      name: '深证成指',
      price: 13901.57,
      change: -286.4,
      changePercent: -2.02,
      turnoverAmount: 1175704000000,
      timestamp: '2026-03-19T07:00:00Z'
    },
    {
      symbol: 'sz399006',
      name: '创业板指',
      price: 3309.1,
      change: -37.1,
      changePercent: -1.11,
      turnoverAmount: 545209000000,
      timestamp: '2026-03-19T07:00:00Z'
    },
    {
      symbol: 'hsi',
      name: '恒生指数',
      price: 25277.32,
      change: -223.26,
      changePercent: -0.88,
      turnoverAmount: 0,
      timestamp: '2026-03-19T07:00:00Z'
    },
    {
      symbol: 'hstech',
      name: '恒生科技指数',
      price: 4872.38,
      change: -123.9,
      changePercent: -2.48,
      turnoverAmount: 0,
      timestamp: '2026-03-19T07:00:00Z'
    },
    {
      symbol: 'n225',
      name: '日经225',
      price: 53372.53,
      change: -1866.87,
      changePercent: -3.38,
      turnoverAmount: 0,
      timestamp: '2026-03-19T07:00:00Z'
    },
    {
      symbol: 'ndx',
      name: '纳斯达克',
      price: 21647.61,
      change: 122.01,
      changePercent: 0.57,
      turnoverAmount: 0,
      timestamp: '2026-03-19T07:00:00Z'
    },
    {
      symbol: 'spx',
      name: '标普500',
      price: 6506.48,
      change: -100.01,
      changePercent: -1.51,
      turnoverAmount: 0,
      timestamp: '2026-03-19T07:00:00Z'
    },
    {
      symbol: 'ftse',
      name: '英国富时100',
      price: 9918.33,
      change: 58.42,
      changePercent: 0.59,
      turnoverAmount: 0,
      timestamp: '2026-03-19T07:00:00Z'
    }
  ],
  mainCapitalFlow: {
    snapshotTime: '2026-03-19T07:00:00Z',
    amountUnit: '亿元',
    mainNetInflow: 12.34,
    superLargeOrderNetInflow: 5.67
  },
  northboundFlow: {
    snapshotTime: '2026-03-19T07:00:00Z',
    amountUnit: '亿元',
    totalNetInflow: 8.9,
    shanghaiNetInflow: 4.5,
    shenzhenNetInflow: 4.4
  },
  breadth: {
    tradingDate: '2026-03-19',
    advancers: 1234,
    decliners: 3210,
    flatCount: 88,
    limitUpCount: 56,
    limitDownCount: 12
  }
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
    if (String(url).startsWith('/api/stocks/quote?')) {
      const params = new URLSearchParams(String(url).split('?')[1] || '')
      const symbol = params.get('symbol') || 'sz000021'
      return makeResponse({
        ok: true,
        status: 200,
        json: async () => ({
          symbol,
          name: symbol === 'sh600000' ? '浦发银行' : '深科技',
          price: symbol === 'sh600000' ? 10.1 : 31.1,
          change: 0,
          changePercent: 0,
          turnoverRate: 0,
          peRatio: 0,
          high: 0,
          low: 0,
          speed: 0,
          timestamp: '2026-03-18T02:00:00Z',
          news: [],
          indicators: []
        })
      })
    }
    if (String(url).startsWith('/api/stocks/chart?')) {
      const params = new URLSearchParams(String(url).split('?')[1] || '')
      const symbol = params.get('symbol') || 'sz000021'
      const price = symbol === 'sh600000' ? 10.1 : 31.1
      const includeQuote = params.get('includeQuote') !== 'false'
      const includeMinute = params.get('includeMinute') !== 'false'
      return makeResponse({
        ok: true,
        status: 200,
        json: async () => ({
          ...(includeQuote ? {
            quote: {
              symbol,
              name: symbol === 'sh600000' ? '浦发银行' : '深科技',
              price,
              change: 0,
              changePercent: 0
            }
          } : {}),
          kLines: [{ date: '2026-03-18', open: price, close: price, low: price, high: price, volume: 100 }],
          ...(includeMinute ? {
            minuteLines: [{ date: '2026-03-18', time: '09:31:00', price, averagePrice: price, volume: 12 }]
          } : {})
        })
      })
    }
    if (String(url).startsWith('/api/stocks/detail/cache?')) {
      const params = new URLSearchParams(String(url).split('?')[1] || '')
      const symbol = params.get('symbol') || 'sz000021'
      const price = symbol === 'sh600000' ? 10.1 : 31.1
      return makeResponse({
        ok: true,
        status: 200,
        json: async () => ({
          quote: {
            symbol,
            name: symbol === 'sh600000' ? '浦发银行' : '深科技',
            price,
            change: 0,
            changePercent: 0,
            timestamp: '2026-03-18T02:00:00Z'
          },
          messages: [],
          fundamentalSnapshot: null
        })
      })
    }
    if (String(url).startsWith('/api/stocks/messages?')) {
      return makeResponse({ ok: true, status: 200, json: async () => ([]) })
    }
    if (String(url).startsWith('/api/stocks/fundamental-snapshot?')) {
      return makeResponse({ ok: false, status: 404, json: async () => ({ message: 'not found' }) })
    }
    if (String(url).startsWith('/api/market/realtime/overview')) {
      const params = new URLSearchParams(String(url).split('?')[1] || '')
      const firstSymbol = (params.get('symbols') || '').split(',').find(Boolean) || 'sz000021'
      return makeResponse({ ok: true, status: 200, json: async () => createRealtimeOverviewPayload(firstSymbol) })
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

    if (url === '/api/stocks/copilot/turns/draft' && options.method === 'POST') {
      const body = JSON.parse(options.body)
      return makeResponse({
        ok: true,
        status: 200,
        json: async () => ({
          sessionKey: body.sessionKey || `copilot-${body.symbol}`,
          symbol: body.symbol,
          title: body.sessionTitle || `${body.symbol} Copilot`,
          createdAt: '2026-03-23T02:00:00Z',
          updatedAt: '2026-03-23T02:00:00Z',
          turns: [
            {
              turnId: `turn-${Date.now()}`,
              sessionKey: body.sessionKey || `copilot-${body.symbol}`,
              symbol: body.symbol,
              userQuestion: body.question,
              createdAt: '2026-03-23T02:00:00Z',
              status: 'draft',
              plannerSummary: 'planner 已把问题拆成 2 个受控工具步骤。',
              governorSummary: body.allowExternalSearch
                ? 'governor 已放行当前 draft 中的本地工具调用。'
                : 'governor 已放行本地工具，外部搜索仍需显式授权。',
              marketContext: { stageLabel: '主升', mainlineSectorName: 'AI 算力' },
              planSteps: [
                {
                  stepId: 'planner-1',
                  owner: 'planner',
                  title: '确认问题与市场环境',
                  description: '先确认问题意图，再决定工具顺序。',
                  status: 'planned',
                  dependsOn: [],
                  toolName: null
                },
                {
                  stepId: 'tool-1',
                  owner: 'planner',
                  title: '读取 K 线结构',
                  description: '检查 60 日结构、支撑与压力。',
                  status: 'approved',
                  dependsOn: ['planner-1'],
                  toolName: 'StockKlineMcp'
                },
                {
                  stepId: 'tool-2',
                  owner: 'planner',
                  title: '读取本地新闻证据',
                  description: '核对本地公告和消息。',
                  status: 'approved',
                  dependsOn: ['planner-1'],
                  toolName: 'StockNewsMcp'
                }
              ],
              toolCalls: [
                {
                  callId: 'call-kline',
                  stepId: 'tool-1',
                  toolName: 'StockKlineMcp',
                  policyClass: 'local_required',
                  purpose: '检查价格结构与关键位',
                  inputSummary: `symbol=${body.symbol}; interval=day; count=60`,
                  approvalStatus: 'approved',
                  blockedReason: null
                },
                {
                  callId: 'call-news',
                  stepId: 'tool-2',
                  toolName: 'StockNewsMcp',
                  policyClass: 'local_required',
                  purpose: '读取 Local-First 证据链',
                  inputSummary: `symbol=${body.symbol}; level=stock`,
                  approvalStatus: 'approved',
                  blockedReason: null
                }
              ],
              toolResults: [],
              finalAnswer: {
                status: 'needs_tool_execution',
                summary: '当前只是会话编排草案；需要先执行已批准工具步骤。',
                groundingMode: 'tool_results_required',
                confidenceScore: null,
                needsToolExecution: true,
                constraints: [
                  '最终回答只能引用 tool result 或已保存 evidence 中出现的事实。',
                  'degradedFlags 会系统性压低 confidence 与动作强度。'
                ]
              },
              followUpActions: [
                {
                  actionId: 'action-news',
                  label: '查看本地新闻证据',
                  actionType: 'inspect_news',
                  toolName: 'StockNewsMcp',
                  description: '核对本地公告、消息与 readStatus。',
                  enabled: true,
                  blockedReason: null
                }
              ]
            }
          ]
        })
      })
    }

    if (url.startsWith('/api/stocks/mcp/kline?')) {
      return makeResponse({
        ok: true,
        status: 200,
        json: async () => ({
          traceId: 'trace-kline',
          data: {
            bars: Array.from({ length: 60 }, (_, index) => ({ index })),
            keyLevels: {
              resistanceLevels: [32.1, 33.5],
              supportLevels: [29.8]
            }
          },
          evidence: [],
          features: [{ key: 'trendState', value: 'uptrend' }],
          warnings: [],
          degradedFlags: []
        })
      })
    }

    if (url.startsWith('/api/stocks/mcp/news?')) {
      return makeResponse({
        ok: true,
        status: 200,
        json: async () => ({
          traceId: 'trace-news',
          data: {
            itemCount: 2,
            latestPublishedAt: '2026-03-23T01:00:00Z'
          },
          evidence: [
            {
              source: '上交所公告',
              title: '浦发银行关于董事会决议的公告',
              url: 'https://example.com/notice',
              publishedAt: '2026-03-23T01:00:00Z',
              excerpt: '公告摘要',
              readMode: 'full_text',
              readStatus: 'read'
            }
          ],
          features: [{ key: 'itemCount', value: '2' }],
          warnings: [],
          degradedFlags: []
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
  vi.useRealTimers()
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

  it('renders merged top market overview belt above market news and workspace grid', () => {
    const wrapper = mount(StockInfoTab)
    const overviewBelt = wrapper.find('.page-top-market-overview-belt')
    const marketNewsPanel = wrapper.find('.market-news-panel')
    const workspaceGrid = wrapper.find('.workspace-grid')

    expect(overviewBelt.exists()).toBe(true)
    expect(marketNewsPanel.exists()).toBe(true)
    expect(workspaceGrid.exists()).toBe(true)
    expect(wrapper.find('.page-top-realtime-context').exists()).toBe(false)
    expect(wrapper.find('.page-top-market-quick-strip').exists()).toBe(false)
    expect(overviewBelt.element.compareDocumentPosition(marketNewsPanel.element) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(overviewBelt.element.compareDocumentPosition(workspaceGrid.element) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('keeps the merged top market overview belt outside the copilot sidebar', () => {
    const wrapper = mount(StockInfoTab)
    const overviewBelt = wrapper.find('.page-top-market-overview-belt')
    const marketNewsPanel = wrapper.find('.market-news-panel')
    const workspaceGrid = wrapper.find('.workspace-grid')
    const sidebarQuickStrip = wrapper.find('.trading-plan-board-card .plan-board-realtime-strip')

    expect(overviewBelt.exists()).toBe(true)
    expect(marketNewsPanel.exists()).toBe(true)
    expect(workspaceGrid.exists()).toBe(true)
    expect(sidebarQuickStrip.exists()).toBe(false)
    expect(overviewBelt.element.compareDocumentPosition(marketNewsPanel.element) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(overviewBelt.element.compareDocumentPosition(workspaceGrid.element) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('renders global indices with rise and fall styling in the top market overview belt', async () => {
    const { fetchMock } = createChatFetchMock()
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    await flushPromises()
    await flushPromises()

    const overviewBelt = wrapper.find('.page-top-market-overview-belt')
    const globalCluster = wrapper.find('.market-overview-cluster-global')

    expect(overviewBelt.text()).toContain('全球指数')
    expect(globalCluster.text()).toContain('恒生指数')
    expect(globalCluster.text()).toContain('标普500')
    expect(globalCluster.text()).toContain('英国富时100')
    expect(globalCluster.find('.text-rise').exists()).toBe(true)
    expect(globalCluster.find('.text-fall').exists()).toBe(true)
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

  it('renders realtime market context for the selected stock', async () => {
    const { fetchMock } = createChatFetchMock()
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: {
        name: '深科技',
        symbol: 'sz000021',
        price: 31.1,
        change: 0.6,
        changePercent: 1.97,
        high: 31.5,
        low: 30.2,
        timestamp: '2026-03-19T07:00:00Z'
      },
      kLines: [],
      minuteLines: [],
      messages: []
    }

    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()

    expect(wrapper.text()).toContain('顶部市场总览带')
    expect(wrapper.text()).toContain('当前标的')
    expect(wrapper.text()).toContain('上证指数')
    expect(wrapper.text()).toContain('恒生科技指数')
    expect(wrapper.text()).toContain('主力 +12.34 亿')
    expect(fetchMock.mock.calls.some(args => String(args[0]).startsWith('/api/market/realtime/overview?'))).toBe(true)
  })

  it('can hide realtime market context without affecting the stock sidebar', async () => {
    const { fetchMock } = createChatFetchMock()
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: {
        name: '深科技',
        symbol: 'sz000021',
        price: 31.1,
        change: 0.6,
        changePercent: 1.97,
        timestamp: '2026-03-19T07:00:00Z'
      },
      kLines: [],
      minuteLines: [],
      messages: []
    }

    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()

    const toggleButton = wrapper.findAll('button').find(button => button.text() === '隐藏')
    expect(toggleButton).toBeTruthy()

    await toggleButton.trigger('click')
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('顶部市场总览带已隐藏，可随时重新展开。')
    expect(wrapper.text()).toContain('资讯影响')
    expect(localStorage.getItem('stock_realtime_context_enabled')).toBe('false')
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
        controller.enqueue(encoder.encode('data: **Defining the Scope****Interpreting the Data**\n\n'))
        controller.enqueue(encoder.encode('data: **Assessing Risk Elements**\n\n'))
        controller.enqueue(encoder.encode('data: 风险提示\n\n'))
        controller.enqueue(encoder.encode('data: **Synthesizing Risk Insights** 保持仓位纪律\n\n'))
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
    expect(assistant.content).toBe('风险提示保持仓位纪律')

    const sessionKey = wrapper.vm.selectedChatSession
    expect(messagesBySession[sessionKey]?.some(item => item.content === '风险提示保持仓位纪律')).toBe(true)

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
    expect(restored?.content).toBe('风险提示保持仓位纪律')
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
    await flushPromises()

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

    wrapper.vm.selectedChatSession = oldSessionKey
    await flushPromises()
    await flushPromises()
    await wrapper.vm.$nextTick()
    await wrapper.vm.$nextTick()

    const restored = chatWindow.vm.chatMessages.find(item => item.role === 'assistant')
    expect(restored?.content).toBe('历史A')
  })

  it('builds a stock copilot draft turn and renders timeline, tool cards, and actions', async () => {
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

    const textarea = wrapper.find('.copilot-session-card textarea')
    await textarea.setValue('先看这只股票 60 日结构，再核对本地公告有没有新的风险点。')
    await wrapper.find('.copilot-session-form').trigger('submit')
    await flushPromises()
    await flushPromises()

    const draftCall = fetchMock.mock.calls.find(args => args[0] === '/api/stocks/copilot/turns/draft')
    expect(draftCall).toBeTruthy()
    expect(JSON.parse(draftCall[1].body).symbol).toBe('sh600000')
    expect(wrapper.text()).toContain('planner 已把问题拆成 2 个受控工具步骤。')
    expect(wrapper.text()).toContain('读取 K 线结构')
    expect(wrapper.text()).toContain('StockNewsMcp')
    expect(wrapper.text()).toContain('查看本地新闻证据')
  })

  it('executes approved stock copilot tools and shows evidence summaries', async () => {
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

    await wrapper.find('.copilot-session-card textarea').setValue('核对这只股票的本地公告。')
  await wrapper.find('.copilot-session-form').trigger('submit')
    await flushPromises()
    await flushPromises()

    const actionChip = wrapper.find('.copilot-action-chip')
    await actionChip.trigger('click')
    await flushPromises()
    await flushPromises()

    expect(fetchMock.mock.calls.some(args => String(args[0]).startsWith('/api/stocks/mcp/news?'))).toBe(true)
    expect(wrapper.text()).toContain('本地新闻 2 条')
    expect(wrapper.text()).toContain('浦发银行关于董事会决议的公告')
    expect(wrapper.text()).toContain('上交所公告')
  })

  it('drives chart, news, and plan workflows from copilot follow-up actions', async () => {
    const agentCalls = []
    const { fetchMock } = createChatFetchMock({
      handle: async (url, options = {}) => {
        if (url === '/api/stocks/copilot/turns/draft') {
          const body = JSON.parse(options.body)
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              sessionKey: 'copilot-r3',
              title: '浦发银行 Copilot',
              turns: [
                {
                  turnId: 'turn-r3',
                  sessionKey: 'copilot-r3',
                  symbol: body.symbol,
                  userQuestion: body.question,
                  status: 'drafted',
                  plannerSummary: '先核对 K 线，再核对本地新闻，最后决定是否进入计划流。',
                  governorSummary: '仅允许本地工具，计划动作需要在工具结果齐备后解锁。',
                  planSteps: [],
                  toolCalls: [
                    {
                      callId: 'call-kline-r3',
                      toolName: 'StockKlineMcp',
                      policyClass: 'local_required',
                      purpose: '检查日 K 结构',
                      inputSummary: `symbol=${body.symbol}; interval=day; count=60`,
                      approvalStatus: 'approved',
                      blockedReason: null
                    },
                    {
                      callId: 'call-news-r3',
                      toolName: 'StockNewsMcp',
                      policyClass: 'local_required',
                      purpose: '检查本地新闻证据',
                      inputSummary: `symbol=${body.symbol}; level=stock`,
                      approvalStatus: 'approved',
                      blockedReason: null
                    }
                  ],
                  toolResults: [],
                  finalAnswer: {
                    status: 'needs_tool_execution',
                    summary: '需要先执行工具。',
                    constraints: []
                  },
                  followUpActions: [
                    {
                      actionId: 'action-kline',
                      label: '查看 K 线结构',
                      actionType: 'inspect_chart',
                      toolName: 'StockKlineMcp',
                      description: '切到日 K，并刷新结构位。',
                      enabled: true,
                      blockedReason: null
                    },
                    {
                      actionId: 'action-news',
                      label: '查看新闻证据',
                      actionType: 'inspect_news',
                      toolName: 'StockNewsMcp',
                      description: '刷新本地新闻证据。',
                      enabled: true,
                      blockedReason: null
                    },
                    {
                      actionId: 'action-plan',
                      label: '起草交易计划',
                      actionType: 'draft_trading_plan',
                      toolName: '',
                      description: '把 Copilot 证据承接到交易计划。',
                      enabled: false,
                      blockedReason: '需要先完成工具执行并得到最终判断。'
                    }
                  ]
                }
              ]
            })
          })
        }

        if (String(url).startsWith('/api/stocks/mcp/kline?')) {
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              traceId: 'trace-kline-r3',
              data: {
                bars: Array.from({ length: 60 }, (_, index) => ({ index })),
                keyLevels: {
                  resistanceLevels: [10.8],
                  supportLevels: [9.7]
                }
              },
              evidence: [],
              features: [{ key: 'trendState', value: 'uptrend' }],
              warnings: [],
              degradedFlags: []
            })
          })
        }

        if (String(url).startsWith('/api/stocks/mcp/news?')) {
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              traceId: 'trace-news-r3',
              data: {
                itemCount: 1,
                latestPublishedAt: '2026-03-23T01:00:00Z'
              },
              evidence: [
                {
                  source: '上交所公告',
                  title: '浦发银行最新公告',
                  url: 'https://example.com/pfbank-notice',
                  publishedAt: '2026-03-23T01:00:00Z',
                  excerpt: '公告摘要',
                  readMode: 'full_text',
                  readStatus: 'read'
                }
              ],
              features: [{ key: 'itemCount', value: '1' }],
              warnings: [],
              degradedFlags: []
            })
          })
        }

        if (url === '/api/stocks/agents/single') {
          const body = JSON.parse(options.body)
          agentCalls.push(body)
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              agentId: body.agentId,
              agentName: body.agentId,
              success: true,
              data: { summary: body.agentId }
            })
          })
        }

        if (url === '/api/stocks/agents/history' && options.method === 'POST') {
          return makeResponse({ ok: true, status: 200, json: async () => ({ id: 88 }) })
        }

        if (String(url).startsWith('/api/stocks/agents/history?')) {
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ([{ id: 88, symbol: 'sh600000', createdAt: '2026-03-23T02:00:00Z' }])
          })
        }

        if (url === '/api/stocks/plans/draft' && options.method === 'POST') {
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              symbol: 'sh600000',
              name: '浦发银行',
              direction: 'Long',
              status: 'Pending',
              triggerPrice: 10.6,
              invalidPrice: 9.8,
              stopLossPrice: 9.7,
              takeProfitPrice: 11.4,
              targetPrice: 11.8,
              expectedCatalyst: '站上压力位',
              invalidConditions: '跌破关键支撑',
              riskLimits: '单笔风险 2%',
              analysisSummary: '等待量价共振确认',
              analysisHistoryId: 88,
              sourceAgent: 'commander',
              userNote: null
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

    await wrapper.find('.copilot-session-card textarea').setValue('先看结构，再看新闻，最后起草交易计划。')
    await wrapper.find('.copilot-session-form').trigger('submit')
    await flushPromises()
    await flushPromises()

    expect(wrapper.find('.copilot-action-chip[data-action-id="action-plan"]').attributes('disabled')).toBeDefined()
    expect(wrapper.find('.copilot-action-chip[data-action-id="action-plan"]').attributes('title')).toContain('至少先执行一张已批准的 Copilot 工具卡')

    await wrapper.find('.copilot-action-chip[data-action-id="action-kline"]').trigger('click')
    await flushPromises()
    await flushPromises()

    expect(fetchMock.mock.calls.some(args => String(args[0]).startsWith('/api/stocks/mcp/kline?'))).toBe(true)
    expect(wrapper.find('.stock-chart-section').classes()).toContain('copilot-section-active')
    expect(wrapper.findComponent({ name: 'StockCharts' }).props('focusedView')).toBe('day')
    expect(wrapper.find('.copilot-action-chip[data-action-id="action-plan"]').attributes('title')).toContain('还有 1 张已批准工具卡未执行')

    await wrapper.find('.copilot-action-chip[data-action-id="action-news"]').trigger('click')
    await flushPromises()
    await flushPromises()

    expect(fetchMock.mock.calls.some(args => String(args[0]).startsWith('/api/stocks/mcp/news?'))).toBe(true)
    expect(wrapper.find('.stock-news-impact-section').classes()).toContain('copilot-section-active')
    expect(wrapper.text()).toContain('浦发银行最新公告')

    const planAction = wrapper.find('.copilot-action-chip[data-action-id="action-plan"]')
    expect(planAction.attributes('disabled')).toBeUndefined()

    await planAction.trigger('click')
    await flushPromises()
    await flushPromises()
    await flushPromises()

    expect(agentCalls).toHaveLength(5)
    expect(fetchMock.mock.calls.some(args => args[0] === '/api/stocks/plans/draft')).toBe(true)
    expect(wrapper.find('.plan-modal').exists()).toBe(true)
    expect(wrapper.find('.stock-plan-section').classes()).toContain('copilot-section-active')
  })

  it('keeps recent stock copilot turns as replay chips', async () => {
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

    const textarea = wrapper.find('.copilot-session-card textarea')
    await textarea.setValue('第一轮问题')
    await wrapper.find('.copilot-session-form').trigger('submit')
    await flushPromises()
    await flushPromises()

    await textarea.setValue('第二轮问题')
    await wrapper.find('.copilot-session-form').trigger('submit')
    await flushPromises()
    await flushPromises()

    const replayChips = wrapper.findAll('.copilot-replay-chip')
    expect(replayChips).toHaveLength(2)
    expect(wrapper.text()).toContain('第一轮问题')
    expect(wrapper.text()).toContain('第二轮问题')
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

  it('shows only recent bullish and bearish impact events in the headline list', async () => {
    vi.spyOn(Date, 'now').mockReturnValue(new Date('2026-03-15T12:00:00Z').getTime())

    const { fetchMock } = createChatFetchMock({
      handle: async (url) => {
        if (url.startsWith('/api/stocks/news/impact')) {
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              summary: { positive: 3, neutral: 1, negative: 2, overall: '多空分化' },
              events: [
                { title: '两周前的旧利好', category: '利好', impactScore: 98, publishedAt: '2026-03-01T09:00:00Z' },
                { title: '最新回购公告', category: '利好', impactScore: 46, publishedAt: '2026-03-15T11:30:00Z' },
                { title: '最新监管问询', category: '利空', impactScore: -52, publishedAt: '2026-03-15T10:45:00Z' },
                { title: '刚刚披露合作进展', category: '利好', impactScore: 28, publishedAt: '2026-03-15T09:40:00Z' },
                { title: '中性行业点评', category: '中性', impactScore: 80, publishedAt: '2026-03-15T11:50:00Z' }
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

    const headlineItems = wrapper.findAll('.news-impact-list li')
    const headlineText = headlineItems.map(item => item.text()).join(' | ')

    expect(headlineItems).toHaveLength(3)
    expect(headlineText).toContain('最新回购公告')
    expect(headlineText).toContain('最新监管问询')
    expect(headlineText).toContain('刚刚披露合作进展')
    expect(headlineText).not.toContain('两周前的旧利好')
    expect(headlineText).not.toContain('中性行业点评')
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

    const refreshButton = wrapper.find('.news-impact .news-impact-header button')
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
    const liveChart = createDeferred()
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

        if (String(url).startsWith('/api/stocks/chart?')) {
          const params = new URLSearchParams(String(url).split('?')[1])
          return liveChart.promise.then(() => makeResponse({
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
    const detailCall = fetchMock.mock.calls.find(args => String(args[0]).startsWith('/api/stocks/chart?'))
    expect(cacheCall).toBeTruthy()
    expect(detailCall).toBeTruthy()
    expect(String(detailCall[0])).toContain('symbol=sh600000')
    expect(wrapper.vm.detail?.quote?.price).toBe(9.9)
    expect(wrapper.vm.loading).toBe(true)
    expect(wrapper.find('.search-field button').attributes('disabled')).toBeUndefined()
    expect(wrapper.text()).toContain('后台刷新中...')

    liveChart.resolve()
    await flushPromises()
    await flushPromises()

    expect(wrapper.vm.detail?.quote?.symbol).toBe('sh600000')
    expect(wrapper.vm.detail?.quote?.price).toBe(10.1)
    expect(wrapper.vm.loading).toBe(false)
  })

  it('starts cache and live chart requests in parallel', async () => {
    const cacheDetail = createDeferred()
    const liveChart = createDeferred()
    const requestOrder = []

    const { fetchMock } = createChatFetchMock({
      handle: async url => {
        const text = String(url)

        if (text.startsWith('/api/stocks/quote?')) {
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              symbol: 'sh600000',
              name: '浦发银行',
              price: 10.1,
              change: 0,
              changePercent: 0,
              turnoverRate: 0,
              peRatio: 0,
              high: 0,
              low: 0,
              speed: 0,
              timestamp: '2026-03-18T02:00:00Z',
              news: [],
              indicators: []
            })
          })
        }

        if (text.startsWith('/api/stocks/fundamental-snapshot?')) {
          return makeResponse({ ok: false, status: 404 })
        }

        if (text.startsWith('/api/stocks/detail/cache?')) {
          requestOrder.push('cache')
          return cacheDetail.promise.then(() => makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              quote: { name: '浦发银行', symbol: 'sh600000', price: 9.9, change: 0, changePercent: 0 },
              kLines: [],
              minuteLines: [],
              messages: []
            })
          }))
        }

        if (text.startsWith('/api/stocks/chart?')) {
          requestOrder.push('chart')
          return liveChart.promise.then(() => makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              quote: { name: '浦发银行', symbol: 'sh600000', price: 10.1, change: 0, changePercent: 0 },
              kLines: [{ date: '2026-03-18', open: 10, close: 10.1, low: 9.9, high: 10.2, volume: 100 }],
              minuteLines: [{ date: '2026-03-18', time: '09:31:00', price: 10.1, averagePrice: 10.05, volume: 12 }],
              messages: []
            })
          }))
        }

        return null
      }
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    const input = wrapper.find('.search-field input')
    const button = wrapper.find('.search-field button')

    await input.setValue('600000')
    await button.trigger('click')
    await flushPromises()
    await flushPromises()

    expect(requestOrder).toContain('cache')
    expect(requestOrder).toContain('chart')
    expect(requestOrder.indexOf('chart')).toBeGreaterThan(-1)

    liveChart.resolve()
    await flushPromises()
    await flushPromises()

    expect(wrapper.vm.detail?.quote?.price).toBe(10.1)

    cacheDetail.resolve()
    await flushPromises()
    await flushPromises()

    expect(wrapper.vm.detail?.quote?.price).toBe(10.1)
    expect(wrapper.vm.loading).toBe(false)
  })

  it('switches chart interval by requesting only the lightweight chart endpoint', async () => {
    const { fetchMock } = createChatFetchMock()
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

    fetchMock.mockClear()

    const monthButton = wrapper.findAll('button').find(item => item.text() === '月K图')
    expect(monthButton).toBeTruthy()

    await monthButton.trigger('click')
    await flushPromises()
    await flushPromises()

    const requestedUrls = fetchMock.mock.calls.map(args => String(args[0]))
    expect(requestedUrls.some(url => url.startsWith('/api/stocks/chart?') && url.includes('interval=month') && url.includes('includeQuote=false') && url.includes('includeMinute=false'))).toBe(true)
    expect(requestedUrls.some(url => url.startsWith('/api/stocks/detail/cache?'))).toBe(false)
    expect(requestedUrls.some(url => url.startsWith('/api/stocks/messages?'))).toBe(false)
    expect(requestedUrls.some(url => url.startsWith('/api/stocks/fundamental-snapshot?'))).toBe(false)
  })

  it('retries transient chart fetch failures during the first stock load', async () => {
    let chartCalls = 0

    const { fetchMock } = createChatFetchMock({
      handle: async url => {
        if (String(url).startsWith('/api/stocks/chart?')) {
          chartCalls += 1
          if (chartCalls === 1) {
            throw new TypeError('Failed to fetch')
          }
        }

        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    await flushPromises()

    const input = wrapper.find('.search-field input')
    const button = wrapper.find('.search-field button')

    await input.setValue('600000')
    await button.trigger('click')
    await flushPromises()
    await new Promise(resolve => setTimeout(resolve, 950))
    await flushPromises()

    expect(chartCalls).toBe(2)
    expect(wrapper.vm.detail?.quote?.symbol).toBe('sh600000')
    expect(wrapper.vm.error).toBe('')
  })

  it('keeps background chart refresh lightweight after the stock is already loaded', async () => {
    const { fetchMock } = createChatFetchMock()
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

    fetchMock.mockClear()

    await wrapper.vm.refreshChartData('sh600000')
    await flushPromises()
    await flushPromises()

    const chartCall = fetchMock.mock.calls.find(args => String(args[0]).startsWith('/api/stocks/chart?'))
    expect(chartCall).toBeTruthy()
    expect(String(chartCall[0])).toContain('includeQuote=false')
    expect(String(chartCall[0])).toContain('includeMinute=true')
  })

  it('requests quote in chart payload for a newly searched stock without cache', async () => {
    const { fetchMock } = createChatFetchMock({
      handle: async url => {
        if (String(url).startsWith('/api/stocks/detail/cache?')) {
          return makeResponse({ ok: false, status: 404, json: async () => ({ message: 'not found' }) })
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

    const chartCall = fetchMock.mock.calls.find(args => String(args[0]).startsWith('/api/stocks/chart?'))
    expect(chartCall).toBeTruthy()
    expect(String(chartCall[0])).toContain('includeQuote=true')
    expect(wrapper.vm.detail?.quote?.symbol).toBe('sh600000')
    expect(wrapper.text()).toContain('浦发银行')
  })

  it('requests summary-only cache payload when opening a stock', async () => {
    const { fetchMock } = createChatFetchMock()
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

    const cacheCall = fetchMock.mock.calls.find(args => String(args[0]).startsWith('/api/stocks/detail/cache?'))
    expect(cacheCall).toBeTruthy()
    expect(String(cacheCall[0])).not.toContain('interval=')
    expect(String(cacheCall[0])).not.toContain('includeLegacyCharts=')
  })

  it('shows Tencent and Eastmoney progress while stock detail is refreshing', async () => {
    const tencentQuote = createDeferred()
    const liveChart = createDeferred()
    const fundamentalSnapshot = createDeferred()

    const { fetchMock } = createChatFetchMock({
      handle: async url => {
        const text = String(url)

        if (text.startsWith('/api/stocks/detail/cache?')) {
          const params = new URLSearchParams(text.split('?')[1])
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

        if (text.startsWith('/api/stocks/quote?')) {
          return tencentQuote.promise.then(() => makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              symbol: 'sh600000',
              name: '浦发银行',
              price: 10.1,
              change: 0.2,
              changePercent: 2.0,
              turnoverRate: 0,
              peRatio: 6.9,
              high: 10.2,
              low: 9.8,
              speed: 0,
              timestamp: '2026-03-18T02:00:00Z',
              news: [],
              indicators: []
            })
          }))
        }

        if (text.startsWith('/api/stocks/fundamental-snapshot?')) {
          return fundamentalSnapshot.promise.then(() => makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              updatedAt: '2026-03-18T02:00:30Z',
              facts: [
                { label: '公司全称', value: '上海浦东发展银行股份有限公司', source: '东方财富公司概况' }
              ]
            })
          }))
        }

        if (text.startsWith('/api/stocks/chart?')) {
          const params = new URLSearchParams(text.split('?')[1])
          return liveChart.promise.then(() => makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              quote: {
                name: '浦发银行',
                symbol: params.get('symbol') || '',
                price: 10.1,
                change: 0.2,
                changePercent: 2.0
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

    const input = wrapper.find('.search-field input')
    const button = wrapper.find('.search-field button')

    await input.setValue('600000')
    await button.trigger('click')
    await flushPromises()
    await flushPromises()

    const liveDetailCall = fetchMock.mock.calls.find(args => String(args[0]).startsWith('/api/stocks/chart?'))
    expect(wrapper.text()).toContain('后台刷新进度')
    expect(wrapper.text()).toContain('缓存回显')
    expect(wrapper.text()).toContain('K线/分时图表')
    expect(wrapper.text()).toContain('腾讯行情')
    expect(wrapper.text()).toContain('东方财富基本面')

    tencentQuote.resolve()
    fundamentalSnapshot.resolve()
    await flushPromises()
    await flushPromises()

    expect(wrapper.vm.loading).toBe(true)
    expect(wrapper.text().includes('100%')).toBe(false)
    expect(wrapper.text()).toContain('腾讯实时行情已返回')
    expect(wrapper.text()).toContain('请求实时图表数据')

    liveChart.resolve()
    await flushPromises()
    await flushPromises()

    expect(wrapper.vm.loading).toBe(false)
    expect(wrapper.vm.detail?.fundamentalSnapshot?.facts?.[0]?.value).toBe('上海浦东发展银行股份有限公司')
  })

  it('keeps stock switching interactive while live refresh is still pending', async () => {
    const firstLiveChart = createDeferred()
    const secondLiveChart = createDeferred()

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

        if (text.startsWith('/api/stocks/chart?')) {
          const params = new URLSearchParams(text.split('?')[1])
          const currentSymbol = params.get('symbol') || ''
          if (currentSymbol === 'sh600000') {
            return firstLiveChart.promise.then(() => makeResponse({ ok: true, status: 200, json: async () => buildDetail(currentSymbol, 10.1) }))
          }
          if (currentSymbol === 'sh600519') {
            return secondLiveChart.promise.then(() => makeResponse({ ok: true, status: 200, json: async () => buildDetail(currentSymbol, 20.1) }))
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

    secondLiveChart.resolve()
    firstLiveChart.resolve()
    await flushPromises()
    await flushPromises()

    expect(wrapper.vm.detail?.quote?.symbol).toBe('sh600519')
    expect(wrapper.vm.loading).toBe(false)
  })

  it('ignores stale detail responses when switching stocks quickly', async () => {
    const firstLiveChart = createDeferred()
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

        if (String(url).startsWith('/api/stocks/chart?')) {
          const params = new URLSearchParams(String(url).split('?')[1])
          const currentSymbol = params.get('symbol') || ''
          if (currentSymbol === 'sh600000') {
            return firstLiveChart.promise.then(() => makeResponse({
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

    firstLiveChart.resolve()
    await flushPromises()
    await flushPromises()

    expect(wrapper.vm.detail?.quote?.symbol).toBe('sh600519')
    expect(wrapper.vm.detail?.quote?.price).toBe(20.1)
  })

  it('keeps prior stock requests running when switching stocks quickly', async () => {
    const firstLiveChart = createDeferred()
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

        if (text.startsWith('/api/stocks/chart?')) {
          const params = new URLSearchParams(text.split('?')[1])
          const currentSymbol = params.get('symbol') || ''
          if (currentSymbol === 'sh600000') {
            return createAbortableResponse(
              firstLiveChart,
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

    firstLiveChart.resolve()
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

        if (text.startsWith('/api/stocks/detail/cache?') || text.startsWith('/api/stocks/chart?')) {
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
    expect(wrapper.text()).toContain('深科技 · 观察中')
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

  it('keeps the trading plan modal visible after switching the active workspace', async () => {
    const { fetchMock } = createChatFetchMock({
      handle: async (url, options = {}) => {
        if (url === '/api/stocks/agents/history' && options.method === 'POST') {
          return makeResponse({ ok: true, status: 200, json: async () => ({ id: 88 }) })
        }

        if (String(url).startsWith('/api/stocks/agents/history?')) {
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ([{ id: 88, symbol: 'sh600000', createdAt: '2026-03-23T02:00:00Z' }])
          })
        }

        if (url === '/api/stocks/plans/draft' && options.method === 'POST') {
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              symbol: 'sh600000',
              name: '浦发银行',
              direction: 'Long',
              status: 'Pending',
              triggerPrice: 10.6,
              invalidPrice: 9.8,
              stopLossPrice: 9.7,
              takeProfitPrice: 11.4,
              targetPrice: 11.8,
              expectedCatalyst: '站上压力位',
              invalidConditions: '跌破关键支撑',
              riskLimits: '单笔风险 2%',
              analysisSummary: '等待量价共振确认',
              analysisHistoryId: 88,
              sourceAgent: 'commander',
              userNote: null
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
    wrapper.vm.agentResults = [
      {
        agentId: 'commander',
        agentName: '指挥Agent',
        success: true,
        data: {
          summary: '偏多',
          analysis_opinion: '等待量价共振确认',
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
    await flushPromises()

    expect(wrapper.find('.plan-modal').exists()).toBe(true)
    expect(wrapper.find('.plan-field input[disabled]').element.value).toBe('sh600000')

    wrapper.vm.detail = {
      quote: { name: '深科技', symbol: 'sz000021', price: 31.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()
    await flushPromises()

    expect(wrapper.find('.plan-modal').exists()).toBe(true)
    expect(wrapper.find('.plan-field input[disabled]').element.value).toBe('sh600000')
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

  it('retries transient trading plan board fetch failures on initial load', async () => {
    let boardPlanCalls = 0

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
      analysisHistoryId: 42,
      sourceAgent: 'commander',
      updatedAt: '2026-03-14T09:00:00Z',
      createdAt: '2026-03-14T08:30:00Z'
    }]

    const { fetchMock } = createChatFetchMock({
      handle: async url => {
        if (url.startsWith('/api/stocks/plans?take=20')) {
          boardPlanCalls += 1
          if (boardPlanCalls === 1) {
            throw new TypeError('Failed to fetch')
          }

          return makeResponse({ ok: true, status: 200, json: async () => planList })
        }

        if (url.startsWith('/api/stocks/plans/alerts?take=20')) {
          return makeResponse({ ok: true, status: 200, json: async () => ([]) })
        }

        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    await flushPromises()
    await new Promise(resolve => setTimeout(resolve, 950))
    await flushPromises()

    expect(boardPlanCalls).toBe(2)
    expect(wrapper.find('.trading-plan-board-card').text()).toContain('深科技')
    expect(wrapper.find('.trading-plan-board-card').text()).not.toContain('暂无交易计划，可从 commander 分析一键起草。')
  })
})
