import { describe, it, expect } from 'vitest'
import {
  BALANCE_SHEET_FIELDS,
  INCOME_STATEMENT_FIELDS,
  CASH_FLOW_FIELDS,
  pickFieldValue,
  formatFieldValue
} from '../financialFieldDictionary.js'

describe('financialFieldDictionary - 字段白名单', () => {
  const tables = [
    ['BalanceSheet', BALANCE_SHEET_FIELDS],
    ['IncomeStatement', INCOME_STATEMENT_FIELDS],
    ['CashFlow', CASH_FLOW_FIELDS]
  ]

  for (const [name, list] of tables) {
    it(`${name} 应有 5 项`, () => {
      expect(list).toHaveLength(5)
    })

    it(`${name} 每项应包含 key/label/fallbacks`, () => {
      for (const item of list) {
        expect(typeof item.key).toBe('string')
        expect(item.key.length).toBeGreaterThan(0)
        expect(typeof item.label).toBe('string')
        expect(item.label.length).toBeGreaterThan(0)
        expect(Array.isArray(item.fallbacks)).toBe(true)
      }
    })
  }

  it('BalanceSheet 包含指定字段顺序', () => {
    expect(BALANCE_SHEET_FIELDS.map(f => f.key)).toEqual([
      'totalAssets',
      'totalLiabilities',
      'totalEquity',
      'monetaryFunds',
      'accountsReceivable'
    ])
  })

  it('IncomeStatement 包含指定字段顺序', () => {
    expect(INCOME_STATEMENT_FIELDS.map(f => f.key)).toEqual([
      'revenue',
      'operatingProfit',
      'netProfit',
      'epsBasic',
      'grossProfit'
    ])
  })

  it('CashFlow 包含指定字段顺序', () => {
    expect(CASH_FLOW_FIELDS.map(f => f.key)).toEqual([
      'operatingCashFlow',
      'investingCashFlow',
      'financingCashFlow',
      'netIncreaseInCash',
      'cashEnd'
    ])
  })

  it('monetaryFunds 的 fallback 含 cashAndEquivalents', () => {
    const f = BALANCE_SHEET_FIELDS.find(x => x.key === 'monetaryFunds')
    expect(f.fallbacks).toContain('cashAndEquivalents')
  })

  it('revenue 的 fallback 含 operatingRevenue 和 totalRevenue', () => {
    const f = INCOME_STATEMENT_FIELDS.find(x => x.key === 'revenue')
    expect(f.fallbacks).toEqual(expect.arrayContaining(['operatingRevenue', 'totalRevenue']))
  })

  it('cashEnd 的 fallback 含 endingCashBalance', () => {
    const f = CASH_FLOW_FIELDS.find(x => x.key === 'cashEnd')
    expect(f.fallbacks).toContain('endingCashBalance')
  })
})

describe('pickFieldValue', () => {
  const field = { key: 'revenue', fallbacks: ['operatingRevenue', 'totalRevenue'] }

  it('精确匹配主 key', () => {
    expect(pickFieldValue({ revenue: 100 }, field)).toBe(100)
  })

  it('大小写不敏感匹配主 key', () => {
    expect(pickFieldValue({ Revenue: 200 }, field)).toBe(200)
    expect(pickFieldValue({ REVENUE: 300 }, field)).toBe(300)
  })

  it('主 key 为 null 时尝试 fallback', () => {
    expect(pickFieldValue({ revenue: null, operatingRevenue: 50 }, field)).toBe(50)
  })

  it('全部缺失返回 null', () => {
    expect(pickFieldValue({ unrelated: 1 }, field)).toBeNull()
    expect(pickFieldValue(null, field)).toBeNull()
    expect(pickFieldValue(undefined, field)).toBeNull()
    expect(pickFieldValue({}, field)).toBeNull()
  })
})

describe('formatFieldValue', () => {
  it('null/undefined/空字符串 → "—"', () => {
    expect(formatFieldValue(null)).toBe('—')
    expect(formatFieldValue(undefined)).toBe('—')
    expect(formatFieldValue('')).toBe('—')
    expect(formatFieldValue('   ')).toBe('—')
  })

  it('整数千分位、不带小数', () => {
    expect(formatFieldValue(1234567)).toBe('1,234,567')
  })

  it('小数保留 2 位', () => {
    expect(formatFieldValue(1234.5)).toBe('1,234.50')
    expect(formatFieldValue(0.123)).toBe('0.12')
  })

  it('数字字符串自动按数字格式化', () => {
    expect(formatFieldValue('1000')).toBe('1,000')
    expect(formatFieldValue('1000.5')).toBe('1,000.50')
  })

  it('非数字字符串原样输出（trim）', () => {
    expect(formatFieldValue(' hello ')).toBe('hello')
  })
})
