/**
 * Show a toast notification from anywhere in the app.
 * @param {{ message: string, type?: 'info'|'success'|'warning'|'error', duration?: number }} options
 */
export function showToast(options) {
  window.dispatchEvent(new CustomEvent('app-toast', { detail: options }))
}
