import { describe, it, expect } from 'vitest'
import {
  ensureMarkdown,
  markdownToSafeHtml,
  parseJsonIfPossible,
  toReadableInlineText,
  parseJsonArray
} from './jsonMarkdownService.js'

describe('jsonMarkdownService', () => {
  it('converts JSON object string into readable markdown with translated labels', () => {
    const input = JSON.stringify({
      peRatio: 12.34,
      volumeRatio: 1.23,
      shareholderCount: 98765,
      floatMarketCap: 12300000000
    })

    const md = ensureMarkdown(input)

    expect(md).toContain('市盈率')
    expect(md).toContain('量比')
    expect(md).toContain('股东户数')
    expect(md).toContain('流通市值')
    expect(md).not.toContain('{')
  })

  it('keeps plain markdown text unchanged', () => {
    const input = '## 结论\n- 保持观察'
    expect(ensureMarkdown(input)).toBe(input)
  })

  it('formats nested object to inline readable text instead of JSON', () => {
    const value = {
      confidence: 0.81,
      riskLimits: ['仓位不超过30%', '跌破20日线止损']
    }

    const inline = toReadableInlineText(value)

    expect(inline).toContain('置信度')
    expect(inline).toContain('风险限制')
    expect(inline).toContain('81%')
    expect(inline).not.toContain('{')
  })

  it('unwraps nested JSON strings inside object fields', () => {
    const input = JSON.stringify({
      content: JSON.stringify({
        qualityView: '稳健',
        metrics: { peRatio: 5.02, volumeRatio: 2.01 }
      })
    })

    const md = ensureMarkdown(input)

    expect(md).toContain('质量评估')
    expect(md).toContain('市盈率')
    expect(md).toContain('量比')
    expect(md).not.toContain('{"qualityView"')
  })

  it('parses JSON arrays safely', () => {
    expect(parseJsonArray('[1,2,3]')).toEqual([1, 2, 3])
    expect(parseJsonArray('oops')).toEqual([])
  })

  it('parses only valid JSON-like strings', () => {
    expect(parseJsonIfPossible('{"a":1}')).toEqual({ a: 1 })
    expect(parseJsonIfPossible('not json')).toBe('not json')
  })

  describe('markdown code block stripping', () => {
    it('should handle JSON wrapped in code fence', () => {
      const input = '```json\n{"key":"value"}\n```'
      const result = markdownToSafeHtml(input)
      expect(result).not.toContain('```')
      expect(result).not.toContain('"key"')
    })

    it('should handle plain text code fence', () => {
      const input = '```\nsome text\n```'
      const result = markdownToSafeHtml(input)
      expect(result).toContain('some text')
    })
  })
})
