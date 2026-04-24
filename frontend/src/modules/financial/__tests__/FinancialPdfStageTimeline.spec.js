/**
 * @vitest-environment jsdom
 */
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'

import FinancialPdfStageTimeline from '../FinancialPdfStageTimeline.vue'

const STAGES = ['download', 'extract', 'vote', 'parse', 'persist']

const makeLog = (stage, status, durationMs, message = null, details = null) => ({
  stage,
  status,
  durationMs,
  message,
  details,
  occurredAt: '2026-04-22T10:00:00Z'
})

describe('FinancialPdfStageTimeline', () => {
  it('5 阶段全成功：每个节点都显示成功徽标且总耗时正确，无 failed 节点', () => {
    const stageLogs = [
      makeLog('download', 'success', 120),
      makeLog('extract', 'success', 300),
      makeLog('vote', 'success', 80),
      makeLog('parse', 'success', 450),
      makeLog('persist', 'success', 50)
    ]
    const wrapper = mount(FinancialPdfStageTimeline, { props: { stageLogs } })

    for (const stage of STAGES) {
      const item = wrapper.find(`[data-testid="fc-pdf-stage-item-${stage}"]`)
      expect(item.exists()).toBe(true)
      expect(item.attributes('data-status')).toBe('success')
      expect(wrapper.find(`[data-testid="fc-pdf-stage-badge-${stage}"]`).text()).toBe('成功')
    }

    expect(wrapper.find('[data-testid="fc-pdf-stage-success-count"]').text()).toBe('5')
    // 120+300+80+450+50 = 1000ms => 不 >1000，按 ms 显示
    expect(wrapper.find('[data-testid="fc-pdf-stage-total-duration"]').text()).toBe('1000ms')
    expect(wrapper.find('[data-testid="fc-pdf-stage-last-failed"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="fc-pdf-stage-empty"]').exists()).toBe(false)
  })

  it('中段失败 + 后续 skipped：extract 红色显示 message，后续节点为 skipped', () => {
    const stageLogs = [
      makeLog('download', 'success', 100),
      makeLog('extract', 'failed', 250, 'pdfplumber 抛出 PageCountError: missing /Pages'),
      makeLog('vote', 'skipped', 0),
      makeLog('parse', 'skipped', 0),
      makeLog('persist', 'skipped', 0)
    ]
    const wrapper = mount(FinancialPdfStageTimeline, { props: { stageLogs } })

    expect(
      wrapper.find('[data-testid="fc-pdf-stage-item-download"]').attributes('data-status')
    ).toBe('success')
    const extract = wrapper.find('[data-testid="fc-pdf-stage-item-extract"]')
    expect(extract.attributes('data-status')).toBe('failed')
    expect(extract.classes()).toContain('is-failed')

    const message = wrapper.find('[data-testid="fc-pdf-stage-message-extract"]')
    expect(message.exists()).toBe(true)
    expect(message.text()).toContain('PageCountError')

    for (const stage of ['vote', 'parse', 'persist']) {
      expect(
        wrapper.find(`[data-testid="fc-pdf-stage-item-${stage}"]`).attributes('data-status')
      ).toBe('skipped')
      expect(wrapper.find(`[data-testid="fc-pdf-stage-badge-${stage}"]`).text()).toBe('跳过')
    }

    expect(wrapper.find('[data-testid="fc-pdf-stage-success-count"]').text()).toBe('1')
    expect(wrapper.find('[data-testid="fc-pdf-stage-last-failed"]').text()).toContain('文本提取')
  })

  it('首段失败 + 后续阶段缺失：download 红色，其余 4 个为 pending', () => {
    const stageLogs = [makeLog('download', 'failed', 80, '下载超时')]
    const wrapper = mount(FinancialPdfStageTimeline, { props: { stageLogs } })

    const download = wrapper.find('[data-testid="fc-pdf-stage-item-download"]')
    expect(download.attributes('data-status')).toBe('failed')
    expect(download.classes()).toContain('is-failed')
    expect(wrapper.find('[data-testid="fc-pdf-stage-message-download"]').text()).toContain(
      '下载超时'
    )

    for (const stage of ['extract', 'vote', 'parse', 'persist']) {
      const item = wrapper.find(`[data-testid="fc-pdf-stage-item-${stage}"]`)
      expect(item.attributes('data-status')).toBe('pending')
      expect(wrapper.find(`[data-testid="fc-pdf-stage-badge-${stage}"]`).text()).toBe('待执行')
    }

    expect(wrapper.find('[data-testid="fc-pdf-stage-success-count"]').text()).toBe('0')
    expect(wrapper.find('[data-testid="fc-pdf-stage-last-failed"]').text()).toContain('下载')
  })

  it('stageLogs 为空 / null：5 个 pending 节点 + 「尚未解析」提示', () => {
    for (const stageLogs of [[], null, undefined]) {
      const wrapper = mount(FinancialPdfStageTimeline, { props: { stageLogs } })
      expect(wrapper.find('[data-testid="fc-pdf-stage-empty"]').text()).toContain('尚未解析')

      for (const stage of STAGES) {
        const item = wrapper.find(`[data-testid="fc-pdf-stage-item-${stage}"]`)
        expect(item.exists()).toBe(true)
        expect(item.attributes('data-status')).toBe('pending')
      }

      expect(wrapper.find('[data-testid="fc-pdf-stage-success-count"]').text()).toBe('0')
      expect(wrapper.find('[data-testid="fc-pdf-stage-total-duration"]').text()).toBe('—')
      expect(wrapper.find('[data-testid="fc-pdf-stage-last-failed"]').exists()).toBe(false)
    }
  })

  it('耗时格式化：500 显示 500ms，2500 显示 2.5s，缺失显示 —', () => {
    const stageLogs = [
      makeLog('download', 'success', 500),
      makeLog('extract', 'success', 2500),
      makeLog('vote', 'success', null)
    ]
    const wrapper = mount(FinancialPdfStageTimeline, { props: { stageLogs } })

    expect(wrapper.find('[data-testid="fc-pdf-stage-duration-download"]').text()).toBe('500ms')
    expect(wrapper.find('[data-testid="fc-pdf-stage-duration-extract"]').text()).toBe('2.5s')
    expect(wrapper.find('[data-testid="fc-pdf-stage-duration-vote"]').text()).toBe('—')
  })

  it('stage 字段大小写不敏感：DOWNLOAD / Extract 等大写也能命中', () => {
    const stageLogs = [
      { stage: 'DOWNLOAD', status: 'SUCCESS', durationMs: 100, message: null },
      { stage: 'Extract', status: 'Failed', durationMs: 200, message: '解析错误' }
    ]
    const wrapper = mount(FinancialPdfStageTimeline, { props: { stageLogs } })

    expect(
      wrapper.find('[data-testid="fc-pdf-stage-item-download"]').attributes('data-status')
    ).toBe('success')
    expect(
      wrapper.find('[data-testid="fc-pdf-stage-item-extract"]').attributes('data-status')
    ).toBe('failed')
    expect(wrapper.find('[data-testid="fc-pdf-stage-message-extract"]').text()).toContain(
      '解析错误'
    )
  })

  it('compact=true 时仍然渲染 <details> 折叠面板', () => {
    const stageLogs = [makeLog('download', 'failed', 100, '网络异常详情很长很长')]
    const wrapper = mount(FinancialPdfStageTimeline, {
      props: { stageLogs, compact: true }
    })

    const details = wrapper.find('[data-testid="fc-pdf-stage-details-download"]')
    expect(details.exists()).toBe(true)
    expect(details.element.tagName).toBe('DETAILS')
    const message = wrapper.find('[data-testid="fc-pdf-stage-message-download"]')
    expect(message.exists()).toBe(true)
    expect(message.text()).toContain('网络异常详情很长很长')
    expect(wrapper.find('.fc-pdf-stage-list').classes()).toContain('is-compact')
  })

  it('每阶段渲染为 <details> 折叠面板', () => {
    const stageLogs = STAGES.map(s => makeLog(s, 'success', 100))
    const wrapper = mount(FinancialPdfStageTimeline, { props: { stageLogs } })

    for (const stage of STAGES) {
      const details = wrapper.find(`[data-testid="fc-pdf-stage-details-${stage}"]`)
      expect(details.exists()).toBe(true)
      expect(details.element.tagName).toBe('DETAILS')
    }
  })

  it('details 字段存在时渲染 key-value 列表', () => {
    const stageLogs = [
      makeLog('download', 'success', 100, '已存在本地', { filePath: '/tmp/test.pdf', fileSize: '12345' }),
      makeLog('extract', 'success', 300, null, { 'PdfPig.success': 'True', 'PdfPig.pages': '10' })
    ]
    const wrapper = mount(FinancialPdfStageTimeline, { props: { stageLogs } })

    const dlDownload = wrapper.find('[data-testid="fc-pdf-stage-detail-list-download"]')
    expect(dlDownload.exists()).toBe(true)
    const dts = dlDownload.findAll('dt')
    const dds = dlDownload.findAll('dd')
    expect(dts.length).toBe(2)
    expect(dts[0].text()).toBe('filePath')
    expect(dds[0].text()).toBe('/tmp/test.pdf')
    expect(dts[1].text()).toBe('fileSize')
    expect(dds[1].text()).toBe('12345')

    const dlExtract = wrapper.find('[data-testid="fc-pdf-stage-detail-list-extract"]')
    expect(dlExtract.exists()).toBe(true)
    expect(dlExtract.findAll('dt').length).toBe(2)
  })

  it('details 为 null 且无 message 时显示「暂无详细信息」', () => {
    const stageLogs = [makeLog('download', 'success', 100)]
    const wrapper = mount(FinancialPdfStageTimeline, { props: { stageLogs } })

    const noDetails = wrapper.find('.fc-pdf-stage-no-details')
    expect(noDetails.exists()).toBe(true)
    expect(noDetails.text()).toContain('暂无详细信息')
  })
})
