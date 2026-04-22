/**
 * @vitest-environment jsdom
 * @vitest-environment-options {"url":"http://localhost/"}
 */
import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import FinancialDetailDrawer from '../FinancialDetailDrawer.vue'

const item = {
  id: 42,
  symbol: '600519',
  reportDate: '2024-12-31'
}

const mountDrawer = (props = {}) => mount(FinancialDetailDrawer, {
  attachTo: document.body,
  props: {
    visible: true,
    item,
    ...props
  }
})

describe('FinancialDetailDrawer', () => {
  it('renders Symbol / ReportDate / Report ID rows when visible', () => {
    const wrapper = mountDrawer()
    const text = document.body.textContent
    expect(text).toContain('Symbol')
    expect(text).toContain('ReportDate')
    expect(text).toContain('Report ID')
    expect(text).toContain('600519')
    expect(text).toContain('2024-12-31')
    expect(text).toContain('42')
    wrapper.unmount()
  })

  it('emits close on Esc, overlay click, and × button', async () => {
    const wrapper = mountDrawer()

    // × button
    const closeBtn = document.querySelector('.fc-drawer-close')
    expect(closeBtn).toBeTruthy()
    closeBtn.dispatchEvent(new Event('click', { bubbles: true }))
    await nextTick()

    // overlay click (target === currentTarget)
    const overlay = document.querySelector('.fc-drawer-overlay')
    expect(overlay).toBeTruthy()
    overlay.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await nextTick()

    // Escape on window
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    await nextTick()

    const events = wrapper.emitted('close') || []
    expect(events.length).toBeGreaterThanOrEqual(3)
    wrapper.unmount()
  })

  it('adds keydown listener when visible flips to true and removes when flipped to false', async () => {
    const addSpy = vi.spyOn(window, 'addEventListener')
    const removeSpy = vi.spyOn(window, 'removeEventListener')
    const wrapper = mount(FinancialDetailDrawer, {
      attachTo: document.body,
      props: { visible: false, item: null }
    })
    // initial mount: visible=false → immediate watcher runs removeEventListener
    expect(removeSpy).toHaveBeenCalledWith('keydown', expect.any(Function))

    addSpy.mockClear()
    removeSpy.mockClear()
    await wrapper.setProps({ visible: true, item })
    expect(addSpy).toHaveBeenCalledWith('keydown', expect.any(Function))

    addSpy.mockClear()
    removeSpy.mockClear()
    await wrapper.setProps({ visible: false })
    expect(removeSpy).toHaveBeenCalledWith('keydown', expect.any(Function))

    wrapper.unmount()
  })
})
