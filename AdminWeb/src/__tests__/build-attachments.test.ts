// build-attachments.test.ts — byte-compat proof for extracted buildAttachments()
// Verifies output is identical to the original main.ts implementation for all types.

import { describe, it, expect } from 'vitest'
import { buildAttachments } from '../modules/build-attachments'
import type { AttachmentDraft } from '../types'
import { Rarity } from '../types'

const BASE_ITEM_ROW = {
  BlueprintId:  '',
  CurrentLevel: 1,
  Rarity:       Rarity.Common,
  InitialLevel: 1,
  FromSource:   '',
}

// ─── Currency ─────────────────────────────────────────────────────────────────

describe('buildAttachments — Currency [byte-compat]', () => {
  it('plain string payoutAssetId', () => {
    const result = buildAttachments([{
      payoutAssetId: 'gem', assetType: 'Currency', payoutAmount: 10, chance: 1, itemRows: [BASE_ITEM_ROW],
    }])
    expect(result).toHaveLength(1)
    expect(result![0].id).toBe('gem')
    expect(result![0].itemId).toBe('gem')
    expect(result![0].type).toBe('Currency')
    expect(result![0].amount).toBe(10)
    expect(result![0].quantity).toBe(10)
    expect(result![0].chance).toBe(1)
  })

  it('id and itemId are identical strings', () => {
    const r = buildAttachments([{ payoutAssetId: 'gold', assetType: 'Currency', payoutAmount: 5, chance: 0.5, itemRows: [] }])
    expect(r![0].id).toBe(r![0].itemId)
  })
})

// ─── Item ─────────────────────────────────────────────────────────────────────

describe('buildAttachments — Item [byte-compat]', () => {
  it('plain string payoutAssetId', () => {
    const result = buildAttachments([{
      payoutAssetId: 'W_Dagger', assetType: 'Item', payoutAmount: 1, chance: 0.5, itemRows: [],
    }])
    expect(result![0].id).toBe('W_Dagger')
    expect(result![0].type).toBe('Item')
    expect(result![0].chance).toBeCloseTo(0.5)
  })
})

// ─── ItemSpecificAsset ────────────────────────────────────────────────────────

describe('buildAttachments — ItemSpecificAsset [byte-compat]', () => {
  const ISA_ROW = {
    BlueprintId:  'bp_sword',
    CurrentLevel: 5,
    Rarity:       Rarity.Epic,
    InitialLevel: 1,
    FromSource:   'chest',
  }

  it('serializes to JSON string with exact key order', () => {
    const result = buildAttachments([{
      payoutAssetId: '', assetType: 'ItemSpecificAsset', payoutAmount: 1, chance: 1, itemRows: [ISA_ROW],
    }])
    expect(result).toHaveLength(1)
    const expected = JSON.stringify({
      BlueprintId:  'bp_sword',
      CurrentLevel: 5,
      Rarity:       3,
      InitialLevel: 1,
      FromSource:   'chest',
    })
    expect(result![0].id).toBe(expected)
    expect(result![0].itemId).toBe(expected)
  })

  it('is NOT skipped even with empty BlueprintId', () => {
    const result = buildAttachments([{
      payoutAssetId: '', assetType: 'ItemSpecificAsset', payoutAmount: 1, chance: 1,
      itemRows: [{ ...BASE_ITEM_ROW, BlueprintId: '' }],
    }])
    expect(result).toHaveLength(1)
  })

  it('falls back to default row when itemRows empty', () => {
    const result = buildAttachments([{
      payoutAssetId: '', assetType: 'ItemSpecificAsset', payoutAmount: 1, chance: 1, itemRows: [],
    }])
    expect(result).toHaveLength(1)
    const parsed = JSON.parse(result![0].id)
    expect(parsed.BlueprintId).toBe('')
    expect(parsed.CurrentLevel).toBe(1)
    expect(parsed.Rarity).toBe(Rarity.Common)
  })
})

// ─── Ticket ───────────────────────────────────────────────────────────────────

describe('buildAttachments — Ticket [byte-compat]', () => {
  it('serializes to JSON string identical to ISA format', () => {
    const result = buildAttachments([{
      payoutAssetId: '', assetType: 'Ticket', payoutAmount: 1, chance: 1,
      itemRows: [{ BlueprintId: 'elite_ticket', CurrentLevel: 1, Rarity: Rarity.None, InitialLevel: 1, FromSource: '' }],
    }])
    const expected = JSON.stringify({ BlueprintId: 'elite_ticket', CurrentLevel: 1, Rarity: 0, InitialLevel: 1, FromSource: '' })
    expect(result![0].id).toBe(expected)
    expect(result![0].type).toBe('Ticket')
  })
})

// ─── Skip / edge cases ────────────────────────────────────────────────────────

describe('buildAttachments — skip and edge cases', () => {
  it('skips Currency with empty payoutAssetId', () => {
    const result = buildAttachments([{
      payoutAssetId: '', assetType: 'Currency', payoutAmount: 1, chance: 1, itemRows: [],
    }])
    expect(result).toBeNull()
  })

  it('skips Item with whitespace-only payoutAssetId', () => {
    const result = buildAttachments([{
      payoutAssetId: '   ', assetType: 'Item', payoutAmount: 1, chance: 1, itemRows: [],
    }])
    expect(result).toBeNull()
  })

  it('returns null when all drafts skipped', () => {
    expect(buildAttachments([])).toBeNull()
    expect(buildAttachments([{ payoutAssetId: '', assetType: 'Currency', payoutAmount: 1, chance: 1, itemRows: [] }])).toBeNull()
  })

  it('throws on payoutAmount <= 0', () => {
    expect(() => buildAttachments([{
      payoutAssetId: 'gem', assetType: 'Currency', payoutAmount: 0, chance: 1, itemRows: [],
    }])).toThrow('PayoutAmount must be > 0')
  })

  it('throws on chance <= 0', () => {
    expect(() => buildAttachments([{
      payoutAssetId: 'gem', assetType: 'Currency', payoutAmount: 1, chance: 0, itemRows: [],
    }])).toThrow('Chance must be > 0')
  })

  it('trims whitespace from payoutAssetId for Currency/Item', () => {
    const result = buildAttachments([{
      payoutAssetId: '  gem  ', assetType: 'Currency', payoutAmount: 1, chance: 1, itemRows: [],
    }])
    expect(result![0].id).toBe('gem')
  })

  it('defaulted assetType empty string → "Currency"', () => {
    const result = buildAttachments([{
      payoutAssetId: 'gem', assetType: '  ', payoutAmount: 1, chance: 1, itemRows: [],
    }])
    expect(result![0].type).toBe('Currency')
  })
})
