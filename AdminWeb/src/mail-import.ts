// ─── Mail import / validation — pure logic, no DOM deps ─────────────────────────
//
// Prototype-pollution safe: never merge raw parsed objects.
// All field extraction uses an explicit whitelist.
// Forbidden keys (__proto__, constructor, prototype) cause immediate rejection.

import type { AttachmentDraft, ItemSpecificAsset, RarityValue } from './types'
import { Rarity } from './types'

// ── Safety ────────────────────────────────────────────────────────────────────────
const FORBIDDEN_KEYS = new Set(['__proto__', 'constructor', 'prototype'])

function hasForbiddenKey(obj: Record<string, unknown>): boolean {
  return Object.keys(obj).some(k => FORBIDDEN_KEYS.has(k))
}

// ── Whitelist-safe coercions ──────────────────────────────────────────────────────
function safeStr(v: unknown, fallback = ''): string {
  return typeof v === 'string' ? v : fallback
}

function safeNum(v: unknown, fallback = 0): number {
  return typeof v === 'number' && isFinite(v) ? v : fallback
}

function safeArr(v: unknown): unknown[] {
  return Array.isArray(v) ? v : []
}

// ── Result types ──────────────────────────────────────────────────────────────────
export interface ImportedDraft {
  title: string
  content: string
  endTime: string | null
  targetUserIds: string[]
  attachments: AttachmentDraft[]
}

export interface ImportValidationResult {
  ok: boolean
  errors: string[]
  warnings: string[]   // unknown IDs, legacy Ticket format, skipped attachments
  draft: ImportedDraft | null
}

// ── Core: validate + import ───────────────────────────────────────────────────────
/**
 * Validate a parsed JSON import and extract a safe ImportedDraft.
 *
 * @param parsed         The result of JSON.parse — unknown type.
 * @param knownCurrencyIds  IDs from CURRENCY_IDS.
 * @param knownItemIds      IDs from ITEM_IDS.
 * @param knownTicketIds    IDs from TICKET_IDS.
 * @param rawJson        Original string (for size check).
 * @param maxBytes       Max allowed import size (default 256 KB).
 */
export function validateAndImport(
  parsed: unknown,
  knownCurrencyIds: readonly string[],
  knownItemIds: readonly string[],
  knownTicketIds: readonly string[],
  rawJson = '',
  maxBytes = 256 * 1024,
): ImportValidationResult {
  const errors: string[] = []
  const warnings: string[] = []

  // ── Size check (before any parsing cost) ─────────────────────────────────────
  if (rawJson.length > maxBytes) {
    return {
      ok: false,
      errors: [`Import too large: ${rawJson.length} bytes (max ${maxBytes})`],
      warnings: [],
      draft: null,
    }
  }

  // ── Top-level type check ──────────────────────────────────────────────────────
  if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
    return { ok: false, errors: ['Import must be a JSON object'], warnings: [], draft: null }
  }

  const obj = parsed as Record<string, unknown>

  // ── Forbidden key guard on root ───────────────────────────────────────────────
  if (hasForbiddenKey(obj)) {
    return { ok: false, errors: ['Import contains forbidden key (__proto__/constructor/prototype)'], warnings: [], draft: null }
  }

  // ── schemaVersion ─────────────────────────────────────────────────────────────
  if (obj['schemaVersion'] !== 1) {
    errors.push(`schemaVersion must be 1, got: ${JSON.stringify(obj['schemaVersion'])}`)
  }

  // ── mail field ────────────────────────────────────────────────────────────────
  const rawMail = obj['mail']
  if (typeof rawMail !== 'object' || rawMail === null || Array.isArray(rawMail)) {
    return { ok: false, errors: [...errors, '"mail" field is missing or not an object'], warnings, draft: null }
  }

  const mail = rawMail as Record<string, unknown>

  if (hasForbiddenKey(mail)) {
    return { ok: false, errors: ['Import "mail" contains forbidden key'], warnings: [], draft: null }
  }

  if (errors.length > 0) {
    return { ok: false, errors, warnings, draft: null }
  }

  // ── Whitelist-copy mail fields ────────────────────────────────────────────────
  const title   = safeStr(mail['title'])
  const content = safeStr(mail['content'])
  const endTime = typeof mail['endTime'] === 'string' ? mail['endTime'] : null

  // targetUserIds: plain string array only — NEVER overwrite proxy/env/projectId
  const rawTargetUserIds = safeArr(mail['targetUserIds'])
  const targetUserIds: string[] = rawTargetUserIds
    .filter(x => typeof x === 'string')
    .map(x => x as string)

  // ── Attachments ───────────────────────────────────────────────────────────────
  const knownCurrencySet = new Set(knownCurrencyIds)
  const knownItemSet     = new Set(knownItemIds)
  const knownTicketSet   = new Set(knownTicketIds)

  const rawAtts = safeArr(mail['attachments'])
  const attachments: AttachmentDraft[] = []

  for (let i = 0; i < rawAtts.length; i++) {
    const rawAtt = rawAtts[i]
    if (typeof rawAtt !== 'object' || rawAtt === null || Array.isArray(rawAtt)) {
      warnings.push(`Attachment[${i}]: skipped (not an object)`)
      continue
    }
    const att = rawAtt as Record<string, unknown>

    if (hasForbiddenKey(att)) {
      warnings.push(`Attachment[${i}]: skipped (contains forbidden key)`)
      continue
    }

    // Whitelist-copy only
    const assetType     = safeStr(att['AssetType'] ?? att['assetType'], 'Currency')
    const payoutAssetId = safeStr(att['PayoutAssetId'] ?? att['payoutAssetId'])
    const payoutAmount  = safeNum(att['PayoutAmount'] ?? att['payoutAmount'], 1)
    const chance        = safeNum(att['Chance'] ?? att['chance'], 1)

    const lowerType = assetType.toLowerCase()
    let unknownIdWarning: string | undefined

    // ── Unknown-ID check ───────────────────────────────────────────────────────
    if (lowerType === 'currency') {
      if (payoutAssetId && !knownCurrencySet.has(payoutAssetId)) {
        unknownIdWarning = `⚠ unknown ID "${payoutAssetId}"`
        warnings.push(`Attachment[${i}]: unknown Currency ID "${payoutAssetId}"`)
      }
    } else if (lowerType === 'item') {
      if (payoutAssetId && !knownItemSet.has(payoutAssetId)) {
        unknownIdWarning = `⚠ unknown ID "${payoutAssetId}"`
        warnings.push(`Attachment[${i}]: unknown Item ID "${payoutAssetId}"`)
      }
    } else if (lowerType === 'ticket') {
      // Ticket: PayoutAssetId may be JSON object or plain string
      let ticketId = payoutAssetId
      try {
        const p2 = JSON.parse(payoutAssetId)
        if (typeof p2 === 'object' && p2 !== null && !Array.isArray(p2)) {
          const p2r = p2 as Record<string, unknown>
          ticketId = safeStr(p2r['BlueprintId'])
        }
      } catch { /* plain string */ }
      if (ticketId && !knownTicketSet.has(ticketId)) {
        unknownIdWarning = `⚠ unknown ID "${ticketId}"`
        warnings.push(`Attachment[${i}]: unknown Ticket ID "${ticketId}"`)
      }
    } else if (lowerType === 'itemspecificasset') {
      try {
        const p2 = JSON.parse(payoutAssetId)
        if (typeof p2 === 'object' && p2 !== null && !Array.isArray(p2)) {
          const bpId = safeStr((p2 as Record<string, unknown>)['BlueprintId'])
          if (bpId && !knownItemSet.has(bpId)) {
            unknownIdWarning = `⚠ unknown ID "${bpId}"`
            warnings.push(`Attachment[${i}]: unknown ISA BlueprintId "${bpId}"`)
          }
        }
      } catch { /* ignore */ }
    }

    // ── Build draft via whitelist (never merge raw object) ─────────────────────
    const isISA    = lowerType === 'itemspecificasset'
    const isTicket = lowerType === 'ticket'

    const defaultRow: ItemSpecificAsset = {
      BlueprintId:  '',
      CurrentLevel: 1,
      Rarity:       Rarity.Common,
      InitialLevel: 1,
      FromSource:   '',
    }

    let itemRows: ItemSpecificAsset[] = [defaultRow]

    if (isISA || isTicket) {
      try {
        const p2 = JSON.parse(payoutAssetId)
        const r = Array.isArray(p2) ? (p2[0] ?? {}) : (p2 ?? {})
        if (typeof r === 'object' && r !== null) {
          const rr = r as Record<string, unknown>
          itemRows = [{
            BlueprintId:  safeStr(rr['BlueprintId']),
            CurrentLevel: safeNum(rr['CurrentLevel'], 1) || 1,
            Rarity:       (safeNum(rr['Rarity'], Rarity.Common)) as RarityValue,
            InitialLevel: safeNum(rr['InitialLevel'], 1) || 1,
            FromSource:   safeStr(rr['FromSource']),
          }]
        }
      } catch { /* use default */ }
    }

    attachments.push({
      _id:           crypto.randomUUID(),
      payoutAssetId: (isISA || isTicket) ? '' : payoutAssetId,
      assetType,
      payoutAmount:      payoutAmount || 1,
      chance:            chance || 1,
      itemRows,
      _unknownIdWarning: unknownIdWarning,
    })
  }

  return {
    ok: true,
    errors: [],
    warnings,
    draft: { title, content, endTime, targetUserIds, attachments },
  }
}
