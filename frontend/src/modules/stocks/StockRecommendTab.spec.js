import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { nextTick } from 'vue'
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

const flushPromises = async () => {
  await Promise.resolve()
  await Promise.resolve()
}

if (!window.HTMLElement.prototype.scrollIntoView) {
  Object.defineProperty(window.HTMLElement.prototype, 'scrollIntoView', {
    configurable: true,
    value() {}
  })
}

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

const createSessionSummary = ({
  id,
  status = 'Completed',
  lastUserIntent = `prompt-${id}`,
  activeTurnId = null,
  createdAt = '2026-04-01T10:00:00Z',
  updatedAt = '2026-04-01T10:00:00Z'
} = {}) => ({
  id,
  status,
  lastUserIntent,
  activeTurnId,
  createdAt,
  updatedAt
})

const createSessionDetail = ({
  id,
  status = 'Completed',
  activeTurnId = null,
  turns = [],
  feedItems = [],
  lastUserIntent = `detail-${id}`,
  createdAt = '2026-04-01T10:00:00Z',
  updatedAt = '2026-04-01T10:05:00Z'
} = {}) => ({
  id,
  status,
  activeTurnId,
  lastUserIntent,
  turns,
  feedItems,
  createdAt,
  updatedAt
})

const createTurn = ({
  id,
  turnIndex = 0,
  status = 'Completed',
  requestedAt = '2026-04-01T10:00:00Z',
  startedAt = requestedAt,
  completedAt = null,
  stageSnapshots = [],
  feedItems = []
} = {}) => ({
  id,
  turnIndex,
  userPrompt: `turn-${id}`,
  status,
  continuationMode: 'NewSession',
  requestedAt,
  startedAt,
  completedAt,
  stageSnapshots,
  feedItems
})

const createEventSourceController = () => {
  const instances = []
  const EventSourceMock = vi.fn(url => {
    const instance = {
      url,
      onopen: null,
      onmessage: null,
      onerror: null,
      close: vi.fn()
    }
    instances.push(instance)
    return instance
  })

  return { EventSourceMock, instances }
}

const createDeferredResponse = () => {
  let resolve
  const promise = new Promise(res => {
    resolve = res
  })

  return { promise, resolve }
}

beforeEach(() => {
  vi.restoreAllMocks()
  localStorage.clear()
  vi.stubGlobal('EventSource', vi.fn(() => ({
    onmessage: null,
    onerror: null,
    onopen: null,
    close: vi.fn(),
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
  })))
})

afterEach(() => {
  vi.useRealTimers()
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

  it('renders the no-session guide instead of tabs when idle', async () => {
    vi.stubGlobal('fetch', defaultFetchMock())
    const wrapper = mount(StockRecommendTab)
    await flushPromises()

    expect(wrapper.find('.recommend-empty-guide').exists()).toBe(true)
    expect(wrapper.text()).toContain('智能选股推荐')
    expect(wrapper.text()).toContain('在下方输入你的投资问题，或点击快捷按钮开始分析。')
    expect(wrapper.findAll('.tab-btn')).toHaveLength(0)
  })

  it('keeps report tab content hidden while showing the no-session guide', async () => {
    vi.stubGlobal('fetch', defaultFetchMock())
    const wrapper = mount(StockRecommendTab)
    await flushPromises()

    expect(wrapper.find('.recommend-empty-guide').exists()).toBe(true)
    expect(wrapper.text()).toContain('系统将调度 13 个 AI 角色，通过多阶段辩论为你筛选优质标的。')
    expect(wrapper.text()).not.toContain('推荐报告尚未生成')
    expect(wrapper.findAll('.tab-btn')).toHaveLength(0)
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

  it('clears the previous completed report, progress, and feed while a new session detail is still loading', async () => {
    const completedStageSnapshots = [
      { stageType: 'MarketScan', status: 'Completed', roleStates: [] },
      { stageType: 'SectorDebate', status: 'Completed', roleStates: [] },
      { stageType: 'StockPicking', status: 'Completed', roleStates: [] },
      { stageType: 'StockDebate', status: 'Completed', roleStates: [] },
      {
        stageType: 'FinalDecision',
        status: 'Completed',
        roleStates: [
          {
            roleId: 'recommend_director',
            status: 'Completed',
            outputContentJson: JSON.stringify({
              marketSentiment: 'bullish',
              overallConfidence: 0.81,
              summary: '旧会话报告摘要',
              stockCards: [
                {
                  symbol: 'sh600519',
                  name: '旧会话示例股',
                  pickType: 'leader',
                  reason: '旧会话理由'
                }
              ]
            })
          }
        ]
      }
    ]

    const completedDetail = createSessionDetail({
      id: 5,
      status: 'Completed',
      activeTurnId: 501,
      turns: [createTurn({
        id: 501,
        turnIndex: 0,
        status: 'Completed',
        requestedAt: '2026-04-01T10:00:00Z',
        startedAt: '2026-04-01T10:00:05Z',
        completedAt: '2026-04-01T10:00:40Z',
        stageSnapshots: completedStageSnapshots,
        feedItems: [
          {
            id: 1,
            turnId: 501,
            itemType: 'RoleMessage',
            roleId: 'recommend_director',
            content: '旧会话辩论内容'
          }
        ]
      })]
    })

    const newDetailDeferred = createDeferredResponse()
    const nextRunningDetail = createSessionDetail({
      id: 99,
      status: 'Running',
      activeTurnId: 901,
      turns: [createTurn({
        id: 901,
        turnIndex: 0,
        status: 'Pending',
        requestedAt: '2026-04-01T10:01:00Z',
        startedAt: null,
        completedAt: null,
        stageSnapshots: [],
        feedItems: []
      })]
    })

    const fetchMock = defaultFetchMock({
      sessions: [createSessionSummary({ id: 5, status: 'Completed', activeTurnId: 501, lastUserIntent: '旧会话' })],
      handler: async (url, opts) => {
        if (url === '/api/recommend/sessions/5') {
          return makeResponse({ ok: true, status: 200, json: async () => completedDetail })
        }

        if (url === '/api/recommend/sessions' && opts?.method === 'POST') {
          return makeResponse({ ok: true, status: 201, json: async () => ({ id: 99, sessionKey: 'sk-99', turnId: 901 }) })
        }

        if (url === '/api/recommend/sessions/99') {
          return newDetailDeferred.promise
        }

        return makeResponse({ ok: true, status: 200, json: async () => ({}) })
      }
    })
    vi.stubGlobal('fetch', fetchMock)

    const { EventSourceMock } = createEventSourceController()
    vi.stubGlobal('EventSource', EventSourceMock)

    const wrapper = mount(StockRecommendTab)
    await flushPromises()
    await flushPromises()

    await wrapper.find('.session-item').trigger('click')
    await flushPromises()
    await flushPromises()

    expect(wrapper.text()).toContain('旧会话报告摘要')
    expect(wrapper.text()).toContain('旧会话示例股')

    await wrapper.findAll('.tab-btn')[1].trigger('click')
    await nextTick()
    expect(wrapper.text()).toContain('旧会话辩论内容')

    await wrapper.findAll('.tab-btn')[2].trigger('click')
    await nextTick()
    expect(wrapper.findAll('.stage-status').map(node => node.text())).toEqual([
      '已完成',
      '已完成',
      '已完成',
      '已完成',
      '已完成'
    ])

    const startNewRecommend = wrapper.vm.$.setupState.handleNewRecommend
    const pendingCreate = startNewRecommend('启动一轮全新推荐')
    for (let index = 0; index < 6; index += 1) {
      if (fetchMock.mock.calls.some(([url, opts]) => url === '/api/recommend/sessions/99' && (!opts || opts.method == null))) {
        break
      }
      await flushPromises()
      await nextTick()
    }

    expect(wrapper.find('.status-banner').exists()).toBe(true)
    expect(fetchMock.mock.calls.some(([url, opts]) => url === '/api/recommend/sessions/99' && (!opts || opts.method == null))).toBe(true)
    expect(wrapper.text()).toContain('会话已创建，正在连接分析流...')
    expect(wrapper.text()).toContain('正在准备分析团队...')
    expect(wrapper.findAll('.stage-status')).toHaveLength(0)
    expect(wrapper.text()).not.toContain('旧会话报告摘要')
    expect(wrapper.text()).not.toContain('旧会话示例股')
    expect(wrapper.text()).not.toContain('旧会话辩论内容')

    await wrapper.findAll('.tab-btn')[0].trigger('click')
    await nextTick()
    expect(wrapper.text()).toContain('推荐报告尚未生成，请等待分析完成。')
    expect(wrapper.text()).not.toContain('旧会话报告摘要')

    await wrapper.findAll('.tab-btn')[1].trigger('click')
    await nextTick()
    expect(wrapper.text()).toContain('辩论过程暂无记录。启动推荐后将实时显示各角色发言。')
    expect(wrapper.text()).not.toContain('旧会话辩论内容')

    newDetailDeferred.resolve(makeResponse({ ok: true, status: 200, json: async () => nextRunningDetail }))
    await pendingCreate
    await flushPromises()
    await flushPromises()
    wrapper.unmount()
  })

  it('renders recommendation empty guide when no session', async () => {
    vi.stubGlobal('fetch', defaultFetchMock())
    const wrapper = mount(StockRecommendTab)
    await flushPromises()

    expect(wrapper.find('.recommend-empty-guide').exists()).toBe(true)
    expect(wrapper.text()).toContain('智能选股推荐')
    expect(wrapper.text()).not.toContain('推荐报告尚未生成')
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

  it('selecting a completed session after a running session clears executing state and unlocks actions', async () => {
    vi.useFakeTimers()

    const runningSummary = createSessionSummary({ id: 1, status: 'Running', activeTurnId: 101, lastUserIntent: 'running-session' })
    const completedSummary = createSessionSummary({ id: 2, status: 'Completed', activeTurnId: 201, lastUserIntent: 'completed-session' })

    const runningDetail = createSessionDetail({
      id: 1,
      status: 'Running',
      activeTurnId: 101,
      turns: [createTurn({
        id: 101,
        turnIndex: 0,
        status: 'Running',
        requestedAt: '2026-04-01T10:00:00Z',
        startedAt: '2026-04-01T10:00:05Z'
      })]
    })

    const completedDetail = createSessionDetail({
      id: 2,
      status: 'Completed',
      activeTurnId: 201,
      turns: [createTurn({
        id: 201,
        turnIndex: 0,
        status: 'Completed',
        requestedAt: '2026-04-01T10:10:00Z',
        startedAt: '2026-04-01T10:10:02Z',
        completedAt: '2026-04-01T10:10:20Z'
      })]
    })

    const fetchMock = defaultFetchMock({
      sessions: [runningSummary, completedSummary],
      handler: async (url) => {
        if (url === '/api/recommend/sessions/1') {
          return makeResponse({ ok: true, status: 200, json: async () => runningDetail })
        }
        if (url === '/api/recommend/sessions/2') {
          return makeResponse({ ok: true, status: 200, json: async () => completedDetail })
        }
        return makeResponse({ ok: true, status: 200, json: async () => ({}) })
      }
    })
    vi.stubGlobal('fetch', fetchMock)

    const { EventSourceMock, instances } = createEventSourceController()
    vi.stubGlobal('EventSource', EventSourceMock)

    const wrapper = mount(StockRecommendTab)
    await flushPromises()
    await flushPromises()

    await wrapper.findAll('.session-item')[0].trigger('click')
    await flushPromises()
    await flushPromises()

    expect(EventSourceMock).toHaveBeenCalledTimes(1)
    instances[0].onopen?.()
    await nextTick()

    expect(wrapper.find('.status-banner').classes()).toContain('status-running')
    expect(wrapper.find('.follow-up-send').text()).toBe('打断并发送')
    expect(wrapper.find('.session-history-head .session-new').attributes('disabled')).toBeDefined()

    await wrapper.findAll('.session-item')[1].trigger('click')
    await flushPromises()
    await flushPromises()

    expect(instances[0].close).toHaveBeenCalledTimes(1)
    expect(wrapper.find('.status-banner').classes()).toContain('status-completed')
    expect(wrapper.find('.status-banner').classes()).not.toContain('status-running')
    expect(wrapper.find('.follow-up-send').text()).toBe('发送')
    expect(wrapper.find('.session-history-head .session-new').attributes('disabled')).toBeUndefined()
  })

  it('keeps live running state and reconnects SSE for a loaded running session', async () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-01T10:10:10Z'))

    const runningSummary = createSessionSummary({ id: 3, status: 'Running', activeTurnId: 301, lastUserIntent: 'resume-running' })
    const runningDetail = createSessionDetail({
      id: 3,
      status: 'Running',
      activeTurnId: 301,
      turns: [createTurn({
        id: 301,
        turnIndex: 0,
        status: 'Running',
        requestedAt: '2026-04-01T10:09:58Z',
        startedAt: '2026-04-01T10:10:00Z'
      })]
    })

    const fetchMock = defaultFetchMock({
      sessions: [runningSummary],
      handler: async (url) => {
        if (url === '/api/recommend/sessions/3') {
          return makeResponse({ ok: true, status: 200, json: async () => runningDetail })
        }
        return makeResponse({ ok: true, status: 200, json: async () => ({}) })
      }
    })
    vi.stubGlobal('fetch', fetchMock)

    const { EventSourceMock, instances } = createEventSourceController()
    vi.stubGlobal('EventSource', EventSourceMock)

    const wrapper = mount(StockRecommendTab)
    await flushPromises()
    await flushPromises()

    await wrapper.find('.session-item').trigger('click')
    await flushPromises()
    await flushPromises()

    expect(EventSourceMock).toHaveBeenCalledWith('/api/recommend/sessions/3/events')
    expect(wrapper.find('.status-banner').classes()).toContain('status-connecting')

    instances[0].onopen?.()
    await nextTick()

    expect(wrapper.find('.status-banner').classes()).toContain('status-running')
    expect(wrapper.find('.follow-up-send').text()).toBe('打断并发送')
    expect(wrapper.find('.session-history-head .session-new').attributes('disabled')).toBeDefined()
    expect(Number.parseInt(wrapper.find('.status-elapsed').text(), 10)).toBeGreaterThanOrEqual(10)
  })

  it('rebinds follow-up reruns to the newest live turn instead of reusing the previous completed progress', async () => {
    const completedStageSnapshots = [
      { stageType: 'MarketScan', status: 'Completed', roleStates: [] },
      { stageType: 'SectorDebate', status: 'Completed', roleStates: [] },
      { stageType: 'StockPicking', status: 'Completed', roleStates: [] },
      { stageType: 'StockDebate', status: 'Completed', roleStates: [] },
      { stageType: 'FinalDecision', status: 'Completed', roleStates: [] }
    ]
    const completedFeedItems = completedStageSnapshots.map((snapshot, index) => ({
      id: index + 1,
      turnId: 701,
      eventType: 'StageCompleted',
      stageType: snapshot.stageType
    }))

    const completedTurn = createTurn({
      id: 701,
      turnIndex: 0,
      status: 'Completed',
      requestedAt: '2026-04-01T10:00:00Z',
      startedAt: '2026-04-01T10:00:05Z',
      completedAt: '2026-04-01T10:00:30Z',
      stageSnapshots: completedStageSnapshots,
      feedItems: completedFeedItems
    })
    const rerunTurn = createTurn({
      id: 702,
      turnIndex: 1,
      status: 'Queued',
      requestedAt: '2026-04-01T10:01:00Z',
      startedAt: null,
      completedAt: null,
      stageSnapshots: [],
      feedItems: []
    })

    const initialDetail = createSessionDetail({
      id: 7,
      status: 'Completed',
      activeTurnId: 701,
      turns: [completedTurn]
    })
    const rerunDetail = createSessionDetail({
      id: 7,
      status: 'Running',
      activeTurnId: 701,
      turns: [completedTurn, rerunTurn]
    })

    let detailRequestCount = 0
    const fetchMock = defaultFetchMock({
      sessions: [createSessionSummary({ id: 7, status: 'Completed', activeTurnId: 701, lastUserIntent: '半导体追问' })],
      handler: async (url, opts) => {
        if (url === '/api/recommend/sessions/7' && (!opts || opts.method == null)) {
          detailRequestCount += 1
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => (detailRequestCount === 1 ? initialDetail : rerunDetail)
          })
        }

        if (url === '/api/recommend/sessions/7/follow-up' && opts?.method === 'POST') {
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({ turnId: 702, turnIndex: 1, strategy: 'FullRerun', status: 'Queued' })
          })
        }

        return makeResponse({ ok: true, status: 200, json: async () => ({}) })
      }
    })
    vi.stubGlobal('fetch', fetchMock)

    const { EventSourceMock } = createEventSourceController()
    vi.stubGlobal('EventSource', EventSourceMock)

    const wrapper = mount(StockRecommendTab)
    await flushPromises()
    await flushPromises()

    await wrapper.find('.session-item').trigger('click')
    await flushPromises()
    await flushPromises()

    await wrapper.find('.follow-up-input').setValue('重新推荐')
    await wrapper.find('.follow-up-send').trigger('click')
    await flushPromises()
    await flushPromises()

    expect(wrapper.find('.status-banner').classes()).toContain('status-connecting')
    await wrapper.findAll('.tab-btn')[2].trigger('click')
    await nextTick()
    expect(wrapper.findAll('.stage-status').map(node => node.text())).toEqual([
      '待执行',
      '待执行',
      '待执行',
      '待执行',
      '待执行'
    ])
  })

  it('shows a terminal error state when the latest terminal turn has an unparsable director report', () => {
    const wrapper = mount(RecommendReportCard, {
      props: {
        session: {
          status: 'Completed',
          activeTurnId: 42,
          turns: [
            {
              id: 41,
              turnIndex: 0,
              status: 'Completed',
              stageSnapshots: [
                {
                  stageType: 'FinalDecision',
                  status: 'Completed',
                  roleStates: [
                    {
                      roleId: 'recommend_director',
                      status: 'Completed',
                      outputContentJson: JSON.stringify({
                        marketSentiment: 'bullish',
                        summary: '旧报告，不应在最新终态缺失时继续显示',
                        stockCards: [
                          { symbol: '600519', name: '贵州茅台', pickType: 'leader' }
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
              status: 'Completed',
              stageSnapshots: [
                {
                  stageType: 'FinalDecision',
                  status: 'Completed',
                  roleStates: [
                    {
                      roleId: 'recommend_director',
                      status: 'Completed',
                      outputContentJson: '{bad-json'
                    }
                  ]
                }
              ]
            }
          ]
        }
      }
    })

    expect(wrapper.find('.report-error-hint').exists()).toBe(true)
    expect(wrapper.text()).toContain('推荐总监报告缺失或无法解析')
    expect(wrapper.text()).not.toContain('推荐报告尚未生成，请等待分析完成')
    expect(wrapper.text()).not.toContain('贵州茅台')
  })
})
