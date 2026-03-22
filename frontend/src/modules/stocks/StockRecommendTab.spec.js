import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import StockRecommendTab from './StockRecommendTab.vue'

const makeResponse = ({ ok, status, json, text }) => ({
  ok,
  status,
  json: json || (async () => ({ content: '' })),
  text: text || (async () => '')
})

const flushPromises = () => new Promise(resolve => setTimeout(resolve, 0))

beforeEach(() => {
  vi.restoreAllMocks()
  localStorage.clear()
})

describe('StockRecommendTab', () => {
  it('renders realtime market context and sector quick board on mount', async () => {
    const fetchMock = vi.fn(async (url) => {
      if (url === '/api/market/realtime/overview') {
        return makeResponse({
          ok: true,
          status: 200,
          json: async () => ({
            snapshotTime: '2026-03-19T07:00:00Z',
            indices: [
              { symbol: 'sh000001', name: '上证指数', price: 3401.22, changePercent: 0.82 },
              { symbol: 'sz399001', name: '深证成指', price: 10888.12, changePercent: 1.15 },
              { symbol: 'sz399006', name: '创业板指', price: 2210.45, changePercent: 1.43 }
            ],
            mainCapitalFlow: { mainNetInflow: 12.34 },
            northboundFlow: { totalNetInflow: 8.9 },
            breadth: { advancers: 3210, decliners: 1450, limitUpCount: 55, limitDownCount: 6 }
          })
        })
      }

      if (url === '/api/market/sectors/realtime?boardType=concept&take=8&sort=rank') {
        return makeResponse({
          ok: true,
          status: 200,
          json: async () => ({
            items: [
              { sectorCode: 'BK1', sectorName: '机器人', changePercent: 4.21, mainNetInflow: 1230000000, rankNo: 1 },
              { sectorCode: 'BK2', sectorName: '算力', changePercent: 3.12, mainNetInflow: 980000000, rankNo: 2 }
            ]
          })
        })
      }

      return makeResponse({ ok: true, status: 200, json: async () => ({ content: 'ok' }) })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockRecommendTab)
    await flushPromises()
    await flushPromises()

    expect(wrapper.text()).toContain('推荐前市场快照')
    expect(wrapper.text()).toContain('上证指数')
    expect(wrapper.text()).toContain('机器人')
    expect(wrapper.text()).toContain('主力 +12.34 亿')
  })

  it('sends preset prompt when clicking button', async () => {
    const fetchMock = vi.fn(async (url) => {
      if (url === '/api/llm/chat/stream/openai') {
        return makeResponse({ ok: true, status: 200, json: async () => ({ content: 'ok' }) })
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockRecommendTab)
    const buttons = wrapper.findAll('.preset-button')
    await buttons[0].trigger('click')

    const call = fetchMock.mock.calls.find(args => args[0] === '/api/llm/chat/stream/openai')
    expect(call).toBeTruthy()
    const body = JSON.parse(call[1].body)
    expect(body.prompt).toContain('今日国内外重要财经新闻')
    expect(body.useInternet).toBe(true)
  })

  it('streams assistant response chunks', async () => {
    const encoder = new TextEncoder()
    const stream = new ReadableStream({
      start(controller) {
        controller.enqueue(encoder.encode('data: 你好\n\n'))
        controller.enqueue(encoder.encode('data: 世界\n\n'))
        controller.enqueue(encoder.encode('data: [DONE]\n\n'))
        controller.close()
      }
    })

    const fetchMock = vi.fn(async (url) => {
      if (url === '/api/llm/chat/stream/openai') {
        return {
          ok: true,
          status: 200,
          body: stream,
          text: async () => '',
          json: async () => ({})
        }
      }
      return makeResponse({ ok: false, status: 404 })
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockRecommendTab)
  await flushPromises()
    const chatWindow = wrapper.findComponent({ name: 'ChatWindow' })
    await chatWindow.vm.sendChat('请给我一份测试推荐')
    await flushPromises()

    const assistant = chatWindow.vm.chatMessages.find(item => item.role === 'assistant')
    expect(assistant.content).toBe('你好世界')
  })

  it('renders markdown content', async () => {
    const fetchMock = vi.fn(async () => makeResponse({ ok: true, status: 200, json: async () => ({ content: 'ok' }) }))
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockRecommendTab)
    const chatWindow = wrapper.findComponent({ name: 'ChatWindow' })
    chatWindow.vm.chatMessages = [
      { role: 'assistant', content: '**加粗**\n\n- 列表项', timestamp: '2026-01-30T00:00:00Z' }
    ]

    await wrapper.vm.$nextTick()

    const html = wrapper.find('.chat-content').html()
    expect(html).toContain('<strong>加粗</strong>')
    expect(html).toContain('<li>列表项</li>')
  })

  it('creates new session and allows switching back to history', async () => {
    const fetchMock = vi.fn(async () => makeResponse({ ok: true, status: 200, json: async () => ({ content: 'ok' }) }))
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockRecommendTab)
    await wrapper.vm.$nextTick()
  await flushPromises()

    const chatWindow = wrapper.findComponent({ name: 'ChatWindow' })
    chatWindow.vm.chatMessages = [{ role: 'assistant', content: '历史A', timestamp: '2026-01-30T00:00:00Z' }]
    await wrapper.vm.$nextTick()

    const newChatButton = wrapper.findAll('button').find(button => button.text() === '新建对话')
    await newChatButton.trigger('click')
    await wrapper.vm.$nextTick()

    const selector = wrapper.find('select')
    const options = selector.findAll('option')
    expect(options.length).toBeGreaterThan(1)

    await selector.setValue(options[options.length - 1].attributes('value'))
    await flushPromises()
    await wrapper.vm.$nextTick()

    const restored = chatWindow.vm.chatMessages.find(item => item.role === 'assistant')
    expect(restored?.content).toBe('历史A')
  })
})
