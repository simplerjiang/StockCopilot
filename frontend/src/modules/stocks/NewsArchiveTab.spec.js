import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
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

const flushPromises = () => new Promise(resolve => setTimeout(resolve, 0))

beforeEach(() => {
  vi.restoreAllMocks()
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
})