import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'

// V041-S6: stub ComparePane to keep tests focused on Tab behaviour and avoid pulling
// the PDF viewer / parse / voting subtree.
vi.mock('../financial/FinancialReportComparePane.vue', () => ({
  default: {
    name: 'FinancialReportComparePaneStub',
    props: ['pdfFileId', 'pdfFileDetail', 'loading', 'error'],
    emits: ['refresh', 'close'],
    template: '<div class="compare-pane-stub" data-testid="compare-pane-stub">stub:{{ pdfFileId }}</div>'
  }
}))

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

const emptyTrendData = {
  symbol: 'SZ000001',
  revenue: [],
  netProfit: [],
  totalAssets: [],
  recentDividends: []
}

const emptySummaryData = {
  symbol: 'SZ000001',
  periods: []
}

const sparsePdfSummaryData = {
  symbol: 'SZ000001',
  periods: [
    {
      reportDate: '2024-09-30',
      reportType: 'Quarterly',
      sourceChannel: 'pdf',
      keyMetrics: {}
    },
    {
      reportDate: '2024-06-30',
      reportType: 'Quarterly',
      sourceChannel: 'pdf',
      keyMetrics: null
    }
  ]
}

const camelCaseSuccessCollectResult = {
  success: true,
  channel: 'emweb',
  reportCount: 4,
  durationMs: 3500,
  isDegraded: true,
  degradeReason: 'emweb empty data'
}

const pascalCaseSuccessCollectResult = {
  Success: true,
  Channel: 'emweb',
  ReportCount: 4,
  DurationMs: 3500
}

function createJsonResponse(data, ok = true, status = ok ? 200 : 500) {
  return {
    ok,
    status,
    json: () => Promise.resolve(data)
  }
}

function setupFetchMock(options = {}) {
  const {
    trendData = mockTrendData,
    summaryData = mockSummaryData,
    trendOk = true,
    summaryOk = true
  } = options

  mockFetch.mockImplementation((url) => {
    if (url.includes('/trend/')) {
      return Promise.resolve(createJsonResponse(trendData, trendOk))
    }
    if (url.includes('/summary/')) {
      return Promise.resolve(createJsonResponse(summaryData, summaryOk))
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

  it('shows collect button when endpoints return empty payloads', async () => {
    setupFetchMock({ trendData: emptyTrendData, summaryData: emptySummaryData })
    const wrapper = mount(FinancialReportTab, { props: { symbol: 'SZ000001', active: true } })
    await flushPromises()
    expect(wrapper.find('.collect-btn').exists()).toBe(true)
    expect(wrapper.find('.collect-btn').text()).toContain('获取财务数据')
    expect(wrapper.find('.refresh-btn').exists()).toBe(false)
  })

  it('refreshes data from a camelCase collect success response and keeps the success banner', async () => {
    mockFetch
      .mockResolvedValueOnce(createJsonResponse(emptyTrendData))
      .mockResolvedValueOnce(createJsonResponse(emptySummaryData))
      .mockResolvedValueOnce(createJsonResponse(camelCaseSuccessCollectResult))
      .mockResolvedValueOnce(createJsonResponse(mockTrendData))
      .mockResolvedValueOnce(createJsonResponse(mockSummaryData))

    const wrapper = mount(FinancialReportTab, { props: { symbol: 'SZ000001', active: true } })
    await flushPromises()

    expect(wrapper.find('.collect-btn').exists()).toBe(true)

    await wrapper.find('.collect-btn').trigger('click')
    await flushPromises()

    const postCall = mockFetch.mock.calls.find(c => c[1]?.method === 'POST')
    expect(postCall).toBeTruthy()
    expect(postCall[0]).toContain('/api/stocks/financial/collect/SZ000001')
    expect(wrapper.find('.refresh-btn').exists()).toBe(true)
    expect(wrapper.find('.collect-btn').exists()).toBe(false)
    expect(wrapper.text()).toContain('营业收入')
    expect(wrapper.find('.collect-info').exists()).toBe(true)
    expect(wrapper.find('.collect-info').text()).toContain('已通过 emweb 获取 4 期报表')
    expect(wrapper.find('.collect-info').text()).toContain('提示：采集渠道未返回有效数据。')
  })

  it('localizes a live-style English collect error message for the empty state', async () => {
    mockFetch
      .mockResolvedValueOnce(createJsonResponse(emptyTrendData))
      .mockResolvedValueOnce(createJsonResponse(emptySummaryData))
      .mockResolvedValueOnce(createJsonResponse({ success: false, errorMessage: 'All channels (API + PDF) failed or returned empty data', isDegraded: true, degradeReason: 'emweb empty data' }))

    const wrapper = mount(FinancialReportTab, { props: { symbol: 'SZ000001', active: true } })
    await flushPromises()

    await wrapper.find('.collect-btn').trigger('click')
    await flushPromises()

    expect(wrapper.find('.error-msg').exists()).toBe(true)
    expect(wrapper.find('.error-msg').classes()).toContain('error-msg-prominent')
    expect(wrapper.find('.error-msg').text()).toContain('所有采集渠道都未返回有效财务数据，请稍后重试或更换股票。')
  })

  it('falls back to the camelCase degradeReason when collect errorMessage is missing', async () => {
    mockFetch
      .mockResolvedValueOnce(createJsonResponse(emptyTrendData))
      .mockResolvedValueOnce(createJsonResponse(emptySummaryData))
      .mockResolvedValueOnce(createJsonResponse({ success: false, isDegraded: true, degradeReason: 'emweb empty data' }))

    const wrapper = mount(FinancialReportTab, { props: { symbol: 'SZ000001', active: true } })
    await flushPromises()

    await wrapper.find('.collect-btn').trigger('click')
    await flushPromises()

    expect(wrapper.find('.error-msg').exists()).toBe(true)
    expect(wrapper.find('.error-msg').text()).toContain('采集渠道未返回有效数据。')
  })

  it('accepts a PascalCase collect success response for compatibility', async () => {
    mockFetch
      .mockResolvedValueOnce(createJsonResponse(emptyTrendData))
      .mockResolvedValueOnce(createJsonResponse(emptySummaryData))
      .mockResolvedValueOnce(createJsonResponse(pascalCaseSuccessCollectResult))
      .mockResolvedValueOnce(createJsonResponse(mockTrendData))
      .mockResolvedValueOnce(createJsonResponse(mockSummaryData))

    const wrapper = mount(FinancialReportTab, { props: { symbol: 'SZ000001', active: true } })
    await flushPromises()

    await wrapper.find('.collect-btn').trigger('click')
    await flushPromises()

    expect(wrapper.find('.collect-info').exists()).toBe(true)
    expect(wrapper.find('.collect-info').text()).toContain('已通过 emweb 获取 4 期报表')
    expect(wrapper.text()).toContain('营业收入')
  })

  it('shows an explicit partial-data message when collect succeeds with only sparse report periods', async () => {
    mockFetch
      .mockResolvedValueOnce(createJsonResponse(emptyTrendData))
      .mockResolvedValueOnce(createJsonResponse(emptySummaryData))
      .mockResolvedValueOnce(createJsonResponse({ success: true, channel: 'pdf', reportCount: 2, durationMs: 1800 }))
      .mockResolvedValueOnce(createJsonResponse(emptyTrendData))
      .mockResolvedValueOnce(createJsonResponse(sparsePdfSummaryData))

    const wrapper = mount(FinancialReportTab, { props: { symbol: 'SZ000001', active: true } })
    await flushPromises()

    await wrapper.find('.collect-btn').trigger('click')
    await flushPromises()

    expect(wrapper.find('.refresh-btn').exists()).toBe(true)
    expect(wrapper.find('.collect-info').exists()).toBe(false)
    expect(wrapper.find('.partial-data-message').exists()).toBe(true)
    expect(wrapper.find('.partial-data-message').text()).toContain('已通过 pdf 获取 2 期报表')
    expect(wrapper.find('.partial-data-message').text()).toContain('暂无可展示的结构化财务指标')
    expect(wrapper.find('.partial-data-message').text()).toContain('2024-09-30')
    expect(wrapper.find('.partial-data-message').text()).toContain('来源：pdf')
    expect(wrapper.find('.summary-table').exists()).toBe(false)
    expect(wrapper.find('.trend-table').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('营业收入')
  })

  it('clears previous symbol data when the next symbol returns empty payloads', async () => {
    mockFetch.mockImplementation((url) => {
      if (url.includes('/trend/600519')) {
        return Promise.resolve(createJsonResponse(mockTrendData))
      }
      if (url.includes('/summary/600519')) {
        return Promise.resolve(createJsonResponse(mockSummaryData))
      }
      if (url.includes('/trend/SZ000001')) {
        return Promise.resolve(createJsonResponse(emptyTrendData))
      }
      if (url.includes('/summary/SZ000001')) {
        return Promise.resolve(createJsonResponse(emptySummaryData))
      }
      return Promise.resolve({ ok: false })
    })

    const wrapper = mount(FinancialReportTab, { props: { symbol: '600519', active: true } })
    await flushPromises()

    expect(wrapper.find('.refresh-btn').exists()).toBe(true)
    expect(wrapper.text()).toContain('营业收入')

    await wrapper.setProps({ symbol: 'SZ000001' })
    await flushPromises()

    expect(wrapper.find('.collect-btn').exists()).toBe(true)
    expect(wrapper.find('.refresh-btn').exists()).toBe(false)
    expect(wrapper.text()).toContain('暂无财务数据')
    expect(wrapper.text()).not.toContain('营业收入')
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
      .mockResolvedValueOnce(createJsonResponse(emptyTrendData))
      .mockResolvedValueOnce(createJsonResponse(emptySummaryData))
      .mockResolvedValueOnce(createJsonResponse({ error: '采集失败 (503)' }, false, 503))

    const wrapper = mount(FinancialReportTab, { props: { symbol: 'SZ000001', active: true } })
    await flushPromises()

    await wrapper.find('.collect-btn').trigger('click')
    await flushPromises()

    expect(wrapper.find('.error-msg').exists()).toBe(true)
    expect(wrapper.find('.error-msg').text()).toContain('采集失败')
  })

  it('renders V040-S4 透明化字段：报告期/标题/来源 Tag/降级原因/PDF 摘要', async () => {
    mockFetch
      .mockResolvedValueOnce(createJsonResponse(emptyTrendData))
      .mockResolvedValueOnce(createJsonResponse(emptySummaryData))
      .mockResolvedValueOnce(createJsonResponse({
        success: true,
        channel: 'emweb',
        reportCount: 4,
        durationMs: 3500,
        reportPeriod: '2024-12-31',
        reportTitle: '2024年年度报告',
        sourceChannel: 'emweb',
        fallbackReason: 'datacenter empty data',
        pdfSummary: 'pdf:2_tables_appended'
      }))
      .mockResolvedValueOnce(createJsonResponse(mockTrendData))
      .mockResolvedValueOnce(createJsonResponse(mockSummaryData))

    const wrapper = mount(FinancialReportTab, { props: { symbol: 'SZ000001', active: true } })
    await flushPromises()
    await wrapper.find('.collect-btn').trigger('click')
    await flushPromises()

    const meta = wrapper.find('.collect-meta')
    expect(meta.exists()).toBe(true)
    expect(meta.find('[data-field="reportPeriod"]').text()).toContain('2024-12-31')
    expect(meta.find('[data-field="reportTitle"]').text()).toContain('2024年年度报告')

    const channelRow = meta.find('[data-field="sourceChannel"]')
    expect(channelRow.exists()).toBe(true)
    const tag = channelRow.find('.source-channel-tag')
    expect(tag.exists()).toBe(true)
    expect(tag.attributes('data-channel-key')).toBe('emweb')
    expect(tag.text()).toBe('EM 网页')

    expect(meta.find('[data-field="fallbackReason"]').text()).toContain('datacenter empty data')

    const pdfRow = meta.find('[data-field="pdfSummary"]')
    expect(pdfRow.exists()).toBe(true)
    expect(wrapper.find('.pdf-summary-content').exists()).toBe(false)
    await pdfRow.find('.pdf-summary-toggle').trigger('click')
    expect(wrapper.find('.pdf-summary-content').text()).toContain('pdf:2_tables_appended')
  })

  it('omits 透明化 meta rows when fields are missing (旧响应兼容)', async () => {
    mockFetch
      .mockResolvedValueOnce(createJsonResponse(emptyTrendData))
      .mockResolvedValueOnce(createJsonResponse(emptySummaryData))
      .mockResolvedValueOnce(createJsonResponse({
        success: true,
        channel: 'emweb',
        reportCount: 4,
        durationMs: 3500
      }))
      .mockResolvedValueOnce(createJsonResponse(mockTrendData))
      .mockResolvedValueOnce(createJsonResponse(mockSummaryData))

    const wrapper = mount(FinancialReportTab, { props: { symbol: 'SZ000001', active: true } })
    await flushPromises()
    await wrapper.find('.collect-btn').trigger('click')
    await flushPromises()

    // sourceChannel falls back to legacy `channel`, so the row exists; other 4 should not.
    const meta = wrapper.find('.collect-meta')
    expect(meta.exists()).toBe(true)
    expect(meta.find('[data-field="reportPeriod"]').exists()).toBe(false)
    expect(meta.find('[data-field="reportTitle"]').exists()).toBe(false)
    expect(meta.find('[data-field="fallbackReason"]').exists()).toBe(false)
    expect(meta.find('[data-field="pdfSummary"]').exists()).toBe(false)
    expect(meta.find('[data-field="sourceChannel"] .source-channel-tag').attributes('data-channel-key')).toBe('emweb')
  })

  it('falls back to reportPeriods[0] / pdfSummarySupplement aliases', async () => {
    mockFetch
      .mockResolvedValueOnce(createJsonResponse(emptyTrendData))
      .mockResolvedValueOnce(createJsonResponse(emptySummaryData))
      .mockResolvedValueOnce(createJsonResponse({
        success: true,
        channel: 'pdf',
        reportCount: 1,
        durationMs: 2000,
        reportPeriods: ['2024-09-30', '2024-06-30'],
        pdfSummarySupplement: 'pdf:1_tables_appended'
      }))
      .mockResolvedValueOnce(createJsonResponse(emptyTrendData))
      .mockResolvedValueOnce(createJsonResponse(sparsePdfSummaryData))

    const wrapper = mount(FinancialReportTab, { props: { symbol: 'SZ000001', active: true } })
    await flushPromises()
    await wrapper.find('.collect-btn').trigger('click')
    await flushPromises()

    const meta = wrapper.find('.collect-meta')
    expect(meta.exists()).toBe(true)
    expect(meta.find('[data-field="reportPeriod"]').text()).toContain('2024-09-30')
    expect(meta.find('[data-field="sourceChannel"] .source-channel-tag').attributes('data-channel-key')).toBe('pdf')
    expect(meta.find('[data-field="pdfSummary"]').exists()).toBe(true)
  })

  // ============================================================================
  // V041-S6: 「查看 PDF 原件 / 对照」入口 + ComparePane 复用
  // ============================================================================

  it('V041-S6 入口可见性：数据加载后 report-header 中可见 view-pdf-btn', async () => {
    setupFetchMock()
    const wrapper = mount(FinancialReportTab, {
      props: { symbol: '600519', active: true },
      attachTo: document.body
    })
    await flushPromises()
    const btn = wrapper.find('[data-testid="view-pdf-btn"]')
    expect(btn.exists()).toBe(true)
    expect(btn.text()).toContain('查看 PDF 原件')
    wrapper.unmount()
  })

  it('V041-S6 点击跳转：解析 pdfFileId 后挂载 ComparePane stub', async () => {
    mockFetch.mockImplementation((url) => {
      if (url.includes('/trend/')) return Promise.resolve(createJsonResponse(mockTrendData))
      if (url.includes('/summary/')) return Promise.resolve(createJsonResponse(mockSummaryData))
      if (url.includes('/pdf-files?')) {
        return Promise.resolve(createJsonResponse({
          items: [
            { id: 4242, reportPeriod: '2024-12-31', fileName: 'mt.pdf' },
            { id: 4001, reportPeriod: '2024-06-30', fileName: 'old.pdf' }
          ],
          total: 2,
          page: 1,
          pageSize: 5
        }))
      }
      return Promise.resolve({ ok: false })
    })

    const wrapper = mount(FinancialReportTab, {
      props: { symbol: '600519', active: true },
      attachTo: document.body
    })
    await flushPromises()

    await wrapper.find('[data-testid="view-pdf-btn"]').trigger('click')
    await flushPromises()

    // Modal Teleport 到 body，需要在 document 上找
    const modal = document.querySelector('[data-testid="pdf-viewer-modal"]')
    expect(modal).not.toBeNull()
    const stub = modal.querySelector('[data-testid="compare-pane-stub"]')
    expect(stub).not.toBeNull()
    // 期望按 reportDate 匹配，挑到 id=4242 的那条
    expect(stub.textContent).toContain('4242')

    // 至少调用过一次 listPdfFiles（带 symbol 参数）
    const pdfCall = mockFetch.mock.calls.find(c => String(c[0]).includes('/pdf-files?'))
    expect(pdfCall).toBeTruthy()
    expect(String(pdfCall[0])).toContain('symbol=600519')

    wrapper.unmount()
    // 清理 Teleport 残留
    document.querySelectorAll('[data-testid="pdf-viewer-modal"]').forEach(el => el.remove())
  })

  it('V041-S6 reparse 回写：ComparePane emit refresh 后 Tab 不抛异常且 modal 仍可关闭', async () => {
    mockFetch.mockImplementation((url) => {
      if (url.includes('/trend/')) return Promise.resolve(createJsonResponse(mockTrendData))
      if (url.includes('/summary/')) return Promise.resolve(createJsonResponse(mockSummaryData))
      if (url.includes('/pdf-files?')) {
        return Promise.resolve(createJsonResponse({
          items: [{ id: 7777, reportPeriod: '2024-12-31', fileName: 'rp.pdf' }]
        }))
      }
      return Promise.resolve({ ok: false })
    })

    const wrapper = mount(FinancialReportTab, {
      props: { symbol: '600519', active: true },
      attachTo: document.body
    })
    await flushPromises()
    await wrapper.find('[data-testid="view-pdf-btn"]').trigger('click')
    await flushPromises()

    const stubComp = wrapper.findComponent({ name: 'FinancialReportComparePaneStub' })
    expect(stubComp.exists()).toBe(true)

    // 触发 refresh —— Tab 自身 onComparePaneRefresh 不应抛异常
    expect(() => stubComp.vm.$emit('refresh', { id: 7777, voteConfidence: 0.95 })).not.toThrow()
    await flushPromises()

    // modal 仍存在
    expect(document.querySelector('[data-testid="pdf-viewer-modal"]')).not.toBeNull()

    // 触发 ComparePane close → 关闭 modal
    stubComp.vm.$emit('close')
    await flushPromises()
    expect(document.querySelector('[data-testid="pdf-viewer-modal"]')).toBeNull()

    wrapper.unmount()
  })

  it('V041-S6 空态：listPdfFiles 返回空列表时显示提示，不挂载 ComparePane', async () => {
    mockFetch.mockImplementation((url) => {
      if (url.includes('/trend/')) return Promise.resolve(createJsonResponse(mockTrendData))
      if (url.includes('/summary/')) return Promise.resolve(createJsonResponse(mockSummaryData))
      if (url.includes('/pdf-files?')) {
        return Promise.resolve(createJsonResponse({ items: [], total: 0, page: 1, pageSize: 5 }))
      }
      return Promise.resolve({ ok: false })
    })

    const wrapper = mount(FinancialReportTab, {
      props: { symbol: '600519', active: true },
      attachTo: document.body
    })
    await flushPromises()
    await wrapper.find('[data-testid="view-pdf-btn"]').trigger('click')
    await flushPromises()

    const modal = document.querySelector('[data-testid="pdf-viewer-modal"]')
    expect(modal).not.toBeNull()
    expect(modal.querySelector('[data-testid="compare-pane-stub"]')).toBeNull()
    expect(modal.textContent).toContain('暂无 PDF 原件')

    wrapper.unmount()
    document.querySelectorAll('[data-testid="pdf-viewer-modal"]').forEach(el => el.remove())
  })

  // ============================================================================
  // V041-S8-FU-1: 「📥 采集 PDF 原件」入口
  // ============================================================================
  it('V041-S8-FU-1 点击「📥 采集 PDF 原件」调用 collect 接口，成功后显示完成提示', async () => {
    mockFetch.mockImplementation((url, init) => {
      if (typeof url === 'string' && url.includes('/pdf-files/collect/')) {
        return Promise.resolve(createJsonResponse({ success: true, processedCount: 1 }))
      }
      if (url.includes('/trend/')) return Promise.resolve(createJsonResponse(mockTrendData))
      if (url.includes('/summary/')) return Promise.resolve(createJsonResponse(mockSummaryData))
      return Promise.resolve({ ok: false })
    })

    const wrapper = mount(FinancialReportTab, {
      props: { symbol: '600519', active: true },
      attachTo: document.body
    })
    await flushPromises()

    const btn = wrapper.find('[data-testid="collect-pdf-btn"]')
    expect(btn.exists()).toBe(true)
    expect(btn.text()).toContain('采集 PDF 原件')

    await btn.trigger('click')
    await flushPromises()

    const collectCall = mockFetch.mock.calls.find(c => String(c[0]).includes('/pdf-files/collect/'))
    expect(collectCall).toBeTruthy()
    expect(String(collectCall[0])).toContain('600519')
    expect(collectCall[1]).toEqual({ method: 'POST' })

    expect(wrapper.find('[data-testid="collect-pdf-info"]').exists()).toBe(true)

    wrapper.unmount()
  })

  // ============================================================================
  // V042-P0-C (B3): 入口 fallback 修复
  //   1) symbol 必须 strip 市场前缀（sh/sz）→ 后端按数字精确匹配才能命中
  //   2) reportPeriod 没命中时 fallback 到 fieldCount 最大那条（主报告而非摘要）
  //   3) 空数组仍展示「暂无 PDF 原件」alert
  //   4) ComparePane emit refresh 后调用 fetchData 局部刷新
  // ============================================================================

  it('V042-P0-C 入口：sh600519 → listPdfFiles 仅传裸数字 600519', async () => {
    const calls = []
    mockFetch.mockImplementation((url) => {
      const u = String(url)
      calls.push(u)
      if (u.includes('/trend/')) return Promise.resolve(createJsonResponse(mockTrendData))
      if (u.includes('/summary/')) return Promise.resolve(createJsonResponse(mockSummaryData))
      if (u.includes('/pdf-files?')) {
        return Promise.resolve(createJsonResponse({
          items: [{ id: 'PDF-1', reportPeriod: '2024-12-31', fieldCount: 10, fileName: 'main.pdf' }],
          total: 1, page: 1, pageSize: 10
        }))
      }
      return Promise.resolve({ ok: false })
    })

    const wrapper = mount(FinancialReportTab, {
      props: { symbol: 'sh600519', active: true },
      attachTo: document.body
    })
    await flushPromises()
    await wrapper.find('[data-testid="view-pdf-btn"]').trigger('click')
    await flushPromises()

    const pdfCall = calls.find(u => u.includes('/pdf-files?'))
    expect(pdfCall).toBeTruthy()
    expect(pdfCall).toContain('symbol=600519')
    expect(pdfCall).not.toContain('sh600519')
    // ComparePane 应该被挂载，不应出现 alert
    const modal = document.querySelector('[data-testid="pdf-viewer-modal"]')
    expect(modal).not.toBeNull()
    expect(modal.querySelector('[data-testid="compare-pane-stub"]')).not.toBeNull()
    expect(modal.textContent).not.toContain('暂无 PDF 原件')

    wrapper.unmount()
    document.querySelectorAll('[data-testid="pdf-viewer-modal"]').forEach(el => el.remove())
  })

  it('V042-P0-C fallback：reportPeriod 未命中时挑 fieldCount 最大那条（主报告）', async () => {
    mockFetch.mockImplementation((url) => {
      const u = String(url)
      if (u.includes('/trend/')) return Promise.resolve(createJsonResponse(mockTrendData))
      if (u.includes('/summary/')) return Promise.resolve(createJsonResponse(mockSummaryData))
      if (u.includes('/pdf-files?')) {
        // 3 条记录：摘要 fieldCount=0、主报告 fieldCount=10、扉页 fieldCount=2
        // 没有任何一条的 reportPeriod 跟 summary 的 2024-12-31 对得上
        return Promise.resolve(createJsonResponse({
          items: [
            { id: 'PDF-SUMMARY', reportPeriod: '2024-09-30', fieldCount: 0, fileName: 'summary.pdf' },
            { id: 'PDF-MAIN', reportPeriod: '2024-06-30', fieldCount: 10, fileName: 'annual.pdf' },
            { id: 'PDF-COVER', reportPeriod: '2024-06-30', fieldCount: 2, fileName: 'cover.pdf' }
          ],
          total: 3, page: 1, pageSize: 10
        }))
      }
      return Promise.resolve({ ok: false })
    })

    const wrapper = mount(FinancialReportTab, {
      props: { symbol: '600519', active: true },
      attachTo: document.body
    })
    await flushPromises()
    await wrapper.find('[data-testid="view-pdf-btn"]').trigger('click')
    await flushPromises()

    const modal = document.querySelector('[data-testid="pdf-viewer-modal"]')
    expect(modal).not.toBeNull()
    const stub = modal.querySelector('[data-testid="compare-pane-stub"]')
    expect(stub).not.toBeNull()
    // fieldCount 最大那条 = PDF-MAIN
    expect(stub.textContent).toContain('PDF-MAIN')
    expect(modal.textContent).not.toContain('暂无 PDF 原件')

    wrapper.unmount()
    document.querySelectorAll('[data-testid="pdf-viewer-modal"]').forEach(el => el.remove())
  })

  it('V042-P0-C fallback：reportPeriod 命中时优先按 reportPeriod 选（不被 fieldCount 覆盖）', async () => {
    mockFetch.mockImplementation((url) => {
      const u = String(url)
      if (u.includes('/trend/')) return Promise.resolve(createJsonResponse(mockTrendData))
      if (u.includes('/summary/')) return Promise.resolve(createJsonResponse(mockSummaryData))
      if (u.includes('/pdf-files?')) {
        return Promise.resolve(createJsonResponse({
          items: [
            // reportPeriod 命中（fieldCount 较小）
            { id: 'PDF-PERIOD-MATCH', reportPeriod: '2024-12-31', fieldCount: 4, fileName: 'q4.pdf' },
            // fieldCount 更大，但 reportPeriod 不匹配 — 不应被选
            { id: 'PDF-BIG-OTHER', reportPeriod: '2023-12-31', fieldCount: 99, fileName: 'old-annual.pdf' }
          ],
          total: 2, page: 1, pageSize: 10
        }))
      }
      return Promise.resolve({ ok: false })
    })

    const wrapper = mount(FinancialReportTab, {
      props: { symbol: '600519', active: true },
      attachTo: document.body
    })
    await flushPromises()
    await wrapper.find('[data-testid="view-pdf-btn"]').trigger('click')
    await flushPromises()

    const stub = document.querySelector('[data-testid="compare-pane-stub"]')
    expect(stub).not.toBeNull()
    expect(stub.textContent).toContain('PDF-PERIOD-MATCH')

    wrapper.unmount()
    document.querySelectorAll('[data-testid="pdf-viewer-modal"]').forEach(el => el.remove())
  })

  it('V042-P0-C 空数组仍显示 alert，不挂载 ComparePane', async () => {
    mockFetch.mockImplementation((url) => {
      const u = String(url)
      if (u.includes('/trend/')) return Promise.resolve(createJsonResponse(mockTrendData))
      if (u.includes('/summary/')) return Promise.resolve(createJsonResponse(mockSummaryData))
      if (u.includes('/pdf-files?')) {
        return Promise.resolve(createJsonResponse({ items: [], total: 0, page: 1, pageSize: 10 }))
      }
      return Promise.resolve({ ok: false })
    })

    const wrapper = mount(FinancialReportTab, {
      props: { symbol: '600519', active: true },
      attachTo: document.body
    })
    await flushPromises()
    await wrapper.find('[data-testid="view-pdf-btn"]').trigger('click')
    await flushPromises()

    const modal = document.querySelector('[data-testid="pdf-viewer-modal"]')
    expect(modal).not.toBeNull()
    expect(modal.querySelector('[data-testid="compare-pane-stub"]')).toBeNull()
    expect(modal.textContent).toContain('暂无 PDF 原件')

    wrapper.unmount()
    document.querySelectorAll('[data-testid="pdf-viewer-modal"]').forEach(el => el.remove())
  })

  it('V042-P0-C (B4) ComparePane refresh → 触发一次 fetchData（trend + summary 重新拉取）', async () => {
    let trendCalls = 0
    let summaryCalls = 0
    mockFetch.mockImplementation((url) => {
      const u = String(url)
      if (u.includes('/trend/')) { trendCalls++; return Promise.resolve(createJsonResponse(mockTrendData)) }
      if (u.includes('/summary/')) { summaryCalls++; return Promise.resolve(createJsonResponse(mockSummaryData)) }
      if (u.includes('/pdf-files?')) {
        return Promise.resolve(createJsonResponse({
          items: [{ id: 'PDF-X', reportPeriod: '2024-12-31', fieldCount: 8 }]
        }))
      }
      return Promise.resolve({ ok: false })
    })

    const wrapper = mount(FinancialReportTab, {
      props: { symbol: '600519', active: true },
      attachTo: document.body
    })
    await flushPromises()
    expect(trendCalls).toBe(1)
    expect(summaryCalls).toBe(1)

    await wrapper.find('[data-testid="view-pdf-btn"]').trigger('click')
    await flushPromises()

    const stubComp = wrapper.findComponent({ name: 'FinancialReportComparePaneStub' })
    expect(stubComp.exists()).toBe(true)
    stubComp.vm.$emit('refresh', { id: 'PDF-X' })
    await flushPromises()

    expect(trendCalls).toBe(2)
    expect(summaryCalls).toBe(2)

    wrapper.unmount()
    document.querySelectorAll('[data-testid="pdf-viewer-modal"]').forEach(el => el.remove())
  })
})
