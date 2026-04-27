import { formatDate, normalizePlanNumber, normalizeStockSymbol } from './stockInfoTabFormatting'
import {
  getTradingPlanReviewText,
  normalizeTradingPlanStatus,
  parseTradingPlanAlertMetadata
} from './tradingPlanReview'

export const TRADING_PLAN_SCENARIO_OPTIONS = [
  { value: 'Primary', label: '主场景' },
  { value: 'Backup', label: '备选场景' }
]

export const PLAN_MARKET_CONTEXT_MISSING_LABELS = ['市场阶段', '主线方向', '仓位建议', '执行节奏']

const normalizePlanDateValue = value => {
  if (!value) return ''
  if (typeof value === 'string') {
    const trimmed = value.trim()
    if (!trimmed) return ''
    const match = trimmed.match(/^\d{4}-\d{2}-\d{2}/)
    if (match) return match[0]
  }

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

export const normalizeTradingPlanScenario = value => {
  if (!value) return ''
  const normalized = String(value).trim()
  if (!normalized) return ''
  if (['Primary', 'Main', '主场景'].includes(normalized)) return 'Primary'
  if (['Backup', 'Alternative', '备选场景'].includes(normalized)) return 'Backup'
  return normalized
}

export const formatTradingPlanScenario = value => {
  const normalized = normalizeTradingPlanScenario(value)
  return TRADING_PLAN_SCENARIO_OPTIONS.find(item => item.value === normalized)?.label || normalized || ''
}

export const formatTradingPlanDateRange = (startDate, endDate) => {
  const start = normalizePlanDateValue(startDate)
  const end = normalizePlanDateValue(endDate)
  if (start && end) return `有效期 ${start} ~ ${end}`
  if (start) return `开始 ${start}`
  if (end) return `到期 ${end}`
  return ''
}

export const getTradingPlanExpiryText = item => {
  const end = normalizePlanDateValue(item?.planEndDate ?? item?.PlanEndDate)
  if (!end) return ''
  return normalizeTradingPlanStatus(item?.status ?? item?.Status) === 'Invalid'
    ? `已于 ${end} 到期`
    : `到期 ${end}`
}

const readMarketContextField = (item, key) => item?.[key] ?? item?.[`${key[0].toUpperCase()}${key.slice(1)}`]

export const getMissingMarketContextLabels = item => {
  if (!item) return [...PLAN_MARKET_CONTEXT_MISSING_LABELS]

  const missing = []
  if (!readMarketContextField(item, 'stageLabel')) missing.push('市场阶段')
  if (!readMarketContextField(item, 'mainlineSectorName')) missing.push('主线方向')

  const positionScale = readMarketContextField(item, 'suggestedPositionScale')
  if (positionScale == null || positionScale === '') missing.push('仓位建议')

  if (!readMarketContextField(item, 'executionFrequencyLabel')) missing.push('执行节奏')
  return missing
}

export const buildPlanMarketContextMessage = missingLabels => {
  const labels = Array.isArray(missingLabels) && missingLabels.length ? missingLabels : PLAN_MARKET_CONTEXT_MISSING_LABELS
  return `当前未获取到${labels.join('、')}，不影响保存计划。`
}

export const normalizeMarketContext = item => item ? ({
  snapshotTime: item?.snapshotTime ?? item?.SnapshotTime ?? '',
  stageLabel: item?.stageLabel ?? item?.StageLabel ?? '混沌',
  stageConfidence: normalizePlanNumber(item?.stageConfidence ?? item?.StageConfidence) ?? 0,
  stockSectorName: item?.stockSectorName ?? item?.StockSectorName ?? '',
  mainlineSectorName: item?.mainlineSectorName ?? item?.MainlineSectorName ?? '',
  sectorCode: item?.sectorCode ?? item?.SectorCode ?? '',
  mainlineScore: normalizePlanNumber(item?.mainlineScore ?? item?.MainlineScore) ?? 0,
  suggestedPositionScale: normalizePlanNumber(item?.suggestedPositionScale ?? item?.SuggestedPositionScale) ?? 0,
  executionFrequencyLabel: item?.executionFrequencyLabel ?? item?.ExecutionFrequencyLabel ?? '',
  counterTrendWarning: Boolean(item?.counterTrendWarning ?? item?.CounterTrendWarning ?? false),
  isMainlineAligned: Boolean(item?.isMainlineAligned ?? item?.IsMainlineAligned ?? false)
}) : null

export const normalizeTradingPlan = item => ({
  id: item?.id ?? item?.Id ?? '',
  symbol: item?.symbol ?? item?.Symbol ?? '',
  name: item?.name ?? item?.Name ?? '',
  direction: item?.direction ?? item?.Direction ?? 'Long',
  status: normalizeTradingPlanStatus(item?.status ?? item?.Status),
  triggerPrice: normalizePlanNumber(item?.triggerPrice ?? item?.TriggerPrice),
  invalidPrice: normalizePlanNumber(item?.invalidPrice ?? item?.InvalidPrice),
  stopLossPrice: normalizePlanNumber(item?.stopLossPrice ?? item?.StopLossPrice),
  takeProfitPrice: normalizePlanNumber(item?.takeProfitPrice ?? item?.TakeProfitPrice),
  targetPrice: normalizePlanNumber(item?.targetPrice ?? item?.TargetPrice),
  expectedCatalyst: item?.expectedCatalyst ?? item?.ExpectedCatalyst ?? '',
  invalidConditions: item?.invalidConditions ?? item?.InvalidConditions ?? '',
  riskLimits: item?.riskLimits ?? item?.RiskLimits ?? '',
  analysisSummary: item?.analysisSummary ?? item?.AnalysisSummary ?? '',
  analysisHistoryId: item?.analysisHistoryId ?? item?.AnalysisHistoryId ?? '',
  sourceAgent: item?.sourceAgent ?? item?.SourceAgent ?? 'commander',
  userNote: item?.userNote ?? item?.UserNote ?? '',
  activeScenario: normalizeTradingPlanScenario(item?.activeScenario ?? item?.ActiveScenario),
  planStartDate: normalizePlanDateValue(item?.planStartDate ?? item?.PlanStartDate),
  planEndDate: normalizePlanDateValue(item?.planEndDate ?? item?.PlanEndDate),
  createdAt: item?.createdAt ?? item?.CreatedAt ?? '',
  updatedAt: item?.updatedAt ?? item?.UpdatedAt ?? '',
  watchlistEnsured: item?.watchlistEnsured ?? item?.WatchlistEnsured ?? null,
  marketContext: normalizeMarketContext(item?.marketContext ?? item?.MarketContext ?? null),
  marketContextAtCreation: normalizeMarketContext(item?.marketContextAtCreation ?? item?.MarketContextAtCreation ?? null),
  currentMarketContext: normalizeMarketContext(item?.currentMarketContext ?? item?.CurrentMarketContext ?? null),
  executionSummary: normalizeExecutionSummary(item?.executionSummary ?? item?.ExecutionSummary ?? null),
  currentScenarioStatus: normalizeScenarioStatus(item?.currentScenarioStatus ?? item?.CurrentScenarioStatus ?? null),
  currentPositionSnapshot: normalizePositionSnapshot(item?.currentPositionSnapshot ?? item?.CurrentPositionSnapshot ?? null)
})

export const normalizeExecutionSummary = item => item ? ({
  executionCount: Number(item?.executionCount ?? item?.ExecutionCount ?? 0),
  latestAction: item?.latestAction ?? item?.LatestAction ?? '',
  latestExecutedAt: item?.latestExecutedAt ?? item?.LatestExecutedAt ?? '',
  deviatedCount: Number(item?.deviatedCount ?? item?.DeviatedCount ?? 0),
  unplannedCount: Number(item?.unplannedCount ?? item?.UnplannedCount ?? 0),
  latestComplianceTag: item?.latestComplianceTag ?? item?.LatestComplianceTag ?? '',
  latestDeviationTags: item?.latestDeviationTags ?? item?.LatestDeviationTags ?? [],
  summary: item?.summary ?? item?.Summary ?? ''
}) : null

export const normalizeScenarioStatus = item => item ? ({
  code: item?.code ?? item?.Code ?? 'Watch',
  label: item?.label ?? item?.Label ?? '待观察',
  reason: item?.reason ?? item?.Reason ?? '',
  snapshotType: item?.snapshotType ?? item?.SnapshotType ?? 'Current',
  snapshotAt: item?.snapshotAt ?? item?.SnapshotAt ?? '',
  referencePrice: normalizePlanNumber(item?.referencePrice ?? item?.ReferencePrice),
  marketStage: item?.marketStage ?? item?.MarketStage ?? '',
  counterTrendWarning: Boolean(item?.counterTrendWarning ?? item?.CounterTrendWarning ?? false),
  isMainlineAligned: Boolean(item?.isMainlineAligned ?? item?.IsMainlineAligned ?? false),
  abandonTriggered: Boolean(item?.abandonTriggered ?? item?.AbandonTriggered ?? false),
  planStatus: item?.planStatus ?? item?.PlanStatus ?? '',
  summary: item?.summary ?? item?.Summary ?? ''
}) : null

export const normalizePositionSnapshot = item => item ? ({
  symbol: item?.symbol ?? item?.Symbol ?? '',
  name: item?.name ?? item?.Name ?? '',
  quantity: Number(item?.quantity ?? item?.Quantity ?? 0),
  averageCost: normalizePlanNumber(item?.averageCost ?? item?.AverageCost) ?? 0,
  latestPrice: normalizePlanNumber(item?.latestPrice ?? item?.LatestPrice),
  marketValue: normalizePlanNumber(item?.marketValue ?? item?.MarketValue),
  unrealizedPnL: normalizePlanNumber(item?.unrealizedPnL ?? item?.UnrealizedPnL),
  positionRatio: normalizePlanNumber(item?.positionRatio ?? item?.PositionRatio),
  snapshotType: item?.snapshotType ?? item?.SnapshotType ?? 'Current',
  snapshotAt: item?.snapshotAt ?? item?.SnapshotAt ?? '',
  availableCash: normalizePlanNumber(item?.availableCash ?? item?.AvailableCash),
  totalPositionRatio: normalizePlanNumber(item?.totalPositionRatio ?? item?.TotalPositionRatio),
  summary: item?.summary ?? item?.Summary ?? ''
}) : null

const readFirstField = (item, keys) => {
  if (!item) return undefined
  for (const key of keys) {
    if (Object.prototype.hasOwnProperty.call(item, key)) return item[key]
  }
  return undefined
}

const normalizeSnapshotNumber = (value, defaultWhenMissing = null) => (
  value === undefined ? defaultWhenMissing : normalizePlanNumber(value)
)

const normalizePortfolioPosition = item => {
  if (!item) return null
  const quantityLots = normalizeSnapshotNumber(readFirstField(item, ['quantityLots', 'QuantityLots', 'quantity', 'Quantity']), 0)
  const avgCostPrice = normalizeSnapshotNumber(readFirstField(item, ['avgCostPrice', 'AvgCostPrice', 'averageCost', 'AverageCost']))

  return {
    symbol: readFirstField(item, ['symbol', 'Symbol']) ?? '',
    name: readFirstField(item, ['name', 'Name']) ?? '',
    quantityLots,
    quantity: quantityLots,
    avgCostPrice,
    averageCost: avgCostPrice,
    totalCost: normalizeSnapshotNumber(readFirstField(item, ['totalCost', 'TotalCost']), 0),
    latestPrice: normalizeSnapshotNumber(readFirstField(item, ['latestPrice', 'LatestPrice'])),
    marketValue: normalizeSnapshotNumber(readFirstField(item, ['marketValue', 'MarketValue']), 0),
    unrealizedPnL: normalizeSnapshotNumber(readFirstField(item, ['unrealizedPnL', 'UnrealizedPnL']), 0),
    unrealizedReturnRate: normalizeSnapshotNumber(readFirstField(item, ['unrealizedReturnRate', 'UnrealizedReturnRate'])),
    positionRatio: normalizeSnapshotNumber(readFirstField(item, ['positionRatio', 'PositionRatio']))
  }
}

export const normalizePortfolioSnapshot = item => {
  if (!item) return null
  const positions = readFirstField(item, ['positions', 'Positions'])
  return {
    totalCapital: normalizeSnapshotNumber(readFirstField(item, ['totalCapital', 'TotalCapital']), 0),
    totalCost: normalizeSnapshotNumber(readFirstField(item, ['totalCost', 'TotalCost']), 0),
    totalMarketValue: normalizeSnapshotNumber(readFirstField(item, ['totalMarketValue', 'TotalMarketValue']), 0),
    totalUnrealizedPnL: normalizeSnapshotNumber(readFirstField(item, ['totalUnrealizedPnL', 'TotalUnrealizedPnL']), 0),
    availableCash: normalizeSnapshotNumber(readFirstField(item, ['availableCash', 'AvailableCash']), 0),
    totalPositionRatio: normalizeSnapshotNumber(readFirstField(item, ['totalPositionRatio', 'TotalPositionRatio']), 0),
    positions: Array.isArray(positions) ? positions.map(normalizePortfolioPosition).filter(Boolean) : []
  }
}

const normalizeExposureMode = item => item ? ({
  executionMode: readFirstField(item, ['executionMode', 'ExecutionMode']) ?? '',
  confirmationLevel: readFirstField(item, ['confirmationLevel', 'ConfirmationLevel']) ?? ''
}) : null

const normalizeSymbolExposure = item => item ? ({
  symbol: readFirstField(item, ['symbol', 'Symbol']) ?? '',
  name: readFirstField(item, ['name', 'Name']) ?? '',
  exposure: normalizeSnapshotNumber(readFirstField(item, ['exposure', 'Exposure']), 0),
  marketValue: normalizeSnapshotNumber(readFirstField(item, ['marketValue', 'MarketValue']), 0)
}) : null

const normalizeSectorExposure = item => item ? ({
  sectorName: readFirstField(item, ['sectorName', 'SectorName']) ?? '',
  exposure: normalizeSnapshotNumber(readFirstField(item, ['exposure', 'Exposure']), 0),
  marketValue: normalizeSnapshotNumber(readFirstField(item, ['marketValue', 'MarketValue']), 0)
}) : null

export const normalizePortfolioExposure = item => {
  if (!item) return null
  const symbolExposures = readFirstField(item, ['symbolExposures', 'SymbolExposures'])
  const sectorExposures = readFirstField(item, ['sectorExposures', 'SectorExposures'])
  return {
    totalExposure: normalizeSnapshotNumber(readFirstField(item, ['totalExposure', 'TotalExposure']), 0),
    pendingExposure: normalizeSnapshotNumber(readFirstField(item, ['pendingExposure', 'PendingExposure']), 0),
    combinedExposure: normalizeSnapshotNumber(readFirstField(item, ['combinedExposure', 'CombinedExposure']), 0),
    symbolExposures: Array.isArray(symbolExposures) ? symbolExposures.map(normalizeSymbolExposure).filter(Boolean) : [],
    sectorExposures: Array.isArray(sectorExposures) ? sectorExposures.map(normalizeSectorExposure).filter(Boolean) : [],
    currentMode: normalizeExposureMode(readFirstField(item, ['currentMode', 'CurrentMode']))
  }
}

export const normalizeTradingPlanAlert = item => ({
  id: item?.id ?? item?.Id ?? '',
  planId: item?.planId ?? item?.PlanId ?? '',
  symbol: item?.symbol ?? item?.Symbol ?? '',
  eventType: item?.eventType ?? item?.EventType ?? '',
  severity: item?.severity ?? item?.Severity ?? 'Info',
  message: item?.message ?? item?.Message ?? '',
  snapshotPrice: normalizePlanNumber(item?.snapshotPrice ?? item?.SnapshotPrice),
  metadataJson: item?.metadataJson ?? item?.MetadataJson ?? '',
  occurredAt: item?.occurredAt ?? item?.OccurredAt ?? ''
})

export const createTradingPlanForm = item => ({
  id: item?.id ?? '',
  symbol: item?.symbol ?? '',
  name: item?.name ?? '',
  direction: item?.direction ?? 'Long',
  status: normalizeTradingPlanStatus(item?.status ?? item?.Status ?? 'Pending'),
  activeScenario: normalizeTradingPlanScenario(item?.activeScenario ?? item?.ActiveScenario) || 'Primary',
  planStartDate: normalizePlanDateValue(item?.planStartDate ?? item?.PlanStartDate),
  planEndDate: normalizePlanDateValue(item?.planEndDate ?? item?.PlanEndDate),
  triggerPrice: item?.triggerPrice ?? '',
  invalidPrice: item?.invalidPrice ?? '',
  stopLossPrice: item?.stopLossPrice ?? '',
  takeProfitPrice: item?.takeProfitPrice ?? '',
  targetPrice: item?.targetPrice ?? '',
  expectedCatalyst: item?.expectedCatalyst ?? '',
  invalidConditions: item?.invalidConditions ?? '',
  riskLimits: item?.riskLimits ?? '',
  analysisSummary: item?.analysisSummary ?? '',
  analysisHistoryId: item?.analysisHistoryId ?? '',
  sourceAgent: item?.sourceAgent ?? 'commander',
  userNote: item?.userNote ?? '',
  marketContext: normalizeMarketContext(item?.marketContext ?? item?.marketContextAtCreation ?? item?.currentMarketContext ?? null),
  marketContextLoading: Boolean(item?.marketContextLoading ?? false),
  marketContextMessage: item?.marketContextMessage ?? '',
  marketContextMissingLabels: Array.isArray(item?.marketContextMissingLabels) ? [...item.marketContextMissingLabels] : [],
  signalMetrics: item?.signalMetrics ?? null,
  realTradeMetrics: item?.realTradeMetrics ?? null,
  executionMode: item?.executionMode ?? null,
  metricsLoading: false
})

export const buildRealtimeContextSymbols = (symbolKey, domesticSymbols, globalSymbols) => {
  return [symbolKey, ...domesticSymbols, ...globalSymbols]
    .map(normalizeStockSymbol)
    .filter(Boolean)
    .filter((item, index, list) => list.indexOf(item) === index)
}

export const getLatestPlanAlert = (workspace, planId) => {
  if (!workspace || !planId) return null
  return (Array.isArray(workspace.planAlerts) ? workspace.planAlerts : []).find(item => String(item.planId) === String(planId)) || null
}

export const getPlanAlertClass = severity => {
  if (severity === 'Critical') return 'plan-alert-critical'
  if (severity === 'Warning') return 'plan-alert-warning'
  return 'plan-alert-info'
}

export const formatPlanAlertSummary = alert => {
  if (!alert) return ''
  const occurredAt = formatDate(alert.occurredAt)
  return occurredAt ? `${alert.message} · ${occurredAt}` : alert.message
}

export const getPlanReviewText = alert => getTradingPlanReviewText(alert)

export const getPlanReviewHeadline = alert => {
  const metadata = parseTradingPlanAlertMetadata(alert?.metadataJson)
  return metadata?.newsTitle || ''
}

export const canEditTradingPlan = item => ['Draft', 'Pending', 'ReviewRequired'].includes(normalizeTradingPlanStatus(item?.status))

export const canResumeTradingPlan = item => normalizeTradingPlanStatus(item?.status) === 'ReviewRequired'

export const canCancelTradingPlan = item => ['Pending', 'ReviewRequired', 'Draft'].includes(normalizeTradingPlanStatus(item?.status))