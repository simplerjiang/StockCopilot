/**
 * V040-S5 财报中心详情抽屉 — 公共 API 封装
 *
 * 仅做 fetch 透传 + 错误格式化，不在此处做中英文翻译（保持单一职责）。
 */

const extractMessage = async (resp) => {
  try {
    const body = await resp.json()
    if (body && typeof body === 'object') {
      return body.message
        || body.Message
        || body.errorMessage
        || body.ErrorMessage
        || body.error
        || ''
    }
  } catch {
    // body 不是 JSON，忽略
  }
  return ''
}

/**
 * 拉取财报详情
 * @param {string|number} id 报告 ID
 * @returns {Promise<object>}
 */
export async function fetchFinancialReportDetail(id) {
  if (id === undefined || id === null || id === '') {
    throw new Error('id 不能为空')
  }
  const resp = await fetch(`/api/stocks/financial/reports/${encodeURIComponent(String(id))}`)
  if (!resp.ok) {
    const msg = await extractMessage(resp)
    throw new Error(msg ? `${msg} (HTTP ${resp.status})` : `加载详情失败 (HTTP ${resp.status})`)
  }
  return resp.json()
}

/**
 * 触发财报重新采集
 * @param {string} symbol 股票代码
 * @returns {Promise<object>}
 */
export async function recollectFinancialReport(symbol) {
  if (!symbol || !String(symbol).trim()) {
    throw new Error('symbol 不能为空')
  }
  const resp = await fetch(
    `/api/stocks/financial/collect/${encodeURIComponent(String(symbol).trim())}`,
    { method: 'POST' }
  )
  if (!resp.ok) {
    const msg = await extractMessage(resp)
    throw new Error(msg ? `${msg} (HTTP ${resp.status})` : `重新采集失败 (HTTP ${resp.status})`)
  }
  return resp.json().catch(() => ({}))
}

/**
 * 触发 PDF 原件采集（巨潮下载 + 提取 + 投票 + 解析 + 持久化）。
 * 与 recollectFinancialReport 不同：本接口专门用于将 PDF 原件入库，
 * 完成后前端应重新调用 listPdfFiles 拿到新的 pdfFileId。
 * 后端代理超时为 5 分钟（V041-S8-FU-1）。
 * @param {string} symbol 股票代码
 * @returns {Promise<object>} worker 响应（可能含 success / processedCount / errors 等字段）
 */
export async function collectPdfFiles(symbol) {
  if (!symbol || !String(symbol).trim()) {
    throw new Error('symbol 不能为空')
  }
  const resp = await fetch(
    `/api/stocks/financial/pdf-files/collect/${encodeURIComponent(String(symbol).trim())}`,
    { method: 'POST' }
  )
  if (!resp.ok) {
    const msg = await extractMessage(resp)
    throw new Error(msg ? `${msg} (HTTP ${resp.status})` : `采集 PDF 原件失败 (HTTP ${resp.status})`)
  }
  return resp.json().catch(() => ({}))
}

// ============================================================================
// V041-S4: PDF 文件相关 API（列表 / 详情 / 内容 URL / 重新解析）
// ----------------------------------------------------------------------------
// 与上方 fetchFinancialReportDetail 共享 extractMessage + ok 检查风格。
// ============================================================================

/**
 * 拉取 PDF 文件列表
 * @param {object} params
 * @param {string=} params.symbol 股票代码（可选）
 * @param {string=} params.reportType 报告类型（可选）
 * @param {number=} params.page 页码（默认 1）
 * @param {number=} params.pageSize 每页大小（默认 20）
 * @returns {Promise<object>}
 */
export async function listPdfFiles({ symbol, reportType, page = 1, pageSize = 20 } = {}) {
  const query = new URLSearchParams()
  if (symbol != null && String(symbol).trim() !== '') {
    query.set('symbol', String(symbol).trim())
  }
  if (reportType != null && String(reportType).trim() !== '') {
    query.set('reportType', String(reportType).trim())
  }
  query.set('page', String(page))
  query.set('pageSize', String(pageSize))
  const resp = await fetch(`/api/stocks/financial/pdf-files?${query.toString()}`)
  if (!resp.ok) {
    const msg = await extractMessage(resp)
    throw new Error(msg ? `${msg} (HTTP ${resp.status})` : `加载 PDF 列表失败 (HTTP ${resp.status})`)
  }
  return resp.json()
}

/**
 * 拉取 PDF 文件详情（含 ParseUnits）
 * @param {string|number} id PDF 文件 ID
 * @returns {Promise<object>}
 */
export async function fetchPdfFileDetail(id) {
  if (id === undefined || id === null || id === '') {
    throw new Error('id 不能为空')
  }
  const resp = await fetch(`/api/stocks/financial/pdf-files/${encodeURIComponent(String(id))}`)
  if (!resp.ok) {
    const msg = await extractMessage(resp)
    throw new Error(msg ? `${msg} (HTTP ${resp.status})` : `加载 PDF 详情失败 (HTTP ${resp.status})`)
  }
  return resp.json()
}

/**
 * 构造 PDF 内容 URL（给 iframe.src 用）。不发起请求。
 * @param {string|number} id PDF 文件 ID
 * @returns {string}
 */
export function buildPdfFileContentUrl(id) {
  return `/api/stocks/financial/pdf-files/${encodeURIComponent(String(id ?? ''))}/content`
}

/**
 * 触发 PDF 重新解析
 * @param {string|number} id PDF 文件 ID
 * @returns {Promise<{success: boolean, error?: string|null, detail?: object|null}>}
 */
export async function reparsePdfFile(id) {
  if (id === undefined || id === null || id === '') {
    throw new Error('id 不能为空')
  }
  const resp = await fetch(
    `/api/stocks/financial/pdf-files/${encodeURIComponent(String(id))}/reparse`,
    { method: 'POST' }
  )
  if (!resp.ok) {
    const msg = await extractMessage(resp)
    throw new Error(msg ? `${msg} (HTTP ${resp.status})` : `重新解析失败 (HTTP ${resp.status})`)
  }
  return resp.json().catch(() => ({ success: false, error: '响应解析失败', detail: null }))
}

// ============================================================================
// 财报采集配置 API
// ============================================================================

/**
 * 获取财报采集配置
 * @returns {Promise<object>}
 */
export async function fetchFinancialConfig() {
  const resp = await fetch('/api/stocks/financial/config')
  if (!resp.ok) {
    const msg = await extractMessage(resp)
    throw new Error(msg ? `${msg} (HTTP ${resp.status})` : `加载配置失败 (HTTP ${resp.status})`)
  }
  return resp.json()
}

/**
 * 更新财报采集配置
 * @param {object} config 配置对象
 * @returns {Promise<object>}
 */
export async function updateFinancialConfig(config) {
  const resp = await fetch('/api/stocks/financial/config', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(config)
  })
  if (!resp.ok) {
    const msg = await extractMessage(resp)
    throw new Error(msg ? `${msg} (HTTP ${resp.status})` : `保存配置失败 (HTTP ${resp.status})`)
  }
  return resp.json()
}
