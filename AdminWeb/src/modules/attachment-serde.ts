// src/modules/attachment-serde.ts
// Consolidated serde, validation, and summary logic for AttachmentDraft.
// Migrated from attachment-editor.ts, mail-editor-drawer.ts (private _attInfoToDraft).

import type { AttachmentDraft, ItemSpecificAsset, MailAttachmentInfo, RarityValue } from '../types'
import { Rarity, RARITY_LABELS } from '../types'
import type { ComboboxOption } from './asset-selector'
import {
  validateAttachmentType,
  validateAttachmentId,
  validateAttachmentAmount,
  validateAttachmentChance,
  validateBlueprintId,
  validateLevel,
} from './validation'

// ─── Private helpers ──────────────────────────────────────────────────────────

function _isJsonObjType(t: string): boolean {
  const l = t.trim().toLowerCase()
  return l === 'itemspecificasset' || l === 'ticket'
}

export function _defaultItemRow(): ItemSpecificAsset {
  return { BlueprintId: '', CurrentLevel: 1, Rarity: Rarity.Common, InitialLevel: 1, FromSource: '' }
}

function _idLabel(assetType: string): string {
  const l = assetType.trim().toLowerCase()
  if (l === 'currency') return 'Currency ID'
  if (l === 'item') return 'Payout Asset ID'
  return 'Asset ID'
}

// ─── AttachmentFormState ──────────────────────────────────────────────────────

export interface AttachmentFormState {
  _id:           string
  assetType:     string
  payoutAssetId: string
  payoutAmount:  number
  chance:        number
  blueprintId:   string
  currentLevel:  number
  rarity:        RarityValue
  initialLevel:  number
  fromSource:    string
  _legacyWarning?:    string
  _unknownIdWarning?: string
}

// ─── AttachmentFormErrors ─────────────────────────────────────────────────────

export interface AttachmentFormErrors {
  isValid:        boolean
  assetType?:     string | null
  payoutAssetId?: string | null
  payoutAmount?:  string | null
  chance?:        string | null
  blueprintId?:   string | null
  currentLevel?:  string | null
  initialLevel?:  string | null
}

// ─── SummaryOptions / AttachmentSummary ───────────────────────────────────────

export interface SummaryOptions {
  currencyOptions: ComboboxOption[]
  itemOptions:     ComboboxOption[]
  ticketOptions:   ComboboxOption[]
}

export interface AttachmentSummary {
  typeLabel:  string
  assetName:  string
  assetId:    string
  amount:     number
  chancePct:  string
  isValid:    boolean
  hasLegacy:  boolean
  hasUnknown: boolean
}

// ─── 5.1 deserializeAttachmentToForm ─────────────────────────────────────────

/**
 * Convert a server-side MailAttachmentInfo to an AttachmentDraft.
 * Exact migration of _attInfoToDraft from mail-editor-drawer.ts — no behaviour change.
 */
export function deserializeAttachmentToForm(att: MailAttachmentInfo): AttachmentDraft {
  const assetType     = att.AssetType ?? att.assetType ?? 'Currency'
  const payoutAssetId = att.PayoutAssetId ?? att.payoutAssetId ?? ''
  const isJson        = _isJsonObjType(assetType)
  const _id           = crypto.randomUUID()

  if (isJson) {
    try {
      const parsed = JSON.parse(payoutAssetId)
      const r: Record<string, unknown> = Array.isArray(parsed) ? (parsed[0] ?? {}) : (parsed ?? {})
      return {
        _id,
        payoutAssetId: '',
        assetType,
        payoutAmount: att.PayoutAmount ?? att.payoutAmount ?? 1,
        chance:       att.Chance       ?? att.chance       ?? 1,
        itemRows: [{
          BlueprintId:  typeof r['BlueprintId']  === 'string' ? r['BlueprintId']  as string : '',
          CurrentLevel: typeof r['CurrentLevel'] === 'number' ? r['CurrentLevel'] as number : 1,
          Rarity:       (typeof r['Rarity']      === 'number' ? r['Rarity']       as number : Rarity.Common) as RarityValue,
          InitialLevel: typeof r['InitialLevel'] === 'number' ? r['InitialLevel'] as number : 1,
          FromSource:   typeof r['FromSource']   === 'string' ? r['FromSource']   as string : '',
        }],
      }
    } catch {
      return {
        _id,
        payoutAssetId: '',
        assetType,
        payoutAmount: att.PayoutAmount ?? att.payoutAmount ?? 1,
        chance:       att.Chance       ?? att.chance       ?? 1,
        itemRows:     [{ ..._defaultItemRow(), BlueprintId: payoutAssetId }],
        _legacyWarning: `⚠ legacy format: plain-string PayoutAssetId "${payoutAssetId}"`,
      }
    }
  }

  return {
    _id,
    payoutAssetId,
    assetType,
    payoutAmount: att.PayoutAmount ?? att.payoutAmount ?? 1,
    chance:       att.Chance       ?? att.chance       ?? 1,
    itemRows:     [_defaultItemRow()],
  }
}

// ─── 5.2 serializeAttachmentForm ─────────────────────────────────────────────

/**
 * Convert modal form state back to an AttachmentDraft for the list and buildAttachments.
 * Preserves _id so list can identify the draft.
 */
export function serializeAttachmentForm(form: AttachmentFormState): AttachmentDraft {
  const isJson = _isJsonObjType(form.assetType)
  return {
    _id:           form._id,
    payoutAssetId: isJson ? '' : form.payoutAssetId.trim(),
    assetType:     form.assetType,
    payoutAmount:  form.payoutAmount,
    chance:        form.chance,
    itemRows: isJson
      ? [{
          BlueprintId:  form.blueprintId,
          CurrentLevel: form.currentLevel,
          Rarity:       form.rarity,
          InitialLevel: form.initialLevel,
          FromSource:   form.fromSource,
        }]
      : [_defaultItemRow()],
    _legacyWarning:    form._legacyWarning,
    _unknownIdWarning: form._unknownIdWarning,
  }
}

// ─── 5.3 validateAttachmentForm ───────────────────────────────────────────────

/**
 * Pure validation of modal form state. Returns per-field errors and top-level isValid.
 */
export function validateAttachmentForm(form: AttachmentFormState): AttachmentFormErrors {
  const isJson = _isJsonObjType(form.assetType)
  const errors: AttachmentFormErrors = { isValid: true }

  const typeErr = validateAttachmentType(form.assetType)
  if (typeErr) { errors.assetType = typeErr; errors.isValid = false }

  if (!isJson) {
    const label = _idLabel(form.assetType)
    const idErr = validateAttachmentId(form.payoutAssetId, label)
    if (idErr) { errors.payoutAssetId = idErr; errors.isValid = false }
  }

  const amtErr = validateAttachmentAmount(form.payoutAmount)
  if (amtErr) { errors.payoutAmount = amtErr; errors.isValid = false }

  const chanceErr = validateAttachmentChance(form.chance)
  if (chanceErr) { errors.chance = chanceErr; errors.isValid = false }

  if (isJson) {
    const bpErr = validateBlueprintId(form.blueprintId)
    if (bpErr) { errors.blueprintId = bpErr; errors.isValid = false }

    const clErr = validateLevel(form.currentLevel, 'Level')
    if (clErr) { errors.currentLevel = clErr; errors.isValid = false }

    const ilErr = validateLevel(form.initialLevel, 'Initial Level')
    if (ilErr) { errors.initialLevel = ilErr; errors.isValid = false }
  }

  return errors
}

// ─── 5.4 getAttachmentSummary ─────────────────────────────────────────────────

/**
 * Build a display summary for a list row from an AttachmentDraft.
 */
export function getAttachmentSummary(draft: AttachmentDraft, opts: SummaryOptions): AttachmentSummary {
  const lt       = draft.assetType.trim().toLowerCase()
  const isJson   = _isJsonObjType(draft.assetType)

  // Type label
  let typeLabel = draft.assetType
  if (lt === 'itemspecificasset') typeLabel = 'Item'
  else if (lt === 'ticket') typeLabel = 'Ticket'
  else if (lt === 'currency') typeLabel = 'Currency'
  else if (lt === 'item') typeLabel = 'Item (legacy)'

  // Asset ID and name
  let assetId   = ''
  let assetName = ''

  if (isJson) {
    const row = draft.itemRows?.[0]
    assetId   = row?.BlueprintId ?? ''
    const optList = lt === 'ticket' ? opts.ticketOptions : opts.itemOptions
    const found   = optList.find(o => o.id === assetId)
    assetName     = found?.label ?? assetId
  } else {
    assetId   = draft.payoutAssetId
    const optList = lt === 'currency' ? opts.currencyOptions
      : lt === 'item' ? opts.itemOptions
      : []
    const found   = optList.find(o => o.id === assetId)
    assetName     = found?.label ?? assetId
  }

  // Validity — derive form state inline to call validateAttachmentForm
  const formForValidation = draftToFormState(draft)
  const validity          = validateAttachmentForm(formForValidation)

  const chancePct = `${Math.round(draft.chance * 100)}%`

  return {
    typeLabel,
    assetName,
    assetId,
    amount:     draft.payoutAmount,
    chancePct,
    isValid:    validity.isValid,
    hasLegacy:  !!draft._legacyWarning,
    hasUnknown: !!draft._unknownIdWarning,
  }
}

// ─── 5.5 createDefaultDraft ──────────────────────────────────────────────────

export function createDefaultDraft(assetType = 'Currency'): AttachmentDraft {
  return {
    _id:           crypto.randomUUID(),
    payoutAssetId: '',
    assetType,
    payoutAmount:  1,
    chance:        1,
    itemRows:      [_defaultItemRow()],
  }
}

// ─── 5.6 draftToFormState ────────────────────────────────────────────────────

export function draftToFormState(draft: AttachmentDraft): AttachmentFormState {
  const row = draft.itemRows?.[0] ?? _defaultItemRow()
  return {
    _id:           draft._id ?? crypto.randomUUID(),
    assetType:     draft.assetType,
    payoutAssetId: draft.payoutAssetId,
    payoutAmount:  draft.payoutAmount,
    chance:        draft.chance,
    blueprintId:   row.BlueprintId,
    currentLevel:  row.CurrentLevel,
    rarity:        row.Rarity,
    initialLevel:  row.InitialLevel,
    fromSource:    row.FromSource,
    _legacyWarning:    draft._legacyWarning,
    _unknownIdWarning: draft._unknownIdWarning,
  }
}

// ─── Re-exports for use across modules ───────────────────────────────────────

export { RARITY_LABELS, Rarity }
export { _isJsonObjType }
