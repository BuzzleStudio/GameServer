// src/modules/status.ts — Mail status derivation and badge HTML.
// Pure module — no DOM dependencies.

import type { MailStatus } from '../state'

// Re-export MailStatus from state.ts so callers import from a single place.
export type { MailStatus }

const EXPIRING_SOON_MS = 24 * 60 * 60 * 1000  // 24 hours

/**
 * Derive mail status from server timestamps + current time.
 * @param startTime  ISO string or null/undefined (MailInfo.StartTime)
 * @param expireTime ISO string or null/undefined (MailInfo.ExpireTime)
 * @param now        ms since epoch — ALWAYS passed in, never calls Date.now() internally
 */
export function deriveMailStatus(
  startTime:  string | null | undefined,
  expireTime: string | null | undefined,
  now: number,
): MailStatus {
  const start  = startTime  ? Date.parse(startTime)  : null
  const expire = expireTime ? Date.parse(expireTime) : null

  // Priority order matters: Expired before Scheduled edge case
  if (expire !== null && expire <= now)        return 'Expired'
  if (start  !== null && start  > now)         return 'Scheduled'
  if (expire === null)                         return 'No expiry'
  if (expire - now <= EXPIRING_SOON_MS)        return 'Expiring soon'
  return 'Active'
}

export function statusBadgeHtml(status: MailStatus): string {
  const cls: Record<MailStatus, string> = {
    'Active':        'status-active',
    'Expiring soon': 'status-expiring',
    'Expired':       'status-expired',
    'Scheduled':     'status-scheduled',
    'No expiry':     'status-noexpiry',
  }
  return `<span class="status-badge ${cls[status]}">${status}</span>`
}
