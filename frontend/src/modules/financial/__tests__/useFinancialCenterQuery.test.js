/**
 * @vitest-environment jsdom
 * @vitest-environment-options {"url":"http://localhost/"}
 */
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { nextTick } from 'vue'
import { useFinancialCenterQuery } from '../useFinancialCenterQuery.js'
import { DEFAULT_QUERY } from '../financialCenterConstants.js'

const setUrl = (search) => {
  const next = `http://localhost/${search ? `?${search}` : ''}`
  window.history.replaceState({}, '', next)
}

const flushMicrotasks = async () => {
  await Promise.resolve()
  await Promise.resolve()
  await nextTick()
}

beforeEach(() => {
  setUrl('')
  vi.restoreAllMocks()
})

afterEach(() => {
  setUrl('')
})

describe('useFinancialCenterQuery — URL parsing', () => {
  it('returns DEFAULT_QUERY when URL has no fc.* params', () => {
    const { query } = useFinancialCenterQuery()
    expect(query.symbols).toEqual([])
    expect(query.reportTypes).toEqual(DEFAULT_QUERY.reportTypes)
    expect(query.keyword).toBe('')
    expect(query.page).toBe(1)
    expect(query.pageSize).toBe(20)
    expect(query.sortField).toBe('reportDate')
    expect(query.sortDirection).toBe('desc')
    // dates default to today-1y → today (yyyy-MM-dd)
    expect(query.startDate).toMatch(/^\d{4}-\d{2}-\d{2}$/)
    expect(query.endDate).toMatch(/^\d{4}-\d{2}-\d{2}$/)
    expect(query.startDate).toBe(DEFAULT_QUERY.startDate)
    expect(query.endDate).toBe(DEFAULT_QUERY.endDate)
  })

  it('parses a fully populated URL', () => {
    setUrl(
      'fc.symbols=600519,000001&fc.start=2025-01-01&fc.end=2025-12-31' +
      '&fc.types=annual,q1&fc.kw=test&fc.page=2&fc.size=50&fc.sort=collectedAt:asc'
    )
    const { query } = useFinancialCenterQuery()
    expect(query.symbols).toEqual(['600519', '000001'])
    expect(query.startDate).toBe('2025-01-01')
    expect(query.endDate).toBe('2025-12-31')
    expect(query.reportTypes).toEqual(['annual', 'q1'])
    expect(query.keyword).toBe('test')
    expect(query.page).toBe(2)
    expect(query.pageSize).toBe(50)
    expect(query.sortField).toBe('collectedAt')
    expect(query.sortDirection).toBe('asc')
  })
})

describe('useFinancialCenterQuery — URL writeback', () => {
  it('keeps URL clean when query stays at defaults', async () => {
    vi.useFakeTimers()
    const replaceSpy = vi.spyOn(window.history, 'replaceState')
    const { query } = useFinancialCenterQuery()
    // force watcher to fire (mutate then revert)
    query.page = 2
    query.page = 1
    vi.advanceTimersByTime(400)
    await flushMicrotasks()
    // last replaceState call should yield URL without default params
    const calls = replaceSpy.mock.calls
    const lastUrl = calls.length > 0 ? String(calls[calls.length - 1][2]) : window.location.href
    expect(lastUrl).not.toMatch(/fc\.size=20/)
    expect(lastUrl).not.toMatch(/fc\.sort=reportDate:desc/)
    expect(lastUrl).not.toMatch(/fc\.page=1/)
    expect(lastUrl).not.toMatch(/fc\.types=annual,q1,q2,q3/)
    vi.useRealTimers()
  })

  it('serializes non-default values into URL params', async () => {
    vi.useFakeTimers()
    const replaceSpy = vi.spyOn(window.history, 'replaceState')
    const { query } = useFinancialCenterQuery()
    query.symbols.push('600519')
    query.keyword = 'profit'
    query.page = 3
    query.pageSize = 50
    query.sortField = 'symbol'
    query.sortDirection = 'asc'
    query.reportTypes = ['annual']
    await nextTick()
    vi.advanceTimersByTime(400)
    await Promise.resolve()
    expect(replaceSpy).toHaveBeenCalled()
    const lastCall = replaceSpy.mock.calls[replaceSpy.mock.calls.length - 1]
    const url = String(lastCall[2])
    expect(url).toContain('fc.symbols=600519')
    expect(url).toContain('fc.kw=profit')
    expect(url).toContain('fc.page=3')
    expect(url).toContain('fc.size=50')
    // colon may be percent-encoded
    expect(url).toMatch(/fc\.sort=symbol(:|%3A)asc/)
    expect(url).toContain('fc.types=annual')
    vi.useRealTimers()
  })
})

describe('useFinancialCenterQuery — fetchReports request URL', () => {
  it('includes all parameters in the request URL', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({ items: [], total: 0 })
    })
    global.fetch = fetchMock

    const { query, fetchReports } = useFinancialCenterQuery()
    query.symbols.push('600519', '000001')
    query.startDate = '2025-01-01'
    query.endDate = '2025-12-31'
    query.reportTypes = ['annual', 'q1']
    // keyword 留空，避免触发 B-2 关键词模式 pageSize/page 覆盖
    query.page = 2
    query.pageSize = 50
    query.sortField = 'collectedAt'
    query.sortDirection = 'asc'

    await fetchReports()
    const url = fetchMock.mock.calls[0][0]
    expect(url).toContain('/api/stocks/financial/reports?')
    expect(url).toContain('symbol=600519%2C000001')
    // 后端 LiteDB IN 大小写敏感，前端发送时把小写枚举映射为首字母大写
    expect(url).toContain('reportType=Annual%2CQ1')
    expect(url).toContain('startDate=2025-01-01')
    expect(url).toContain('endDate=2025-12-31')
    expect(url).not.toContain('keyword=')
    expect(url).toContain('page=2')
    expect(url).toContain('pageSize=50')
    expect(url).toContain('sort=collectedAt%3Aasc')
  })

  it('omits empty fields from the request URL', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: () => Promise.resolve([])
    })
    global.fetch = fetchMock

    const { fetchReports } = useFinancialCenterQuery()
    await fetchReports()
    const url = fetchMock.mock.calls[0][0]
    expect(url).not.toContain('symbol=')
    expect(url).not.toContain('keyword=')
    // page, pageSize, sort are always set (per implementation)
    expect(url).toContain('page=1')
    expect(url).toContain('pageSize=20')
    expect(url).toContain('sort=reportDate%3Adesc')
  })
})

describe('useFinancialCenterQuery — race condition guard', () => {
  it('latest response wins when an earlier slow request resolves later', async () => {
    let resolveFirst
    const firstResp = new Promise((resolve) => { resolveFirst = resolve })
    const secondResp = Promise.resolve({
      ok: true,
      json: () => Promise.resolve({ items: [{ id: 'B' }], total: 1 })
    })

    const fetchMock = vi.fn()
      .mockImplementationOnce(() => firstResp)
      .mockImplementationOnce(() => secondResp)
    global.fetch = fetchMock

    const { items, fetchReports } = useFinancialCenterQuery()
    const p1 = fetchReports()
    const p2 = fetchReports()
    await p2
    expect(items.value).toEqual([{ id: 'B' }])

    // resolve the first (stale) request
    resolveFirst({
      ok: true,
      json: () => Promise.resolve({ items: [{ id: 'A' }], total: 1 })
    })
    await p1
    expect(items.value).toEqual([{ id: 'B' }])
  })
})

describe('useFinancialCenterQuery — symbol name lazy load', () => {
  it('populates symbolNameMap from /api/stocks/search responses', async () => {
    const fetchMock = vi.fn().mockImplementation((url) => {
      if (typeof url === 'string' && url.startsWith('/api/stocks/financial/reports')) {
        return Promise.resolve({
          ok: true,
          json: () => Promise.resolve({
            items: [{ symbol: '600519' }, { symbol: '000001' }],
            total: 2
          })
        })
      }
      // /api/stocks/search?q=...
      const m = /q=([^&]+)/.exec(url)
      const sym = m ? decodeURIComponent(m[1]) : ''
      return Promise.resolve({
        ok: true,
        json: () => Promise.resolve([{ symbol: sym, name: `名-${sym}` }])
      })
    })
    global.fetch = fetchMock

    const { fetchReports, symbolNameMap } = useFinancialCenterQuery()
    await fetchReports()
    // wait for name lookups
    for (let i = 0; i < 5; i++) await flushMicrotasks()
    expect(symbolNameMap['600519']).toBe('名-600519')
    expect(symbolNameMap['000001']).toBe('名-000001')
  })

  it('writes empty string for missing symbol so we never re-request', async () => {
    const fetchMock = vi.fn().mockImplementation((url) => {
      if (url.startsWith('/api/stocks/financial/reports')) {
        return Promise.resolve({
          ok: true,
          json: () => Promise.resolve({ items: [{ symbol: 'XYZ' }], total: 1 })
        })
      }
      // search returns empty
      return Promise.resolve({
        ok: true,
        json: () => Promise.resolve([])
      })
    })
    global.fetch = fetchMock
    const { fetchReports, symbolNameMap, enqueueSymbol } = useFinancialCenterQuery()
    await fetchReports()
    for (let i = 0; i < 5; i++) await flushMicrotasks()
    expect(Object.prototype.hasOwnProperty.call(symbolNameMap, 'XYZ')).toBe(true)
    expect(symbolNameMap.XYZ).toBe('')

    // second enqueue must be no-op (no extra fetch call)
    const callsBefore = fetchMock.mock.calls.length
    enqueueSymbol('XYZ')
    for (let i = 0; i < 3; i++) await flushMicrotasks()
    expect(fetchMock.mock.calls.length).toBe(callsBefore)
  })

  it('limits concurrent symbol name lookups to 5', async () => {
    let active = 0
    let peak = 0
    const deferreds = []
    const fetchMock = vi.fn().mockImplementation((url) => {
      if (url.startsWith('/api/stocks/financial/reports')) {
        return Promise.resolve({ ok: true, json: () => Promise.resolve({ items: [], total: 0 }) })
      }
      active++
      if (active > peak) peak = active
      let resolveFn
      const p = new Promise((resolve) => { resolveFn = resolve })
      deferreds.push(() => {
        active--
        resolveFn({ ok: true, json: () => Promise.resolve([]) })
      })
      return p
    })
    global.fetch = fetchMock

    const { enqueueSymbol } = useFinancialCenterQuery()
    for (let i = 0; i < 10; i++) enqueueSymbol(`S${i}`)
    // let microtasks settle so pending promises register
    await flushMicrotasks()
    expect(active).toBeLessThanOrEqual(5)
    expect(peak).toBeLessThanOrEqual(5)
    // drain
    while (deferreds.length > 0) {
      const d = deferreds.shift()
      d()
      await flushMicrotasks()
    }
    expect(peak).toBeLessThanOrEqual(5)
    expect(peak).toBeGreaterThan(0)
  })
})

describe('useFinancialCenterQuery — response shape parsing', () => {
  const cases = [
    { name: 'bare array', payload: [] },
    { name: '{items:[]}', payload: { items: [] } },
    { name: '{data:[]}', payload: { data: [] } },
    { name: '{results:[]}', payload: { results: [] } }
  ]
  for (const c of cases) {
    it(`parses ${c.name} as empty list`, async () => {
      const fetchMock = vi.fn().mockResolvedValue({
        ok: true,
        json: () => Promise.resolve(c.payload)
      })
      global.fetch = fetchMock
      const { fetchReports, items, total } = useFinancialCenterQuery()
      await fetchReports()
      expect(items.value).toEqual([])
      expect(total.value).toBe(0)
    })
  }

  it('prefers total > totalCount > count', async () => {
    const make = (extra) => ({
      ok: true,
      json: () => Promise.resolve({ items: [{ id: 1 }], ...extra })
    })

    // total wins
    let fetchMock = vi.fn().mockResolvedValue(make({ total: 99, totalCount: 50, count: 10 }))
    global.fetch = fetchMock
    let inst = useFinancialCenterQuery()
    await inst.fetchReports()
    expect(inst.total.value).toBe(99)

    // totalCount wins when total absent
    fetchMock = vi.fn().mockResolvedValue(make({ totalCount: 50, count: 10 }))
    global.fetch = fetchMock
    inst = useFinancialCenterQuery()
    await inst.fetchReports()
    expect(inst.total.value).toBe(50)

    // count last
    fetchMock = vi.fn().mockResolvedValue(make({ count: 10 }))
    global.fetch = fetchMock
    inst = useFinancialCenterQuery()
    await inst.fetchReports()
    expect(inst.total.value).toBe(10)
  })
})

describe('useFinancialCenterQuery — keyword frontend filter (B-2)', () => {
  it('keyword 非空时透传 keyword 到后端，page/pageSize 保持用户值', async () => {
    const fetchMock = vi.fn().mockImplementation((url) => {
      if (typeof url === 'string' && url.startsWith('/api/stocks/financial/reports')) {
        return Promise.resolve({ ok: true, json: () => Promise.resolve({ items: [], total: 0 }) })
      }
      return Promise.resolve({ ok: true, json: () => Promise.resolve([]) })
    })
    global.fetch = fetchMock

    const { query, fetchReports } = useFinancialCenterQuery()
    query.keyword = '茅台'
    query.page = 5
    query.pageSize = 20
    await fetchReports()
    const url = fetchMock.mock.calls[0][0]
    expect(url).toContain('keyword=%E8%8C%85%E5%8F%B0')
    expect(url).toContain('page=5')
    expect(url).toContain('pageSize=20')
  })

  it('keyword 空时按用户的 page/pageSize 透传到后端', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({ items: [], total: 0 })
    })
    global.fetch = fetchMock

    const { query, fetchReports } = useFinancialCenterQuery()
    query.page = 3
    query.pageSize = 50
    await fetchReports()
    const url = fetchMock.mock.calls[0][0]
    expect(url).toContain('page=3')
    expect(url).toContain('pageSize=50')
  })

  it('收到列表后按 symbol 子串过滤（关键词为代码）', async () => {
    const fetchMock = vi.fn().mockImplementation((url) => {
      if (url.startsWith('/api/stocks/financial/reports')) {
        return Promise.resolve({
          ok: true,
          json: () => Promise.resolve({
            items: [
              { id: '1', symbol: '600519' },
              { id: '2', symbol: '000001' },
              { id: '3', symbol: '600036' }
            ],
            total: 3
          })
        })
      }
      // /api/stocks/search → 返回空，避免影响 symbol 子串匹配
      return Promise.resolve({ ok: true, json: () => Promise.resolve([]) })
    })
    global.fetch = fetchMock

    const { query, fetchReports, items, total } = useFinancialCenterQuery()
    query.keyword = '600'
    await fetchReports()
    await flushMicrotasks()
    expect(items.value.map(i => i.symbol)).toEqual(['600519', '600036'])
    expect(total.value).toBe(2)
  })

  it('收到列表后按 name 子串过滤（关键词为公司名）', async () => {
    const fetchMock = vi.fn().mockImplementation((url) => {
      if (url.startsWith('/api/stocks/financial/reports')) {
        return Promise.resolve({
          ok: true,
          json: () => Promise.resolve({
            items: [
              { id: '1', symbol: '600519' },
              { id: '2', symbol: '000001' }
            ],
            total: 2
          })
        })
      }
      const m = /q=([^&]+)/.exec(url)
      const sym = m ? decodeURIComponent(m[1]) : ''
      const name = sym === '600519' ? '贵州茅台' : '平安银行'
      return Promise.resolve({
        ok: true,
        json: () => Promise.resolve([{ symbol: sym, name }])
      })
    })
    global.fetch = fetchMock

    const { query, fetchReports, items, total } = useFinancialCenterQuery()
    query.keyword = '茅台'
    await fetchReports()
    // 等名字异步加载并触发重算
    for (let i = 0; i < 8; i++) await flushMicrotasks()
    expect(items.value.map(i => i.symbol)).toEqual(['600519'])
    expect(total.value).toBe(1)
  })

  it('keyword 清空后立即恢复原始列表与原始 total', async () => {
    const fetchMock = vi.fn().mockImplementation((url) => {
      if (url.startsWith('/api/stocks/financial/reports')) {
        return Promise.resolve({
          ok: true,
          json: () => Promise.resolve({
            items: [
              { id: '1', symbol: '600519' },
              { id: '2', symbol: '000001' }
            ],
            total: 2
          })
        })
      }
      return Promise.resolve({ ok: true, json: () => Promise.resolve([]) })
    })
    global.fetch = fetchMock

    const { query, fetchReports, items, total } = useFinancialCenterQuery()
    query.keyword = '600519'
    await fetchReports()
    await flushMicrotasks()
    expect(items.value.length).toBe(1)
    query.keyword = ''
    await flushMicrotasks()
    expect(items.value.length).toBe(2)
    expect(total.value).toBe(2)
  })
})
