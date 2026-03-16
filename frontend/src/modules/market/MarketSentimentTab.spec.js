import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import MarketSentimentTab from './MarketSentimentTab.vue'

const makeResponse = ({ ok = true, status = 200, json }) => ({
  ok,
  status,
  json: json || (async () => ({}))
})

const flushPromises = () => new Promise(resolve => setTimeout(resolve, 0))

beforeEach(() => {
  vi.restoreAllMocks()
})

describe('MarketSentimentTab', () => {
  it('loads market summary, sector list and first detail on mount', async () => {
    const fetchMock = vi.fn(async input => {
      const url = String(input)
      if (url === '/api/market/sentiment/latest') {
        return makeResponse({
          json: async () => ({
            snapshotTime: '2026-03-15T06:35:00Z',
            sessionPhase: '盘中',
            stageLabel: '主升',
            stageScore: 78.6,
            maxLimitUpStreak: 5,
            limitUpCount: 62,
            limitDownCount: 4,
            brokenBoardCount: 10,
            brokenBoardRate: 16.1,
            advancers: 3680,
            decliners: 1120,
            flatCount: 202,
            top3SectorTurnoverShare: 26.4,
            top10SectorTurnoverShare: 58.8,
            diffusionScore: 72.5,
            continuationScore: 76.1,
            stageLabelV2: '主升',
            stageConfidence: 84,
            top3SectorTurnoverShare5dAvg: 24.1,
            top10SectorTurnoverShare5dAvg: 55.2,
            limitUpCount5dAvg: 48.6,
            brokenBoardRate5dAvg: 15.2
          })
        })
      }
      if (url === '/api/market/sentiment/history?days=10') {
        return makeResponse({
          json: async () => ([
            { tradingDate: '2026-03-14T00:00:00Z', snapshotTime: '2026-03-14T07:00:00Z', stageLabel: '混沌', stageScore: 49.3 },
            { tradingDate: '2026-03-15T00:00:00Z', snapshotTime: '2026-03-15T06:35:00Z', stageLabel: '主升', stageScore: 78.6 }
          ])
        })
      }
      if (url.includes('/api/market/sectors?')) {
        return makeResponse({
          json: async () => ({
            total: 2,
            snapshotTime: '2026-03-15T06:35:00Z',
            items: [
              {
                boardType: 'concept',
                sectorCode: 'BK1101',
                sectorName: '机器人',
                changePercent: 4.82,
                mainNetInflow: 1260000000,
                breadthScore: 86,
                continuityScore: 74,
                strengthScore: 81,
                newsSentiment: '利好',
                newsHotCount: 6,
                leaderName: '巨能股份',
                rankNo: 1,
                strengthAvg5d: 79,
                strengthAvg10d: 75,
                strengthAvg20d: 68,
                diffusionRate: 81,
                rankChange5d: 2,
                rankChange10d: 4,
                rankChange20d: 6,
                leaderStabilityScore: 70,
                mainlineScore: 78,
                isMainline: true,
                advancerCount: 22,
                declinerCount: 5,
                flatMemberCount: 1,
                limitUpMemberCount: 3
              },
              {
                boardType: 'concept',
                sectorCode: 'BK1102',
                sectorName: '算力租赁',
                changePercent: 3.15,
                mainNetInflow: 920000000,
                breadthScore: 75,
                continuityScore: 63,
                strengthScore: 70,
                newsSentiment: '中性',
                newsHotCount: 3,
                leaderName: '恒润股份',
                rankNo: 2,
                strengthAvg5d: 68,
                strengthAvg10d: 64,
                strengthAvg20d: 58,
                diffusionRate: 70,
                rankChange5d: 1,
                rankChange10d: 2,
                rankChange20d: 1,
                leaderStabilityScore: 55,
                mainlineScore: 62,
                isMainline: false,
                advancerCount: 15,
                declinerCount: 8,
                flatMemberCount: 3,
                limitUpMemberCount: 1
              }
            ]
          })
        })
      }
      if (url.includes('/api/market/sectors/BK1101?')) {
        return makeResponse({
          json: async () => ({
            snapshot: { boardType: 'concept', sectorCode: 'BK1101', sectorName: '机器人', changePercent: 4.82 },
            history: [{ tradingDate: '2026-03-15T00:00:00Z', changePercent: 4.82, strengthScore: 81, strengthAvg10d: 75, rankChange10d: 4 }],
            leaders: [{ rankInSector: 1, symbol: '832876', name: '巨能股份', changePercent: 11.2 }],
            news: [{ translatedTitle: '机器人链条获增量订单', source: '证券时报', sentiment: '利好', publishTime: '2026-03-15T05:00:00Z' }]
          })
        })
      }
      throw new Error(`unexpected url: ${url}`)
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(MarketSentimentTab)
    await flushPromises()
    await flushPromises()
    await flushPromises()

    expect(wrapper.text()).toContain('情绪轮动')
    expect(wrapper.text()).toContain('主升')
    expect(wrapper.text()).toContain('机器人')
    expect(wrapper.text()).toContain('巨能股份')
    expect(wrapper.text()).toContain('机器人链条获增量订单')
    expect(wrapper.text()).toContain('主线')
    expect(wrapper.text()).toContain('比较窗口')
  })

  it('reloads board list when board type changes', async () => {
    const fetchMock = vi.fn(async input => {
      const url = String(input)
      if (url === '/api/market/sentiment/latest') {
        return makeResponse({ json: async () => ({ stageLabel: '混沌', snapshotTime: '2026-03-15T06:35:00Z' }) })
      }
      if (url === '/api/market/sentiment/history?days=10') {
        return makeResponse({ json: async () => ([]) })
      }
      if (url.includes('/api/market/sectors?boardType=concept')) {
        return makeResponse({ json: async () => ({ total: 1, items: [{ boardType: 'concept', sectorCode: 'BK1', sectorName: '概念A', rankNo: 1 }] }) })
      }
      if (url.includes('/api/market/sectors/BK1?')) {
        return makeResponse({ json: async () => ({ snapshot: { boardType: 'concept', sectorCode: 'BK1', sectorName: '概念A' }, history: [], leaders: [], news: [] }) })
      }
      if (url.includes('/api/market/sectors?boardType=industry')) {
        return makeResponse({ json: async () => ({ total: 1, items: [{ boardType: 'industry', sectorCode: 'BK2', sectorName: '行业A', rankNo: 1 }] }) })
      }
      if (url.includes('/api/market/sectors/BK2?boardType=industry')) {
        return makeResponse({ json: async () => ({ snapshot: { boardType: 'industry', sectorCode: 'BK2', sectorName: '行业A' }, history: [], leaders: [], news: [] }) })
      }
      throw new Error(`unexpected url: ${url}`)
    })
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(MarketSentimentTab)
    await flushPromises()
    await flushPromises()

    await wrapper.find('select').setValue('industry')
    await flushPromises()
    await flushPromises()

    expect(fetchMock.mock.calls.some(([url]) => String(url).includes('/api/market/sectors?boardType=industry'))).toBe(true)
    expect(wrapper.text()).toContain('行业A')
  })
})
