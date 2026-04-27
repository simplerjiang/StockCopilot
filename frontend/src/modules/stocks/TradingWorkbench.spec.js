import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick, ref, defineComponent, toRef } from 'vue'
import TradingWorkbench from './workbench/TradingWorkbench.vue'
import TradingWorkbenchHeader from './workbench/TradingWorkbenchHeader.vue'
import TradingWorkbenchProgress from './workbench/TradingWorkbenchProgress.vue'
import TradingWorkbenchReport from './workbench/TradingWorkbenchReport.vue'
import TradingWorkbenchComposer from './workbench/TradingWorkbenchComposer.vue'
import TradingWorkbenchFeed from './workbench/TradingWorkbenchFeed.vue'
import { useTradingWorkbench, STAGES, STATUS_MAP } from './workbench/useTradingWorkbench.js'

const flushPromises = () => new Promise(resolve => setTimeout(resolve, 0))
const originalFetch = globalThis.fetch
const originalScrollIntoView = globalThis.HTMLElement?.prototype.scrollIntoView

const WorkbenchHarness = defineComponent({
  props: {
    symbol: { type: String, default: '' }
  },
  setup(props) {
    return useTradingWorkbench(toRef(props, 'symbol'))
  },
  template: '<div />'
})

function createJsonResponse(body, { status = 200, statusText = 'OK' } = {}) {
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText,
    text: async () => (body == null ? '' : JSON.stringify(body)),
    json: async () => body
  }
}

function createDeferred() {
  let resolve
  let reject
  const promise = new Promise((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

async function flushWorkbench() {
  await flushPromises()
  await nextTick()
  await flushPromises()
  await nextTick()
}

beforeEach(() => {
  vi.restoreAllMocks()
  if (globalThis.HTMLElement) {
    globalThis.HTMLElement.prototype.scrollIntoView = vi.fn()
  }
})
afterEach(() => {
  if (originalFetch) {
    globalThis.fetch = originalFetch
  } else {
    delete globalThis.fetch
  }
  if (globalThis.HTMLElement) {
    if (originalScrollIntoView) {
      globalThis.HTMLElement.prototype.scrollIntoView = originalScrollIntoView
    } else {
      delete globalThis.HTMLElement.prototype.scrollIntoView
    }
  }
})

// ── Header ────────────────────────────────────────────

describe('TradingWorkbenchHeader', () => {
  it('renders session and turn badges', () => {
    const wrapper = mount(TradingWorkbenchHeader, {
      props: {
        session: { id: 42 },
        activeTurn: { id: 7, turnIndex: 2 },
        sessionStatus: STATUS_MAP.Running,
        currentStage: '分析师团队',
        isRunning: true
      }
    })
    expect(wrapper.text()).toContain('S42')
    expect(wrapper.text()).toContain('T2')
    expect(wrapper.text()).toContain('执行中')
    expect(wrapper.text()).toContain('分析师团队')
    expect(wrapper.find('.pulse-dot').exists()).toBe(true)
  })

  it('shows error when provided', () => {
    const wrapper = mount(TradingWorkbenchHeader, {
      props: {
        sessionStatus: STATUS_MAP.Idle,
        error: 'Connection failed'
      }
    })
    expect(wrapper.find('.wb-error').exists()).toBe(true)
    expect(wrapper.text()).toContain('Connection failed')
  })

  it('emits refresh on button click', async () => {
    const wrapper = mount(TradingWorkbenchHeader, {
      props: { sessionStatus: STATUS_MAP.Idle }
    })
    await wrapper.find('.wb-refresh-btn').trigger('click')
    expect(wrapper.emitted('refresh')).toHaveLength(1)
  })
})

// ── Progress ──────────────────────────────────────────

describe('TradingWorkbenchProgress', () => {
  it('renders all 6 stages', () => {
    const stages = STAGES.map((s, i) => ({
      ...s,
      status: i < 3 ? 'Completed' : (i === 3 ? 'Running' : 'Pending'),
      roles: [],
      degradedFlags: []
    }))
    const wrapper = mount(TradingWorkbenchProgress, { props: { stages } })
    expect(wrapper.findAll('.wb-stage')).toHaveLength(6)
    expect(wrapper.text()).toContain('公司概览')
    expect(wrapper.text()).toContain('交易方案')
  })

  it('shows role list for running stage', () => {
    const stages = [{
      key: 'AnalystTeam', label: '分析师团队', icon: '📊',
      status: 'Running',
      roles: [
        { roleId: 'MarketAnalyst', roleLabel: '市场分析', status: 'Running' },
        { roleId: 'NewsAnalyst', roleLabel: '新闻分析', status: 'Completed' }
      ],
      degradedFlags: []
    }]
    const wrapper = mount(TradingWorkbenchProgress, { props: { stages } })
    expect(wrapper.findAll('.wb-role')).toHaveLength(2)
    expect(wrapper.text()).toContain('市场分析')
    expect(wrapper.text()).toContain('新闻分析')
  })

  it('renders collapsible MCP result panels', async () => {
    const stages = [{
      key: 'CompanyOverviewPreflight',
      label: '公司概览',
      icon: '🏢',
      status: 'Completed',
      roles: [{
        roleId: 'CompanyOverviewAnalyst',
        roleLabel: '公司概览',
        status: 'Completed',
        outputContentJson: JSON.stringify({ content: JSON.stringify({ summary: '公司基础画像完整' }) }),
        outputRefsJson: JSON.stringify([{ toolName: 'CompanyOverviewMcp', status: 'Completed', summary: '已获取基础信息', resultJson: JSON.stringify({ data: { name: '平安银行', peRatio: 8.2 } }) }])
      }],
      degradedFlags: []
    }]

    const wrapper = mount(TradingWorkbenchProgress, { props: { stages } })
    const buttons = wrapper.findAll('.wb-role-detail-btn')
    expect(buttons).toHaveLength(2)
    await buttons[1].trigger('click')
    expect(wrapper.text()).toContain('MCP 结果')
    expect(wrapper.text()).toContain('CompanyOverviewMcp')
    expect(wrapper.text()).toContain('平安银行')
  })

  it('shows degraded flags', () => {
    const stages = [{
      key: 'AnalystTeam', label: '分析师团队', icon: '📊',
      status: 'Completed',
      roles: [],
      degradedFlags: ['tool_timeout: kline data']
    }]
    const wrapper = mount(TradingWorkbenchProgress, { props: { stages } })
    expect(wrapper.find('.wb-degraded-flag').exists()).toBe(true)
    expect(wrapper.text()).toContain('tool_timeout')
  })

  it('shows empty state when no stages', () => {
    const wrapper = mount(TradingWorkbenchProgress, { props: { stages: [] } })
    expect(wrapper.text()).toContain('等待研究会话启动')
  })

  it('shows load failure state when progress is empty after an error', () => {
    const wrapper = mount(TradingWorkbenchProgress, {
      props: {
        stages: [],
        error: 'API 500: disk full'
      }
    })

    expect(wrapper.text()).toContain('研究进度加载失败')
    expect(wrapper.text()).not.toContain('等待研究会话启动')
  })
})

// ── Report ────────────────────────────────────────────

describe('TradingWorkbenchReport', () => {
  it('renders decision with rating and confidence', () => {
    const wrapper = mount(TradingWorkbenchReport, {
      props: {
        blocks: [],
        decision: {
          rating: 'buy',
          executiveSummary: '建议买入',
          confidence: 0.82,
          confidenceExplanation: '高证据覆盖'
        },
        nextActions: []
      }
    })
    expect(wrapper.text()).toContain('看好')
    expect(wrapper.text()).toContain('82%')
    expect(wrapper.text()).toContain('高证据覆盖')
  })

  it('renders report blocks with key points', () => {
    const wrapper = mount(TradingWorkbenchReport, {
      props: {
        blocks: [{
          id: 1, blockType: 'Market', status: 'Complete',
          headline: '市场趋势偏多',
          summary: '均线多头排列',
          keyPointsJson: '["MA20上穿MA60","成交量放大"]'
        }],
        decision: null,
        nextActions: []
      }
    })
    expect(wrapper.text()).toContain('市场趋势偏多')
    expect(wrapper.text()).toContain('MA20上穿MA60')
    expect(wrapper.text()).toContain('成交量放大')
  })

  it('renders object key points as readable localized text', () => {
    const wrapper = mount(TradingWorkbenchReport, {
      props: {
        blocks: [{
          id: 10,
          blockType: 'Fundamentals',
          status: 'Complete',
          keyPointsJson: '[{"peRatio":12.3,"volumeRatio":1.8,"shareholderCount":120000}]'
        }],
        decision: null,
        nextActions: []
      }
    })

    const text = wrapper.text()
    expect(text).toContain('市盈率')
    expect(text).toContain('量比')
    expect(text).toContain('股东户数')
    expect(text).not.toContain('{"peRatio"')
  })

  it('renders degraded block with badge', () => {
    const wrapper = mount(TradingWorkbenchReport, {
      props: {
        blocks: [{
          id: 2, blockType: 'News', status: 'Degraded',
          headline: '新闻获取异常'
        }],
        decision: null,
        nextActions: []
      }
    })
    expect(wrapper.find('.wb-block.block-degraded').exists()).toBe(true)
    expect(wrapper.text()).toContain('降级')
  })

  it('shows routing summary for follow-up turn', () => {
    const wrapper = mount(TradingWorkbenchReport, {
      props: {
        blocks: [],
        decision: null,
        nextActions: [],
        turnSummary: {
          continuationMode: 'PartialRerun',
          routingDecision: 'PartialRerun',
          routingStageIndex: 4,
          routingConfidence: 0.82,
          routingReasoning: '该追问聚焦风险控制。'
        }
      }
    })

    expect(wrapper.text()).toContain('追问路由')
    expect(wrapper.text()).toContain('局部重跑')
    expect(wrapper.text()).toContain('从 风险评估 开始')
    expect(wrapper.text()).toContain('82%')
  })

  it('renders nextActions and emits action on click', async () => {
    const wrapper = mount(TradingWorkbenchReport, {
      props: {
        blocks: [],
        decision: null,
        nextActions: [
          { actionType: 'ViewDailyChart', label: '看日K线', reasonSummary: '确认趋势' },
          { actionType: 'DraftTradingPlan', label: '起草交易计划', reasonSummary: '执行买入' }
        ]
      }
    })
    expect(wrapper.text()).toContain('看日K线')
    expect(wrapper.text()).toContain('起草交易计划')
    const btns = wrapper.findAll('.wb-action-btn')
    await btns[0].trigger('click')
    expect(wrapper.emitted('action')[0][0].actionType).toBe('ViewDailyChart')
  })

  it('shows empty state when no data', () => {
    const wrapper = mount(TradingWorkbenchReport, {
      props: { blocks: [], decision: null, nextActions: [] }
    })
    expect(wrapper.text()).toContain('暂无研究报告')
  })

  it('shows load failure state when report is empty after an error', () => {
    const wrapper = mount(TradingWorkbenchReport, {
      props: {
        blocks: [],
        decision: null,
        nextActions: [],
        error: 'API 500: disk full'
      }
    })

    expect(wrapper.text()).toContain('研究报告加载失败')
    expect(wrapper.text()).not.toContain('暂无研究报告')
  })

  it('shows evidence and counter-evidence tags', () => {
    const wrapper = mount(TradingWorkbenchReport, {
      props: {
        blocks: [{
          id: 3, blockType: 'PortfolioDecision', status: 'Complete',
          headline: '决策',
          evidenceRefsJson: '["均线多头","业绩增长"]',
          counterEvidenceRefsJson: '["估值偏高"]'
        }],
        decision: null,
        nextActions: []
      }
    })
    expect(wrapper.findAll('.wb-evidence-tag.positive')).toHaveLength(2)
    expect(wrapper.findAll('.wb-evidence-tag.negative')).toHaveLength(1)
  })
})

// ── Feed ──────────────────────────────────────────────

describe('TradingWorkbenchFeed', () => {
  it('groups items by turn', () => {
    const items = [
      { turnId: 1, type: 'StageTransition', summary: '开始公司概览' },
      { turnId: 1, type: 'RoleOutput', roleId: 'MarketAnalyst', summary: '市场分析完成' },
      { turnId: 2, type: 'UserFollowUp', summary: '追问风险' }
    ]
    const wrapper = mount(TradingWorkbenchFeed, { props: { items } })
    expect(wrapper.findAll('.feed-turn-group')).toHaveLength(2)
    // 2 visible items: 1 role bubble + 1 user bubble (StageTransition is hidden)
    const msgs = wrapper.findAll('.feed-divider, .feed-msg, .feed-tool, .feed-system')
    expect(msgs.length).toBe(2)
  })

  it('shows empty state', () => {
    const wrapper = mount(TradingWorkbenchFeed, { props: { items: [] } })
    expect(wrapper.text()).toContain('暂无讨论动态')
  })
})

// ── Composer ──────────────────────────────────────────

describe('TradingWorkbenchComposer', () => {
  it('emits submit with prompt and continuationMode', async () => {
    const wrapper = mount(TradingWorkbenchComposer, {
      props: { session: { id: 1 }, isRunning: false, symbol: 'sz000001' }
    })
    const textarea = wrapper.find('.wb-input')
    await textarea.setValue('分析该股票趋势')
    await wrapper.find('.wb-send-btn').trigger('click')
    const emitted = wrapper.emitted('submit')
    expect(emitted).toHaveLength(1)
    expect(emitted[0][0].prompt).toBe('分析该股票趋势')
    expect(emitted[0][0].options.continuationMode).toBe('ContinueSession')
  })

  it('disables input when running', () => {
    const wrapper = mount(TradingWorkbenchComposer, {
      props: { session: { id: 1 }, isRunning: true, symbol: 'sz000001' }
    })
    // While running, the send button is replaced by a cancel button so user
    // cannot submit a new prompt; placeholder text reflects the running state.
    expect(wrapper.find('.wb-send-btn').exists()).toBe(false)
    expect(wrapper.find('.wb-cancel-btn').exists()).toBe(true)
    expect(wrapper.find('.wb-input').attributes('placeholder')).toContain('分析进行中')
  })

  it('does not emit on empty prompt', async () => {
    const wrapper = mount(TradingWorkbenchComposer, {
      props: { session: null, isRunning: false, symbol: 'sz000001' }
    })
    await wrapper.find('.wb-send-btn').trigger('click')
    expect(wrapper.emitted('submit')).toBeUndefined()
  })

  it('shows mode selector when session exists', () => {
    const wrapper = mount(TradingWorkbenchComposer, {
      props: { session: { id: 1 }, isRunning: false, symbol: 'sz000001' }
    })
    expect(wrapper.find('.wb-mode-select').exists()).toBe(true)
  })

  it('hides mode selector when no session', () => {
    const wrapper = mount(TradingWorkbenchComposer, {
      props: { session: null, isRunning: false, symbol: 'sz000001' }
    })
    expect(wrapper.find('.wb-mode-select').exists()).toBe(false)
  })
})

// ── Composable ────────────────────────────────────────

describe('useTradingWorkbench', () => {
  it('exports STAGES with 6 entries', () => {
    expect(STAGES).toHaveLength(6)
    expect(STAGES[0].key).toBe('CompanyOverviewPreflight')
    expect(STAGES[5].key).toBe('PortfolioDecision')
  })

  it('exports STATUS_MAP for known statuses', () => {
    expect(STATUS_MAP.Running.label).toBe('执行中')
    expect(STATUS_MAP.Completed.label).toBe('已完成')
    expect(STATUS_MAP.Failed.label).toBe('失败')
  })

  it('clears stale completed state before a new submitted session detail loads', async () => {
    const oldDetail = {
      id: 12,
      status: 'Completed',
      turns: [{ id: 101, turnIndex: 3, status: 'Completed', userPrompt: '旧分析' }],
      feedItems: [{ id: 1, turnId: 101, type: 'RoleOutput', summary: '旧会话输出' }],
      stageSnapshots: [{ stageType: 'AnalystTeam', status: 'Completed', roleStates: [] }]
    }
    const newDetailDeferred = createDeferred()

    globalThis.fetch = vi.fn(async (input, init) => {
      const url = String(input)
      if (url === '/api/stocks/translations/json-keys') return createJsonResponse({})
      if (url.includes('/active-session?symbol=')) {
        return createJsonResponse({ sessionId: 12, status: 'Completed', sessionKey: 'old-session' })
      }
      if (url.endsWith('/sessions/12')) return createJsonResponse(oldDetail)
      if (url.endsWith('/turns/101/report')) {
        return createJsonResponse({
          blocks: [{ id: 'old-block', headline: '旧报告标题' }],
          finalDecision: { rating: 'buy', executiveSummary: '旧决策摘要' }
        })
      }
      if (url.endsWith('/turns') && init?.method === 'POST') {
        return createJsonResponse({ sessionId: 56 })
      }
      if (url.endsWith('/sessions/56')) return newDetailDeferred.promise
      if (url.endsWith('/turns/201/report')) {
        return createJsonResponse({
          blocks: [{ id: 'new-block', headline: '新报告标题' }],
          finalDecision: { rating: 'hold', executiveSummary: '新决策摘要' }
        })
      }
      throw new Error(`Unexpected fetch: ${url}`)
    })

    const wrapper = mount(WorkbenchHarness, { props: { symbol: 'sh600000' } })
    await flushWorkbench()

    expect(wrapper.vm.session.id).toBe(12)
    expect(wrapper.vm.activeTurn.id).toBe(101)
    expect(wrapper.vm.reportBlocks).toHaveLength(1)
    expect(wrapper.vm.reportBlocks[0].headline).toBe('旧报告标题')
    expect(wrapper.vm.stageSnapshots[1].status).toBe('Completed')
    expect(wrapper.vm.feedItems).toHaveLength(1)

    const submitPromise = wrapper.vm.submitFollowUp('开始新的研究')
    await flushWorkbench()

    expect(wrapper.vm.session.id).toBe(56)
    expect(wrapper.vm.session.status).toBe('Running')
    expect(wrapper.vm.sessionDetail).toBeNull()
    expect(wrapper.vm.activeTurn).toBeNull()
    expect(wrapper.vm.stageSnapshots).toEqual([])
    expect(wrapper.vm.currentStageName).toBeNull()
    expect(wrapper.vm.reportBlocks).toEqual([])
    expect(wrapper.vm.decision).toBeNull()
    expect(wrapper.vm.feedItems).toHaveLength(1)
    expect(wrapper.vm.feedItems[0].content).toBe('开始新的研究')
    expect(wrapper.vm.feedItems[0]._optimistic).toBe(true)

    newDetailDeferred.resolve(createJsonResponse({
      id: 56,
      status: 'Running',
      turns: [{ id: 201, turnIndex: 4, status: 'Running', userPrompt: '开始新的研究' }],
      feedItems: [{ id: 9, turnId: 201, type: 'UserFollowUp', content: '开始新的研究' }],
      stageSnapshots: [{ stageType: 'AnalystTeam', status: 'Running', roleStates: [] }]
    }))

    await submitPromise
    await flushWorkbench()

    expect(wrapper.vm.activeTurn.id).toBe(201)
    expect(wrapper.vm.reportBlocks).toHaveLength(1)
    expect(wrapper.vm.reportBlocks[0].headline).toBe('新报告标题')
    expect(wrapper.vm.feedItems).toHaveLength(1)
    expect(wrapper.vm.feedItems[0].content).toBe('开始新的研究')
    expect(wrapper.vm.feedItems[0]._optimistic).toBeUndefined()

    wrapper.unmount()
  })

  it('clears stale completed state before a rerun turn detail loads', async () => {
    const oldDetail = {
      id: 12,
      status: 'Completed',
      turns: [{ id: 101, turnIndex: 3, status: 'Completed', userPrompt: '旧分析' }],
      feedItems: [{ id: 1, turnId: 101, type: 'RoleOutput', summary: '旧会话输出' }],
      stageSnapshots: [{ stageType: 'AnalystTeam', status: 'Completed', roleStates: [] }]
    }
    const rerunDetailDeferred = createDeferred()
    let session12RequestCount = 0

    globalThis.fetch = vi.fn(async (input, init) => {
      const url = String(input)
      if (url === '/api/stocks/translations/json-keys') return createJsonResponse({})
      if (url.includes('/active-session?symbol=')) {
        return createJsonResponse({ sessionId: 12, status: 'Completed', sessionKey: 'old-session' })
      }
      if (url.endsWith('/sessions/12')) {
        session12RequestCount += 1
        return session12RequestCount === 1 ? createJsonResponse(oldDetail) : rerunDetailDeferred.promise
      }
      if (url.endsWith('/turns/101/report')) {
        return createJsonResponse({
          blocks: [{ id: 'old-block', headline: '旧报告标题' }],
          finalDecision: { rating: 'buy', executiveSummary: '旧决策摘要' }
        })
      }
      if (url.endsWith('/turns') && init?.method === 'POST') {
        return createJsonResponse({ sessionId: 12 })
      }
      if (url.endsWith('/turns/202/report')) {
        return createJsonResponse({
          blocks: [{ id: 'rerun-block', headline: '重跑报告标题' }],
          finalDecision: { rating: 'sell', executiveSummary: '重跑决策摘要' }
        })
      }
      throw new Error(`Unexpected fetch: ${url}`)
    })

    const wrapper = mount(WorkbenchHarness, { props: { symbol: 'sh600000' } })
    await flushWorkbench()

    expect(wrapper.vm.activeTurn.id).toBe(101)
    expect(wrapper.vm.stageSnapshots[1].status).toBe('Completed')
    expect(wrapper.vm.feedItems).toHaveLength(1)

    const rerunPromise = wrapper.vm.rerunFromStage(1)
    await flushWorkbench()

    expect(wrapper.vm.session.id).toBe(12)
    expect(wrapper.vm.session.status).toBe('Running')
    expect(wrapper.vm.activeTab).toBe('feed')
    expect(wrapper.vm.sessionDetail).toBeNull()
    expect(wrapper.vm.activeTurn).toBeNull()
    expect(wrapper.vm.stageSnapshots).toEqual([])
    expect(wrapper.vm.reportBlocks).toEqual([])
    expect(wrapper.vm.decision).toBeNull()
    expect(wrapper.vm.feedItems).toEqual([])

    rerunDetailDeferred.resolve(createJsonResponse({
      id: 12,
      status: 'Running',
      turns: [{ id: 202, turnIndex: 4, status: 'Running', userPrompt: '旧分析' }],
      feedItems: [{ id: 10, turnId: 202, type: 'TurnStarted', summary: '第 4 轮分析开始' }],
      stageSnapshots: [{ stageType: 'AnalystTeam', status: 'Running', roleStates: [] }]
    }))

    await rerunPromise
    await flushWorkbench()

    expect(wrapper.vm.activeTurn.id).toBe(202)
    expect(wrapper.vm.reportBlocks).toHaveLength(1)
    expect(wrapper.vm.reportBlocks[0].headline).toBe('重跑报告标题')
    expect(wrapper.vm.feedItems).toHaveLength(1)

    wrapper.unmount()
  })

  it('clears replay turn identity when returning to latest and the active-session refresh fails', async () => {
    let activeSessionCallCount = 0

    globalThis.fetch = vi.fn(async (input) => {
      const url = String(input)
      if (url === '/api/stocks/translations/json-keys') return createJsonResponse({})
      if (url.includes('/active-session?symbol=')) {
        activeSessionCallCount += 1
        if (activeSessionCallCount === 1) {
          return createJsonResponse({ sessionId: 77, status: 'Running', sessionKey: 'live-session' })
        }
        return createJsonResponse({ message: 'disk full' }, { status: 500, statusText: 'Internal Server Error' })
      }
      if (url.endsWith('/sessions/77')) {
        return createJsonResponse({
          id: 77,
          status: 'Running',
          turns: [{ id: 701, turnIndex: 5, status: 'Running', userPrompt: '最新研究' }],
          feedItems: [{ id: 1, turnId: 701, type: 'RoleOutput', summary: '最新会话输出' }],
          stageSnapshots: [{ stageType: 'AnalystTeam', status: 'Running', roleStates: [] }]
        })
      }
      if (url.endsWith('/turns/701/report')) {
        return createJsonResponse({
          blocks: [{ id: 'live-block', headline: '最新报告标题' }],
          finalDecision: { rating: 'buy', executiveSummary: '最新决策摘要' }
        })
      }
      if (url.endsWith('/sessions/12')) {
        return createJsonResponse({
          id: 12,
          status: 'Completed',
          turns: [
            { id: 201, turnIndex: 1, status: 'Completed', userPrompt: '历史回放' },
            { id: 202, turnIndex: 2, status: 'Completed', userPrompt: '另一条历史回放' }
          ],
          feedItems: [{ id: 2, turnId: 201, type: 'RoleOutput', summary: '历史会话输出' }],
          stageSnapshots: [{ stageType: 'AnalystTeam', status: 'Completed', roleStates: [] }]
        })
      }
      if (url.endsWith('/turns/201/report')) {
        return createJsonResponse({
          blocks: [{ id: 'replay-block', headline: '历史报告标题' }],
          finalDecision: { rating: 'sell', executiveSummary: '历史决策摘要' }
        })
      }
      throw new Error(`Unexpected fetch: ${url}`)
    })

    const wrapper = mount(WorkbenchHarness, { props: { symbol: 'sh600000' } })
    await flushWorkbench()

    expect(wrapper.vm.session.id).toBe(77)
    expect(wrapper.vm.activeTurn.id).toBe(701)
    expect(wrapper.vm.reportBlocks[0].headline).toBe('最新报告标题')

    await wrapper.vm.enterReplay(12, 201)
    await flushWorkbench()

    expect(wrapper.vm.session.id).toBe(12)
    expect(wrapper.vm.session.status).toBe('Completed')
    expect(wrapper.vm.sessionStatus.label).toBe('已完成')
    expect(wrapper.vm.replayTurnId).toBe(201)
    expect(wrapper.vm.activeTurn.id).toBe(201)
    expect(wrapper.vm.reportBlocks[0].headline).toBe('历史报告标题')

    const replayHeader = mount(TradingWorkbenchHeader, {
      props: {
        session: wrapper.vm.session,
        activeTurn: wrapper.vm.activeTurn,
        sessionStatus: wrapper.vm.sessionStatus,
        currentStage: wrapper.vm.currentStageName,
        isRunning: wrapper.vm.isRunning,
        error: wrapper.vm.error
      }
    })

    expect(replayHeader.text()).toContain('S12')
    expect(replayHeader.text()).toContain('T1')
    expect(replayHeader.text()).toContain('已完成')
    expect(replayHeader.text()).not.toContain('S77')

    replayHeader.unmount()

    wrapper.vm.exitReplay()
    await flushWorkbench()

    expect(wrapper.vm.session).toBeNull()
    expect(wrapper.vm.replayTurnId).toBeNull()
    expect(wrapper.vm.sessionDetail).toBeNull()
    expect(wrapper.vm.activeTurn).toBeNull()
    expect(wrapper.vm.reportBlocks).toEqual([])
    expect(wrapper.vm.decision).toBeNull()
    expect(wrapper.vm.feedItems).toEqual([])
    expect(wrapper.vm.error).toContain('API 500')

    const header = mount(TradingWorkbenchHeader, {
      props: {
        session: wrapper.vm.session,
        activeTurn: wrapper.vm.activeTurn,
        sessionStatus: wrapper.vm.sessionStatus,
        currentStage: wrapper.vm.currentStageName,
        isRunning: wrapper.vm.isRunning,
        error: wrapper.vm.error
      }
    })

    expect(header.find('.wb-session-badge').exists()).toBe(false)
    expect(header.find('.wb-status').text()).not.toContain('已完成')
    expect(header.text()).toContain('API 500')

    header.unmount()

    wrapper.unmount()
  })

  it('clears stale progress, report, and feed when the latest session detail refresh fails', async () => {
    let activeSessionCallCount = 0

    globalThis.fetch = vi.fn(async (input) => {
      const url = String(input)
      if (url === '/api/stocks/translations/json-keys') return createJsonResponse({})
      if (url.includes('/active-session?symbol=')) {
        activeSessionCallCount += 1
        return activeSessionCallCount === 1
          ? createJsonResponse({ sessionId: 12, status: 'Completed', sessionKey: 'old-session' })
          : createJsonResponse({ sessionId: 56, status: 'Running', sessionKey: 'new-session' })
      }
      if (url.endsWith('/sessions/12')) {
        return createJsonResponse({
          id: 12,
          status: 'Completed',
          turns: [{ id: 101, turnIndex: 3, status: 'Completed', userPrompt: '旧分析' }],
          feedItems: [{ id: 1, turnId: 101, type: 'RoleOutput', summary: '旧会话输出' }],
          stageSnapshots: [{ stageType: 'AnalystTeam', status: 'Completed', roleStates: [] }]
        })
      }
      if (url.endsWith('/turns/101/report')) {
        return createJsonResponse({
          blocks: [{ id: 'old-block', headline: '旧报告标题' }],
          finalDecision: { rating: 'buy', executiveSummary: '旧决策摘要' }
        })
      }
      if (url.endsWith('/sessions/56')) {
        return createJsonResponse({ message: 'disk full' }, { status: 500, statusText: 'Internal Server Error' })
      }
      throw new Error(`Unexpected fetch: ${url}`)
    })

    const wrapper = mount(WorkbenchHarness, { props: { symbol: 'sh600000' } })
    await flushWorkbench()

    expect(wrapper.vm.session.id).toBe(12)
    expect(wrapper.vm.activeTurn.id).toBe(101)
    expect(wrapper.vm.stageSnapshots[1].status).toBe('Completed')
    expect(wrapper.vm.reportBlocks[0].headline).toBe('旧报告标题')
    expect(wrapper.vm.feedItems).toHaveLength(1)

    await wrapper.vm.loadActiveSession()
    await flushWorkbench()

    expect(wrapper.vm.session.id).toBe(56)
    expect(wrapper.vm.session.status).toBe('Running')
    expect(wrapper.vm.sessionDetail).toBeNull()
    expect(wrapper.vm.activeTurn).toBeNull()
    expect(wrapper.vm.stageSnapshots).toEqual([])
    expect(wrapper.vm.currentStageName).toBeNull()
    expect(wrapper.vm.reportBlocks).toEqual([])
    expect(wrapper.vm.decision).toBeNull()
    expect(wrapper.vm.feedItems).toEqual([])
    expect(wrapper.vm.error).toContain('API 500')

    wrapper.unmount()
  })
})

// ── Main Container ────────────────────────────────────

describe('TradingWorkbench', () => {
  beforeEach(() => {
    globalThis.fetch = vi.fn(async (url) => ({
      ok: false, status: 404, json: async () => null
    }))
  })
  afterEach(() => { delete globalThis.fetch })

  it('renders empty state when no session for symbol', async () => {
    const wrapper = mount(TradingWorkbench, {
      props: { symbol: 'sz000001' }
    })
    await flushPromises()
    await nextTick()
    expect(wrapper.text()).toContain('多角色研究工作台')
  })

  it('renders all four tabs', () => {
    const wrapper = mount(TradingWorkbench, {
      props: { symbol: 'sz000001' }
    })
    const tabs = wrapper.findAll('.wb-tab')
    expect(tabs).toHaveLength(4)
    expect(tabs[0].text()).toContain('研究报告')
    expect(tabs[1].text()).toContain('团队进度')
    expect(tabs[2].text()).toContain('讨论动态')
    expect(tabs[3].text()).toContain('历史记录')
  })

  it('switches tabs on click', async () => {
    const wrapper = mount(TradingWorkbench, {
      props: { symbol: 'sz000001' }
    })
    const tabs = wrapper.findAll('.wb-tab')
    await tabs[1].trigger('click')
    expect(tabs[1].classes()).toContain('active')
  })

  it('shows explicit failure copy in report and progress after a session detail refresh error clears stale state', async () => {
    let activeSessionCallCount = 0

    globalThis.fetch = vi.fn(async (input) => {
      const url = String(input)
      if (url === '/api/stocks/translations/json-keys') return createJsonResponse({})
      if (url.includes('/active-session?symbol=')) {
        activeSessionCallCount += 1
        return activeSessionCallCount === 1
          ? createJsonResponse({ sessionId: 12, status: 'Completed', sessionKey: 'old-session' })
          : createJsonResponse({ sessionId: 56, status: 'Running', sessionKey: 'new-session' })
      }
      if (url.endsWith('/sessions/12')) {
        return createJsonResponse({
          id: 12,
          status: 'Completed',
          turns: [{ id: 101, turnIndex: 3, status: 'Completed', userPrompt: '旧分析' }],
          feedItems: [{ id: 1, turnId: 101, type: 'RoleOutput', summary: '旧会话输出' }],
          stageSnapshots: [{ stageType: 'AnalystTeam', status: 'Completed', roleStates: [] }]
        })
      }
      if (url.endsWith('/turns/101/report')) {
        return createJsonResponse({
          blocks: [{ id: 'old-block', headline: '旧报告标题' }],
          finalDecision: { rating: 'buy', executiveSummary: '旧决策摘要' }
        })
      }
      if (url.endsWith('/sessions/56')) {
        return createJsonResponse({ message: 'disk full' }, { status: 500, statusText: 'Internal Server Error' })
      }
      throw new Error(`Unexpected fetch: ${url}`)
    })

    const wrapper = mount(TradingWorkbench, {
      props: { symbol: 'sh600000' }
    })
    await flushWorkbench()

    await wrapper.find('.wb-refresh-btn').trigger('click')
    await flushWorkbench()

    expect(wrapper.findComponent(TradingWorkbenchReport).text()).toContain('研究报告加载失败')
    expect(wrapper.findComponent(TradingWorkbenchProgress).text()).toContain('研究进度加载失败')
    expect(wrapper.findComponent(TradingWorkbenchProgress).text()).not.toContain('等待研究会话启动')
  })

  it('keeps the main workbench state intact when history session expansion fails and shows a local history error', async () => {
    globalThis.fetch = vi.fn(async (input) => {
      const url = String(input)
      if (url === '/api/stocks/translations/json-keys') return createJsonResponse({})
      if (url.includes('/active-session?symbol=')) {
        return createJsonResponse({ sessionId: 12, status: 'Completed', sessionKey: 'live-session' })
      }
      if (url.endsWith('/sessions/12')) {
        return createJsonResponse({
          id: 12,
          name: '当前完成会话',
          status: 'Completed',
          turns: [{ id: 101, turnIndex: 3, status: 'Completed', userPrompt: '当前完成研究' }],
          feedItems: [{ id: 1, turnId: 101, type: 'RoleOutput', summary: '当前会话讨论动态' }],
          stageSnapshots: [{ stageType: 'AnalystTeam', status: 'Completed', roleStates: [] }]
        })
      }
      if (url.endsWith('/turns/101/report')) {
        return createJsonResponse({
          blocks: [{ id: 'live-block', headline: '当前报告标题' }],
          finalDecision: { rating: 'buy', executiveSummary: '当前决策摘要' }
        })
      }
      if (url.endsWith('/sessions?symbol=sh600000&limit=20')) {
        return createJsonResponse([
          {
            id: 12,
            name: '当前完成会话',
            status: 'Completed',
            createdAt: '2026-04-08T20:00:00Z',
            latestDecisionHeadline: '当前会话结论'
          },
          {
            id: 45,
            name: '历史失败会话',
            status: 'Completed',
            createdAt: '2026-04-08T19:45:00Z',
            latestDecisionHeadline: '历史会话结论'
          }
        ])
      }
      if (url.endsWith('/sessions/45')) {
        return createJsonResponse({ message: 'disk full' }, { status: 500, statusText: 'Internal Server Error' })
      }
      throw new Error(`Unexpected fetch: ${url}`)
    })

    const wrapper = mount(TradingWorkbench, {
      props: { symbol: 'sh600000' }
    })
    await flushWorkbench()

    expect(wrapper.findComponent(TradingWorkbenchHeader).props('session')?.id).toBe(12)
    expect(wrapper.findComponent(TradingWorkbenchHeader).props('error')).toBeNull()
    expect(wrapper.findComponent(TradingWorkbenchReport).props('blocks')[0].headline).toBe('当前报告标题')
    expect(
      wrapper.findComponent(TradingWorkbenchProgress).props('stages')
        .find(stage => stage.key === 'AnalystTeam')?.status
    ).toBe('Completed')
    expect(wrapper.findComponent(TradingWorkbenchFeed).props('items')).toHaveLength(1)

    const historyTab = wrapper.findAll('.wb-tab').find(tab => tab.text().includes('历史记录'))
    expect(historyTab).toBeTruthy()
    await historyTab.trigger('click')
    await flushWorkbench()

    const targetRow = wrapper.findAll('.history-session-row').find(row => row.text().includes('历史失败会话'))
    expect(targetRow).toBeTruthy()
    await targetRow.trigger('click')
    await flushWorkbench()

    expect(wrapper.findComponent(TradingWorkbenchHeader).props('session')?.id).toBe(12)
    expect(wrapper.findComponent(TradingWorkbenchHeader).props('error')).toBeNull()
    expect(wrapper.findComponent(TradingWorkbenchReport).props('blocks')[0].headline).toBe('当前报告标题')
    expect(
      wrapper.findComponent(TradingWorkbenchProgress).props('stages')
        .find(stage => stage.key === 'AnalystTeam')?.status
    ).toBe('Completed')
    expect(wrapper.findComponent(TradingWorkbenchFeed).props('items')).toHaveLength(1)

    const historyPanel = wrapper.find('.wb-history')
    expect(historyPanel.find('.history-turns-error').exists()).toBe(true)
    expect(historyPanel.text()).toContain('历史记录加载失败，请重试。')
    expect(historyPanel.text()).not.toContain('加载中…')
  })
})
