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
    expect(wrapper.text()).toContain('候选提取器排序与投票明细将在后续版本暴露')
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
})
