import { computed, ref } from 'vue'

const STATUS_URL = '/api/stocks/financial/embedding/status'

const readNumber = (source, camelKey, pascalKey = null) => {
  const value = source?.[camelKey] ?? (pascalKey ? source?.[pascalKey] : undefined)
  const numeric = Number(value)
  return Number.isFinite(numeric) ? numeric : null
}

const readBoolean = (source, camelKey, pascalKey = null) => {
  const value = source?.[camelKey] ?? (pascalKey ? source?.[pascalKey] : undefined)
  return value === true
}

const getCoverage = source => readNumber(source, 'coverage', 'Coverage')
const getChunkCount = source => readNumber(source, 'chunkCount', 'ChunkCount') ?? 0
const getEmbeddingCount = source => readNumber(source, 'embeddingCount', 'EmbeddingCount') ?? 0

export function isEmbeddingStatusDegraded(statusValue, errorValue = '') {
  if (errorValue) return true
  if (!statusValue) return false

  const available = readBoolean(statusValue, 'available', 'Available')
  const coverage = getCoverage(statusValue)
  const chunkCount = getChunkCount(statusValue)
  const embeddingCount = getEmbeddingCount(statusValue)

  if (chunkCount > 0 && embeddingCount === 0) return true
  if (available !== true) return true
  if (coverage !== null && coverage < 0.5) return true

  return false
}

export function useEmbeddingStatus() {
  const status = ref(null)
  const loading = ref(false)
  const error = ref('')

  const isEmbeddingDegraded = computed(() => isEmbeddingStatusDegraded(status.value, error.value))

  const coveragePercent = computed(() => {
    const coverage = getCoverage(status.value)
    if (coverage === null) return null
    return Math.max(0, Math.min(100, coverage * 100))
  })

  const refreshEmbeddingStatus = async () => {
    loading.value = true
    error.value = ''
    try {
      const response = await fetch(STATUS_URL)
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`)
      }
      status.value = await response.json()
    } catch {
      error.value = '无法检查 Embedding 状态'
    } finally {
      loading.value = false
    }
  }

  return {
    status,
    loading,
    error,
    isEmbeddingDegraded,
    coveragePercent,
    refreshEmbeddingStatus
  }
}