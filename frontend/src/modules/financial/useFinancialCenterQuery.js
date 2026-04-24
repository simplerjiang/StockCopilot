/**
 * V040-S3 财报中心 — 数据查询 Composable
 * 封装 query state、URL 同步、fetchReports、股票名懒加载
 */
import { reactive, ref, watch } from 'vue'
import { DEFAULT_QUERY, SORT_FIELDS, buildDefaultDateRange } from './financialCenterConstants.js'
import { pickStockMatch } from './symbolMarketUtil.js'

// 前端枚举（小写）→ 后端存储值（首字母大写）映射
const REPORT_TYPE_API_MAP = {
  annual: 'Annual',
  q1: 'Q1',
  q2: 'Q2',
  q3: 'Q3'
}

const URL_PREFIX = 'fc.'
const URL_KEYS = {
  symbols: `${URL_PREFIX}symbols`,
  startDate: `${URL_PREFIX}start`,
  endDate: `${URL_PREFIX}end`,
  reportTypes: `${URL_PREFIX}types`,
  keyword: `${URL_PREFIX}kw`,
  page: `${URL_PREFIX}page`,
  pageSize: `${URL_PREFIX}size`,
  sort: `${URL_PREFIX}sort`
}

const arraysEqualUnordered = (a, b) => {
  if (a.length !== b.length) return false
  const sa = [...a].sort()
  const sb = [...b].sort()
  return sa.every((v, i) => v === sb[i])
}

const cloneDefault = () => ({
  symbols: [...DEFAULT_QUERY.symbols],
  startDate: DEFAULT_QUERY.startDate,
  endDate: DEFAULT_QUERY.endDate,
  reportTypes: [...DEFAULT_QUERY.reportTypes],
  keyword: DEFAULT_QUERY.keyword,
  page: DEFAULT_QUERY.page,
  pageSize: DEFAULT_QUERY.pageSize,
  sortField: DEFAULT_QUERY.sortField,
  sortDirection: DEFAULT_QUERY.sortDirection
})

const parseSort = (raw) => {
  if (!raw || typeof raw !== 'string') return null
  const [field, dir] = raw.split(':')
  if (!SORT_FIELDS.includes(field)) return null
  if (dir !== 'asc' && dir !== 'desc') return null
  return { sortField: field, sortDirection: dir }
}

const parseFromUrl = () => {
  const params = new URLSearchParams(window.location.search)
  const out = cloneDefault()

  const symbolsRaw = params.get(URL_KEYS.symbols)
  if (symbolsRaw) {
    out.symbols = symbolsRaw.split(',').map(s => s.trim()).filter(Boolean)
  }
  const start = params.get(URL_KEYS.startDate)
  if (start) out.startDate = start
  const end = params.get(URL_KEYS.endDate)
  if (end) out.endDate = end
  const typesRaw = params.get(URL_KEYS.reportTypes)
  if (typesRaw) {
    const allowed = new Set(DEFAULT_QUERY.reportTypes)
    const parsed = typesRaw.split(',').map(s => s.trim()).filter(t => allowed.has(t))
    if (parsed.length > 0) out.reportTypes = parsed
  }
  const kw = params.get(URL_KEYS.keyword)
  if (kw) out.keyword = kw
  const page = parseInt(params.get(URL_KEYS.page) || '', 10)
  if (Number.isFinite(page) && page >= 1) out.page = page
  const size = parseInt(params.get(URL_KEYS.pageSize) || '', 10)
  if (Number.isFinite(size) && size >= 1) out.pageSize = size
  const sort = parseSort(params.get(URL_KEYS.sort))
  if (sort) {
    out.sortField = sort.sortField
    out.sortDirection = sort.sortDirection
  }
  return out
}

const writeToUrl = (query) => {
  const url = new URL(window.location.href)
  const params = url.searchParams

  // symbols
  if (query.symbols && query.symbols.length > 0) {
    params.set(URL_KEYS.symbols, query.symbols.join(','))
  } else {
    params.delete(URL_KEYS.symbols)
  }

  // dates — only persist when different from current default range
  const def = buildDefaultDateRange()
  if (query.startDate && query.startDate !== def.startDate) {
    params.set(URL_KEYS.startDate, query.startDate)
  } else {
    params.delete(URL_KEYS.startDate)
  }
  if (query.endDate && query.endDate !== def.endDate) {
    params.set(URL_KEYS.endDate, query.endDate)
  } else {
    params.delete(URL_KEYS.endDate)
  }

  // report types — only persist when not equal to default (all four)
  if (query.reportTypes && !arraysEqualUnordered(query.reportTypes, DEFAULT_QUERY.reportTypes)) {
    params.set(URL_KEYS.reportTypes, query.reportTypes.join(','))
  } else {
    params.delete(URL_KEYS.reportTypes)
  }

  // keyword
  if (query.keyword && query.keyword.trim()) {
    params.set(URL_KEYS.keyword, query.keyword.trim())
  } else {
    params.delete(URL_KEYS.keyword)
  }

  // page
  if (query.page && query.page !== DEFAULT_QUERY.page) {
    params.set(URL_KEYS.page, String(query.page))
  } else {
    params.delete(URL_KEYS.page)
  }

  // pageSize
  if (query.pageSize && query.pageSize !== DEFAULT_QUERY.pageSize) {
    params.set(URL_KEYS.pageSize, String(query.pageSize))
  } else {
    params.delete(URL_KEYS.pageSize)
  }

  // sort
  if (
    query.sortField !== DEFAULT_QUERY.sortField ||
    query.sortDirection !== DEFAULT_QUERY.sortDirection
  ) {
    params.set(URL_KEYS.sort, `${query.sortField}:${query.sortDirection}`)
  } else {
    params.delete(URL_KEYS.sort)
  }

  const next = `${url.pathname}?${params.toString()}${url.hash}`
  window.history.replaceState({}, '', next)
}

const buildApiUrl = (query) => {
  const params = new URLSearchParams()
  if (query.symbols && query.symbols.length > 0) {
    params.set('symbol', query.symbols.join(','))
  }
  if (query.reportTypes && query.reportTypes.length > 0) {
    // 后端 LiteDB BsonExpression IN 大小写敏感，存储值首字母大写（Annual/Q1/Q2/Q3）
    params.set('reportType', query.reportTypes.map(t => REPORT_TYPE_API_MAP[t] || t).join(','))
  }
  if (query.startDate) params.set('startDate', query.startDate)
  if (query.endDate) params.set('endDate', query.endDate)
  if (query.keyword && query.keyword.trim()) {
    params.set('keyword', query.keyword.trim())
  }
  params.set('page', String(query.page))
  params.set('pageSize', String(query.pageSize))
  params.set('sort', `${query.sortField}:${query.sortDirection}`)
  return `/api/stocks/financial/reports?${params.toString()}`
}

export function useFinancialCenterQuery() {
  const query = reactive(parseFromUrl())
  const rawItems = ref([])
  const rawTotal = ref(0)
  const items = ref([])
  const total = ref(0)
  const loading = ref(false)
  const error = ref('')
  const lastRequestId = ref(0)

  // 股票名懒加载
  const symbolNameMap = reactive({})
  const symbolNamePending = new Set()
  const nameQueue = []
  let nameInflight = 0
  const NAME_MAX_CONCURRENCY = 5

  const enqueueSymbol = (symbol) => {
    if (!symbol) return
    if (Object.prototype.hasOwnProperty.call(symbolNameMap, symbol)) return
    if (symbolNamePending.has(symbol)) return
    symbolNamePending.add(symbol)
    nameQueue.push(symbol)
    pumpNameQueue()
  }

  const pumpNameQueue = () => {
    while (nameInflight < NAME_MAX_CONCURRENCY && nameQueue.length > 0) {
      const sym = nameQueue.shift()
      nameInflight++
      fetch(`/api/stocks/search?q=${encodeURIComponent(sym)}&limit=5`)
        .then(r => {
          if (!r.ok) return null
          const ct = (r.headers && typeof r.headers.get === 'function')
            ? (r.headers.get('content-type') || '')
            : 'application/json'
          if (!ct.includes('application/json')) return null
          return r.json()
        })
        .then(data => {
          let name = ''
          if (Array.isArray(data) && data.length > 0) {
            const pick = pickStockMatch(data, sym)
            name = pick?.name || pick?.shortName || ''
          } else if (data && typeof data === 'object') {
            const arr = data.items || data.data || data.results
            if (Array.isArray(arr) && arr.length > 0) {
              const pick = pickStockMatch(arr, sym)
              name = pick?.name || pick?.shortName || ''
            } else {
              name = data.name || data.shortName || ''
            }
          }
          symbolNameMap[sym] = name || ''
          // 名称到位后重算前端过滤（关键词模式下生效）
          applyClientFilter()
        })
        .catch(() => {
          symbolNameMap[sym] = ''
          applyClientFilter()
        })
        .finally(() => {
          nameInflight--
          symbolNamePending.delete(sym)
          pumpNameQueue()
        })
    }
  }

  const normaliseItems = (raw) => {
    if (Array.isArray(raw)) return raw
    if (raw && typeof raw === 'object') {
      if (Array.isArray(raw.items)) return raw.items
      if (Array.isArray(raw.data)) return raw.data
      if (Array.isArray(raw.results)) return raw.results
    }
    return []
  }

  const normaliseTotal = (raw, fallback) => {
    if (raw && typeof raw === 'object') {
      if (typeof raw.total === 'number') return raw.total
      if (typeof raw.totalCount === 'number') return raw.totalCount
      if (typeof raw.count === 'number') return raw.count
    }
    return fallback
  }

  /**
   * 后端已支持 keyword 按 Symbol LIKE 过滤；
   * 前端仅做股票名二次过滤（名称不在后端 DB 中）。
   */
  const applyClientFilter = () => {
    const list = rawItems.value
    const kw = (query.keyword || '').trim().toLowerCase()
    if (!kw) {
      items.value = list
      total.value = rawTotal.value
      return
    }
    // 后端已按 Symbol 过滤，前端再补充按股票名过滤
    const filtered = list.filter((it) => {
      const sym = String(it?.symbol || it?.Symbol || '').toLowerCase()
      if (sym.includes(kw)) return true
      const name = String(symbolNameMap[it?.symbol || it?.Symbol] || '').toLowerCase()
      return name.includes(kw)
    })
    items.value = filtered
    total.value = filtered.length
  }

  const fetchReports = async () => {
    const reqId = ++lastRequestId.value
    loading.value = true
    error.value = ''
    try {
      const url = buildApiUrl(query)
      const res = await fetch(url)
      const contentType = (res.headers && typeof res.headers.get === 'function')
        ? (res.headers.get('content-type') || '')
        : 'application/json'
      if (!res.ok || !contentType.includes('application/json')) {
        throw new Error(res.ok ? '服务端响应非 JSON（接口可能未上线或路径错误）' : `HTTP ${res.status}`)
      }
      const data = await res.json()
      if (reqId !== lastRequestId.value) return
      const list = normaliseItems(data)
      rawItems.value = list
      rawTotal.value = normaliseTotal(data, list.length)
      applyClientFilter()
      // 触发懒加载股票名
      for (const item of list) {
        const sym = item?.symbol || item?.Symbol
        if (sym) enqueueSymbol(sym)
      }
    } catch (err) {
      if (reqId !== lastRequestId.value) return
      rawItems.value = []
      rawTotal.value = 0
      items.value = []
      total.value = 0
      error.value = (err && err.message) ? err.message : '加载失败'
    } finally {
      if (reqId === lastRequestId.value) {
        loading.value = false
      }
    }
  }

  const resetQuery = () => {
    const def = cloneDefault()
    Object.assign(query, def)
  }

  // URL 同步：query 变更 → 300ms 防抖 → URL
  let syncTimer = null
  const scheduleSyncToUrl = () => {
    if (syncTimer) clearTimeout(syncTimer)
    syncTimer = setTimeout(() => {
      writeToUrl(query)
      syncTimer = null
    }, 300)
  }

  watch(
    () => [
      [...query.symbols],
      query.startDate,
      query.endDate,
      [...query.reportTypes],
      query.keyword,
      query.page,
      query.pageSize,
      query.sortField,
      query.sortDirection
    ],
    scheduleSyncToUrl,
    { deep: false }
  )

  // keyword 变化时若已有原始数据，立即重算前端过滤
  watch(
    () => query.keyword,
    () => { applyClientFilter() }
  )

  return {
    query,
    items,
    total,
    loading,
    error,
    symbolNameMap,
    fetchReports,
    resetQuery,
    enqueueSymbol
  }
}
