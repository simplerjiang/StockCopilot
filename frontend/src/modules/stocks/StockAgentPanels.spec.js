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
})
