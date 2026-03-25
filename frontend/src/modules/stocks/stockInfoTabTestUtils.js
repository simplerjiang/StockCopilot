import { mount } from '@vue/test-utils'

import StockInfoTab from './StockInfoTab.vue'

const createMockFunction = implementation => {
  const mockFn = async (...args) => {
    mockFn.mock.calls.push(args)
    return implementation(...args)
  }
  mockFn.mock = { calls: [] }
  mockFn.mockClear = () => {
    mockFn.mock.calls.length = 0
  }
  return mockFn
}

const makeResponse = ({ ok, status, json, text }) => ({
  ok,
  status,
  json: json || (async () => ([])),
  text: text || (async () => '')
})

const createRealtimeOverviewPayload = (symbol = 'sz000021', name = '深科技') => ({
  snapshotTime: '2026-03-19T07:00:00Z',
  indices: [
    {
      symbol,
      name,
      price: 31.1,
      change: 0.6,
      changePercent: 1.97,
      turnoverAmount: 456700000,
      timestamp: '2026-03-19T07:00:00Z'
    },
    {
      symbol: 'sh000001',
      name: '上证指数',
      price: 4006.55,
      change: -56.43,
      changePercent: -1.39,
      turnoverAmount: 935265000000,
      timestamp: '2026-03-19T07:00:00Z'
    },
    {
      symbol: 'sz399001',
      name: '深证成指',
      price: 13901.57,
      change: -286.4,
      changePercent: -2.02,
      turnoverAmount: 1175704000000,
      timestamp: '2026-03-19T07:00:00Z'
    },
    {
      symbol: 'sz399006',
      name: '创业板指',
      price: 3309.1,
      change: -37.1,
      changePercent: -1.11,
      turnoverAmount: 545209000000,
      timestamp: '2026-03-19T07:00:00Z'
    },
    {
      symbol: 'hsi',
      name: '恒生指数',
      price: 25277.32,
      change: -223.26,
      changePercent: -0.88,
      turnoverAmount: 0,
      timestamp: '2026-03-19T07:00:00Z'
    },
    {
      symbol: 'hstech',
      name: '恒生科技指数',
      price: 4872.38,
      change: -123.9,
      changePercent: -2.48,
      turnoverAmount: 0,
      timestamp: '2026-03-19T07:00:00Z'
    },
    {
      symbol: 'n225',
      name: '日经225',
      price: 53372.53,
      change: -1866.87,
      changePercent: -3.38,
      turnoverAmount: 0,
      timestamp: '2026-03-19T07:00:00Z'
    },
    {
      symbol: 'ndx',
      name: '纳斯达克',
      price: 21647.61,
      change: 122.01,
      changePercent: 0.57,
      turnoverAmount: 0,
      timestamp: '2026-03-19T07:00:00Z'
    },
    {
      symbol: 'spx',
      name: '标普500',
      price: 6506.48,
      change: -100.01,
      changePercent: -1.51,
      turnoverAmount: 0,
      timestamp: '2026-03-19T07:00:00Z'
    },
    {
      symbol: 'ftse',
      name: '英国富时100',
      price: 9918.33,
      change: 58.42,
      changePercent: 0.59,
      turnoverAmount: 0,
      timestamp: '2026-03-19T07:00:00Z'
    }
  ],
  mainCapitalFlow: {
    snapshotTime: '2026-03-19T07:00:00Z',
    amountUnit: '亿元',
    mainNetInflow: 12.34,
    superLargeOrderNetInflow: 5.67
  },
  northboundFlow: {
    snapshotTime: '2026-03-19T07:00:00Z',
    amountUnit: '亿元',
    totalNetInflow: 8.9,
    shanghaiNetInflow: 4.5,
    shenzhenNetInflow: 4.4
  },
  breadth: {
    tradingDate: '2026-03-19',
    advancers: 1234,
    decliners: 3210,
    flatCount: 88,
    limitUpCount: 56,
    limitDownCount: 12
  }
})

const createCopilotAcceptanceBaselinePayload = request => {
  const turn = request?.turn || {}
  const toolCalls = Array.isArray(turn.toolCalls) ? turn.toolCalls : []
  const toolExecutions = Array.isArray(request?.toolExecutions) ? request.toolExecutions : []
  const followUpActions = Array.isArray(turn.followUpActions) ? turn.followUpActions : []
  const approvedToolCallCount = toolCalls.filter(item => item.approvalStatus === 'approved').length
  const executedToolCallCount = toolExecutions.length
  const localExecutions = toolExecutions.filter(item => item.policyClass === 'local_required').length
  const externalExecutions = toolExecutions.filter(item => item.policyClass === 'external_gated').length
  const evidenceCoveredCount = toolExecutions.filter(item => Number(item.evidenceCount || 0) > 0).length
  const enabledActionCount = followUpActions.filter(item => item.enabled).length
  const averageLatencyMs = executedToolCallCount
    ? toolExecutions.reduce((sum, item) => sum + Number(item.latencyMs || 0), 0) / executedToolCallCount
    : 0
  const warningCount = toolExecutions.reduce((sum, item) => sum + ((item.warnings || []).length), 0)
  const degradedFlagCount = toolExecutions.reduce((sum, item) => sum + ((item.degradedFlags || []).length), 0)
  const percentage = (numerator, denominator) => (denominator ? Math.round((numerator * 10000) / denominator) / 100 : 0)

  return {
    symbol: request?.symbol || turn.symbol || 'sh600000',
    sessionKey: turn.sessionKey || 'copilot-sh600000',
    turnId: turn.turnId || 'turn-acceptance',
    generatedAt: '2026-03-24T02:30:00Z',
    overallScore: 82,
    approvedToolCallCount,
    executedToolCallCount,
    averageLatencyMs,
    warningCount,
    degradedFlagCount,
    highlights: [
      `本轮已执行 ${executedToolCallCount}/${approvedToolCallCount} 张已批准工具卡。`,
      'Replay 基线已有 4 条样本。'
    ],
    metrics: [
      { key: 'tool_efficiency', label: '工具效率', value: percentage(executedToolCallCount, approvedToolCallCount), unit: '%', status: 'good', description: '已执行工具数占已批准工具数的比例。' },
      { key: 'evidence_coverage', label: '证据覆盖率', value: percentage(evidenceCoveredCount, executedToolCallCount), unit: '%', status: 'good', description: '已执行工具中，实际返回 evidence 的占比。' },
      { key: 'local_first_hit', label: 'Local-First 命中率', value: percentage(localExecutions, executedToolCallCount), unit: '%', status: 'good', description: '本轮执行工具中，Local-First 工具的占比。' },
      { key: 'external_search_trigger', label: '外部搜索触发率', value: percentage(externalExecutions, executedToolCallCount), unit: '%', status: externalExecutions ? 'watch' : 'good', description: '本轮执行工具中，external-gated 搜索的占比。' },
      { key: 'final_answer_traceability', label: '最终回答可追溯度', value: percentage(evidenceCoveredCount, executedToolCallCount), unit: '%', status: 'good', description: '已执行工具中，具备 traceId 或 evidence 的结果占比。' },
      { key: 'action_quality', label: '动作卡就绪度', value: percentage(enabledActionCount, followUpActions.length), unit: '%', status: 'good', description: '当前动作卡中，已可执行动作的占比。' },
      { key: 'tool_latency', label: '工具延迟得分', value: executedToolCallCount ? 100 : 0, unit: '%', status: 'good', description: '根据平均工具延迟换算出的体验得分。' }
    ],
    replayBaseline: {
      scope: request?.symbol || turn.symbol || 'sh600000',
      generatedAt: '2026-03-24T02:30:00Z',
      sampleCount: 4,
      traceableEvidenceRate: 75,
      parseRepairRate: 25,
      pollutedEvidenceRate: 5,
      revisionCompletenessRate: 50,
      horizons: [
        {
          horizonDays: 5,
          sampleCount: 4,
          hitRate: 75,
          averageReturnPercent: 3.2,
          brierScore: 0.18,
          bullWinRate: 80,
          bearWinRate: 20,
          baseWinRate: 50
        }
      ],
      samples: []
    }
  }
}

const flushPromises = () => new Promise(resolve => setTimeout(resolve, 0))
const findVisibleChatWindow = wrapper => wrapper.findAllComponents({ name: 'ChatWindow' }).find(component => component.isVisible())
const findChatWindowForSymbol = (wrapper, symbolKey) =>
  wrapper.findAllComponents({ name: 'ChatWindow' }).find(component => String(component.props('historyKey') || '').startsWith(symbolKey))

const createDeferred = () => {
  let resolve
  let reject
  const promise = new Promise((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

const createAbortError = () => Object.assign(new Error('Aborted'), { name: 'AbortError' })

const createAbortableResponse = (deferred, factory, signal, onAbort) => new Promise((resolve, reject) => {
  if (signal?.aborted) {
    onAbort?.()
    reject(createAbortError())
    return
  }

  const abortHandler = () => {
    onAbort?.()
    reject(createAbortError())
  }

  signal?.addEventListener('abort', abortHandler, { once: true })
  deferred.promise.then(
    () => {
      signal?.removeEventListener?.('abort', abortHandler)
      resolve(factory())
    },
    error => {
      signal?.removeEventListener?.('abort', abortHandler)
      reject(error)
    }
  )
})

const createChatFetchMock = (handlers = {}) => {
  const sessionsBySymbol = {}
  const messagesBySession = {}

  const ensureSessions = symbol => {
    if (!sessionsBySymbol[symbol]) {
      sessionsBySymbol[symbol] = [
        { sessionKey: `${symbol}-1`, title: '默认会话' }
      ]
    }
    return sessionsBySymbol[symbol]
  }

  const fetchMock = createMockFunction(async (url, options = {}) => {
    if (handlers.handle) {
      const handled = await handlers.handle(url, options)
      if (handled) return handled
    }

    if (url.startsWith('/api/stocks/chat/sessions?')) {
      const params = new URLSearchParams(url.split('?')[1])
      const symbol = params.get('symbol') || ''
      const list = ensureSessions(symbol)
      return makeResponse({ ok: true, status: 200, json: async () => list })
    }

    if (url === '/api/stocks/chat/sessions' && options.method === 'POST') {
      const body = JSON.parse(options.body)
      const symbol = body.symbol
      const title = body.title || '默认会话'
      const key = `${symbol}-${Date.now()}`
      const entry = { sessionKey: key, title }
      sessionsBySymbol[symbol] = [entry, ...(sessionsBySymbol[symbol] || [])]
      return makeResponse({ ok: true, status: 200, json: async () => entry })
    }

    if (url.includes('/api/stocks/chat/sessions/') && url.endsWith('/messages')) {
      const sessionKey = url.split('/api/stocks/chat/sessions/')[1].replace('/messages', '')
      if (options.method === 'PUT') {
        const body = JSON.parse(options.body)
        messagesBySession[sessionKey] = body.messages || []
        return makeResponse({ ok: true, status: 200, json: async () => ({ status: 'ok' }) })
      }
      return makeResponse({ ok: true, status: 200, json: async () => messagesBySession[sessionKey] || [] })
    }

    if (url === '/api/stocks/sources') {
      return makeResponse({ ok: true, status: 200, json: async () => ([]) })
    }
    if (String(url).startsWith('/api/stocks/quote?')) {
      const params = new URLSearchParams(String(url).split('?')[1] || '')
      const symbol = params.get('symbol') || 'sz000021'
      return makeResponse({
        ok: true,
        status: 200,
        json: async () => ({
          symbol,
          name: symbol === 'sh600000' ? '浦发银行' : '深科技',
          price: symbol === 'sh600000' ? 10.1 : 31.1,
          change: 0,
          changePercent: 0,
          turnoverRate: 0,
          peRatio: 0,
          high: 0,
          low: 0,
          speed: 0,
          timestamp: '2026-03-18T02:00:00Z',
          news: [],
          indicators: []
        })
      })
    }
    if (String(url).startsWith('/api/stocks/chart?')) {
      const params = new URLSearchParams(String(url).split('?')[1] || '')
      const symbol = params.get('symbol') || 'sz000021'
      const price = symbol === 'sh600000' ? 10.1 : 31.1
      const includeQuote = params.get('includeQuote') !== 'false'
      const includeMinute = params.get('includeMinute') !== 'false'
      return makeResponse({
        ok: true,
        status: 200,
        json: async () => ({
          ...(includeQuote ? {
            quote: {
              symbol,
              name: symbol === 'sh600000' ? '浦发银行' : '深科技',
              price,
              change: 0,
              changePercent: 0
            }
          } : {}),
          kLines: [{ date: '2026-03-18', open: price, close: price, low: price, high: price, volume: 100 }],
          ...(includeMinute ? {
            minuteLines: [{ date: '2026-03-18', time: '09:31:00', price, averagePrice: price, volume: 12 }]
          } : {})
        })
      })
    }
    if (String(url).startsWith('/api/stocks/detail/cache?')) {
      const params = new URLSearchParams(String(url).split('?')[1] || '')
      const symbol = params.get('symbol') || 'sz000021'
      const price = symbol === 'sh600000' ? 10.1 : 31.1
      return makeResponse({
        ok: true,
        status: 200,
        json: async () => ({
          quote: {
            symbol,
            name: symbol === 'sh600000' ? '浦发银行' : '深科技',
            price,
            change: 0,
            changePercent: 0,
            timestamp: '2026-03-18T02:00:00Z'
          },
          messages: [],
          fundamentalSnapshot: null
        })
      })
    }
    if (String(url).startsWith('/api/stocks/messages?')) {
      return makeResponse({ ok: true, status: 200, json: async () => ([]) })
    }
    if (String(url).startsWith('/api/stocks/fundamental-snapshot?')) {
      return makeResponse({ ok: false, status: 404, json: async () => ({ message: 'not found' }) })
    }
    if (String(url).startsWith('/api/market/realtime/overview')) {
      const params = new URLSearchParams(String(url).split('?')[1] || '')
      const firstSymbol = (params.get('symbols') || '').split(',').find(Boolean) || 'sz000021'
      return makeResponse({ ok: true, status: 200, json: async () => createRealtimeOverviewPayload(firstSymbol) })
    }
    if (url === '/api/stocks/history') {
      return makeResponse({ ok: true, status: 200, json: async () => ([]) })
    }
    if (url.startsWith('/api/stocks/agents/history')) {
      return makeResponse({ ok: true, status: 200, json: async () => ([]) })
    }

    if (url.startsWith('/api/stocks/news/impact')) {
      return makeResponse({
        ok: true,
        status: 200,
        json: async () => ({
          summary: { positive: 0, neutral: 0, negative: 0, overall: '中性' },
          events: []
        })
      })
    }

    if (url === '/api/stocks/copilot/turns/draft' && options.method === 'POST') {
      const body = JSON.parse(options.body)
      return makeResponse({
        ok: true,
        status: 200,
        json: async () => ({
          sessionKey: body.sessionKey || `copilot-${body.symbol}`,
          symbol: body.symbol,
          title: body.sessionTitle || `${body.symbol} Copilot`,
          createdAt: '2026-03-23T02:00:00Z',
          updatedAt: '2026-03-23T02:00:00Z',
          turns: [
            {
              turnId: `turn-${Date.now()}`,
              sessionKey: body.sessionKey || `copilot-${body.symbol}`,
              symbol: body.symbol,
              userQuestion: body.question,
              createdAt: '2026-03-23T02:00:00Z',
              status: 'draft',
              plannerSummary: 'planner 已把问题拆成 2 个受控工具步骤。',
              governorSummary: body.allowExternalSearch
                ? 'governor 已放行当前 draft 中的本地工具调用。'
                : 'governor 已放行本地工具，外部搜索仍需显式授权。',
              marketContext: { stageLabel: '主升', mainlineSectorName: 'AI 算力' },
              planSteps: [
                {
                  stepId: 'planner-1',
                  owner: 'planner',
                  title: '确认问题与市场环境',
                  description: '先确认问题意图，再决定工具顺序。',
                  status: 'planned',
                  dependsOn: [],
                  toolName: null
                },
                {
                  stepId: 'tool-1',
                  owner: 'planner',
                  title: '读取 K 线结构',
                  description: '检查 60 日结构、支撑与压力。',
                  status: 'approved',
                  dependsOn: ['planner-1'],
                  toolName: 'StockKlineMcp'
                },
                {
                  stepId: 'tool-2',
                  owner: 'planner',
                  title: '读取本地新闻证据',
                  description: '核对本地公告和消息。',
                  status: 'approved',
                  dependsOn: ['planner-1'],
                  toolName: 'StockNewsMcp'
                }
              ],
              toolCalls: [
                {
                  callId: 'call-kline',
                  stepId: 'tool-1',
                  toolName: 'StockKlineMcp',
                  policyClass: 'local_required',
                  purpose: '检查价格结构与关键位',
                  inputSummary: `symbol=${body.symbol}; interval=day; count=60`,
                  approvalStatus: 'approved',
                  blockedReason: null
                },
                {
                  callId: 'call-news',
                  stepId: 'tool-2',
                  toolName: 'StockNewsMcp',
                  policyClass: 'local_required',
                  purpose: '读取 Local-First 证据链',
                  inputSummary: `symbol=${body.symbol}; level=stock`,
                  approvalStatus: 'approved',
                  blockedReason: null
                }
              ],
              toolResults: [],
              finalAnswer: {
                status: 'done',
                summary: '结构与本地证据已经收口，可继续进入后续动作。',
                groundingMode: 'local_first_grounded',
                confidenceScore: 0.74,
                needsToolExecution: false,
                constraints: [
                  '最终回答只能引用 tool result 或已保存 evidence 中出现的事实。',
                  'degradedFlags 会系统性压低 confidence 与动作强度。'
                ]
              },
              followUpActions: [
                {
                  actionId: 'action-news',
                  label: '查看本地新闻证据',
                  actionType: 'inspect_news',
                  toolName: 'StockNewsMcp',
                  description: '核对本地公告、消息与 readStatus。',
                  enabled: true,
                  blockedReason: null
                }
              ]
            }
          ]
        })
      })
    }

    if (url === '/api/stocks/copilot/acceptance/baseline' && options.method === 'POST') {
      const body = JSON.parse(options.body)
      return makeResponse({
        ok: true,
        status: 200,
        json: async () => createCopilotAcceptanceBaselinePayload(body)
      })
    }

    if (url.startsWith('/api/stocks/mcp/kline?')) {
      return makeResponse({
        ok: true,
        status: 200,
        json: async () => ({
          traceId: 'trace-kline',
          data: {
            bars: Array.from({ length: 60 }, (_, index) => ({ index })),
            keyLevels: {
              resistanceLevels: [32.1, 33.5],
              supportLevels: [29.8]
            }
          },
          evidence: [],
          features: [{ key: 'trendState', value: 'uptrend' }],
          warnings: [],
          degradedFlags: []
        })
      })
    }

    if (url.startsWith('/api/stocks/mcp/news?')) {
      return makeResponse({
        ok: true,
        status: 200,
        json: async () => ({
          traceId: 'trace-news',
          data: {
            itemCount: 2,
            latestPublishedAt: '2026-03-23T01:00:00Z'
          },
          evidence: [
            {
              source: '上交所公告',
              title: '浦发银行关于董事会决议的公告',
              url: 'https://example.com/notice',
              publishedAt: '2026-03-23T01:00:00Z',
              excerpt: '公告摘要',
              readMode: 'full_text',
              readStatus: 'read'
            }
          ],
          features: [{ key: 'itemCount', value: '2' }],
          warnings: [],
          degradedFlags: []
        })
      })
    }

    if (url.startsWith('/api/stocks/plans/alerts?')) {
      return makeResponse({ ok: true, status: 200, json: async () => ([]) })
    }

    if (url.startsWith('/api/news?')) {
      const params = new URLSearchParams(url.split('?')[1])
      const level = params.get('level') || 'stock'
      return makeResponse({
        ok: true,
        status: 200,
        json: async () => ({
          symbol: params.get('symbol') || '',
          level,
          sectorName: level === 'sector' ? '银行' : null,
          items: []
        })
      })
    }

    return makeResponse({ ok: false, status: 404 })
  })

  return { fetchMock, messagesBySession, sessionsBySymbol }
}

const createStockInfoTabCaseContext = ({ expect, vi }) => ({
  StockInfoTab,
  mount,
  createAbortableResponse,
  createChatFetchMock,
  createCopilotAcceptanceBaselinePayload,
  createDeferred,
  createRealtimeOverviewPayload,
  expect,
  findChatWindowForSymbol,
  findVisibleChatWindow,
  flushPromises,
  makeResponse,
  vi
})

const resetStockInfoTabTestEnvironment = vi => {
  vi.restoreAllMocks()
  vi.useRealTimers()
  localStorage.clear()
}

const installStockInfoTabCaseSuite = ({ beforeEach, describe, expect, it, vi }, casesFactory) => {
  beforeEach(() => {
    resetStockInfoTabTestEnvironment(vi)
  })

  describe('StockInfoTab', () => {
    for (const { title, run } of casesFactory(createStockInfoTabCaseContext({ expect, vi }))) {
      it(title, run)
    }
  })
}

export {
  StockInfoTab,
  mount,
  makeResponse,
  createRealtimeOverviewPayload,
  createCopilotAcceptanceBaselinePayload,
  flushPromises,
  findVisibleChatWindow,
  findChatWindowForSymbol,
  createDeferred,
  createAbortError,
  createAbortableResponse,
  createChatFetchMock,
  createStockInfoTabCaseContext,
  resetStockInfoTabTestEnvironment,
  installStockInfoTabCaseSuite
}
