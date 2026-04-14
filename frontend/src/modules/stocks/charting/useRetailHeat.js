import { ref, watch, onUnmounted } from 'vue'

export function useRetailHeat(symbol) {
  const heatData = ref(null)
  const loading = ref(false)
  const backfilling = ref(false)
  let pollTimer = null

  function stopPolling() {
    if (pollTimer) {
      clearInterval(pollTimer)
      pollTimer = null
    }
  }

  async function fetchHeat() {
    if (!symbol.value) {
      heatData.value = null
      backfilling.value = false
      stopPolling()
      return
    }
    loading.value = true
    try {
      const from = new Date()
      from.setDate(from.getDate() - 180)
      const fromStr = from.toISOString().split('T')[0]
      const res = await fetch(`/api/stocks/${encodeURIComponent(symbol.value)}/retail-heat?from=${fromStr}`)
      if (res.ok) {
        const json = await res.json()
        heatData.value = json

        if (json.backfilling) {
          backfilling.value = true
          if (!pollTimer) {
            pollTimer = setInterval(fetchHeat, 5000)
          }
        } else if (json.data && json.data.length > 0) {
          backfilling.value = false
          stopPolling()
        } else if (!json.backfilling && (!json.data || json.data.length === 0)) {
          // 首次触发，回填可能刚启动还未标记，短暂轮询等待
          backfilling.value = true
          if (!pollTimer) {
            pollTimer = setInterval(fetchHeat, 5000)
            // 超时保护：120 秒后停止轮询
            setTimeout(() => {
              if (pollTimer && (!heatData.value?.data?.length)) {
                backfilling.value = false
                stopPolling()
              }
            }, 120000)
          }
        }
      } else {
        heatData.value = null
        backfilling.value = false
        stopPolling()
      }
    } catch {
      heatData.value = null
      backfilling.value = false
      stopPolling()
    } finally {
      loading.value = false
    }
  }

  watch(symbol, () => {
    stopPolling()
    backfilling.value = false
    fetchHeat()
  }, { immediate: true })

  onUnmounted(stopPolling)

  return { heatData, loading, backfilling, fetchHeat }
}
