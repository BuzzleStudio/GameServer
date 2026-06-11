// src/modules/validation.ts — Pure input validation functions.
// No DOM dependencies.

import type { AttachmentDraft } from '../types'

/** Returns error string or null if valid. */
export function validateSubject(v: string): string | null {
  if (!v.trim()) return 'Subject is required'
  if (v.length > 128) return `Subject too long: ${v.length}/128 chars`
  return null
}

/** Returns error string or null if valid. */
export function validateBody(v: string): string | null {
  if (!v.trim()) return 'Body is required'
  if (v.length > 1024) return `Body too long: ${v.length}/1024 chars`
  return null
}

/** Returns error string or null if both inputs are valid UTC date/time for the API. */
export function validateExpiryInputs(date: string, time: string): string | null {
  if (!date || !time) return 'Both date and time are required when expiry is set'
  const d = new Date(`${date}T${time}:00Z`)
  if (isNaN(d.getTime())) return 'Invalid date/time — use yyyy-MM-dd and HH:mm'
  // Detect date overflow (e.g. 2025-02-29 silently becomes 2025-03-01 in V8)
  const pad = (n: number) => String(n).padStart(2, '0')
  const parsedDate = `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`
  if (parsedDate !== date.trim()) return 'Invalid date/time — use yyyy-MM-dd and HH:mm'
  return null
}

/** Returns error string or null if the single attachment draft is valid. */
export function validateAttachment(d: AttachmentDraft): string | null {
  if (d.payoutAmount <= 0) return 'Amount must be > 0'
  if (d.chance <= 0 || d.chance > 1) return 'Chance must be between 0.01 and 1.00'
  return null
}

/** Returns a list of per-attachment error strings (empty = all valid). */
export function validateAttachments(drafts: AttachmentDraft[]): string[] {
  return drafts
    .map((d, i) => {
      const e = validateAttachment(d)
      return e ? `Attachment ${i + 1}: ${e}` : null
    })
    .filter(Boolean) as string[]
}

export interface TargetUserValidation {
  valid:      string[]   // unique non-empty IDs (first occurrence wins)
  duplicates: string[]   // IDs that appear more than once
  empty:      number     // count of blank/whitespace-only lines
}

/**
 * Parse a textarea value of user IDs (one per line).
 * Normalises \r\n and \r line endings. Trims each ID.
 */
export function validateTargetUserIds(raw: string): TargetUserValidation {
  // Normalise Windows/Mac line endings to \n
  const normalised = raw.replace(/\r\n/g, '\n').replace(/\r/g, '\n')
  const lines = normalised.split('\n').map(s => s.trim())
  const nonEmpty = lines.filter(Boolean)
  const empty = lines.length - nonEmpty.length
  const seen = new Set<string>()
  const duplicates: string[] = []
  const valid: string[] = []
  for (const id of nonEmpty) {
    if (seen.has(id)) {
      duplicates.push(id)
    } else {
      seen.add(id)
      valid.push(id)
    }
  }
  return { valid, duplicates, empty }
}
