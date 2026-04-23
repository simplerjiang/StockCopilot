/**
 * @vitest-environment jsdom
 */
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'

const mocks = {
  reparsePdfFile: vi.fn()
}

vi.mock('../financialApi.js', () => ({
  reparsePdfFile: (...args) => mocks.reparsePdfFile(...args),
  // 其它 export 用 noop 占位（组件并不会调用，只防 import 时崩）
  fetchFinancialReportDetail: vi.fn(),
  recollectFinancialReport: vi.fn(),
  listPdfFiles: vi.fn(),
  fetchPdfFileDetail: vi.fn(),
  buildPdfFileContentUrl: vi.fn()
}))

import FinancialPdfVotingPanel from '../FinancialPdfVotingPanel.vue'

const baseDetail = {
  id: 'pdf-1',
  extractor: 'pdfplumber',
  voteConfidence: 'high',
  fieldCount: 42,
  lastError: null,
  lastParsedAt: '2026-04-20T10:00:00Z',
  lastReparsedAt: '2026-04-22T08:30:00Z'
}

beforeEach(() => {
  mocks.reparsePdfFile.mockReset()
})

describe('FinancialPdfVotingPanel', () => {
  it('渲染 extractor + voteConfidence + fieldCount', () => {
    const wrapper = mount(FinancialPdfVotingPanel, { props: { detail: baseDetail } })
    expect(wrapper.find('[data-testid="fc-pdf-voting-extractor"]').text()).toBe('pdfplumber')
    expect(wrapper.find('[data-testid="fc-pdf-voting-confidence"]').text()).toBe('high')
    expect(wrapper.find('[data-testid="fc-pdf-voting-field-count"]').text()).toBe('42')
    expect(wrapper.text()).toContain('解析投票')
  })

  it('extractor / voteConfidence 缺失时回退「未知」', () => {
    const wrapper = mount(FinancialPdfVotingPanel, {
      props: { detail: { ...baseDetail, extractor: null, voteConfidence: '' } }
    })
    expect(wrapper.find('[data-testid="fc-pdf-voting-extractor"]').text()).toBe('未知')
    expect(wrapper.find('[data-testid="fc-pdf-voting-confidence"]').text()).toBe('未知')
  })

  it('lastError 非空时显示错误条幅', () => {
    const wrapper = mount(FinancialPdfVotingPanel, {
      props: { detail: { ...baseDetail, lastError: '解析超时：30s' } }
    })
    const banner = wrapper.find('[data-testid="fc-pdf-voting-error"]')
    expect(banner.exists()).toBe(true)
    expect(banner.text()).toContain('解析超时：30s')
  })

  it('lastError 为空时不渲染错误条幅', () => {
    const wrapper = mount(FinancialPdfVotingPanel, { props: { detail: baseDetail } })
    expect(wrapper.find('[data-testid="fc-pdf-voting-error"]').exists()).toBe(false)
  })

  it('点击重新解析触发 reparse emit 且不直接调 API', async () => {
    const wrapper = mount(FinancialPdfVotingPanel, { props: { detail: baseDetail } })
    await wrapper.find('[data-testid="fc-pdf-voting-reparse-btn"]').trigger('click')
    expect(wrapper.emitted('reparse')).toBeTruthy()
    expect(wrapper.emitted('reparse').length).toBe(1)
    expect(mocks.reparsePdfFile).not.toHaveBeenCalled()
  })

  it('reparsing=true 时按钮禁用且文案变化', async () => {
    const wrapper = mount(FinancialPdfVotingPanel, {
      props: { detail: baseDetail, reparsing: true }
    })
    const btn = wrapper.find('[data-testid="fc-pdf-voting-reparse-btn"]')
    expect(btn.attributes('disabled')).toBeDefined()
    expect(btn.text()).toBe('解析中…')
    await btn.trigger('click')
    expect(wrapper.emitted('reparse')).toBeFalsy()
  })

  it('renders candidates when votingCandidates are present', () => {
    const detail = {
      ...baseDetail,
      votingCandidates: [
        { extractor: 'PdfPig', success: true, pageCount: 120, sampleText: '示例文本', isWinner: true },
        { extractor: 'iText7', success: true, pageCount: 120, sampleText: '示例文本2', isWinner: false },
        { extractor: 'Docnet', success: false, pageCount: 0, sampleText: null, isWinner: false }
      ]
    }
    const wrapper = mount(FinancialPdfVotingPanel, { props: { detail } })
    const candidateEls = wrapper.findAll('[data-testid="fc-voting-candidate"]')
    expect(candidateEls.length).toBe(3)
    expect(wrapper.find('[data-testid="fc-voting-candidates"]').exists()).toBe(true)
  })

  it('shows winner badge for isWinner candidate', () => {
    const detail = {
      ...baseDetail,
      votingCandidates: [
        { extractor: 'PdfPig', success: true, pageCount: 120, sampleText: null, isWinner: true },
        { extractor: 'iText7', success: true, pageCount: 120, sampleText: null, isWinner: false }
      ]
    }
    const wrapper = mount(FinancialPdfVotingPanel, { props: { detail } })
    expect(wrapper.find('[data-testid="fc-voting-winner-badge"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="fc-voting-winner-badge"]').text()).toContain('胜出')
  })

  it('shows failed badge for !success candidate', () => {
    const detail = {
      ...baseDetail,
      votingCandidates: [
        { extractor: 'PdfPig', success: true, pageCount: 120, sampleText: null, isWinner: true },
        { extractor: 'Docnet', success: false, pageCount: 0, sampleText: null, isWinner: false }
      ]
    }
    const wrapper = mount(FinancialPdfVotingPanel, { props: { detail } })
    expect(wrapper.find('[data-testid="fc-voting-failed-badge"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="fc-voting-failed-badge"]').text()).toContain('失败')
  })

  it('shows voting notes when present', () => {
    const detail = {
      ...baseDetail,
      votingNotes: 'PdfPig and iText7 agree on page count'
    }
    const wrapper = mount(FinancialPdfVotingPanel, { props: { detail } })
    expect(wrapper.find('[data-testid="fc-voting-notes"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('PdfPig and iText7 agree on page count')
  })

  it('renders textLength quality indicator for candidates', () => {
    const detail = {
      ...baseDetail,
      votingCandidates: [
        { extractor: 'PdfPig', success: true, pageCount: 120, textLength: 45000, sampleText: null, isWinner: true },
        { extractor: 'iText7', success: true, pageCount: 120, textLength: 800, sampleText: null, isWinner: false },
        { extractor: 'Docnet', success: false, pageCount: 0, textLength: 0, sampleText: null, isWinner: false }
      ]
    }
    const wrapper = mount(FinancialPdfVotingPanel, { props: { detail } })
    const text = wrapper.text()
    expect(text).toContain('4.5万字符')
    expect(text).toContain('800字符')
  })
})
