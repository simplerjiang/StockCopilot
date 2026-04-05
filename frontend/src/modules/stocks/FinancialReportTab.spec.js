import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import FinancialReportTab from './FinancialReportTab.vue'

const mockFetch = vi.fn()
global.fetch = mockFetch

const mockTrendData = {
  symbol: '600519',
  periodCount: 2,
  revenue: [
    { period: '2024-12-31', value: 173695000000, yoY: 16.25 },
    { period: '2023-12-31', value: 149451000000, yoY: 18.04 }
  ],
  netProfit: [
    { period: '2024-12-31', value: 86229000000, yoY: 15.38 },
    { period: '2023-12-31', value: 74734000000, yoY: 19.16 }
  ],
  totalAssets: [
    { period: '2024-12-31', value: 278543000000, yoY: 12.50 }
  ],
  recentDividends: [
    { plan: '2024年年报 10派275.83元', dividendPerShare: 27.583 }
  ]
}

const mockSummaryData = {
  symbol: '600519',
  periodCount: 2,
  periods: [
    {
      reportDate: '2024-12-31',
      reportType: 'Annual',
      sourceChannel: 'emweb',
      keyMetrics: { Revenue: 173695000000, NetProfit: 86229000000, TotalAssets: 278543000000, DebtToAssetRatio: 0.25 }
    },
    {
      reportDate: '2023-12-31',
      reportType: 'Annual',
      sourceChannel: 'emweb',
      keyMetrics: { Revenue: 149451000000, NetProfit: 74734000000, TotalAssets: 247594000000, DebtToAssetRatio: 0.23 }
    }
  ]
}

function setupFetchMock(trendOk = true, summaryOk = true) {
  mockFetch.mockImplementation((url) => {
    if (url.includes('/trend/')) {
      return Promise.resolve({
        ok: trendOk,
        json: () => Promise.resolve(mockTrendData)
      })
    }
    if (url.includes('/summary/')) {
      return Promise.resolve({
        ok: summaryOk,
        json: () => Promise.resolve(mockSummaryData)
      })
    }
    return Promise.resolve({ ok: false })
  })
}

describe('FinancialReportTab', () => {
  beforeEach(() => {
    mockFetch.mockReset()
  })

  it('shows empty state when no symbol', async () => {
    const wrapper = mount(FinancialReportTab, { props: { symbol: '', active: true } })
    await flushPromises()
    expect(wrapper.text()).toContain('请先选择一只股票')
    expect(mockFetch).not.toHaveBeenCalled()
  })

  it('does not fetch when inactive', async () => {
    setupFetchMock()
    mount(FinancialReportTab, { props: { symbol: '600519', active: false } })
    await flushPromises()
    expect(mockFetch).not.toHaveBeenCalled()
  })

  it('fetches data when active with symbol', async () => {
    setupFetchMock()
    mount(FinancialReportTab, { props: { symbol: '600519', active: true } })
    await flushPromises()
    expect(mockFetch).toHaveBeenCalledTimes(2)
    expect(mockFetch).toHaveBeenCalledWith(expect.stringContaining('/api/stocks/financial/trend/600519'))
    expect(mockFetch).toHaveBeenCalledWith(expect.stringContaining('/api/stocks/financial/summary/600519'))
  })

  it('renders metric cards with data', async () => {
    setupFetchMock()
    const wrapper = mount(FinancialReportTab, { props: { symbol: '600519', active: true } })
    await flushPromises()
    const cards = wrapper.findAll('.metric-card')
    expect(cards.length).toBe(3)
    expect(wrapper.text()).toContain('营业收入')
    expect(wrapper.text()).toContain('净利润')
    expect(wrapper.text()).toContain('总资产')
  })

  it('renders trend table rows', async () => {
    setupFetchMock()
    const wrapper = mount(FinancialReportTab, { props: { symbol: '600519', active: true } })
    await flushPromises()
    const rows = wrapper.findAll('.trend-table tbody tr')
    expect(rows.length).toBeGreaterThanOrEqual(1)
    expect(wrapper.text()).toContain('2024-12-31')
  })

  it('switches between statement types', async () => {
    setupFetchMock()
    const wrapper = mount(FinancialReportTab, { props: { symbol: '600519', active: true } })
    await flushPromises()

    expect(wrapper.find('.statement-tabs button.active').text()).toBe('利润表')

    const buttons = wrapper.findAll('.statement-tabs button')
    const balanceBtn = buttons.find(b => b.text() === '资产负债表')
    await balanceBtn.trigger('click')
    expect(wrapper.find('.statement-tabs button.active').text()).toBe('资产负债表')
  })

  it('renders dividend records', async () => {
    setupFetchMock()
    const wrapper = mount(FinancialReportTab, { props: { symbol: '600519', active: true } })
    await flushPromises()
    expect(wrapper.text()).toContain('近期分红')
    expect(wrapper.text()).toContain('2024年年报')
  })

  it('shows error state on fetch failure', async () => {
    mockFetch.mockRejectedValue(new Error('Network error'))
    const wrapper = mount(FinancialReportTab, { props: { symbol: '600519', active: true } })
    await flushPromises()
    expect(wrapper.text()).toContain('加载失败')
  })

  it('shows collect button when no data', async () => {
    mockFetch.mockResolvedValue({ ok: false })
    const wrapper = mount(FinancialReportTab, { props: { symbol: 'SZ000001', active: true } })
    await flushPromises()
    expect(wrapper.find('.collect-btn').exists()).toBe(true)
    expect(wrapper.find('.collect-btn').text()).toContain('获取财务数据')
  })

  it('calls collect endpoint and refreshes data', async () => {
    // First load: no data
    mockFetch
      .mockResolvedValueOnce({ ok: false })
      .mockResolvedValueOnce({ ok: false })
    // Collect POST call
      .mockResolvedValueOnce({ ok: true, json: () => Promise.resolve({ Success: true, Channel: 'emweb', ReportCount: 4, DurationMs: 3500 }) })
    // Refresh after collect: trend + summary
      .mockResolvedValueOnce({ ok: true, json: () => Promise.resolve(mockTrendData) })
      .mockResolvedValueOnce({ ok: true, json: () => Promise.resolve(mockSummaryData) })

    const wrapper = mount(FinancialReportTab, { props: { symbol: 'SZ000001', active: true } })
    await flushPromises()

    await wrapper.find('.collect-btn').trigger('click')
    await flushPromises()

    const postCall = mockFetch.mock.calls.find(c => c[1]?.method === 'POST')
    expect(postCall).toBeTruthy()
    expect(postCall[0]).toContain('/api/stocks/financial/collect/SZ000001')
  })

  it('shows refresh button when data exists', async () => {
    setupFetchMock()
    const wrapper = mount(FinancialReportTab, { props: { symbol: '600519', active: true } })
    await flushPromises()
    expect(wrapper.find('.refresh-btn').exists()).toBe(true)
    expect(wrapper.find('.refresh-btn').text()).toContain('刷新数据')
  })

  it('shows error when collect fails', async () => {
    mockFetch
      .mockResolvedValueOnce({ ok: false })
      .mockResolvedValueOnce({ ok: false })
      .mockResolvedValueOnce({ ok: false, status: 503 })

    const wrapper = mount(FinancialReportTab, { props: { symbol: 'SZ000001', active: true } })
    await flushPromises()

    await wrapper.find('.collect-btn').trigger('click')
    await flushPromises()

    expect(wrapper.find('.error-msg').exists()).toBe(true)
  })
})
