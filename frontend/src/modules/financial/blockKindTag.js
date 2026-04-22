/**
 * V041-S4: PDF ParseUnit blockKind → Tag 颜色映射
 *
 * 与 sourceChannelTag.js 风格保持一致。后端 V041-S1 定义的 blockKind 取值：
 *   narrative_section（叙述段落） / table（表格） / figure_caption（图注）
 * 其他/缺失值统一兜底为 unknown。
 */

const META = {
  narrative_section: {
    key: 'narrative_section',
    label: '叙述段落',
    tone: 'blue',
    color: '#1d4ed8',
    bg: 'rgba(29, 78, 216, 0.10)',
    border: 'rgba(29, 78, 216, 0.25)'
  },
  table: {
    key: 'table',
    label: '表格',
    tone: 'green',
    color: '#059669',
    bg: 'rgba(5, 150, 105, 0.10)',
    border: 'rgba(5, 150, 105, 0.25)'
  },
  figure_caption: {
    key: 'figure_caption',
    label: '图注',
    tone: 'purple',
    color: '#7c3aed',
    bg: 'rgba(124, 58, 237, 0.10)',
    border: 'rgba(124, 58, 237, 0.25)'
  }
}

const FALLBACK = {
  key: 'unknown',
  label: '未知',
  tone: 'gray',
  color: '#6b7280',
  bg: 'rgba(107, 114, 128, 0.10)',
  border: 'rgba(107, 114, 128, 0.25)'
}

/**
 * 解析 blockKind 字符串为 Tag meta。
 * @param {unknown} kind
 * @returns {{key:string,label:string,tone:string,color:string,bg:string,border:string}}
 */
export function getBlockKindMeta(kind) {
  if (kind == null) return FALLBACK
  const raw = String(kind).trim()
  if (!raw) return FALLBACK
  const matched = META[raw.toLowerCase()]
  if (matched) return matched
  return { ...FALLBACK, label: raw }
}

export const BLOCK_KIND_META = META
export const BLOCK_KIND_FALLBACK = FALLBACK
