import { describe, expect, it } from 'vitest'
import {
  formatNorthboundUnavailableText,
  getNorthboundStatusKind,
  isNorthboundStatusAvailable
} from './northboundStatus.js'

describe('northboundStatus', () => {
  it.each([
    [{ status: 'unavailable', isStale: false }, 'unavailable'],
    [{ status: 'unavailable', isStale: true }, 'unavailable'],
    [{ status: 'closed', isStale: false }, 'closed'],
    [{ status: 'closed', isStale: true }, 'closed'],
    [{ status: 'stale', isStale: false }, 'stale'],
    [{ status: 'stale', isStale: true }, 'stale'],
    [{ status: 'ok', isStale: false }, 'available'],
    [{ status: 'ok', isStale: true }, 'available'],
    [{ status: 'live', isStale: false }, 'available'],
    [{ status: 'live', isStale: true }, 'available'],
    [{ isStale: true }, 'stale'],
    [{ isStale: false }, 'available']
  ])('resolves %o to %s with explicit status before stale fallback', (flow, expectedKind) => {
    expect(getNorthboundStatusKind(flow)).toBe(expectedKind)
  })

  it('treats explicit ok and live as available even when isStale is true', () => {
    expect(isNorthboundStatusAvailable({ status: 'ok', isStale: true })).toBe(true)
    expect(isNorthboundStatusAvailable({ status: 'live', isStale: true })).toBe(true)
  })

  it.each([
    [{ status: 'unavailable', isStale: true }, '不可用'],
    [{ status: 'closed', isStale: true }, '休市'],
    [{ status: 'stale', isStale: false }, '数据待更新'],
    [{ status: 'live', isStale: true }, '']
  ])('formats %o as %s', (flow, expectedText) => {
    expect(formatNorthboundUnavailableText(flow)).toBe(expectedText)
  })
})