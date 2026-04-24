/**
 * @vitest-environment jsdom
 * @vitest-environment-options {"url":"http://localhost/"}
 */
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { nextTick } from 'vue'

import FinancialPdfViewer from '../FinancialPdfViewer.vue'

describe('FinancialPdfViewer', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('renders an iframe pointing to src when src is provided', async () => {
    const wrapper = mount(FinancialPdfViewer, {
      props: { src: '/api/stocks/financial/pdf-files/abc/content', title: '测试报告' }
    })
    await nextTick()
    const iframe = wrapper.find('[data-testid="fc-pdf-iframe"]')
    expect(iframe.exists()).toBe(true)
    expect(iframe.attributes('src')).toContain('/api/stocks/financial/pdf-files/abc/content')
    expect(iframe.attributes('title')).toBe('测试报告')
    // Demo usage: parent component would simply do
    //   <FinancialPdfViewer :src="pdfUrl" :page="2" @load="..." @error="..." />
    wrapper.unmount()
  })

  it('appends #page=N when page prop is given', async () => {
    const wrapper = mount(FinancialPdfViewer, {
      props: { src: 'https://example.com/r.pdf', page: 3 }
    })
    await nextTick()
    const iframe = wrapper.find('[data-testid="fc-pdf-iframe"]')
    expect(iframe.attributes('src')).toBe('https://example.com/r.pdf#page=3')
    wrapper.unmount()
  })

  it('shows placeholder when src is empty', async () => {
    const wrapper = mount(FinancialPdfViewer, { props: { src: '' } })
    await nextTick()
    expect(wrapper.find('[data-testid="fc-pdf-empty"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="fc-pdf-iframe"]').exists()).toBe(false)
    wrapper.unmount()
  })

  it('emits load and hides loading state when iframe load fires', async () => {
    const wrapper = mount(FinancialPdfViewer, {
      props: { src: '/x.pdf' }
    })
    await nextTick()
    expect(wrapper.find('[data-testid="fc-pdf-loading"]').exists()).toBe(true)
    await wrapper.find('[data-testid="fc-pdf-iframe"]').trigger('load')
    await nextTick()
    expect(wrapper.emitted('load')).toBeTruthy()
    expect(wrapper.find('[data-testid="fc-pdf-loading"]').exists()).toBe(false)
    wrapper.unmount()
  })

  it('emits error and shows download fallback when iframe errors', async () => {
    const wrapper = mount(FinancialPdfViewer, {
      props: { src: '/x.pdf' }
    })
    await nextTick()
    await wrapper.find('[data-testid="fc-pdf-iframe"]').trigger('error')
    await nextTick()
    expect(wrapper.emitted('error')).toBeTruthy()
    const fallback = wrapper.find('[data-testid="fc-pdf-error"]')
    expect(fallback.exists()).toBe(true)
    expect(fallback.text()).toContain('无法预览')
    expect(fallback.find('a').attributes('href')).toBe('/x.pdf')
    wrapper.unmount()
  })

  it('emits error after loadTimeoutMs when iframe never loads', async () => {
    const wrapper = mount(FinancialPdfViewer, {
      props: { src: '/x.pdf', loadTimeoutMs: 1000 }
    })
    await nextTick()
    vi.advanceTimersByTime(1500)
    await flushPromises()
    expect(wrapper.emitted('error')).toBeTruthy()
    expect(wrapper.find('[data-testid="fc-pdf-error"]').exists()).toBe(true)
    wrapper.unmount()
  })

  it('emits pageChange when page prop changes', async () => {
    const wrapper = mount(FinancialPdfViewer, {
      props: { src: '/x.pdf', page: 1 }
    })
    await nextTick()
    await wrapper.setProps({ page: 5 })
    await nextTick()
    const events = wrapper.emitted('pageChange')
    expect(events).toBeTruthy()
    expect(events[events.length - 1]).toEqual([5])
    wrapper.unmount()
  })
})
