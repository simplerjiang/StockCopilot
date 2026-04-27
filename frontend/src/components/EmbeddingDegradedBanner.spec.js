/**
 * @vitest-environment jsdom
 * @vitest-environment-options {"url":"http://localhost/"}
 */
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import EmbeddingDegradedBanner from './EmbeddingDegradedBanner.vue'

describe('EmbeddingDegradedBanner', () => {
  it('renders unavailable embedding status with coverage details', () => {
    const wrapper = mount(EmbeddingDegradedBanner, {
      props: {
        status: {
          available: false,
          model: 'bge-m3',
          dimension: null,
          embeddingCount: 0,
          chunkCount: 1456,
          coverage: 0
        }
      }
    })

    expect(wrapper.text()).toContain('RAG 检索能力已降级')
    expect(wrapper.text()).toContain('Embedding 不可用或覆盖率不足')
    expect(wrapper.text()).toContain('bge-m3')
    expect(wrapper.text()).toContain('0 / 1456 chunks (0.0%)')
  })

  it('does not render when embedding is available with full coverage', () => {
    const wrapper = mount(EmbeddingDegradedBanner, {
      props: {
        status: {
          available: true,
          model: 'bge-m3',
          dimension: 1024,
          embeddingCount: 1456,
          chunkCount: 1456,
          coverage: 1
        }
      }
    })

    expect(wrapper.html()).toBe('<!--v-if-->')
  })

  it('emits refresh when clicking refresh button', async () => {
    const wrapper = mount(EmbeddingDegradedBanner, {
      props: {
        status: {
          available: false,
          model: 'bge-m3',
          embeddingCount: 0,
          chunkCount: 10,
          coverage: 0
        }
      }
    })

    await wrapper.find('button').trigger('click')
    expect(wrapper.emitted('refresh')).toHaveLength(1)
  })
})