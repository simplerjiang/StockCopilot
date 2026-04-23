import { describe, it, expect } from 'vitest'
import {
  BALANCE_SHEET_FIELDS,
  INCOME_STATEMENT_FIELDS,
  CASH_FLOW_FIELDS,
  pickFieldValue,
  formatFieldValue,
  formatMoneyDisplay
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

  it('中文键命中（fallback）', () => {
    const totalAssets = BALANCE_SHEET_FIELDS.find(f => f.key === 'totalAssets')
    expect(pickFieldValue({ '资产合计': 1234 }, totalAssets)).toBe(1234)
  })

  it('带 * 前缀中文键命中（透明剥离）', () => {
    const totalAssets = BALANCE_SHEET_FIELDS.find(f => f.key === 'totalAssets')
    expect(pickFieldValue({ '*资产合计': 5678 }, totalAssets)).toBe(5678)
    const netProfit = INCOME_STATEMENT_FIELDS.find(f => f.key === 'netProfit')
    expect(pickFieldValue({ '*净利润': 9999 }, netProfit)).toBe(9999)
    const op = CASH_FLOW_FIELDS.find(f => f.key === 'operatingCashFlow')
    expect(pickFieldValue({ '*经营活动产生的现金流量净额': 4242 }, op)).toBe(4242)
  })

  it('原始 key 优先于 * 剥离 key', () => {
    const totalAssets = BALANCE_SHEET_FIELDS.find(f => f.key === 'totalAssets')
    // 同时存在 * 和不带 * 的两种 key 时，返回不带 * 的（fallback 中明确含 '资产合计'）
    expect(pickFieldValue({ '资产合计': 1, '*资产合计': 2 }, totalAssets)).toBe(1)
  })

  it('_同比 噪声字段不会误命中', () => {
    const totalAssets = BALANCE_SHEET_FIELDS.find(f => f.key === 'totalAssets')
    // 仅存在 _同比 后缀键时不应命中
    expect(pickFieldValue({ '资产合计_同比': 0.12, '*资产合计_同比': 0.34 }, totalAssets)).toBeNull()
  })

  it('真实后端响应样本（ths 渠道带 * 前缀）能命中三表', () => {
    const balanceSheet = {
      '*资产合计': 1000,
      '*负债合计': 400,
      '*所有者权益（或股东权益）合计': 600,
      '货币资金': 200,
      '应收账款': 50,
      '*资产合计_同比': 0.1
    }
    const income = {
      '*营业总收入': 800,
      '三、营业利润': 250,
      '*净利润': 200,
      '（一）基本每股收益': 1.23
    }
    const cashFlow = {
      '*经营活动产生的现金流量净额': 300,
      '*投资活动产生的现金流量净额': -100,
      '*筹资活动产生的现金流量净额': -50,
      '*现金及现金等价物净增加额': 150,
      '*期末现金及现金等价物余额': 500
    }
    expect(pickFieldValue(balanceSheet, BALANCE_SHEET_FIELDS[0])).toBe(1000)
    expect(pickFieldValue(balanceSheet, BALANCE_SHEET_FIELDS[1])).toBe(400)
    expect(pickFieldValue(balanceSheet, BALANCE_SHEET_FIELDS[2])).toBe(600)
    expect(pickFieldValue(balanceSheet, BALANCE_SHEET_FIELDS[3])).toBe(200)
    expect(pickFieldValue(balanceSheet, BALANCE_SHEET_FIELDS[4])).toBe(50)
    expect(pickFieldValue(income, INCOME_STATEMENT_FIELDS[0])).toBe(800)
    expect(pickFieldValue(income, INCOME_STATEMENT_FIELDS[1])).toBe(250)
    expect(pickFieldValue(income, INCOME_STATEMENT_FIELDS[2])).toBe(200)
    expect(pickFieldValue(income, INCOME_STATEMENT_FIELDS[3])).toBe(1.23)
    expect(pickFieldValue(cashFlow, CASH_FLOW_FIELDS[0])).toBe(300)
    expect(pickFieldValue(cashFlow, CASH_FLOW_FIELDS[1])).toBe(-100)
    expect(pickFieldValue(cashFlow, CASH_FLOW_FIELDS[2])).toBe(-50)
    expect(pickFieldValue(cashFlow, CASH_FLOW_FIELDS[3])).toBe(150)
    expect(pickFieldValue(cashFlow, CASH_FLOW_FIELDS[4])).toBe(500)
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

describe('formatMoneyDisplay', () => {
  it('formats values >= 1亿 as X.XX亿', () => {
    expect(formatMoneyDisplay(123456789).display).toBe('1.23亿')
    expect(formatMoneyDisplay(123456789).full).toBe('123,456,789.00')
  })

  it('formats values >= 1万 as X.XX万', () => {
    expect(formatMoneyDisplay(56789).display).toBe('5.68万')
  })

  it('formats values < 1万 as decimal', () => {
    expect(formatMoneyDisplay(1234.5).display).toBe('1234.50')
  })

  it('handles null/undefined/empty', () => {
    expect(formatMoneyDisplay(null).display).toBe('—')
    expect(formatMoneyDisplay(undefined).display).toBe('—')
    expect(formatMoneyDisplay('').display).toBe('—')
  })

  it('handles negative values', () => {
    expect(formatMoneyDisplay(-234567890).display).toBe('-2.35亿')
  })

  it('handles string numbers', () => {
    expect(formatMoneyDisplay('99999').display).toBe('10.00万')
  })

  it('returns full formatted value with thousands separator', () => {
    expect(formatMoneyDisplay(56789).full).toBe('56,789.00')
    expect(formatMoneyDisplay(1234.5).full).toBe('1,234.50')
  })

  it('handles zero', () => {
    expect(formatMoneyDisplay(0).display).toBe('0.00')
  })

  it('handles NaN string', () => {
    expect(formatMoneyDisplay('abc').display).toBe('—')
  })
})
