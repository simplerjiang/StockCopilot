import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, shallowMount } from '@vue/test-utils'
import App from '../App.vue'

const makeResponse = payload => ({
  ok: true,
  json: async () => payload
})

const stubFetch = onboardingPayload => vi.fn(async url => {
  if (url === '/api/app/version') {
    return makeResponse({ version: '0.0.1' })
  }

  return makeResponse(onboardingPayload)
})

const mountOptions = {
  global: {
    stubs: { KeepAlive: { template: '<slot />' } }
  }
}

describe('App', () => {
  beforeEach(() => {
    window.history.replaceState({}, '', '/')
    vi.restoreAllMocks()
    vi.stubGlobal('fetch', stubFetch({
      requiresOnboarding: false,
      activeProviderKey: 'default',
      recommendedTabKey: 'admin-llm'
    }))
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('renders tab buttons', () => {
    const wrapper = shallowMount(App, mountOptions)
    const buttons = wrapper.findAll('button.nav-tab')
    expect(buttons.length).toBeGreaterThan(0)
    expect(wrapper.text()).toContain('情绪轮动')
  })

  it('shows compact date context in the header clock', async () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date(2026, 3, 27, 14, 5, 33))

    const wrapper = shallowMount(App, mountOptions)
    await flushPromises()

    expect(wrapper.find('.header-clock').text()).toBe('04-27 14:05:33')
  })

  it('polls backend health every 45 seconds during trading hours', async () => {
    vi.useFakeTimers()
    // CST 14:05 — within afternoon trading session (13:00-15:00)
    vi.setSystemTime(new Date('2026-04-27T06:05:33Z'))
    const setIntervalSpy = vi.spyOn(window, 'setInterval')

    shallowMount(App, mountOptions)
    await flushPromises()

    expect(setIntervalSpy).toHaveBeenCalledWith(expect.any(Function), 1000)
    expect(setIntervalSpy).toHaveBeenCalledWith(expect.any(Function), 45000)
  })

  it('polls backend health every 120 seconds outside trading hours', async () => {
    vi.useFakeTimers()
    // CST 08:00 — before market open (pre-9:15)
    vi.setSystemTime(new Date('2026-04-27T00:00:00Z'))
    const setIntervalSpy = vi.spyOn(window, 'setInterval')

    shallowMount(App, mountOptions)
    await flushPromises()

    expect(setIntervalSpy).toHaveBeenCalledWith(expect.any(Function), 1000)
    expect(setIntervalSpy).toHaveBeenCalledWith(expect.any(Function), 120000)
  })

  it('switches to the LLM tab when onboarding is required', async () => {
    vi.stubGlobal('fetch', stubFetch({
      requiresOnboarding: true,
      activeProviderKey: 'default',
      recommendedTabKey: 'admin-llm'
    }))

    const wrapper = shallowMount(App, mountOptions)
    await flushPromises()

    expect(wrapper.text()).toContain('首次启动还未配置 LLM Key')
    expect(wrapper.text()).toContain('v0.0.1')
    expect(window.location.search).toContain('tab=admin-llm')
    expect(window.location.search).toContain('onboarding=1')

    // LLM settings is now in the settings dropdown, not the main nav
    const activeComponent = wrapper.findComponent({ name: 'AdminLlmSettings' })
    expect(activeComponent.exists()).toBe(true)
  })

  it('hides onboarding banner after settings are saved and onboarding is no longer required', async () => {
    let onboardingRequestCount = 0
    const fetchMock = vi.fn(async url => {
      if (url === '/api/app/version') {
        return makeResponse({ version: '0.0.1' })
      }

      onboardingRequestCount += 1
      return makeResponse({
        requiresOnboarding: onboardingRequestCount === 1,
        activeProviderKey: 'default',
        recommendedTabKey: 'admin-llm'
      })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = shallowMount(App, mountOptions)
    await flushPromises()

    expect(wrapper.text()).toContain('首次启动还未配置 LLM Key')

    const adminSettingsStub = wrapper.findComponent({ name: 'AdminLlmSettings' })
    adminSettingsStub.vm.$emit('settings-saved')
    await flushPromises()

    expect(wrapper.text()).not.toContain('首次启动还未配置 LLM Key')
    expect(window.location.search).not.toContain('onboarding=1')
    expect(fetchMock).toHaveBeenCalledTimes(3)
  })

  it('stores pending trade-log navigation before switching to the trade log tab', async () => {
    const wrapper = shallowMount(App, mountOptions)
    await flushPromises()

    const detail = {
      plan: {
        id: 7,
        symbol: '000001',
        name: '平安银行',
        direction: 'Long'
      }
    }

    window.dispatchEvent(new CustomEvent('navigate-trade-log', { detail }))
    await flushPromises()

    expect(window.__pendingNavigateTradeLog).toEqual(detail)
    expect(window.location.search).toContain('tab=trade-log')

    wrapper.unmount()
    delete window.__pendingNavigateTradeLog
  })

  it('shows a recoverable error state when the active tab component throws', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    let shouldThrow = true
    const wrapper = shallowMount(App, {
      global: {
        stubs: {
          KeepAlive: { template: '<slot />' },
          StockInfoTab: {
            name: 'StockInfoTab',
            template: '<div />',
            mounted() {
              if (shouldThrow) {
                shouldThrow = false
                throw new Error('测试组件异常')
              }
            }
          }
        }
      }
    })
    await flushPromises()

    expect(wrapper.text()).toContain('页面组件已进入可恢复错误态')
    expect(wrapper.text()).toContain('测试组件异常')
    expect(consoleError).toHaveBeenCalledWith('[App error boundary]', expect.any(Error), expect.any(String))

    await wrapper.find('.app-error-boundary button').trigger('click')
    await flushPromises()
    expect(wrapper.text()).not.toContain('页面组件已进入可恢复错误态')
  })
})
