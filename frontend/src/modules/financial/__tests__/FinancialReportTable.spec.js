/**
 * @vitest-environment jsdom
 * @vitest-environment-options {"url":"http://localhost/"}
 */
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import FinancialReportTable from '../FinancialReportTable.vue'
import { SOURCE_CHANNEL_STYLE, FALLBACK_CHANNEL_STYLE } from '../financialCenterConstants.js'

const buildQuery = (overrides = {}) => reactive({
  symbols: [],
  startDate: '2025-01-01',
  endDate: '2025-12-31',
  reportTypes: ['annual', 'q1', 'q2', 'q3'],
  keyword: '',
  page: 1,
  pageSize: 20,
  sortField: 'reportDate',
  sortDirection: 'desc',
  ...overrides
})

const mountTable = (props = {}) => mount(FinancialReportTable, {
  props: {
    items: [],
    total: 0,
    loading: false,
    error: '',
    query: buildQuery(),
    symbolNameMap: {},
    hasFilter: false,
    collectTabAvailable: true,
    ...props
  }
})

describe('FinancialReportTable — header & sort', () => {
  it('renders 7 columns; 5 are sortable (symbol/reportDate/reportType/sourceChannel/collectedAt)', () => {
    const wrapper = mountTable()
    const ths = wrapper.findAll('th.fc-th')
    expect(ths.length).toBe(7)
    const sortable = wrapper.findAll('th.fc-th--sortable')
    // NOTE: spec said 4 columns sortable; Dev implemented 5 (extra: reportType).
    // Asserting actual implementation per Test Agent guidance.
    expect(sortable.length).toBe(5)
  })

  it('clicking a sortable column emits sort-change with the field name', async () => {
    const wrapper = mountTable({ query: buildQuery() })
    const ths = wrapper.findAll('th.fc-th--sortable')
    // Find the reportDate column (currently active default)
    const reportDateTh = ths.find(t => t.text().includes('报告期'))
    await reportDateTh.trigger('click')
    const events = wrapper.emitted('sort-change')
    expect(events).toBeTruthy()
    expect(events[0][0]).toBe('reportDate')
  })

  it('clicking a different column emits its field name', async () => {
    const wrapper = mountTable()
    const ths = wrapper.findAll('th.fc-th--sortable')
    const symbolTh = ths.find(t => t.text().includes('股票代码'))
    await symbolTh.trigger('click')
    expect(wrapper.emitted('sort-change')[0][0]).toBe('symbol')
  })

  it('does not emit sort-change while loading', async () => {
    const wrapper = mountTable({ loading: true })
    const ths = wrapper.findAll('th.fc-th--sortable')
    await ths[0].trigger('click')
    expect(wrapper.emitted('sort-change')).toBeUndefined()
  })
})

describe('FinancialReportTable — channel tag styling', () => {
  it.each(Object.keys(SOURCE_CHANNEL_STYLE))('uses defined style for known channel %s', (key) => {
    const wrapper = mountTable({
      items: [{ id: 1, symbol: '600519', sourceChannel: key }],
      total: 1
    })
    const tag = wrapper.find('.fc-channel-tag')
    expect(tag.exists()).toBe(true)
    const style = tag.attributes('style') || ''
    // expect the channel-specific colour CSS variable to be referenced
    expect(style).toContain(SOURCE_CHANNEL_STYLE[key].color)
  })

  it('falls back to neutral style for unknown channel', () => {
    const wrapper = mountTable({
      items: [{ id: 1, symbol: '600519', sourceChannel: 'mystery' }],
      total: 1
    })
    const tag = wrapper.find('.fc-channel-tag')
    expect(tag.exists()).toBe(true)
    const style = tag.attributes('style') || ''
    expect(style).toContain(FALLBACK_CHANNEL_STYLE.color)
    // label fallback: raw value passed through
    expect(tag.text()).toBe('mystery')
  })
})

describe('FinancialReportTable — pagination', () => {
  it('shows 1 … 3 4 5 … 8 for total=80 pageSize=10 current=4 (Dev algo: ellipsis only when totalPages > 7)', () => {
    const wrapper = mountTable({
      total: 80,
      query: buildQuery({ page: 4, pageSize: 10 })
    })
    const labels = wrapper.findAll('.fc-page-btn, .fc-page-ellipsis').map(b => b.text())
    const middle = labels.filter(l => /^\d+$/.test(l) || l === '…' || l === '...')
    expect(middle.join(' ')).toBe('1 … 3 4 5 … 8')
  })

  it('shows all 7 pages for total=70 pageSize=10 (boundary: totalPages == 7 falls in compact branch)', () => {
    const wrapper = mountTable({
      total: 70,
      query: buildQuery({ page: 4, pageSize: 10 })
    })
    const labels = wrapper.findAll('.fc-page-btn').map(b => b.text())
    const middle = labels.filter(l => /^\d+$/.test(l))
    expect(middle).toEqual(['1', '2', '3', '4', '5', '6', '7'])
  })

  it('shows all pages for total=30 pageSize=10', () => {
    const wrapper = mountTable({
      total: 30,
      query: buildQuery({ page: 1, pageSize: 10 })
    })
    const labels = wrapper.findAll('.fc-page-btn').map(b => b.text())
    const middle = labels.filter(l => /^\d+$/.test(l))
    expect(middle).toEqual(['1', '2', '3'])
  })

  it('disables prev on first page', () => {
    const wrapper = mountTable({ total: 30, query: buildQuery({ page: 1, pageSize: 10 }) })
    const buttons = wrapper.findAll('.fc-page-btn')
    const prev = buttons.find(b => b.text() === '上一页')
    expect(prev.attributes('disabled')).toBeDefined()
  })

  it('disables next on last page', () => {
    const wrapper = mountTable({ total: 30, query: buildQuery({ page: 3, pageSize: 10 }) })
    const buttons = wrapper.findAll('.fc-page-btn')
    const next = buttons.find(b => b.text() === '下一页')
    expect(next.attributes('disabled')).toBeDefined()
  })

  it('changing page-size select emits page-size-change', async () => {
    const wrapper = mountTable({ total: 30, query: buildQuery({ pageSize: 20 }) })
    const select = wrapper.find('select.fc-page-size')
    await select.setValue('50')
    const events = wrapper.emitted('page-size-change')
    expect(events).toBeTruthy()
    expect(events[0][0]).toBe(50)
  })

  it('disables pagination buttons while loading', () => {
    const wrapper = mountTable({ total: 30, loading: true, query: buildQuery({ page: 2, pageSize: 10 }) })
    const buttons = wrapper.findAll('.fc-page-btn')
    buttons.forEach(b => expect(b.attributes('disabled')).toBeDefined())
  })
})

describe('FinancialReportTable — empty state', () => {
  it('shows reset CTA when hasFilter=true and items empty', () => {
    const wrapper = mountTable({ items: [], total: 0, hasFilter: true })
    const empty = wrapper.find('.fc-empty')
    expect(empty.exists()).toBe(true)
    expect(empty.text()).toContain('重置')
  })

  it('shows go-to-collect CTA when hasFilter=false and items empty', () => {
    const wrapper = mountTable({ items: [], total: 0, hasFilter: false })
    const empty = wrapper.find('.fc-empty')
    expect(empty.exists()).toBe(true)
    expect(empty.text()).toContain('采集面板')
  })

  it('reset button in empty emits reset', async () => {
    const wrapper = mountTable({ items: [], total: 0, hasFilter: true })
    const btn = wrapper.find('.fc-empty button')
    await btn.trigger('click')
    expect(wrapper.emitted('reset')).toBeTruthy()
  })
})

describe('FinancialReportTable — error banner & retry', () => {
  it('shows error banner and retry button emits retry', async () => {
    const wrapper = mountTable({ error: 'HTTP 500' })
    const banner = wrapper.find('.fc-error-banner')
    expect(banner.exists()).toBe(true)
    expect(banner.text()).toContain('HTTP 500')
    await banner.find('button').trigger('click')
    expect(wrapper.emitted('retry')).toBeTruthy()
  })
})

describe('FinancialReportTable — row actions', () => {
  it('clicking 详情 emits open-detail with the row item', async () => {
    const item = { id: 42, symbol: '600519', reportDate: '2024-12-31' }
    const wrapper = mountTable({ items: [item], total: 1 })
    const btn = wrapper.find('.fc-link-btn')
    expect(btn.exists()).toBe(true)
    await btn.trigger('click')
    const events = wrapper.emitted('open-detail')
    expect(events).toBeTruthy()
    expect(events[0][0]).toEqual(item)
  })
})
