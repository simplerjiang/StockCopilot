/**
 * @vitest-environment jsdom
 * @vitest-environment-options {"url":"http://localhost/"}
 */
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { nextTick } from 'vue'

const mocks = {
  fetchFinancialReportDetail: vi.fn(),
  recollectFinancialReport: vi.fn(),
  collectPdfFiles: vi.fn(),
  listPdfFiles: vi.fn(),
  fetchPdfFileDetail: vi.fn(),
  reparsePdfFile: vi.fn(),
  buildPdfFileContentUrl: vi.fn((id) => `/api/stocks/financial/pdf-files/${id}/content`)
}

vi.mock('../financialApi.js', () => ({
  fetchFinancialReportDetail: (...args) => mocks.fetchFinancialReportDetail(...args),
  recollectFinancialReport: (...args) => mocks.recollectFinancialReport(...args),
  collectPdfFiles: (...args) => mocks.collectPdfFiles(...args),
  listPdfFiles: (...args) => mocks.listPdfFiles(...args),
  fetchPdfFileDetail: (...args) => mocks.fetchPdfFileDetail(...args),
  reparsePdfFile: (...args) => mocks.reparsePdfFile(...args),
  buildPdfFileContentUrl: (...args) => mocks.buildPdfFileContentUrl(...args)
}))

import FinancialDetailDrawer from '../FinancialDetailDrawer.vue'

const baseItem = {
  id: '650f0a1b2c3d4e5f60718293',
  symbol: '600519',
  reportDate: '2024-12-31',
  reportType: 'annual'
}

const fullDetail = () => ({
  id: baseItem.id,
  symbol: '600519',
  reportDate: '2024-12-31',
  reportType: 'annual',
  sourceChannel: 'emweb',
  collectedAt: '2025-01-15T10:30:00Z',
  updatedAt: '2025-01-15T10:35:00Z',
  balanceSheet: {
    TotalAssets: 1000000,
    TotalLiabilities: 400000,
    TotalEquity: 600000,
    MonetaryFunds: 200000,
    AccountsReceivable: null
  },
  incomeStatement: {
    Revenue: 500000,
    OperatingProfit: 120000,
    NetProfit: 100000,
    EpsBasic: 1.23,
    GrossProfit: 250000
  },
  cashFlow: {
    OperatingCashFlow: 150000,
    InvestingCashFlow: -50000,
    FinancingCashFlow: 30000,
    NetIncreaseInCash: 130000,
    CashEnd: 200000
  }
})

const mountDrawer = (props = {}) => mount(FinancialDetailDrawer, {
  attachTo: document.body,
  props: {
    visible: true,
    item: baseItem,
    reportId: baseItem.id,
    ...props
  }
})

beforeEach(() => {
  mocks.fetchFinancialReportDetail.mockReset()
  mocks.recollectFinancialReport.mockReset()
  mocks.collectPdfFiles.mockReset()
  mocks.listPdfFiles.mockReset()
  mocks.fetchPdfFileDetail.mockReset()
  mocks.reparsePdfFile.mockReset()
  // 默认无 PDF：让多数旧用例进入「无 PDF」空态分支，与原占位行为兼容
  mocks.listPdfFiles.mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 5 })
  document.body.innerHTML = ''
})

describe('FinancialDetailDrawer - 加载与渲染', () => {
  it('mount 时调用 fetchFinancialReportDetail 并先显示 loading', async () => {
    let resolve
    const pending = new Promise((r) => { resolve = r })
    mocks.fetchFinancialReportDetail.mockReturnValueOnce(pending)

    const wrapper = mountDrawer()
    await nextTick()
    expect(mocks.fetchFinancialReportDetail).toHaveBeenCalledWith(baseItem.id)
    expect(document.body.textContent).toContain('正在加载财报详情')

    resolve(fullDetail())
    await flushPromises()
    expect(document.body.textContent).not.toContain('正在加载财报详情')

    wrapper.unmount()
  })

  it('fetch 成功后渲染元数据 + 三表白名单字段', async () => {
    mocks.fetchFinancialReportDetail.mockResolvedValueOnce(fullDetail())
    const wrapper = mountDrawer()
    await flushPromises()

    const text = document.body.textContent
    expect(text).toContain('600519')
    expect(text).toContain('2024-12-31')
    expect(text).toContain('年报')
    // 元数据
    expect(text).toContain('报告期')
    expect(text).toContain('报告类型')
    expect(text).toContain('来源渠道')
    expect(text).toContain('采集时间')
    expect(text).toContain('更新时间')
    expect(text).not.toContain('Report ID')
    expect(text).not.toContain(baseItem.id)
    // 三表 label
    expect(text).toContain('总资产')
    expect(text).toContain('股东权益合计')
    expect(text).toContain('营业收入')
    expect(text).toContain('净利润')
    expect(text).toContain('经营活动现金流净额')
    expect(text).toContain('期末现金及现金等价物余额')
    // 数字格式化（formatMoneyDisplay 缩写：1000000 → 100.00万，epsBasic=1.23 保留原值）
    expect(text).toContain('100.00万')
    expect(text).toContain('1.23')

    wrapper.unmount()
  })

  it('三表中某字段缺失时显示 —', async () => {
    mocks.fetchFinancialReportDetail.mockResolvedValueOnce(fullDetail())
    const wrapper = mountDrawer()
    await flushPromises()

    // accountsReceivable 为 null，应渲染 "—"
    const balanceSection = Array.from(document.querySelectorAll('.fc-drawer-section'))
      .find(s => s.textContent.includes('资产负债表'))
    expect(balanceSection).toBeTruthy()
    const rows = balanceSection.querySelectorAll('.fc-drawer-row')
    const arRow = Array.from(rows).find(r => r.textContent.includes('应收账款'))
    expect(arRow).toBeTruthy()
    expect(arRow.querySelector('dd').textContent.trim()).toBe('—')

    wrapper.unmount()
  })

  it('PDF 占位区显示空态文案（V041-S5：无 pdfFileId 时）', async () => {
    mocks.fetchFinancialReportDetail.mockResolvedValueOnce(fullDetail())
    mocks.listPdfFiles.mockResolvedValueOnce({ items: [], total: 0, page: 1, pageSize: 5 })
    const wrapper = mountDrawer()
    await flushPromises()
    const empty = document.querySelector('[data-testid="fc-drawer-pdf-empty"]')
    expect(empty).toBeTruthy()
    expect(empty.textContent).toContain('该报告暂无 PDF 原件')
    expect(empty.textContent).toContain('采集 PDF 原件')
    wrapper.unmount()
  })

  it('fetch 失败显示错误 + 重试按钮，点击重试再次调用', async () => {
    mocks.fetchFinancialReportDetail
      .mockRejectedValueOnce(new Error('boom (HTTP 500)'))
      .mockResolvedValueOnce(fullDetail())

    const wrapper = mountDrawer()
    await flushPromises()
    expect(document.body.textContent).toContain('boom')

    const retry = Array.from(document.querySelectorAll('button'))
      .find(b => b.textContent.trim() === '重试')
    expect(retry).toBeTruthy()
    retry.dispatchEvent(new Event('click', { bubbles: true }))
    await flushPromises()
    expect(mocks.fetchFinancialReportDetail).toHaveBeenCalledTimes(2)
    expect(document.body.textContent).toContain('总资产')

    wrapper.unmount()
  })
})

describe('FinancialDetailDrawer - 重新采集', () => {
  it('点击「重新采集」调用 recollectFinancialReport，成功后重新 fetch 详情', async () => {
    mocks.fetchFinancialReportDetail.mockResolvedValue(fullDetail())
    mocks.recollectFinancialReport.mockResolvedValueOnce({
      success: true, channel: 'emweb', reportCount: 5, durationMs: 2300
    })

    const wrapper = mountDrawer()
    await flushPromises()
    expect(mocks.fetchFinancialReportDetail).toHaveBeenCalledTimes(1)

    const recollectBtn = Array.from(document.querySelectorAll('button'))
      .find(b => b.textContent.includes('重新采集'))
    expect(recollectBtn).toBeTruthy()
    recollectBtn.dispatchEvent(new Event('click', { bubbles: true }))
    await flushPromises()

    expect(mocks.recollectFinancialReport).toHaveBeenCalledWith('600519')
    expect(document.body.textContent).toContain('采集完成')
    expect(document.body.textContent).toContain('emweb')
    expect(document.body.textContent).toContain('5 期报告')
    expect(mocks.fetchFinancialReportDetail).toHaveBeenCalledTimes(2)

    wrapper.unmount()
  })

  it('recollect 失败显示错误且抽屉不关闭', async () => {
    mocks.fetchFinancialReportDetail.mockResolvedValue(fullDetail())
    mocks.recollectFinancialReport.mockRejectedValueOnce(new Error('采集服务不可用 (HTTP 502)'))

    const wrapper = mountDrawer()
    await flushPromises()

    const recollectBtn = Array.from(document.querySelectorAll('button'))
      .find(b => b.textContent.includes('重新采集'))
    recollectBtn.dispatchEvent(new Event('click', { bubbles: true }))
    await flushPromises()

    expect(document.body.textContent).toContain('采集服务不可用')
    expect(wrapper.emitted('close')).toBeFalsy()
    expect(document.querySelector('.fc-drawer')).toBeTruthy()

    wrapper.unmount()
  })
})

describe('FinancialDetailDrawer - V041-S8-FU-1 PDF 原件采集', () => {
  it('点击「📥 采集 PDF 原件」调用 collectPdfFiles 并在成功后重新解析 PDF', async () => {
    mocks.fetchFinancialReportDetail.mockResolvedValue(fullDetail())
    // 第一次解析（mount 时）→ 空；第二次解析（采集后）→ 有 PDF
    mocks.listPdfFiles
      .mockResolvedValueOnce({ items: [], total: 0, page: 1, pageSize: 5 })
      .mockResolvedValueOnce({
        items: [{ id: 'pdf-new', symbol: '600519', reportPeriod: '2024-12-31' }],
        total: 1,
        page: 1,
        pageSize: 5
      })
    mocks.collectPdfFiles.mockResolvedValueOnce({ success: true, downloadedCount: 1, parsedCount: 1 })

    const wrapper = mountDrawer()
    await flushPromises()

    const collectBtn = document.querySelector('[data-testid="fc-drawer-collect-pdf-btn"]')
    expect(collectBtn).toBeTruthy()
    collectBtn.dispatchEvent(new Event('click', { bubbles: true }))
    await flushPromises()

    expect(mocks.collectPdfFiles).toHaveBeenCalledWith('600519')
    expect(mocks.listPdfFiles).toHaveBeenCalledTimes(2)
    expect(document.body.textContent).toContain('PDF 原件采集完成')

    wrapper.unmount()
  })

  it('collectPdfFiles 失败时显示错误条幅且不关闭抽屉', async () => {
    mocks.fetchFinancialReportDetail.mockResolvedValue(fullDetail())
    mocks.collectPdfFiles.mockRejectedValueOnce(new Error('PDF 服务不可用 (HTTP 502)'))

    const wrapper = mountDrawer()
    await flushPromises()

    const collectBtn = document.querySelector('[data-testid="fc-drawer-collect-pdf-btn"]')
    expect(collectBtn).toBeTruthy()
    collectBtn.dispatchEvent(new Event('click', { bubbles: true }))
    await flushPromises()

    const err = document.querySelector('[data-testid="fc-drawer-pdf-collect-error"]')
    expect(err).toBeTruthy()
    expect(err.textContent).toContain('PDF 服务不可用')
    expect(wrapper.emitted('close')).toBeFalsy()

    wrapper.unmount()
  })
})

describe('FinancialDetailDrawer - 关闭交互', () => {
  it('emits close on Esc, overlay click, and × button', async () => {
    mocks.fetchFinancialReportDetail.mockResolvedValue(fullDetail())
    const wrapper = mountDrawer()
    await flushPromises()

    const closeBtn = document.querySelector('.fc-drawer-close')
    expect(closeBtn).toBeTruthy()
    closeBtn.dispatchEvent(new Event('click', { bubbles: true }))
    await nextTick()

    const overlay = document.querySelector('.fc-drawer-overlay')
    overlay.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await nextTick()

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    await nextTick()

    const events = wrapper.emitted('close') || []
    expect(events.length).toBeGreaterThanOrEqual(3)
    wrapper.unmount()
  })

  it('adds keydown listener when visible flips to true and removes when flipped to false', async () => {
    mocks.fetchFinancialReportDetail.mockResolvedValue(fullDetail())
    const addSpy = vi.spyOn(window, 'addEventListener')
    const removeSpy = vi.spyOn(window, 'removeEventListener')
    const wrapper = mount(FinancialDetailDrawer, {
      attachTo: document.body,
      props: { visible: false, item: null, reportId: null }
    })
    expect(removeSpy).toHaveBeenCalledWith('keydown', expect.any(Function))

    addSpy.mockClear()
    removeSpy.mockClear()
    await wrapper.setProps({ visible: true, item: baseItem, reportId: baseItem.id })
    await flushPromises()
    expect(addSpy).toHaveBeenCalledWith('keydown', expect.any(Function))

    addSpy.mockClear()
    removeSpy.mockClear()
    await wrapper.setProps({ visible: false })
    expect(removeSpy).toHaveBeenCalledWith('keydown', expect.any(Function))

    wrapper.unmount()
  })
})

describe('FinancialDetailDrawer - V041-S5 PDF 对照接入', () => {
  it('listPdfFiles 返回 items 时渲染 PDF 入口（点击后挂载 ComparePane Modal）', async () => {
    mocks.fetchFinancialReportDetail.mockResolvedValueOnce(fullDetail())
    mocks.listPdfFiles.mockResolvedValueOnce({
      items: [
        {
          id: 'pdf-abc',
          symbol: '600519',
          reportPeriod: '2024-12-31',
          reportType: 'annual',
          fileName: '600519-2024-annual.pdf'
        }
      ],
      total: 1,
      page: 1,
      pageSize: 5
    })
    mocks.fetchPdfFileDetail.mockResolvedValueOnce({
      id: 'pdf-abc',
      fileName: '600519-2024-annual.pdf',
      reportPeriod: '2024-12-31',
      parseUnits: []
    })

    const wrapper = mountDrawer()
    await flushPromises()
    await flushPromises()

    expect(mocks.listPdfFiles).toHaveBeenCalledWith({
      symbol: '600519',
      reportType: 'annual',
      page: 1,
      pageSize: 5
    })
    // V042-R3 N3：抽屉里现在只渲染入口按钮 + meta 摘要（不内嵌 ComparePane）
    const summary = document.querySelector('[data-testid="fc-drawer-pdf-summary"]')
    expect(summary).toBeTruthy()
    expect(document.querySelector('[data-testid="fc-drawer-pdf-open-btn"]')).toBeTruthy()
    expect(document.querySelector('[data-testid="fc-drawer-pdf-empty"]')).toBeFalsy()
    // 没点击之前 ComparePane 不挂载（Modal 关闭）
    expect(document.querySelector('[data-testid="fc-compare-pane"]')).toBeFalsy()

    // 点击入口按钮 → Modal 弹出 → ComparePane 挂载
    const btn = document.querySelector('[data-testid="fc-drawer-pdf-open-btn"]')
    btn.dispatchEvent(new Event('click', { bubbles: true }))
    await flushPromises()
    expect(document.querySelector('[data-testid="fc-drawer-pdf-modal"]')).toBeTruthy()
    expect(document.querySelector('[data-testid="fc-compare-pane"]')).toBeTruthy()

    wrapper.unmount()
  })

  it('listPdfFiles 返回空时渲染空态文案，不渲染 ComparePane', async () => {
    mocks.fetchFinancialReportDetail.mockResolvedValueOnce(fullDetail())
    mocks.listPdfFiles.mockResolvedValueOnce({ items: [], total: 0, page: 1, pageSize: 5 })

    const wrapper = mountDrawer()
    await flushPromises()
    await flushPromises()

    expect(document.querySelector('[data-testid="fc-drawer-pdf-empty"]')).toBeTruthy()
    // 没有 PDF 时连入口按钮都不渲染
    expect(document.querySelector('[data-testid="fc-drawer-pdf-open-btn"]')).toBeFalsy()
    expect(document.querySelector('[data-testid="fc-compare-pane"]')).toBeFalsy()
    expect(mocks.fetchPdfFileDetail).not.toHaveBeenCalled()

    wrapper.unmount()
  })

  // V041-S8 NIT-4 防回归：reparse → @refresh="loadDetail" → resolvePdfFileId
  // 不能因为「先把 resolvedPdfId 置 null 再赋值」导致 ComparePane 重新拉一次 PDF 详情
  // （会冲掉 handleReparse 刚刚写入的 internalDetail，包括新的 lastReparsedAt）。
  it('reparse 后 resolvePdfFileId 命中相同 id 时不重复触发 fetchPdfFileDetail', async () => {
    mocks.fetchFinancialReportDetail.mockResolvedValue(fullDetail())
    mocks.listPdfFiles.mockResolvedValue({
      items: [
        {
          id: 'pdf-abc',
          symbol: '600519',
          reportPeriod: '2024-12-31',
          reportType: 'annual',
          fileName: '600519-2024-annual.pdf'
        }
      ],
      total: 1,
      page: 1,
      pageSize: 5
    })
    mocks.fetchPdfFileDetail.mockResolvedValue({
      id: 'pdf-abc',
      fileName: '600519-2024-annual.pdf',
      reportPeriod: '2024-12-31',
      lastReparsedAt: '2026-04-22T08:00:00Z',
      parseUnits: []
    })
    mocks.reparsePdfFile.mockResolvedValueOnce({
      success: true,
      detail: {
        id: 'pdf-abc',
        fileName: '600519-2024-annual.pdf',
        reportPeriod: '2024-12-31',
        lastReparsedAt: '2026-04-22T12:00:00Z',
        parseUnits: []
      }
    })

    const wrapper = mountDrawer()
    await flushPromises()
    await flushPromises()
    expect(mocks.fetchPdfFileDetail).not.toHaveBeenCalled()  // V042-R3 N3：抽屉里不再内嵌 ComparePane，需要点开 Modal 才会拉 detail

    // 点开 PDF Modal 才挂载 ComparePane（drawer 现在只是入口）
    const openBtn = document.querySelector('[data-testid="fc-drawer-pdf-open-btn"]')
    expect(openBtn).toBeTruthy()
    openBtn.dispatchEvent(new Event('click', { bubbles: true }))
    await flushPromises()
    await flushPromises()
    expect(mocks.fetchPdfFileDetail).toHaveBeenCalledTimes(1)

    // 模拟用户从 ComparePane 触发 reparse → emit('refresh', detail) → onPdfModalRefresh
    // 通过组件 API 直接触发以绕过 PDF iframe 与 jsdom 的兼容问题
    const pane = wrapper.findComponent({ name: 'FinancialReportComparePane' })
    expect(pane.exists()).toBe(true)
    await pane.vm.$emit('refresh', { id: 'pdf-abc' })
    // onPdfModalRefresh → loadDetail → resolvePdfFileId 异步
    await flushPromises()
    await flushPromises()

    // 关键断言：resolvedPdfId 没有被中间态 null 抖动，导致 ComparePane 重新挂载/拉取详情
    expect(mocks.fetchPdfFileDetail).toHaveBeenCalledTimes(1)
    wrapper.unmount()
  })
})
