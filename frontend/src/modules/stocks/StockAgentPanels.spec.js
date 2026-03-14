import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import StockAgentPanels from './StockAgentPanels.vue'

describe('StockAgentPanels', () => {
  it('marks stale evidence publishedAt with expired risk tag', () => {
    const staleTime = '2024-01-01 09:30'
    const wrapper = mount(StockAgentPanels, {
      props: {
        agents: [
          {
            agentId: 'stock_news',
            agentName: '个股资讯Agent',
            success: true,
            data: {
              summary: 'test',
              evidence: [
                {
                  point: '旧消息',
                  source: '测试源',
                  publishedAt: staleTime,
                  url: null
                }
              ],
              signals: [],
              risks: [],
              triggers: [],
              invalidations: [],
              riskLimits: []
            }
          }
        ]
      }
    })

    const expiredCell = wrapper.find('td.cell-expired')
    expect(expiredCell.exists()).toBe(true)
    expect(expiredCell.text()).toContain('过期风险')
  })

  it('emits standard and pro run flags from action buttons', async () => {
    const wrapper = mount(StockAgentPanels)

    await wrapper.find('.run-standard-button').trigger('click')
    await wrapper.find('.run-pro-button').trigger('click')

    expect(wrapper.emitted('run')).toEqual([[false], [true]])
  })

  it('renders commander opinion schema fields', () => {
    const wrapper = mount(StockAgentPanels, {
      props: {
        agents: [
          {
            agentId: 'commander',
            agentName: '指挥Agent',
            success: true,
            data: {
              summary: '偏谨慎',
              analysis_opinion: '当前更适合观察，等待放量突破。',
              confidence_score: 72,
              trigger_conditions: '放量突破 12.60',
              invalid_conditions: '跌破 11.90',
              risk_warning: '单笔亏损控制在 2% 以内',
              evidence: [],
              signals: [],
              risks: []
            }
          }
        ]
      }
    })

    expect(wrapper.text()).toContain('分析结论')
    expect(wrapper.text()).toContain('放量突破 12.60')
    expect(wrapper.text()).toContain('跌破 11.90')
  })

  it('emits draft-plan from commander card', async () => {
    const wrapper = mount(StockAgentPanels, {
      props: {
        agents: [
          {
            agentId: 'commander',
            agentName: '指挥Agent',
            success: true,
            data: {
              summary: '偏多',
              analysis_opinion: '等待确认',
              triggers: [],
              invalidations: [],
              riskLimits: []
            }
          }
        ]
      }
    })

    await wrapper.find('.draft-plan-button').trigger('click')

    expect(wrapper.emitted('draft-plan')).toEqual([[]])
  })
})
