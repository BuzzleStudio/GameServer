/**
 * validation.test.ts — validateSubject, validateBody, validateExpiryInputs,
 *                       validateAttachment, validateAttachments, validateTargetUserIds
 *
 * Design ref: §7.1 (exact function signatures + exact error strings)
 * Module:     src/modules/validation.ts (lands in task #4)
 *
 * SKELETON — fails on import until implementation lands.
 */
import { describe, it, expect } from 'vitest'
import {
  validateSubject,
  validateBody,
  validateExpiryInputs,
  validateAttachment,
  validateAttachments,
  validateTargetUserIds,
  type TargetUserValidation,
} from '../modules/validation'
import type { AttachmentDraft } from '../types'

// Minimal valid AttachmentDraft for test fixtures
function makeDraft(overrides: Partial<AttachmentDraft> = {}): AttachmentDraft {
  return {
    payoutAssetId: 'gem',
    assetType: 'Currency',
    payoutAmount: 1,
    chance: 1,
    itemRows: [],
    ...overrides,
  }
}

// ─── validateSubject ─────────────────────────────────────────────────────────

describe('validateSubject', () => {
  it('empty string → "Subject is required"', () => {
    expect(validateSubject('')).toBe('Subject is required')
  })

  it('whitespace-only string → "Subject is required" (trim check)', () => {
    expect(validateSubject('   ')).toBe('Subject is required')
  })

  it('single character → null (valid)', () => {
    expect(validateSubject('a')).toBeNull()
  })

  it('128 chars → null (valid, at limit)', () => {
    expect(validateSubject('x'.repeat(128))).toBeNull()
  })

  it('129 chars → "Subject too long: 129/128 chars"', () => {
    expect(validateSubject('x'.repeat(129))).toBe('Subject too long: 129/128 chars')
  })

  it('130 chars → "Subject too long: 130/128 chars"', () => {
    expect(validateSubject('x'.repeat(130))).toBe('Subject too long: 130/128 chars')
  })

  it('normal subject → null', () => {
    expect(validateSubject('Welcome to Backpack Adventures!')).toBeNull()
  })
})

// ─── validateBody ────────────────────────────────────────────────────────────

describe('validateBody', () => {
  it('empty string → "Body is required"', () => {
    expect(validateBody('')).toBe('Body is required')
  })

  it('whitespace-only → "Body is required"', () => {
    expect(validateBody('\n\t  \n')).toBe('Body is required')
  })

  it('1 char → null (valid)', () => {
    expect(validateBody('a')).toBeNull()
  })

  it('1024 chars → null (valid, at limit)', () => {
    expect(validateBody('x'.repeat(1024))).toBeNull()
  })

  it('1025 chars → "Body too long: 1025/1024 chars"', () => {
    expect(validateBody('x'.repeat(1025))).toBe('Body too long: 1025/1024 chars')
  })

  it('1026 chars → "Body too long: 1026/1024 chars"', () => {
    expect(validateBody('x'.repeat(1026))).toBe('Body too long: 1026/1024 chars')
  })
})

// ─── validateExpiryInputs ────────────────────────────────────────────────────

describe('validateExpiryInputs', () => {
  it('valid date and time → null', () => {
    expect(validateExpiryInputs('2026-12-31', '23:59')).toBeNull()
  })

  it('empty date → error', () => {
    expect(validateExpiryInputs('', '12:00')).not.toBeNull()
  })

  it('empty time → error', () => {
    expect(validateExpiryInputs('2026-01-01', '')).not.toBeNull()
  })

  it('both empty → error', () => {
    expect(validateExpiryInputs('', '')).not.toBeNull()
  })

  it('non-date string → error (invalid date/time)', () => {
    const err = validateExpiryInputs('not-a-date', '12:00')
    expect(err).not.toBeNull()
    expect(err).toContain('Invalid date')
  })

  it('non-leap year Feb 29 → error', () => {
    expect(validateExpiryInputs('2025-02-29', '00:00')).not.toBeNull()
  })

  it('leap year Feb 29 → null (valid)', () => {
    expect(validateExpiryInputs('2024-02-29', '00:00')).toBeNull()
  })

  it('error message mentions yyyy-MM-dd and HH:mm format', () => {
    const err = validateExpiryInputs('31/12/2026', '23:59')
    expect(err).toMatch(/yyyy-MM-dd/i)
  })
})

// ─── validateAttachment ──────────────────────────────────────────────────────

describe('validateAttachment — amount checks', () => {
  it('amount=1, chance=1 → null (valid)', () => {
    expect(validateAttachment(makeDraft({ payoutAmount: 1, chance: 1 }))).toBeNull()
  })

  it('amount=0 → "Amount must be > 0"', () => {
    expect(validateAttachment(makeDraft({ payoutAmount: 0 }))).toBe('Amount must be > 0')
  })

  it('amount=-1 → error', () => {
    expect(validateAttachment(makeDraft({ payoutAmount: -1 }))).not.toBeNull()
  })

  it('amount=1000 → null (valid, no upper limit)', () => {
    expect(validateAttachment(makeDraft({ payoutAmount: 1000 }))).toBeNull()
  })
})

describe('validateAttachment — chance checks', () => {
  it('chance=1.0 → null (valid, max)', () => {
    expect(validateAttachment(makeDraft({ chance: 1.0 }))).toBeNull()
  })

  it('chance=0.01 → null (valid, practical min)', () => {
    expect(validateAttachment(makeDraft({ chance: 0.01 }))).toBeNull()
  })

  it('chance=0.5 → null', () => {
    expect(validateAttachment(makeDraft({ chance: 0.5 }))).toBeNull()
  })

  it('chance=0 → "Chance must be between 0.01 and 1.00"', () => {
    expect(validateAttachment(makeDraft({ chance: 0 }))).toBe('Chance must be between 0.01 and 1.00')
  })

  it('chance=1.01 → error (> 1)', () => {
    expect(validateAttachment(makeDraft({ chance: 1.01 }))).not.toBeNull()
  })

  it('chance=-0.01 → error (negative)', () => {
    expect(validateAttachment(makeDraft({ chance: -0.01 }))).not.toBeNull()
  })
})

// ─── validateAttachments ─────────────────────────────────────────────────────

describe('validateAttachments', () => {
  it('empty array → no errors', () => {
    expect(validateAttachments([])).toEqual([])
  })

  it('all valid drafts → no errors', () => {
    const drafts = [makeDraft(), makeDraft({ payoutAmount: 100, chance: 0.5 })]
    expect(validateAttachments(drafts)).toEqual([])
  })

  it('one invalid draft → one error in array', () => {
    const drafts = [makeDraft({ payoutAmount: 0 })]
    const errors = validateAttachments(drafts)
    expect(errors).toHaveLength(1)
    expect(errors[0]).toContain('Attachment 1')
  })

  it('error message includes 1-based attachment index', () => {
    const drafts = [makeDraft(), makeDraft({ payoutAmount: 0 })]
    const errors = validateAttachments(drafts)
    expect(errors[0]).toContain('Attachment 2')
  })

  it('multiple invalid drafts → multiple errors', () => {
    const drafts = [
      makeDraft({ payoutAmount: 0 }),
      makeDraft({ chance: 0 }),
    ]
    expect(validateAttachments(drafts)).toHaveLength(2)
  })
})

// ─── validateTargetUserIds ───────────────────────────────────────────────────

describe('validateTargetUserIds — no duplicates', () => {
  it('two unique IDs → valid=[both], duplicates=[], empty=0', () => {
    const result: TargetUserValidation = validateTargetUserIds('uuid-a\nuuid-b')
    expect(result.valid).toEqual(['uuid-a', 'uuid-b'])
    expect(result.duplicates).toEqual([])
    expect(result.empty).toBe(0)
  })

  it('single ID → valid=[id], duplicates=[], empty=0', () => {
    const result = validateTargetUserIds('uuid-a')
    expect(result.valid).toEqual(['uuid-a'])
    expect(result.duplicates).toEqual([])
    expect(result.empty).toBe(0)
  })
})

describe('validateTargetUserIds — duplicates', () => {
  it('one dup → first occurrence in valid, second in duplicates', () => {
    const result = validateTargetUserIds('uuid-a\nuuid-b\nuuid-a')
    expect(result.valid).toEqual(['uuid-a', 'uuid-b'])
    expect(result.duplicates).toEqual(['uuid-a'])
    expect(result.empty).toBe(0)
  })

  it('triple occurrence → valid has 1, duplicates has 2', () => {
    const result = validateTargetUserIds('uuid-a\nuuid-a\nuuid-a')
    expect(result.valid).toEqual(['uuid-a'])
    expect(result.duplicates).toEqual(['uuid-a', 'uuid-a'])
  })

  it('all-dups scenario: three same IDs', () => {
    const result = validateTargetUserIds('x\nx\nx')
    expect(result.valid).toHaveLength(1)
    expect(result.duplicates).toHaveLength(2)
  })
})

describe('validateTargetUserIds — empty lines', () => {
  it('blank line between IDs → empty count = 1, blank not in valid', () => {
    const result = validateTargetUserIds('uuid-a\n\nuuid-b')
    expect(result.valid).toEqual(['uuid-a', 'uuid-b'])
    expect(result.empty).toBe(1)
  })

  it('leading/trailing blank lines counted', () => {
    const result = validateTargetUserIds('\nuuid-a\n')
    expect(result.valid).toEqual(['uuid-a'])
    expect(result.empty).toBe(2)  // leading + trailing
  })

  it('all-blank input → valid=[], duplicates=[], empty=N', () => {
    const result = validateTargetUserIds('\n\n\n')
    expect(result.valid).toEqual([])
    expect(result.duplicates).toEqual([])
    expect(result.empty).toBeGreaterThan(0)
  })

  it('whitespace-only lines treated as empty', () => {
    const result = validateTargetUserIds('uuid-a\n   \nuuid-b')
    // Trimmed whitespace line is blank → empty count
    expect(result.valid).toEqual(['uuid-a', 'uuid-b'])
    expect(result.empty).toBeGreaterThanOrEqual(1)
  })

  it('mixed valid + dup + blank', () => {
    const result = validateTargetUserIds('a\nb\na\n\nc')
    expect(result.valid).toEqual(['a', 'b', 'c'])
    expect(result.duplicates).toEqual(['a'])
    expect(result.empty).toBe(1)
  })
})
