import { formatDate, normalizePlanNumber, normalizeStockSymbol } from './stockInfoTabFormatting'
import {
  getTradingPlanReviewText,
  normalizeTradingPlanStatus,
  parseTradingPlanAlertMetadata
} from './tradingPlanReview'

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
  createdAt: item?.createdAt ?? item?.CreatedAt ?? '',
  updatedAt: item?.updatedAt ?? item?.UpdatedAt ?? '',
  watchlistEnsured: item?.watchlistEnsured ?? item?.WatchlistEnsured ?? null,
  marketContext: normalizeMarketContext(item?.marketContext ?? item?.MarketContext ?? null),
  marketContextAtCreation: normalizeMarketContext(item?.marketContextAtCreation ?? item?.MarketContextAtCreation ?? null),
  currentMarketContext: normalizeMarketContext(item?.currentMarketContext ?? item?.CurrentMarketContext ?? null)
})

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

export const canEditTradingPlan = item => ['Pending', 'ReviewRequired'].includes(normalizeTradingPlanStatus(item?.status))

export const canResumeTradingPlan = item => normalizeTradingPlanStatus(item?.status) === 'ReviewRequired'

export const canCancelTradingPlan = item => ['Pending', 'ReviewRequired', 'Draft'].includes(normalizeTradingPlanStatus(item?.status))