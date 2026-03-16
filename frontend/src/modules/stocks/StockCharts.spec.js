import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import StockCharts from './StockCharts.vue'
import { getIndicatorFiltersForView } from './charting/chartStrategyRegistry'

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

  it('shows detailed hover info including price change and change percent for kline bars', async () => {
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
          { date: '2026-01-02', open: 10, close: 11, low: 9, high: 12, volume: 1200 }
        ],
        minuteLines: [],
        interval: 'day'
      }
    })

    await nextTick()

    const crosshairHandler = chartMocks.klineSubscribeActionMock.mock.calls.find(([type]) => type === 'onCrosshairChange')?.[1]
    const klineData = chartMocks.klineDataLoads.at(-1) ?? []

    expect(crosshairHandler).toBeTypeOf('function')
    expect(klineData).toHaveLength(2)

    crosshairHandler({
      timestamp: klineData[1].timestamp,
      kLineData: klineData[1],
      x: 120,
      y: 90
    })
    await nextTick()

    const hoverTip = wrapper.find('.hover-tip')
    expect(hoverTip.exists()).toBe(true)
    expect(hoverTip.text()).toContain('2026-01-02')
    expect(hoverTip.text()).toContain('开: 10')
    expect(hoverTip.text()).toContain('收: 11')
    expect(hoverTip.text()).toContain('涨跌: +2')
    expect(hoverTip.text()).toContain('涨跌幅: 22.22%')
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

  it('toggles the professional terminal fullscreen state with the header button', async () => {
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

    let fullscreenElement = null
    const requestFullscreenMock = vi.fn(function () {
      fullscreenElement = this
      document.dispatchEvent(new Event('fullscreenchange'))
      return Promise.resolve()
    })
    const exitFullscreenMock = vi.fn(() => {
      fullscreenElement = null
      document.dispatchEvent(new Event('fullscreenchange'))
      return Promise.resolve()
    })

    Object.defineProperty(document, 'fullscreenElement', {
      configurable: true,
      get() {
        return fullscreenElement
      }
    })
    Object.defineProperty(document, 'exitFullscreen', {
      configurable: true,
      value: exitFullscreenMock
    })
    Object.defineProperty(HTMLElement.prototype, 'requestFullscreen', {
      configurable: true,
      value: requestFullscreenMock
    })

    const wrapper = mount(StockCharts, {
      props: {
        kLines: [{ date: '2026-01-01', open: 8, close: 9, low: 7, high: 10, volume: 800 }],
        minuteLines: [{ date: '2026-01-01', time: '09:30:00', price: 9, volume: 500 }],
        interval: 'day'
      }
    })

    await nextTick()

    const fullscreenButton = wrapper.find('.chart-fullscreen-toggle')
    expect(fullscreenButton.exists()).toBe(true)
    expect(fullscreenButton.text()).toBe('全屏')

    await fullscreenButton.trigger('click')
    await Promise.resolve()
    await nextTick()

    expect(requestFullscreenMock).toHaveBeenCalledTimes(1)
    expect(wrapper.find('.chart-wrapper').classes()).toContain('chart-wrapper-fullscreen')
    expect(wrapper.find('.chart-fullscreen-toggle').text()).toBe('退出全屏')
    expect(wrapper.find('.chart-fullscreen-toggle').classes()).toContain('active')

    await wrapper.find('.chart-fullscreen-toggle').trigger('click')
    await Promise.resolve()
    await nextTick()

    expect(exitFullscreenMock).toHaveBeenCalledTimes(1)
    expect(wrapper.find('.chart-wrapper').classes()).not.toContain('chart-wrapper-fullscreen')
    expect(wrapper.find('.chart-fullscreen-toggle').text()).toBe('全屏')
    expect(wrapper.find('.chart-fullscreen-toggle').classes()).not.toContain('active')
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
    const atrChip = wrapper.findAll('.chart-chip-button').find(button => button.text() === 'ATR')

    await ma60Chip.trigger('click')
    await bollChip.trigger('click')
    await atrChip.trigger('click')
    await nextTick()

    const lastMaCall = chartMocks.klineIndicatorCalls.filter(item => item.value?.name === 'MA').at(-1)
    expect(lastMaCall?.value?.calcParams).toEqual([5, 10, 60])
    expect(chartMocks.klineIndicatorCalls.some(item => item.value?.name === 'BOLL')).toBe(true)
    const lastAtrCall = chartMocks.klineIndicatorCalls.filter(item => item.value?.name === 'ATR').at(-1)
    expect(lastAtrCall?.value?.calcParams).toEqual([14])
    expect(lastAtrCall?.paneOptions?.id).toBe('atr_pane')

    await wrapper.findAll('.tab')[2].trigger('click')
    await nextTick()

    expect(wrapper.findAll('.chart-chip-button').some(button => button.text() === 'ATR')).toBe(true)
    expect(getIndicatorFiltersForView('month').some(item => item?.paneId === 'atr_pane' && item?.name === 'ATR')).toBe(true)
  })

  it('shows floating strategy badges with hover help and supports hiding them', async () => {
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
          { date: '2026-01-05', open: 10.8, close: 11.6, low: 10.4, high: 11.8, volume: 1360 }
        ],
        minuteLines: [],
        aiLevels: { resistance: 13.5, support: 11.4 },
        interval: 'day'
      }
    })

    await nextTick()

    expect(wrapper.findAll('.chart-floating-badge').some(button => button.text() === 'MA5')).toBe(true)
    expect(wrapper.find('.chart-badge-toggle').text()).toBe('隐藏小标')

    const ma5Badge = wrapper.findAll('.chart-floating-badge').find(button => button.text() === 'MA5')
    await ma5Badge.trigger('mouseenter')
    await nextTick()

    expect(wrapper.find('.chart-floating-tooltip').text()).toContain('介绍：')
    expect(wrapper.find('.chart-floating-tooltip').text()).toContain('5 日均线')
    expect(wrapper.find('.chart-floating-tooltip').text()).toContain('最近 5 个交易日的平均成本')

    await wrapper.find('.chart-badge-toggle').trigger('click')
    await nextTick()

    expect(wrapper.findAll('.chart-floating-badge')).toHaveLength(0)
    expect(wrapper.find('.chart-floating-tooltip').exists()).toBe(false)
    expect(wrapper.find('.chart-badge-toggle').text()).toBe('显示小标')
  })

  it('renders line color mapping for multi-line indicators like RSI and KDJ', async () => {
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
          { date: '2026-01-10', open: 12.8, close: 13.1, low: 12.4, high: 13.3, volume: 1750 }
        ],
        minuteLines: [],
        interval: 'day'
      }
    })

    await nextTick()

    const rsiChip = wrapper.findAll('.chart-chip-button').find(button => button.text() === 'RSI')
    const kdjChip = wrapper.findAll('.chart-chip-button').find(button => button.text() === 'KDJ')

    await rsiChip.trigger('click')
    await kdjChip.trigger('click')
    await nextTick()

    const kdjBadge = wrapper.findAll('.chart-floating-badge').find(button => button.text() === 'KDJ')
    await kdjBadge.trigger('mouseenter')
    await nextTick()

    const tooltipText = wrapper.find('.chart-floating-tooltip').text()
    expect(tooltipText).toContain('颜色对照')
    expect(tooltipText).toContain('K 线')
    expect(tooltipText).toContain('D 线')
    expect(tooltipText).toContain('J 线')

    const lineLegendItems = wrapper.findAll('.chart-line-legend-item')
    expect(lineLegendItems).toHaveLength(3)
    expect(lineLegendItems[0].text()).toContain('K 线')
    expect(lineLegendItems[1].text()).toContain('D 线')
    expect(lineLegendItems[2].text()).toContain('J 线')

    const swatchStyles = wrapper.findAll('.chart-line-legend-swatch').map(node => node.attributes('style'))
    expect(swatchStyles[0]).toContain('rgb(255, 0, 92)')
    expect(swatchStyles[1]).toContain('rgb(57, 255, 20)')
    expect(swatchStyles[2]).toContain('rgb(0, 229, 255)')
    const kdjIndicators = chartMocks.klineIndicatorCalls.filter(item => /^KDJ_[KDJ]_VISUAL$/.test(item.value?.name ?? ''))
    expect(new Set(kdjIndicators.map(item => item.value?.name))).toEqual(new Set(['KDJ_K_VISUAL', 'KDJ_D_VISUAL', 'KDJ_J_VISUAL']))
    expect(kdjIndicators.every(item => JSON.stringify(item.value?.calcParams) === JSON.stringify([9, 3, 3]))).toBe(true)
  })

  it('renders MA cross markers on day view without duplicate marker points', async () => {
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
          { date: '2026-02-01', open: 10.0, close: 10.0, low: 9.8, high: 10.2, volume: 1000 },
          { date: '2026-02-02', open: 9.9, close: 9.9, low: 9.7, high: 10.1, volume: 990 },
          { date: '2026-02-03', open: 9.8, close: 9.8, low: 9.6, high: 10.0, volume: 980 },
          { date: '2026-02-04', open: 9.7, close: 9.7, low: 9.5, high: 9.9, volume: 970 },
          { date: '2026-02-05', open: 9.6, close: 9.6, low: 9.4, high: 9.8, volume: 960 },
          { date: '2026-02-06', open: 9.5, close: 9.5, low: 9.3, high: 9.7, volume: 950 },
          { date: '2026-02-07', open: 9.4, close: 9.4, low: 9.2, high: 9.6, volume: 940 },
          { date: '2026-02-08', open: 9.3, close: 9.3, low: 9.1, high: 9.5, volume: 930 },
          { date: '2026-02-09', open: 9.2, close: 9.2, low: 9.0, high: 9.4, volume: 920 },
          { date: '2026-02-10', open: 9.1, close: 9.1, low: 8.9, high: 9.3, volume: 910 },
          { date: '2026-02-11', open: 10.8, close: 10.8, low: 10.5, high: 11.0, volume: 1800 },
          { date: '2026-02-12', open: 11.2, close: 11.2, low: 10.9, high: 11.4, volume: 1900 },
          { date: '2026-02-13', open: 11.6, close: 11.6, low: 11.3, high: 11.8, volume: 2000 },
          { date: '2026-02-14', open: 11.9, close: 11.9, low: 11.6, high: 12.1, volume: 2050 },
          { date: '2026-02-15', open: 12.1, close: 12.1, low: 11.8, high: 12.3, volume: 2100 },
          { date: '2026-02-16', open: 8.9, close: 8.9, low: 8.6, high: 9.1, volume: 2200 },
          { date: '2026-02-17', open: 8.7, close: 8.7, low: 8.4, high: 8.9, volume: 2250 },
          { date: '2026-02-18', open: 8.5, close: 8.5, low: 8.2, high: 8.7, volume: 2300 },
          { date: '2026-02-19', open: 8.3, close: 8.3, low: 8.0, high: 8.5, volume: 2350 },
          { date: '2026-02-20', open: 8.1, close: 8.1, low: 7.8, high: 8.3, volume: 2400 }
        ],
        minuteLines: [],
        interval: 'day'
      }
    })

    await nextTick()

    const maCrossChip = wrapper.findAll('.chart-chip-button').find(button => button.text() === 'MA金叉/死叉')
    await maCrossChip.trigger('click')
    await nextTick()

    const markerOverlays = chartMocks.klineOverlayCalls.filter(item => item?.groupId === 'day-markers' && item?.name === 'simpleAnnotation')
    const uniqueMarkerOverlays = Array.from(new Map(
      markerOverlays.map(item => [`${item.extendData}-${item.points?.[0]?.timestamp}`, item])
    ).values())

    expect(uniqueMarkerOverlays.length).toBeGreaterThanOrEqual(2)
    expect(uniqueMarkerOverlays.some(item => item.extendData === 'MA金叉')).toBe(true)
    expect(uniqueMarkerOverlays.some(item => item.extendData === 'MA死叉')).toBe(true)
    expect(new Set(markerOverlays.map(item => `${item.extendData}-${item.points?.[0]?.timestamp}`)).size).toBe(uniqueMarkerOverlays.length)
    expect(uniqueMarkerOverlays.find(item => item.extendData === 'MA金叉')?.styles?.line?.color).toBe('#22c55e')
    expect(uniqueMarkerOverlays.find(item => item.extendData === 'MA死叉')?.styles?.line?.color).toBe('#ef4444')
    expect(wrapper.findAll('.chart-floating-badge').some(button => button.text() === 'MA金叉/死叉')).toBe(true)
  })

  it('renders TD sequential markers from 6 to 9 with weak and strong emphasis', async () => {
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

    const closes = [10.0, 10.1, 10.2, 10.3, 10.5, 10.7, 10.9, 11.1, 11.3, 11.5, 11.7, 11.9, 12.1, 11.0, 10.5, 10.0, 9.5, 9.0, 8.5, 8.0, 7.5, 7.0]
    const kLines = closes.map((close, index) => ({
      date: `2026-03-${String(index + 1).padStart(2, '0')}`,
      open: close,
      close,
      low: Number((close - 0.2).toFixed(2)),
      high: Number((close + 0.2).toFixed(2)),
      volume: 1000 + index * 50
    }))

    const wrapper = mount(StockCharts, {
      props: {
        kLines,
        minuteLines: [],
        interval: 'day'
      }
    })

    await nextTick()

    const tdChip = wrapper.findAll('.chart-chip-button').find(button => button.text() === 'TD九转')
    expect(tdChip).toBeTruthy()

    await tdChip.trigger('click')
    await nextTick()

    const markerOverlays = chartMocks.klineOverlayCalls.filter(item => item?.groupId === 'day-markers' && item?.name === 'simpleAnnotation')
    const uniqueTdOverlays = Array.from(new Map(
      markerOverlays
        .filter(item => /^TD[买卖][1-9]$/.test(item.extendData ?? ''))
        .map(item => [`${item.extendData}-${item.points?.[0]?.timestamp}`, item])
    ).values())

    expect(uniqueTdOverlays).toHaveLength(8)
    expect(uniqueTdOverlays.some(item => item.extendData === 'TD卖6')).toBe(true)
    expect(uniqueTdOverlays.some(item => item.extendData === 'TD卖7')).toBe(true)
    expect(uniqueTdOverlays.some(item => item.extendData === 'TD卖8')).toBe(true)
    expect(uniqueTdOverlays.some(item => item.extendData === 'TD卖9')).toBe(true)
    expect(uniqueTdOverlays.some(item => item.extendData === 'TD买6')).toBe(true)
    expect(uniqueTdOverlays.some(item => item.extendData === 'TD买7')).toBe(true)
    expect(uniqueTdOverlays.some(item => item.extendData === 'TD买8')).toBe(true)
    expect(uniqueTdOverlays.some(item => item.extendData === 'TD买9')).toBe(true)
    expect(uniqueTdOverlays.some(item => item.extendData === 'TD卖1')).toBe(false)
    expect(uniqueTdOverlays.some(item => item.extendData === 'TD买1')).toBe(false)
    expect(uniqueTdOverlays.find(item => item.extendData === 'TD卖6')?.styles?.line?.color).toBe('#fca5a5')
    expect(uniqueTdOverlays.find(item => item.extendData === 'TD卖8')?.styles?.line?.color).toBe('#ef4444')
    expect(uniqueTdOverlays.find(item => item.extendData === 'TD买6')?.styles?.line?.color).toBe('#86efac')
    expect(uniqueTdOverlays.find(item => item.extendData === 'TD买8')?.styles?.line?.color).toBe('#22c55e')
    expect(uniqueTdOverlays.find(item => item.extendData === 'TD卖6')?.styles?.text?.size).toBe(10)
    expect(uniqueTdOverlays.find(item => item.extendData === 'TD卖9')?.styles?.text?.size).toBe(12)
    expect(wrapper.findAll('.chart-floating-badge').some(button => button.text() === 'TD九转')).toBe(true)

    await wrapper.findAll('.tab')[2].trigger('click')
    await nextTick()

    expect(wrapper.findAll('.chart-chip-button').some(button => button.text() === 'TD九转')).toBe(false)
  })

  it('renders MACD cross markers on day view with deterministic buy and sell signals', async () => {
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

    const closes = [
      20.0, 19.7, 19.4, 19.1, 18.8, 18.5, 18.2, 17.9, 17.6, 17.3,
      17.0, 16.7, 16.4, 16.1, 15.8, 15.5, 15.2, 14.9, 14.6, 14.3,
      14.0, 13.7, 13.4, 13.1, 12.8, 12.5, 12.2, 11.9, 11.6, 11.3,
      11.0, 10.7, 10.4, 10.1, 9.8, 10.3, 10.8, 11.3, 11.8, 12.3,
      12.8, 13.3, 13.8, 14.3, 14.8, 15.3, 15.8, 16.3, 16.8, 17.3,
      16.8, 16.2, 15.6, 15.0, 14.4, 13.8, 13.2, 12.6, 12.0, 11.4,
      10.8, 10.2, 9.6, 9.0
    ]
    const kLines = closes.map((close, index) => ({
      date: `2026-04-${String(index + 1).padStart(2, '0')}`,
      open: close,
      close,
      low: Number((close - 0.25).toFixed(2)),
      high: Number((close + 0.25).toFixed(2)),
      volume: 1200 + index * 60
    }))

    const wrapper = mount(StockCharts, {
      props: {
        kLines,
        minuteLines: [],
        interval: 'day'
      }
    })

    await nextTick()

    const macdCrossChip = wrapper.findAll('.chart-chip-button').find(button => button.text() === 'MACD金叉/死叉')
    expect(macdCrossChip).toBeTruthy()

    await macdCrossChip.trigger('click')
    await nextTick()

    const markerOverlays = chartMocks.klineOverlayCalls.filter(item => item?.groupId === 'day-markers' && item?.name === 'simpleAnnotation')
    const uniqueMacdOverlays = Array.from(new Map(
      markerOverlays
        .filter(item => item.extendData === 'MACD金叉' || item.extendData === 'MACD死叉')
        .map(item => [`${item.extendData}-${item.points?.[0]?.timestamp}`, item])
    ).values())

    expect(uniqueMacdOverlays).toHaveLength(2)
    expect(uniqueMacdOverlays.find(item => item.extendData === 'MACD金叉')?.styles?.line?.color).toBe('#22c55e')
    expect(uniqueMacdOverlays.find(item => item.extendData === 'MACD死叉')?.styles?.line?.color).toBe('#ef4444')
    expect(wrapper.findAll('.chart-floating-badge').some(button => button.text() === 'MACD金叉/死叉')).toBe(true)

    await wrapper.findAll('.tab')[3].trigger('click')
    await nextTick()

    expect(wrapper.findAll('.chart-chip-button').some(button => button.text() === 'MACD金叉/死叉')).toBe(false)
  })

  it('renders remaining day-view Phase C markers for KDJ cross, breakout, and gaps', async () => {
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

    const kLines = [
      { date: '2026-05-01', open: 10.0, close: 10.0, low: 9.85, high: 10.15, volume: 1000 },
      { date: '2026-05-02', open: 9.9, close: 9.9, low: 9.75, high: 10.05, volume: 990 },
      { date: '2026-05-03', open: 9.8, close: 9.8, low: 9.65, high: 9.95, volume: 980 },
      { date: '2026-05-04', open: 9.7, close: 9.7, low: 9.55, high: 9.85, volume: 970 },
      { date: '2026-05-05', open: 9.6, close: 9.6, low: 9.45, high: 9.75, volume: 960 },
      { date: '2026-05-06', open: 9.5, close: 9.5, low: 9.35, high: 9.65, volume: 950 },
      { date: '2026-05-07', open: 9.4, close: 9.4, low: 9.25, high: 9.55, volume: 940 },
      { date: '2026-05-08', open: 9.3, close: 9.3, low: 9.15, high: 9.45, volume: 930 },
      { date: '2026-05-09', open: 9.2, close: 9.2, low: 9.05, high: 9.35, volume: 920 },
      { date: '2026-05-10', open: 9.5, close: 9.5, low: 9.35, high: 9.65, volume: 930 },
      { date: '2026-05-11', open: 9.9, close: 9.9, low: 9.75, high: 10.05, volume: 940 },
      { date: '2026-05-12', open: 10.3, close: 10.3, low: 10.15, high: 10.45, volume: 950 },
      { date: '2026-05-13', open: 10.7, close: 10.7, low: 10.55, high: 10.85, volume: 960 },
      { date: '2026-05-14', open: 11.1, close: 11.1, low: 10.95, high: 11.25, volume: 970 },
      { date: '2026-05-15', open: 11.3, close: 11.3, low: 11.15, high: 11.45, volume: 980 },
      { date: '2026-05-16', open: 10.9, close: 10.9, low: 10.75, high: 11.05, volume: 990 },
      { date: '2026-05-17', open: 10.5, close: 10.5, low: 10.35, high: 10.65, volume: 1000 },
      { date: '2026-05-18', open: 10.1, close: 10.1, low: 9.95, high: 10.25, volume: 1010 },
      { date: '2026-05-19', open: 9.7, close: 9.7, low: 9.55, high: 9.85, volume: 1020 },
      { date: '2026-05-20', open: 9.3, close: 9.3, low: 9.15, high: 9.45, volume: 1030 },
      { date: '2026-05-21', open: 11.4, close: 11.6, low: 11.35, high: 11.8, volume: 2600 },
      { date: '2026-05-22', open: 11.6, close: 11.4, low: 11.2, high: 11.75, volume: 1400 },
      { date: '2026-05-23', open: 11.5, close: 11.0, low: 10.95, high: 12.0, volume: 2200 }
    ]

    const wrapper = mount(StockCharts, {
      props: {
        kLines,
        minuteLines: [],
        interval: 'day'
      }
    })

    await nextTick()

    const kdjCrossChip = wrapper.findAll('.chart-chip-button').find(button => button.text() === 'KDJ金叉/死叉')
    const breakoutChip = wrapper.findAll('.chart-chip-button').find(button => button.text() === '放量突破/假突破')
    const gapChip = wrapper.findAll('.chart-chip-button').find(button => button.text() === '缺口')

    expect(kdjCrossChip).toBeTruthy()
    expect(breakoutChip).toBeTruthy()
    expect(gapChip).toBeTruthy()

    await kdjCrossChip.trigger('click')
    await breakoutChip.trigger('click')
    await gapChip.trigger('click')
    await nextTick()

    const markerOverlays = chartMocks.klineOverlayCalls.filter(item => item?.groupId === 'day-markers' && item?.name === 'simpleAnnotation')
    const uniqueDayOverlays = Array.from(new Map(
      markerOverlays.map(item => [`${item.extendData}-${item.points?.[0]?.timestamp}`, item])
    ).values())

    expect(uniqueDayOverlays.some(item => item.extendData === 'KDJ金叉')).toBe(true)
    expect(uniqueDayOverlays.some(item => item.extendData === 'KDJ死叉')).toBe(true)
    expect(uniqueDayOverlays.some(item => item.extendData === '放量突破')).toBe(true)
    expect(uniqueDayOverlays.some(item => item.extendData === '假突破')).toBe(true)
    expect(uniqueDayOverlays.some(item => item.extendData === '高开缺口')).toBe(true)
    expect(uniqueDayOverlays.some(item => item.extendData === '回补缺口')).toBe(true)
    expect(wrapper.findAll('.chart-floating-badge').some(button => button.text() === 'KDJ金叉/死叉')).toBe(true)
    expect(wrapper.findAll('.chart-floating-badge').some(button => button.text() === '放量突破/假突破')).toBe(true)
    expect(wrapper.findAll('.chart-floating-badge').some(button => button.text() === '缺口')).toBe(true)

    await wrapper.findAll('.tab')[2].trigger('click')
    await nextTick()

    expect(wrapper.findAll('.chart-chip-button').some(button => button.text() === 'KDJ金叉/死叉')).toBe(false)
    expect(wrapper.findAll('.chart-chip-button').some(button => button.text() === '放量突破/假突破')).toBe(false)
    expect(wrapper.findAll('.chart-chip-button').some(button => button.text() === '缺口')).toBe(false)
  })

  it('renders remaining minute-view Phase C markers for divergence and VWAP strength', async () => {
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
      { date: '2026-06-01', time: '09:30:00', price: 10.0, volume: 200 },
      { date: '2026-06-01', time: '09:31:00', price: 9.9, volume: 380 },
      { date: '2026-06-01', time: '09:32:00', price: 9.8, volume: 540 },
      { date: '2026-06-01', time: '09:33:00', price: 9.85, volume: 690 },
      { date: '2026-06-01', time: '09:34:00', price: 10.1, volume: 830 },
      { date: '2026-06-01', time: '09:35:00', price: 10.2, volume: 960 },
      { date: '2026-06-01', time: '09:36:00', price: 10.3, volume: 1080 },
      { date: '2026-06-01', time: '09:37:00', price: 10.4, volume: 1190 },
      { date: '2026-06-01', time: '09:38:00', price: 10.5, volume: 1290 },
      { date: '2026-06-01', time: '09:39:00', price: 10.15, volume: 1410 },
      { date: '2026-06-01', time: '09:40:00', price: 9.9, volume: 1550 },
      { date: '2026-06-01', time: '09:41:00', price: 9.65, volume: 1710 },
      { date: '2026-06-01', time: '09:42:00', price: 9.4, volume: 1890 },
      { date: '2026-06-01', time: '09:43:00', price: 9.2, volume: 2110 }
    ]

    const wrapper = mount(StockCharts, {
      props: {
        kLines: [],
        minuteLines,
        basePrice: 10.0,
        interval: 'day'
      }
    })

    await nextTick()

    await wrapper.findAll('.tab')[0].trigger('click')
    await nextTick()

    const divergenceChip = wrapper.findAll('.chart-chip-button').find(button => button.text() === '量价背离')
    const vwapStrengthChip = wrapper.findAll('.chart-chip-button').find(button => button.text() === 'VWAP强弱')
    expect(divergenceChip).toBeTruthy()
    expect(vwapStrengthChip).toBeTruthy()

    await divergenceChip.trigger('click')
    await vwapStrengthChip.trigger('click')
    await nextTick()

    const markerOverlays = chartMocks.minuteOverlayCalls.filter(item => item?.groupId === 'minute-markers' && item?.name === 'simpleAnnotation')
    const uniqueMinuteOverlays = Array.from(new Map(
      markerOverlays.map(item => [`${item.extendData}-${item.points?.[0]?.timestamp}`, item])
    ).values())

    expect(uniqueMinuteOverlays.some(item => item.extendData === '顶背离')).toBe(true)
    expect(uniqueMinuteOverlays.some(item => item.extendData === '底背离')).toBe(true)
    expect(uniqueMinuteOverlays.some(item => item.extendData === 'VWAP企稳')).toBe(true)
    expect(uniqueMinuteOverlays.some(item => item.extendData === 'VWAP转弱')).toBe(true)
    expect(wrapper.findAll('.chart-floating-badge').some(button => button.text() === '量价背离')).toBe(true)
    expect(wrapper.findAll('.chart-floating-badge').some(button => button.text() === 'VWAP强弱')).toBe(true)

    await wrapper.findAll('.tab')[1].trigger('click')
    await nextTick()

    expect(wrapper.findAll('.chart-chip-button').some(button => button.text() === '量价背离')).toBe(false)
    expect(wrapper.findAll('.chart-chip-button').some(button => button.text() === 'VWAP强弱')).toBe(false)
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
