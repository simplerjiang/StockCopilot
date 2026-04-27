// 财报中心股票名称匹配工具：解决腾讯/搜索接口对裸 code（如 000001）
// 同时返回 sh000001(上证指数) 与 sz000001(平安银行) 时的歧义。
// 仅做前端选择逻辑修复，不改后端 /api/stocks/search 排序。

/**
 * 根据 6 位股票/指数代码推断市场前缀。
 * 返回 'sh' / 'sz' / 'bj' / ''（无法推断时为空串）。
 *
 * 规则（覆盖 A 股主板/中小创/科创/北交所，及常见基金/可转债/B 股）：
 *  - 6 -> sh（沪市主板/科创板）
 *  - 9 -> sh（沪 B 股等）
 *  - 5 -> sh（沪市基金/可转债等）
 *  - 0 -> sz（深市主板/中小板）
 *  - 3 -> sz（创业板）
 *  - 2 -> sz（深 B 股）
 *  - 1 -> sz（深市基金/可转债等）
 *  - 4 / 8 -> bj（北交所/新三板）
 *  - 其他 -> ''
 */
export function inferMarketFromCode(code) {
  if (code === null || code === undefined) return ''
  const s = String(code).trim()
  if (!s) return ''
  // 已带前缀时，取末尾 6 位继续判断；纯字母前缀直接返回小写前缀
  let core = s
  const prefixMatch = s.match(/^([a-zA-Z]{2})(\d+)$/)
  if (prefixMatch) {
    return prefixMatch[1].toLowerCase()
  }
  if (!/^\d+$/.test(core)) return ''
  const first = core.charAt(0)
  switch (first) {
    case '6':
    case '9':
    case '5':
      return 'sh'
    case '0':
    case '3':
    case '2':
    case '1':
      return 'sz'
    case '4':
    case '8':
      return 'bj'
    default:
      return ''
  }
}

const extractCode = (item) => {
  if (!item) return ''
  const raw = item.code || item.Code || item.symbol || item.Symbol || ''
  const s = String(raw).trim()
  const m = s.match(/^([a-zA-Z]{2})?(\d{4,6})$/)
  return m ? m[2] : s
}

const extractMarket = (item) => {
  if (!item) return ''
  if (item.market || item.Market) return String(item.market || item.Market).toLowerCase()
  const sym = String(item.symbol || item.Symbol || '').trim()
  const m = sym.match(/^([a-zA-Z]{2})\d+$/)
  return m ? m[1].toLowerCase() : ''
}

const extractSymbol = (item) => {
  if (!item) return ''
  return String(item.symbol || item.Symbol || '').trim()
}

/**
 * 在 /api/stocks/search 返回的结果数组里挑选与 sym 最匹配的一项。
 *
 * 优先级：
 *  1. 完整 symbol 严格相等（d.symbol === sym）
 *  2. code 相等 且 market 与 inferredMarket 一致
 *  3. code 相等（任意 market，作为兜底）
 *  4. 数组首条（最终兜底）
 *
 * 关键约束：当 inferredMarket 已确定，且某结果 code 相等但 market 与之不一致，
 * 不会在第 2 级返回它；只能在第 3/4 兜底链中被选中。这样 sym='000001' 时
 * 不会因 sh000001(上证指数) 排在前面就被选成上证指数。
 */
export function pickStockMatch(results, sym) {
  if (!Array.isArray(results) || results.length === 0) return null
  if (sym === null || sym === undefined || sym === '') return results[0] || null
  const symStr = String(sym).trim()
  if (!symStr) return results[0] || null

  // 提取 sym 的 code 与 inferredMarket
  const symPrefixMatch = symStr.match(/^([a-zA-Z]{2})(\d{4,6})$/)
  const symMarket = symPrefixMatch ? symPrefixMatch[1].toLowerCase() : ''
  const symCode = symPrefixMatch ? symPrefixMatch[2] : (/^\d+$/.test(symStr) ? symStr : '')
  const inferredMarket = symMarket || inferMarketFromCode(symCode)

  // 1. 完整 symbol 严格相等
  const exactSymbol = results.find(d => extractSymbol(d) === symStr)
  if (exactSymbol) return exactSymbol

  if (symCode) {
    const sameCode = results.filter(d => extractCode(d) === symCode)
    if (sameCode.length > 0) {
      // 2. code 相等 且 market 一致
      if (inferredMarket) {
        const marketMatch = sameCode.find(d => extractMarket(d) === inferredMarket)
        if (marketMatch) return marketMatch
      }
      // 3. code 相等（任意 market）兜底
      return sameCode[0]
    }
  }

  // 4. 最终兜底
  return results[0] || null
}
