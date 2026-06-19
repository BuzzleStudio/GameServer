// src/modules/environment-badge.ts
// Pure HTML string helpers for environment labels — no DOM deps.

function _esc(s: string): string {
  return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
}

function _isProd(env: string): boolean {
  return env.toLowerCase().includes('prod')
}

/** Compact colored badge for header area. */
export function renderEnvBadge(env: string): string {
  const cls = _isProd(env) ? 'env-badge env-badge-prod' : 'env-badge env-badge-other'
  return `<span class="${cls}" title="Environment: ${_esc(env)}">${_esc(env)}</span>`
}

/**
 * Full-width warning banner — shown only for production.
 * Returns empty string for non-prod environments.
 */
export function renderEnvBanner(env: string): string {
  if (!env || !_isProd(env)) return ''
  return `<div class="env-banner env-banner-prod" role="alert">⚠ Sending to: <strong>${_esc(env)}</strong> — production environment</div>`
}
