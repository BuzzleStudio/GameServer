/**
 * date-format.test.ts — buildEndTimeIso, isoToEditInputs, formatDateShort, formatDateUtc, formatDateLocal
 *
 * Design ref: §9.2 (function signatures + exact behavior)
 * Module:     src/modules/date-format.ts (lands in task #4)
 *
 * SKELETON — fails on import until implementation lands.
 * BYTE-COMPAT rows marked — serialization must be exact.
 */
import { describe, it, expect } from 'vitest'
import {
  buildEndTimeIso,
  isoToEditInputs,
  formatDateShort,
  formatDateUtc,
  formatDateLocal,
} from '../modules/date-format'

// ─── buildEndTimeIso ─────────────────────────────────────────────────────────

describe('buildEndTimeIso — null cases (§9.2)', () => {
  it('returns null when both date and time are empty', () => {
    expect(buildEndTimeIso('', '')).toBeNull()
  })

  it('returns null when date is empty but time is set', () => {
    expect(buildEndTimeIso('', '12:00')).toBeNull()
  })

  it('returns null when date is set but time is empty', () => {
    expect(buildEndTimeIso('2025-12-31', '')).toBeNull()
  })
})

describe('buildEndTimeIso — valid dates [BYTE-COMPAT]', () => {
  it('[BYTE-COMPAT] 2025-12-31 23:59 → exact ISO string "2025-12-31T23:59:00.000Z"', () => {
    // BLOCKING: serialization must match C# backend expectation byte-for-byte
    expect(buildEndTimeIso('2025-12-31', '23:59')).toBe('2025-12-31T23:59:00.000Z')
  })

  it('[BYTE-COMPAT] 2026-01-01 00:00 → "2026-01-01T00:00:00.000Z"', () => {
    expect(buildEndTimeIso('2026-01-01', '00:00')).toBe('2026-01-01T00:00:00.000Z')
  })

  it('[BYTE-COMPAT] midday date serializes correctly', () => {
    expect(buildEndTimeIso('2026-06-15', '12:30')).toBe('2026-06-15T12:30:00.000Z')
  })

  it('leap year 2024-02-29 is valid and does not throw', () => {
    expect(() => buildEndTimeIso('2024-02-29', '00:00')).not.toThrow()
    const result = buildEndTimeIso('2024-02-29', '00:00')
    expect(result).toBe('2024-02-29T00:00:00.000Z')
  })

  it('end-of-day 23:59 is valid', () => {
    expect(() => buildEndTimeIso('2026-12-31', '23:59')).not.toThrow()
  })
})

describe('buildEndTimeIso — invalid inputs throw', () => {
  it('throws on non-date string (not silent NaN)', () => {
    expect(() => buildEndTimeIso('not-a-date', '12:00')).toThrow()
  })

  it('throws on non-leap year Feb 29 (2025-02-29)', () => {
    expect(() => buildEndTimeIso('2025-02-29', '00:00')).toThrow()
  })

  it('throws on invalid time string', () => {
    expect(() => buildEndTimeIso('2026-01-01', '99:99')).toThrow()
  })
})

// ─── isoToEditInputs ──────────────────────────────────────────────────────────

describe('isoToEditInputs — round-trip with buildEndTimeIso [BYTE-COMPAT]', () => {
  it('[BYTE-COMPAT] round-trip: buildEndTimeIso(isoToEditInputs(iso)) === iso', () => {
    const original = '2025-12-31T23:59:00.000Z'
    const { date, time } = isoToEditInputs(original)
    expect(buildEndTimeIso(date, time)).toBe(original)
  })

  it('[BYTE-COMPAT] 2026-01-01T00:00:00.000Z round-trip', () => {
    const original = '2026-01-01T00:00:00.000Z'
    const { date, time } = isoToEditInputs(original)
    expect(buildEndTimeIso(date, time)).toBe(original)
  })

  it('extracts correct date part "2025-12-31"', () => {
    const { date } = isoToEditInputs('2025-12-31T23:59:00.000Z')
    expect(date).toBe('2025-12-31')
  })

  it('extracts correct time part "23:59"', () => {
    const { time } = isoToEditInputs('2025-12-31T23:59:00.000Z')
    expect(time).toBe('23:59')
  })

  it('returns empty strings for null input', () => {
    const { date, time } = isoToEditInputs(null)
    expect(date).toBe('')
    expect(time).toBe('')
  })

  it('returns empty strings for undefined input', () => {
    const { date, time } = isoToEditInputs(undefined)
    expect(date).toBe('')
    expect(time).toBe('')
  })

  it('returns empty strings for invalid ISO string', () => {
    const { date, time } = isoToEditInputs('not-a-date')
    expect(date).toBe('')
    expect(time).toBe('')
  })

  it('midnight (00:00) round-trips correctly', () => {
    const original = '2026-03-15T00:00:00.000Z'
    const { date, time } = isoToEditInputs(original)
    expect(date).toBe('2026-03-15')
    expect(time).toBe('00:00')
    expect(buildEndTimeIso(date, time)).toBe(original)
  })
})

// ─── formatDateShort ──────────────────────────────────────────────────────────

describe('formatDateShort — table display format (§9.1)', () => {
  it('returns "—" for null', () => {
    expect(formatDateShort(null)).toBe('—')
  })

  it('returns "—" for undefined', () => {
    expect(formatDateShort(undefined)).toBe('—')
  })

  it('formats "2026-06-11T10:00:00Z" as "Jun 11, 2026"', () => {
    // UTC date; en-US locale; short month + day + year
    expect(formatDateShort('2026-06-11T10:00:00Z')).toBe('Jun 11, 2026')
  })

  it('formats "2026-01-01T00:00:00Z" as "Jan 1, 2026"', () => {
    expect(formatDateShort('2026-01-01T00:00:00Z')).toBe('Jan 1, 2026')
  })

  it('returns the raw string for an unparseable ISO value (not crash)', () => {
    const bad = 'totally-invalid'
    expect(formatDateShort(bad)).toBe(bad)
  })

  it('uses UTC timezone (date does not shift with local offset)', () => {
    // 2026-06-11T00:30:00Z — even in UTC+14 (far east) this is still Jun 11
    expect(formatDateShort('2026-06-11T00:30:00Z')).toBe('Jun 11, 2026')
  })
})

// ─── formatDateUtc ────────────────────────────────────────────────────────────

describe('formatDateUtc — drawer display format (§9.1)', () => {
  it('returns "—" for null', () => {
    expect(formatDateUtc(null)).toBe('—')
  })

  it('returns "—" for undefined', () => {
    expect(formatDateUtc(undefined)).toBe('—')
  })

  it('output contains "UTC" suffix', () => {
    expect(formatDateUtc('2026-06-11T23:59:00.000Z')).toMatch(/UTC$/)
  })

  it('output contains the time "23:59"', () => {
    expect(formatDateUtc('2026-06-11T23:59:00.000Z')).toContain('23:59')
  })

  it('output contains the year "2026"', () => {
    expect(formatDateUtc('2026-06-11T23:59:00.000Z')).toContain('2026')
  })

  it('returns raw input for unparseable ISO (no crash)', () => {
    const bad = 'bad-iso'
    expect(formatDateUtc(bad)).toBe(bad)
  })
})

// ─── formatDateLocal ─────────────────────────────────────────────────────────

describe('formatDateLocal — browser local hint (§9.1)', () => {
  it('returns empty string for null', () => {
    expect(formatDateLocal(null)).toBe('')
  })

  it('returns empty string for undefined', () => {
    expect(formatDateLocal(undefined)).toBe('')
  })

  it('returns empty string for invalid ISO', () => {
    expect(formatDateLocal('bad-iso')).toBe('')
  })

  it('returns non-empty string for valid ISO', () => {
    // We cannot assert exact value (depends on test runner timezone)
    // but it must return something
    const result = formatDateLocal('2026-06-11T12:00:00Z')
    expect(typeof result).toBe('string')
    expect(result.length).toBeGreaterThan(0)
  })

  it('output contains timezone abbreviation (from Intl.DateTimeFormat timeZoneName:short)', () => {
    const result = formatDateLocal('2026-06-11T12:00:00Z')
    // Should contain some TZ info (e.g. UTC, GMT, ICT, EST, etc.)
    expect(result).toMatch(/[A-Z]{2,5}/)
  })
})
