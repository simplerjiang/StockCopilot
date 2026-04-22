/**
 * @vitest-environment jsdom
 */
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import {
  listPdfFiles,
  fetchPdfFileDetail,
  buildPdfFileContentUrl,
  reparsePdfFile
} from '../financialApi.js'

const okJson = (body) => ({
  ok: true,
  status: 200,
  json: async () => body
})

const errResp = (status, body) => ({
  ok: false,
  status,
  json: async () => body
})

let originalFetch

beforeEach(() => {
  originalFetch = globalThis.fetch
  globalThis.fetch = vi.fn()
})

afterEach(() => {
  globalThis.fetch = originalFetch
})

describe('financialApi - PDF endpoints', () => {
  it('listPdfFiles 拼接 query 参数', async () => {
    globalThis.fetch.mockResolvedValueOnce(okJson({ items: [], total: 0 }))
    await listPdfFiles({ symbol: '600519', reportType: 'annual', page: 2, pageSize: 50 })
    expect(globalThis.fetch).toHaveBeenCalledTimes(1)
    const url = globalThis.fetch.mock.calls[0][0]
    expect(url.startsWith('/api/stocks/financial/pdf-files?')).toBe(true)
    expect(url).toContain('symbol=600519')
    expect(url).toContain('reportType=annual')
    expect(url).toContain('page=2')
    expect(url).toContain('pageSize=50')
  })

  it('listPdfFiles 省略空 symbol/reportType 但保留分页', async () => {
    globalThis.fetch.mockResolvedValueOnce(okJson({ items: [] }))
    await listPdfFiles({})
    const url = globalThis.fetch.mock.calls[0][0]
    expect(url).not.toContain('symbol=')
    expect(url).not.toContain('reportType=')
    expect(url).toContain('page=1')
    expect(url).toContain('pageSize=20')
  })

  it('fetchPdfFileDetail 请求路径正确', async () => {
    globalThis.fetch.mockResolvedValueOnce(okJson({ id: 'abc', parseUnits: [] }))
    const detail = await fetchPdfFileDetail('abc 123')
    expect(globalThis.fetch).toHaveBeenCalledWith('/api/stocks/financial/pdf-files/abc%20123')
    expect(detail.id).toBe('abc')
  })

  it('fetchPdfFileDetail 在 id 为空时抛错', async () => {
    await expect(fetchPdfFileDetail('')).rejects.toThrow(/id/)
    expect(globalThis.fetch).not.toHaveBeenCalled()
  })

  it('buildPdfFileContentUrl 返回稳定路径', () => {
    expect(buildPdfFileContentUrl('abc'))
      .toBe('/api/stocks/financial/pdf-files/abc/content')
    expect(buildPdfFileContentUrl('a/b'))
      .toBe('/api/stocks/financial/pdf-files/a%2Fb/content')
  })

  it('reparsePdfFile 用 POST 且解析 success 字段', async () => {
    globalThis.fetch.mockResolvedValueOnce(okJson({
      success: true,
      error: null,
      detail: { id: 'pdf-1' }
    }))
    const result = await reparsePdfFile('pdf-1')
    const [url, init] = globalThis.fetch.mock.calls[0]
    expect(url).toBe('/api/stocks/financial/pdf-files/pdf-1/reparse')
    expect(init).toEqual({ method: 'POST' })
    expect(result.success).toBe(true)
    expect(result.detail.id).toBe('pdf-1')
  })

  it('!response.ok 时 throw 含错误信息', async () => {
    globalThis.fetch.mockResolvedValueOnce(errResp(500, { message: '后端炸了' }))
    await expect(listPdfFiles({ symbol: '600519' })).rejects.toThrow(/后端炸了/)
  })

  it('!response.ok 且 body 不是 JSON 时使用兜底文案', async () => {
    globalThis.fetch.mockResolvedValueOnce({
      ok: false,
      status: 502,
      json: async () => { throw new Error('not json') }
    })
    await expect(fetchPdfFileDetail('xx')).rejects.toThrow(/HTTP 502/)
  })

  it('reparsePdfFile 在 !ok 时 throw', async () => {
    globalThis.fetch.mockResolvedValueOnce(errResp(404, { message: '未找到' }))
    await expect(reparsePdfFile('missing')).rejects.toThrow(/未找到|HTTP 404/)
  })
})
