import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import StockCharts from './StockCharts.vue'

const chartMocks = vi.hoisted(() => ({
  klineSetDataMock: vi.fn(),
  volumeSetDataMock: vi.fn(),
  ma5SetDataMock: vi.fn(),
  ma10SetDataMock: vi.fn(),
  klineResistanceSetDataMock: vi.fn(),
  klineSupportSetDataMock: vi.fn(),
  minuteSetDataMock: vi.fn(),
  minuteVolumeSetDataMock: vi.fn(),
  minuteResistanceSetDataMock: vi.fn(),
  minuteSupportSetDataMock: vi.fn(),
  minuteCreatePriceLineMock: vi.fn(),
  minuteRemovePriceLineMock: vi.fn(),
  subscribeCrosshairMoveMock: vi.fn(),
  applyOptionsMock: vi.fn(),
  fitContentMock: vi.fn(),
  removeMock: vi.fn()
}))

const resizeObserverState = vi.hoisted(() => ({
  callback: null
}))

vi.mock('lightweight-charts', () => {
  let createCount = 0
  let klineLineSeriesCount = 0
  let minuteLineSeriesCount = 0

  const klineChart = {
    addSeries: vi.fn(seriesType => {
      if (seriesType === 'CandlestickSeries') {
        return { setData: chartMocks.klineSetDataMock }
      }
      if (seriesType === 'HistogramSeries') {
        return { setData: chartMocks.volumeSetDataMock }
      }
      if (seriesType === 'LineSeries') {
        klineLineSeriesCount += 1
        if (klineLineSeriesCount === 1) return { setData: chartMocks.ma5SetDataMock }
        if (klineLineSeriesCount === 2) return { setData: chartMocks.ma10SetDataMock }
        if (klineLineSeriesCount === 3) return { setData: chartMocks.klineResistanceSetDataMock }
        return { setData: chartMocks.klineSupportSetDataMock }
      }
      return { setData: vi.fn() }
    }),
    priceScale: vi.fn(() => ({ applyOptions: vi.fn() })),
    subscribeCrosshairMove: chartMocks.subscribeCrosshairMoveMock,
    applyOptions: chartMocks.applyOptionsMock,
    timeScale: vi.fn(() => ({ fitContent: chartMocks.fitContentMock })),
    remove: chartMocks.removeMock
  }

  const minuteChart = {
    addSeries: vi.fn(seriesType => {
      if (seriesType === 'AreaSeries') {
        return {
          setData: chartMocks.minuteSetDataMock,
          createPriceLine: chartMocks.minuteCreatePriceLineMock,
          removePriceLine: chartMocks.minuteRemovePriceLineMock
        }
      }
      if (seriesType === 'HistogramSeries') {
        return { setData: chartMocks.minuteVolumeSetDataMock }
      }
      if (seriesType === 'LineSeries') {
        minuteLineSeriesCount += 1
        return { setData: minuteLineSeriesCount === 1 ? chartMocks.minuteResistanceSetDataMock : chartMocks.minuteSupportSetDataMock }
      }
      return { setData: vi.fn() }
    }),
    priceScale: vi.fn(() => ({ applyOptions: vi.fn() })),
    subscribeCrosshairMove: chartMocks.subscribeCrosshairMoveMock,
    applyOptions: chartMocks.applyOptionsMock,
    timeScale: vi.fn(() => ({ fitContent: chartMocks.fitContentMock })),
    remove: chartMocks.removeMock
  }

  return {
    createChart: vi.fn(() => {
      createCount += 1
      if (createCount % 2 === 1) {
        klineLineSeriesCount = 0
      } else {
        minuteLineSeriesCount = 0
      }
      return createCount % 2 === 1 ? klineChart : minuteChart
    }),
    ColorType: { Solid: 'solid' },
    CandlestickSeries: 'CandlestickSeries',
    HistogramSeries: 'HistogramSeries',
    AreaSeries: 'AreaSeries',
    LineSeries: 'LineSeries'
  }
})

describe('StockCharts', () => {
  beforeEach(() => {
    chartMocks.klineSetDataMock.mockClear()
    chartMocks.volumeSetDataMock.mockClear()
    chartMocks.ma5SetDataMock.mockClear()
    chartMocks.ma10SetDataMock.mockClear()
    chartMocks.klineResistanceSetDataMock.mockClear()
    chartMocks.klineSupportSetDataMock.mockClear()
    chartMocks.minuteSetDataMock.mockClear()
    chartMocks.minuteVolumeSetDataMock.mockClear()
    chartMocks.minuteResistanceSetDataMock.mockClear()
    chartMocks.minuteSupportSetDataMock.mockClear()
    chartMocks.minuteCreatePriceLineMock.mockClear()
    chartMocks.minuteRemovePriceLineMock.mockClear()
    chartMocks.subscribeCrosshairMoveMock.mockClear()
    chartMocks.applyOptionsMock.mockClear()
    chartMocks.fitContentMock.mockClear()
    chartMocks.removeMock.mockClear()
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

    mount(StockCharts, {
      props: {
        kLines: [],
        minuteLines,
        aiLevels: { resistance: 32, support: 30.5 },
        interval: 'day'
      }
    })

    await nextTick()

    expect(chartMocks.minuteSetDataMock).toHaveBeenCalled()
    const minuteSeriesData = chartMocks.minuteSetDataMock.mock.calls.at(-1)?.[0] ?? []
    expect(minuteSeriesData.length).toBe(2)
    expect(minuteSeriesData[0].value).toBe(31.1)
    expect(minuteSeriesData[0].time).toBeLessThan(minuteSeriesData[1].time)

    expect(chartMocks.minuteVolumeSetDataMock).toHaveBeenCalled()
    const minuteVolumeData = chartMocks.minuteVolumeSetDataMock.mock.calls.at(-1)?.[0] ?? []
    expect(minuteVolumeData[0].value).toBe(1800)
    expect(minuteVolumeData[1].value).toBe(2300)
    expect(chartMocks.minuteCreatePriceLineMock).toHaveBeenCalled()

    const minuteResistanceData = chartMocks.minuteResistanceSetDataMock.mock.calls.at(-1)?.[0] ?? []
    const minuteSupportData = chartMocks.minuteSupportSetDataMock.mock.calls.at(-1)?.[0] ?? []
    expect(minuteResistanceData).toHaveLength(2)
    expect(minuteSupportData).toHaveLength(2)
    expect(minuteResistanceData[0].value).toBe(32)
    expect(minuteSupportData[0].value).toBe(30.5)
    expect(minuteResistanceData[0].time).toBeLessThan(minuteResistanceData[1].time)
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
    ]

    mount(StockCharts, {
      props: {
        kLines,
        minuteLines: [],
        aiLevels: { resistance: 13.5, support: 11.4 },
        interval: 'day'
      }
    })

    await nextTick()

    expect(chartMocks.klineSetDataMock).toHaveBeenCalled()
    expect(chartMocks.volumeSetDataMock).toHaveBeenCalled()
    expect(chartMocks.ma5SetDataMock).toHaveBeenCalled()
    expect(chartMocks.ma10SetDataMock).toHaveBeenCalled()

    const klineData = chartMocks.klineSetDataMock.mock.calls.at(-1)?.[0] ?? []
    expect(klineData[0].time).toEqual({ year: 2026, month: 1, day: 1 })
    expect(klineData[0].open).toBe(8)
    expect(klineData[1].close).toBe(11)

    const volumeData = chartMocks.volumeSetDataMock.mock.calls.at(-1)?.[0] ?? []
    expect(volumeData[0].value).toBe(800)
    expect(volumeData.at(-1).value).toBe(1750)

    const ma5Data = chartMocks.ma5SetDataMock.mock.calls.at(-1)?.[0] ?? []
    const ma10Data = chartMocks.ma10SetDataMock.mock.calls.at(-1)?.[0] ?? []
    const resistanceData = chartMocks.klineResistanceSetDataMock.mock.calls.at(-1)?.[0] ?? []
    const supportData = chartMocks.klineSupportSetDataMock.mock.calls.at(-1)?.[0] ?? []
    expect(ma5Data.length).toBe(6)
    expect(ma10Data.length).toBe(1)
    expect(ma10Data[0].time).toEqual({ year: 2026, month: 1, day: 10 })
    expect(resistanceData).toHaveLength(2)
    expect(supportData).toHaveLength(2)
    expect(resistanceData[0].time).toEqual({ year: 2026, month: 1, day: 1 })
    expect(resistanceData[1].time).toEqual({ year: 2026, month: 1, day: 10 })
    expect(resistanceData[0].value).toBe(13.5)
    expect(supportData[0].value).toBe(11.4)
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
    chartMocks.applyOptionsMock.mockClear()

    const charts = wrapper.findAll('.chart')
    Object.defineProperty(charts[0].element, 'clientWidth', { configurable: true, get: () => 720 })
    Object.defineProperty(charts[0].element, 'clientHeight', { configurable: true, get: () => 360 })
    Object.defineProperty(charts[1].element, 'clientWidth', { configurable: true, get: () => 720 })
    Object.defineProperty(charts[1].element, 'clientHeight', { configurable: true, get: () => 260 })

    resizeObserverState.callback?.([{ target: charts[0].element, contentRect: { width: 720, height: 360 } }])
    await new Promise(resolve => setTimeout(resolve, 0))

    expect(chartMocks.applyOptionsMock).toHaveBeenCalled()
    expect(chartMocks.applyOptionsMock.mock.calls.some(([options]) => options.width === 720 && options.height === 360)).toBe(true)
  })
})
