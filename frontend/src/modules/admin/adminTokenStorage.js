export const ADMIN_TOKEN_KEY = 'admin_token'
const LEGACY_ADMIN_TOKEN_KEYS = ['admin-token']

export const readAdminToken = () => {
  const current = localStorage.getItem(ADMIN_TOKEN_KEY)
  if (current) {
    LEGACY_ADMIN_TOKEN_KEYS.forEach(key => localStorage.removeItem(key))
    return current
  }

  for (const key of LEGACY_ADMIN_TOKEN_KEYS) {
    const legacyValue = localStorage.getItem(key)
    if (legacyValue) {
      localStorage.setItem(ADMIN_TOKEN_KEY, legacyValue)
      LEGACY_ADMIN_TOKEN_KEYS.forEach(legacyKey => localStorage.removeItem(legacyKey))
      return legacyValue
    }
  }

  return ''
}

export const writeAdminToken = value => {
  if (value) {
    localStorage.setItem(ADMIN_TOKEN_KEY, value)
  } else {
    localStorage.removeItem(ADMIN_TOKEN_KEY)
  }
  LEGACY_ADMIN_TOKEN_KEYS.forEach(key => localStorage.removeItem(key))
}

export const clearAdminToken = () => {
  localStorage.removeItem(ADMIN_TOKEN_KEY)
  LEGACY_ADMIN_TOKEN_KEYS.forEach(key => localStorage.removeItem(key))
}