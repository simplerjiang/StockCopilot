import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import App from '../App.vue'

describe('App', () => {
  it('renders tab buttons', () => {
    const wrapper = mount(App)
    const buttons = wrapper.findAll('button.tab')
    expect(buttons.length).toBeGreaterThan(0)
    expect(wrapper.text()).toContain('情绪轮动')
  })
})
