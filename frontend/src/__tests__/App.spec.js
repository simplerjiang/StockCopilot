import { beforeEach, describe, expect, it, vi } from 'vitest'
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

  it('renders tab buttons', () => {
    const wrapper = shallowMount(App, mountOptions)
    const buttons = wrapper.findAll('button.nav-tab')
    expect(buttons.length).toBeGreaterThan(0)
    expect(wrapper.text()).toContain('情绪轮动')
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
})
