/**
 * status.test.ts — deriveMailStatus + statusBadgeHtml
 *
 * Design ref: §2.1 (priority order), §2.2 (badge classes)
 * Module:     src/modules/status.ts (lands in task #4)
 *
 * SKELETON — fails on import until implementation lands.
 * All assertions derived from the exact priority logic in §2.1:
 *   1. expire !== null && expire <= now  → 'Expired'
 *   2. start  !== null && start  >  now  → 'Scheduled'
 *   3. expire === null                   → 'No expiry'
 *   4. expire - now <= EXPIRING_SOON_MS  → 'Expiring soon'
 *   5. else                             → 'Active'
 *
 * now is ALWAYS passed in — no Date.now() in the module.
 */
import { describe, it, expect } from 'vitest'
import { deriveMailStatus, statusBadgeHtml, type MailStatus } from '../modules/status'

// Fixed epoch — deterministic across all runs
const NOW = 1_750_000_000_000

// Convenience helpers
const ms = (h: number) => h * 60 * 60 * 1000
const iso = (offset: number) => new Date(NOW + offset).toISOString()

const EXPIRING_SOON_MS = 24 * 60 * 60 * 1000  // 86_400_000 ms — matches design §2.1

describe('deriveMailStatus — basic statuses', () => {
  it('returns "No expiry" when both startTime and expireTime are null', () => {
    expect(deriveMailStatus(null, null, NOW)).toBe<MailStatus>('No expiry')
  })

  it('returns "No expiry" when startTime is past and expireTime is null', () => {
    expect(deriveMailStatus(iso(-ms(1)), null, NOW)).toBe<MailStatus>('No expiry')
  })

  it('returns "Active" when expireTime is far in the future (>24h)', () => {
    expect(deriveMailStatus(iso(-ms(1)), iso(EXPIRING_SOON_MS + 1), NOW)).toBe<MailStatus>('Active')
  })

  it('returns "Expired" when expireTime is in the past', () => {
    expect(deriveMailStatus(null, iso(-ms(1)), NOW)).toBe<MailStatus>('Expired')
  })

  it('returns "Scheduled" when startTime is in the future and expireTime is null', () => {
    expect(deriveMailStatus(iso(+ms(1)), null, NOW)).toBe<MailStatus>('Scheduled')
  })

  it('returns "Scheduled" when startTime is future and expireTime is far future', () => {
    expect(deriveMailStatus(iso(+ms(1)), iso(EXPIRING_SOON_MS + ms(2)), NOW)).toBe<MailStatus>('Scheduled')
  })
})

describe('deriveMailStatus — boundary values', () => {
  it('expire === now exactly → "Expired" (boundary: expire <= now)', () => {
    expect(deriveMailStatus(null, iso(0), NOW)).toBe<MailStatus>('Expired')
  })

  it('expire = now + EXPIRING_SOON_MS → "Expiring soon" (boundary: diff <= EXPIRING_SOON_MS)', () => {
    expect(deriveMailStatus(null, iso(EXPIRING_SOON_MS), NOW)).toBe<MailStatus>('Expiring soon')
  })

  it('expire = now + EXPIRING_SOON_MS + 1ms → "Active"', () => {
    expect(deriveMailStatus(null, iso(EXPIRING_SOON_MS + 1), NOW)).toBe<MailStatus>('Active')
  })

  it('expire = now + EXPIRING_SOON_MS - 1ms → "Expiring soon"', () => {
    expect(deriveMailStatus(null, iso(EXPIRING_SOON_MS - 1), NOW)).toBe<MailStatus>('Expiring soon')
  })
})

describe('deriveMailStatus — priority order', () => {
  it('Expired beats Scheduled: startTime future + expireTime past → "Expired"', () => {
    // §2.1: Expired check is first in priority order
    expect(deriveMailStatus(iso(+ms(2)), iso(-ms(1)), NOW)).toBe<MailStatus>('Expired')
  })

  it('Expired beats No expiry would not occur (expireTime null → no Expired check)', () => {
    // When expire is null, Expired is never returned
    expect(deriveMailStatus(null, null, NOW)).not.toBe<MailStatus>('Expired')
  })
})

describe('deriveMailStatus — null / undefined inputs', () => {
  it('handles undefined startTime gracefully → treats as null', () => {
    expect(deriveMailStatus(undefined, null, NOW)).toBe<MailStatus>('No expiry')
  })

  it('handles undefined expireTime gracefully → treats as null', () => {
    expect(deriveMailStatus(null, undefined, NOW)).toBe<MailStatus>('No expiry')
  })

  it('handles both undefined → "No expiry"', () => {
    expect(deriveMailStatus(undefined, undefined, NOW)).toBe<MailStatus>('No expiry')
  })
})

describe('deriveMailStatus — determinism', () => {
  it('returns identical result on repeated calls with same arguments', () => {
    const result1 = deriveMailStatus(iso(-ms(1)), iso(EXPIRING_SOON_MS - 1), NOW)
    const result2 = deriveMailStatus(iso(-ms(1)), iso(EXPIRING_SOON_MS - 1), NOW)
    expect(result1).toBe(result2)
  })

  it('does not call Date.now() — different now values produce different results', () => {
    // Same mail, different now → different status
    const pastNow = NOW - EXPIRING_SOON_MS / 2  // mail was in Expiring-soon window before (12h before expiry)
    const futureNow = NOW + EXPIRING_SOON_MS * 2  // mail is now Expired
    const expireTime = iso(0)  // expires at NOW
    expect(deriveMailStatus(null, expireTime, pastNow)).toBe<MailStatus>('Expiring soon')
    expect(deriveMailStatus(null, expireTime, futureNow)).toBe<MailStatus>('Expired')
  })
})

describe('statusBadgeHtml — HTML generation (§2.2)', () => {
  it('Active → class "status-active" and text "Active"', () => {
    const html = statusBadgeHtml('Active')
    expect(html).toContain('status-active')
    expect(html).toContain('Active')
    expect(html).toContain('status-badge')
  })

  it('Expiring soon → class "status-expiring"', () => {
    const html = statusBadgeHtml('Expiring soon')
    expect(html).toContain('status-expiring')
    expect(html).toContain('Expiring soon')
  })

  it('Expired → class "status-expired"', () => {
    const html = statusBadgeHtml('Expired')
    expect(html).toContain('status-expired')
  })

  it('Scheduled → class "status-scheduled"', () => {
    const html = statusBadgeHtml('Scheduled')
    expect(html).toContain('status-scheduled')
  })

  it('No expiry → class "status-noexpiry"', () => {
    const html = statusBadgeHtml('No expiry')
    expect(html).toContain('status-noexpiry')
  })

  it('output is a <span> element', () => {
    const html = statusBadgeHtml('Active')
    expect(html).toMatch(/^<span/)
    expect(html).toMatch(/<\/span>$/)
  })
})
