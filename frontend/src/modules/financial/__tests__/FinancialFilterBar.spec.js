/**
 * @vitest-environment jsdom
 * @vitest-environment-options {"url":"http://localhost/"}
 */
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { reactive } from 'vue'
import FinancialFilterBar from '../FinancialFilterBar.vue'

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

const mountBar = (props = {}) => mount(FinancialFilterBar, {
  props: {
    query: buildQuery(),
    loading: false,
    ...props
  }
})

afterEach(() => {
  vi.useRealTimers()
})

describe('FinancialFilterBar — rendering & loading state', () => {
  it('renders props and disables all controls when loading=true', () => {
    const query = buildQuery({ symbols: ['600519'], keyword: 'hi', reportTypes: ['annual'] })
    const wrapper = mount(FinancialFilterBar, { props: { query, loading: true } })
    // chips visible
    expect(wrapper.text()).toContain('600519')
    // chip-input field disabled
    const chipInput = wrapper.find('.fc-chip-input-field')
    expect(chipInput.attributes('disabled')).toBeDefined()
    // date inputs disabled
    const dateInputs = wrapper.findAll('input[type="date"]')
    expect(dateInputs.length).toBe(2)
    dateInputs.forEach(d => expect(d.attributes('disabled')).toBeDefined())
    // pill buttons disabled
    wrapper.findAll('.fc-pill').forEach(p => expect(p.attributes('disabled')).toBeDefined())
    // keyword input disabled
    const kw = wrapper.find('.fc-input--full')
    expect(kw.attributes('disabled')).toBeDefined()
    // action buttons disabled
    wrapper.findAll('.fc-filter-actions button').forEach(b => expect(b.attributes('disabled')).toBeDefined())
  })
})

describe('FinancialFilterBar — chip input', () => {
  it('Enter pushes the draft into symbols', async () => {
    const wrapper = mountBar()
    const field = wrapper.find('.fc-chip-input-field')
    await field.setValue('600519')
    await field.trigger('keydown', { key: 'Enter' })
    expect(wrapper.props('query').symbols).toEqual(['600519'])
  })

  it('comma (ASCII or full-width) splits multiple symbols', async () => {
    const wrapper = mountBar()
    const field = wrapper.find('.fc-chip-input-field')
    // the implementation tokenises on , or ， or whitespace when addSymbolsFromDraft runs
    await field.setValue('600519,000001，002594')
    await field.trigger('keydown', { key: 'Enter' })
    expect(wrapper.props('query').symbols).toEqual(['600519', '000001', '002594'])
  })

  it('Backspace on empty draft pops the last chip', async () => {
    const wrapper = mountBar({ query: buildQuery({ symbols: ['600519', '000001'] }) })
    const field = wrapper.find('.fc-chip-input-field')
    await field.setValue('')
    await field.trigger('keydown', { key: 'Backspace' })
    expect(wrapper.props('query').symbols).toEqual(['600519'])
  })

  it('dedupes symbols pasted with duplicates', async () => {
    const wrapper = mountBar()
    const field = wrapper.find('.fc-chip-input-field')
    await field.setValue('600519,600519,000001')
    await field.trigger('keydown', { key: 'Enter' })
    expect(wrapper.props('query').symbols).toEqual(['600519', '000001'])
  })
})

describe('FinancialFilterBar — report type pills', () => {
  it('keeps at least one type when user tries to remove the last one', async () => {
    const wrapper = mountBar({ query: buildQuery({ reportTypes: ['annual'] }) })
    // find the pill labeled 年报
    const pills = wrapper.findAll('.fc-pill')
    const annualPill = pills.find(p => p.text().includes('年报'))
    expect(annualPill).toBeDefined()
    await annualPill.trigger('click')
    expect(wrapper.props('query').reportTypes).toEqual(['annual'])
  })

  it('toggling a type emits change with reason=type', async () => {
    const wrapper = mountBar({ query: buildQuery({ reportTypes: ['annual', 'q1'] }) })
    const pills = wrapper.findAll('.fc-pill')
    const annualPill = pills.find(p => p.text().includes('年报'))
    await annualPill.trigger('click')
    const events = wrapper.emitted('change')
    expect(events).toBeTruthy()
    expect(events[0][0]).toEqual({ reason: 'type' })
    expect(wrapper.props('query').reportTypes).toEqual(['q1'])
  })
})

describe('FinancialFilterBar — keyword debounce', () => {
  it('debounces 300ms then emits change once', async () => {
    vi.useFakeTimers()
    const wrapper = mountBar()
    const kw = wrapper.find('.fc-input--full')
    await kw.setValue('hello')
    // before debounce, no emit
    expect(wrapper.emitted('change')).toBeUndefined()
    vi.advanceTimersByTime(299)
    expect(wrapper.emitted('change')).toBeUndefined()
    vi.advanceTimersByTime(2)
    const events = wrapper.emitted('change')
    expect(events).toBeTruthy()
    expect(events.length).toBe(1)
    expect(events[0][0]).toEqual({ reason: 'keyword' })
    expect(wrapper.props('query').keyword).toBe('hello')
  })

  it('Enter cancels debounce and fires immediately with reason=keyword-enter', async () => {
    vi.useFakeTimers()
    const wrapper = mountBar()
    const kw = wrapper.find('.fc-input--full')
    await kw.setValue('q')
    await kw.trigger('keydown.enter')
    const events = wrapper.emitted('change')
    expect(events).toBeTruthy()
    expect(events.length).toBe(1)
    expect(events[0][0]).toEqual({ reason: 'keyword-enter' })
    expect(wrapper.props('query').keyword).toBe('q')
    // advance further — debounce should have been cancelled, no extra emit
    vi.advanceTimersByTime(500)
    expect(wrapper.emitted('change').length).toBe(1)
  })
})

describe('FinancialFilterBar — date inputs', () => {
  it('start date change fires emit immediately with reason=date', async () => {
    const wrapper = mountBar()
    const dates = wrapper.findAll('input[type="date"]')
    await dates[0].setValue('2025-06-01')
    await dates[0].trigger('change')
    const events = wrapper.emitted('change')
    expect(events).toBeTruthy()
    expect(events[0][0]).toEqual({ reason: 'date' })
    expect(wrapper.props('query').startDate).toBe('2025-06-01')
  })

  it('end date change fires emit immediately with reason=date', async () => {
    const wrapper = mountBar()
    const dates = wrapper.findAll('input[type="date"]')
    await dates[1].setValue('2025-09-01')
    await dates[1].trigger('change')
    const events = wrapper.emitted('change')
    expect(events).toBeTruthy()
    expect(events[0][0]).toEqual({ reason: 'date' })
    expect(wrapper.props('query').endDate).toBe('2025-09-01')
  })
})

describe('FinancialFilterBar — action buttons', () => {
  it('reset button emits reset', async () => {
    const wrapper = mountBar()
    const buttons = wrapper.findAll('.fc-filter-actions button')
    const resetBtn = buttons.find(b => b.text().includes('重置'))
    expect(resetBtn).toBeDefined()
    await resetBtn.trigger('click')
    expect(wrapper.emitted('reset')).toBeTruthy()
  })

  it('submit button emits submit', async () => {
    const wrapper = mountBar()
    const buttons = wrapper.findAll('.fc-filter-actions button')
    const submitBtn = buttons.find(b => b.text().includes('查询'))
    expect(submitBtn).toBeDefined()
    await submitBtn.trigger('click')
    expect(wrapper.emitted('submit')).toBeTruthy()
  })
})
