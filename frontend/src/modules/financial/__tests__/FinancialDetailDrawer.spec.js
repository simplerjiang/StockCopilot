/**
 * @vitest-environment jsdom
 * @vitest-environment-options {"url":"http://localhost/"}
 */
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { nextTick } from 'vue'

const mocks = {
  fetchFinancialReportDetail: vi.fn(),
  recollectFinancialReport: vi.fn()
}

vi.mock('../financialApi.js', () => ({
  fetchFinancialReportDetail: (...args) => mocks.fetchFinancialReportDetail(...args),
  recollectFinancialReport: (...args) => mocks.recollectFinancialReport(...args)
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
    expect(text).toContain('Report ID')
    // 三表 label
    expect(text).toContain('总资产')
    expect(text).toContain('股东权益合计')
    expect(text).toContain('营业收入')
    expect(text).toContain('净利润')
    expect(text).toContain('经营活动现金流净额')
    expect(text).toContain('期末现金及现金等价物余额')
    // 数字格式化
    expect(text).toContain('1,000,000')
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

  it('PDF 占位区显示提示文案', async () => {
    mocks.fetchFinancialReportDetail.mockResolvedValueOnce(fullDetail())
    const wrapper = mountDrawer()
    await flushPromises()
    expect(document.body.textContent).toContain('PDF 原件预览')
    expect(document.body.textContent).toContain('v0.4.1')
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
    mocks.recollectFinancialReport.mockResolvedValueOnce({ success: true })

    const wrapper = mountDrawer()
    await flushPromises()
    expect(mocks.fetchFinancialReportDetail).toHaveBeenCalledTimes(1)

    const recollectBtn = Array.from(document.querySelectorAll('button'))
      .find(b => b.textContent.includes('重新采集'))
    expect(recollectBtn).toBeTruthy()
    recollectBtn.dispatchEvent(new Event('click', { bubbles: true }))
    await flushPromises()

    expect(mocks.recollectFinancialReport).toHaveBeenCalledWith('600519')
    expect(document.body.textContent).toContain('已重新采集，刷新中')
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
