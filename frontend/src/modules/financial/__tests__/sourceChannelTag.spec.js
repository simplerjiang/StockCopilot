import { describe, it, expect } from 'vitest'
import {
  getSourceChannelTag,
  sourceChannelTagStyle,
  SOURCE_CHANNEL_TAGS,
  SOURCE_CHANNEL_FALLBACK_TAG
} from '../sourceChannelTag.js'

describe('sourceChannelTag util', () => {
  it('exposes 4 known channels', () => {
    expect(Object.keys(SOURCE_CHANNEL_TAGS).sort()).toEqual([
      'datacenter',
      'emweb',
      'pdf',
      'ths'
    ])
  })

  it('maps emweb → blue', () => {
    const tag = getSourceChannelTag('emweb')
    expect(tag.tone).toBe('blue')
    expect(tag.label).toBe('EM 网页')
    expect(tag.color).toBe('#1d4ed8')
  })

  it('maps datacenter → teal', () => {
    const tag = getSourceChannelTag('datacenter')
    expect(tag.tone).toBe('teal')
    expect(tag.label).toBe('数据中心')
  })

  it('maps ths → purple', () => {
    const tag = getSourceChannelTag('ths')
    expect(tag.tone).toBe('purple')
    expect(tag.label).toBe('同花顺')
  })

  it('maps pdf → orange', () => {
    const tag = getSourceChannelTag('pdf')
    expect(tag.tone).toBe('orange')
    expect(tag.label).toBe('PDF')
  })

  it('is case-insensitive', () => {
    expect(getSourceChannelTag('EMWeb').tone).toBe('blue')
    expect(getSourceChannelTag('  PDF  ').tone).toBe('orange')
  })

  it('falls back to gray for null / undefined / empty', () => {
    expect(getSourceChannelTag(null)).toEqual(SOURCE_CHANNEL_FALLBACK_TAG)
    expect(getSourceChannelTag(undefined)).toEqual(SOURCE_CHANNEL_FALLBACK_TAG)
    expect(getSourceChannelTag('')).toEqual(SOURCE_CHANNEL_FALLBACK_TAG)
    expect(getSourceChannelTag('   ')).toEqual(SOURCE_CHANNEL_FALLBACK_TAG)
  })

  it('falls back to gray for unknown channel but preserves label', () => {
    const tag = getSourceChannelTag('mystery-source')
    expect(tag.tone).toBe('gray')
    expect(tag.color).toBe(SOURCE_CHANNEL_FALLBACK_TAG.color)
    expect(tag.label).toBe('mystery-source')
  })

  it('sourceChannelTagStyle joins color/bg/border into inline css', () => {
    const tag = getSourceChannelTag('emweb')
    const css = sourceChannelTagStyle(tag)
    expect(css).toContain('color:#1d4ed8')
    expect(css).toContain('background:rgba(29, 78, 216, 0.10)')
    expect(css).toContain('border:1px solid rgba(29, 78, 216, 0.25)')
  })

  it('sourceChannelTagStyle returns empty for falsy tag', () => {
    expect(sourceChannelTagStyle(null)).toBe('')
    expect(sourceChannelTagStyle(undefined)).toBe('')
  })
})
