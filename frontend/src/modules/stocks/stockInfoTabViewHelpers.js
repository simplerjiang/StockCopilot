import { formatDate, getChangeClass } from './stockInfoTabFormatting'

const normalizeLocalNewsItem = item => ({
  title: item?.title ?? item?.Title ?? '',
  translatedTitle: item?.translatedTitle ?? item?.TranslatedTitle ?? '',
  source: item?.source ?? item?.Source ?? '',
  sourceTag: item?.sourceTag ?? item?.SourceTag ?? '',
  category: item?.category ?? item?.Category ?? '',
  sentiment: item?.sentiment ?? item?.Sentiment ?? '中性',
  publishTime: item?.publishTime ?? item?.PublishTime ?? '',
  crawledAt: item?.crawledAt ?? item?.CrawledAt ?? '',
  url: item?.url ?? item?.Url ?? '',
  aiTarget: item?.aiTarget ?? item?.AiTarget ?? '',
  aiTags: Array.isArray(item?.aiTags ?? item?.AiTags) ? (item.aiTags ?? item.AiTags) : []
})

export const normalizeNewsBucket = (level, payload) => ({
  level,
  symbol: payload?.symbol ?? payload?.Symbol ?? '',
  sectorName: payload?.sectorName ?? payload?.SectorName ?? '',
  items: Array.isArray(payload?.items ?? payload?.Items) ? (payload.items ?? payload.Items).map(normalizeLocalNewsItem) : []
})

const normalizeRealtimeQuote = item => ({
  symbol: item?.symbol ?? item?.Symbol ?? '',
  name: item?.name ?? item?.Name ?? '',
  price: Number(item?.price ?? item?.Price ?? 0),
  change: Number(item?.change ?? item?.Change ?? 0),
  changePercent: Number(item?.changePercent ?? item?.ChangePercent ?? 0),
  turnoverAmount: Number(item?.turnoverAmount ?? item?.TurnoverAmount ?? 0),
  timestamp: item?.timestamp ?? item?.Timestamp ?? ''
})

const normalizeRealtimeOverviewSection = (payload, legacyPayload) => {
  const source = payload ?? legacyPayload
  if (!source) {
    return null
  }

  return {
    snapshotTime: source?.snapshotTime ?? source?.SnapshotTime ?? '',
    amountUnit: source?.amountUnit ?? source?.AmountUnit ?? '亿元',
    mainNetInflow: Number(source?.mainNetInflow ?? source?.MainNetInflow ?? 0),
    superLargeOrderNetInflow: Number(source?.superLargeOrderNetInflow ?? source?.SuperLargeOrderNetInflow ?? 0),
    totalNetInflow: Number(source?.totalNetInflow ?? source?.TotalNetInflow ?? 0),
    shanghaiNetInflow: Number(source?.shanghaiNetInflow ?? source?.ShanghaiNetInflow ?? 0),
    shenzhenNetInflow: Number(source?.shenzhenNetInflow ?? source?.ShenzhenNetInflow ?? 0),
    tradingDate: source?.tradingDate ?? source?.TradingDate ?? '',
    advancers: Number(source?.advancers ?? source?.Advancers ?? 0),
    decliners: Number(source?.decliners ?? source?.Decliners ?? 0),
    flatCount: Number(source?.flatCount ?? source?.FlatCount ?? 0),
    limitUpCount: Number(source?.limitUpCount ?? source?.LimitUpCount ?? 0),
    limitDownCount: Number(source?.limitDownCount ?? source?.LimitDownCount ?? 0),
    isStale: Boolean(source?.isStale ?? source?.IsStale ?? false),
    status: source?.status ?? source?.Status ?? 'ok'
  }
}

export const normalizeRealtimeOverview = payload => payload ? ({
  snapshotTime: payload?.snapshotTime ?? payload?.SnapshotTime ?? '',
  isStale: Boolean(payload?.isStale ?? payload?.IsStale ?? false),
  indices: Array.isArray(payload?.indices ?? payload?.Indices) ? (payload.indices ?? payload.Indices).map(normalizeRealtimeQuote) : [],
  mainCapitalFlow: normalizeRealtimeOverviewSection(payload?.mainCapitalFlow, payload?.MainCapitalFlow),
  northboundFlow: normalizeRealtimeOverviewSection(payload?.northboundFlow, payload?.NorthboundFlow),
  breadth: normalizeRealtimeOverviewSection(payload?.breadth, payload?.Breadth)
}) : null

export const normalizeOptionalText = value => {
  const result = String(value ?? '').trim()
  return result || null
}

export const buildStockContext = currentDetail => {
  const quote = currentDetail?.quote
  if (!quote) return ''
  const name = quote.name ?? ''
  const symbol = quote.symbol ?? ''
  const price = quote.price ?? ''
  const change = quote.change ?? ''
  const changePercent = quote.changePercent ?? ''
  const high = quote.high ?? ''
  const low = quote.low ?? ''
  const peRatio = quote.peRatio ?? ''
  const floatMarketCap = quote.floatMarketCap ?? ''
  const volumeRatio = quote.volumeRatio ?? ''
  const shareholderCount = quote.shareholderCount ?? ''
  const sectorName = quote.sectorName ?? ''
  const timestamp = quote.timestamp ?? ''
  return `股票：${name}（${symbol}）\n价格：${price}\n涨跌：${change}（${changePercent}%）\n高：${high} 低：${low}\n市盈率：${peRatio}\n流通市值：${floatMarketCap}\n量比：${volumeRatio}\n股东户数：${shareholderCount}\n所属板块：${sectorName}\n时间：${formatDate(timestamp)}`
}

export const parseLevelNumber = value => {
  const number = Number(value)
  return Number.isFinite(number) ? number : null
}

export const extractTaggedPriceLevels = (value, patterns) => {
  const text = String(value || '')
  const results = []
  patterns.forEach(pattern => {
    const matches = text.matchAll(pattern)
    for (const match of matches) {
      const number = Number(match[1])
      if (Number.isFinite(number)) {
        results.push(number)
      }
    }
  })
  return results
}

export const getPriceClass = item => {
  const change = item.changePercent ?? item.ChangePercent ?? 0
  return getChangeClass(change)
}

export const getHighClass = item => {
  const price = Number(item.price ?? item.Price)
  const high = Number(item.high ?? item.High)
  if (Number.isNaN(price) || Number.isNaN(high)) return ''
  return high >= price ? 'text-rise' : 'text-fall'
}

export const getLowClass = item => {
  const price = Number(item.price ?? item.Price)
  const low = Number(item.low ?? item.Low)
  if (Number.isNaN(price) || Number.isNaN(low)) return ''
  return low <= price ? 'text-fall' : 'text-rise'
}

export const formatPercent = value => {
  if (value === null || value === undefined || value === '') return ''
  return `${value}%`
}

export const getSortValue = (item, key) => {
  switch (key) {
    case 'id':
      return Number(item.id ?? item.Id ?? 0)
    case 'symbol':
      return String(item.symbol ?? item.Symbol ?? '')
    case 'name':
      return String(item.name ?? item.Name ?? '')
    case 'price':
      return Number(item.price ?? item.Price ?? 0)
    case 'changePercent':
      return Number(item.changePercent ?? item.ChangePercent ?? 0)
    case 'turnoverRate':
      return Number(item.turnoverRate ?? item.TurnoverRate ?? 0)
    case 'peRatio':
      return Number(item.peRatio ?? item.PeRatio ?? 0)
    case 'speed':
      return Number(item.speed ?? item.Speed ?? 0)
    case 'high':
      return Number(item.high ?? item.High ?? 0)
    case 'low':
      return Number(item.low ?? item.Low ?? 0)
    case 'updatedAt':
      return new Date(item.updatedAt ?? item.UpdatedAt ?? 0).getTime()
    default:
      return 0
  }
}