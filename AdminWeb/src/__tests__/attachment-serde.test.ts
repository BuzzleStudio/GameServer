// attachment-serde.test.ts — pure serde/validation/summary functions

import { describe, it, expect } from 'vitest'
import {
  deserializeAttachmentToForm,
  serializeAttachmentForm,
  validateAttachmentForm,
  getAttachmentSummary,
  createDefaultDraft,
  draftToFormState,
  _defaultItemRow,
} from '../modules/attachment-serde'
import { buildAttachments } from '../modules/build-attachments'
import { Rarity } from '../types'
import type { AttachmentFormState } from '../modules/attachment-serde'
import type { MailAttachmentInfo } from '../types'

const EMPTY_OPTS = { currencyOptions: [], itemOptions: [], ticketOptions: [] }

const BASE_ROW = _defaultItemRow()

// ─── deserializeAttachmentToForm ─────────────────────────────────────────────

describe('deserializeAttachmentToForm', () => {
  it('Currency: plain payoutAssetId round-trips', () => {
    const info: MailAttachmentInfo = {
      AssetType: 'Currency', PayoutAssetId: 'gem', PayoutAmount: 5, Chance: 0.5,
    }
    const draft = deserializeAttachmentToForm(info)
    expect(draft.assetType).toBe('Currency')
    expect(draft.payoutAssetId).toBe('gem')
    expect(draft.payoutAmount).toBe(5)
    expect(draft.chance).toBe(0.5)
    expect(draft._id).toBeTruthy()
    expect(draft.itemRows).toHaveLength(1)
    expect(draft._legacyWarning).toBeUndefined()
  })

  it('ItemSpecificAsset: parses JSON PayoutAssetId to itemRows', () => {
    const isaPayload = JSON.stringify([{
      BlueprintId: 'sword_01', CurrentLevel: 3, Rarity: 2, InitialLevel: 1, FromSource: 'loot',
    }])
    const info: MailAttachmentInfo = {
      AssetType: 'ItemSpecificAsset', PayoutAssetId: isaPayload, PayoutAmount: 1, Chance: 1,
    }
    const draft = deserializeAttachmentToForm(info)
    expect(draft.assetType).toBe('ItemSpecificAsset')
    expect(draft.payoutAssetId).toBe('')
    expect(draft.itemRows[0].BlueprintId).toBe('sword_01')
    expect(draft.itemRows[0].CurrentLevel).toBe(3)
    expect(draft.itemRows[0].Rarity).toBe(2)
    expect(draft.itemRows[0].FromSource).toBe('loot')
    expect(draft._legacyWarning).toBeUndefined()
  })

  it('Ticket: parses object PayoutAssetId', () => {
    const ticketPayload = JSON.stringify({ BlueprintId: 'raid_ticket', CurrentLevel: 1, Rarity: 0, InitialLevel: 1, FromSource: '' })
    const info: MailAttachmentInfo = {
      AssetType: 'Ticket', PayoutAssetId: ticketPayload, PayoutAmount: 2, Chance: 1,
    }
    const draft = deserializeAttachmentToForm(info)
    expect(draft.assetType).toBe('Ticket')
    expect(draft.itemRows[0].BlueprintId).toBe('raid_ticket')
    expect(draft._legacyWarning).toBeUndefined()
  })

  it('ItemSpecificAsset: legacy plain-string sets _legacyWarning', () => {
    const info: MailAttachmentInfo = {
      AssetType: 'ItemSpecificAsset', PayoutAssetId: 'not_json', PayoutAmount: 1, Chance: 1,
    }
    const draft = deserializeAttachmentToForm(info)
    expect(draft._legacyWarning).toBeTruthy()
    expect(draft._legacyWarning).toContain('legacy format')
    expect(draft.itemRows[0].BlueprintId).toBe('not_json')
  })

  it('each call produces unique _id', () => {
    const info: MailAttachmentInfo = { AssetType: 'Currency', PayoutAssetId: 'gem', PayoutAmount: 1, Chance: 1 }
    const a = deserializeAttachmentToForm(info)
    const b = deserializeAttachmentToForm(info)
    expect(a._id).not.toBe(b._id)
  })

  it('camelCase field aliases work', () => {
    const info: MailAttachmentInfo = {
      assetType: 'Currency', payoutAssetId: 'gold', payoutAmount: 10, chance: 0.25,
    }
    const draft = deserializeAttachmentToForm(info)
    expect(draft.payoutAssetId).toBe('gold')
    expect(draft.payoutAmount).toBe(10)
    expect(draft.chance).toBe(0.25)
  })
})

// ─── serializeAttachmentForm ──────────────────────────────────────────────────

describe('serializeAttachmentForm', () => {
  it('Currency: preserves _id, sets payoutAssetId', () => {
    const form: AttachmentFormState = {
      _id: 'test-id', assetType: 'Currency', payoutAssetId: 'gem',
      payoutAmount: 5, chance: 1,
      blueprintId: '', currentLevel: 1, rarity: Rarity.Common, initialLevel: 1, fromSource: '',
    }
    const draft = serializeAttachmentForm(form)
    expect(draft._id).toBe('test-id')
    expect(draft.payoutAssetId).toBe('gem')
    expect(draft.assetType).toBe('Currency')
    expect(draft.payoutAmount).toBe(5)
    expect(draft.itemRows[0]).toEqual(BASE_ROW)
  })

  it('ItemSpecificAsset: clears payoutAssetId, fills itemRows', () => {
    const form: AttachmentFormState = {
      _id: 'isa-id', assetType: 'ItemSpecificAsset', payoutAssetId: 'ignored',
      payoutAmount: 1, chance: 1,
      blueprintId: 'sword_01', currentLevel: 3, rarity: Rarity.Rare, initialLevel: 1, fromSource: 'loot',
    }
    const draft = serializeAttachmentForm(form)
    expect(draft.payoutAssetId).toBe('')
    expect(draft.itemRows[0].BlueprintId).toBe('sword_01')
    expect(draft.itemRows[0].CurrentLevel).toBe(3)
    expect(draft.itemRows[0].Rarity).toBe(Rarity.Rare)
    expect(draft.itemRows[0].FromSource).toBe('loot')
  })

  it('payoutAssetId is trimmed', () => {
    const form: AttachmentFormState = {
      _id: 'x', assetType: 'Currency', payoutAssetId: '  gem  ',
      payoutAmount: 1, chance: 1,
      blueprintId: '', currentLevel: 1, rarity: Rarity.Common, initialLevel: 1, fromSource: '',
    }
    expect(serializeAttachmentForm(form).payoutAssetId).toBe('gem')
  })
})

// ─── draftToFormState ─────────────────────────────────────────────────────────

describe('draftToFormState', () => {
  it('Currency: maps fields correctly', () => {
    const draft = createDefaultDraft('Currency')
    draft.payoutAssetId = 'gem'
    draft.payoutAmount  = 7
    const form = draftToFormState(draft)
    expect(form._id).toBe(draft._id)
    expect(form.assetType).toBe('Currency')
    expect(form.payoutAssetId).toBe('gem')
    expect(form.payoutAmount).toBe(7)
    expect(form.blueprintId).toBe('')
  })

  it('ISA: itemRows[0] mapped to flat fields', () => {
    const info: MailAttachmentInfo = {
      AssetType: 'ItemSpecificAsset',
      PayoutAssetId: JSON.stringify([{ BlueprintId: 'axe', CurrentLevel: 2, Rarity: 1, InitialLevel: 1, FromSource: '' }]),
      PayoutAmount: 1, Chance: 1,
    }
    const draft = deserializeAttachmentToForm(info)
    const form  = draftToFormState(draft)
    expect(form.blueprintId).toBe('axe')
    expect(form.currentLevel).toBe(2)
    expect(form.rarity).toBe(1)
  })

  it('missing _id generates new UUID', () => {
    const draft = createDefaultDraft()
    const noId: typeof draft = { ...draft, _id: undefined }
    const form = draftToFormState(noId)
    expect(form._id).toBeTruthy()
    expect(form._id).not.toBe('')
  })
})

// ─── validateAttachmentForm ──────────────────────────────────────────────────

describe('validateAttachmentForm', () => {
  function makeForm(overrides: Partial<AttachmentFormState> = {}): AttachmentFormState {
    return {
      _id: 'x', assetType: 'Currency', payoutAssetId: 'gem',
      payoutAmount: 1, chance: 1,
      blueprintId: '', currentLevel: 1, rarity: Rarity.Common, initialLevel: 1, fromSource: '',
      ...overrides,
    }
  }

  it('valid Currency form returns isValid=true', () => {
    expect(validateAttachmentForm(makeForm()).isValid).toBe(true)
  })

  it('empty assetType is invalid', () => {
    const r = validateAttachmentForm(makeForm({ assetType: '' }))
    expect(r.isValid).toBe(false)
    expect(r.assetType).toBeTruthy()
  })

  it('empty payoutAssetId (Currency) is invalid', () => {
    const r = validateAttachmentForm(makeForm({ payoutAssetId: '' }))
    expect(r.isValid).toBe(false)
    expect(r.payoutAssetId).toBeTruthy()
  })

  it('payoutAssetId not validated for ISA', () => {
    const form = makeForm({ assetType: 'ItemSpecificAsset', payoutAssetId: '',
      blueprintId: 'sword_01' })
    const r = validateAttachmentForm(form)
    expect(r.payoutAssetId).toBeFalsy()
  })

  it('blueprintId required for ISA', () => {
    const form = makeForm({ assetType: 'ItemSpecificAsset', payoutAssetId: '', blueprintId: '' })
    const r = validateAttachmentForm(form)
    expect(r.isValid).toBe(false)
    expect(r.blueprintId).toBeTruthy()
  })

  it('amount 0 is invalid', () => {
    const r = validateAttachmentForm(makeForm({ payoutAmount: 0 }))
    expect(r.isValid).toBe(false)
    expect(r.payoutAmount).toBeTruthy()
  })

  it('chance 0 is invalid', () => {
    const r = validateAttachmentForm(makeForm({ chance: 0 }))
    expect(r.isValid).toBe(false)
    expect(r.chance).toBeTruthy()
  })

  it('chance > 1 is invalid', () => {
    const r = validateAttachmentForm(makeForm({ chance: 1.5 }))
    expect(r.isValid).toBe(false)
  })
})

// ─── getAttachmentSummary ─────────────────────────────────────────────────────

describe('getAttachmentSummary', () => {
  it('Currency: typeLabel and resolved name from options', () => {
    const draft = { ...createDefaultDraft('Currency'), payoutAssetId: 'gem' }
    const summary = getAttachmentSummary(draft, {
      currencyOptions: [{ id: 'gem', label: 'Gem' }],
      itemOptions: [], ticketOptions: [],
    })
    expect(summary.typeLabel).toBe('Currency')
    expect(summary.assetName).toBe('Gem')
    expect(summary.assetId).toBe('gem')
    expect(summary.amount).toBe(1)
  })

  it('unknown currency ID: assetName falls back to id', () => {
    const draft = { ...createDefaultDraft('Currency'), payoutAssetId: 'unknown_id' }
    const s = getAttachmentSummary(draft, EMPTY_OPTS)
    expect(s.assetName).toBe('unknown_id')
  })

  it('ISA: typeLabel = "Item", assetId from itemRows[0].BlueprintId', () => {
    const info: MailAttachmentInfo = {
      AssetType: 'ItemSpecificAsset',
      PayoutAssetId: JSON.stringify([{ BlueprintId: 'sword_01', CurrentLevel: 1, Rarity: 0, InitialLevel: 1, FromSource: '' }]),
      PayoutAmount: 1, Chance: 1,
    }
    const draft = deserializeAttachmentToForm(info)
    const s = getAttachmentSummary(draft, EMPTY_OPTS)
    expect(s.typeLabel).toBe('Item')
    expect(s.assetId).toBe('sword_01')
  })

  it('chancePct formatted as percentage', () => {
    const draft = { ...createDefaultDraft('Currency'), payoutAssetId: 'gem', chance: 0.25 }
    expect(getAttachmentSummary(draft, EMPTY_OPTS).chancePct).toBe('25%')
  })

  it('invalid form: isValid=false', () => {
    const draft = { ...createDefaultDraft('Currency'), payoutAssetId: '' }
    expect(getAttachmentSummary(draft, EMPTY_OPTS).isValid).toBe(false)
  })

  it('legacy warning sets hasLegacy', () => {
    const draft = { ...createDefaultDraft('Currency'), payoutAssetId: 'gem', _legacyWarning: 'old' }
    expect(getAttachmentSummary(draft, EMPTY_OPTS).hasLegacy).toBe(true)
  })
})

// ─── createDefaultDraft ───────────────────────────────────────────────────────

describe('createDefaultDraft', () => {
  it('creates valid Currency draft', () => {
    const d = createDefaultDraft('Currency')
    expect(d.assetType).toBe('Currency')
    expect(d.payoutAmount).toBe(1)
    expect(d.chance).toBe(1)
    expect(d._id).toBeTruthy()
    expect(d.itemRows).toHaveLength(1)
  })

  it('each call produces unique _id', () => {
    const a = createDefaultDraft()
    const b = createDefaultDraft()
    expect(a._id).not.toBe(b._id)
  })

  it('defaults to Currency when type omitted', () => {
    expect(createDefaultDraft().assetType).toBe('Currency')
  })
})

// ─── PAYLOAD PURITY: _id never serialized ────────────────────────────────────

describe('Payload purity: _id never appears in buildAttachments output', () => {
  it('Currency draft with _id — wire payload clean', () => {
    const draft = { ...createDefaultDraft('Currency'), payoutAssetId: 'gem', payoutAmount: 3, chance: 0.5 }
    const result = buildAttachments([draft])
    const wire = JSON.stringify(result)
    expect(wire).not.toContain('_id')
    expect(wire).not.toContain('_legacyWarning')
    expect(wire).not.toContain('_unknownIdWarning')
  })

  it('ISA draft with _id — wire payload clean', () => {
    const info: MailAttachmentInfo = {
      AssetType: 'ItemSpecificAsset',
      PayoutAssetId: JSON.stringify([{ BlueprintId: 'axe', CurrentLevel: 2, Rarity: 1, InitialLevel: 1, FromSource: '' }]),
      PayoutAmount: 1, Chance: 1,
    }
    const draft = deserializeAttachmentToForm(info)
    const result = buildAttachments([draft])
    const wire = JSON.stringify(result)
    expect(wire).not.toContain('_id')
  })

  it('multiple drafts — none contain _id in wire', () => {
    const drafts = [
      { ...createDefaultDraft('Currency'), payoutAssetId: 'gem' },
      { ...createDefaultDraft('Currency'), payoutAssetId: 'gold' },
    ]
    const result = buildAttachments(drafts)
    expect(JSON.stringify(result)).not.toContain('_id')
  })
})
