export const isAbortError = err => err?.name === 'AbortError'

const createAbortError = () => Object.assign(new Error('Aborted'), { name: 'AbortError' })

const isRetryableFetchError = err => {
  if (isAbortError(err)) {
    return false
  }

  const message = String(err?.message || '')
  return err instanceof TypeError
    || /failed to fetch|networkerror|load failed|connection refused|fetch failed/i.test(message)
}

const waitForRetryDelay = (delayMs, signal) => new Promise((resolve, reject) => {
  if (!delayMs || delayMs <= 0) {
    resolve()
    return
  }

  if (signal?.aborted) {
    reject(createAbortError())
    return
  }

  const abortHandler = () => {
    clearTimeout(timer)
    reject(createAbortError())
  }

  const timer = setTimeout(() => {
    signal?.removeEventListener?.('abort', abortHandler)
    resolve()
  }, delayMs)

  signal?.addEventListener('abort', abortHandler, { once: true })
})

const fetchWithRetry = async (url, options = {}, retryOptions = {}) => {
  const retries = Math.max(0, retryOptions.retries ?? 0)
  const retryDelayMs = retryOptions.retryDelayMs ?? 300

  for (let attempt = 0; ; attempt += 1) {
    try {
      return await fetch(url, options)
    } catch (err) {
      if (attempt >= retries || !isRetryableFetchError(err)) {
        throw err
      }

      const delayMs = typeof retryDelayMs === 'function'
        ? retryDelayMs(attempt + 1)
        : retryDelayMs * (attempt + 1)
      await waitForRetryDelay(delayMs, options.signal)
    }
  }
}

export const fetchBackendGet = (url, options = {}) => fetchWithRetry(url, options, {
  retries: 2,
  retryDelayMs: attempt => attempt * 300
})

export const replaceAbortController = currentController => {
  currentController?.abort()
  return new AbortController()
}

export const parseResponseMessage = async (response, fallback) => {
  try {
    const text = await response.text()
    if (!text) {
      return fallback
    }
    const payload = JSON.parse(text)
    return payload?.message || fallback
  } catch {
    return fallback
  }
}