// 注意：本次仅做前端选择逻辑修复。后端 /api/stocks/search 对裸 code（如 000001）
// 的排序问题（指数 sh000001 排在 sz000001 平安银行前面）影响多处消费者，
// 风险更大，本次不做后端改动。
import { describe, it, expect } from 'vitest'
import { inferMarketFromCode, pickStockMatch } from '../symbolMarketUtil'

describe('inferMarketFromCode', () => {
  it('6 开头 -> sh', () => {
    expect(inferMarketFromCode('600519')).toBe('sh')
    expect(inferMarketFromCode('688001')).toBe('sh')
  })
  it('0 开头 -> sz', () => {
    expect(inferMarketFromCode('000001')).toBe('sz')
  })
  it('3 开头 -> sz（创业板）', () => {
    expect(inferMarketFromCode('300750')).toBe('sz')
  })
  it('4 / 8 开头 -> bj', () => {
    expect(inferMarketFromCode('430718')).toBe('bj')
    expect(inferMarketFromCode('830799')).toBe('bj')
  })
  it('9 开头 -> sh，2 开头 -> sz（B 股）', () => {
    expect(inferMarketFromCode('900901')).toBe('sh')
    expect(inferMarketFromCode('200002')).toBe('sz')
  })
  it('5 开头 -> sh，1 开头 -> sz（基金/可转债）', () => {
    expect(inferMarketFromCode('510300')).toBe('sh')
    expect(inferMarketFromCode('159915')).toBe('sz')
  })
  it('已带前缀的 symbol 直接返回前缀', () => {
    expect(inferMarketFromCode('sh600519')).toBe('sh')
    expect(inferMarketFromCode('SZ000001')).toBe('sz')
  })
  it('非法输入返回空串', () => {
    expect(inferMarketFromCode('')).toBe('')
    expect(inferMarketFromCode(null)).toBe('')
    expect(inferMarketFromCode(undefined)).toBe('')
    expect(inferMarketFromCode('abc')).toBe('')
    expect(inferMarketFromCode('7xxxxx')).toBe('')
  })
})

describe('pickStockMatch', () => {
  it('sym=000001：sh000001(上证指数) + sz000001(平安银行) -> 平安银行', () => {
    const results = [
      { symbol: 'sh000001', code: '000001', name: '上证指数', market: 'sh' },
      { symbol: 'sz000001', code: '000001', name: '平安银行', market: 'sz' },
    ]
    const pick = pickStockMatch(results, '000001')
    expect(pick?.name).toBe('平安银行')
  })

  it('兼容 PascalCase 搜索结果字段', () => {
    const results = [
      { Symbol: 'sh000001', Code: '000001', Name: '上证指数', Market: 'sh' },
      { Symbol: 'sz000001', Code: '000001', Name: '平安银行', Market: 'sz' },
    ]
    const pick = pickStockMatch(results, '000001')
    expect(pick?.Name).toBe('平安银行')
  })

  it('sym=600519：返回 sh600519 贵州茅台', () => {
    const results = [
      { symbol: 'sh600519', code: '600519', name: '贵州茅台', market: 'sh' },
    ]
    const pick = pickStockMatch(results, '600519')
    expect(pick?.name).toBe('贵州茅台')
  })

  it('sym=sh600519：通过完整 symbol 严格匹配返回', () => {
    const results = [
      { symbol: 'sh600518', code: '600518', name: '康美药业', market: 'sh' },
      { symbol: 'sh600519', code: '600519', name: '贵州茅台', market: 'sh' },
    ]
    const pick = pickStockMatch(results, 'sh600519')
    expect(pick?.symbol).toBe('sh600519')
    expect(pick?.name).toBe('贵州茅台')
  })

  it('sym=399001：market 一致，返回深证成指（正常指数查询）', () => {
    const results = [
      { symbol: 'sz399001', code: '399001', name: '深证成指', market: 'sz' },
    ]
    const pick = pickStockMatch(results, '399001')
    expect(pick?.name).toBe('深证成指')
  })

  it('sym=000001：结果只含 sh000001 -> 兜底返回首条（即便是指数）', () => {
    const results = [
      { symbol: 'sh000001', code: '000001', name: '上证指数', market: 'sh' },
    ]
    const pick = pickStockMatch(results, '000001')
    // code 相等兜底
    expect(pick?.symbol).toBe('sh000001')
  })

  it('空数组返回 null', () => {
    expect(pickStockMatch([], '000001')).toBe(null)
  })

  it('sym 为 null/空 -> 返回首条或 null', () => {
    expect(pickStockMatch(null, '000001')).toBe(null)
    expect(pickStockMatch([{ symbol: 'sh600519', name: 'x' }], null)?.symbol).toBe('sh600519')
    expect(pickStockMatch([{ symbol: 'sh600519', name: 'x' }], '')?.symbol).toBe('sh600519')
  })

  it('sym=300750：返回 sz300750 宁德时代（market 一致优先）', () => {
    const results = [
      // 假设接口异常返回了一条 market 不一致的脏数据在前
      { symbol: 'sh300750', code: '300750', name: '脏数据', market: 'sh' },
      { symbol: 'sz300750', code: '300750', name: '宁德时代', market: 'sz' },
    ]
    const pick = pickStockMatch(results, '300750')
    expect(pick?.name).toBe('宁德时代')
  })

  it('code 完全不在结果中 -> 返回首条兜底', () => {
    const results = [
      { symbol: 'sh600519', code: '600519', name: '贵州茅台', market: 'sh' },
    ]
    const pick = pickStockMatch(results, '999999')
    expect(pick?.symbol).toBe('sh600519')
  })
})
