const AVAILABLE_STATUSES = new Set(['ok', 'live'])

export const normalizeNorthboundStatus = status => String(status ?? '').trim().toLowerCase()

export const getNorthboundStatusKind = flow => {
  if (!flow) return 'unavailable'

  const status = normalizeNorthboundStatus(flow.status)
  if (status === 'unavailable') return 'unavailable'
  if (status === 'closed') return 'closed'
  if (status === 'stale') return 'stale'
  if (AVAILABLE_STATUSES.has(status)) return 'available'
  if (flow.isStale) return 'stale'
  if (status === '') return 'available'
  return 'unavailable'
}

export const isNorthboundStatusAvailable = flow => getNorthboundStatusKind(flow) === 'available'

export const formatNorthboundUnavailableText = (flow, unavailableText = '不可用') => {
  const kind = getNorthboundStatusKind(flow)
  if (kind === 'closed') return '休市'
  if (kind === 'stale') return '数据待更新'
  if (kind === 'unavailable') return unavailableText
  return ''
}