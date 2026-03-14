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
  klineResizeMock: vi.fn(),
  minuteResizeMock: vi.fn(),
  klineSubscribeActionMock: vi.fn(),
  minuteSubscribeActionMock: vi.fn(),
  disposeMock: vi.fn()
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
      setStyles: vi.fn(),
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
      removeIndicator: vi.fn(() => true),
      createOverlay: vi.fn(value => {
        targetOverlays.push(...(Array.isArray(value) ? value : [value]))
        return `${kind}-overlay-${targetOverlays.length}`
      }),
      removeOverlay: vi.fn(() => true),
      subscribeAction: vi.fn((type, callback) => {
        targetSubscribe(type, callback)
      }),
      unsubscribeAction: vi.fn(),
      scrollToRealTime: vi.fn(),
      resize: targetResize
    }
  }

  return {
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
    chartMocks.klineResizeMock.mockClear()
    chartMocks.minuteResizeMock.mockClear()
    chartMocks.klineSubscribeActionMock.mockClear()
    chartMocks.minuteSubscribeActionMock.mockClear()
    chartMocks.disposeMock.mockClear()
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

    const aiOverlays = chartMocks.klineOverlayCalls.filter(item => item.groupId === 'kline-ai-levels')
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
