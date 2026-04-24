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

const toastMock = {
  success: vi.fn(),
  error: vi.fn(),
  info: vi.fn(),
  warning: vi.fn()
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

vi.mock('../../../composables/useToast.js', () => ({
  useToast: () => toastMock
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
  toastMock.success.mockReset()
  toastMock.error.mockReset()
  toastMock.info.mockReset()
  toastMock.warning.mockReset()
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

  it('reparse 失败（success=false）→ 显示错误，但仍刷新 detail（V042-R3.1：错误与时间字段解耦）', async () => {
    mocks.reparsePdfFile.mockResolvedValueOnce({
      success: false,
      error: '解析超时',
      detail: null
    })
    // mount 一次 + reparse 后重拉一次（默认返回 baseDetail）
    mocks.fetchPdfFileDetail.mockResolvedValue(baseDetail())

    const wrapper = mountPane({ pdfFileDetail: null })
    await flushPromises()
    const initialUnits = wrapper.findComponent({ name: 'FinancialPdfParsePreview' }).props('parseUnits')
    expect(initialUnits).toHaveLength(2)

    await wrapper.find('[data-testid="fc-compare-tab-voting"]').trigger('click')
    await nextTick()
    await wrapper.find('[data-testid="fc-pdf-voting-reparse-btn"]').trigger('click')
    await flushPromises()

    // 错误条幅 + toast 仍要显示
    const banner = wrapper.find('[data-testid="fc-compare-error"]')
    expect(banner.exists()).toBe(true)
    expect(banner.text()).toContain('解析超时')
    expect(toastMock.error).toHaveBeenCalled()
    // V042-R3.1：失败也要刷 detail，emit refresh，让父级 picker / 时间字段同步
    expect(wrapper.emitted('refresh')).toBeTruthy()

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

// ========================================================================
// V042-P0/P1 修复用例
// ========================================================================
describe('FinancialReportComparePane - V042 修复', () => {
  // ── B1: 智能默认 + picker ────────────────────────────────────────────
  it('V042-P0-A: 未传 pdfFileId 时按 fieldCount + 摘要降权智能选 PDF', async () => {
    mocks.fetchPdfFileDetail.mockResolvedValue(baseDetail({ id: 'pdf-main' }))
    const list = [
      // 摘要 PDF：fieldCount=0
      { id: 'pdf-summary', fileName: '贵州茅台2025年年度报告摘要.pdf', fieldCount: 0, lastParsedAt: '2026-04-22T10:00:00Z' },
      // 主报告：fieldCount=10
      { id: 'pdf-main', fileName: '贵州茅台2025年年度报告.pdf', fieldCount: 10, lastParsedAt: '2026-04-21T10:00:00Z' },
      // 英文版：fieldCount=8
      { id: 'pdf-en', fileName: '600519-2025-Annual-English.pdf', fieldCount: 8, lastParsedAt: '2026-04-22T11:00:00Z' }
    ]
    const wrapper = mount(FinancialReportComparePane, {
      props: { pdfFileId: null, pdfFiles: list, pdfFileDetail: null },
      attachTo: document.body
    })
    await flushPromises()

    // 自选 pdf-main（fieldCount 最大且非摘要/英文）
    expect(mocks.fetchPdfFileDetail).toHaveBeenCalledWith('pdf-main')
    wrapper.unmount()
  })

  it('V042-P0-A: 传入 pdfFileId 时尊重外部选择，picker 仍渲染', async () => {
    mocks.fetchPdfFileDetail.mockResolvedValue(baseDetail({ id: 'pdf-summary' }))
    const list = [
      { id: 'pdf-summary', fileName: '摘要.pdf', fieldCount: 0 },
      { id: 'pdf-main', fileName: '主报告.pdf', fieldCount: 10 }
    ]
    const wrapper = mount(FinancialReportComparePane, {
      props: { pdfFileId: 'pdf-summary', pdfFiles: list, pdfFileDetail: null },
      attachTo: document.body
    })
    await flushPromises()

    expect(mocks.fetchPdfFileDetail).toHaveBeenCalledWith('pdf-summary')
    // picker 渲染了两个候选
    const picker = wrapper.find('[data-testid="fc-compare-picker-select"]')
    expect(picker.exists()).toBe(true)
    expect(wrapper.findAll('option')).toHaveLength(2)

    wrapper.unmount()
  })

  it('V042-P0-A: picker 切换 PDF → 重拉 detail 并 emit pdfChange', async () => {
    mocks.fetchPdfFileDetail
      .mockResolvedValueOnce(baseDetail({ id: 'pdf-main' }))
      .mockResolvedValueOnce(baseDetail({ id: 'pdf-summary', lastReparsedAt: '2026-04-23T01:00:00Z' }))
    const list = [
      { id: 'pdf-main', fileName: '主报告.pdf', fieldCount: 10 },
      { id: 'pdf-summary', fileName: '摘要.pdf', fieldCount: 0 }
    ]
    const wrapper = mount(FinancialReportComparePane, {
      props: { pdfFileId: 'pdf-main', pdfFiles: list, pdfFileDetail: null },
      attachTo: document.body
    })
    await flushPromises()

    const select = wrapper.find('[data-testid="fc-compare-picker-select"]')
    await select.setValue('pdf-summary')
    await flushPromises()

    expect(mocks.fetchPdfFileDetail).toHaveBeenLastCalledWith('pdf-summary')
    expect(wrapper.emitted('pdfChange')).toBeTruthy()
    expect(wrapper.emitted('pdfChange')[0]).toEqual(['pdf-summary'])

    wrapper.unmount()
  })

  it('V042-P0-A: pdfFiles 只有 1 项时不渲染 picker', async () => {
    const list = [{ id: 'pdf-only', fileName: '唯一.pdf', fieldCount: 5 }]
    const wrapper = mount(FinancialReportComparePane, {
      props: { pdfFileId: 'pdf-only', pdfFiles: list, pdfFileDetail: baseDetail() },
      attachTo: document.body
    })
    await flushPromises()
    expect(wrapper.find('[data-testid="fc-compare-picker-select"]').exists()).toBe(false)
    wrapper.unmount()
  })

  // ── B4: reparse 后主动重拉 detail，lastReparsedAt 刷新 ─────────────
  it('V042-P0-D: reparse 成功后主动调 fetchPdfFileDetail 重拉，lastReparsedAt 用最新值', async () => {
    const initial = baseDetail({ lastReparsedAt: '2026-04-22T08:30:00Z' })
    const newStamp = '2026-04-23T05:00:00Z'
    mocks.fetchPdfFileDetail
      .mockResolvedValueOnce(initial)                                // mount
      .mockResolvedValueOnce(baseDetail({ lastReparsedAt: newStamp })) // reparse 后重拉

    // reparse 返回的 detail 故意是「旧」的 stamp，验证前端拿的是重拉的新值
    mocks.reparsePdfFile.mockResolvedValueOnce({
      success: true,
      detail: baseDetail({ lastReparsedAt: '2026-04-22T08:30:00Z' })
    })

    const wrapper = mountPane({ pdfFileDetail: null })
    await flushPromises()
    await wrapper.find('[data-testid="fc-compare-tab-voting"]').trigger('click')
    await nextTick()

    await wrapper.find('[data-testid="fc-pdf-voting-reparse-btn"]').trigger('click')
    await flushPromises()

    expect(mocks.fetchPdfFileDetail).toHaveBeenCalledTimes(2)
    const votingPanel = wrapper.findComponent({ name: 'FinancialPdfVotingPanel' })
    expect(votingPanel.props('detail')?.lastReparsedAt).toBe(newStamp)

    wrapper.unmount()
  })

  // ── M1: 顶部按钮 loading + toast ──────────────────────────────────
  it('V042-P1-A: 顶部「重新解析 PDF」按钮，reparsing 中切换文案 + disabled', async () => {
    let resolveReparse
    const pending = new Promise((r) => { resolveReparse = r })
    mocks.reparsePdfFile.mockReturnValueOnce(pending)
    mocks.fetchPdfFileDetail.mockResolvedValue(baseDetail())

    const wrapper = mountPane()
    await flushPromises()

    const topBtn = wrapper.find('[data-testid="fc-compare-reparse-btn"]')
    expect(topBtn.exists()).toBe(true)
    expect(topBtn.text()).toContain('重新解析 PDF')
    expect(topBtn.attributes('disabled')).toBeUndefined()

    await topBtn.trigger('click')
    await nextTick()
    expect(topBtn.text()).toContain('解析中')
    expect(topBtn.attributes('disabled')).toBeDefined()
    // VotingPanel 内按钮也共享 reparsing
    await wrapper.find('[data-testid="fc-compare-tab-voting"]').trigger('click')
    await nextTick()
    expect(wrapper.find('[data-testid="fc-pdf-voting-reparse-btn"]').attributes('disabled')).toBeDefined()

    resolveReparse({ success: true, detail: baseDetail() })
    await flushPromises()
    expect(topBtn.text()).toContain('重新解析 PDF')
    expect(toastMock.success).toHaveBeenCalled()

    wrapper.unmount()
  })

  it('V042-P1-A: reparse 失败时 toast.error 触发', async () => {
    mocks.reparsePdfFile.mockResolvedValueOnce({ success: false, error: '解析超时', detail: null })
    mocks.fetchPdfFileDetail.mockResolvedValue(baseDetail())

    const wrapper = mountPane({ pdfFileDetail: null })
    await flushPromises()
    await wrapper.find('[data-testid="fc-compare-reparse-btn"]').trigger('click')
    await flushPromises()

    expect(toastMock.error).toHaveBeenCalled()
    expect(toastMock.error.mock.calls[0][0]).toContain('解析超时')

    wrapper.unmount()
  })

  // V042-R3 M1：reparse 中两个按钮都要有 spinner（视觉反馈）
  it('V042-R3 M1: reparse 中顶部 + VotingPanel 按钮渲染 spinner', async () => {
    let resolveReparse
    const pending = new Promise((r) => { resolveReparse = r })
    mocks.reparsePdfFile.mockReturnValueOnce(pending)
    mocks.fetchPdfFileDetail.mockResolvedValue(baseDetail())

    const wrapper = mountPane()
    await flushPromises()

    const topBtn = wrapper.find('[data-testid="fc-compare-reparse-btn"]')
    expect(wrapper.find('[data-testid="fc-compare-reparse-spinner"]').exists()).toBe(false)
    await topBtn.trigger('click')
    await nextTick()

    expect(wrapper.find('[data-testid="fc-compare-reparse-spinner"]').exists()).toBe(true)
    expect(topBtn.classes()).toContain('fc-compare-reparse-btn--loading')

    await wrapper.find('[data-testid="fc-compare-tab-voting"]').trigger('click')
    await nextTick()
    expect(wrapper.find('[data-testid="fc-pdf-voting-reparse-spinner"]').exists()).toBe(true)

    resolveReparse({ success: true, detail: baseDetail() })
    await flushPromises()
    // 完成后 spinner 消失
    expect(wrapper.find('[data-testid="fc-compare-reparse-spinner"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="fc-pdf-voting-reparse-spinner"]').exists()).toBe(false)

    wrapper.unmount()
  })

  // V042-R3 B4：reparse 后 VotingPanel 渲染的 DOM 文本要反映新 lastReparsedAt
  // （不仅是 props 层面）。这是 R2 失败的核心：用户看的是 DOM，不是 props。
  it('V042-R3 B4: reparse 后 VotingPanel 渲染的「最近重解析」时间文本变化', async () => {
    const oldStamp = '2026-04-22T08:30:00Z'
    const newStamp = '2026-04-23T05:00:00Z'
    mocks.fetchPdfFileDetail
      .mockResolvedValueOnce(baseDetail({ lastReparsedAt: oldStamp }))   // mount
      .mockResolvedValueOnce(baseDetail({ lastReparsedAt: newStamp }))   // reparse 后重拉
    mocks.reparsePdfFile.mockResolvedValueOnce({
      success: true,
      detail: baseDetail({ lastReparsedAt: oldStamp })  // gateway 返回旧值，验证前端拿重拉值
    })

    const wrapper = mountPane({ pdfFileDetail: null })
    await flushPromises()
    await wrapper.find('[data-testid="fc-compare-tab-voting"]').trigger('click')
    await nextTick()

    const oldFormatted = new Date(oldStamp).toLocaleString()
    const newFormatted = new Date(newStamp).toLocaleString()

    // 切到 voting tab 后能看到旧时间戳
    let votingDom = wrapper.find('[data-testid="fc-pdf-voting-panel"]')
    expect(votingDom.text()).toContain(oldFormatted)
    expect(votingDom.text()).not.toContain(newFormatted)

    await wrapper.find('[data-testid="fc-pdf-voting-reparse-btn"]').trigger('click')
    await flushPromises()
    await nextTick()

    votingDom = wrapper.find('[data-testid="fc-pdf-voting-panel"]')
    // DOM 文本必须反映新时间戳（R2 实测失败：API 返回新值，UI 仍是旧值）
    expect(votingDom.text()).toContain(newFormatted)
    expect(votingDom.text()).not.toContain(oldFormatted)

    wrapper.unmount()
  })

  // V042-R3.1：reparse 失败 / parseUnits 为空时，「最近重解析」依然要刷新到新时间戳
  // 后端语义：哪怕解析失败（success=false 或 parseError 非空），lastReparsedAt 已更新。
  // 前端不能因为「解析失败」就跳过 detail 刷新，导致 UI 时间停留在旧值。
  it('V042-R3.1: reparse 返回 success=false + 空 parseUnits 时，DOM 依然刷新到新时间戳', async () => {
    const oldStamp = '2026-04-22T08:30:00Z'
    const newStamp = '2026-04-23T05:00:00Z'
    mocks.fetchPdfFileDetail
      .mockResolvedValueOnce(baseDetail({ lastReparsedAt: oldStamp, parseUnits: [] })) // mount
      .mockResolvedValueOnce(baseDetail({                                              // reparse 后重拉：新时间 + 空 parseUnits
        lastReparsedAt: newStamp,
        parseUnits: [],
        fieldCount: 0,
        lastError: 'PDF 文本中未找到可解析的财务数据'
      }))
    mocks.reparsePdfFile.mockResolvedValueOnce({
      success: false,
      error: 'PDF 文本中未找到可解析的财务数据',
      detail: baseDetail({ lastReparsedAt: newStamp, parseUnits: [], fieldCount: 0 })
    })

    const wrapper = mountPane({ pdfFileDetail: null })
    await flushPromises()
    await wrapper.find('[data-testid="fc-compare-tab-voting"]').trigger('click')
    await nextTick()

    const oldFormatted = new Date(oldStamp).toLocaleString()
    const newFormatted = new Date(newStamp).toLocaleString()

    let votingDom = wrapper.find('[data-testid="fc-pdf-voting-panel"]')
    expect(votingDom.text()).toContain(oldFormatted)

    await wrapper.find('[data-testid="fc-pdf-voting-reparse-btn"]').trigger('click')
    await flushPromises()
    await nextTick()

    votingDom = wrapper.find('[data-testid="fc-pdf-voting-panel"]')
    // 关键断言：哪怕后端语义是「解析失败 / 空 parseUnits」，DOM 上的「最近重解析」依然要刷到新时间戳
    expect(votingDom.text()).toContain(newFormatted)
    expect(votingDom.text()).not.toContain(oldFormatted)
    // 错误展示走 toast，不阻断时间字段刷新
    expect(toastMock.error).toHaveBeenCalled()
    expect(toastMock.error.mock.calls[0][0]).toContain('PDF 文本中未找到可解析的财务数据')
    // 父组件依然会收到 refresh 事件（带最新 detail），下游 picker / list 才能刷新
    expect(wrapper.emitted('refresh')).toBeTruthy()
    const lastEmitted = wrapper.emitted('refresh').at(-1)?.[0]
    expect(lastEmitted?.lastReparsedAt).toBe(newStamp)

    wrapper.unmount()
  })

  // V042-R3.1：reparse 重拉 detail 失败时，必须 fallback 到 result.detail，仍要刷新时间字段
  it('V042-R3.1: reparse 后 fetchPdfFileDetail 抛错时 fallback 到 result.detail 刷新时间', async () => {
    const oldStamp = '2026-04-22T08:30:00Z'
    const newStamp = '2026-04-23T05:00:00Z'
    mocks.fetchPdfFileDetail
      .mockResolvedValueOnce(baseDetail({ lastReparsedAt: oldStamp })) // mount
      .mockRejectedValueOnce(new Error('网络抖动'))                    // reparse 后重拉失败
    mocks.reparsePdfFile.mockResolvedValueOnce({
      success: false,
      error: 'parse 阶段超时',
      detail: baseDetail({ lastReparsedAt: newStamp, parseUnits: [], fieldCount: 0 })
    })

    const wrapper = mountPane({ pdfFileDetail: null })
    await flushPromises()
    await wrapper.find('[data-testid="fc-compare-tab-voting"]').trigger('click')
    await nextTick()

    await wrapper.find('[data-testid="fc-pdf-voting-reparse-btn"]').trigger('click')
    await flushPromises()
    await nextTick()

    const newFormatted = new Date(newStamp).toLocaleString()
    const votingDom = wrapper.find('[data-testid="fc-pdf-voting-panel"]')
    expect(votingDom.text()).toContain(newFormatted)
    expect(toastMock.error).toHaveBeenCalled()

    wrapper.unmount()
  })

  // ── M2: StageTimeline tab ────────────────────────────────────────
  it('V042-P1-B: 「解析阶段」Tab 渲染 5 阶段 timeline', async () => {
    const stageLogs = [
      { stage: 'download', status: 'success', durationMs: 1200, message: '' },
      { stage: 'extract', status: 'success', durationMs: 2400, message: '' },
      { stage: 'vote', status: 'success', durationMs: 80, message: '' },
      { stage: 'parse', status: 'success', durationMs: 5600, message: '' },
      { stage: 'persist', status: 'success', durationMs: 120, message: '' }
    ]
    const wrapper = mountPane({
      pdfFileDetail: baseDetail({ stageLogs })
    })
    await flushPromises()

    const stagesTab = wrapper.find('[data-testid="fc-compare-tab-stages"]')
    expect(stagesTab.exists()).toBe(true)
    await stagesTab.trigger('click')
    await nextTick()

    expect(wrapper.find('[data-testid="fc-pdf-stage-timeline"]').exists()).toBe(true)
    // 5 个 stage item 都渲染
    for (const stage of ['download', 'extract', 'vote', 'parse', 'persist']) {
      expect(wrapper.find(`[data-testid="fc-pdf-stage-item-${stage}"]`).exists()).toBe(true)
    }
    expect(wrapper.find('[data-testid="fc-pdf-stage-success-count"]').text()).toBe('5')

    wrapper.unmount()
  })
})
