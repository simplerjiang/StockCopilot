import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import StockTopMarketOverview from './StockTopMarketOverview.vue'

const noop = () => ''
const identity = v => v
const baseProps = {
  enabled: true,
  loading: false,
  error: '',
  overview: {
    snapshotTime: '2026-04-01T14:30:00Z',
    mainCapitalFlow: { mainNetInflow: 12300000000, superLargeOrderNetInflow: 8100000000 },
    northboundFlow: { totalNetInflow: -5700000000, shanghaiNetInflow: -3200000000, shenzhenNetInflow: -2500000000 },
    breadth: { advancers: 2341, decliners: 1820, flatCount: 412, limitUpCount: 48, limitDownCount: 12 }
  },
  detail: null,
  currentStockRealtimeQuote: null,
  stockRealtimeDomesticIndices: [
    { symbol: 'sh000001', name: '上证指数', price: 3342.15, change: 41.23, changePercent: 1.23, turnoverAmount: 284700000000 },
    { symbol: 'sz399001', name: '深证成指', price: 10887.6, change: -49.1, changePercent: -0.45, turnoverAmount: 312000000000 },
    { symbol: 'sz399006', name: '创业板指', price: 2156.3, change: 18.9, changePercent: 0.88, turnoverAmount: 98000000000 }
  ],
  stockRealtimeGlobalIndices: [
    { symbol: 'hk_HSI', name: '恒生指数', price: 20115.3, change: 64.2, changePercent: 0.32, turnoverAmount: 0 }
  ],
  stockRealtimeRelativeStrength: null,
  stockRealtimeBreadthBias: null,
  formatDate: noop,
  getChangeClass: () => '',
  formatSignedNumber: v => String(v),
  formatSignedPercent: v => `${v}%`,
  formatSignedRealtimeAmount: v => `${v}亿`,
  formatRealtimeMoney: v => `${v}亿`
}

describe('StockTopMarketOverview', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-01T10:00:00+08:00'))
    localStorage.clear()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('renders domestic index cards', () => {
    const wrapper = mount(StockTopMarketOverview, { props: baseProps })
    expect(wrapper.text()).toContain('上证指数')
    expect(wrapper.text()).toContain('深证成指')
    expect(wrapper.text()).toContain('创业板指')
    expect(wrapper.text()).toContain('3342.15')
  })

  it('renders global index cards', () => {
    const wrapper = mount(StockTopMarketOverview, { props: baseProps })
    expect(wrapper.text()).toContain('恒生指数')
    expect(wrapper.text()).toContain('20115.30')
  })

  it('renders pulse chips with market data', () => {
    const wrapper = mount(StockTopMarketOverview, { props: baseProps })
    expect(wrapper.text()).toContain('主力')
    expect(wrapper.text()).toContain('北向')
    expect(wrapper.text()).toContain('广度')
    expect(wrapper.text()).toContain('封板')
    expect(wrapper.text()).toContain('2341')
    expect(wrapper.text()).toContain('1820')
  })

  it('shows hidden state when not enabled', () => {
    const wrapper = mount(StockTopMarketOverview, {
      props: { ...baseProps, enabled: false }
    })
    expect(wrapper.text()).toContain('市场总览已隐藏')
    expect(wrapper.text()).not.toContain('上证指数')
  })

  it('emits toggle when show button clicked in hidden state', async () => {
    const wrapper = mount(StockTopMarketOverview, {
      props: { ...baseProps, enabled: false }
    })
    await wrapper.find('.expand-toggle').trigger('click')
    expect(wrapper.emitted('toggle')).toHaveLength(1)
  })

  it('emits open-chart when index card is clicked', async () => {
    const wrapper = mount(StockTopMarketOverview, { props: baseProps })
    const cards = wrapper.findAll('.idx-card')
    expect(cards.length).toBeGreaterThanOrEqual(3)
    await cards[0].trigger('click')
    expect(wrapper.emitted('open-chart')).toHaveLength(1)
    const payload = wrapper.emitted('open-chart')[0][0]
    expect(payload.symbol).toBe('sh000001')
    expect(payload.name).toBe('上证指数')
    expect(payload.icon).toBe('📈')
  })

  it('emits open-chart with globe icon for global indices', async () => {
    const wrapper = mount(StockTopMarketOverview, { props: baseProps })
    const globalCards = wrapper.findAll('.idx-row')[1].findAll('.idx-card')
    await globalCards[0].trigger('click')
    const payload = wrapper.emitted('open-chart')[0][0]
    expect(payload.symbol).toBe('hk_HSI')
    expect(payload.icon).toBe('🌏')
  })

  it('auto-refresh countdown emits refresh at zero', () => {
    const wrapper = mount(StockTopMarketOverview, { props: baseProps })
    expect(wrapper.emitted('refresh')).toBeUndefined()
    vi.advanceTimersByTime(30000)
    expect(wrapper.emitted('refresh')).toHaveLength(1)
  })

  it('manual refresh resets countdown and emits refresh', async () => {
    const wrapper = mount(StockTopMarketOverview, { props: baseProps })
    vi.advanceTimersByTime(10000)
    await wrapper.find('.refresh-indicator').trigger('click')
    expect(wrapper.emitted('refresh')).toHaveLength(1)
    vi.advanceTimersByTime(29000)
    expect(wrapper.emitted('refresh')).toHaveLength(1)
    vi.advanceTimersByTime(1000)
    expect(wrapper.emitted('refresh')).toHaveLength(2)
  })

  it('shows loading state', () => {
    const wrapper = mount(StockTopMarketOverview, {
      props: { ...baseProps, loading: true, overview: null }
    })
    expect(wrapper.text()).toContain('加载市场数据中')
  })

  it('shows error state', () => {
    const wrapper = mount(StockTopMarketOverview, {
      props: { ...baseProps, error: '网络错误' }
    })
    expect(wrapper.text()).toContain('网络错误')
  })

  it('expands detail tray on toggle click and persists state', async () => {
    const wrapper = mount(StockTopMarketOverview, { props: baseProps })
    expect(wrapper.find('.bar-detail-tray').exists()).toBe(false)
    await wrapper.find('.expand-toggle').trigger('click')
    expect(wrapper.find('.bar-detail-tray').exists()).toBe(true)
    expect(localStorage.getItem('market_bar_expanded')).toBe('true')
    expect(wrapper.text()).toContain('主力资金')
    expect(wrapper.text()).toContain('超大单')
    expect(wrapper.text()).toContain('北向资金')
    expect(wrapper.text()).toContain('市场广度')
    expect(wrapper.text()).toContain('封板温度')
  })

  it('does not show current stock badge when no stock selected', () => {
    const wrapper = mount(StockTopMarketOverview, { props: baseProps })
    expect(wrapper.find('.cur-stk-badge').exists()).toBe(false)
  })

  it('shows current stock badge when stock is selected', () => {
    const wrapper = mount(StockTopMarketOverview, {
      props: {
        ...baseProps,
        currentStockRealtimeQuote: {
          name: '贵州茅台', symbol: 'sh600519', price: 1856.0, change: 38.6, changePercent: 2.12, turnoverAmount: 15000000000
        }
      }
    })
    expect(wrapper.find('.cur-stk-badge').exists()).toBe(true)
    expect(wrapper.text()).toContain('贵州茅台')
  })

  it('shows individual stock comparison in expanded tray only when stock selected', async () => {
    const wrapper = mount(StockTopMarketOverview, {
      props: {
        ...baseProps,
        currentStockRealtimeQuote: {
          name: '贵州茅台', symbol: 'sh600519', price: 1856.0, change: 38.6, changePercent: 2.12, turnoverAmount: 15000000000
        },
        stockRealtimeRelativeStrength: { label: '跑赢沪指', spread: 0.89 }
      }
    })
    await wrapper.find('.expand-toggle').trigger('click')
    expect(wrapper.text()).toContain('个股对照')
    expect(wrapper.text()).toContain('贵州茅台')
    expect(wrapper.text()).toContain('跑赢沪指')
    expect(wrapper.text()).toContain('+0.89 pp')
    expect(wrapper.text()).not.toContain('+0.89%')
  })

  it('keeps collapsed and expanded northbound copy consistent when data is closed', async () => {
    const wrapper = mount(StockTopMarketOverview, {
      props: {
        ...baseProps,
        overview: {
          ...baseProps.overview,
          northboundFlow: { totalNetInflow: 0, shanghaiNetInflow: 0, shenzhenNetInflow: 0, isStale: true, status: 'closed' }
        },
        formatSignedRealtimeAmount: v => `${Number(v ?? 0) >= 0 ? '+' : ''}${Number(v ?? 0).toFixed(2)} 亿`
      }
    })

    expect(wrapper.text()).toContain('休市')
    await wrapper.find('.expand-toggle').trigger('click')

    const text = wrapper.text()
    expect(text).toContain('北向资金')
    expect(text).toContain('休市')
    expect(text).not.toContain('+0.00 亿')
  })

  it('renders valid northbound amount normally', () => {
    const wrapper = mount(StockTopMarketOverview, {
      props: {
        ...baseProps,
        overview: {
          ...baseProps.overview,
          northboundFlow: { totalNetInflow: 8.9, shanghaiNetInflow: 4.1, shenzhenNetInflow: 4.8, isStale: false, status: 'ok' }
        },
        formatSignedRealtimeAmount: v => `${Number(v ?? 0) >= 0 ? '+' : ''}${Number(v ?? 0).toFixed(2)} 亿`
      }
    })

    expect(wrapper.text()).toContain('+8.90 亿')
  })

  it('stops countdown when disabled', async () => {
    const wrapper = mount(StockTopMarketOverview, { props: baseProps })
    vi.advanceTimersByTime(10000)
    await wrapper.setProps({ enabled: false })
    vi.advanceTimersByTime(30000)
    expect(wrapper.emitted('refresh')).toBeUndefined()
  })
})
