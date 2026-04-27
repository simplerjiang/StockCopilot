import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import TradeLogTab from './TradeLogTab.vue'

const mockConfirm = vi.fn(() => Promise.resolve(true))
vi.mock('../../composables/useConfirm.js', () => ({
  useConfirm: () => ({ confirm: mockConfirm })
}))

const makeResponse = ({ ok = true, json, text } = {}) => ({
  ok,
  json: json || (async () => ({})),
  text: text || (async () => '{}')
})

const flushPromises = () => new Promise(resolve => setTimeout(resolve, 0))
const wait = (ms) => new Promise(resolve => setTimeout(resolve, ms))
const waitForStockSearch = async () => {
  await wait(350)
  await flushPromises()
  await flushPromises()
}
const getTradeField = (wrapper, testId) => wrapper.get(`[data-testid="${testId}"]`)
const getLabeledValue = (wrapper, itemSelector, labelSelector, valueSelector, labelText) => {
  const item = wrapper.findAll(itemSelector).find(node => {
    const label = node.find(labelSelector)
    return label.exists() && label.text() === labelText
  })
  expect(item).toBeTruthy()
  return item.find(valueSelector).text()
}

const defaultSnapshot = {
  totalCapital: 100000,
  totalCost: 60000,
  totalMarketValue: 80000,
  totalUnrealizedPnL: 2000,
  availableCash: 20000,
  totalPositionRatio: 0.8,
  positions: []
}

const navigateStockPosition = {
  symbol: '000001',
  name: '平安银行',
  quantity: 1000,
  quantityLots: 1000,
  averageCost: 10.5,
  totalCost: 10500,
  latestPrice: 11.2,
  marketValue: 11200,
  unrealizedPnL: 700,
  positionRatio: 0.112
}

const defaultSummary = {
  totalPnL: 1500,
  winRate: 0.6,
  profitLossRatio: 2.1,
  dayTradePnL: 300,
  plannedTradeCount: 3,
  totalTrades: 5,
  complianceRate: 0.8,
  maxSingleLoss: -500
}

const defaultExposure = {
  combinedExposure: 0.5,
  totalExposure: 0.4,
  pendingExposure: 0.1,
  currentMode: {
    executionMode: '正常执行',
    confirmationLevel: 'normal'
  },
  symbolExposures: []
}

const defaultBehaviorStats = {
  disciplineScore: 80,
  planExecutionRate: 0.8,
  currentLossStreak: 0,
  trades7Days: 3,
  isOverTrading: false,
  activeAlerts: []
}

const defaultPlanList = [
  {
    id: 7,
    symbol: '000001',
    name: '平安银行',
    status: 'Pending',
    direction: 'Long',
    activeScenario: 'Primary',
    triggerPrice: 10.45,
    analysisSummary: '等待突破确认',
    currentScenarioStatus: {
      code: 'Primary',
      label: '主场景',
      summary: '价格正在接近触发位'
    },
    currentMarketContext: {
      stageLabel: '主升'
    },
    executionSummary: {
      executionCount: 2,
      summary: '已执行 2 次 · 最近一次为买入执行'
    }
  },
  {
    id: 12,
    symbol: '600999',
    name: '示例失效计划',
    status: 'Invalid',
    direction: 'Long',
    triggerPrice: 25.4,
    analysisSummary: '这是一条用于测试“全部计划”切换的计划'
  }
]

const defaultPlanAlerts = [
  {
    id: 101,
    planId: 7,
    symbol: '000001',
    eventType: 'Warning',
    severity: 'Warning',
    message: '价格接近触发位',
    occurredAt: '2026-04-03T09:32:00Z'
  }
]

const defaultTrades = [
  {
    id: 1,
    symbol: '000001',
    name: '平安银行',
    direction: 'Buy',
    tradeType: 'Normal',
    executedPrice: 10.5,
    quantity: 1000,
    executedAt: '2026-04-03T09:30:00Z',
    complianceTag: 'FollowedPlan',
    realizedPnL: 200,
    returnRate: 0.02,
    planTitle: '银行板块计划',
    agentDirection: 'Buy',
    agentConfidence: 0.85,
    planId: 7,
    planAction: '计划买入',
    executionAction: '买入执行',
    deviationTags: [],
    scenarioSnapshot: {
      code: 'Primary',
      label: '主场景',
      reason: '当前价格已接近触发位',
      snapshotType: 'Historical',
      referencePrice: 10.5,
      planStatus: 'Triggered'
    },
    positionSnapshot: {
      symbol: '000001',
      quantity: 1000,
      averageCost: 10.2,
      marketValue: 10500,
      unrealizedPnL: 300,
      positionRatio: 0.1,
      snapshotType: 'Historical',
      summary: '执行时持仓 1000 股 · 浮盈 +300.00'
    },
    coachTip: '本次执行基本仍在预案内，继续跟踪场景变化与仓位节奏。'
  }
]

const defaultPlanExecutionContext = {
  plan: {
    id: 7,
    symbol: '000001',
    name: '平安银行',
    sourceAgent: 'manual',
    analysisSummary: '等待突破确认'
  },
  scenarioStatus: {
    code: 'Primary',
    label: '主场景',
    reason: '现价接近触发位',
    snapshotType: 'Current',
    referencePrice: 10.48,
    marketStage: '主升',
    planStatus: 'Pending',
    summary: '主场景 · 现价接近触发位'
  },
  currentPositionSnapshot: {
    symbol: '000001',
    quantity: 1200,
    averageCost: 10.12,
    marketValue: 12576,
    unrealizedPnL: 432,
    positionRatio: 0.13,
    snapshotType: 'Current',
    summary: '当前持仓 1200 股 · 成本 10.12 · 浮盈 +432.00'
  },
  portfolioSummary: {
    summary: '当前总仓位 62.0% · 可用资金 38000.00 · 浮盈 +2000.00'
  },
  executionSummary: {
    executionCount: 2,
    latestAction: '买入执行',
    latestExecutedAt: '2026-04-03T09:30:00Z',
    latestComplianceTag: 'DeviatedFromPlan',
    latestDeviationTags: ['追价'],
    summary: '已执行 2 次 · 最近 买入执行 · 偏差 1 次'
  }
}

function setupFetchMock(overrides = {}) {
  const responses = {
    '/api/portfolio/snapshot': makeResponse({ json: async () => overrides.snapshot ?? defaultSnapshot }),
    '/api/portfolio/exposure': makeResponse({ json: async () => overrides.exposure ?? defaultExposure }),
    '/api/trades/summary': makeResponse({ json: async () => overrides.summary ?? defaultSummary }),
    '/api/trades': makeResponse({ json: async () => overrides.trades ?? defaultTrades }),
    '/api/trades/behavior-stats': makeResponse({ json: async () => overrides.behaviorStats ?? defaultBehaviorStats }),
    '/api/trades/reviews': makeResponse({ json: async () => overrides.reviewList ?? [] }),
    '/api/stocks/plans?': makeResponse({ json: async () => overrides.plans ?? defaultPlanList }),
    '/api/stocks/plans/alerts?': makeResponse({ json: async () => overrides.planAlerts ?? defaultPlanAlerts }),
    ...overrides.extra
  }

  return vi.fn(async (url, opts) => {
    const key = Object.keys(responses).find(k => url.startsWith(k))
    if (key) return responses[key]
    return makeResponse()
  })
}

const mountWithNavigablePosition = async () => {
  vi.stubGlobal('fetch', setupFetchMock({
    snapshot: {
      ...defaultSnapshot,
      positions: [navigateStockPosition]
    }
  }))
  const wrapper = mount(TradeLogTab)
  await flushPromises()
  await flushPromises()
  return wrapper
}

const listenForNavigateStock = () => {
  const handler = vi.fn()
  window.addEventListener('navigate-stock', handler)
  return {
    handler,
    cleanup: () => window.removeEventListener('navigate-stock', handler)
  }
}

const expectNavigateStockEvent = (handler) => {
  expect(handler).toHaveBeenCalledTimes(1)
  expect(handler.mock.calls[0][0].detail).toEqual({
    symbol: navigateStockPosition.symbol,
    name: navigateStockPosition.name
  })
}

const mountQuickEntryWithStockSearch = async (stockSearchResults) => {
  const fetchMock = setupFetchMock({
    extra: {
      '/api/stocks/search?': makeResponse({ json: async () => stockSearchResults })
    }
  })
  vi.stubGlobal('fetch', fetchMock)
  const wrapper = mount(TradeLogTab)
  await flushPromises()
  await flushPromises()

  await wrapper.find('.toolbar-actions .btn-primary').trigger('click')
  await flushPromises()

  return { wrapper, fetchMock }
}

const mountQuickEntryWithStockSearchByQuery = async (stockSearchResultsByQuery) => {
  const baseFetchMock = setupFetchMock()
  const fetchMock = vi.fn(async (url, opts) => {
    const requestUrl = String(url)
    if (requestUrl.startsWith('/api/stocks/search?')) {
      const query = new URL(requestUrl, 'http://local.test').searchParams.get('q') || ''
      return makeResponse({ json: async () => stockSearchResultsByQuery[query] ?? [] })
    }
    return baseFetchMock(url, opts)
  })
  vi.stubGlobal('fetch', fetchMock)
  const wrapper = mount(TradeLogTab)
  await flushPromises()
  await flushPromises()

  await wrapper.find('.toolbar-actions .btn-primary').trigger('click')
  await flushPromises()

  return { wrapper, fetchMock }
}

const fillSymbolAndBlur = async (wrapper, symbol) => {
  const symbolInput = getTradeField(wrapper, 'trade-symbol')
  await symbolInput.setValue(symbol)
  await symbolInput.trigger('blur')
  await flushPromises()
  await flushPromises()
}

const fillSymbolAndWaitForSearch = async (wrapper, symbol) => {
  await getTradeField(wrapper, 'trade-symbol').setValue(symbol)
  await waitForStockSearch()
}

beforeEach(() => {
  vi.restoreAllMocks()
  mockConfirm.mockImplementation(() => Promise.resolve(true))
})

describe('TradeLogTab', () => {
  // ── Rendering ──

  it('renders portfolio snapshot and summary on mount', async () => {
    vi.stubGlobal('fetch', setupFetchMock())
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    expect(wrapper.text()).toContain('持仓总览')
    expect(wrapper.text()).toContain('仓位')
    expect(wrapper.text()).toContain('总盈亏')
    expect(wrapper.text()).toContain('胜率')
  })

  it('normalizes PascalCase portfolio snapshot totals', async () => {
    vi.stubGlobal('fetch', setupFetchMock({
      snapshot: {
        TotalCapital: 250000,
        TotalCost: 123456.78,
        TotalMarketValue: 130000.25,
        TotalUnrealizedPnL: 6543.47,
        AvailableCash: 126000.75,
        TotalPositionRatio: 0.493827,
        Positions: []
      }
    }))
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    expect(wrapper.get('[data-testid="portfolio-total-capital"]').text()).toBe('250000.00')
    expect(wrapper.get('[data-testid="portfolio-total-cost"]').text()).toBe('123456.78')
    expect(wrapper.get('[data-testid="portfolio-total-market-value"]').text()).toBe('130000.25')
    expect(wrapper.get('[data-testid="portfolio-total-unrealized-pnl"]').text()).toBe('+6543.47')
    expect(wrapper.get('[data-testid="portfolio-available-cash"]').text()).toBe('126000.75')
  })

  it('normalizes PascalCase portfolio position rows without mixing cost market value and PnL', async () => {
    vi.stubGlobal('fetch', setupFetchMock({
      snapshot: {
        TotalCapital: 100000,
        TotalCost: 35530,
        TotalMarketValue: 38000,
        TotalUnrealizedPnL: 2470,
        AvailableCash: 64470,
        TotalPositionRatio: 0.3553,
        Positions: [
          {
            Symbol: '000001',
            Name: '平安银行',
            QuantityLots: 1000,
            AvgCostPrice: 35.53,
            TotalCost: 35530,
            LatestPrice: 38,
            MarketValue: 38000,
            UnrealizedPnL: 2470,
            UnrealizedReturnRate: 0.0695,
            PositionRatio: 0.3553
          }
        ]
      }
    }))
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    expect(wrapper.text()).toContain('平安银行')
    expect(wrapper.get('[data-testid="portfolio-position-cost-000001"]').text()).toBe('成本 35530.00')
    expect(wrapper.get('[data-testid="portfolio-position-market-value-000001"]').text()).toBe('市值 38000.00')
    expect(wrapper.get('[data-testid="portfolio-position-pnl-000001"]').text()).toBe('浮盈 +2470.00')
  })

  it('dispatches navigate-stock with symbol and name when clicking a portfolio position row', async () => {
    const wrapper = await mountWithNavigablePosition()
    const row = wrapper.get('.position-item-clickable')
    const { handler, cleanup } = listenForNavigateStock()

    expect(row.attributes('role')).toBe('button')
    expect(row.attributes('tabindex')).toBe('0')

    try {
      await row.trigger('click')

      expectNavigateStockEvent(handler)
    } finally {
      cleanup()
    }
  })

  it('dispatches navigate-stock with symbol and name when pressing Enter on a portfolio position row', async () => {
    const wrapper = await mountWithNavigablePosition()
    const row = wrapper.get('.position-item-clickable')
    const { handler, cleanup } = listenForNavigateStock()

    try {
      await row.trigger('keydown', { key: 'Enter', code: 'Enter' })

      expectNavigateStockEvent(handler)
    } finally {
      cleanup()
    }
  })

  it('dispatches navigate-stock with symbol and name when pressing Space on a portfolio position row', async () => {
    const wrapper = await mountWithNavigablePosition()
    const row = wrapper.get('.position-item-clickable')
    const { handler, cleanup } = listenForNavigateStock()

    try {
      await row.trigger('keydown', { key: ' ', code: 'Space' })

      expectNavigateStockEvent(handler)
    } finally {
      cleanup()
    }
  })

  it('renders trade list items', async () => {
    vi.stubGlobal('fetch', setupFetchMock())
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    expect(wrapper.text()).toContain('000001')
    expect(wrapper.text()).toContain('平安银行')
    expect(wrapper.text()).toContain('计划内')
    expect(wrapper.text()).toContain('复盘工作区')
    expect(wrapper.text()).toContain('刷新当前状态')
    expect(wrapper.text()).toContain('本次执行摘要')
    expect(wrapper.text()).toContain('执行当时情况')
    expect(wrapper.text()).toContain('复盘关注点')
    expect(wrapper.text()).not.toContain('复盘提示')
    expect(wrapper.text()).not.toContain('纪律提醒')
    expect(wrapper.text()).not.toContain('持仓快照语境')
    expect(wrapper.text()).not.toContain('教练提示')
    expect(wrapper.text()).not.toContain('Coach')
  })

  it('renders dashes for health score and plan execution rate when behavior stats has zero trades', async () => {
    vi.stubGlobal('fetch', setupFetchMock({
      summary: {
        ...defaultSummary,
        totalTrades: 0,
        plannedTradeCount: 0,
        winRate: null,
        complianceRate: null
      },
      behaviorStats: {
        disciplineScore: null,
        planExecutionRate: null,
        trades7Days: 0,
        trades30Days: 0,
        avgDailyTrades7Days: 0,
        avgDailyTrades30Days: 0,
        plannedTrades30Days: 0,
        totalTrades30Days: 0,
        currentLossStreak: 0,
        maxLossStreak30Days: 0,
        chasingBuyCount30Days: 0,
        chasingBuyRate: 0,
        isOverTrading: false,
        activeAlerts: []
      }
    }))
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    const behaviorDashboard = wrapper.get('.behavior-dashboard')
    const healthScore = behaviorDashboard.get('.discipline-score').text()
    const healthPlanExecutionRate = getLabeledValue(behaviorDashboard, '.behavior-metric', '.metric-label', '.metric-value', '计划执行率')
    const summaryPlanExecutionRate = getLabeledValue(wrapper, '.summary-item', '.summary-label', '.summary-value', '计划执行率')

    expect(healthScore).toBe('—')
    expect(healthPlanExecutionRate).toBe('—')
    expect(summaryPlanExecutionRate).toBe('—')
    expect([healthScore, healthPlanExecutionRate, summaryPlanExecutionRate]).not.toContain('100 分')
    expect([healthPlanExecutionRate, summaryPlanExecutionRate]).not.toContain('100.0%')
    expect([healthPlanExecutionRate, summaryPlanExecutionRate]).not.toContain('0.0%')
  })

  it('shows active plans by default and filters trades after selecting a plan', async () => {
    vi.stubGlobal('fetch', setupFetchMock({
      trades: [
        ...defaultTrades,
        {
          ...defaultTrades[0],
          id: 2,
          symbol: '600519',
          name: '贵州茅台',
          planId: 19,
          planTitle: '白酒观察计划'
        }
      ],
      extra: {
        '/api/stocks/plans/7/execution-context': makeResponse({ json: async () => defaultPlanExecutionContext })
      }
    }))
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    expect(wrapper.text()).toContain('计划管理工作区')
    expect(wrapper.text()).not.toContain('示例失效计划')

    await wrapper.get('[data-testid="trade-plan-item-7"]').trigger('click')
    await flushPromises()
    await flushPromises()

    expect(wrapper.get('[data-testid="trade-plan-linkage-bar"]').text()).toContain('平安银行')
    const tradeItems = wrapper.findAll('.trade-list .trade-item')
    expect(tradeItems).toHaveLength(1)
    expect(tradeItems[0].text()).toContain('000001')
    expect(wrapper.text()).toContain('当前计划')
  })

  it('auto-loads feedback execution context for the default selected planned trade on first screen', async () => {
    const fetchMock = setupFetchMock({
      extra: {
        '/api/stocks/plans/7/execution-context': makeResponse({ json: async () => defaultPlanExecutionContext })
      }
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    const executionContextCalls = fetchMock.mock.calls.filter(call => String(call[0]).startsWith('/api/stocks/plans/7/execution-context'))
    expect(executionContextCalls).toHaveLength(1)
    expect(wrapper.text()).toContain('当前最新状态')
    expect(wrapper.text()).not.toContain('当前还没拿到计划的最新状态，可点击“刷新当前状态”再试一次。')
    expect(wrapper.find('[data-testid="feedback-runtime-note"]').exists()).toBe(false)
  })

  it('shows auto-loading feedback state instead of failure-like copy before runtime context returns', async () => {
    const fetchMock = vi.fn(async (url) => {
      if (url.startsWith('/api/stocks/plans/7/execution-context')) {
        return new Promise(() => {})
      }
      if (url.startsWith('/api/portfolio/snapshot')) return makeResponse({ json: async () => defaultSnapshot })
      if (url.startsWith('/api/trades/summary')) return makeResponse({ json: async () => defaultSummary })
      if (url.startsWith('/api/trades')) return makeResponse({ json: async () => defaultTrades })
      if (url.startsWith('/api/portfolio/exposure')) return makeResponse({ json: async () => defaultExposure })
      if (url.startsWith('/api/trades/behavior-stats')) return makeResponse({ json: async () => defaultBehaviorStats })
      if (url.startsWith('/api/trades/reviews')) return makeResponse({ json: async () => [] })
      if (url.startsWith('/api/stocks/plans?')) return makeResponse({ json: async () => defaultPlanList })
      if (url.startsWith('/api/stocks/plans/alerts?')) return makeResponse({ json: async () => defaultPlanAlerts })
      return makeResponse()
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    expect(wrapper.text()).toContain('正在自动加载计划最新状态...')
    expect(wrapper.text()).not.toContain('当前还没拿到计划的最新状态，可点击“刷新当前状态”再试一次。')
    expect(wrapper.find('[data-testid="feedback-runtime-note"]').exists()).toBe(false)
  })

  it('shows alert and execution summary separately in the plan workspace', async () => {
    vi.stubGlobal('fetch', setupFetchMock())
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    expect(wrapper.get('[data-testid="trade-plan-alert-7"]').text()).toContain('告警')
    expect(wrapper.get('[data-testid="trade-plan-alert-7"]').text()).toContain('价格接近触发位')
    expect(wrapper.get('[data-testid="trade-plan-execution-7"]').text()).toContain('执行摘要')
    expect(wrapper.get('[data-testid="trade-plan-execution-7"]').text()).toContain('已执行 2 次')
  })

  it('clarifies that selecting linkage affects both review and related trades', async () => {
    vi.stubGlobal('fetch', setupFetchMock())
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    const linkageHint = wrapper.get('[data-testid="trade-plan-linkage-hint-7"]')
    expect(linkageHint.text()).toContain('右侧复盘')
    expect(linkageHint.text()).toContain('关联交易')

    await wrapper.get('[data-testid="trade-plan-item-7"]').trigger('click')
    await flushPromises()
    await flushPromises()

    expect(wrapper.get('[data-testid="trade-plan-linkage-hint-7"]').text()).toContain('已联动右侧复盘')
  })

  it('supports switching to all plans and local search in the plan workspace', async () => {
    vi.stubGlobal('fetch', setupFetchMock())
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    expect(wrapper.text()).not.toContain('示例失效计划')

    const allPlansBtn = wrapper.findAll('[data-testid="trade-plan-workspace"] .btn').find(btn => btn.text() === '全部计划')
    await allPlansBtn.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('示例失效计划')

    await wrapper.get('[data-testid="trade-plan-search"]').setValue('平安')
    await flushPromises()

    expect(wrapper.text()).toContain('平安银行')
    expect(wrapper.text()).not.toContain('示例失效计划')
  })

  it('opens execution entry directly from the plan workspace quick action', async () => {
    vi.stubGlobal('fetch', setupFetchMock({
      extra: {
        '/api/stocks/plans/7/execution-context': makeResponse({ json: async () => defaultPlanExecutionContext })
      }
    }))
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    const recordTradeButton = wrapper.findAll('[data-testid="trade-plan-item-7"] button').find(btn => btn.text() === '录入执行')
    await recordTradeButton.trigger('click')
    await flushPromises()
    await flushPromises()

    expect(wrapper.find('.trade-modal').exists()).toBe(true)
    expect(wrapper.text()).toContain('录入执行')
    expect(wrapper.text()).toContain('平安银行')
  })

  it('shows no-plan hint instead of current runtime status card for unplanned trades', async () => {
    vi.stubGlobal('fetch', setupFetchMock({
      trades: [{
        ...defaultTrades[0],
        id: 11,
        planId: null,
        complianceTag: 'Unplanned',
        deviationTags: ['无计划交易'],
        planAction: null,
        planSourceAgent: null,
        executionAction: '买入执行',
        deviationNote: ''
      }]
    }))
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    expect(wrapper.text()).toContain('暂无最新预案状态')
    expect(wrapper.find('[data-testid="feedback-current-status-card"]').exists()).toBe(false)
  })

  it('refreshes current plan runtime status from the workspace header button', async () => {
    const fetchMock = setupFetchMock({
      extra: {
        '/api/stocks/plans/7/execution-context': makeResponse({ json: async () => defaultPlanExecutionContext })
      }
    })
    vi.stubGlobal('fetch', fetchMock)
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    const executionContextCallsBeforeRefresh = fetchMock.mock.calls.filter(call => String(call[0]).startsWith('/api/stocks/plans/7/execution-context'))

    await wrapper.get('[data-testid="feedback-refresh-button"]').trigger('click')
    await flushPromises()
    await flushPromises()

    expect(wrapper.text()).toContain('当前最新状态')
    expect(wrapper.text()).toContain('最新回写')
    expect(wrapper.text()).toContain('主场景')

    const executionContextCalls = fetchMock.mock.calls.filter(call => String(call[0]).startsWith('/api/stocks/plans/7/execution-context'))
    expect(executionContextCalls).toHaveLength(executionContextCallsBeforeRefresh.length + 1)
  })

  it('shows loading states', async () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))
    const wrapper = mount(TradeLogTab)
    await flushPromises()

    expect(wrapper.text()).toContain('加载持仓中...')
    expect(wrapper.text()).toContain('加载中...')
    expect(wrapper.text()).toContain('汇总加载中...')
  })

  // ── Period switching ──

  it('reloads data on period change', async () => {
    const fetchMock = setupFetchMock()
    vi.stubGlobal('fetch', fetchMock)
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    const initialCount = fetchMock.mock.calls.length

    const weekBtn = wrapper.findAll('.toolbar .btn').find(b => b.text() === '本周')
    expect(weekBtn).toBeTruthy()
    await weekBtn.trigger('click')
    await flushPromises()

    expect(fetchMock.mock.calls.length).toBeGreaterThan(initialCount)
  })

  it('shows date inputs for custom period', async () => {
    vi.stubGlobal('fetch', setupFetchMock())
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    const customBtn = wrapper.findAll('.toolbar .btn').find(b => b.text() === '自定义')
    await customBtn.trigger('click')
    await flushPromises()

    expect(wrapper.findAll('input[type="date"]').length).toBe(2)
  })

  // ── Custom period summary fix (Fix 4) ──

  it('does not send period=day for custom mode summary', async () => {
    const fetchMock = setupFetchMock()
    vi.stubGlobal('fetch', fetchMock)
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    const customBtn = wrapper.findAll('.toolbar .btn').find(b => b.text() === '自定义')
    await customBtn.trigger('click')
    await flushPromises()
    await flushPromises()

    const summaryCalls = fetchMock.mock.calls.filter(c => String(c[0]).includes('/api/trades/summary'))
    const lastSummaryUrl = String(summaryCalls.at(-1)?.[0])
    expect(lastSummaryUrl).not.toContain('period=day')
  })

  // ── Trade modal open/close ──

  it('opens quick entry modal', async () => {
    vi.stubGlobal('fetch', setupFetchMock())
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    await wrapper.find('.toolbar-actions .btn-primary').trigger('click')
    await flushPromises()

    expect(wrapper.find('.trade-modal').exists()).toBe(true)
    expect(wrapper.text()).toContain('快速录入')
  })

  it('auto-fills quick entry name for bare 000001 using the best stock match', async () => {
    const { wrapper, fetchMock } = await mountQuickEntryWithStockSearch([
      { Symbol: 'sh000001', Code: '000001', Name: '上证指数', Market: 'sh' },
      { Symbol: 'sz000001', Code: '000001', Name: '平安银行', Market: 'sz' }
    ])

    await fillSymbolAndBlur(wrapper, '000001')

    expect(fetchMock.mock.calls.some(call => String(call[0]) === '/api/stocks/search?q=000001')).toBe(true)
    expect(getTradeField(wrapper, 'trade-name').element.value).toBe('平安银行')
  })

  it('auto-fills quick entry name for prefixed sz000001', async () => {
    const { wrapper } = await mountQuickEntryWithStockSearch([
      { Symbol: 'sh000001', Code: '000001', Name: '上证指数', Market: 'sh' },
      { Symbol: 'sz000001', Code: '000001', Name: '平安银行', Market: 'sz' }
    ])

    await fillSymbolAndBlur(wrapper, 'sz000001')

    expect(getTradeField(wrapper, 'trade-name').element.value).toBe('平安银行')
  })

  it('auto-fills quick entry name for 600519', async () => {
    const { wrapper } = await mountQuickEntryWithStockSearch([
      { Symbol: 'sh600519', Code: '600519', Name: '贵州茅台', Market: 'sh' }
    ])

    await fillSymbolAndBlur(wrapper, '600519')

    expect(getTradeField(wrapper, 'trade-name').element.value).toBe('贵州茅台')
  })

  it.each([
    {
      symbol: '000001',
      results: [
        { Symbol: 'sh000001', Code: '000001', Name: '上证指数', Market: 'sh' },
        { Symbol: 'sz000001', Code: '000001', Name: '平安银行', Market: 'sz' }
      ],
      expectedName: '平安银行'
    },
    {
      symbol: 'sz000001',
      results: [
        { Symbol: 'sh000001', Code: '000001', Name: '上证指数', Market: 'sh' },
        { Symbol: 'sz000001', Code: '000001', Name: '平安银行', Market: 'sz' }
      ],
      expectedName: '平安银行'
    },
    {
      symbol: '600519',
      results: [
        { Symbol: 'sh600519', Code: '600519', Name: '贵州茅台', Market: 'sh' }
      ],
      expectedName: '贵州茅台'
    }
  ])('auto-fills quick entry name for $symbol as soon as input search resolves', async ({ symbol, results, expectedName }) => {
    const { wrapper } = await mountQuickEntryWithStockSearch(results)

    await fillSymbolAndWaitForSearch(wrapper, symbol)

    expect(getTradeField(wrapper, 'trade-name').element.value).toBe(expectedName)
    expect(wrapper.find('.search-dropdown').exists()).toBe(false)
  })

  it('updates a previously auto-filled quick entry name when the symbol changes', async () => {
    const { wrapper } = await mountQuickEntryWithStockSearchByQuery({
      '000001': [
        { Symbol: 'sh000001', Code: '000001', Name: '上证指数', Market: 'sh' },
        { Symbol: 'sz000001', Code: '000001', Name: '平安银行', Market: 'sz' }
      ],
      '600519': [
        { Symbol: 'sh600519', Code: '600519', Name: '贵州茅台', Market: 'sh' }
      ]
    })

    await fillSymbolAndWaitForSearch(wrapper, '000001')
    expect(getTradeField(wrapper, 'trade-name').element.value).toBe('平安银行')

    await fillSymbolAndWaitForSearch(wrapper, '600519')

    expect(getTradeField(wrapper, 'trade-name').element.value).toBe('贵州茅台')
    expect(wrapper.find('.search-dropdown').exists()).toBe(false)
  })

  it('does not overwrite a manually edited quick entry name during input auto-fill', async () => {
    const { wrapper } = await mountQuickEntryWithStockSearch([
      { Symbol: 'sh600519', Code: '600519', Name: '贵州茅台', Market: 'sh' }
    ])

    await getTradeField(wrapper, 'trade-name').setValue('手工名称')
    await fillSymbolAndWaitForSearch(wrapper, '600519')

    expect(getTradeField(wrapper, 'trade-name').element.value).toBe('手工名称')
  })

  it('ignores stale quick entry stock search results after the symbol changes', async () => {
    let resolveFirstSearch
    const baseFetchMock = setupFetchMock()
    const fetchMock = vi.fn(async (url, opts) => {
      const requestUrl = String(url)
      if (requestUrl.startsWith('/api/stocks/search?')) {
        const query = new URL(requestUrl, 'http://local.test').searchParams.get('q') || ''
        if (query === '000001') {
          return new Promise(resolve => {
            resolveFirstSearch = () => resolve(makeResponse({
              json: async () => [
                { Symbol: 'sh000001', Code: '000001', Name: '上证指数', Market: 'sh' },
                { Symbol: 'sz000001', Code: '000001', Name: '平安银行', Market: 'sz' }
              ]
            }))
          })
        }
        if (query === '600519') {
          return makeResponse({
            json: async () => [
              { Symbol: 'sh600519', Code: '600519', Name: '贵州茅台', Market: 'sh' }
            ]
          })
        }
      }
      return baseFetchMock(url, opts)
    })
    vi.stubGlobal('fetch', fetchMock)
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    await wrapper.find('.toolbar-actions .btn-primary').trigger('click')
    await flushPromises()

    await getTradeField(wrapper, 'trade-symbol').setValue('000001')
    await waitForStockSearch()
    expect(resolveFirstSearch).toBeTypeOf('function')

    await fillSymbolAndWaitForSearch(wrapper, '600519')
    expect(getTradeField(wrapper, 'trade-name').element.value).toBe('贵州茅台')

    resolveFirstSearch()
    await flushPromises()
    await flushPromises()

    expect(getTradeField(wrapper, 'trade-symbol').element.value).toBe('600519')
    expect(getTradeField(wrapper, 'trade-name').element.value).toBe('贵州茅台')
    expect(wrapper.find('.search-dropdown').exists()).toBe(false)
  })

  it('closes modal on cancel', async () => {
    vi.stubGlobal('fetch', setupFetchMock())
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    await wrapper.find('.toolbar-actions .btn-primary').trigger('click')
    await flushPromises()

    const cancelBtn = wrapper.find('.trade-modal-actions .btn-secondary')
    await cancelBtn.trigger('click')
    await flushPromises()

    expect(wrapper.find('.trade-modal').exists()).toBe(false)
  })

  it('opens plan execution modal with plan context from stock info navigation', async () => {
    const fetchMock = setupFetchMock({
      extra: {
        '/api/stocks/plans/7/execution-context': makeResponse({ json: async () => defaultPlanExecutionContext })
      }
    })
    vi.stubGlobal('fetch', fetchMock)
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    window.dispatchEvent(new CustomEvent('navigate-trade-log', {
      detail: {
        plan: {
          id: 7,
          symbol: '000001',
          name: '平安银行',
          direction: 'Long'
        }
      }
    }))
    await flushPromises()
    await flushPromises()

    expect(wrapper.find('.trade-modal').exists()).toBe(true)
    expect(wrapper.text()).toContain('录入执行')
    expect(wrapper.text()).toContain('预案来源 / 场景')
    expect(wrapper.text()).toContain('主场景')
    expect(wrapper.text()).toContain('当前持仓快照')
    expect(wrapper.text()).toContain('平安银行')
  })

  it('keeps detailed feedback collapsed by default and keeps key quick-capture fields before detailed feedback', async () => {
    const fetchMock = setupFetchMock({
      extra: {
        '/api/stocks/plans/7/execution-context': makeResponse({ json: async () => defaultPlanExecutionContext })
      }
    })
    vi.stubGlobal('fetch', fetchMock)
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    window.dispatchEvent(new CustomEvent('navigate-trade-log', {
      detail: {
        plan: { id: 7, symbol: '000001', name: '平安银行', direction: 'Long' }
      }
    }))
    await flushPromises()
    await flushPromises()

    expect(wrapper.find('[data-testid="trade-detail-panel"]').exists()).toBe(false)
    expect(getTradeField(wrapper, 'trade-detail-toggle').attributes('aria-expanded')).toBe('false')

    const formHtml = wrapper.find('.trade-form').html()
    expect(formHtml).not.toContain('需要复盘（强字段）')
    expect(formHtml.indexOf('偏差标签')).toBeGreaterThan(-1)
    expect(formHtml.indexOf('偏差标签')).toBeLessThan(formHtml.indexOf('详细反馈 / 更多说明'))

    await getTradeField(wrapper, 'trade-detail-toggle').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="trade-detail-panel"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('偏差说明')
    expect(wrapper.text()).toContain('放弃原因')
    expect(wrapper.text()).toContain('备注')
  })

  it('consumes pending trade-log navigation on first mount', async () => {
    const fetchMock = setupFetchMock({
      extra: {
        '/api/stocks/plans/7/execution-context': makeResponse({ json: async () => defaultPlanExecutionContext })
      }
    })
    vi.stubGlobal('fetch', fetchMock)
    window.__pendingNavigateTradeLog = {
      plan: {
        id: 7,
        symbol: '000001',
        name: '平安银行',
        direction: 'Long'
      }
    }

    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    expect(wrapper.find('.trade-modal').exists()).toBe(true)
    expect(wrapper.text()).toContain('录入执行')
    expect(window.__pendingNavigateTradeLog).toBeUndefined()
  })

  it('shows loading placeholders instead of misleading fallback while execution context is loading', async () => {
    const fetchMock = vi.fn(async (url) => {
      if (url.startsWith('/api/stocks/plans/7/execution-context')) {
        return new Promise(() => {})
      }
      if (url.startsWith('/api/portfolio/snapshot')) return makeResponse({ json: async () => defaultSnapshot })
      if (url.startsWith('/api/trades/summary')) return makeResponse({ json: async () => defaultSummary })
      if (url.startsWith('/api/trades')) return makeResponse({ json: async () => defaultTrades })
      if (url.startsWith('/api/portfolio/exposure')) return makeResponse({ json: async () => ({ combinedExposure: 0.5, totalExposure: 0.4, pendingExposure: 0.1, symbolExposures: [] }) })
      if (url.startsWith('/api/trades/behavior-stats')) return makeResponse({ json: async () => ({ disciplineScore: 80, planExecutionRate: 0.8, currentLossStreak: 0, activeAlerts: [] }) })
      if (url.startsWith('/api/trades/reviews')) return makeResponse({ json: async () => [] })
      return makeResponse()
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    window.dispatchEvent(new CustomEvent('navigate-trade-log', {
      detail: {
        plan: { id: 7, symbol: '000001', name: '平安银行', direction: 'Long' }
      }
    }))
    await flushPromises()

    expect(wrapper.text()).toContain('正在加载预案执行上下文，请稍候...')
    expect(wrapper.text()).toContain('正在加载场景状态...')
    expect(wrapper.text()).toContain('正在加载持仓快照...')
    expect(wrapper.text()).not.toContain('待观察')
    expect(wrapper.text()).not.toContain('暂无场景状态')
    expect(wrapper.text()).not.toContain('当前暂无持仓快照')
  })

  it('distinguishes empty position from loading when execution context has no holding snapshot', async () => {
    const fetchMock = setupFetchMock({
      extra: {
        '/api/stocks/plans/7/execution-context': makeResponse({
          json: async () => ({
            ...defaultPlanExecutionContext,
            currentPositionSnapshot: null
          })
        })
      }
    })
    vi.stubGlobal('fetch', fetchMock)
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    window.dispatchEvent(new CustomEvent('navigate-trade-log', {
      detail: {
        plan: { id: 7, symbol: '000001', name: '平安银行', direction: 'Long' }
      }
    }))
    await flushPromises()
    await flushPromises()

    expect(wrapper.text()).toContain('当前无持仓')
    expect(wrapper.text()).not.toContain('当前暂无持仓快照')
  })

  it('submits deviation tags when saving execution from plan context', async () => {
    const fetchMock = vi.fn(async (url, opts) => {
      if (url.startsWith('/api/stocks/plans/7/execution-context')) {
        return makeResponse({ json: async () => defaultPlanExecutionContext })
      }
      if (url.startsWith('/api/portfolio/snapshot')) return makeResponse({ json: async () => defaultSnapshot })
      if (url.startsWith('/api/trades/summary')) return makeResponse({ json: async () => defaultSummary })
      if (url === '/api/trades' && opts?.method === 'POST') return makeResponse({ json: async () => ({ id: 9, symbol: '000001', planId: 7 }) })
      if (url.startsWith('/api/trades')) return makeResponse({ json: async () => defaultTrades })
      if (url.startsWith('/api/portfolio/exposure')) return makeResponse({ json: async () => ({ combinedExposure: 0.5, totalExposure: 0.4, pendingExposure: 0.1, symbolExposures: [] }) })
      if (url.startsWith('/api/trades/behavior-stats')) return makeResponse({ json: async () => ({ disciplineScore: 80, planExecutionRate: 0.8, currentLossStreak: 0, activeAlerts: [] }) })
      if (url.startsWith('/api/trades/reviews')) return makeResponse({ json: async () => [] })
      return makeResponse()
    })
    vi.stubGlobal('fetch', fetchMock)
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    window.dispatchEvent(new CustomEvent('navigate-trade-log', {
      detail: {
        plan: { id: 7, symbol: '000001', name: '平安银行', direction: 'Long' }
      }
    }))
    await flushPromises()
    await flushPromises()

    await getTradeField(wrapper, 'trade-executed-price').setValue('10.58')
    await getTradeField(wrapper, 'trade-quantity').setValue('1000')
    await getTradeField(wrapper, 'trade-executed-at').setValue('2026-04-03T09:40')

    const tagCheckbox = wrapper.findAll('.tag-selector-item input').find(input => input.element.value === '追价')
    await tagCheckbox.setValue(true)
    await getTradeField(wrapper, 'trade-execution-action').setValue('加仓执行')

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    const postCalls = fetchMock.mock.calls.filter(call => call[1]?.method === 'POST' && call[0] === '/api/trades')
    expect(postCalls.length).toBe(1)
    const body = JSON.parse(postCalls[0][1].body)
    expect(body.planId).toBe(7)
    expect(body.executionAction).toBe('加仓执行')
    expect(body.deviationTags).toContain('追价')
  })

  it('finishes save flow and closes modal even if post-save context refresh hangs', async () => {
    let executionContextCallCount = 0
    const fetchMock = vi.fn(async (url, opts) => {
      if (url.startsWith('/api/stocks/plans/7/execution-context')) {
        executionContextCallCount += 1
        if (executionContextCallCount === 1) {
          return makeResponse({ json: async () => defaultPlanExecutionContext })
        }
        return new Promise(() => {})
      }
      if (url.startsWith('/api/portfolio/snapshot')) return makeResponse({ json: async () => defaultSnapshot })
      if (url.startsWith('/api/trades/summary')) return makeResponse({ json: async () => defaultSummary })
      if (url === '/api/trades' && opts?.method === 'POST') return makeResponse({ json: async () => ({ id: 9, symbol: '000001', planId: 7 }) })
      if (url.startsWith('/api/trades')) return makeResponse({ json: async () => defaultTrades })
      if (url.startsWith('/api/portfolio/exposure')) return makeResponse({ json: async () => ({ combinedExposure: 0.5, totalExposure: 0.4, pendingExposure: 0.1, symbolExposures: [] }) })
      if (url.startsWith('/api/trades/behavior-stats')) return makeResponse({ json: async () => ({ disciplineScore: 80, planExecutionRate: 0.8, currentLossStreak: 0, activeAlerts: [] }) })
      if (url.startsWith('/api/trades/reviews')) return makeResponse({ json: async () => [] })
      return makeResponse()
    })
    vi.stubGlobal('fetch', fetchMock)
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    window.dispatchEvent(new CustomEvent('navigate-trade-log', {
      detail: {
        plan: { id: 7, symbol: '000001', name: '平安银行', direction: 'Long' }
      }
    }))
    await flushPromises()
    await flushPromises()

    await getTradeField(wrapper, 'trade-executed-price').setValue('10.58')
    await getTradeField(wrapper, 'trade-quantity').setValue('1000')
    await getTradeField(wrapper, 'trade-executed-at').setValue('2026-04-03T09:40')

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.find('.trade-modal').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('保存中...')
  })

  it('filters feedback workspace list by deviation and unplanned status', async () => {
    const mixedTrades = [
      { ...defaultTrades[0], id: 1, symbol: '000001', complianceTag: 'FollowedPlan', deviationTags: [] },
      { ...defaultTrades[0], id: 2, symbol: '000002', complianceTag: 'DeviatedFromPlan', deviationTags: ['追价'] },
      { ...defaultTrades[0], id: 3, symbol: '000003', complianceTag: 'Unplanned', deviationTags: ['无计划交易'] }
    ]
    vi.stubGlobal('fetch', setupFetchMock({ trades: mixedTrades }))
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    expect(wrapper.findAll('.trade-list .trade-item')).toHaveLength(3)

    const deviatedBtn = wrapper.findAll('.trade-filter-strip .btn').find(btn => btn.text() === '仅看偏离')
    await deviatedBtn.trigger('click')
    await flushPromises()
    expect(wrapper.findAll('.trade-list .trade-item')).toHaveLength(1)
    expect(wrapper.find('.trade-list .trade-item').text()).toContain('000002')

    const unplannedBtn = wrapper.findAll('.trade-filter-strip .btn').find(btn => btn.text() === '仅看无计划')
    await unplannedBtn.trigger('click')
    await flushPromises()
    expect(wrapper.findAll('.trade-list .trade-item')).toHaveLength(1)
    expect(wrapper.find('.trade-list .trade-item').text()).toContain('000003')
  })

  // ── Form validation (Fix 2) ──

  it('validates empty symbol', async () => {
    vi.stubGlobal('fetch', setupFetchMock())
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    await wrapper.find('.toolbar-actions .btn-primary').trigger('click')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('请输入股票代码')
  })

  it('validates invalid price', async () => {
    vi.stubGlobal('fetch', setupFetchMock())
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    await wrapper.find('.toolbar-actions .btn-primary').trigger('click')
    await flushPromises()

    await getTradeField(wrapper, 'trade-symbol').setValue('000001')
    await getTradeField(wrapper, 'trade-name').setValue('测试')

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('请输入有效的成交价')
  })

  it('validates missing time', async () => {
    vi.stubGlobal('fetch', setupFetchMock())
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    await wrapper.find('.toolbar-actions .btn-primary').trigger('click')
    await flushPromises()

    await getTradeField(wrapper, 'trade-symbol').setValue('000001')
    await getTradeField(wrapper, 'trade-name').setValue('测试')
    await getTradeField(wrapper, 'trade-executed-price').setValue('10.50')
    await getTradeField(wrapper, 'trade-quantity').setValue('1000')

    // Clear the auto-filled time so the missing-time validation triggers
    const timeInput = getTradeField(wrapper, 'trade-executed-at')
    await timeInput.setValue('')

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('请选择成交时间')
  })

  // ── Delete confirm ──

  it('calls delete API on confirm', async () => {
    const fetchMock = setupFetchMock()
    vi.stubGlobal('fetch', fetchMock)
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    const deleteBtns = wrapper.findAll('.trade-item-actions .btn')
    const deleteBtn = deleteBtns.find(b => b.text() === '删除')
    expect(deleteBtn.exists()).toBe(true)
    await deleteBtn.trigger('click')
    await flushPromises()

    expect(mockConfirm).toHaveBeenCalledWith({ message: '确定删除此交易记录？' })
    const deleteCalls = fetchMock.mock.calls.filter(c => c[1]?.method === 'DELETE')
    expect(deleteCalls.length).toBe(1)
    expect(String(deleteCalls[0][0])).toContain('/api/trades/1')
  })

  it('sends reset-all confirmation text after both UI confirmations', async () => {
    const fetchMock = setupFetchMock({
      extra: {
        '/api/trades/reset-all': makeResponse({
          json: async () => ({ success: true, deletedTradeCount: 3, deletedPositionCount: 2, deletedReviewCount: 1 })
        })
      }
    })
    vi.stubGlobal('fetch', fetchMock)
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    const resetBtn = wrapper.findAll('.toolbar-actions .btn').find(btn => btn.text().includes('重置'))
    expect(resetBtn.exists()).toBe(true)
    await resetBtn.trigger('click')
    await flushPromises()
    await flushPromises()

    expect(mockConfirm).toHaveBeenCalledTimes(2)
    const resetCalls = fetchMock.mock.calls.filter(call => call[0] === '/api/trades/reset-all' && call[1]?.method === 'POST')
    expect(resetCalls.length).toBe(1)
    expect(JSON.parse(resetCalls[0][1].body)).toEqual({ confirmText: 'RESET_ALL_TRADES' })
  })

  // ── Settings modal ──

  it('opens settings modal', async () => {
    vi.stubGlobal('fetch', setupFetchMock())
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    await wrapper.find('.toolbar-actions .btn-secondary').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('设置本金')
    expect(wrapper.text()).toContain('总本金（元）')
  })

  // ── Error display (Fix 5) ──

  it('shows error when delete fails', async () => {
    const fetchMock = vi.fn(async (url, opts) => {
      if (opts?.method === 'DELETE') throw new Error('Network error')
      const key = ['/api/portfolio/snapshot', '/api/trades/summary', '/api/trades'].find(k => url.startsWith(k))
      if (key === '/api/portfolio/snapshot') return makeResponse({ json: async () => defaultSnapshot })
      if (key === '/api/trades/summary') return makeResponse({ json: async () => defaultSummary })
      if (key === '/api/trades') return makeResponse({ json: async () => defaultTrades })
      return makeResponse()
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    const deleteBtns = wrapper.findAll('.trade-item-actions .btn')
    await deleteBtns.find(b => b.text() === '删除').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('删除交易记录失败')
  })

  it('shows error when snapshot fails', async () => {
    const fetchMock = vi.fn(async (url) => {
      if (url.startsWith('/api/portfolio/snapshot')) throw new Error('fail')
      if (url.startsWith('/api/trades/summary')) return makeResponse({ json: async () => defaultSummary })
      if (url.startsWith('/api/trades')) return makeResponse({ json: async () => defaultTrades })
      return makeResponse()
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    expect(wrapper.text()).toContain('加载持仓信息失败')
  })

  it('shows error when summary fails', async () => {
    const fetchMock = vi.fn(async (url) => {
      if (url.startsWith('/api/portfolio/snapshot')) return makeResponse({ json: async () => defaultSnapshot })
      if (url.startsWith('/api/trades/summary')) throw new Error('fail')
      if (url.startsWith('/api/trades')) return makeResponse({ json: async () => defaultTrades })
      return makeResponse()
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    expect(wrapper.text()).toContain('加载汇总数据失败')
  })

  // ── Unified fetch (Fix 1) ──

  it('uses POST with json body for saveTrade', async () => {
    const fetchMock = vi.fn(async (url, opts) => {
      if (url.startsWith('/api/portfolio/snapshot')) return makeResponse({ json: async () => defaultSnapshot })
      if (url.startsWith('/api/trades/summary')) return makeResponse({ json: async () => defaultSummary })
      if (url === '/api/trades' && opts?.method === 'POST') return makeResponse()
      if (url.startsWith('/api/trades')) return makeResponse({ json: async () => defaultTrades })
      return makeResponse()
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    await wrapper.find('.toolbar-actions .btn-primary').trigger('click')
    await flushPromises()

    await getTradeField(wrapper, 'trade-symbol').setValue('000001')
    await getTradeField(wrapper, 'trade-name').setValue('测试')
    await getTradeField(wrapper, 'trade-executed-price').setValue('10.50')
    await getTradeField(wrapper, 'trade-quantity').setValue('1000')
    await getTradeField(wrapper, 'trade-executed-at').setValue('2026-04-03T09:30')

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    const postCalls = fetchMock.mock.calls.filter(c => c[1]?.method === 'POST')
    expect(postCalls.length).toBe(1)
    expect(postCalls[0][1].headers['Content-Type']).toBe('application/json')
    const body = JSON.parse(postCalls[0][1].body)
    expect(body.symbol).toBe('000001')
    expect(body.executedPrice).toBe(10.5)
    expect(body.quantity).toBe(1000)
  })

  it('uses PUT with json body for saveSettings', async () => {
    const fetchMock = vi.fn(async (url, opts) => {
      if (url.startsWith('/api/portfolio/snapshot')) return makeResponse({ json: async () => defaultSnapshot })
      if (url.startsWith('/api/trades/summary')) return makeResponse({ json: async () => defaultSummary })
      if (url.startsWith('/api/portfolio/settings')) return makeResponse()
      if (url.startsWith('/api/trades')) return makeResponse({ json: async () => defaultTrades })
      return makeResponse()
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    await wrapper.find('.toolbar-actions .btn-secondary').trigger('click')
    await flushPromises()

    const settingsInput = wrapper.findAll('.trade-modal input').at(-1)
    await settingsInput.setValue('200000')

    const forms = wrapper.findAll('form')
    await forms.at(-1).trigger('submit')
    await flushPromises()

    const putCalls = fetchMock.mock.calls.filter(c => c[1]?.method === 'PUT')
    expect(putCalls.length).toBe(1)
    expect(putCalls[0][1].headers['Content-Type']).toBe('application/json')
    const body = JSON.parse(putCalls[0][1].body)
    expect(body.totalCapital).toBe(200000)
  })

  // ── Review (复盘) feature ──

  it('renders review dropdown menu', async () => {
    vi.stubGlobal('fetch', setupFetchMock())
    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    const reviewBtn = wrapper.findAll('.btn').find(b => b.text().includes('生成复盘总结'))
    expect(reviewBtn).toBeTruthy()

    await reviewBtn.trigger('click')
    await flushPromises()

    expect(wrapper.find('.review-menu').exists()).toBe(true)
    expect(wrapper.text()).toContain('今日复盘')
    expect(wrapper.text()).toContain('本周复盘')
    expect(wrapper.text()).toContain('本月复盘')
    expect(wrapper.text()).toContain('自定义时段')
  })

  it('triggers POST /api/trades/reviews/generate on 今日复盘', async () => {
    const reviewResult = {
      id: 1,
      reviewType: 'Daily',
      periodStart: '2026-04-03T00:00:00',
      periodEnd: '2026-04-03T15:00:00',
      tradeCount: 3,
      totalPnL: 500,
      winRate: 0.67,
      complianceRate: 0.8,
      reviewContent: '### 复盘内容\n\n测试内容',
      createdAt: '2026-04-03T16:00:00'
    }
    const fetchMock = vi.fn(async (url, opts) => {
      if (url.startsWith('/api/trades/reviews/generate') && opts?.method === 'POST')
        return makeResponse({ json: async () => reviewResult })
      if (url.startsWith('/api/trades/reviews'))
        return makeResponse({ json: async () => [reviewResult] })
      if (url.startsWith('/api/portfolio/snapshot'))
        return makeResponse({ json: async () => defaultSnapshot })
      if (url.startsWith('/api/trades/summary'))
        return makeResponse({ json: async () => defaultSummary })
      if (url.startsWith('/api/trades'))
        return makeResponse({ json: async () => defaultTrades })
      return makeResponse()
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    // Open menu
    const reviewBtn = wrapper.findAll('.btn').find(b => b.text().includes('生成复盘总结'))
    await reviewBtn.trigger('click')
    await flushPromises()

    // Click 今日复盘
    const dailyBtn = wrapper.findAll('.review-menu-item').find(b => b.text() === '今日复盘')
    await dailyBtn.trigger('click')
    await flushPromises()
    await flushPromises()

    const generateCalls = fetchMock.mock.calls.filter(c =>
      String(c[0]).includes('/api/trades/reviews/generate') && c[1]?.method === 'POST'
    )
    expect(generateCalls.length).toBe(1)
    const body = JSON.parse(generateCalls[0][1].body)
    expect(body.type).toBe('daily')
  })

  it('shows spinner while generating review', async () => {
    const fetchMock = vi.fn(async (url, opts) => {
      if (url.startsWith('/api/trades/reviews/generate'))
        return new Promise(() => {}) // never resolves
      if (url.startsWith('/api/portfolio/snapshot'))
        return makeResponse({ json: async () => defaultSnapshot })
      if (url.startsWith('/api/trades/summary'))
        return makeResponse({ json: async () => defaultSummary })
      if (url.startsWith('/api/trades'))
        return makeResponse({ json: async () => defaultTrades })
      return makeResponse()
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    const reviewBtn = wrapper.findAll('.btn').find(b => b.text().includes('生成复盘总结'))
    await reviewBtn.trigger('click')
    await flushPromises()

    const dailyBtn = wrapper.findAll('.review-menu-item').find(b => b.text() === '今日复盘')
    await dailyBtn.trigger('click')
    await flushPromises()

    expect(wrapper.find('.review-spinner').exists()).toBe(true)
    expect(wrapper.text()).toContain('AI 正在生成复盘总结')
  })

  it('renders review content with markdown', async () => {
    const reviewResult = {
      id: 1,
      reviewType: 'Daily',
      periodStart: '2026-04-03T00:00:00',
      periodEnd: '2026-04-03T15:00:00',
      tradeCount: 2,
      totalPnL: 300,
      winRate: 0.5,
      complianceRate: 1.0,
      reviewContent: '### 市场环境\n\n今天市场**震荡**，主线板块为新能源。\n\n- 要点一\n- 要点二',
      createdAt: '2026-04-03T16:00:00'
    }
    const fetchMock = vi.fn(async (url, opts) => {
      if (url.startsWith('/api/trades/reviews/generate') && opts?.method === 'POST')
        return makeResponse({ json: async () => reviewResult })
      if (url.startsWith('/api/trades/reviews'))
        return makeResponse({ json: async () => [reviewResult] })
      if (url.startsWith('/api/portfolio/snapshot'))
        return makeResponse({ json: async () => defaultSnapshot })
      if (url.startsWith('/api/trades/summary'))
        return makeResponse({ json: async () => defaultSummary })
      if (url.startsWith('/api/trades'))
        return makeResponse({ json: async () => defaultTrades })
      return makeResponse()
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    const reviewBtn = wrapper.findAll('.btn').find(b => b.text().includes('生成复盘总结'))
    await reviewBtn.trigger('click')
    await flushPromises()
    const dailyBtn = wrapper.findAll('.review-menu-item').find(b => b.text() === '今日复盘')
    await dailyBtn.trigger('click')
    await flushPromises()
    await flushPromises()

    expect(wrapper.find('.review-panel').exists()).toBe(true)
    expect(wrapper.find('.review-body').exists()).toBe(true)
    // The markdown content should be rendered into the review body
    const reviewBody = wrapper.find('.review-body')
    expect(reviewBody.text()).toContain('市场环境')
    expect(reviewBody.text()).toContain('震荡')
    expect(reviewBody.text()).toContain('要点一')
  })

  it('loads review history list', async () => {
    const reviewItems = [
      { id: 1, reviewType: 'Daily', periodStart: '2026-04-03T00:00:00', totalPnL: 500, winRate: 0.67 },
      { id: 2, reviewType: 'Weekly', periodStart: '2026-03-31T00:00:00', totalPnL: -200, winRate: 0.4 }
    ]
    const fetchMock = vi.fn(async (url, opts) => {
      if (url.startsWith('/api/trades/reviews/generate') && opts?.method === 'POST')
        return makeResponse({ json: async () => ({ ...reviewItems[0], reviewContent: '测试', tradeCount: 1, complianceRate: 1, createdAt: '2026-04-03T16:00:00' }) })
      if (url.startsWith('/api/trades/reviews'))
        return makeResponse({ json: async () => reviewItems })
      if (url.startsWith('/api/portfolio/snapshot'))
        return makeResponse({ json: async () => defaultSnapshot })
      if (url.startsWith('/api/trades/summary'))
        return makeResponse({ json: async () => defaultSummary })
      if (url.startsWith('/api/trades'))
        return makeResponse({ json: async () => defaultTrades })
      return makeResponse()
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    // Trigger generate to also load review list
    const reviewBtn = wrapper.findAll('.btn').find(b => b.text().includes('生成复盘总结'))
    await reviewBtn.trigger('click')
    await flushPromises()
    const dailyBtn = wrapper.findAll('.review-menu-item').find(b => b.text() === '今日复盘')
    await dailyBtn.trigger('click')
    await flushPromises()
    await flushPromises()
    await flushPromises()

    expect(wrapper.find('.review-history').exists()).toBe(true)
    expect(wrapper.text()).toContain('复盘历史')
  })

  it('shows error when review generation fails', async () => {
    const fetchMock = vi.fn(async (url, opts) => {
      if (url.startsWith('/api/trades/reviews/generate') && opts?.method === 'POST')
        return makeResponse({ ok: false, text: async () => '生成复盘失败' })
      if (url.startsWith('/api/portfolio/snapshot'))
        return makeResponse({ json: async () => defaultSnapshot })
      if (url.startsWith('/api/trades/summary'))
        return makeResponse({ json: async () => defaultSummary })
      if (url.startsWith('/api/trades'))
        return makeResponse({ json: async () => defaultTrades })
      return makeResponse()
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    const reviewBtn = wrapper.findAll('.btn').find(b => b.text().includes('生成复盘总结'))
    await reviewBtn.trigger('click')
    await flushPromises()
    const dailyBtn = wrapper.findAll('.review-menu-item').find(b => b.text() === '今日复盘')
    await dailyBtn.trigger('click')
    await flushPromises()
    await flushPromises()

    expect(wrapper.find('.review-panel').exists()).toBe(true)
    expect(wrapper.text()).toContain('生成复盘失败')
  })

  // ── XSS sanitization via markdownToSafeHtml (rendered through review panel) ──

  it('strips <script> tags from review content', async () => {
    const xssReview = {
      id: 1, reviewType: 'Daily', periodStart: '2026-04-03T00:00:00', periodEnd: '2026-04-03T15:00:00',
      tradeCount: 1, totalPnL: 0, winRate: 0, complianceRate: 0,
      reviewContent: '正常内容<script>alert("xss")</script>结尾',
      createdAt: '2026-04-03T16:00:00'
    }
    const fetchMock = vi.fn(async (url, opts) => {
      if (url.startsWith('/api/trades/reviews/generate') && opts?.method === 'POST')
        return makeResponse({ json: async () => xssReview })
      if (url.startsWith('/api/trades/reviews'))
        return makeResponse({ json: async () => [] })
      if (url.startsWith('/api/portfolio/snapshot'))
        return makeResponse({ json: async () => defaultSnapshot })
      if (url.startsWith('/api/trades/summary'))
        return makeResponse({ json: async () => defaultSummary })
      if (url.startsWith('/api/trades'))
        return makeResponse({ json: async () => defaultTrades })
      return makeResponse()
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    const reviewBtn = wrapper.findAll('.btn').find(b => b.text().includes('生成复盘总结'))
    await reviewBtn.trigger('click')
    await flushPromises()
    const dailyBtn = wrapper.findAll('.review-menu-item').find(b => b.text() === '今日复盘')
    await dailyBtn.trigger('click')
    await flushPromises()
    await flushPromises()

    const reviewBody = wrapper.find('.review-body')
    expect(reviewBody.html()).not.toContain('<script>')
    expect(reviewBody.text()).toContain('正常内容')
    expect(reviewBody.text()).toContain('结尾')
  })

  it('strips onerror handlers from review content', async () => {
    const xssReview = {
      id: 1, reviewType: 'Daily', periodStart: '2026-04-03T00:00:00', periodEnd: '2026-04-03T15:00:00',
      tradeCount: 1, totalPnL: 0, winRate: 0, complianceRate: 0,
      reviewContent: '前面<img src=x onerror="alert(\'xss\')">后面',
      createdAt: '2026-04-03T16:00:00'
    }
    const fetchMock = vi.fn(async (url, opts) => {
      if (url.startsWith('/api/trades/reviews/generate') && opts?.method === 'POST')
        return makeResponse({ json: async () => xssReview })
      if (url.startsWith('/api/trades/reviews'))
        return makeResponse({ json: async () => [] })
      if (url.startsWith('/api/portfolio/snapshot'))
        return makeResponse({ json: async () => defaultSnapshot })
      if (url.startsWith('/api/trades/summary'))
        return makeResponse({ json: async () => defaultSummary })
      if (url.startsWith('/api/trades'))
        return makeResponse({ json: async () => defaultTrades })
      return makeResponse()
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    const reviewBtn = wrapper.findAll('.btn').find(b => b.text().includes('生成复盘总结'))
    await reviewBtn.trigger('click')
    await flushPromises()
    const dailyBtn = wrapper.findAll('.review-menu-item').find(b => b.text() === '今日复盘')
    await dailyBtn.trigger('click')
    await flushPromises()
    await flushPromises()

    const reviewBody = wrapper.find('.review-body')
    expect(reviewBody.html()).not.toContain('onerror')
    expect(reviewBody.text()).toContain('前面')
    expect(reviewBody.text()).toContain('后面')
  })

  it('renders normal markdown correctly in review content', async () => {
    const mdReview = {
      id: 1, reviewType: 'Daily', periodStart: '2026-04-03T00:00:00', periodEnd: '2026-04-03T15:00:00',
      tradeCount: 1, totalPnL: 0, winRate: 0, complianceRate: 0,
      reviewContent: '# 标题\n\n- 列表项一\n- 列表项二\n\n**粗体文本**',
      createdAt: '2026-04-03T16:00:00'
    }
    const fetchMock = vi.fn(async (url, opts) => {
      if (url.startsWith('/api/trades/reviews/generate') && opts?.method === 'POST')
        return makeResponse({ json: async () => mdReview })
      if (url.startsWith('/api/trades/reviews'))
        return makeResponse({ json: async () => [] })
      if (url.startsWith('/api/portfolio/snapshot'))
        return makeResponse({ json: async () => defaultSnapshot })
      if (url.startsWith('/api/trades/summary'))
        return makeResponse({ json: async () => defaultSummary })
      if (url.startsWith('/api/trades'))
        return makeResponse({ json: async () => defaultTrades })
      return makeResponse()
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(TradeLogTab)
    await flushPromises()
    await flushPromises()

    const reviewBtn = wrapper.findAll('.btn').find(b => b.text().includes('生成复盘总结'))
    await reviewBtn.trigger('click')
    await flushPromises()
    const dailyBtn = wrapper.findAll('.review-menu-item').find(b => b.text() === '今日复盘')
    await dailyBtn.trigger('click')
    await flushPromises()
    await flushPromises()

    const reviewBody = wrapper.find('.review-body')
    expect(reviewBody.html()).toContain('<h1>')
    expect(reviewBody.html()).toContain('<li>')
    expect(reviewBody.html()).toContain('<strong>')
    expect(reviewBody.text()).toContain('标题')
    expect(reviewBody.text()).toContain('列表项一')
    expect(reviewBody.text()).toContain('粗体文本')
  })
})
