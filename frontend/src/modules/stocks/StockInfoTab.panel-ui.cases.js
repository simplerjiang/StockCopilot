export const stockInfoTabPanelUiCases = ({
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
    title: "keeps market news visible alongside the reserved extension area",
    run: async () => {
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

    expect(wrapper.find('.market-news-panel').text()).toContain('全球宏观信号仍在左侧显示')
    expect(wrapper.find('.sidebar-workspace').exists()).toBe(true)
    expect(wrapper.text()).not.toContain('股票助手')
  }
  },
  {
    title: "opens market news in a modal for dense reading",
    run: async () => {
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
  }
  },
  {
    title: "renders all local news items with sentiment tags instead of truncating the list",
    run: async () => {
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
  }
  },
  {
    title: "keeps local news visible when the news impact API fails",
    run: async () => {
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
  }
  },
  {
    title: "opens intraday message links in a new window when clicked",
    run: async () => {
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
  }
  },
  {
    title: "renders a blank extension placeholder before any stock is loaded",
    run: async () => {
    const { fetchMock } = createChatFetchMock()
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    await flushPromises()

    expect(wrapper.find('.ai-placeholder-card').exists()).toBe(true)
    expect(wrapper.text()).toContain('股票助手、会话化协驾和多 Agent 分析模块已移除')
  }
  }
]
