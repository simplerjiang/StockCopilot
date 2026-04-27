import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import RecommendProgress from './RecommendProgress.vue'

const STAGE_TYPES = ['MarketScan', 'SectorDebate', 'StockPicking', 'StockDebate', 'FinalDecision']
const ALL_PENDING = new Array(STAGE_TYPES.length).fill('待执行')

const createTurn = ({
  id,
  turnIndex = 0,
  status = 'Completed',
  stageSnapshots = [],
  feedItems = []
} = {}) => ({
  id,
  turnIndex,
  status,
  userPrompt: `turn-${id}`,
  requestedAt: '2026-04-01T10:00:00Z',
  startedAt: '2026-04-01T10:00:05Z',
  stageSnapshots,
  feedItems
})

const completedStageSnapshots = STAGE_TYPES.map(stageType => ({
  stageType,
  status: 'Completed',
  roleStates: []
}))

const getStageRows = wrapper => wrapper.findAll('.stage-row')
const getStageRow = (wrapper, index) => getStageRows(wrapper)[index]

describe('RecommendProgress', () => {
  it('keeps a fresh startup turn pending instead of borrowing older snapshots', () => {
    const wrapper = mount(RecommendProgress, {
      props: {
        isRunning: true,
        session: {
          status: 'Running',
          turns: [
            createTurn({ id: 801, turnIndex: 0, status: 'Completed', stageSnapshots: completedStageSnapshots }),
            createTurn({ id: 802, turnIndex: 1, status: null, stageSnapshots: [] })
          ],
          feedItems: []
        }
      }
    })

    expect(wrapper.findAll('.stage-status').map(node => node.text())).toEqual(ALL_PENDING)
  })

  it('ignores unscoped session feed items while a fresh live turn is waiting for snapshots', () => {
    const wrapper = mount(RecommendProgress, {
      props: {
        isRunning: true,
        session: {
          status: 'Running',
          activeTurnId: 902,
          turns: [
            createTurn({ id: 901, turnIndex: 0, status: 'Completed', stageSnapshots: completedStageSnapshots }),
            createTurn({ id: 902, turnIndex: 1, status: 'Queued', stageSnapshots: [] })
          ],
          feedItems: STAGE_TYPES.map((stageType, index) => ({
            id: index + 1,
            eventType: 'StageCompleted',
            stageType
          }))
        }
      }
    })

    expect(wrapper.findAll('.stage-status').map(node => node.text())).toEqual(ALL_PENDING)
  })

  it('merges repeated stage snapshots in a stable order instead of dropping earlier step roles', () => {
    const wrapper = mount(RecommendProgress, {
      props: {
        session: {
          status: 'Completed',
          turns: [
            createTurn({
              id: 1001,
              stageSnapshots: [
                {
                  id: 11,
                  stageType: 'StockPicking',
                  stageRunIndex: 2,
                  status: 'Completed',
                  startedAt: '2026-04-01T10:02:00Z',
                  roleStates: [
                    { roleId: 'recommend_leader_picker', status: 'Completed' },
                    { roleId: 'recommend_growth_picker', status: 'Completed' }
                  ]
                },
                {
                  id: 12,
                  stageType: 'StockPicking',
                  stageRunIndex: 2,
                  status: 'Completed',
                  startedAt: '2026-04-01T10:03:00Z',
                  roleStates: [
                    { roleId: 'recommend_chart_validator', status: 'Completed' }
                  ]
                }
              ]
            })
          ],
          feedItems: []
        }
      }
    })

    const stockPickingRow = getStageRow(wrapper, 2)
    expect(stockPickingRow.find('.stage-status').text()).toBe('已完成')
    expect(stockPickingRow.findAll('.role-status-icon').map(node => node.text())).toEqual(['✅', '✅', '✅'])
  })

  it('uses the latest repeated role state when duplicate snapshots still arrive from history', () => {
    const wrapper = mount(RecommendProgress, {
      props: {
        session: {
          status: 'Completed',
          turns: [
            createTurn({
              id: 1002,
              stageSnapshots: [
                {
                  id: 21,
                  stageType: 'FinalDecision',
                  stageRunIndex: 4,
                  status: 'Running',
                  startedAt: '2026-04-01T10:04:00Z',
                  roleStates: [
                    { roleId: 'recommend_director', status: 'Running' }
                  ]
                },
                {
                  id: 22,
                  stageType: 'FinalDecision',
                  stageRunIndex: 4,
                  status: 'Completed',
                  startedAt: '2026-04-01T10:05:00Z',
                  roleStates: [
                    { roleId: 'recommend_director', status: 'Completed' }
                  ]
                }
              ]
            })
          ],
          feedItems: []
        }
      }
    })

    const finalDecisionRow = getStageRow(wrapper, 4)
    expect(finalDecisionRow.find('.stage-status').text()).toBe('已完成')
    expect(finalDecisionRow.find('.role-status-icon').text()).toBe('✅')
  })
})