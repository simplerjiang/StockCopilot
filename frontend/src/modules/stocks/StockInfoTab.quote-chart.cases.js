export const stockInfoTabQuoteChartCases = ({
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
    title: "loads stock detail immediately when clicking a recent-history item",
    run: async () => {
    const liveChart = createDeferred()
    const savedHistory = {
      id: 9,
      symbol: 'sh600000',
      name: '浦发银行',
      price: 10.1,
      changePercent: 0,
      turnoverRate: 1.5,
      peRatio: 8,
      high: 10.3,
      low: 9.8,
      speed: 0.2,
      updatedAt: '2026-03-13T00:00:00Z'
    }
    const { fetchMock } = createChatFetchMock({
      handle: async (url, options = {}) => {
        if (url === '/api/stocks/history') {
          if (options.method === 'POST') {
            return makeResponse({
              ok: true,
              status: 200,
              json: async () => savedHistory
            })
          }

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

    const recordCall = fetchMock.mock.calls.find(args => args[0] === '/api/stocks/history' && args[1]?.method === 'POST')
    expect(recordCall).toBeTruthy()
    expect(wrapper.vm.detail?.quote?.symbol).toBe('sh600000')
    expect(wrapper.vm.detail?.quote?.price).toBe(10.1)
    expect(wrapper.vm.loading).toBe(false)
    expect(wrapper.vm.historyList[0]).toMatchObject(savedHistory)
  }
  },
  {
    title: "records history only for manual fetchQuote success and prepends returned item",
    run: async () => {
    const recordedItems = []
    const { fetchMock } = createChatFetchMock({
      handle: async (url, options = {}) => {
        if (url === '/api/stocks/history') {
          if (options.method === 'POST') {
            const body = JSON.parse(options.body)
            recordedItems.push(body)
            return makeResponse({
              ok: true,
              status: 200,
              json: async () => ({
                id: 101,
                symbol: body.symbol,
                name: body.name,
                price: body.price,
                changePercent: body.changePercent,
                updatedAt: '2026-03-24T01:00:00Z'
              })
            })
          }

          return makeResponse({ ok: true, status: 200, json: async () => ([]) })
        }

        if (String(url).startsWith('/api/stocks/detail/cache?')) {
          return makeResponse({ ok: false, status: 404, json: async () => ({}) })
        }

        if (String(url).startsWith('/api/stocks/chart?')) {
          const params = new URLSearchParams(String(url).split('?')[1])
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              quote: {
                name: '贵州茅台',
                symbol: params.get('symbol') || '',
                price: 1234,
                change: 0,
                changePercent: 1.8,
                turnoverRate: 2.1,
                peRatio: 30,
                high: 1250,
                low: 1200,
                speed: 0.5
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

    await wrapper.find('.search-field input').setValue('600519')
    await wrapper.find('.search-field button').trigger('click')
    await flushPromises()
    await flushPromises()

    expect(recordedItems).toHaveLength(1)
    expect(recordedItems[0]).toMatchObject({
      symbol: 'sh600519',
      name: '贵州茅台',
      price: 1234,
      changePercent: 1.8
    })
    expect(wrapper.vm.historyList[0]).toMatchObject({
      id: 101,
      symbol: 'sh600519',
      name: '贵州茅台'
    })

    await wrapper.vm.refreshChartData('sh600519')
    await flushPromises()
    await flushPromises()

    expect(recordedItems).toHaveLength(1)
  }
  },
  {
    title: "keeps quote visible when history recording fails after a successful manual query",
    run: async () => {
    const { fetchMock } = createChatFetchMock({
      handle: async (url, options = {}) => {
        if (url === '/api/stocks/history') {
          if (options.method === 'POST') {
            return makeResponse({ ok: false, status: 500, text: async () => JSON.stringify({ message: '保存失败' }) })
          }

          return makeResponse({ ok: true, status: 200, json: async () => ([]) })
        }

        if (String(url).startsWith('/api/stocks/detail/cache?')) {
          return makeResponse({ ok: false, status: 404, json: async () => ({}) })
        }

        if (String(url).startsWith('/api/stocks/chart?')) {
          const params = new URLSearchParams(String(url).split('?')[1])
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              quote: {
                name: '浦发银行',
                symbol: params.get('symbol') || '',
                price: 10.1,
                change: 0,
                changePercent: 1.2,
                turnoverRate: 1.5,
                peRatio: 8,
                high: 10.3,
                low: 9.8,
                speed: 0.2
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

    await wrapper.find('.search-field input').setValue('600000')
    await wrapper.find('.search-field button').trigger('click')
    await flushPromises()
    await flushPromises()

    expect(wrapper.vm.detail?.quote?.symbol).toBe('sh600000')
    expect(wrapper.vm.detail?.quote?.price).toBe(10.1)
    expect(wrapper.vm.error).toBe('')
    expect(wrapper.vm.historyError).toBe('保存失败')
  }
  },
  {
    title: "starts cache and live chart requests in parallel",
    run: async () => {
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
  }
  },
  {
    title: "switches chart interval by requesting only the lightweight chart endpoint",
    run: async () => {
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
  }
  },
  {
    title: "retries transient chart fetch failures during the first stock load",
    run: async () => {
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
  }
  },
  {
    title: "keeps background chart refresh lightweight after the stock is already loaded",
    run: async () => {
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
  }
  },
  {
    title: "refreshes the chart every second only when minute TD sequential is enabled in minute view",
    run: async () => {
    vi.useFakeTimers()
    const { fetchMock } = createChatFetchMock()
    vi.stubGlobal('fetch', fetchMock)

    try {
      const wrapper = mount(StockInfoTab)
      await Promise.resolve()
      await vi.advanceTimersByTimeAsync(0)
      await Promise.resolve()
      await vi.advanceTimersByTimeAsync(0)

      const input = wrapper.find('.search-field input')
      const button = wrapper.find('.search-field button')

      await input.setValue('600000')
      await button.trigger('click')
      await Promise.resolve()
      await vi.advanceTimersByTimeAsync(0)
      await Promise.resolve()
      await vi.advanceTimersByTimeAsync(0)

      fetchMock.mockClear()

      const chart = wrapper.findComponent({ name: 'StockCharts' })
      chart.vm.$emit('strategy-visibility-change', {
        viewId: 'minute',
        strategyId: 'minuteTdSequential',
        active: true,
        visibilityState: { minuteTdSequential: true }
      })
      await wrapper.vm.$nextTick()

      await vi.advanceTimersByTimeAsync(1000)
      await Promise.resolve()
      await vi.advanceTimersByTimeAsync(0)

      expect(fetchMock.mock.calls.filter(args => String(args[0]).startsWith('/api/stocks/chart?'))).toHaveLength(0)

      chart.vm.$emit('view-change', 'minute')
      await wrapper.vm.$nextTick()

      await vi.advanceTimersByTimeAsync(1000)
      await Promise.resolve()
      await vi.advanceTimersByTimeAsync(0)

      const chartCallsAfterEnable = fetchMock.mock.calls.filter(args => String(args[0]).startsWith('/api/stocks/chart?'))
      expect(chartCallsAfterEnable).toHaveLength(1)
      expect(String(chartCallsAfterEnable[0][0])).toContain('includeQuote=false')
      expect(String(chartCallsAfterEnable[0][0])).toContain('includeMinute=true')

      chart.vm.$emit('strategy-visibility-change', {
        viewId: 'minute',
        strategyId: 'minuteTdSequential',
        active: false,
        visibilityState: { minuteTdSequential: false }
      })
      await wrapper.vm.$nextTick()

      await vi.advanceTimersByTimeAsync(1000)
      await Promise.resolve()
      await vi.advanceTimersByTimeAsync(0)

      expect(fetchMock.mock.calls.filter(args => String(args[0]).startsWith('/api/stocks/chart?'))).toHaveLength(1)
    } finally {
      vi.useRealTimers()
    }
  }
  },
  {
    title: "requests quote in chart payload for a newly searched stock without cache",
    run: async () => {
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
  }
  },
  {
    title: "requests summary-only cache payload when opening a stock",
    run: async () => {
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
  }
  },
  {
    title: "shows Tencent and Eastmoney progress while stock detail is refreshing",
    run: async () => {
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
  }
  }
]
