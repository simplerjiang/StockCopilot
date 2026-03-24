const cnDateTimeFormatter = new Intl.DateTimeFormat('zh-CN', {
  timeZone: 'Asia/Shanghai',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hour12: false
})

export const normalizeStockSymbol = value => {
  const raw = String(value ?? '').trim().toLowerCase()
  if (!raw) return ''
  if (/^(sh|sz)\d{6}$/.test(raw)) return raw
  if (/^6\d{5}$/.test(raw)) return `sh${raw}`
  if (/^[03]\d{5}$/.test(raw)) return `sz${raw}`
  return raw
}

export const isDirectStockSymbol = value => /^(sh|sz)\d{6}$/.test(normalizeStockSymbol(value))

export const formatDate = value => {
  if (!value) return ''
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''
  return cnDateTimeFormatter.format(date)
}

export const formatImpactScore = value => {
  if (value == null || Number.isNaN(Number(value))) return ''
  const num = Number(value)
  return num > 0 ? `+${num}` : `${num}`
}

export const getImpactClass = category => {
  if (category === '利好') return 'impact-positive'
  if (category === '利空') return 'impact-negative'
  return 'impact-neutral'
}

export const getImpactCategoryValue = item => item?.category ?? item?.Category ?? '中性'

export const getImpactScoreValue = item => Number(item?.impactScore ?? item?.ImpactScore ?? 0)

export const getImpactPublishedAtValue = item => {
  const rawValue = item?.publishedAt ?? item?.PublishedAt
  if (!rawValue) {
    return 0
  }
  const timestamp = Date.parse(rawValue)
  return Number.isNaN(timestamp) ? 0 : timestamp
}

export const getHeadlineNewsImpactEvents = impact => {
  const events = Array.isArray(impact?.events ?? impact?.Events) ? (impact.events ?? impact.Events) : []
  const directionalEvents = events.filter(item => {
    const category = getImpactCategoryValue(item)
    return category === '利好' || category === '利空'
  })

  if (!directionalEvents.length) {
    return []
  }

  const cutoff = Date.now() - 72 * 60 * 60 * 1000
  const recentDirectionalEvents = directionalEvents.filter(item => getImpactPublishedAtValue(item) >= cutoff)
  const prioritizedEvents = (recentDirectionalEvents.length ? recentDirectionalEvents : directionalEvents)
    .slice()
    .sort((left, right) => {
      const publishedAtDelta = getImpactPublishedAtValue(right) - getImpactPublishedAtValue(left)
      if (publishedAtDelta !== 0) {
        return publishedAtDelta
      }

      const scoreDelta = Math.abs(getImpactScoreValue(right)) - Math.abs(getImpactScoreValue(left))
      if (scoreDelta !== 0) {
        return scoreDelta
      }

      return String(left?.title ?? left?.Title ?? '').localeCompare(String(right?.title ?? right?.Title ?? ''))
    })

  return prioritizedEvents.slice(0, 6)
}

export const getLocalNewsHeadline = item => item?.translatedTitle || item?.title || ''

export const normalizePlanNumber = value => {
  if (value === '' || value == null) return null
  const number = Number(value)
  return Number.isFinite(number) ? number : null
}

export const formatPlanPrice = value => {
  const number = normalizePlanNumber(value)
  return Number.isFinite(number) ? Number(number).toFixed(2) : '待补录'
}

export const formatPlanScale = value => {
  const number = normalizePlanNumber(value)
  return Number.isFinite(number) ? `${(number * 100).toFixed(0)}%` : '--'
}

export const formatRealtimeMoney = value => {
  const number = Number(value ?? 0)
  const abs = Math.abs(number)
  if (abs >= 100000000) return `${(number / 100000000).toFixed(2)} 亿`
  if (abs >= 10000) return `${(number / 10000).toFixed(2)} 万`
  return number.toFixed(0)
}

export const formatSignedRealtimeAmount = value => {
  const number = Number(value ?? 0)
  return `${number >= 0 ? '+' : ''}${number.toFixed(2)} 亿`
}

export const formatSignedNumber = (value, digits = 2) => {
  const number = Number(value)
  if (Number.isNaN(number)) {
    return ''
  }

  return `${number >= 0 ? '+' : ''}${number.toFixed(digits)}`
}

export const formatSignedPercent = value => {
  const formatted = formatSignedNumber(value, 2)
  return formatted ? `${formatted}%` : ''
}

export const getChangeClass = value => {
  const number = Number(value)
  if (Number.isNaN(number)) return ''
  if (number > 0) return 'text-rise'
  if (number < 0) return 'text-fall'
  return ''
}