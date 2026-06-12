import { describe, it, expect } from 'vitest'
import { validateAndImport } from '../mail-import'

const KNOWN_CURRENCIES = ['gem', 'gold', 'mine_1']
const KNOWN_ITEMS = ['W_Dagger', 'W_Bow', 'E_SlashArmor']
const KNOWN_TICKETS = ['expedition_map_ticket_grass', 'expedition_map_ticket_forest']

function makeParsed(overrides: Record<string, unknown> = {}): unknown {
  return {
    schemaVersion: 1,
    scope: 'Global',
    sourceEnv: 'production',
    exportedAt: '2024-06-01T00:00:00.000Z',
    mail: {
      messageId: 'mail-123',
      title: 'Hello',
      content: 'World',
      endTime: null,
      targetUserIds: [],
      attachments: [],
    },
    ...overrides,
  }
}

describe('validateAndImport', () => {
  describe('size limit', () => {
    it('rejects payloads exceeding maxBytes', () => {
      const bigStr = 'x'.repeat(260 * 1024)
      const result = validateAndImport({}, KNOWN_CURRENCIES, KNOWN_ITEMS, KNOWN_TICKETS, bigStr, 256 * 1024)
      expect(result.ok).toBe(false)
      expect(result.errors[0]).toContain('too large')
    })
  })

  describe('top-level validation', () => {
    it('rejects non-object', () => {
      const result = validateAndImport([1, 2], KNOWN_CURRENCIES, KNOWN_ITEMS, KNOWN_TICKETS)
      expect(result.ok).toBe(false)
      expect(result.errors[0]).toContain('JSON object')
    })

    it('rejects null', () => {
      const result = validateAndImport(null, KNOWN_CURRENCIES, KNOWN_ITEMS, KNOWN_TICKETS)
      expect(result.ok).toBe(false)
    })

    it('rejects wrong schemaVersion', () => {
      const result = validateAndImport(makeParsed({ schemaVersion: 2 }), KNOWN_CURRENCIES, KNOWN_ITEMS, KNOWN_TICKETS)
      expect(result.ok).toBe(false)
      expect(result.errors.some(e => e.includes('schemaVersion'))).toBe(true)
    })

    it('rejects missing mail field', () => {
      const parsed = { schemaVersion: 1 }
      const result = validateAndImport(parsed, KNOWN_CURRENCIES, KNOWN_ITEMS, KNOWN_TICKETS)
      expect(result.ok).toBe(false)
      expect(result.errors.some(e => e.includes('"mail"'))).toBe(true)
    })
  })

  describe('prototype-pollution safety', () => {
    it('rejects __proto__ in root', () => {
      // Use Object.create(null) and add key via defineProperty to avoid actual pollution
      const evil: Record<string, unknown> = Object.create(null) as Record<string, unknown>
      Object.defineProperty(evil, '__proto__', { value: {}, enumerable: true })
      evil['schemaVersion'] = 1
      evil['mail'] = { title: 'x', content: 'y', attachments: [] }
      const result = validateAndImport(evil, KNOWN_CURRENCIES, KNOWN_ITEMS, KNOWN_TICKETS)
      expect(result.ok).toBe(false)
      expect(result.errors[0]).toContain('forbidden key')
    })

    it('rejects constructor in mail', () => {
      const evil: Record<string, unknown> = { schemaVersion: 1, mail: Object.create(null) as Record<string, unknown> }
      Object.defineProperty(evil['mail'] as object, 'constructor', { value: {}, enumerable: true })
      ;(evil['mail'] as Record<string, unknown>)['title'] = 'x'
      ;(evil['mail'] as Record<string, unknown>)['content'] = 'y'
      ;(evil['mail'] as Record<string, unknown>)['attachments'] = []
      const result = validateAndImport(evil, KNOWN_CURRENCIES, KNOWN_ITEMS, KNOWN_TICKETS)
      expect(result.ok).toBe(false)
      expect(result.errors[0]).toContain('forbidden key')
    })
  })

  describe('happy path', () => {
    it('extracts title, content, endTime, targetUserIds', () => {
      const parsed = makeParsed({
        mail: {
          title: 'My Mail',
          content: 'Hello',
          endTime: '2024-12-31T00:00:00.000Z',
          targetUserIds: ['user-a'],
          attachments: [],
        },
      })
      const result = validateAndImport(parsed, KNOWN_CURRENCIES, KNOWN_ITEMS, KNOWN_TICKETS)
      expect(result.ok).toBe(true)
      expect(result.draft?.title).toBe('My Mail')
      expect(result.draft?.content).toBe('Hello')
      expect(result.draft?.endTime).toBe('2024-12-31T00:00:00.000Z')
      expect(result.draft?.targetUserIds).toEqual(['user-a'])
    })

    it('extracts Currency attachment without warning', () => {
      const parsed = makeParsed({
        mail: {
          title: 'T', content: 'C', endTime: null, targetUserIds: [],
          attachments: [{ AssetType: 'Currency', PayoutAssetId: 'gold', PayoutAmount: 50, Chance: 1 }],
        },
      })
      const result = validateAndImport(parsed, KNOWN_CURRENCIES, KNOWN_ITEMS, KNOWN_TICKETS)
      expect(result.ok).toBe(true)
      expect(result.warnings).toHaveLength(0)
      expect(result.draft?.attachments[0].payoutAssetId).toBe('gold')
      expect(result.draft?.attachments[0].payoutAmount).toBe(50)
    })
  })

  describe('unknown ID warnings', () => {
    it('warns for unknown Currency ID', () => {
      const parsed = makeParsed({
        mail: {
          title: 'T', content: 'C', endTime: null, targetUserIds: [],
          attachments: [{ AssetType: 'Currency', PayoutAssetId: 'dragon_scale', PayoutAmount: 1, Chance: 1 }],
        },
      })
      const result = validateAndImport(parsed, KNOWN_CURRENCIES, KNOWN_ITEMS, KNOWN_TICKETS)
      expect(result.ok).toBe(true)
      expect(result.warnings.some(w => w.includes('dragon_scale'))).toBe(true)
      expect(result.draft?.attachments[0]._unknownIdWarning).toBeDefined()
    })

    it('warns for unknown Item ID', () => {
      const parsed = makeParsed({
        mail: {
          title: 'T', content: 'C', endTime: null, targetUserIds: [],
          attachments: [{ AssetType: 'Item', PayoutAssetId: 'W_Unknown', PayoutAmount: 1, Chance: 1 }],
        },
      })
      const result = validateAndImport(parsed, KNOWN_CURRENCIES, KNOWN_ITEMS, KNOWN_TICKETS)
      expect(result.ok).toBe(true)
      expect(result.warnings.some(w => w.includes('W_Unknown'))).toBe(true)
    })

    it('warns for unknown Ticket ID (plain string)', () => {
      const parsed = makeParsed({
        mail: {
          title: 'T', content: 'C', endTime: null, targetUserIds: [],
          attachments: [{ AssetType: 'Ticket', PayoutAssetId: 'unknown_ticket', PayoutAmount: 1, Chance: 1 }],
        },
      })
      const result = validateAndImport(parsed, KNOWN_CURRENCIES, KNOWN_ITEMS, KNOWN_TICKETS)
      expect(result.ok).toBe(true)
      expect(result.warnings.some(w => w.includes('unknown_ticket'))).toBe(true)
    })

    it('no warning for known Ticket ID (plain string)', () => {
      const parsed = makeParsed({
        mail: {
          title: 'T', content: 'C', endTime: null, targetUserIds: [],
          attachments: [{ AssetType: 'Ticket', PayoutAssetId: 'expedition_map_ticket_grass', PayoutAmount: 1, Chance: 1 }],
        },
      })
      const result = validateAndImport(parsed, KNOWN_CURRENCIES, KNOWN_ITEMS, KNOWN_TICKETS)
      expect(result.ok).toBe(true)
      const ticketWarnings = result.warnings.filter(w => w.toLowerCase().includes('ticket'))
      expect(ticketWarnings).toHaveLength(0)
    })

    it('extracts Ticket BlueprintId from JSON-object PayoutAssetId', () => {
      const ticketJson = JSON.stringify({ BlueprintId: 'expedition_map_ticket_grass', CurrentLevel: 1, Rarity: 0, InitialLevel: 1, FromSource: '' })
      const parsed = makeParsed({
        mail: {
          title: 'T', content: 'C', endTime: null, targetUserIds: [],
          attachments: [{ AssetType: 'Ticket', PayoutAssetId: ticketJson, PayoutAmount: 1, Chance: 1 }],
        },
      })
      const result = validateAndImport(parsed, KNOWN_CURRENCIES, KNOWN_ITEMS, KNOWN_TICKETS)
      expect(result.ok).toBe(true)
      const ticketWarnings = result.warnings.filter(w => w.toLowerCase().includes('ticket'))
      expect(ticketWarnings).toHaveLength(0)
      expect(result.draft?.attachments[0].itemRows[0].BlueprintId).toBe('expedition_map_ticket_grass')
    })
  })

  describe('attachment safety', () => {
    it('skips non-object attachments with warning', () => {
      const parsed = makeParsed({
        mail: {
          title: 'T', content: 'C', endTime: null, targetUserIds: [],
          attachments: ['bad', null, { AssetType: 'Currency', PayoutAssetId: 'gold', PayoutAmount: 1, Chance: 1 }],
        },
      })
      const result = validateAndImport(parsed, KNOWN_CURRENCIES, KNOWN_ITEMS, KNOWN_TICKETS)
      expect(result.ok).toBe(true)
      expect(result.draft?.attachments).toHaveLength(1)
      expect(result.warnings).toHaveLength(2)
    })

    it('NEVER copies targetUserIds to its own protected set', () => {
      // targetUserIds must be extracted as plain strings only (never as objects/arrays of objects)
      const parsed = makeParsed({
        mail: {
          title: 'T', content: 'C', endTime: null,
          targetUserIds: ['user-a', 42, null, 'user-b'],  // mixed types
          attachments: [],
        },
      })
      const result = validateAndImport(parsed, KNOWN_CURRENCIES, KNOWN_ITEMS, KNOWN_TICKETS)
      expect(result.ok).toBe(true)
      // Only string values pass through
      expect(result.draft?.targetUserIds).toEqual(['user-a', 'user-b'])
    })
  })

  // ─── SR-40: copy-paste round-trip ──────────────────────────────────────────
  // Simulate user copy-pasting the export JSON back in through the import path.
  // No dependency on buildAttachments — construct the export-shaped JSON directly.

  describe('copy-paste round-trip (SR-40)', () => {
    it('[SR-40] Currency attachment survives export-format round-trip', () => {
      const exportShaped = {
        schemaVersion: 1,
        scope: 'Global',
        sourceEnv: 'production',
        exportedAt: '2024-06-01T00:00:00.000Z',
        mail: {
          messageId: 'mail-001',
          title: 'Round-trip Test',
          content: 'Body text',
          endTime: null,
          targetUserIds: [],
          attachments: [
            { AssetType: 'Currency', PayoutAssetId: 'gold', PayoutAmount: 200, Chance: 0.5 },
          ],
        },
      }
      const result = validateAndImport(exportShaped, KNOWN_CURRENCIES, KNOWN_ITEMS, KNOWN_TICKETS)
      expect(result.ok).toBe(true)
      expect(result.errors).toHaveLength(0)
      expect(result.draft?.title).toBe('Round-trip Test')
      expect(result.draft?.attachments[0].payoutAssetId).toBe('gold')
      expect(result.draft?.attachments[0].payoutAmount).toBe(200)
      expect(result.draft?.attachments[0].chance).toBe(0.5)
    })

    it('[SR-40b] Ticket JSON-object attachment survives round-trip with itemRows intact', () => {
      const ticketJson = JSON.stringify({
        BlueprintId: 'expedition_map_ticket_forest',
        CurrentLevel: 2,
        Rarity: 1,
        InitialLevel: 1,
        FromSource: 'quest',
      })
      const exportShaped = {
        schemaVersion: 1,
        scope: 'Global',
        sourceEnv: 'testing',
        exportedAt: '2024-06-01T00:00:00.000Z',
        mail: {
          title: 'Ticket RT',
          content: 'Body',
          endTime: null,
          targetUserIds: [],
          attachments: [
            { AssetType: 'Ticket', PayoutAssetId: ticketJson, PayoutAmount: 1, Chance: 1 },
          ],
        },
      }
      const result = validateAndImport(exportShaped, KNOWN_CURRENCIES, KNOWN_ITEMS, KNOWN_TICKETS)
      expect(result.ok).toBe(true)
      const row = result.draft?.attachments[0].itemRows[0]
      expect(row?.BlueprintId).toBe('expedition_map_ticket_forest')
      expect(row?.CurrentLevel).toBe(2)
      expect(row?.Rarity).toBe(1)
    })
  })

  // ─── SR-52: ISA JSON-object PayoutAssetId extraction ──────────────────────

  describe('ISA JSON-object PayoutAssetId extraction (SR-52)', () => {
    it('[SR-52] ISA with JSON-object PayoutAssetId: BlueprintId extracted correctly', () => {
      const isaJson = JSON.stringify({
        BlueprintId: 'W_Dagger',
        CurrentLevel: 3,
        Rarity: 1,
        InitialLevel: 2,
        FromSource: 'quest',
      })
      const parsed = makeParsed({
        mail: {
          title: 'T', content: 'C', endTime: null, targetUserIds: [],
          attachments: [{ AssetType: 'ItemSpecificAsset', PayoutAssetId: isaJson, PayoutAmount: 1, Chance: 1 }],
        },
      })
      const result = validateAndImport(parsed, KNOWN_CURRENCIES, KNOWN_ITEMS, KNOWN_TICKETS)
      expect(result.ok).toBe(true)
      const row = result.draft?.attachments[0].itemRows[0]
      expect(row?.BlueprintId).toBe('W_Dagger')
      expect(row?.CurrentLevel).toBe(3)
      expect(row?.InitialLevel).toBe(2)
      expect(row?.FromSource).toBe('quest')
    })

    it('[SR-52b] ISA with JSON-object PayoutAssetId: payoutAssetId set to empty string', () => {
      const isaJson = JSON.stringify({ BlueprintId: 'W_Bow', CurrentLevel: 1, Rarity: 0, InitialLevel: 1, FromSource: '' })
      const parsed = makeParsed({
        mail: {
          title: 'T', content: 'C', endTime: null, targetUserIds: [],
          attachments: [{ AssetType: 'ItemSpecificAsset', PayoutAssetId: isaJson, PayoutAmount: 1, Chance: 1 }],
        },
      })
      const result = validateAndImport(parsed, KNOWN_CURRENCIES, KNOWN_ITEMS, KNOWN_TICKETS)
      expect(result.ok).toBe(true)
      expect(result.draft?.attachments[0].payoutAssetId).toBe('')
    })
  })

  // ─── SR-50/SR-51: _legacyWarning set on plain-string Ticket/ISA ───────────
  //
  // NOTE: These tests INTENTIONALLY FAIL with current mail-import.ts.
  // validateAndImport never sets `_legacyWarning` (confirmed implementation gap).
  // These tests document the requirement. They will pass after the fix lands.

  describe('_legacyWarning set for plain-string Ticket/ISA (SR-50/SR-51) [FAILS until fix]', () => {
    it('[SR-50] Ticket with plain-string PayoutAssetId: _legacyWarning is set on draft', () => {
      const parsed = makeParsed({
        mail: {
          title: 'T', content: 'C', endTime: null, targetUserIds: [],
          attachments: [{ AssetType: 'Ticket', PayoutAssetId: 'expedition_map_ticket_grass', PayoutAmount: 1, Chance: 1 }],
        },
      })
      const result = validateAndImport(parsed, KNOWN_CURRENCIES, KNOWN_ITEMS, KNOWN_TICKETS)
      expect(result.ok).toBe(true)
      // Currently FAILS: _legacyWarning is never set by validateAndImport
      expect(result.draft?.attachments[0]._legacyWarning).toBeDefined()
    })

    it('[SR-51] ISA with plain-string PayoutAssetId: _legacyWarning is set on draft', () => {
      const parsed = makeParsed({
        mail: {
          title: 'T', content: 'C', endTime: null, targetUserIds: [],
          attachments: [{ AssetType: 'ItemSpecificAsset', PayoutAssetId: 'some_legacy_id', PayoutAmount: 1, Chance: 1 }],
        },
      })
      const result = validateAndImport(parsed, KNOWN_CURRENCIES, KNOWN_ITEMS, KNOWN_TICKETS)
      expect(result.ok).toBe(true)
      // Currently FAILS: _legacyWarning is never set by validateAndImport
      expect(result.draft?.attachments[0]._legacyWarning).toBeDefined()
    })
  })
})
