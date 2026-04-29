<script setup>
import { computed, nextTick, onActivated, onMounted, onUnmounted, ref, watch } from 'vue'
import { CHART_VIEW_OPTIONS, isKlineChartView, normalizeKlineInterval, resolveInitialChartView } from './charting/chartViews'
import { createStrategyVisibilityState, getActiveStrategyBadgesForView, getStrategyGroupsForView } from './charting/chartStrategyRegistry'
import { useStockChartAdapter } from './charting/useStockChartAdapter'
import { fetchBackendGet } from './stockInfoTabRequestUtils'

const props = defineProps({
  symbol: {
    type: String,
    default: ''
  },
  kLines: {
    type: Array,
    default: () => []
  },
  minuteLines: {
    type: Array,
    default: () => []
  },
  basePrice: {
    type: Number,
    default: null
  },
  interval: {
    type: String,
    default: 'day'
  },
  focusedView: {
    type: String,
    default: ''
  },
  aiLevels: {
    type: Object,
    default: null
  },
  volumeRatio: {
    type: Number,
    default: null
  },
  retailHeatData: {
    type: Object,
    default: null
  }
})

const emit = defineEmits(['update:interval', 'view-change', 'strategy-visibility-change'])

const chartWrapperRef = ref(null)
const chartShellRef = ref(null)
const klineRef = ref(null)
const minuteRef = ref(null)
let resizeObserver = null
let resizeHandler = null
const activeView = ref(resolveInitialChartView(props.interval))
const featureVisibilityByView = ref(createStrategyVisibilityState())
const showFloatingBadges = ref(true)
const hoveredBadgeId = ref(null)
const isFullscreen = ref(false)
const isFallbackFullscreen = ref(false)
const backtestResults = ref(null)
let backtestFetchedSymbol = ''

const {
  aiLevelText,
  destroyCharts,
  klineHover,
  minuteHover,
  mountCharts,
  queueResize,
  renderAll,
  scrollChartsToRealTime
} = useStockChartAdapter({ props, chartShellRef, klineRef, minuteRef, featureVisibilityByView, backtestResults })

const activeTab = computed(() => CHART_VIEW_OPTIONS.find(item => item.id === activeView.value) ?? CHART_VIEW_OPTIONS[0])
const activeHover = computed(() => (activeView.value === 'minute' ? minuteHover.value : klineHover.value))
const activePlaceholder = computed(() => (activeView.value === 'minute' ? '休市中 · 下一交易日开盘后自动加载' : '暂无 K 线数据'))
const hasActiveData = computed(() => (activeView.value === 'minute' ? props.minuteLines.length > 0 : props.kLines.length > 0))
const strategyGroups = computed(() => getStrategyGroupsForView(activeView.value, featureVisibilityByView.value[activeView.value] ?? {}))
const activeStrategyBadges = computed(() => getActiveStrategyBadgesForView(activeView.value, featureVisibilityByView.value[activeView.value] ?? {}))
const hoveredStrategyBadge = computed(() => activeStrategyBadges.value.find(item => item.id === hoveredBadgeId.value) ?? null)

const getFullscreenElement = () => document.fullscreenElement ?? document.webkitFullscreenElement ?? document.msFullscreenElement ?? null

const requestNativeFullscreen = async () => {
  const element = chartWrapperRef.value
  const request = element?.requestFullscreen ?? element?.webkitRequestFullscreen ?? element?.msRequestFullscreen
  if (request) {
    await request.call(element)
  }
}

const exitNativeFullscreen = async () => {
  const exit = document.exitFullscreen ?? document.webkitExitFullscreen ?? document.msExitFullscreen
  if (exit) {
    await exit.call(document)
  }
}

const syncFullscreenState = () => {
  const fullscreenElement = getFullscreenElement()
  if (fullscreenElement) {
    isFullscreen.value = fullscreenElement === chartWrapperRef.value
    if (!isFullscreen.value) {
      isFallbackFullscreen.value = false
    }
    return
  }
  if (!isFallbackFullscreen.value) {
    isFullscreen.value = false
  }
}

const handleFullscreenChange = () => {
  syncFullscreenState()
  nextTick(() => {
    queueResize()
  })
}

const handleFullscreenKeydown = event => {
  if (event.key !== 'Escape' || !isFallbackFullscreen.value) {
    return
  }
  isFallbackFullscreen.value = false
  isFullscreen.value = false
  nextTick(() => {
    queueResize()
  })
}

const toggleFullscreen = async () => {
  const element = chartWrapperRef.value
  const hasNativeFullscreen = Boolean(
    element &&
    (element.requestFullscreen ?? element.webkitRequestFullscreen ?? element.msRequestFullscreen) &&
    (document.exitFullscreen ?? document.webkitExitFullscreen ?? document.msExitFullscreen)
  )

  if (hasNativeFullscreen) {
    if (getFullscreenElement() === element) {
      await exitNativeFullscreen()
    } else {
      await requestNativeFullscreen()
    }
    syncFullscreenState()
    await nextTick()
    queueResize()
    return
  }

  isFallbackFullscreen.value = !isFallbackFullscreen.value
  isFullscreen.value = isFallbackFullscreen.value
  await nextTick()
  queueResize()
}

const syncKlineInterval = interval => {
  const normalized = normalizeKlineInterval(interval)
  if (normalized !== interval) {
    emit('update:interval', normalized)
  }
  if (isKlineChartView(activeView.value)) {
    activeView.value = normalized
  }
}

const selectView = async viewId => {
  activeView.value = viewId
  if (isKlineChartView(viewId) && viewId !== props.interval) {
    emit('update:interval', viewId)
  }
  await nextTick()
  queueResize()
}

const fetchBacktestResults = async () => {
  const sym = props.symbol
  if (!sym || backtestFetchedSymbol === sym) return
  try {
    const resp = await fetchBackendGet(`/api/backtest/results?symbol=${encodeURIComponent(sym)}&status=calculated&size=100`)
    backtestResults.value = resp?.items ?? []
    backtestFetchedSymbol = sym
  } catch {
    backtestResults.value = []
  }
}

const toggleFeature = async featureId => {
  const isActive = !featureVisibilityByView.value[activeView.value]?.[featureId]
  featureVisibilityByView.value = {
    ...featureVisibilityByView.value,
    [activeView.value]: {
      ...featureVisibilityByView.value[activeView.value],
      [featureId]: isActive
    }
  }
  emit('strategy-visibility-change', {
    viewId: activeView.value,
    strategyId: featureId,
    active: isActive
  })
  if (featureId === 'backtest' && isActive) {
    await fetchBacktestResults()
  }
  renderAll()
  await nextTick()
  queueResize()
}

onMounted(() => {
  mountCharts()
  syncKlineInterval(props.interval)
  syncFullscreenState()
  if (typeof ResizeObserver !== 'undefined') {
    resizeObserver = new ResizeObserver(() => {
      queueResize()
    })
    if (chartShellRef.value) {
      resizeObserver.observe(chartShellRef.value)
    }
    if (klineRef.value) {
      resizeObserver.observe(klineRef.value)
    }
    if (minuteRef.value) {
      resizeObserver.observe(minuteRef.value)
    }
  }
  resizeHandler = () => {
    queueResize()
  }
  window.addEventListener('resize', resizeHandler)
  document.addEventListener('fullscreenchange', handleFullscreenChange)
  document.addEventListener('webkitfullscreenchange', handleFullscreenChange)
  document.addEventListener('MSFullscreenChange', handleFullscreenChange)
  window.addEventListener('keydown', handleFullscreenKeydown)
  nextTick(queueResize)
})

onActivated(() => {
  renderAll()
  nextTick(() => {
    queueResize()
    requestAnimationFrame(() => scrollChartsToRealTime())
  })
})

onUnmounted(() => {
  if (resizeHandler) {
    window.removeEventListener('resize', resizeHandler)
    resizeHandler = null
  }
  if (resizeObserver) {
    resizeObserver.disconnect()
    resizeObserver = null
  }
  document.removeEventListener('fullscreenchange', handleFullscreenChange)
  document.removeEventListener('webkitfullscreenchange', handleFullscreenChange)
  document.removeEventListener('MSFullscreenChange', handleFullscreenChange)
  window.removeEventListener('keydown', handleFullscreenKeydown)
  destroyCharts()
})

watch(() => [props.kLines, props.minuteLines, props.basePrice, props.aiLevels, props.retailHeatData], async () => {
  renderAll()
  await nextTick()
  queueResize()
})

const toggleFloatingBadges = () => {
  showFloatingBadges.value = !showFloatingBadges.value
  if (!showFloatingBadges.value) {
    hoveredBadgeId.value = null
  }
}

const setHoveredBadge = badgeId => {
  hoveredBadgeId.value = badgeId
}

const clearHoveredBadge = badgeId => {
  if (!badgeId || hoveredBadgeId.value === badgeId) {
    hoveredBadgeId.value = null
  }
}

const buildStrategySummary = item => [item.description, item.interpretation, item.usage].filter(Boolean).join(' ')

const resolveBadgeSwatchStyle = item => item.accentSecondaryColor
  ? { backgroundImage: `linear-gradient(135deg, ${item.accentColor}, ${item.accentSecondaryColor})` }
  : { backgroundColor: item.accentColor }

const resolveLineLegendSwatchStyle = item => item.color
  ? { backgroundColor: item.color }
  : { backgroundColor: '#64748b' }

watch(featureVisibilityByView, async () => {
  renderAll()
  await nextTick()
  queueResize()
}, { deep: true })

watch(() => props.symbol, () => {
  backtestResults.value = null
  backtestFetchedSymbol = ''
})

watch(() => props.interval, interval => {
  syncKlineInterval(interval)
})

watch(() => props.focusedView, viewId => {
  if (!viewId || activeView.value === viewId) {
    return
  }

  if (viewId === 'minute') {
    activeView.value = 'minute'
    return
  }

  if (isKlineChartView(viewId)) {
    activeView.value = viewId
    syncKlineInterval(viewId)
  }
})

watch(activeView, async () => {
  emit('view-change', activeView.value)
  await nextTick()
  queueResize()
})
</script>

<template>
  <div class="charts">
    <div
      ref="chartWrapperRef"
      class="chart-wrapper"
      :class="{ 'chart-wrapper-fullscreen': isFullscreen }"
    >
      <div class="chart-header">
        <div>
          <h3>专业图表终端</h3>
          <p v-if="aiLevelText()" class="ai-level-tip">{{ aiLevelText() }}</p>
        </div>
        <div class="chart-header-actions">
          <div class="chart-tabs">
            <button
              v-for="item in CHART_VIEW_OPTIONS"
              :key="item.id"
              class="tab"
              :class="{ active: activeView === item.id }"
              @click="selectView(item.id)"
            >
              {{ item.label }}
            </button>
          </div>
          <button
            type="button"
            class="chart-chip chart-chip-button chart-fullscreen-toggle"
            :class="{ active: isFullscreen }"
            @click="toggleFullscreen"
          >
            {{ isFullscreen ? '退出全屏' : '全屏' }}
          </button>
        </div>
      </div>
      <div class="chart-meta">
        <span class="chart-mode">{{ activeTab.label }}</span>
        <button
          type="button"
          class="chart-chip chart-chip-button chart-badge-toggle"
          :class="{ active: showFloatingBadges }"
          @click="toggleFloatingBadges"
        >
          {{ showFloatingBadges ? '隐藏小标' : '显示小标' }}
        </button>
        <div
          v-for="group in strategyGroups"
          :key="group.id"
          class="chart-strategy-group"
        >
          <span class="chart-group-label">{{ group.label }}</span>
          <button
            v-for="item in group.items"
            :key="item.id"
            type="button"
            class="chart-chip chart-chip-button"
            :class="{ active: item.active }"
            :title="buildStrategySummary(item)"
            @click="toggleFeature(item.id)"
          >
            {{ item.label }}
          </button>
        </div>
      </div>
      <div ref="chartShellRef" class="chart-shell">
        <div v-if="showFloatingBadges && activeStrategyBadges.length" class="chart-floating-legend">
          <button
            v-for="item in activeStrategyBadges"
            :key="item.id"
            type="button"
            class="chart-floating-badge"
            :class="{ active: hoveredBadgeId === item.id }"
            @mouseenter="setHoveredBadge(item.id)"
            @mouseleave="clearHoveredBadge(item.id)"
            @focus="setHoveredBadge(item.id)"
            @blur="clearHoveredBadge(item.id)"
          >
            <span class="chart-floating-swatch" :style="resolveBadgeSwatchStyle(item)" />
            <span>{{ item.label }}</span>
          </button>
        </div>
        <div v-if="showFloatingBadges && hoveredStrategyBadge" class="chart-floating-tooltip">
          <strong>{{ hoveredStrategyBadge.label }}</strong>
          <p><span>介绍：</span>{{ hoveredStrategyBadge.description }}</p>
          <p><span>解释：</span>{{ hoveredStrategyBadge.interpretation }}</p>
          <p><span>用法：</span>{{ hoveredStrategyBadge.usage }}</p>
          <div v-if="hoveredStrategyBadge.lineLegends?.length" class="chart-line-legend">
            <em>颜色对照</em>
            <div
              v-for="item in hoveredStrategyBadge.lineLegends"
              :key="`${hoveredStrategyBadge.id}-${item.label}`"
              class="chart-line-legend-item"
            >
              <span class="chart-line-legend-swatch" :style="resolveLineLegendSwatchStyle(item)" />
              <span class="chart-line-legend-label">{{ item.label }}</span>
              <span class="chart-line-legend-meaning">{{ item.meaning }}</span>
            </div>
          </div>
        </div>
        <div class="minute-chart-layer" :class="{ 'chart-hidden': activeView !== 'minute' }">
          <div ref="minuteRef" class="chart minute-chart-host" />
        </div>
        <div ref="klineRef" class="chart" :class="{ 'chart-hidden': activeView === 'minute' }" />
        <p v-if="!hasActiveData" class="placeholder">{{ activePlaceholder }}</p>
        <div
          v-if="activeHover.visible"
          class="hover-tip"
          :style="{ left: `${activeHover.x}px`, top: `${activeHover.y}px` }"
        >
          <div v-for="(line, idx) in activeHover.lines" :key="idx">{{ line }}</div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.charts {
  display: block;
  width: 100%;
  min-width: 0;
}

.chart-wrapper {
  display: grid;
  grid-template-rows: auto auto minmax(0, 1fr);
  width: 100%;
  min-width: 0;
  overflow: visible;
  position: relative;
  min-height: 0;
  gap: 0.75rem;
}

.chart-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
}

.chart-header-actions {
  display: flex;
  align-items: flex-start;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 0.75rem;
}

.chart-tabs {
  display: flex;
  flex-wrap: wrap;
  gap: 0;
  background: rgba(15, 23, 42, 0.06);
  border-radius: 10px;
  padding: 3px;
}

.chart-tabs .tab {
  border: none;
  border-radius: 8px;
  padding: 0.32rem 0.72rem;
  font-size: 0.82rem;
  font-weight: 500;
  color: var(--color-text-secondary, #64748b);
  background: transparent;
  cursor: pointer;
  transition: background 0.18s ease, color 0.18s ease, box-shadow 0.18s ease;
}

.chart-tabs .tab:hover:not(.active) {
  background: rgba(15, 23, 42, 0.06);
}

.chart-tabs .tab.active {
  background: #fff;
  color: var(--color-accent, #2563eb);
  box-shadow: 0 1px 4px rgba(15, 23, 42, 0.1);
}

.chart-meta {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.55rem;
  color: #94a3b8;
  font-size: 0.8rem;
}

.chart-strategy-group {
  display: inline-flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.45rem;
}

.chart-group-label {
  color: var(--color-text-secondary);
  font-size: 0.75rem;
}

.chart-badge-toggle {
  margin-left: 0.2rem;
}

.chart-fullscreen-toggle {
  white-space: nowrap;
}

.chart-mode,
.chart-chip {
  padding: 0.22rem 0.58rem;
  border-radius: 999px;
  background: rgba(15, 23, 42, 0.06);
}

.chart-chip-button {
  border: 1px solid transparent;
  color: inherit;
  cursor: pointer;
  transition: background-color 120ms ease, color 120ms ease, border-color 120ms ease;
}

.chart-chip-button.active {
  background: rgba(37, 99, 235, 0.14);
  border-color: rgba(37, 99, 235, 0.35);
  color: #1d4ed8;
}

.chart-shell {
  position: relative;
  width: 100%;
  min-width: 0;
  min-height: min(68vh, 760px);
  border-radius: 18px;
  background:
    radial-gradient(circle at top right, rgba(59, 130, 246, 0.16), transparent 34%),
    linear-gradient(180deg, rgba(15, 23, 42, 0.04), rgba(15, 23, 42, 0.02));
  overflow: hidden;
}

.chart-wrapper-fullscreen {
  position: fixed;
  inset: 0;
  z-index: 1200;
  height: 100vh;
  padding: 1rem;
  box-sizing: border-box;
  background: #f8fafc;
  overflow: auto;
  align-content: stretch;
}

.chart-wrapper-fullscreen .chart-shell {
  min-height: max(420px, calc(100vh - 11.5rem));
}

.chart-floating-legend {
  position: absolute;
  top: 0.75rem;
  left: 0.75rem;
  right: 0.75rem;
  z-index: 8;
  display: flex;
  flex-wrap: wrap;
  gap: 0.45rem;
  pointer-events: none;
}

.chart-floating-badge {
  display: inline-flex;
  align-items: center;
  gap: 0.38rem;
  padding: 0.26rem 0.6rem;
  border-radius: 999px;
  border: 1px solid rgba(148, 163, 184, 0.24);
  background: rgba(255, 255, 255, 0.82);
  color: #0f172a;
  font-size: 0.74rem;
  box-shadow: 0 8px 22px rgba(15, 23, 42, 0.08);
  backdrop-filter: blur(14px);
  pointer-events: auto;
  cursor: help;
}

.chart-floating-badge.active {
  border-color: rgba(37, 99, 235, 0.32);
  transform: translateY(-1px);
}

.chart-floating-swatch {
  width: 0.7rem;
  height: 0.7rem;
  border-radius: 999px;
  flex: 0 0 auto;
  box-shadow: inset 0 0 0 1px rgba(255, 255, 255, 0.35);
}

.chart-floating-tooltip {
  position: absolute;
  top: 3.15rem;
  left: 0.75rem;
  z-index: 9;
  max-width: min(30rem, calc(100% - 1.5rem));
  padding: 0.8rem 0.9rem;
  border-radius: 14px;
  background: rgba(15, 23, 42, 0.92);
  color: #e2e8f0;
  box-shadow: 0 14px 36px rgba(15, 23, 42, 0.28);
  backdrop-filter: blur(16px);
}

.chart-floating-tooltip strong {
  display: block;
  margin-bottom: 0.4rem;
  color: #f8fafc;
}

.chart-floating-tooltip p {
  margin: 0.22rem 0;
  line-height: 1.45;
}

.chart-floating-tooltip span {
  color: #93c5fd;
  margin-right: 0.25rem;
}

.chart-line-legend {
  margin-top: 0.65rem;
  padding-top: 0.55rem;
  border-top: 1px solid rgba(148, 163, 184, 0.18);
}

.chart-line-legend em {
  display: block;
  margin-bottom: 0.35rem;
  color: #cbd5e1;
  font-style: normal;
  font-size: 0.78rem;
}

.chart-line-legend-item {
  display: grid;
  grid-template-columns: auto auto 1fr;
  gap: 0.42rem;
  align-items: center;
  margin-top: 0.28rem;
}

.chart-line-legend-swatch {
  width: 0.7rem;
  height: 0.7rem;
  border-radius: 999px;
  box-shadow: inset 0 0 0 1px rgba(255, 255, 255, 0.3);
}

.chart-line-legend-label {
  color: #f8fafc;
  font-weight: 600;
}

.chart-line-legend-meaning {
  color: #cbd5e1;
}

.chart {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  min-width: 0;
  display: block;
}

.minute-chart-layer {
  position: absolute;
  inset: 0;
}

.minute-chart-host {
  z-index: 1;
}

.chart-hidden {
  opacity: 0;
  pointer-events: none;
}

.placeholder {
  position: absolute;
  inset: 0;
  display: grid;
  place-items: center;
  color: var(--color-text-muted);
  margin: 0;
}

.ai-level-tip {
  color: var(--color-text-secondary);
  margin: 0.35rem 0 0;
  font-size: 0.82rem;
}

.hover-tip {
  position: absolute;
  z-index: 20;
  background: rgba(15, 23, 42, 0.9);
  color: #f8fafc;
  padding: 0.5rem 0.7rem;
  border-radius: 8px;
  font-size: 0.8rem;
  pointer-events: none;
  white-space: nowrap;
  box-shadow: 0 12px 28px rgba(15, 23, 42, 0.18);
  border: 1px solid rgba(148, 163, 184, 0.2);
}

@media (max-width: 1180px) {
  .chart-shell {
    min-height: min(56vh, 620px);
  }

  .chart-header {
    flex-direction: column;
  }

  .chart-header-actions {
    width: 100%;
    justify-content: space-between;
  }

  .chart-tabs {
    width: 100%;
  }
}
</style>
