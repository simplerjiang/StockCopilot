import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'

const toast = {
  success: vi.fn(),
  error: vi.fn(),
  info: vi.fn(),
  warning: vi.fn()
}

vi.mock('../../composables/useToast.js', () => ({
  useToast: () => toast
}))

import NewsArchiveTab from './NewsArchiveTab.vue'

const makeResponse = ({ ok = true, status = 200, json }) => {
  const jsonFn = json || (async () => ({}))
  return {
    ok,
    status,
    json: jsonFn,
    text: async () => JSON.stringify(await jsonFn())
  }
}

const makeArchiveJobEvent = ({
  timestamp = '2026-04-07T08:00:00Z',
  level = 'info',
  type = 'request',
  message = 'Archive job event',
  details = '',
  round = 0,
  retry = 0
} = {}) => ({
  timestamp,
  level,
  type,
  message,
  details,
  round,
  retry
})

const makeArchiveJobStatus = ({
  runId = 1,
  state = 'running',
  isRunning = state === 'running',
  completed = state === 'completed',
  requiresManualResume = false,
  rounds = 0,
  processed = { market: 0, sector: 0, stock: 0 },
  remaining = { market: 0, sector: 0, stock: 0 },
  stopReason = '',
  message = '',
  continuation,
  attentionMessage = '',
  consecutiveRecoverableFailures = 0,
  maxRecoverableFailures = 3,
  recentEvents = []
}) => ({
  runId,
  state,
  isRunning,
  processed,
  remaining,
  completed,
  requiresManualResume,
  rounds,
  stopReason,
  message,
  attentionMessage,
  consecutiveRecoverableFailures,
  maxRecoverableFailures,
  recentEvents,
  continuation: continuation || {
    mayContinueAutomatically: false,
    reasonCode: completed ? 'completed' : 'test'
  }
})

const flushPromises = () => new Promise(resolve => setTimeout(resolve, 0))

const idleArchiveJobStatus = () => makeArchiveJobStatus({
  runId: 0,
  state: 'idle',
  isRunning: false,
  completed: false,
  requiresManualResume: false,
  message: '尚未启动后台清洗任务。'
})

const createArchiveFetchMock = ({
  archivePayloads = [],
  statusPayloads = [],
  startPayloads = [],
  pausePayloads = [],
  restartPayloads = []
}) => {
  const counters = {
    archive: 0,
    status: 0,
    start: 0,
    pause: 0,
    restart: 0
  }

  const pick = (items, key) => {
    if (!items.length) {
      throw new Error(`No mock payload configured for ${key}.`)
    }

    const index = counters[key]
    counters[key] += 1
    return items[Math.min(index, items.length - 1)]
  }

  return vi.fn(async (url, options) => {
    if (url.startsWith('/api/news/archive?')) {
      return makeResponse({ json: async () => pick(archivePayloads, 'archive') })
    }

    if (url === '/api/news/archive/process-pending/status') {
      return makeResponse({ json: async () => pick(statusPayloads, 'status') })
    }

    if (url === '/api/news/archive/process-pending' && options?.method === 'POST') {
      return makeResponse({ json: async () => pick(startPayloads, 'start') })
    }

    if (url === '/api/news/archive/process-pending/pause' && options?.method === 'POST') {
      return makeResponse({ json: async () => pick(pausePayloads, 'pause') })
    }

    if (url === '/api/news/archive/process-pending/restart' && options?.method === 'POST') {
      return makeResponse({ json: async () => pick(restartPayloads, 'restart') })
    }

    throw new Error(`Unexpected fetch call: ${url}`)
  })
}

const flushFakeTimers = async () => {
  await Promise.resolve()
  await vi.advanceTimersByTimeAsync(0)
  await Promise.resolve()
}

beforeEach(() => {
  vi.restoreAllMocks()
  toast.success.mockReset()
  toast.error.mockReset()
  toast.info.mockReset()
  toast.warning.mockReset()
})

afterEach(() => {
  vi.useRealTimers()
})

describe('NewsArchiveTab', () => {
  it('loads archive items on mount and renders translated title badges', async () => {
    const fetchMock = vi.fn(async () => makeResponse({
      json: async () => ({
        page: 1,
        pageSize: 20,
        total: 1,
        items: [
          {
            level: 'market',
            symbol: '',
            sectorName: '大盘环境',
            title: 'Fed outlook keeps markets cautious',
            translatedTitle: '美联储前景偏谨慎，市场保持观望',
            source: 'CNBC Finance',
            sentiment: '中性',
            publishTime: '2026-03-13T08:00:00Z',
            aiTarget: '大盘',
            aiTags: ['宏观货币']
          }
        ]
      })
    }))
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(NewsArchiveTab)
    await flushPromises()
    await flushPromises()

    expect(fetchMock).toHaveBeenCalledWith('/api/news/archive?page=1&pageSize=20')
    expect(wrapper.text()).toContain('全量资讯库')
    expect(wrapper.text()).toContain('美联储前景偏谨慎，市场保持观望')
    expect(wrapper.text()).toContain('原题：Fed outlook keeps markets cautious')
    expect(wrapper.text()).toContain('宏观货币')
    expect(wrapper.text()).toContain('大盘')
  })

  it('submits keyword and filters to archive api', async () => {
    const fetchMock = vi.fn(async () => makeResponse({
      json: async () => ({ page: 1, pageSize: 20, total: 0, items: [] })
    }))
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(NewsArchiveTab)
    await flushPromises()

    await wrapper.find('input').setValue('银行')
    await wrapper.findAll('select')[0].setValue('sector')
    await wrapper.findAll('select')[1].setValue('利好')
    await flushPromises()

    const latestCall = fetchMock.mock.calls.at(-1)?.[0]
    expect(latestCall).toContain('/api/news/archive?')
    expect(latestCall).toContain('keyword=%E9%93%B6%E8%A1%8C')
    expect(latestCall).toContain('level=sector')
    expect(latestCall).toContain('sentiment=%E5%88%A9%E5%A5%BD')
  })

  it('starts background cleaning with a quick POST response and polls until completion', async () => {
    vi.useFakeTimers()
    const archivePayload = { page: 1, pageSize: 20, total: 0, items: [] }
    const fetchMock = createArchiveFetchMock({
      archivePayloads: [archivePayload, archivePayload],
      statusPayloads: [
        idleArchiveJobStatus(),
        makeArchiveJobStatus({
          runId: 9,
          state: 'running',
          isRunning: true,
          rounds: 1,
          processed: { market: 1, sector: 0, stock: 0 },
          remaining: { market: 0, sector: 1, stock: 0 },
          message: '后台清洗仍在进行中，已完成 1 轮，累计处理 1 条，正在继续下一批。',
          attentionMessage: '本轮已达到单次清洗上限（每个层级最多 1 条），已保存部分结果。',
          recentEvents: [
            makeArchiveJobEvent({
              type: 'request',
              message: '发送第 1 / 1 批清洗请求，共 2 条。',
              details: '{"provider":"active","model":"test-model","items":[{"id":"market:1"}]}'
            }),
            makeArchiveJobEvent({
              type: 'response',
              message: '第 1 / 1 批已收到模型响应。',
              details: '[{"id":"market:1","translatedTitle":"已清洗:market:1"}]'
            }),
            makeArchiveJobEvent({
              type: 'round',
              message: '第 1 轮完成，正在继续下一轮。',
              round: 1
            })
          ],
          continuation: { mayContinueAutomatically: true, reasonCode: 'round_budget_reached' }
        }),
        makeArchiveJobStatus({
          runId: 9,
          state: 'completed',
          isRunning: false,
          completed: true,
          rounds: 2,
          processed: { market: 1, sector: 1, stock: 0 },
          remaining: { market: 0, sector: 0, stock: 0 },
          message: '后台清洗已完成，共处理 2 条。',
          continuation: { mayContinueAutomatically: false, reasonCode: 'completed' }
        })
      ],
      startPayloads: [
        makeArchiveJobStatus({
          runId: 9,
          state: 'running',
          isRunning: true,
          rounds: 0,
          message: '后台清洗任务已启动，等待首轮结果。',
          continuation: { mayContinueAutomatically: true, reasonCode: 'running' }
        })
      ]
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(NewsArchiveTab)
    await flushFakeTimers()

    const processButton = wrapper.findAll('button').find(button => button.text().includes('批量清洗待处理'))
    await processButton.trigger('click')
    await flushFakeTimers()

    const runningButton = wrapper.find('.archive-process-button')
    expect(runningButton.text()).toContain('后台清洗启动中...')
    expect(runningButton.attributes('disabled')).toBeDefined()
    expect(runningButton.classes()).toContain('is-running')
    expect(wrapper.text()).toContain('暂停后台清洗')
    expect(wrapper.text()).toContain('后台清洗进行中')
    expect(wrapper.text()).toContain('后台任务已启动，正在等待首轮清洗结果。')
    expect(wrapper.text()).not.toContain('本轮后台清洗已结束')

    await vi.advanceTimersByTimeAsync(2000)
    await flushFakeTimers()

    expect(runningButton.text()).toContain('清洗中：已处理 1 条，剩余 1 条（第 1 轮）')
    expect(wrapper.text()).toContain('已处理 1 条（大盘 1 / 板块 0 / 个股 0）')
    expect(wrapper.text()).toContain('剩余 1 条（大盘 0 / 板块 1 / 个股 0）')
    expect(wrapper.text()).toContain('累计轮次 1')
    expect(wrapper.text()).toContain('页面正在自动刷新后台进度。')
    expect(wrapper.text()).toContain('本轮已达到单次清洗上限（每个层级最多 1 条），已保存部分结果。')
    expect(wrapper.find('.archive-process-events').exists()).toBe(true)
    expect(wrapper.text()).toContain('查看最近请求 / 响应详情（3 条，最新在上）')
    expect(wrapper.text()).toContain('发送第 1 / 1 批清洗请求，共 2 条。')
    expect(wrapper.text()).toContain('第 1 / 1 批已收到模型响应。')
    expect(wrapper.text()).toContain('第 1 轮完成，正在继续下一轮。')
    expect(wrapper.findAll('.archive-process-event-head strong').map(node => node.text())).toContain('累计轮次')
    const eventItems = wrapper.findAll('.archive-process-event')
    expect(eventItems[0].text()).toContain('第 1 轮完成，正在继续下一轮。')
    expect(eventItems[1].text()).toContain('第 1 / 1 批已收到模型响应。')
    expect(eventItems[2].text()).toContain('发送第 1 / 1 批清洗请求，共 2 条。')

    await vi.advanceTimersByTimeAsync(2000)
    await flushFakeTimers()

    const postCalls = fetchMock.mock.calls.filter(([url, options]) => url === '/api/news/archive/process-pending' && options?.method === 'POST')
    const statusCalls = fetchMock.mock.calls.filter(([url]) => url === '/api/news/archive/process-pending/status')
    expect(postCalls).toHaveLength(1)
    expect(statusCalls).toHaveLength(3)
    expect(toast.success).toHaveBeenCalledWith(expect.stringContaining('累计处理 2 条'), 5000)
    expect(toast.warning).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('最近一次清洗结果')
    expect(wrapper.text()).toContain('后台清洗完成')
    expect(wrapper.text()).toContain('本次后台任务共处理 2 条，当前没有剩余待清洗资讯。')
  })

  it('resumes polling on mount when a background archive job is already running', async () => {
    vi.useFakeTimers()
    const archivePayload = { page: 1, pageSize: 20, total: 0, items: [] }
    const fetchMock = createArchiveFetchMock({
      archivePayloads: [archivePayload, archivePayload],
      statusPayloads: [
        makeArchiveJobStatus({
          runId: 18,
          state: 'running',
          isRunning: true,
          rounds: 1,
          processed: { market: 1, sector: 0, stock: 0 },
          remaining: { market: 2, sector: 0, stock: 1 },
          message: '后台清洗仍在进行中，已完成 1 轮，累计处理 1 条，正在继续下一批。',
          continuation: { mayContinueAutomatically: true, reasonCode: 'round_budget_reached' }
        }),
        makeArchiveJobStatus({
          runId: 18,
          state: 'completed',
          isRunning: false,
          completed: true,
          rounds: 2,
          processed: { market: 1, sector: 0, stock: 1 },
          remaining: { market: 0, sector: 0, stock: 0 },
          message: '后台清洗已完成，共处理 2 条。',
          continuation: { mayContinueAutomatically: false, reasonCode: 'completed' }
        })
      ],
      startPayloads: []
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(NewsArchiveTab)
    await flushFakeTimers()

    const runningButton = wrapper.find('.archive-process-button')
    expect(runningButton.text()).toContain('清洗中：已处理 1 条，剩余 3 条（第 1 轮）')
    expect(runningButton.attributes('disabled')).toBeDefined()
    expect(runningButton.classes()).toContain('is-running')
    expect(wrapper.text()).toContain('暂停后台清洗')
    expect(wrapper.text()).toContain('后台清洗进行中')
    expect(wrapper.text()).toContain('已处理 1 条（大盘 1 / 板块 0 / 个股 0）')
    expect(wrapper.text()).toContain('剩余 3 条（大盘 2 / 板块 0 / 个股 1）')
    expect(wrapper.text()).toContain('页面正在自动刷新后台进度。')
    expect(fetchMock.mock.calls.filter(([url, options]) => url === '/api/news/archive/process-pending' && options?.method === 'POST')).toHaveLength(0)

    await vi.advanceTimersByTimeAsync(2000)
    await flushFakeTimers()

    expect(toast.success).toHaveBeenCalledWith(expect.stringContaining('累计处理 2 条'), 5000)
  })

  it('keeps loading copy neutral while the archive list is still loading during an active background job', async () => {
    vi.useFakeTimers()
    let resolveArchive
    const archivePayload = {
      page: 1,
      pageSize: 20,
      total: 3,
      items: [
        {
          level: 'market',
          title: 'Markets open mixed as cleanup continues',
          translatedTitle: '后台清洗继续时，市场早盘涨跌互现',
          source: 'Reuters',
          sentiment: '中性',
          publishTime: '2026-04-06T01:00:00Z',
          isAiProcessed: true
        }
      ]
    }

    const fetchMock = vi.fn(url => {
      if (url.startsWith('/api/news/archive?')) {
        return new Promise(resolve => {
          resolveArchive = resolve
        })
      }

      if (url === '/api/news/archive/process-pending/status') {
        return Promise.resolve(makeResponse({
          json: async () => makeArchiveJobStatus({
            runId: 31,
            state: 'running',
            isRunning: true,
            rounds: 2,
            processed: { market: 2, sector: 1, stock: 0 },
            remaining: { market: 0, sector: 2, stock: 1 },
            message: '后台清洗仍在进行中。',
            continuation: { mayContinueAutomatically: true, reasonCode: 'round_budget_reached' }
          })
        }))
      }

      throw new Error(`Unexpected fetch call: ${url}`)
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(NewsArchiveTab)
    await flushFakeTimers()

    const stats = wrapper.find('.archive-stats')
    expect(stats.text()).toContain('资讯库加载中')
    expect(stats.text()).toContain('统计与分页信息加载中')
    expect(wrapper.text()).not.toContain('共 0 条资讯')
    expect(wrapper.text()).not.toContain('第 1 / 1 页')
    expect(wrapper.text()).toContain('正在加载资讯库...')
    expect(wrapper.text()).toContain('后台清洗进行中')
    expect(wrapper.text()).toContain('已处理 3 条（大盘 2 / 板块 1 / 个股 0）')
    expect(wrapper.text()).toContain('剩余 3 条（大盘 0 / 板块 2 / 个股 1）')
    expect(wrapper.find('.archive-pagination').exists()).toBe(false)

    resolveArchive(makeResponse({ json: async () => archivePayload }))
    await flushFakeTimers()

    expect(wrapper.find('.archive-stats').text()).toContain('共 3 条资讯')
    expect(wrapper.find('.archive-stats').text()).toContain('第 1 / 1 页')
    expect(wrapper.text()).toContain('后台清洗继续时，市场早盘涨跌互现')
    expect(wrapper.find('.archive-pagination').exists()).toBe(true)

    wrapper.unmount()
  })

  it('keeps the archive job running and shows auto-retry details on the first recoverable no-progress round', async () => {
    vi.useFakeTimers()
    const archivePayload = { page: 1, pageSize: 20, total: 0, items: [] }
    const fetchMock = createArchiveFetchMock({
      archivePayloads: [archivePayload],
      statusPayloads: [
        idleArchiveJobStatus(),
        makeArchiveJobStatus({
          runId: 12,
          state: 'running',
          isRunning: true,
          completed: false,
          requiresManualResume: false,
          rounds: 1,
          processed: { market: 0, sector: 0, stock: 0 },
          remaining: { market: 1, sector: 1, stock: 1 },
          attentionMessage: '本轮批量清洗未取得进展，剩余待处理项保持未完成状态。',
          message: '后台清洗遇到可恢复问题，将在 0.5 秒后自动重试。',
          consecutiveRecoverableFailures: 1,
          maxRecoverableFailures: 3,
          recentEvents: [
            makeArchiveJobEvent({ type: 'warning', message: '本轮批量清洗未取得进展，剩余待处理项保持未完成状态。' }),
            makeArchiveJobEvent({ type: 'retry', message: '计划在 0.5 秒后自动重试。' })
          ],
          continuation: { mayContinueAutomatically: true, reasonCode: 'auto_retry' }
        })
      ],
      startPayloads: [
        makeArchiveJobStatus({
          runId: 12,
          state: 'running',
          isRunning: true,
          rounds: 0,
          message: '后台清洗任务已启动，等待首轮结果。',
          continuation: { mayContinueAutomatically: true, reasonCode: 'running' }
        })
      ]
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(NewsArchiveTab)
    await flushFakeTimers()

    const processButton = wrapper.findAll('button').find(button => button.text().includes('批量清洗待处理'))
    await processButton.trigger('click')
    await flushFakeTimers()

    await vi.advanceTimersByTimeAsync(2000)
    await flushFakeTimers()

    expect(wrapper.text()).toContain('后台清洗进行中')
    expect(wrapper.text()).toContain('后台清洗遇到可恢复问题，正在进行第 1 次自动重试。')
    expect(wrapper.text()).toContain('本轮批量清洗未取得进展，剩余待处理项保持未完成状态。')
    expect(wrapper.text()).toContain('本轮待处理资讯已保留。页面会继续自动重试，最多 3 次；可展开“最近请求 / 响应详情”查看模型原始返回。')
    expect(wrapper.text()).not.toContain('本轮后台清洗已结束')
    expect(wrapper.text()).not.toContain('继续后台清洗')
    expect(wrapper.find('.archive-process-events').exists()).toBe(true)
    expect(wrapper.findAll('.archive-process-event-head strong').map(node => node.text())).toContain('警告')

    wrapper.unmount()
  })

  it('surfaces retry exhaustion guidance with raw response and llm settings hints', async () => {
    vi.useFakeTimers()
    const archivePayload = { page: 1, pageSize: 20, total: 0, items: [] }
    const fetchMock = createArchiveFetchMock({
      archivePayloads: [archivePayload],
      statusPayloads: [
        makeArchiveJobStatus({
          runId: 19,
          state: 'failed',
          isRunning: false,
          completed: false,
          requiresManualResume: true,
          rounds: 3,
          processed: { market: 0, sector: 0, stock: 0 },
          remaining: { market: 1, sector: 1, stock: 0 },
          stopReason: '后台清洗已连续 3 轮未取得进展，待处理资讯已保留，已停止自动重试。',
          message: '后台清洗已连续 3 轮未取得进展，待处理资讯已保留，已停止自动重试。',
          attentionMessage: '本轮批量清洗未取得进展：模型返回 JSON 解析失败，待处理资讯已保留。',
          consecutiveRecoverableFailures: 3,
          maxRecoverableFailures: 3,
          recentEvents: [
            makeArchiveJobEvent({ type: 'response', message: '第 1 / 1 批已收到模型响应。', details: '[{"broken":true' }),
            makeArchiveJobEvent({ type: 'parse', level: 'warning', message: '模型返回 JSON 解析失败，已跳过该批次。', details: '[{"broken":true' }),
            makeArchiveJobEvent({ type: 'retry', level: 'error', message: '后台清洗已连续 3 轮未取得进展，待处理资讯已保留，已停止自动重试。', retry: 3 })
          ],
          continuation: { mayContinueAutomatically: false, reasonCode: 'retry_exhausted' }
        })
      ]
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(NewsArchiveTab)
    await flushFakeTimers()

    expect(wrapper.text()).toContain('清洗失败')
    expect(wrapper.text()).toContain('后台清洗已连续 3 轮未取得进展，待处理资讯已保留，已停止自动重试。')
    expect(wrapper.text()).toContain('本轮批量清洗未取得进展：模型返回 JSON 解析失败，待处理资讯已保留。')
    expect(wrapper.text()).toContain('可先展开“最近请求 / 响应详情”查看模型原始返回；若详情显示空响应、非 JSON 数组或 JSON 解析失败，请先检查 LLM 设置。再点击“继续后台清洗”或“重新开始清洗”。')
    expect(wrapper.text()).toContain('继续后台清洗')
    expect(wrapper.text()).toContain('重新开始清洗')

    wrapper.unmount()
  })

  it('pauses a running archive job and allows resuming from the paused state', async () => {
    vi.useFakeTimers()
    const archivePayload = { page: 1, pageSize: 20, total: 0, items: [] }
    const fetchMock = createArchiveFetchMock({
      archivePayloads: [archivePayload, archivePayload],
      statusPayloads: [idleArchiveJobStatus()],
      startPayloads: [
        makeArchiveJobStatus({
          runId: 40,
          state: 'running',
          isRunning: true,
          rounds: 0,
          remaining: { market: 1, sector: 1, stock: 0 },
          message: '后台清洗任务已启动，等待首轮结果。',
          continuation: { mayContinueAutomatically: true, reasonCode: 'running' }
        }),
        makeArchiveJobStatus({
          runId: 40,
          state: 'running',
          isRunning: true,
          rounds: 1,
          processed: { market: 1, sector: 0, stock: 0 },
          remaining: { market: 0, sector: 1, stock: 0 },
          message: '后台清洗已恢复，正在处理剩余资讯。',
          continuation: { mayContinueAutomatically: true, reasonCode: 'running' }
        })
      ],
      pausePayloads: [
        makeArchiveJobStatus({
          runId: 40,
          state: 'paused',
          isRunning: false,
          completed: false,
          requiresManualResume: true,
          rounds: 1,
          processed: { market: 1, sector: 0, stock: 0 },
          remaining: { market: 0, sector: 0, stock: 0 },
          message: '后台清洗已暂停。',
          attentionMessage: '当前批次处理完成后已进入暂停状态。',
          continuation: { mayContinueAutomatically: false, reasonCode: 'paused' }
        })
      ]
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(NewsArchiveTab)
    await flushFakeTimers()

    await wrapper.find('.archive-process-button').trigger('click')
    await flushFakeTimers()

    const pauseButton = wrapper.findAll('button').find(button => button.text().includes('暂停后台清洗'))
    expect(pauseButton).toBeTruthy()
    await pauseButton.trigger('click')
    await flushFakeTimers()

    expect(fetchMock.mock.calls.filter(([url, options]) => url === '/api/news/archive/process-pending/pause' && options?.method === 'POST')).toHaveLength(1)
    expect(wrapper.text()).toContain('后台清洗已暂停')
    expect(wrapper.text()).toContain('当前批次处理完成后已进入暂停状态。')
    expect(wrapper.find('.archive-process-button').text()).toContain('继续后台清洗')
    expect(wrapper.text()).toContain('重新开始清洗')
    expect(wrapper.text()).not.toContain('暂停后台清洗')
    expect(toast.info).toHaveBeenCalledWith('后台清洗已暂停，可继续或重新开始。')

    await wrapper.find('.archive-process-button').trigger('click')
    await flushFakeTimers()

    expect(fetchMock.mock.calls.filter(([url, options]) => url === '/api/news/archive/process-pending' && options?.method === 'POST')).toHaveLength(2)
    expect(wrapper.find('.archive-process-button').text()).toContain('清洗中：已处理 1 条，剩余 1 条（第 1 轮）')
    expect(wrapper.text()).toContain('暂停后台清洗')

    wrapper.unmount()
  })

  it('shows the restart control for paused archive work and starts a fresh run when clicked', async () => {
    vi.useFakeTimers()
    const archivePayload = { page: 1, pageSize: 20, total: 0, items: [] }
    const fetchMock = createArchiveFetchMock({
      archivePayloads: [archivePayload, archivePayload],
      statusPayloads: [
        makeArchiveJobStatus({
          runId: 51,
          state: 'paused',
          isRunning: false,
          completed: false,
          requiresManualResume: true,
          rounds: 2,
          processed: { market: 1, sector: 1, stock: 0 },
          remaining: { market: 0, sector: 0, stock: 2 },
          message: '后台清洗已暂停。',
          attentionMessage: '可从当前进度继续，或重新开始一次新的后台清洗。',
          continuation: { mayContinueAutomatically: false, reasonCode: 'paused' }
        })
      ],
      restartPayloads: [
        makeArchiveJobStatus({
          runId: 52,
          state: 'running',
          isRunning: true,
          rounds: 0,
          processed: { market: 0, sector: 0, stock: 0 },
          remaining: { market: 1, sector: 1, stock: 1 },
          message: '后台清洗任务已重新开始，等待首轮结果。',
          continuation: { mayContinueAutomatically: true, reasonCode: 'running' }
        })
      ]
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(NewsArchiveTab)
    await flushFakeTimers()

    expect(wrapper.find('.archive-process-button').text()).toContain('继续后台清洗')
    const restartButton = wrapper.findAll('button').find(button => button.text().includes('重新开始清洗'))
    expect(restartButton).toBeTruthy()

    await restartButton.trigger('click')
    await flushFakeTimers()

    expect(fetchMock.mock.calls.filter(([url, options]) => url === '/api/news/archive/process-pending/restart' && options?.method === 'POST')).toHaveLength(1)
    expect(wrapper.find('.archive-process-button').text()).toContain('后台清洗启动中...')
    expect(wrapper.text()).toContain('暂停后台清洗')
    expect(wrapper.text()).not.toContain('重新开始清洗')

    wrapper.unmount()
  })

  it('keeps the latest archive result visible until the user dismisses it', async () => {
    const archivePayload = { page: 1, pageSize: 20, total: 0, items: [] }
    const fetchMock = createArchiveFetchMock({
      archivePayloads: [archivePayload, archivePayload],
      statusPayloads: [idleArchiveJobStatus()],
      startPayloads: [
        makeArchiveJobStatus({
          runId: 24,
          state: 'stopped',
          isRunning: false,
          completed: false,
          requiresManualResume: true,
          rounds: 1,
          processed: { market: 0, sector: 0, stock: 0 },
          remaining: { market: 1, sector: 1, stock: 1 },
          stopReason: '本轮批量清洗未取得进展，剩余待处理项保持未完成状态。',
          message: '本轮批量清洗未取得进展，剩余待处理项保持未完成状态。',
          continuation: { mayContinueAutomatically: false, reasonCode: 'no_progress' }
        })
      ]
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(NewsArchiveTab)
    await flushPromises()
    await flushPromises()

    const processButton = wrapper.findAll('button').find(button => button.text().includes('批量清洗待处理'))
    await processButton.trigger('click')
    await flushPromises()
    await flushPromises()

    expect(wrapper.find('.archive-process-result').exists()).toBe(true)

    await wrapper.find('.archive-process-dismiss').trigger('click')

    expect(wrapper.find('.archive-process-result').exists()).toBe(false)
  })
})