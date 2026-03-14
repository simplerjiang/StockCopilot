<script setup>
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { CHART_VIEW_OPTIONS, isKlineChartView, normalizeKlineInterval, resolveInitialChartView } from './charting/chartViews'
import { useStockChartAdapter } from './charting/useStockChartAdapter'

const props = defineProps({
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
  aiLevels: {
    type: Object,
    default: null
  }
})

const emit = defineEmits(['update:interval'])

const chartShellRef = ref(null)
const klineRef = ref(null)
const minuteRef = ref(null)
let resizeObserver = null
let resizeHandler = null
const activeView = ref(resolveInitialChartView(props.interval))

const {
  aiLevelText,
  destroyCharts,
  klineHover,
  minuteHover,
  mountCharts,
  queueResize,
  renderAll
} = useStockChartAdapter({ props, chartShellRef, klineRef, minuteRef })

const activeTab = computed(() => CHART_VIEW_OPTIONS.find(item => item.id === activeView.value) ?? CHART_VIEW_OPTIONS[0])
const activeHover = computed(() => (activeView.value === 'minute' ? minuteHover.value : klineHover.value))
const activePlaceholder = computed(() => (activeView.value === 'minute' ? '暂无分时数据' : '暂无 K 线数据'))
const hasActiveData = computed(() => (activeView.value === 'minute' ? props.minuteLines.length > 0 : props.kLines.length > 0))
const overlaySlots = computed(() => activeTab.value.legend)

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

onMounted(() => {
  mountCharts()
  syncKlineInterval(props.interval)
  if (typeof ResizeObserver !== 'undefined') {
    resizeObserver = new ResizeObserver(entries => {
      if (!entries?.length) return
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
  nextTick(() => {
    queueResize()
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
  destroyCharts()
})

watch(() => [props.kLines, props.minuteLines, props.basePrice, props.aiLevels], async () => {
  renderAll()
  await nextTick()
  queueResize()
})

watch(() => props.interval, interval => {
  syncKlineInterval(interval)
})
</script>

<template>
  <div class="charts">
    <div class="chart-wrapper">
      <div class="chart-header">
        <div>
          <h3>专业图表终端</h3>
          <p v-if="aiLevelText()" class="ai-level-tip">{{ aiLevelText() }}</p>
        </div>
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
      </div>
      <div class="chart-meta">
        <span class="chart-mode">{{ activeTab.label }}</span>
        <span v-for="item in overlaySlots" :key="item" class="chart-chip">{{ item }}</span>
      </div>
      <div ref="chartShellRef" class="chart-shell">
        <div ref="minuteRef" class="chart" :class="{ 'chart-hidden': activeView !== 'minute' }" />
        <div ref="klineRef" class="chart" :class="{ 'chart-hidden': activeView === 'minute' }" />
        <p v-if="!hasActiveData" class="placeholder">{{ activePlaceholder }}</p>
      </div>
      <div
        v-if="activeHover.visible"
        class="hover-tip"
        :style="{ left: `${activeHover.x}px`, top: `${activeHover.y}px` }"
      >
        <div v-for="(line, idx) in activeHover.lines" :key="idx">{{ line }}</div>
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

.chart-tabs {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.chart-meta {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.55rem;
  color: #94a3b8;
  font-size: 0.8rem;
}

.chart-mode,
.chart-chip {
  padding: 0.22rem 0.58rem;
  border-radius: 999px;
  background: rgba(15, 23, 42, 0.06);
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

.chart {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  min-width: 0;
  display: block;
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
  color: #9ca3af;
  margin: 0;
}

.ai-level-tip {
  color: #64748b;
  margin: 0.35rem 0 0;
  font-size: 0.82rem;
}

.hover-tip {
  position: absolute;
  z-index: 20;
  background: rgba(15, 23, 42, 0.9);
  color: #f8fafc;
  padding: 0.4rem 0.6rem;
  border-radius: 6px;
  font-size: 0.8rem;
  pointer-events: none;
  white-space: nowrap;
}

@media (max-width: 1180px) {
  .chart-shell {
    min-height: min(56vh, 620px);
  }

  .chart-header {
    flex-direction: column;
  }

  .chart-tabs {
    width: 100%;
  }
}
</style>
