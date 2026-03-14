import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import StockCharts from './StockCharts.vue'

const chartMocks = vi.hoisted(() => ({
  klineDataLoads: [],
  minuteDataLoads: [],
  klineIndicatorCalls: [],
  minuteIndicatorCalls: [],
  klineOverlayCalls: [],
  minuteOverlayCalls: [],
  klineRemoveIndicatorCalls: [],
  minuteRemoveIndicatorCalls: [],
  klineRemoveOverlayCalls: [],
  minuteRemoveOverlayCalls: [],
  klineStyleCalls: [],
  minuteStyleCalls: [],
  klineResizeMock: vi.fn(),
  minuteResizeMock: vi.fn(),
  klineSubscribeActionMock: vi.fn(),
  minuteSubscribeActionMock: vi.fn(),
  disposeMock: vi.fn(),
  registerIndicatorMock: vi.fn()
}))

const resizeObserverState = vi.hoisted(() => ({
  callback: null
}))

vi.mock('klinecharts', () => {
  let createCount = 0

  const makeChart = kind => {
    const targetLoads = kind === 'kline' ? chartMocks.klineDataLoads : chartMocks.minuteDataLoads
    const targetIndicators = kind === 'kline' ? chartMocks.klineIndicatorCalls : chartMocks.minuteIndicatorCalls
    const targetOverlays = kind === 'kline' ? chartMocks.klineOverlayCalls : chartMocks.minuteOverlayCalls
    const targetRemoveIndicators = kind === 'kline' ? chartMocks.klineRemoveIndicatorCalls : chartMocks.minuteRemoveIndicatorCalls
    const targetRemoveOverlays = kind === 'kline' ? chartMocks.klineRemoveOverlayCalls : chartMocks.minuteRemoveOverlayCalls
    const targetStyles = kind === 'kline' ? chartMocks.klineStyleCalls : chartMocks.minuteStyleCalls
    const targetResize = kind === 'kline' ? chartMocks.klineResizeMock : chartMocks.minuteResizeMock
    const targetSubscribe = kind === 'kline' ? chartMocks.klineSubscribeActionMock : chartMocks.minuteSubscribeActionMock
    let loader = null
    let symbol = { ticker: 'A-SHARE', pricePrecision: 2, volumePrecision: 0 }
    let period = kind === 'minute' ? { type: 'minute', span: 1 } : { type: 'day', span: 1 }

    const invokeLoad = () => {
      if (!loader?.getBars) return
      loader.getBars({
        type: 'init',
        timestamp: null,
        symbol,
        period,
        callback: data => {
          targetLoads.push(data)
        }
      })
    }

    return {
      setStyles: vi.fn(styles => {
        targetStyles.push(styles)
      }),
      setFormatter: vi.fn(),
      setDataLoader: vi.fn(nextLoader => {
        loader = nextLoader
      }),
      setSymbol: vi.fn(nextSymbol => {
        symbol = { ...symbol, ...nextSymbol }
        invokeLoad()
      }),
      setPeriod: vi.fn(nextPeriod => {
        period = nextPeriod
        invokeLoad()
      }),
      resetData: vi.fn(() => {
        invokeLoad()
      }),
      createIndicator: vi.fn((value, isStack, paneOptions) => {
        targetIndicators.push({ value, isStack, paneOptions })
        return `${kind}-indicator-${targetIndicators.length}`
      }),
      removeIndicator: vi.fn(filter => {
        targetRemoveIndicators.push(filter)
        return true
      }),
      createOverlay: vi.fn(value => {
        targetOverlays.push(...(Array.isArray(value) ? value : [value]))
        return `${kind}-overlay-${targetOverlays.length}`
      }),
      removeOverlay: vi.fn(filter => {
        targetRemoveOverlays.push(filter)
        return true
      }),
      subscribeAction: vi.fn((type, callback) => {
        targetSubscribe(type, callback)
      }),
      unsubscribeAction: vi.fn(),
      scrollToRealTime: vi.fn(),
      resize: targetResize
    }
  }

  return {
    registerIndicator: chartMocks.registerIndicatorMock,
    init: vi.fn(() => {
      createCount += 1
      return createCount % 2 === 1 ? makeChart('kline') : makeChart('minute')
    }),
    dispose: chartMocks.disposeMock
  }
})

describe('StockCharts', () => {
  beforeEach(() => {
    chartMocks.klineDataLoads.length = 0
    chartMocks.minuteDataLoads.length = 0
    chartMocks.klineIndicatorCalls.length = 0
    chartMocks.minuteIndicatorCalls.length = 0
    chartMocks.klineOverlayCalls.length = 0
    chartMocks.minuteOverlayCalls.length = 0
    chartMocks.klineRemoveIndicatorCalls.length = 0
    chartMocks.minuteRemoveIndicatorCalls.length = 0
    chartMocks.klineRemoveOverlayCalls.length = 0
    chartMocks.minuteRemoveOverlayCalls.length = 0
    chartMocks.klineStyleCalls.length = 0
    chartMocks.minuteStyleCalls.length = 0
    chartMocks.klineResizeMock.mockClear()
    chartMocks.minuteResizeMock.mockClear()
    chartMocks.klineSubscribeActionMock.mockClear()
    chartMocks.minuteSubscribeActionMock.mockClear()
    chartMocks.disposeMock.mockClear()
    chartMocks.registerIndicatorMock.mockClear()
    window.matchMedia = vi.fn().mockReturnValue({
      matches: false,
      media: '',
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn()
    })
    global.ResizeObserver = class {
      constructor(callback) {
        resizeObserverState.callback = callback
      }
      observe() {}
      disconnect() {}
    }
  })

  it('parses minute lines and renders professional minute series', async () => {
    Object.defineProperty(HTMLElement.prototype, 'getBoundingClientRect', {
      configurable: true,
      value: () => ({
        width: 600,
        height: 300,
        top: 0,
        left: 0,
        right: 600,
        bottom: 300
      })
    })
    Object.defineProperty(HTMLElement.prototype, 'clientWidth', {
      configurable: true,
      get() {
        return 600
      }
    })
    Object.defineProperty(HTMLElement.prototype, 'clientHeight', {
      configurable: true,
      get() {
        return 300
      }
    })

    const minuteLines = [
      { date: '2026-01-29', time: '09:31:00', price: 31.2, volume: 2300 },
      { date: '2026-01-29', time: '09:30:00', price: 31.1, volume: 1800 }
    ]

    const wrapper = mount(StockCharts, {
      props: {
        kLines: [],
        minuteLines,
        aiLevels: { resistance: 32, support: 30.5 },
        interval: 'day'
      }
    })

    await nextTick()

    expect(wrapper.text()).toContain('分时图')
    const minuteSeriesData = chartMocks.minuteDataLoads.at(-1) ?? []
    expect(minuteSeriesData.length).toBe(2)
    expect(minuteSeriesData[0].close).toBe(31.1)
    expect(minuteSeriesData[0].timestamp).toBeGreaterThan(1700000000000)
    expect(minuteSeriesData[0].timestamp).toBeLessThan(minuteSeriesData[1].timestamp)
    expect(minuteSeriesData[0].volume).toBe(1800)
    expect(minuteSeriesData[1].volume).toBe(500)

    const minuteIndicators = chartMocks.minuteIndicatorCalls.map(item => item.value?.name)
    expect(minuteIndicators).toContain('VOL')

    const minuteAiOverlays = chartMocks.minuteOverlayCalls.filter(item => item.groupId === 'minute-ai-levels')
    const minuteBaseOverlay = chartMocks.minuteOverlayCalls.find(item => item.groupId === 'minute-base-line')
    expect(minuteAiOverlays).toHaveLength(2)
    expect(minuteAiOverlays[0].points[0].value).toBe(32)
    expect(minuteAiOverlays[1].points[0].value).toBe(30.5)
    expect(minuteBaseOverlay?.points[0].value).toBe(31.1)
  })

  it('sorts kline data and includes candlestick + volume + MA overlays', async () => {
    Object.defineProperty(HTMLElement.prototype, 'getBoundingClientRect', {
      configurable: true,
      value: () => ({
        width: 600,
        height: 300,
        top: 0,
        left: 0,
        right: 600,
        bottom: 300
      })
    })
    Object.defineProperty(HTMLElement.prototype, 'clientWidth', {
      configurable: true,
      get() {
        return 600
      }
    })
    Object.defineProperty(HTMLElement.prototype, 'clientHeight', {
      configurable: true,
      get() {
        return 300
      }
    })

    const kLines = [
      { date: '2026-01-01T00:00:00', open: 8, close: 9, low: 7, high: 10, volume: 800 },
      { date: '2026-01-02T00:00:00', open: 10, close: 11, low: 9, high: 12, volume: 1200 },
      { date: '2026-01-03', open: 11, close: 10, low: 9.8, high: 11.5, volume: 900 },
      { date: '2026-01-04', open: 10, close: 10.8, low: 9.7, high: 11.1, volume: 880 },
      { date: '2026-01-05', open: 10.8, close: 11.6, low: 10.4, high: 11.8, volume: 1360 },
      { date: '2026-01-06', open: 11.6, close: 12.1, low: 11.2, high: 12.4, volume: 1500 },
      { date: '2026-01-07', open: 12.1, close: 12.4, low: 11.9, high: 12.8, volume: 1320 },
      { date: '2026-01-08', open: 12.4, close: 12.0, low: 11.7, high: 12.6, volume: 1210 },
      { date: '2026-01-09', open: 12.0, close: 12.8, low: 11.8, high: 13.0, volume: 1680 },
      { date: '2026-01-10', open: 12.8, close: 13.1, low: 12.4, high: 13.3, volume: 1750 }
    ]

    const wrapper = mount(StockCharts, {
      props: {
        kLines,
        minuteLines: [],
        aiLevels: { resistance: 13.5, support: 11.4 },
        interval: 'day'
      }
    })

    await nextTick()

    expect(wrapper.text()).toContain('专业图表终端')

    const klineData = chartMocks.klineDataLoads.at(-1) ?? []
    expect(klineData[0].timestamp).toBeLessThan(klineData[1].timestamp)
    expect(klineData[0].timestamp).toBeGreaterThan(1700000000000)
    expect(klineData[0].open).toBe(8)
    expect(klineData[1].close).toBe(11)

    const klineIndicators = chartMocks.klineIndicatorCalls.map(item => item.value)
    expect(klineIndicators.some(item => item?.name === 'MA' && item?.calcParams?.join(',') === '5,10')).toBe(true)
    expect(klineIndicators.some(item => item?.name === 'VOL')).toBe(true)

    const aiOverlays = chartMocks.klineOverlayCalls.filter(item => item.groupId === 'day-ai-levels')
    expect(aiOverlays).toHaveLength(2)
    expect(aiOverlays[0].points[0].value).toBe(13.5)
    expect(aiOverlays[1].points[0].value).toBe(11.4)
  })

  it('keeps monthly and yearly kline timestamps renderable for higher timeframes', async () => {
    Object.defineProperty(HTMLElement.prototype, 'clientWidth', {
      configurable: true,
      get() {
        return 600
      }
    })
    Object.defineProperty(HTMLElement.prototype, 'clientHeight', {
      configurable: true,
      get() {
        return 300
      }
    })

    const wrapper = mount(StockCharts, {
      props: {
        kLines: [
          { date: '2025-01-01T00:00:00', open: 100, close: 110, low: 90, high: 115, volume: 240000 },
          { date: '2026-01-01T00:00:00', open: 111, close: 125, low: 105, high: 130, volume: 360000 }
        ],
        minuteLines: [],
        interval: 'year'
      }
    })

    await nextTick()

    const klineData = chartMocks.klineDataLoads.at(-1) ?? []
    expect(klineData).toHaveLength(2)
    expect(klineData[0].timestamp).toBeGreaterThan(1700000000000)
    expect(klineData[1].timestamp).toBeGreaterThan(klineData[0].timestamp)
    expect(wrapper.find('.chart-mode').text()).toContain('年K图')
  })

  it('switches to unified chart tabs and only emits interval updates for K-line tabs', async () => {
    Object.defineProperty(HTMLElement.prototype, 'clientWidth', {
      configurable: true,
      get() {
        return 600
      }
    })
    Object.defineProperty(HTMLElement.prototype, 'clientHeight', {
      configurable: true,
      get() {
        return 320
      }
    })

    const wrapper = mount(StockCharts, {
      props: {
        kLines: [{ date: '2026-01-01', open: 8, close: 9, low: 7, high: 10, volume: 800 }],
        minuteLines: [{ date: '2026-01-01', time: '09:30:00', price: 9, volume: 500 }],
        interval: 'day'
      }
    })

    await nextTick()

    const tabs = wrapper.findAll('.tab')
    expect(tabs.map(tab => tab.text())).toEqual(['分时图', '日K图', '月K图', '年K图'])

    await tabs[0].trigger('click')
    expect(wrapper.emitted('update:interval') ?? []).toHaveLength(0)
    expect(wrapper.find('.tab.active').text()).toBe('分时图')

    await tabs[2].trigger('click')
    expect(wrapper.find('.tab.active').text()).toBe('月K图')
    expect(wrapper.emitted('update:interval')).toEqual([['month']])
  })

  it('toggles minute legend chips to control visible layers', async () => {
    Object.defineProperty(HTMLElement.prototype, 'clientWidth', {
      configurable: true,
      get() {
        return 600
      }
    })
    Object.defineProperty(HTMLElement.prototype, 'clientHeight', {
      configurable: true,
      get() {
        return 300
      }
    })

    const wrapper = mount(StockCharts, {
      props: {
        kLines: [],
        minuteLines: [{ date: '2026-01-01', time: '09:30:00', price: 9, volume: 500 }],
        basePrice: 8.8,
        aiLevels: { resistance: 9.3, support: 8.4 },
        interval: 'day'
      }
    })

    await nextTick()

    const minuteTab = wrapper.findAll('.tab').find(tab => tab.text() === '分时图')
    await minuteTab.trigger('click')
    await nextTick()

    const chipButtons = wrapper.findAll('.chart-chip-button')
    const volumeChip = chipButtons.find(button => button.text() === '量能')
    const baseLineChip = chipButtons.find(button => button.text() === '昨收基线')
    const priceChip = chipButtons.find(button => button.text() === '分时')

    await volumeChip.trigger('click')
    await baseLineChip.trigger('click')
    await priceChip.trigger('click')
    await nextTick()

    expect(volumeChip.classes()).not.toContain('active')
    expect(baseLineChip.classes()).not.toContain('active')
    expect(priceChip.classes()).not.toContain('active')
    expect(chartMocks.minuteRemoveIndicatorCalls.some(item => item?.paneId === 'volume_pane' && item?.name === 'VOL')).toBe(true)
    expect(chartMocks.minuteRemoveOverlayCalls.some(item => item?.groupId === 'minute-base-line')).toBe(true)
    expect(chartMocks.minuteStyleCalls.at(-1)?.candle?.area?.lineColor).toBe('rgba(37, 99, 235, 0)')
  })

  it('toggles kline MA and AI chips to control overlays', async () => {
    Object.defineProperty(HTMLElement.prototype, 'clientWidth', {
      configurable: true,
      get() {
        return 600
      }
    })
    Object.defineProperty(HTMLElement.prototype, 'clientHeight', {
      configurable: true,
      get() {
        return 300
      }
    })

    const wrapper = mount(StockCharts, {
      props: {
        kLines: [
          { date: '2026-01-01', open: 8, close: 9, low: 7, high: 10, volume: 800 },
          { date: '2026-01-02', open: 10, close: 11, low: 9, high: 12, volume: 1200 },
          { date: '2026-01-03', open: 11, close: 10, low: 9.8, high: 11.5, volume: 900 },
          { date: '2026-01-04', open: 10, close: 10.8, low: 9.7, high: 11.1, volume: 880 },
          { date: '2026-01-05', open: 10.8, close: 11.6, low: 10.4, high: 11.8, volume: 1360 }
        ],
        minuteLines: [],
        aiLevels: { resistance: 13.5, support: 11.4 },
        interval: 'day'
      }
    })

    await nextTick()

    const chipButtons = wrapper.findAll('.chart-chip-button')
    const ma10Chip = chipButtons.find(button => button.text() === 'MA10')
    const aiChip = chipButtons.find(button => button.text() === 'AI 价位')

    await ma10Chip.trigger('click')
    await aiChip.trigger('click')
    await nextTick()

    expect(ma10Chip.classes()).not.toContain('active')
    expect(aiChip.classes()).not.toContain('active')
    const lastMaCall = chartMocks.klineIndicatorCalls.filter(item => item.value?.name === 'MA').at(-1)
    expect(lastMaCall?.value?.calcParams).toEqual([5])
    expect(chartMocks.klineRemoveOverlayCalls.some(item => item?.groupId === 'day-ai-levels')).toBe(true)
  })

  it('renders grouped strategy controls and enables additional kline indicators', async () => {
    Object.defineProperty(HTMLElement.prototype, 'clientWidth', {
      configurable: true,
      get() {
        return 600
      }
    })
    Object.defineProperty(HTMLElement.prototype, 'clientHeight', {
      configurable: true,
      get() {
        return 320
      }
    })

    const wrapper = mount(StockCharts, {
      props: {
        kLines: [
          { date: '2026-01-01', open: 8, close: 9, low: 7, high: 10, volume: 800 },
          { date: '2026-01-02', open: 10, close: 11, low: 9, high: 12, volume: 1200 },
          { date: '2026-01-03', open: 11, close: 10, low: 9.8, high: 11.5, volume: 900 },
          { date: '2026-01-04', open: 10, close: 10.8, low: 9.7, high: 11.1, volume: 880 },
          { date: '2026-01-05', open: 10.8, close: 11.6, low: 10.4, high: 11.8, volume: 1360 },
          { date: '2026-01-06', open: 11.6, close: 12.1, low: 11.2, high: 12.4, volume: 1500 },
          { date: '2026-01-07', open: 12.1, close: 12.4, low: 11.9, high: 12.8, volume: 1320 },
          { date: '2026-01-08', open: 12.4, close: 12.0, low: 11.7, high: 12.6, volume: 1210 },
          { date: '2026-01-09', open: 12.0, close: 12.8, low: 11.8, high: 13.0, volume: 1680 },
          { date: '2026-01-10', open: 12.8, close: 13.1, low: 12.4, high: 13.3, volume: 1750 },
          { date: '2026-01-11', open: 13.1, close: 13.5, low: 12.8, high: 13.7, volume: 1850 },
          { date: '2026-01-12', open: 13.5, close: 13.9, low: 13.2, high: 14.1, volume: 1960 },
          { date: '2026-01-13', open: 13.9, close: 14.0, low: 13.5, high: 14.3, volume: 1880 },
          { date: '2026-01-14', open: 14.0, close: 14.4, low: 13.8, high: 14.8, volume: 2020 },
          { date: '2026-01-15', open: 14.4, close: 14.9, low: 14.1, high: 15.2, volume: 2150 },
          { date: '2026-01-16', open: 14.9, close: 15.1, low: 14.5, high: 15.4, volume: 2080 },
          { date: '2026-01-17', open: 15.1, close: 15.3, low: 14.8, high: 15.7, volume: 2160 },
          { date: '2026-01-18', open: 15.3, close: 15.6, low: 15.0, high: 15.9, volume: 2220 },
          { date: '2026-01-19', open: 15.6, close: 15.8, low: 15.2, high: 16.0, volume: 2280 },
          { date: '2026-01-20', open: 15.8, close: 16.1, low: 15.4, high: 16.4, volume: 2350 }
        ],
        minuteLines: [],
        interval: 'day'
      }
    })

    await nextTick()

    expect(wrapper.text()).toContain('基础图层')
    expect(wrapper.text()).toContain('趋势策略')
    expect(wrapper.text()).toContain('动量指标')

    const ma60Chip = wrapper.findAll('.chart-chip-button').find(button => button.text() === 'MA60')
    const bollChip = wrapper.findAll('.chart-chip-button').find(button => button.text() === 'BOLL')

    await ma60Chip.trigger('click')
    await bollChip.trigger('click')
    await nextTick()

    const lastMaCall = chartMocks.klineIndicatorCalls.filter(item => item.value?.name === 'MA').at(-1)
    expect(lastMaCall?.value?.calcParams).toEqual([5, 10, 60])
    expect(chartMocks.klineIndicatorCalls.some(item => item.value?.name === 'BOLL')).toBe(true)
  })

  it('enables minute vwap and orb strategies from grouped controls', async () => {
    Object.defineProperty(HTMLElement.prototype, 'clientWidth', {
      configurable: true,
      get() {
        return 600
      }
    })
    Object.defineProperty(HTMLElement.prototype, 'clientHeight', {
      configurable: true,
      get() {
        return 300
      }
    })

    const wrapper = mount(StockCharts, {
      props: {
        kLines: [],
        minuteLines: [
          { date: '2026-01-01', time: '09:30:00', price: 10, volume: 100 },
          { date: '2026-01-01', time: '09:35:00', price: 10.2, volume: 180 },
          { date: '2026-01-01', time: '09:40:00', price: 10.4, volume: 260 },
          { date: '2026-01-01', time: '09:45:00', price: 10.1, volume: 320 }
        ],
        basePrice: 9.9,
        interval: 'day'
      }
    })

    await nextTick()

    await wrapper.findAll('.tab')[0].trigger('click')
    await nextTick()

    const orbChip = wrapper.findAll('.chart-chip-button').find(button => button.text() === 'ORB')
    expect(wrapper.findAll('.chart-chip-button').some(button => button.text() === 'VWAP')).toBe(true)

    await orbChip.trigger('click')
    await nextTick()

    expect(chartMocks.minuteIndicatorCalls.some(item => item.value?.name === 'VWAP')).toBe(true)
    expect(chartMocks.minuteOverlayCalls.some(item => item.groupId === 'minute-orb-range')).toBe(true)
  })

  it('resizes charts after ResizeObserver reports sidebar layout changes', async () => {
    Object.defineProperty(HTMLElement.prototype, 'clientWidth', {
      configurable: true,
      get() {
        return 600
      }
    })
    Object.defineProperty(HTMLElement.prototype, 'clientHeight', {
      configurable: true,
      get() {
        return 300
      }
    })

    const wrapper = mount(StockCharts, {
      props: {
        kLines: [{ date: '2026-01-01', open: 8, close: 9, low: 7, high: 10, volume: 800 }],
        minuteLines: [{ date: '2026-01-01', time: '09:30:00', price: 9, volume: 500 }],
        interval: 'day'
      }
    })

    await nextTick()
  chartMocks.klineResizeMock.mockClear()
  chartMocks.minuteResizeMock.mockClear()

    const charts = wrapper.findAll('.chart')
    Object.defineProperty(charts[0].element, 'clientWidth', { configurable: true, get: () => 720 })
    Object.defineProperty(charts[0].element, 'clientHeight', { configurable: true, get: () => 360 })
    Object.defineProperty(charts[1].element, 'clientWidth', { configurable: true, get: () => 720 })
    Object.defineProperty(charts[1].element, 'clientHeight', { configurable: true, get: () => 260 })

    resizeObserverState.callback?.([{ target: charts[0].element, contentRect: { width: 720, height: 360 } }])
    await new Promise(resolve => setTimeout(resolve, 0))

    expect(chartMocks.klineResizeMock).toHaveBeenCalled()
    expect(chartMocks.minuteResizeMock).toHaveBeenCalled()
  })
})
