import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import StockRecommendTab from './StockRecommendTab.vue'
import RecommendReportCard from './recommend/RecommendReportCard.vue'

const makeResponse = ({ ok, status, json, text }) => {
  const jsonFn = json || (async () => ({}))
  return {
    ok: ok ?? true,
    status: status ?? 200,
    json: jsonFn,
    text: text || (async () => JSON.stringify(await jsonFn()))
  }
}

const flushPromises = () => new Promise(resolve => setTimeout(resolve, 0))

const defaultFetchMock = (overrides = {}) => vi.fn(async (url, opts) => {
  if (url === '/api/market/realtime/overview') {
    return makeResponse({
      ok: true, status: 200,
      json: async () => ({
        snapshotTime: '2026-03-19T07:00:00Z',
        indices: [
          { symbol: 'sh000001', name: '上证指数', price: 3401.22, changePercent: 0.82 },
          { symbol: 'sz399001', name: '深证成指', price: 10888.12, changePercent: 1.15 }
        ],
        mainCapitalFlow: { mainNetInflow: 12.34 },
        northboundFlow: { totalNetInflow: 8.9 },
        breadth: { advancers: 3210, decliners: 1450, limitUpCount: 55, limitDownCount: 6 }
      })
    })
  }
  if (url === '/api/market/sectors/realtime?boardType=concept&take=8&sort=rank') {
    return makeResponse({
      ok: true, status: 200,
      json: async () => ({
        items: [
          { sectorCode: 'BK1', sectorName: '机器人', changePercent: 4.21, mainNetInflow: 1230000000, rankNo: 1 }
        ]
      })
    })
  }
  if (url === '/api/recommend/sessions' && (!opts || opts.method !== 'POST')) {
    return makeResponse({ ok: true, status: 200, json: async () => overrides.sessions || [] })
  }
  if (overrides.handler) return overrides.handler(url, opts)
  return makeResponse({ ok: true, status: 200, json: async () => ({}) })
})

beforeEach(() => {
  vi.restoreAllMocks()
  localStorage.clear()
})

describe('StockRecommendTab', () => {
  it('uses the latest successful director report when the newest turn has no report output', () => {
    const wrapper = mount(RecommendReportCard, {
      props: {
        session: {
          turns: [
            {
              id: 41,
              turnIndex: 0,
              stageSnapshots: [
                {
                  stageType: 'FinalDecision',
                  roleStates: [
                    {
                      roleId: 'recommend_director',
                      outputContentJson: JSON.stringify({
                        marketSentiment: 'bullish',
                        overallConfidence: 0.78,
                        stockCards: [
                          {
                            symbol: '600519',
                            name: '贵州茅台',
                            pickType: 'leader',
                            reason: '白酒龙头具备防御属性'
                          }
                        ]
                      })
                    }
                  ]
                }
              ]
            },
            {
              id: 42,
              turnIndex: 1,
              stageSnapshots: []
            }
          ]
        }
      }
    })

    expect(wrapper.text()).toContain('偏多')
    expect(wrapper.text()).toContain('置信度 78%')
    expect(wrapper.text()).toContain('贵州茅台')
    expect(wrapper.text()).not.toContain('推荐报告尚未生成')
  })

  it('renders market snapshot and sector board on mount', async () => {
    vi.stubGlobal('fetch', defaultFetchMock())
    const wrapper = mount(StockRecommendTab)
    await flushPromises()
    await flushPromises()

    expect(wrapper.text()).toContain('推荐前市场快照')
    expect(wrapper.text()).toContain('上证指数')
    expect(wrapper.text()).toContain('机器人')
    expect(wrapper.text()).toContain('主力 +12.34 亿')
  })

  it('renders tab bar with three tabs', async () => {
    vi.stubGlobal('fetch', defaultFetchMock())
    const wrapper = mount(StockRecommendTab)
    await flushPromises()

    const tabs = wrapper.findAll('.tab-btn')
    expect(tabs.length).toBe(3)
    expect(tabs[0].text()).toBe('推荐报告')
    expect(tabs[1].text()).toBe('辩论过程')
    expect(tabs[2].text()).toBe('团队进度')
  })

  it('switches tabs on click', async () => {
    vi.stubGlobal('fetch', defaultFetchMock())
    const wrapper = mount(StockRecommendTab)
    await flushPromises()

    const tabs = wrapper.findAll('.tab-btn')
    await tabs[1].trigger('click')
    expect(tabs[1].classes()).toContain('active')

    await tabs[2].trigger('click')
    expect(tabs[2].classes()).toContain('active')
  })

  it('shows follow-up quick action buttons', async () => {
    vi.stubGlobal('fetch', defaultFetchMock())
    const wrapper = mount(StockRecommendTab)
    await flushPromises()

    const quickBtns = wrapper.findAll('.quick-btn')
    expect(quickBtns.length).toBe(3)
    expect(quickBtns[0].text()).toBe('板块深挖')
    expect(quickBtns[1].text()).toBe('换方向')
    expect(quickBtns[2].text()).toBe('重新推荐')
  })

  it('loads session history on mount', async () => {
    const sessions = [
      { id: 1, createdAt: '2026-04-01T10:00:00Z', status: 'Completed' },
      { id: 2, createdAt: '2026-04-01T11:00:00Z', status: 'Running' }
    ]
    vi.stubGlobal('fetch', defaultFetchMock({ sessions }))
    const wrapper = mount(StockRecommendTab)
    await flushPromises()
    await flushPromises()

    const items = wrapper.findAll('.session-item')
    expect(items.length).toBe(2)
  })

  it('creates new recommendation session on button click', async () => {
    const fetchMock = defaultFetchMock({
      handler: async (url, opts) => {
        if (url === '/api/recommend/sessions' && opts?.method === 'POST') {
          return makeResponse({ ok: true, status: 201, json: async () => ({ id: 99, sessionKey: 'sk-99', turnId: 1 }) })
        }
        if (url === '/api/recommend/sessions/99') {
          return makeResponse({ ok: true, status: 200, json: async () => ({ id: 99, status: 'Running', turns: [] }) })
        }
        return makeResponse({ ok: true, status: 200, json: async () => [] })
      }
    })
    vi.stubGlobal('fetch', fetchMock)
    // Mock EventSource
    const mockEs = { onmessage: null, onerror: null, onopen: null, close: vi.fn() }
    vi.stubGlobal('EventSource', vi.fn(() => mockEs))

    const wrapper = mount(StockRecommendTab)
    await flushPromises()

    // Type a prompt into the input and press Enter to trigger handleNewRecommend
    const input = wrapper.find('.follow-up-input')
    await input.setValue('今天有什么值得关注的板块？')
    await input.trigger('keydown.enter')
    await flushPromises()
    await flushPromises()

    const postCall = fetchMock.mock.calls.find(([url, opts]) => url === '/api/recommend/sessions' && opts?.method === 'POST')
    expect(postCall).toBeTruthy()
  })

  it('renders report empty state when no session', async () => {
    vi.stubGlobal('fetch', defaultFetchMock())
    const wrapper = mount(StockRecommendTab)
    await flushPromises()

    expect(wrapper.text()).toContain('推荐报告尚未生成')
  })

  it('hides market snapshot when toggle clicked', async () => {
    vi.stubGlobal('fetch', defaultFetchMock())
    const wrapper = mount(StockRecommendTab)
    await flushPromises()
    await flushPromises()

    expect(wrapper.text()).toContain('上证指数')
    const toggleBtn = wrapper.findAll('button').find(b => b.text() === '隐藏快照')
    await toggleBtn.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('推荐前市场快照已隐藏')
  })
})
