// src/modules/date-format.ts — Date formatting and ISO serialization utilities.
// Pure module — no DOM dependencies.

/**
 * Build an ISO 8601 end-time string for the API.
 * Preserves exact backend serialization: {date}T{time}:00Z → .toISOString()
 * Returns null when date or time are empty.
 */
export function buildEndTimeIso(date: string, time: string): string | null {
  if (!date || !time) return null
  const trimDate = date.trim()
  const raw = `${trimDate}T${time.trim()}:00Z`
  const d = new Date(raw)
  if (isNaN(d.getTime())) throw new Error('End Time: invalid date/time. Use yyyy-MM-dd and HH:mm.')
  // Detect date overflow (e.g. 2025-02-29 silently becomes 2025-03-01 in V8)
  const pad = (n: number) => String(n).padStart(2, '0')
  const parsedDate = `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`
  if (parsedDate !== trimDate) throw new Error('End Time: invalid date/time. Use yyyy-MM-dd and HH:mm.')
  return d.toISOString()
}

/** Compute a UTC date + time offset from now by `days`. */
export function presetFromNow(days: number): { date: string; time: string } {
  const d = new Date(Date.now() + days * 86_400_000)
  const pad = (n: number) => String(n).padStart(2, '0')
  return {
    date: `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`,
    time: `${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}`,
  }
}

/** Format ISO string for table display — short date only (e.g. "Jun 1, 2026"). */
export function formatDateShort(iso: string | null | undefined): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (isNaN(d.getTime())) return iso
  return d.toLocaleDateString('en-US', {
    timeZone: 'UTC',
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  })
}

/** Format ISO string as UTC datetime for drawer display (e.g. "Jul 1, 2026, 23:59 UTC"). */
export function formatDateUtc(iso: string | null | undefined): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (isNaN(d.getTime())) return iso
  const opts: Intl.DateTimeFormatOptions = {
    timeZone: 'UTC',
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }
  return d.toLocaleString('en-US', opts) + ' UTC'
}

/** Format ISO string in browser's local timezone for hint display. */
export function formatDateLocal(iso: string | null | undefined): string {
  if (!iso) return ''
  const d = new Date(iso)
  if (isNaN(d.getTime())) return ''
  return d.toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
    timeZoneName: 'short',
  })
}

/** Extract UTC date and time strings for `<input type="date">` / `<input type="time">` from ISO string. */
export function isoToEditInputs(iso: string | null | undefined): { date: string; time: string } {
  if (!iso) return { date: '', time: '' }
  const d = new Date(iso)
  if (isNaN(d.getTime())) return { date: '', time: '' }
  const pad = (n: number) => String(n).padStart(2, '0')
  return {
    date: `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`,
    time: `${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}`,
  }
}
