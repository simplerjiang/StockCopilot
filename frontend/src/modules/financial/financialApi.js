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
