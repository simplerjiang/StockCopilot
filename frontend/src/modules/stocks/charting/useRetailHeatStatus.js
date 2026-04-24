import { ref, watch } from 'vue'

export function useRetailHeatStatus(symbol) {
  const status = ref(null)
  const collecting = ref(false)
  const collectResult = ref(null)

  async function fetchStatus() {
    if (!symbol.value) { status.value = null; return }
    try {
      const res = await fetch(`/api/stocks/${encodeURIComponent(symbol.value)}/retail-heat/collection-status?days=30`)
      if (res.ok) {
        status.value = await res.json()
      }
    } catch {
      status.value = null
    }
  }

  async function collectNow() {
    if (!symbol.value || collecting.value) return
    collecting.value = true
    collectResult.value = null
    try {
      const res = await fetch(`/api/stocks/${encodeURIComponent(symbol.value)}/retail-heat/collect-now`, { method: 'POST' })
      if (res.ok) {
        collectResult.value = await res.json()
        await fetchStatus()
      }
    } catch (e) {
      collectResult.value = { error: e.message }
    } finally {
      collecting.value = false
    }
  }

  watch(symbol, () => {
    collectResult.value = null
    fetchStatus()
  }, { immediate: true })

  return { status, collecting, collectResult, fetchStatus, collectNow }
}
