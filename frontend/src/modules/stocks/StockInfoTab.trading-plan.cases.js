const getPlanModalBackdrop = () => document.body.querySelector('[data-testid="stock-plan-modal-backdrop"]')
const getPlanModal = () => document.body.querySelector('[data-testid="stock-plan-modal"]')
const getPlanMarketBox = () => getPlanModal()?.querySelector('.plan-market-box')
const getPlanModalCloseButton = () => getPlanModal()?.querySelector('.search-modal-header .market-news-button')

const setTeleportedFieldValue = (element, value) => {
  element.value = value
  element.dispatchEvent(new Event('input', { bubbles: true }))
  element.dispatchEvent(new Event('change', { bubbles: true }))
}

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
    title: "keeps the trading plan section mounted for the active workspace even when detail is missing",
    run: async () => {
    const { fetchMock } = createChatFetchMock({
      handle: async url => {
        if (url.startsWith('/api/stocks/plans?symbol=sh603099')) {
          return makeResponse({ ok: true, status: 200, json: async () => ([]) })
        }

        if (url.startsWith('/api/stocks/plans/alerts?symbol=sh603099')) {
          return makeResponse({ ok: true, status: 200, json: async () => ([]) })
        }

        if (url.startsWith('/api/stocks/market-context?symbol=sh603099')) {
          return makeResponse({ ok: false, status: 404, json: async () => ({ message: 'not found' }) })
        }

        if (url === '/api/market/sentiment/latest') {
          return makeResponse({ ok: true, status: 200, json: async () => ({ snapshotTime: '2026-04-21T08:00:00Z' }) })
        }

        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '长白山', symbol: 'sh603099', price: 36.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }

    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()

    wrapper.vm.currentWorkspace.detail = null
    wrapper.vm.currentWorkspace.planList = []
    wrapper.vm.currentWorkspace.planAlerts = []
    wrapper.vm.currentWorkspace.planListLoaded = true
    wrapper.vm.currentWorkspace.planAlertsLoaded = true

    await wrapper.vm.$nextTick()
    await flushPromises()

    const planSection = wrapper.find('.stock-plan-section')
    expect(planSection.exists()).toBe(true)
    expect(planSection.text()).toContain('当前交易计划')
    expect(planSection.text()).toContain('暂无交易计划，可点击「新建计划」手动录入')

    const createButton = wrapper.findAll('.plan-header-actions .market-news-button').find(button => button.text() === '新建计划')
    expect(createButton).toBeTruthy()
    expect(createButton?.attributes('disabled')).toBeUndefined()

    await createButton.trigger('click')
    await flushPromises()
    await flushPromises()

    expect(getPlanModal()).toBeTruthy()
    const stockSymbolInput = getPlanModal()?.querySelector('.plan-field input[disabled]')
    expect(stockSymbolInput?.value).toBe('sh603099')
    expect(getPlanModal()?.textContent || '').toContain('手动新建计划')
  }
  },
  {
    title: "renders the trading plan modal in the global modal layer and only closes on backdrop click",
    run: async () => {
    const { fetchMock } = createChatFetchMock({ handle: async () => null })

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

    const backdrop = getPlanModalBackdrop()
    const modal = getPlanModal()

    expect(backdrop).toBeTruthy()
    expect(modal).toBeTruthy()
    expect(document.body.contains(backdrop)).toBe(true)
    expect(backdrop?.getAttribute('data-modal-layer')).toBe('global-modal')
    expect(backdrop?.getAttribute('style') || '').toContain('z-index: var(--z-modal);')
    expect(getPlanModal()?.querySelector('[data-testid="plan-active-scenario-select"]')).toBeTruthy()
    expect(getPlanModal()?.querySelector('[data-testid="plan-status-select"]')).toBeTruthy()
    expect(getPlanModal()?.querySelector('[data-testid="plan-start-date-input"]')).toBeTruthy()
    expect(getPlanModal()?.querySelector('[data-testid="plan-end-date-input"]')).toBeTruthy()
    expect(getPlanModal()?.textContent || '').toContain('开始日期')
    expect(getPlanModal()?.textContent || '').toContain('结束日期')
    expect(getPlanModal()?.querySelector('.plan-save-button')?.textContent || '').toContain('保存计划')

    modal?.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await flushPromises()
    expect(document.body.querySelector('[data-testid="stock-plan-modal"]')).toBeTruthy()

    backdrop?.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await flushPromises()
    expect(document.body.querySelector('[data-testid="stock-plan-modal"]')).toBeNull()
  }
  },
  {
    title: "blocks invalid long trading-plan stop loss and take profit prices",
    run: async () => {
    const { fetchMock } = createChatFetchMock({ handle: async () => null })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '深科技', symbol: 'sz000021', price: 10, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }

    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()

    const createButton = wrapper.findAll('.plan-header-actions .market-news-button').find(button => button.text() === '新建计划')
    await createButton.trigger('click')
    await flushPromises()

    const numericInputs = Array.from(getPlanModal()?.querySelectorAll('input[type="number"]') || [])
    setTeleportedFieldValue(numericInputs[0], '10')
    setTeleportedFieldValue(numericInputs[2], '11')
    setTeleportedFieldValue(numericInputs[3], '12')
    getPlanModal()?.querySelector('.plan-save-button')?.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await wrapper.vm.$nextTick()

    expect(getPlanModal()?.textContent || '').toContain('买入计划止损价不应高于触发价或当前价')
    expect(fetchMock.mock.calls.some(([url, options]) => url === '/api/stocks/plans' && options?.method === 'POST')).toBe(false)

    setTeleportedFieldValue(numericInputs[2], '9')
    setTeleportedFieldValue(numericInputs[3], '8')
    getPlanModal()?.querySelector('.plan-save-button')?.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await wrapper.vm.$nextTick()

    expect(getPlanModal()?.textContent || '').toContain('买入计划止盈价不应低于触发价或当前价')
    expect(fetchMock.mock.calls.some(([url, options]) => url === '/api/stocks/plans' && options?.method === 'POST')).toBe(false)
  }
  },
  {
    title: "blocks invalid short trading-plan stop loss and take profit prices",
    run: async () => {
    const { fetchMock } = createChatFetchMock({ handle: async () => null })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '深科技', symbol: 'sz000021', price: 10, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }

    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()

    const createButton = wrapper.findAll('.plan-header-actions .market-news-button').find(button => button.text() === '新建计划')
    await createButton.trigger('click')
    await flushPromises()

    setTeleportedFieldValue(getPlanModal()?.querySelector('select'), 'Short')
    const numericInputs = Array.from(getPlanModal()?.querySelectorAll('input[type="number"]') || [])
    setTeleportedFieldValue(numericInputs[0], '10')
    setTeleportedFieldValue(numericInputs[2], '9')
    setTeleportedFieldValue(numericInputs[3], '8')
    getPlanModal()?.querySelector('.plan-save-button')?.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await wrapper.vm.$nextTick()

    expect(getPlanModal()?.textContent || '').toContain('卖出/减仓计划止损价不应低于触发价或当前价')
    expect(fetchMock.mock.calls.some(([url, options]) => url === '/api/stocks/plans' && options?.method === 'POST')).toBe(false)

    setTeleportedFieldValue(numericInputs[2], '11')
    setTeleportedFieldValue(numericInputs[3], '11')
    getPlanModal()?.querySelector('.plan-save-button')?.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await wrapper.vm.$nextTick()

    expect(getPlanModal()?.textContent || '').toContain('卖出/减仓计划止盈价不应高于触发价或当前价')
    expect(fetchMock.mock.calls.some(([url, options]) => url === '/api/stocks/plans' && options?.method === 'POST')).toBe(false)
  }
  },
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

  const backdrop = getPlanModalBackdrop()
  expect(getPlanModal()).toBeTruthy()
  expect(backdrop).toBeTruthy()
  expect(backdrop?.getAttribute('style') || '').toContain('z-index: var(--z-modal);')
  expect(getPlanMarketBox()?.textContent || '').toContain('市场上下文')
  expect(getPlanMarketBox()?.textContent || '').toContain('正在获取当前市场上下文，不影响保存计划。')

    marketContextDeferred.resolve()
    await flushPromises()
    await flushPromises()

  const marketBoxText = getPlanMarketBox()?.textContent || ''
    expect(marketBoxText).toContain('阶段 主升')
    expect(marketBoxText).toContain('快照时间 2026-04-17')
    expect(marketBoxText).toContain('主线 AI 算力')
    expect(marketBoxText).toContain('建议仓位')
    expect(marketBoxText).not.toContain('当前未获取到')
    expect(marketBoxText).not.toContain('正在获取当前市场上下文，不影响保存计划。')
    expect(fetchMock.mock.calls.some(args => String(args[0]).startsWith('/api/stocks/market-context?symbol=sz000021'))).toBe(true)
  }
  },
  {
    title: "supports editing scenario, status and date range before cancelling a trading plan",
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
              activeScenario: 'Backup',
              planStartDate: '2026-03-15',
              planEndDate: '2026-03-22',
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
              activeScenario: 'Backup',
              planStartDate: '2026-03-15',
              planEndDate: '2026-03-22',
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
                status: stockPlanListCalls >= 2 ? 'Draft' : 'Pending',
                activeScenario: stockPlanListCalls >= 2 ? 'Backup' : 'Primary',
                planStartDate: stockPlanListCalls >= 2 ? '2026-03-15' : '2026-03-14',
                planEndDate: stockPlanListCalls >= 2 ? '2026-03-22' : '2026-03-21',
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
                  activeScenario: 'Backup', planStartDate: '2026-03-15', planEndDate: '2026-03-22',
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
                status: boardPlanListCalls >= 2 ? 'Draft' : 'Pending',
                activeScenario: boardPlanListCalls >= 2 ? 'Backup' : 'Primary',
                planStartDate: boardPlanListCalls >= 2 ? '2026-03-15' : '2026-03-14',
                planEndDate: boardPlanListCalls >= 2 ? '2026-03-22' : '2026-03-21',
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

  expect(getPlanModal()).toBeTruthy()
  const takeProfitInput = getPlanModal()?.querySelector('input[placeholder="优先取指挥/机构目标"]')
  const targetPriceInput = getPlanModal()?.querySelector('input[placeholder="优先取指挥/趋势目标"]')
  const scenarioSelect = getPlanModal()?.querySelector('[data-testid="plan-active-scenario-select"]')
  const statusSelect = getPlanModal()?.querySelector('[data-testid="plan-status-select"]')
  const startDateInput = getPlanModal()?.querySelector('[data-testid="plan-start-date-input"]')
  const endDateInput = getPlanModal()?.querySelector('[data-testid="plan-end-date-input"]')
  const noteTextareas = getPlanModal()?.querySelectorAll('.plan-field textarea') || []
  const saveButton = getPlanModal()?.querySelector('.plan-save-button')

  expect(takeProfitInput?.value).toBe('13.4')
  expect(targetPriceInput?.value).toBe('14.2')
  expect(scenarioSelect?.value).toBe('Primary')
  expect(statusSelect?.value).toBe('Pending')
  expect(startDateInput?.value).toBe('2026-03-14')
  expect(endDateInput?.value).toBe('2026-03-21')

  setTeleportedFieldValue(takeProfitInput, '13.9')
  setTeleportedFieldValue(targetPriceInput, '14.8')
  setTeleportedFieldValue(scenarioSelect, 'Backup')
  setTeleportedFieldValue(statusSelect, 'Draft')
  setTeleportedFieldValue(startDateInput, '2026-03-15')
  setTeleportedFieldValue(endDateInput, '2026-03-22')
  setTeleportedFieldValue(noteTextareas[noteTextareas.length - 1], '上调目标')

  saveButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await flushPromises()
    await flushPromises()

    const updateCall = fetchMock.mock.calls.find(args => args[0] === '/api/stocks/plans/7' && args[1]?.method === 'PUT')
    expect(updateCall).toBeTruthy()
    expect(JSON.parse(updateCall[1].body)).toMatchObject({
      status: 'Draft',
      activeScenario: 'Backup',
      planStartDate: '2026-03-15',
      planEndDate: '2026-03-22',
      takeProfitPrice: 13.9,
      targetPrice: 14.8,
      userNote: '上调目标'
    })
    expect(wrapper.text()).toContain('草稿')
    expect(wrapper.text()).toContain('启用 备选场景')
    expect(wrapper.text()).toContain('有效期 2026-03-15 ~ 2026-03-22')
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
  expect(getPlanMarketBox()?.textContent || '').toContain('正在获取当前市场上下文，不影响保存计划。')

  getPlanModalCloseButton()?.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await flushPromises()
  expect(getPlanModal()).toBeNull()

    await createButton.trigger('click')
    await flushPromises()
    await flushPromises()

    firstMarketContextDeferred.resolve()
    await flushPromises()
    await flushPromises()

    const marketBoxText = getPlanMarketBox()?.textContent || ''
    expect(marketContextCallCount).toBe(2)
    expect(getPlanModal()).toBeTruthy()
    expect(marketBoxText).toContain('当前未获取到市场阶段、主线方向、仓位建议、执行节奏，不影响保存计划。')
    expect(marketBoxText).not.toContain('过期结果')
    expect(marketBoxText).not.toContain('旧主线')
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
    title: "renders scenario status and execution summary on current trading plans",
    run: async () => {
    const { fetchMock } = createChatFetchMock({
      handle: async url => {
        if (url.startsWith('/api/stocks/plans?symbol=sz000021')) {
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ([{
              id: 7,
              symbol: 'sz000021',
              name: '深科技',
              direction: 'Long',
              status: 'Triggered',
              triggerPrice: 12.6,
              invalidPrice: 11.9,
              stopLossPrice: 11.5,
              takeProfitPrice: 13.4,
              targetPrice: 14.2,
              analysisSummary: '等待突破确认',
              sourceAgent: 'manual',
              currentScenarioStatus: {
                code: 'Abandon',
                label: '放弃条件命中',
                reason: '价格跌破失效位',
                summary: '放弃条件命中 · 价格跌破失效位',
                referencePrice: 11.88,
                abandonTriggered: true
              },
              currentPositionSnapshot: {
                quantity: 1200,
                positionRatio: 0.14,
                marketValue: 14256,
                unrealizedPnL: -320,
                summary: '当前持仓 1200 股 · 成本 12.14 · 浮盈 -320.00'
              },
              executionSummary: {
                executionCount: 2,
                latestAction: '加仓执行',
                latestExecutedAt: '2026-03-14T09:40:00Z',
                latestDeviationTags: ['追价', '超仓'],
                summary: '已执行 2 次 · 最近 加仓执行 · 偏差 1 次'
              },
              updatedAt: '2026-03-14T09:00:00Z',
              createdAt: '2026-03-14T08:30:00Z'
            }])
          })
        }

        if (url.startsWith('/api/stocks/plans?take=20')) {
          return makeResponse({ ok: true, status: 200, json: async () => ([]) })
        }

        if (url.startsWith('/api/stocks/plans/alerts?symbol=sz000021') || url.startsWith('/api/stocks/plans/alerts?take=20')) {
          return makeResponse({ ok: true, status: 200, json: async () => ([]) })
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

    const currentPlanCard = wrapper.findAll('section').find(section => section.text().includes('当前交易计划'))
    expect(currentPlanCard.text()).toContain('放弃条件命中')
    expect(currentPlanCard.text()).toContain('当前持仓快照')
    expect(currentPlanCard.text()).toContain('已执行 2 次')
    expect(currentPlanCard.text()).toContain('最近动作')
    expect(currentPlanCard.text()).not.toContain('待复盘')
    expect(currentPlanCard.text()).toContain('追价 / 超仓')
    expect(currentPlanCard.find('.plan-execution-strip').exists()).toBe(true)
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
