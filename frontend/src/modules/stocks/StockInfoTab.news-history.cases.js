export const stockInfoTabNewsHistoryCases = ({
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
    title: "renders news impact summary when data is available",
    run: async () => {
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
  }
  },
  {
    title: "shows only recent bullish and bearish impact events in the headline list",
    run: async () => {
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
  }
  },
  {
    title: "requests local news buckets for the active symbol",
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

    const localNewsCalls = fetchMock.mock.calls.filter(args => String(args[0]).startsWith('/api/news?'))
    expect(localNewsCalls.length).toBeGreaterThanOrEqual(3)
    expect(localNewsCalls.some(args => String(args[0]).includes('symbol=sh600000') && String(args[0]).includes('level=stock'))).toBe(true)
    expect(localNewsCalls.some(args => String(args[0]).includes('symbol=sh600000') && String(args[0]).includes('level=sector'))).toBe(true)
    expect(localNewsCalls.some(args => !String(args[0]).includes('symbol=sh600000') && String(args[0]).includes('level=market'))).toBe(true)
  }
  },
  {
    title: "does not reload market news when switching stocks",
    run: async () => {
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
  }
  },
  {
    title: "reuses sidebar data when switching back to a previously loaded stock",
    run: async () => {
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
    expect(counters.impact.sh600519).toBe(1)
    expect(counters.news['sh600519:stock']).toBe(1)
    expect(counters.news['sh600519:sector']).toBe(1)
  }
  },
  {
    title: "allows manual news-impact refresh even after sidebar data is cached",
    run: async () => {
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
  }
  },
  {
    title: "loads market news without requiring a selected stock",
    run: async () => {
    const { fetchMock } = createChatFetchMock()
    vi.stubGlobal('fetch', fetchMock)

    mount(StockInfoTab)
    await flushPromises()
    await flushPromises()

    const marketCall = fetchMock.mock.calls.find(args => String(args[0]).startsWith('/api/news?') && String(args[0]).includes('level=market'))
    expect(marketCall).toBeTruthy()
    expect(String(marketCall[0])).not.toContain('symbol=')
  }
  },
  {
    title: "renders full history list without slicing to 10 items",
    run: async () => {
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
  }
  }
]
