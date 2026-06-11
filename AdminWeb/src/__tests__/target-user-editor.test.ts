/**
 * target-user-editor.test.ts — validateTargetUserIds (textarea parsing edge cases)
 *
 * Design ref: §8 (target user textarea behavior), §7.1 (validateTargetUserIds signature)
 * Canonical module: src/modules/validation.ts (§5.1) — validateTargetUserIds lives here.
 * src/modules/target-user-editor.ts exports renderTargetUserEditor/readTargetUserEditor
 * (DOM functions, tested separately under happy-dom once task #4 lands).
 *
 * Extra coverage beyond validation.test.ts:
 *   - Windows/Mac line endings (\r\n, \r) normalized
 *   - Leading/trailing whitespace per ID trimmed
 *   - Large input (many IDs) does not crash
 *   - Order of valid IDs preserved (insertion order, first occurrence)
 *
 * SKELETON — fails on import until implementation lands.
 */
import { describe, it, expect } from 'vitest'
import {
  validateTargetUserIds,
  type TargetUserValidation,
} from '../modules/validation'

// ─── Canonical import sanity ──────────────────────────────────────────────────

describe('validateTargetUserIds — imported from validation module (§5.1)', () => {
  it('module exports validateTargetUserIds', () => {
    expect(typeof validateTargetUserIds).toBe('function')
  })
})

// ─── Line ending normalization ────────────────────────────────────────────────

describe('validateTargetUserIds — line ending handling', () => {
  it('Unix LF (\\n) — baseline, two IDs both valid', () => {
    const result = validateTargetUserIds('uuid-a\nuuid-b')
    expect(result.valid).toEqual(['uuid-a', 'uuid-b'])
  })

  it('Windows CRLF (\\r\\n) — treated same as LF', () => {
    const result = validateTargetUserIds('uuid-a\r\nuuid-b')
    expect(result.valid).toContain('uuid-a')
    expect(result.valid).toContain('uuid-b')
    // IDs must not contain \r in the output
    result.valid.forEach(id => expect(id).not.toContain('\r'))
  })

  it('Classic Mac CR (\\r) — treated as line separator', () => {
    const result = validateTargetUserIds('uuid-a\ruuid-b')
    expect(result.valid).toContain('uuid-a')
    expect(result.valid).toContain('uuid-b')
    result.valid.forEach(id => expect(id).not.toContain('\r'))
  })

  it('mixed CRLF and LF — both separators work', () => {
    const result = validateTargetUserIds('uuid-a\r\nuuid-b\nuuid-c')
    expect(result.valid).toHaveLength(3)
  })
})

// ─── ID trimming ──────────────────────────────────────────────────────────────

describe('validateTargetUserIds — whitespace trimming', () => {
  it('ID with leading space → trimmed to canonical ID', () => {
    const result = validateTargetUserIds('  uuid-a\nuuid-b')
    expect(result.valid).toContain('uuid-a')
    result.valid.forEach(id => expect(id).not.toMatch(/^\s/))
  })

  it('ID with trailing space → trimmed', () => {
    const result = validateTargetUserIds('uuid-a  \nuuid-b')
    expect(result.valid).toContain('uuid-a')
    result.valid.forEach(id => expect(id).not.toMatch(/\s$/))
  })

  it('ID with leading and trailing spaces → trimmed', () => {
    const result = validateTargetUserIds('  uuid-a  ')
    expect(result.valid).toContain('uuid-a')
  })

  it('whitespace-only lines treated as empty, not as valid IDs', () => {
    const result = validateTargetUserIds('uuid-a\n   \nuuid-b')
    expect(result.valid).toHaveLength(2)
    expect(result.empty).toBeGreaterThanOrEqual(1)
  })

  it('tab-only line is empty', () => {
    const result = validateTargetUserIds('uuid-a\n\t\nuuid-b')
    expect(result.valid).toHaveLength(2)
    expect(result.empty).toBeGreaterThanOrEqual(1)
  })
})

// ─── Insertion order preservation ────────────────────────────────────────────

describe('validateTargetUserIds — order preservation', () => {
  it('valid IDs in valid[] preserve original input order', () => {
    const result = validateTargetUserIds('charlie\nalpha\nbeta')
    expect(result.valid).toEqual(['charlie', 'alpha', 'beta'])
  })

  it('first occurrence of dup is in valid[], subsequent in duplicates[]', () => {
    // uuid-a appears at line 1 and line 3 — line 1 stays valid, line 3 is dup
    const result = validateTargetUserIds('uuid-a\nuuid-b\nuuid-a\nuuid-c')
    expect(result.valid).toEqual(['uuid-a', 'uuid-b', 'uuid-c'])
    expect(result.duplicates).toContain('uuid-a')
    // uuid-a appears exactly once in duplicates (line 3)
    expect(result.duplicates.filter(d => d === 'uuid-a')).toHaveLength(1)
  })

  it('many unique IDs — all returned in input order', () => {
    const ids = Array.from({ length: 20 }, (_, i) => `user-${String(i).padStart(4, '0')}`)
    const result = validateTargetUserIds(ids.join('\n'))
    expect(result.valid).toEqual(ids)
  })
})

// ─── Edge cases ───────────────────────────────────────────────────────────────

describe('validateTargetUserIds — edge cases', () => {
  it('empty string → valid=[], duplicates=[], empty=0 or 1', () => {
    const result = validateTargetUserIds('')
    expect(result.valid).toEqual([])
    expect(result.duplicates).toEqual([])
    // empty string may count as 0 empty lines or 1 depending on split behavior
    expect(result.empty).toBeGreaterThanOrEqual(0)
  })

  it('single valid ID, no newline → valid=[id], empty=0, duplicates=[]', () => {
    const result = validateTargetUserIds('uuid-only')
    expect(result.valid).toEqual(['uuid-only'])
    expect(result.duplicates).toEqual([])
  })

  it('large input (500 IDs) does not crash', () => {
    const ids = Array.from({ length: 500 }, (_, i) => `bulk-user-${i}`)
    expect(() => validateTargetUserIds(ids.join('\n'))).not.toThrow()
  })

  it('large input: 500 unique IDs → all 500 in valid', () => {
    const ids = Array.from({ length: 500 }, (_, i) => `bulk-user-${i}`)
    const result = validateTargetUserIds(ids.join('\n'))
    expect(result.valid).toHaveLength(500)
    expect(result.duplicates).toHaveLength(0)
  })

  it('large input: 500 same IDs → 1 in valid, 499 in duplicates', () => {
    const ids = Array.from({ length: 500 }, () => 'same-user')
    const result = validateTargetUserIds(ids.join('\n'))
    expect(result.valid).toHaveLength(1)
    expect(result.duplicates).toHaveLength(499)
  })

  it('UUIDs with hyphens (typical UGS user ID format)', () => {
    const uuid = '550e8400-e29b-41d4-a716-446655440000'
    const result = validateTargetUserIds(uuid)
    expect(result.valid).toEqual([uuid])
  })

  it('result type matches TargetUserValidation shape', () => {
    const result: TargetUserValidation = validateTargetUserIds('uuid-a')
    expect(typeof result.empty).toBe('number')
    expect(Array.isArray(result.valid)).toBe(true)
    expect(Array.isArray(result.duplicates)).toBe(true)
  })
})

// ─── Return value counting ────────────────────────────────────────────────────

describe('validateTargetUserIds — result accounting', () => {
  it('total lines = valid.length + duplicates.length + empty', () => {
    // 'a\nb\na\n\nc' → 5 lines: valid=[a,b,c], dups=[a], empty=1
    const raw = 'a\nb\na\n\nc'
    const lineCount = raw.split(/\n/).length  // 5
    const result = validateTargetUserIds(raw)
    expect(result.valid.length + result.duplicates.length + result.empty).toBe(lineCount)
  })

  it('3 IDs all unique → valid=3, dups=0, empty=0', () => {
    const result = validateTargetUserIds('x\ny\nz')
    expect(result.valid).toHaveLength(3)
    expect(result.duplicates).toHaveLength(0)
    expect(result.empty).toBe(0)
  })
})

// ─── DOM exports note ────────────────────────────────────────────────────────
// src/modules/target-user-editor.ts exports renderTargetUserEditor + readTargetUserEditor.
// Those are DOM functions — tested in a separate happy-dom test file once task #4 lands.
// [BLOCK: DOM] — not covered here.
