import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import FinancialDataTestPanel from './FinancialDataTestPanel.vue'

const makeResponse = body => ({
  ok: true,
  status: 200,
  headers: { get: () => 'application/json' },
  json: async () => body,
  text: async () => JSON.stringify(body)
})

describe('FinancialDataTestPanel', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.restoreAllMocks()
  })

  it('loads config and worker status on mount', async () => {
    const fetchMock = vi.fn(async url => {
      if (url === '/api/stocks/financial/config') return makeResponse({ startDate: '2026-04-27T00:00:00Z' })
      if (url === '/api/stocks/financial/logs?limit=50') return makeResponse([])
      if (url === '/api/stocks/financial/worker/status') return makeResponse({ reachable: true, baseUrl: 'http://worker' })
      return makeResponse({})
    })
    vi.stubGlobal('fetch', fetchMock)

    mount(FinancialDataTestPanel)
    await flushPromises()

    expect(fetchMock).toHaveBeenCalledWith('/api/stocks/financial/config')
    expect(fetchMock).toHaveBeenCalledWith('/api/stocks/financial/logs?limit=50')
  })
})