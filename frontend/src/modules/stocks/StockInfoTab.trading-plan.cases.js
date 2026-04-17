export const stockInfoTabTradingPlanCases = ({
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
    title: "loads market context for new manual trading plans without blocking the modal",
    run: async () => {
    const marketContextDeferred = createDeferred()

    const { fetchMock } = createChatFetchMock({
      handle: async (url, options = {}) => {
        if (url === '/api/market/sentiment/latest') {
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              snapshotTime: '2026-04-17T06:43:08Z'
            })
          })
        }

        if (url.startsWith('/api/stocks/market-context?symbol=sz000021')) {
          return createAbortableResponse(
            marketContextDeferred,
            () => makeResponse({
              ok: true,
              status: 200,
              json: async () => ({
                stageLabel: '主升',
                stageConfidence: 78,
                mainlineSectorName: 'AI 算力',
                suggestedPositionScale: 0.6,
                executionFrequencyLabel: '积极',
                counterTrendWarning: false,
                isMainlineAligned: true
              })
            }),
            options.signal
          )
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

    const createButton = wrapper.findAll('.plan-header-actions .market-news-button').find(button => button.text() === '新建计划')
    expect(createButton).toBeTruthy()

    await createButton.trigger('click')
    await flushPromises()

    expect(wrapper.find('.plan-modal').exists()).toBe(true)
    expect(wrapper.find('.plan-market-box').text()).toContain('市场上下文')
    expect(wrapper.find('.plan-market-box').text()).toContain('正在获取当前市场上下文，不影响保存计划。')

    marketContextDeferred.resolve()
    await flushPromises()
    await flushPromises()

    const marketBox = wrapper.find('.plan-market-box')
    expect(marketBox.text()).toContain('阶段 主升')
    expect(marketBox.text()).toContain('快照时间 2026-04-17')
    expect(marketBox.text()).toContain('主线 AI 算力')
    expect(marketBox.text()).toContain('建议仓位')
    expect(marketBox.text()).not.toContain('正在获取当前市场上下文，不影响保存计划。')
    expect(fetchMock.mock.calls.some(args => String(args[0]).startsWith('/api/stocks/market-context?symbol=sz000021'))).toBe(true)
  }
  },
  {
    title: "supports editing and cancelling pending trading plans",
    run: async () => {
    let stockPlanListCalls = 0
    let boardPlanListCalls = 0
    let planCancelled = false
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

        if (url === '/api/stocks/plans/7/cancel' && options.method === 'POST') {
          planCancelled = true
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              id: 7,
              symbol: 'sz000021',
              name: '深科技',
              direction: 'Long',
              status: 'Cancelled',
              triggerPrice: 12.6,
              invalidPrice: 11.9,
              stopLossPrice: 11.3,
              takeProfitPrice: 13.9,
              targetPrice: 14.8,
              expectedCatalyst: '突破前高',
              invalidConditions: '跳破支撑',
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

        if (url.startsWith('/api/stocks/plans?symbol=sz000021')) {
          stockPlanListCalls += 1
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => {
              if (planCancelled) {
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
              if (planCancelled) {
                return [{
                  id: 7, symbol: 'sz000021', name: '深科技', direction: 'Long', status: 'Cancelled',
                  triggerPrice: 12.6, invalidPrice: 11.9, stopLossPrice: 11.3, takeProfitPrice: 13.9,
                  targetPrice: 14.8, expectedCatalyst: '突破前高', invalidConditions: '跌破支撑',
                  riskLimits: '单笔亏损不超过 2%', analysisSummary: '等待突破确认',
                  analysisHistoryId: 42, sourceAgent: 'commander', userNote: '上调目标',
                  updatedAt: '2026-03-14T09:00:00Z', createdAt: '2026-03-14T08:30:00Z'
                }]
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

    await wrapper.find('[data-testid="cancel-plan-btn"]').trigger('click')
    await flushPromises()

    // Confirm the cancellation in the confirmation popover
    await wrapper.find('.confirm-yes').trigger('click')
    await flushPromises()
    await flushPromises()

    expect(fetchMock.mock.calls.some(args => args[0] === '/api/stocks/plans/7/cancel' && args[1]?.method === 'POST')).toBe(true)
    expect(wrapper.text()).toContain('已取消')
  }
  },
  {
    title: "shows fallback for manual plan market context and ignores stale responses after reopen",
    run: async () => {
    const firstMarketContextDeferred = createDeferred()
    let marketContextCallCount = 0

    const { fetchMock } = createChatFetchMock({
      handle: async (url, options = {}) => {
        if (url.startsWith('/api/stocks/market-context?symbol=sz000021')) {
          marketContextCallCount += 1

          if (marketContextCallCount === 1) {
            return createAbortableResponse(
              firstMarketContextDeferred,
              () => makeResponse({
                ok: true,
                status: 200,
                json: async () => ({
                  stageLabel: '过期结果',
                  stageConfidence: 35,
                  mainlineSectorName: '旧主线',
                  suggestedPositionScale: 0.2,
                  executionFrequencyLabel: '谨慎',
                  counterTrendWarning: true,
                  isMainlineAligned: false
                })
              }),
              options.signal
            )
          }

          return makeResponse({
            ok: false,
            status: 404,
            json: async () => ({ message: 'not found' })
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

    const createButton = wrapper.findAll('.plan-header-actions .market-news-button').find(button => button.text() === '新建计划')
    expect(createButton).toBeTruthy()

    await createButton.trigger('click')
    await flushPromises()
    expect(wrapper.find('.plan-market-box').text()).toContain('正在获取当前市场上下文，不影响保存计划。')

    await wrapper.find('.search-modal-header .market-news-button').trigger('click')
    await flushPromises()
    expect(wrapper.find('.plan-modal').exists()).toBe(false)

    await createButton.trigger('click')
    await flushPromises()
    await flushPromises()

    firstMarketContextDeferred.resolve()
    await flushPromises()
    await flushPromises()

    const marketBox = wrapper.find('.plan-market-box')
    expect(marketContextCallCount).toBe(2)
    expect(wrapper.find('.plan-modal').exists()).toBe(true)
    expect(marketBox.text()).toContain('暂未获取到市场上下文，可继续保存计划。')
    expect(marketBox.text()).not.toContain('过期结果')
    expect(marketBox.text()).not.toContain('旧主线')
  }
  },
  {
    title: "renders trading plan alerts and refreshes board summaries",
    run: async () => {
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
  }
  },
  {
    title: "polls the trading plan board even when no stock is active",
    run: async () => {
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

      await vi.advanceTimersByTimeAsync(120000)
      await Promise.resolve()
      await vi.advanceTimersByTimeAsync(0)

      expect(boardPlanCalls).toBeGreaterThanOrEqual(2)
      expect(boardAlertCalls).toBeGreaterThanOrEqual(2)
    } finally {
      vi.useRealTimers()
    }
  }
  },
  {
    title: "retries transient trading plan board fetch failures on initial load",
    run: async () => {
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
  }
  }
]
