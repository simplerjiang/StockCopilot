/**
 * @vitest-environment jsdom
 */
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { nextTick } from 'vue'

const mocks = {
  fetchPdfFileDetail: vi.fn(),
  reparsePdfFile: vi.fn(),
  buildPdfFileContentUrl: vi.fn((id) => `/api/stocks/financial/pdf-files/${id}/content`)
}

vi.mock('../financialApi.js', () => ({
  fetchPdfFileDetail: (...args) => mocks.fetchPdfFileDetail(...args),
  reparsePdfFile: (...args) => mocks.reparsePdfFile(...args),
  buildPdfFileContentUrl: (...args) => mocks.buildPdfFileContentUrl(...args),
  // 防 import 时崩
  fetchFinancialReportDetail: vi.fn(),
  recollectFinancialReport: vi.fn(),
  listPdfFiles: vi.fn()
}))

import FinancialReportComparePane from '../FinancialReportComparePane.vue'

const mkUnit = (overrides = {}) => ({
  blockKind: 'table',
  pageStart: 1,
  pageEnd: 1,
  sectionName: null,
  fieldCount: 0,
  snippet: null,
  ...overrides
})

const baseDetail = (overrides = {}) => ({
  id: 'pdf-1',
  fileName: '600519-2024-annual.pdf',
  reportPeriod: '2024-12-31',
  extractor: 'pdfplumber',
  voteConfidence: 'high',
  fieldCount: 5,
  lastError: null,
  lastParsedAt: '2026-04-20T10:00:00Z',
  lastReparsedAt: '2026-04-22T08:30:00Z',
  parseUnits: [
    mkUnit({ pageStart: 1, pageEnd: 1 }),
    mkUnit({ blockKind: 'narrative_section', pageStart: 3, pageEnd: 4 })
  ],
  ...overrides
})

beforeEach(() => {
  mocks.fetchPdfFileDetail.mockReset()
  mocks.reparsePdfFile.mockReset()
  mocks.buildPdfFileContentUrl.mockClear()
})

const mountPane = (props = {}) => mount(FinancialReportComparePane, {
  props: {
    pdfFileId: 'pdf-1',
    pdfFileDetail: baseDetail(),
    ...props
  },
  attachTo: document.body
})

describe('FinancialReportComparePane - V041-S5', () => {
  it('初始渲染左 PdfViewer + 右 ParsePreview 双栏', async () => {
    const wrapper = mountPane()
    await flushPromises()

    expect(wrapper.find('[data-testid="fc-compare-left"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="fc-compare-right"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="fc-pdf-iframe"]').exists()).toBe(true)
    // 默认右栏为「解析单元」
    expect(wrapper.find('[data-testid="fc-pdf-parse-preview"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="fc-compare-tab-parse"]').classes()).toContain('fc-compare-tab--active')
    // 文案明确「重新解析 PDF」与「重新采集报告」区分
    expect(wrapper.text()).toContain('重新解析 PDF')

    wrapper.unmount()
  })

  it('点击右栏 Tab 切换到投票信息', async () => {
    const wrapper = mountPane()
    await flushPromises()

    await wrapper.find('[data-testid="fc-compare-tab-voting"]').trigger('click')
    await nextTick()

    expect(wrapper.find('[data-testid="fc-compare-tab-voting"]').classes()).toContain('fc-compare-tab--active')
    // ParsePreview 仍然挂载（v-show），但 VotingPanel 也存在
    expect(wrapper.find('[data-testid="fc-pdf-voting-panel"]').exists()).toBe(true)
    // VotingPanel 内的 reparse 按钮可见
    expect(wrapper.find('[data-testid="fc-pdf-voting-reparse-btn"]').exists()).toBe(true)

    wrapper.unmount()
  })

  it('ParsePreview emit jump-to-page → viewerJumpPage 状态更新并透传到 PdfViewer', async () => {
    const wrapper = mountPane()
    await flushPromises()

    const parsePreview = wrapper.findComponent({ name: 'FinancialPdfParsePreview' })
    expect(parsePreview.exists()).toBe(true)
    parsePreview.vm.$emit('jump-to-page', 7)
    await nextTick()

    const viewer = wrapper.findComponent({ name: 'FinancialPdfViewer' })
    expect(viewer.exists()).toBe(true)
    expect(viewer.props('page')).toBe(7)

    wrapper.unmount()
  })

  it('VotingPanel emit reparse → 调用 reparsePdfFile 且 reparsing 状态切换', async () => {
    let resolveReparse
    const pending = new Promise((r) => { resolveReparse = r })
    mocks.reparsePdfFile.mockReturnValueOnce(pending)

    const wrapper = mountPane()
    await flushPromises()
    await wrapper.find('[data-testid="fc-compare-tab-voting"]').trigger('click')
    await nextTick()

    await wrapper.find('[data-testid="fc-pdf-voting-reparse-btn"]').trigger('click')
    await nextTick()

    expect(mocks.reparsePdfFile).toHaveBeenCalledWith('pdf-1')
    // reparsing 中按钮文案变化
    expect(wrapper.find('[data-testid="fc-pdf-voting-reparse-btn"]').text()).toContain('解析中')

    resolveReparse({ success: true, detail: baseDetail({ parseUnits: [mkUnit({ pageStart: 9, pageEnd: 9 })] }) })
    await flushPromises()
    expect(wrapper.find('[data-testid="fc-pdf-voting-reparse-btn"]').text()).toContain('重新解析')

    wrapper.unmount()
  })

  it('reparse 成功 → internalDetail 替换 → ParsePreview 接收新 parseUnits（数量变化）', async () => {
    // 初始 detail 由父级传入，2 个 parseUnits
    const wrapper = mountPane()
    await flushPromises()
    let parsePreview = wrapper.findComponent({ name: 'FinancialPdfParsePreview' })
    expect(parsePreview.props('parseUnits')).toHaveLength(2)

    // reparse 返回新 detail，5 个 parseUnits
    const newDetail = baseDetail({
      parseUnits: [
        mkUnit({ pageStart: 1, pageEnd: 1 }),
        mkUnit({ pageStart: 2, pageEnd: 2 }),
        mkUnit({ pageStart: 3, pageEnd: 3 }),
        mkUnit({ pageStart: 4, pageEnd: 4 }),
        mkUnit({ pageStart: 5, pageEnd: 5 })
      ]
    })
    mocks.reparsePdfFile.mockResolvedValueOnce({ success: true, detail: newDetail })

    // 移除外部 detail，让组件以 internalDetail 为准
    await wrapper.setProps({ pdfFileDetail: null })
    await flushPromises()
    await wrapper.find('[data-testid="fc-compare-tab-voting"]').trigger('click')
    await nextTick()
    await wrapper.find('[data-testid="fc-pdf-voting-reparse-btn"]').trigger('click')
    await flushPromises()

    parsePreview = wrapper.findComponent({ name: 'FinancialPdfParsePreview' })
    expect(parsePreview.props('parseUnits')).toHaveLength(5)
    // emit refresh
    expect(wrapper.emitted('refresh')).toBeTruthy()
    expect(wrapper.emitted('refresh')[0][0]).toEqual(newDetail)

    wrapper.unmount()
  })

  it('reparse 失败（success=false）→ 显示 error，不替换 detail', async () => {
    mocks.reparsePdfFile.mockResolvedValueOnce({
      success: false,
      error: '解析超时',
      detail: null
    })
    mocks.fetchPdfFileDetail.mockResolvedValue(baseDetail())

    const wrapper = mountPane({ pdfFileDetail: null })
    await flushPromises()
    const initialUnits = wrapper.findComponent({ name: 'FinancialPdfParsePreview' }).props('parseUnits')
    expect(initialUnits).toHaveLength(2)

    await wrapper.find('[data-testid="fc-compare-tab-voting"]').trigger('click')
    await nextTick()
    await wrapper.find('[data-testid="fc-pdf-voting-reparse-btn"]').trigger('click')
    await flushPromises()

    // detail 未被替换：parseUnits 数量不变
    const afterUnits = wrapper.findComponent({ name: 'FinancialPdfParsePreview' }).props('parseUnits')
    expect(afterUnits).toHaveLength(2)
    // 错误条幅显示
    const banner = wrapper.find('[data-testid="fc-compare-error"]')
    expect(banner.exists()).toBe(true)
    expect(banner.text()).toContain('解析超时')
    // 没有 emit refresh
    expect(wrapper.emitted('refresh')).toBeFalsy()

    wrapper.unmount()
  })

  it('未传 pdfFileDetail 时调用 fetchPdfFileDetail 自取', async () => {
    mocks.fetchPdfFileDetail.mockResolvedValueOnce(baseDetail())
    const wrapper = mountPane({ pdfFileDetail: null })
    await flushPromises()
    expect(mocks.fetchPdfFileDetail).toHaveBeenCalledWith('pdf-1')
    expect(wrapper.find('[data-testid="fc-pdf-iframe"]').exists()).toBe(true)
    wrapper.unmount()
  })

  // V041-S8 NIT-4 防回归：reparse 成功 → VotingPanel 的 lastReparsedAt 必须刷新
  it('reparse 成功 → VotingPanel.detail.lastReparsedAt 同步刷新', async () => {
    mocks.fetchPdfFileDetail.mockResolvedValueOnce(baseDetail())
    const wrapper = mountPane({ pdfFileDetail: null })
    await flushPromises()

    // 切到投票信息 tab
    await wrapper.find('[data-testid="fc-compare-tab-voting"]').trigger('click')
    await nextTick()

    const oldStamp = '2026-04-22T08:30:00Z'
    const newStamp = '2026-04-22T11:45:30Z'
    let votingPanel = wrapper.findComponent({ name: 'FinancialPdfVotingPanel' })
    expect(votingPanel.props('detail')?.lastReparsedAt).toBe(oldStamp)

    mocks.reparsePdfFile.mockResolvedValueOnce({
      success: true,
      detail: baseDetail({ lastReparsedAt: newStamp })
    })

    await wrapper.find('[data-testid="fc-pdf-voting-reparse-btn"]').trigger('click')
    await flushPromises()

    votingPanel = wrapper.findComponent({ name: 'FinancialPdfVotingPanel' })
    expect(votingPanel.props('detail')?.lastReparsedAt).toBe(newStamp)

    wrapper.unmount()
  })
})
