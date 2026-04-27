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
    expect(wrapper.find('.sc-workspace__right').exists()).toBe(true)
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
    title: "shows fundamental fact sources without hiding labels and values",
    run: async () => {
    const { fetchMock } = createChatFetchMock()
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '贵州茅台', symbol: 'sh600519', price: 1710.5, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: [],
      fundamentalSnapshot: {
        updatedAt: '2026-04-26T02:00:00Z',
        facts: [
          { label: '主营业务', value: '酒类产品生产和销售', source: '东方财富公司概况(经营范围摘要)' },
          { label: '经营范围', value: '酒类产品生产和销售；食品经营。', source: '东方财富公司概况' },
          { label: '上市日期', value: '2001-08-27' },
          { label: '注册资本', value: '12.56 亿', source: '   ' }
        ]
      }
    }

    await wrapper.vm.$nextTick()

    const summaryText = wrapper.find('.terminal-summary-grid').text()
    expect(summaryText).toContain('主营业务：酒类产品生产和销售')
    expect(summaryText).toContain('经营范围：酒类产品生产和销售；食品经营。')
    expect(summaryText).toContain('口径：东方财富公司概况(经营范围摘要)')
    expect(summaryText).toContain('口径：东方财富公司概况')
    expect(summaryText).toContain('上市日期：2001-08-27')
    expect(summaryText).toContain('注册资本：12.56 亿')
    expect(wrapper.findAll('.fundamental-fact-source')).toHaveLength(2)
    expect(summaryText).not.toContain('口径：上市日期')
    expect(summaryText).not.toContain('口径：注册资本')
  }
  },
  {
    title: "shows degraded intraday message state from wrapped empty response",
    run: async () => {
    const warning = '盘中消息暂时不可用，已切换为空态展示'
    const { fetchMock } = createChatFetchMock({
      handle: async url => {
        if (String(url).startsWith('/api/stocks/messages?')) {
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              messages: [],
              degraded: true,
              warning
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

    await wrapper.find('.search-field input').setValue('000001')
    await wrapper.find('.search-field button').trigger('click')
    await flushPromises()
    await flushPromises()

    const tape = wrapper.find('.tape-card')
    expect(tape.text()).toContain('消息降级')
    expect(tape.text()).toContain(warning)
    expect(tape.text()).toContain('盘中消息暂不可用，已降级为空态展示。')
    expect(tape.text()).not.toContain('暂无盘中消息。')
  }
  },
  {
    title: "renders wrapped intraday messages without degraded hint when healthy",
    run: async () => {
    const { fetchMock } = createChatFetchMock({
      handle: async url => {
        if (String(url).startsWith('/api/stocks/messages?')) {
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              messages: [
                { title: '成交异动拉升', source: '新浪', publishedAt: '2026-03-12T09:35:00Z' }
              ],
              degraded: false
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

    await wrapper.find('.search-field input').setValue('000518')
    await wrapper.find('.search-field button').trigger('click')
    await flushPromises()
    await flushPromises()

    const tape = wrapper.find('.tape-card')
    expect(tape.text()).toContain('成交异动拉升')
    expect(tape.text()).not.toContain('消息降级')
    expect(tape.text()).not.toContain('暂不可用')
  }
  },
  {
    title: "clears degraded intraday message state after legacy array response recovers",
    run: async () => {
    const warning = '盘中消息暂时不可用，已切换为空态展示'
    let messageRequestCount = 0
    const { fetchMock } = createChatFetchMock({
      handle: async url => {
        if (String(url).startsWith('/api/stocks/messages?')) {
          messageRequestCount += 1
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => (messageRequestCount === 1
              ? {
                  messages: [],
                  degraded: true,
                  warning
                }
              : [
                  { title: '旧数组恢复后的正常消息', source: '东方财富', publishedAt: '2026-03-12T09:45:00Z' }
                ])
          })
        }

        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    await flushPromises()
    await flushPromises()

    await wrapper.find('.search-field input').setValue('000518')
    await wrapper.find('.search-field button').trigger('click')
    await flushPromises()
    await flushPromises()

    const degradedTape = wrapper.find('.tape-card')
    expect(degradedTape.text()).toContain('消息降级')
    expect(degradedTape.text()).toContain(warning)

    await wrapper.find('.search-field input').setValue('000518')
    await wrapper.find('.search-field button').trigger('click')
    await flushPromises()
    await flushPromises()

    const recoveredTape = wrapper.find('.tape-card')
    expect(recoveredTape.text()).toContain('旧数组恢复后的正常消息')
    expect(recoveredTape.text()).not.toContain('消息降级')
    expect(recoveredTape.text()).not.toContain(warning)
    expect(recoveredTape.text()).not.toContain('盘中消息暂不可用，已降级为空态展示。')
  }
  },
  {
    title: "keeps legacy intraday message array response compatible",
    run: async () => {
    const { fetchMock } = createChatFetchMock({
      handle: async url => {
        if (String(url).startsWith('/api/stocks/messages?')) {
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ([
              { title: '旧数组消息仍显示', source: '东方财富', publishedAt: '2026-03-12T09:40:00Z' }
            ])
          })
        }

        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    await flushPromises()
    await flushPromises()

    await wrapper.find('.search-field input').setValue('000518')
    await wrapper.find('.search-field button').trigger('click')
    await flushPromises()
    await flushPromises()

    const tape = wrapper.find('.tape-card')
    expect(tape.text()).toContain('旧数组消息仍显示')
    expect(tape.text()).not.toContain('消息降级')
    expect(tape.text()).not.toContain('暂不可用')
  }
  },
  {
    title: "falls back to global overview and disables stock tabs when no stock is selected",
    run: async () => {
    const { fetchMock } = createChatFetchMock()
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    await flushPromises()
    await flushPromises()

    const activeTab = wrapper.find('.sc-tabs__item--active')
    expect(activeTab.text()).toContain('全局总览')
    const planTab = wrapper.findAll('.sc-tabs__item').find(tab => tab.text().includes('交易计划'))
    expect(planTab?.attributes('disabled')).toBeDefined()
    expect(planTab?.attributes('title')).toBe('选择股票后可用')
  }
  },
  {
    title: "labels intraday messages published outside trading hours",
    run: async () => {
    const { fetchMock } = createChatFetchMock()
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '深科技', symbol: 'sz000021', price: 31.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: [
        { title: '盘前公告', source: '交易所', publishedAt: '2026-03-12T01:00:00Z' },
        { title: '盘后公告', source: '交易所', publishedAt: '2026-03-12T07:10:00Z' },
        { title: '周末公告', source: '交易所', publishedAt: '2026-03-14T02:00:00Z' }
      ]
    }

    await wrapper.vm.$nextTick()

    const tapeText = wrapper.find('.messages').text()
    expect(tapeText).toContain('发布时间')
    expect(tapeText).toContain('盘前')
    expect(tapeText).toContain('盘后')
    expect(tapeText).toContain('非交易时段')
  }
  },
  {
    title: "renders trading workbench placeholder before any stock is loaded",
    run: async () => {
    const { fetchMock } = createChatFetchMock()
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    await flushPromises()

    expect(wrapper.find('.trading-workbench').exists()).toBe(true)
    expect(wrapper.text()).toContain('研究报告')
  }
  }
]
