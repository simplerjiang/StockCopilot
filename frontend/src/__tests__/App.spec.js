import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, shallowMount } from '@vue/test-utils'
import App from '../App.vue'

const makeResponse = payload => ({
  ok: true,
  json: async () => payload
})

describe('App', () => {
  beforeEach(() => {
    window.history.replaceState({}, '', '/')
    vi.restoreAllMocks()
    vi.stubGlobal('fetch', vi.fn(async () => makeResponse({
      requiresOnboarding: false,
      activeProviderKey: 'default',
      recommendedTabKey: 'admin-llm'
    })))
  })

  it('renders tab buttons', () => {
    const wrapper = shallowMount(App)
    const buttons = wrapper.findAll('button.tab')
    expect(buttons.length).toBeGreaterThan(0)
    expect(wrapper.text()).toContain('情绪轮动')
  })

  it('switches to the LLM tab when onboarding is required', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => makeResponse({
      requiresOnboarding: true,
      activeProviderKey: 'default',
      recommendedTabKey: 'admin-llm'
    })))

    const wrapper = shallowMount(App)
    await flushPromises()

    expect(wrapper.text()).toContain('首次启动还没有 LLM Key')
    expect(window.location.search).toContain('tab=admin-llm')
    expect(window.location.search).toContain('onboarding=1')

    const activeButton = wrapper.find('button.tab.active')
    expect(activeButton.text()).toContain('LLM 设置')
  })
})
