import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick, ref } from 'vue'
import TradingWorkbench from './workbench/TradingWorkbench.vue'
import TradingWorkbenchHeader from './workbench/TradingWorkbenchHeader.vue'
import TradingWorkbenchProgress from './workbench/TradingWorkbenchProgress.vue'
import TradingWorkbenchReport from './workbench/TradingWorkbenchReport.vue'
import TradingWorkbenchComposer from './workbench/TradingWorkbenchComposer.vue'
import TradingWorkbenchFeed from './workbench/TradingWorkbenchFeed.vue'
import { useTradingWorkbench, STAGES, STATUS_MAP } from './workbench/useTradingWorkbench.js'

const flushPromises = () => new Promise(resolve => setTimeout(resolve, 0))

beforeEach(() => { vi.restoreAllMocks() })

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
    expect(wrapper.findAll('.wb-feed-turn')).toHaveLength(2)
    expect(wrapper.findAll('.wb-feed-item')).toHaveLength(3)
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
    expect(wrapper.find('.wb-input').attributes('disabled')).toBeDefined()
    expect(wrapper.find('.wb-send-btn').attributes('disabled')).toBeDefined()
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

  it('renders all three tabs', () => {
    const wrapper = mount(TradingWorkbench, {
      props: { symbol: 'sz000001' }
    })
    const tabs = wrapper.findAll('.wb-tab')
    expect(tabs).toHaveLength(3)
    expect(tabs[0].text()).toContain('研究报告')
    expect(tabs[1].text()).toContain('团队进度')
    expect(tabs[2].text()).toContain('讨论动态')
  })

  it('switches tabs on click', async () => {
    const wrapper = mount(TradingWorkbench, {
      props: { symbol: 'sz000001' }
    })
    const tabs = wrapper.findAll('.wb-tab')
    await tabs[1].trigger('click')
    expect(tabs[1].classes()).toContain('active')
  })
})
