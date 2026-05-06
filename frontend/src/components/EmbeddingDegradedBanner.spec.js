/**
 * @vitest-environment jsdom
 * @vitest-environment-options {"url":"http://localhost/"}
 */
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import EmbeddingDegradedBanner from './EmbeddingDegradedBanner.vue'

describe('EmbeddingDegradedBanner', () => {
  beforeEach(() => {
    sessionStorage.clear()
  })

  it('renders ollama-unavailable cause with no backfill button', () => {
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

    expect(wrapper.text()).toContain('向量模型未就绪')
    expect(wrapper.text()).toContain('Ollama 未运行或 bge-m3 模型未安装')
    expect(wrapper.text()).toContain('bge-m3')
    expect(wrapper.text()).toContain('0 / 1456 chunks (0.0%)')
    const btns = wrapper.findAll('.embedding-degraded-banner__backfill')
    expect(btns).toHaveLength(1)
    expect(btns[0].text()).toBe('启动 Ollama')
  })

  it('renders zero-embedding cause with backfill button', () => {
    const wrapper = mount(EmbeddingDegradedBanner, {
      props: {
        status: {
          available: true,
          model: 'bge-m3',
          dimension: 1024,
          embeddingCount: 0,
          chunkCount: 100,
          coverage: 0
        }
      }
    })

    expect(wrapper.text()).toContain('尚无向量数据')
    expect(wrapper.text()).toContain('点击补建开始构建检索索引')
    const backfillBtn = wrapper.find('.embedding-degraded-banner__backfill')
    expect(backfillBtn.exists()).toBe(true)
    expect(backfillBtn.text()).toBe('开始补建')
  })

  it('renders low-coverage cause with backfill button', () => {
    const wrapper = mount(EmbeddingDegradedBanner, {
      props: {
        status: {
          available: true,
          model: 'bge-m3',
          dimension: 1024,
          embeddingCount: 30,
          chunkCount: 100,
          coverage: 0.3
        }
      }
    })

    expect(wrapper.text()).toContain('向量覆盖不足')
    expect(wrapper.text()).toContain('30.0%')
    const backfillBtn = wrapper.find('.embedding-degraded-banner__backfill')
    expect(backfillBtn.exists()).toBe(true)
    expect(backfillBtn.text()).toBe('补建向量')
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

    await wrapper.find('.embedding-degraded-banner__refresh').trigger('click')
    expect(wrapper.emitted('refresh')).toHaveLength(1)
  })

  it('calls backfill endpoint and shows streaming progress then success', async () => {
    const ndjson = '{"filled":5,"total":10,"done":false}\n{"filled":10,"total":10,"done":true}\n'
    const encoder = new TextEncoder()
    const stream = new ReadableStream({
      start(controller) {
        controller.enqueue(encoder.encode(ndjson))
        controller.close()
      }
    })
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, body: stream }))
    const wrapper = mount(EmbeddingDegradedBanner, {
      props: {
        status: {
          available: true,
          model: 'bge-m3',
          dimension: 1024,
          embeddingCount: 0,
          chunkCount: 100,
          coverage: 0
        }
      }
    })

    await wrapper.find('.embedding-degraded-banner__backfill').trigger('click')
    await vi.dynamicImportSettled()

    expect(fetch).toHaveBeenCalledWith('/api/stocks/financial/embedding/backfill', { method: 'POST' })
    expect(wrapper.text()).toContain('补建完成：已处理 10 条')
    vi.unstubAllGlobals()
  })

  it('shows error message on backfill failure', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status: 500 }))
    const wrapper = mount(EmbeddingDegradedBanner, {
      props: {
        status: {
          available: true,
          model: 'bge-m3',
          dimension: 1024,
          embeddingCount: 10,
          chunkCount: 100,
          coverage: 0.1
        }
      }
    })

    await wrapper.find('.embedding-degraded-banner__backfill').trigger('click')
    await vi.dynamicImportSettled()

    expect(wrapper.text()).toContain('补建失败')
    vi.unstubAllGlobals()
  })

  it('calls start ollama endpoint and shows success message', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      json: () => Promise.resolve({ success: true })
    }))
    const wrapper = mount(EmbeddingDegradedBanner, {
      props: {
        status: {
          available: false,
          model: 'bge-m3',
          dimension: null,
          embeddingCount: 0,
          chunkCount: 100,
          coverage: 0
        }
      }
    })

    const startBtn = wrapper.findAll('.embedding-degraded-banner__backfill').find(b => b.text() === '启动 Ollama')
    expect(startBtn.exists()).toBe(true)
    await startBtn.trigger('click')
    await vi.dynamicImportSettled()

    expect(fetch).toHaveBeenCalledWith('/api/admin/ollama/start', { method: 'POST' })
    expect(wrapper.text()).toContain('Ollama 已启动')
    vi.unstubAllGlobals()
  })

  it('shows error message on start ollama failure', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      json: () => Promise.resolve({ success: false, message: '找不到 Ollama' })
    }))
    const wrapper = mount(EmbeddingDegradedBanner, {
      props: {
        status: {
          available: false,
          model: 'bge-m3',
          dimension: null,
          embeddingCount: 0,
          chunkCount: 100,
          coverage: 0
        }
      }
    })

    const startBtn = wrapper.findAll('.embedding-degraded-banner__backfill').find(b => b.text() === '启动 Ollama')
    await startBtn.trigger('click')
    await vi.dynamicImportSettled()

    expect(wrapper.text()).toContain('找不到 Ollama')
    vi.unstubAllGlobals()
  })
})