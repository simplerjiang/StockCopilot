/**
 * @vitest-environment jsdom
 */
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import FinancialPdfParsePreview from '../FinancialPdfParsePreview.vue'

const mk = (overrides = {}) => ({
  blockKind: 'table',
  pageStart: 1,
  pageEnd: 1,
  sectionName: null,
  fieldCount: 0,
  snippet: null,
  ...overrides
})

describe('FinancialPdfParsePreview', () => {
  it('渲染三类 blockKind 分组与计数', () => {
    const units = [
      mk({ blockKind: 'table', pageStart: 1, pageEnd: 2 }),
      mk({ blockKind: 'table', pageStart: 3, pageEnd: 3 }),
      mk({ blockKind: 'narrative_section', pageStart: 4, pageEnd: 5 }),
      mk({ blockKind: 'narrative_section', pageStart: 6, pageEnd: 6 }),
      mk({ blockKind: 'figure_caption', pageStart: 7, pageEnd: 7 }),
      mk({ blockKind: 'figure_caption', pageStart: 8, pageEnd: 8 })
    ]
    const wrapper = mount(FinancialPdfParsePreview, { props: { parseUnits: units } })

    const groups = wrapper.findAll('.fc-pdf-parse-group')
    expect(groups).toHaveLength(3)
    expect(groups[0].attributes('data-group')).toBe('table')
    expect(groups[1].attributes('data-group')).toBe('narrative_section')
    expect(groups[2].attributes('data-group')).toBe('figure_caption')

    const counts = wrapper.findAll('[data-testid="fc-pdf-parse-group-count"]')
    expect(counts).toHaveLength(3)
    counts.forEach(c => expect(c.text()).toContain('共 2 条'))

    const text = wrapper.text()
    expect(text).toContain('表格')
    expect(text).toContain('叙述段落')
    expect(text).toContain('图注')
  })

  it('同页解析单元只显示单个页码', () => {
    const wrapper = mount(FinancialPdfParsePreview, {
      props: { parseUnits: [mk({ pageStart: 5, pageEnd: 5 })] }
    })
    const btn = wrapper.find('[data-testid="fc-pdf-parse-page-btn"]')
    expect(btn.text()).toBe('P5')
    expect(btn.text()).not.toContain('P5-P5')
  })

  it('点击页码触发 jump-to-page emit', async () => {
    const units = [mk({ pageStart: 12, pageEnd: 14 })]
    const wrapper = mount(FinancialPdfParsePreview, { props: { parseUnits: units } })
    await wrapper.find('[data-testid="fc-pdf-parse-page-btn"]').trigger('click')
    expect(wrapper.emitted('jump-to-page')).toBeTruthy()
    expect(wrapper.emitted('jump-to-page')[0]).toEqual([12])
  })

  it('空态/loading/error 三态分别渲染', () => {
    const empty = mount(FinancialPdfParsePreview, { props: { parseUnits: [] } })
    expect(empty.find('[data-testid="fc-pdf-parse-empty"]').exists()).toBe(true)

    const loading = mount(FinancialPdfParsePreview, { props: { parseUnits: [], loading: true } })
    expect(loading.find('[data-testid="fc-pdf-parse-loading"]').exists()).toBe(true)
    expect(loading.find('[data-testid="fc-pdf-parse-empty"]').exists()).toBe(false)

    const errored = mount(FinancialPdfParsePreview, {
      props: { parseUnits: [], error: '加载失败' }
    })
    expect(errored.find('[data-testid="fc-pdf-parse-error"]').exists()).toBe(true)
    expect(errored.text()).toContain('加载失败')
  })

  it('未知 blockKind 归并到 unknown 分组', () => {
    const wrapper = mount(FinancialPdfParsePreview, {
      props: { parseUnits: [mk({ blockKind: 'mystery', pageStart: 9, pageEnd: 9 })] }
    })
    const groups = wrapper.findAll('.fc-pdf-parse-group')
    expect(groups).toHaveLength(1)
    expect(groups[0].attributes('data-group')).toBe('unknown')
  })

  it('snippet 超过 120 字会截断并加省略号', () => {
    const long = 'x'.repeat(200)
    const wrapper = mount(FinancialPdfParsePreview, {
      props: { parseUnits: [mk({ snippet: long, fieldCount: 3 })] }
    })
    const snippet = wrapper.find('.fc-pdf-parse-snippet')
    expect(snippet.text().endsWith('…')).toBe(true)
    expect(snippet.text().length).toBeLessThanOrEqual(121)
  })
})
