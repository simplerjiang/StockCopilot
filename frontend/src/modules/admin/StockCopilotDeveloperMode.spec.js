import { nextTick } from 'vue'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import StockCopilotDeveloperMode from './StockCopilotDeveloperMode.vue'

const makeResponse = payload => ({
  ok: true,
  json: async () => payload,
  text: async () => JSON.stringify(payload)
})

const pending = () => {
  let resolve
  let reject
  const promise = new Promise((res, rej) => {
    resolve = res
    reject = rej
  })

  return { promise, resolve, reject }
}

const basePayloads = {
  replay: { scope: 'sh600000', sampleCount: 1, horizons: [{ horizonDays: 1 }], samples: [{ traceId: 'replay-trace' }] },
  kline: {
    toolName: 'StockKlineMcp',
    traceId: 'kline-trace',
    taskId: 'stock-copilot-dev-kline',
    meta: { policyClass: 'local_required' },
    data: { bars: [{ close: 10 }] },
    features: [{ name: 'coverageScore' }],
    evidence: [{ title: '公告' }],
    degradedFlags: ['expanded_news_window']
  },
  minute: {
    toolName: 'StockMinuteMcp',
    traceId: 'minute-trace',
    taskId: 'stock-copilot-dev-minute',
    data: { points: [{ price: 10 }], sessionPhase: 'morning_session' },
    degradedFlags: [],
    warnings: []
  },
  strategy: {
    toolName: 'StockStrategyMcp',
    traceId: 'strategy-trace',
    taskId: 'stock-copilot-dev-strategy',
    data: { signals: [{ strategy: 'ma', signal: 'golden' }] },
    features: [{ name: 'trendState' }],
    warnings: ['guardrail_penalty_applied']
  },
  news: {
    toolName: 'StockNewsMcp',
    traceId: 'news-trace',
    taskId: 'stock-copilot-dev-news',
    meta: { policyClass: 'local_required' },
    data: { itemCount: 3 },
    evidence: [{ title: '公告1' }, { title: '公告2' }],
    degradedFlags: []
  },
  search: {
    toolName: 'StockSearchMcp',
    traceId: 'search-trace',
    taskId: 'stock-copilot-dev-search',
    data: { provider: 'tavily', resultCount: 0 },
    degradedFlags: ['external_search_unavailable'],
    warnings: ['未启用']
  }
}

const createFetchMock = () =>
  vi.fn(async url => {
    const target = String(url)
    if (target.includes('/api/stocks/agents/replay/baseline')) {
      return makeResponse(basePayloads.replay)
    }
    if (target.includes('/api/stocks/mcp/kline')) {
      return makeResponse(basePayloads.kline)
    }
    if (target.includes('/api/stocks/mcp/minute')) {
      return makeResponse(basePayloads.minute)
    }
    if (target.includes('/api/stocks/mcp/strategy')) {
      return makeResponse(basePayloads.strategy)
    }
    if (target.includes('/api/stocks/mcp/news')) {
      return makeResponse(basePayloads.news)
    }
    if (target.includes('/api/stocks/mcp/search')) {
      return makeResponse(basePayloads.search)
    }
    throw new Error(`Unexpected fetch: ${target}`)
  })

describe('StockCopilotDeveloperMode', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('loads all stock copilot diagnostics on mount', async () => {
    const fetchMock = createFetchMock()

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockCopilotDeveloperMode)
    await flushPromises()

    expect(fetchMock).toHaveBeenCalledTimes(6)
    expect(wrapper.text()).toContain('股票 Copilot 开发模式')
    expect(wrapper.text()).toContain('StockKlineMcp')
    expect(wrapper.text()).toContain('morning_session')
    expect(wrapper.text()).toContain('external_search_unavailable')
    expect(wrapper.text()).toContain('guardrail_penalty_applied')
    expect(wrapper.text()).toContain('已加载模块')
    expect(wrapper.text()).toContain('请求预览')
  })

  it('sanitizes form values before reloading diagnostics', async () => {
    const fetchMock = createFetchMock()

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockCopilotDeveloperMode)
    await flushPromises()

    const inputs = wrapper.findAll('input')
    await inputs[0].setValue('  sz000001  ')
    await inputs[1].setValue('5')
    await inputs[2].setValue('')
    await inputs[3].setValue('   ')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    const targets = fetchMock.mock.calls.map(call => String(call[0]))
    expect(targets.some(call => call.includes('symbol=sz000001'))).toBe(true)
    expect(targets.some(call => call.includes('count=20'))).toBe(true)
    expect(targets.some(call => call.includes('strategies=ma%2Cmacd%2Crsi%2Ckdj%2Cvwap%2Ctd%2Cbreakout%2Cgap'))).toBe(true)
    expect(
      targets.some(call => {
        const url = new URL(call, 'http://localhost')
        return url.searchParams.get('q') === 'sz000001 最新公告'
      })
    ).toBe(true)
    expect(inputs[0].element.value).toBe('sz000001')
    expect(inputs[1].element.value).toBe('20')
  })

  it('blocks reload and shows validation error when symbol is empty', async () => {
    const fetchMock = createFetchMock()
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockCopilotDeveloperMode)
    await flushPromises()

    fetchMock.mockClear()

    const inputs = wrapper.findAll('input')
    await inputs[0].setValue('   ')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(fetchMock).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('symbol 不能为空')
  })

  it('shows loading state during refresh and surfaces request failures', async () => {
    const firstBatch = Array.from({ length: 6 }, () => pending())
    let requestIndex = 0
    const fetchMock = vi.fn(() => {
      const current = firstBatch[requestIndex]
      requestIndex += 1
      return current.promise
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockCopilotDeveloperMode)
  await nextTick()

    expect(wrapper.text()).toContain('加载中')
    expect(wrapper.find('button').attributes('disabled')).toBeDefined()

    firstBatch[0].resolve(makeResponse(basePayloads.replay))
    firstBatch[1].resolve(makeResponse(basePayloads.kline))
    firstBatch[2].resolve(makeResponse(basePayloads.minute))
    firstBatch[3].resolve(makeResponse(basePayloads.strategy))
    firstBatch[4].resolve(makeResponse(basePayloads.news))
    firstBatch[5].reject(new Error('搜索服务失败'))
    await flushPromises()

    expect(wrapper.text()).toContain('搜索服务失败')
    expect(wrapper.find('button').attributes('disabled')).toBeUndefined()
  })
})