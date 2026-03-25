export const stockInfoTabSwitchingCases = ({
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
    title: "keeps stock switching interactive while live refresh is still pending",
    run: async () => {
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
  }
  },
  {
    title: "ignores stale detail responses when switching stocks quickly",
    run: async () => {
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
  }
  },
  {
    title: "keeps prior stock requests running when switching stocks quickly",
    run: async () => {
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
  }
  },
  {
    title: "preserves in-progress agent tasks when switching away and back to a stock",
    run: async () => {
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
  }
  }
]
