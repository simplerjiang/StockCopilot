/**
 * V040-S4 采集结果透明化 — 来源渠道 Tag 着色工具
 *
 * 渠道 → 颜色映射：
 *   emweb       → 蓝
 *   datacenter  → 青/蓝绿（teal）
 *   ths         → 紫
 *   pdf         → 橙
 *   其他/Unknown → 灰
 *
 * 在 FinancialDataTestPanel.vue 与 FinancialReportTab.vue 复用，
 * 避免两处面板各自维护一份颜色表。
 */

const TAGS = {
  emweb: {
    key: 'emweb',
    label: 'EM 网页',
    tone: 'blue',
    color: '#1d4ed8',
    bg: 'rgba(29, 78, 216, 0.10)',
    border: 'rgba(29, 78, 216, 0.25)'
  },
  datacenter: {
    key: 'datacenter',
    label: '数据中心',
    tone: 'teal',
    color: '#0f766e',
    bg: 'rgba(15, 118, 110, 0.10)',
    border: 'rgba(15, 118, 110, 0.25)'
  },
  ths: {
    key: 'ths',
    label: '同花顺',
    tone: 'purple',
    color: '#7c3aed',
    bg: 'rgba(124, 58, 237, 0.10)',
    border: 'rgba(124, 58, 237, 0.25)'
  },
  pdf: {
    key: 'pdf',
    label: 'PDF',
    tone: 'orange',
    color: '#d97706',
    bg: 'rgba(217, 119, 6, 0.10)',
    border: 'rgba(217, 119, 6, 0.25)'
  }
}

const FALLBACK = {
  key: 'unknown',
  label: '未知来源',
  tone: 'gray',
  color: '#6b7280',
  bg: 'rgba(107, 114, 128, 0.10)',
  border: 'rgba(107, 114, 128, 0.25)'
}

/**
 * 解析来源渠道为可渲染的 Tag 配置。
 * @param {unknown} channel 来源渠道字符串（不区分大小写）
 * @returns {{key:string,label:string,tone:string,color:string,bg:string,border:string}}
 */
export function getSourceChannelTag(channel) {
  if (channel == null) return FALLBACK
  const raw = String(channel).trim()
  if (!raw) return FALLBACK
  const key = raw.toLowerCase()
  const matched = TAGS[key]
  if (matched) return matched
  // 未知渠道：用 fallback 配色，但保留原始名称便于排查
  return { ...FALLBACK, label: raw }
}

/**
 * 把后端 tag 内联样式拼成 CSS style 字符串，便于直接绑定到 :style。
 * @param {{color:string,bg:string,border:string}} tag
 * @returns {string}
 */
export function sourceChannelTagStyle(tag) {
  if (!tag) return ''
  return [
    `color:${tag.color}`,
    `background:${tag.bg}`,
    `border:1px solid ${tag.border}`
  ].join(';')
}

export const SOURCE_CHANNEL_TAGS = TAGS
export const SOURCE_CHANNEL_FALLBACK_TAG = FALLBACK
