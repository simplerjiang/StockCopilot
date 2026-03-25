export const stockInfoTabLayoutChatCases = ({
  StockInfoTab,
  mount,
  createAbortableResponse,
  createChatFetchMock,
  createCopilotAcceptanceBaselinePayload,
  createDeferred,
  createRealtimeOverviewPayload,
  expect,
  findChatWindowForSymbol,
  findVisibleChatWindow,
  flushPromises,
  makeResponse,
  vi,
}) => [
  {
    title: "renders search input and button",
    run: () => {
    const wrapper = mount(StockInfoTab)
    const input = wrapper.find('input')
    const button = wrapper.find('button')

    expect(input.exists()).toBe(true)
    expect(button.exists()).toBe(true)
  }
  },
  {
    title: "renders terminal workspace with copilot sidebar",
    run: () => {
    const wrapper = mount(StockInfoTab)

    expect(wrapper.text()).toContain('TerminalView')
    expect(wrapper.text()).toContain('CopilotPanel')
    expect(wrapper.find('.workspace-grid').exists()).toBe(true)
    expect(wrapper.find('.sticky-toolbar').exists()).toBe(true)
  }
  },
  {
    title: "renders merged top market overview belt above market news and workspace grid",
    run: () => {
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
  }
  },
  {
    title: "keeps the merged top market overview belt outside the copilot sidebar",
    run: () => {
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
  }
  },
  {
    title: "renders global indices with rise and fall styling in the top market overview belt",
    run: async () => {
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
  }
  },
  {
    title: "starts search when pressing Enter in symbol input",
    run: async () => {
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
  }
  },
  {
    title: "sends chat prompt with selected stock context",
    run: async () => {
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
  }
  },
  {
    title: "shows loading indicator while chatting",
    run: async () => {
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
  }
  },
  {
    title: "includes time-check question in chat prompt",
    run: async () => {
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
  }
  },
  {
    title: "renders Step3 fundamentals in terminal summary",
    run: async () => {
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
  }
  },
  {
    title: "renders realtime market context for the selected stock",
    run: async () => {
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
  }
  },
  {
    title: "can hide realtime market context without affecting the stock sidebar",
    run: async () => {
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
  }
  },
  {
    title: "sends pro flag when triggering Pro analysis",
    run: async () => {
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
  }
  },
  {
    title: "sends standard flag when triggering regular analysis",
    run: async () => {
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
  }
  },
  {
    title: "streams assistant response chunks and persists history per stock",
    run: async () => {
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
  }
  },
  {
    title: "keeps chat history per stock and switches on symbol change",
    run: async () => {
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
  }
  },
  {
    title: "keeps prior stock sidebar cards mounted and only hides them on symbol switch",
    run: async () => {
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
  }
  },
  {
    title: "allows creating a new chat for current stock",
    run: async () => {
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
    await flushPromises()
    const chatWindow = findChatWindowForSymbol(wrapper, 'sz000021')
    chatWindow.vm.chatMessages = [{ role: 'assistant', content: '旧记录', timestamp: '2026-01-29T00:00:00Z' }]
    await wrapper.vm.$nextTick()

    await wrapper.find('.chat-session-new').trigger('click')
    await flushPromises()
    await flushPromises()

    const refreshedChatWindow = findChatWindowForSymbol(wrapper, 'sz000021')
    expect(refreshedChatWindow.vm.chatMessages.length).toBe(0)
  }
  },
  {
    title: "renders markdown in chat content",
    run: async () => {
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
  }
  },
  {
    title: "allows switching between chat sessions for same stock",
    run: async () => {
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
    await flushPromises()
    await flushPromises()

    const updatedChatWindow = findChatWindowForSymbol(wrapper, 'sz000021')
    updatedChatWindow.vm.chatMessages = [{ role: 'assistant', content: '历史B', timestamp: '2026-01-29T00:00:00Z' }]
    await wrapper.vm.$nextTick()
    await wrapper.vm.$nextTick()
    messagesBySession[wrapper.vm.selectedChatSession] = updatedChatWindow.vm.chatMessages

    const selector = wrapper.find('.chat-session select')
    const options = selector.findAll('option')
    expect(options.length).toBeGreaterThan(1)

    await selector.setValue(oldSessionKey)
    await flushPromises()
    await flushPromises()
    await wrapper.vm.$nextTick()
    await wrapper.vm.$nextTick()

    const restoredChatWindow = findChatWindowForSymbol(wrapper, 'sz000021')
    const restored = restoredChatWindow.vm.chatMessages.find(item => item.role === 'assistant')
    expect(restored?.content).toBe('历史A')
  }
  }
]
