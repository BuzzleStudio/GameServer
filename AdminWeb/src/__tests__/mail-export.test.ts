import { describe, it, expect } from 'vitest'
import { buildMailExportJson } from '../mail-export'
import type { MailRecord } from '../types'

const FIXED_NOW = '2024-06-01T00:00:00.000Z'

function makeGlobalMail(overrides: Partial<MailRecord> = {}): MailRecord {
  return {
    MessageId: 'mail-abc-123',
    MailInfo: {
      Title: 'Test Title',
      Content: 'Test Content',
      StartTime: '2024-01-01T00:00:00.000Z',
      ExpireTime: undefined,
      Attachment: [],
    },
    MailMetaData: {},
    ...overrides,
  }
}

describe('buildMailExportJson', () => {
  it('returns schemaVersion 1', () => {
    const result = buildMailExportJson(makeGlobalMail(), 'Global', 'production', FIXED_NOW)
    expect(result.schemaVersion).toBe(1)
  })

  it('preserves scope, sourceEnv, exportedAt', () => {
    const result = buildMailExportJson(makeGlobalMail(), 'Global-targeted', 'testing', FIXED_NOW)
    expect(result.scope).toBe('Global-targeted')
    expect(result.sourceEnv).toBe('testing')
    expect(result.exportedAt).toBe(FIXED_NOW)
  })

  it('moves messageId to top-level sourceMailId (not inside mail)', () => {
    const result = buildMailExportJson(makeGlobalMail(), 'Global', 'production', FIXED_NOW)
    expect(result.sourceMailId).toBe('mail-abc-123')
    expect((result.mail as Record<string, unknown>)['messageId']).toBeUndefined()
  })

  it('omits startTime from mail payload', () => {
    const result = buildMailExportJson(makeGlobalMail(), 'Global', 'production', FIXED_NOW)
    expect((result.mail as Record<string, unknown>)['startTime']).toBeUndefined()
  })

  it('extracts remaining mail fields correctly', () => {
    const result = buildMailExportJson(makeGlobalMail(), 'Global', 'production', FIXED_NOW)
    expect(result.mail.title).toBe('Test Title')
    expect(result.mail.content).toBe('Test Content')
    expect(result.mail.endTime).toBeNull()
    expect(result.mail.targetUserIds).toEqual([])
  })

  it('includes targetUserIds when present', () => {
    const mail = makeGlobalMail({ TargetUserIds: ['player-1', 'player-2'] })
    const result = buildMailExportJson(mail, 'Global-targeted', 'production', FIXED_NOW)
    expect(result.mail.targetUserIds).toEqual(['player-1', 'player-2'])
  })

  it('tolerates camelCase fields', () => {
    const mail: MailRecord = {
      messageId: 'mail-lower',
      mailInfo: { title: 'Lower', content: 'Body', startTime: '2024-01-01T00:00:00.000Z' },
    }
    const result = buildMailExportJson(mail, 'User', 'testing', FIXED_NOW)
    expect(result.sourceMailId).toBe('mail-lower')
    expect(result.mail.title).toBe('Lower')
    expect((result.mail as Record<string, unknown>)['startTime']).toBeUndefined()
  })

  it('includes attachment data', () => {
    const mail = makeGlobalMail()
    if (mail.MailInfo) {
      mail.MailInfo.Attachment = [{ AssetType: 'Currency', PayoutAssetId: 'gold', PayoutAmount: 100, Chance: 1 }]
    }
    const result = buildMailExportJson(mail, 'Global', 'production', FIXED_NOW)
    expect(result.mail.attachments).toHaveLength(1)
    expect(result.mail.attachments[0].AssetType).toBe('Currency')
  })

  // ─── SR-41: Ticket attachment export format ─────────────────────────────────
  // Ticket PayoutAssetId must be a JSON-stringified object (not plain string)
  // so that import/export round-trips correctly.

  it('[SR-41] Ticket attachment: PayoutAssetId is a JSON-stringified object in export', () => {
    const ticketPayoutId = JSON.stringify({
      BlueprintId: 'expedition_map_ticket_grass',
      CurrentLevel: 1,
      Rarity: 0,
      InitialLevel: 1,
      FromSource: '',
    })
    const mail = makeGlobalMail()
    if (mail.MailInfo) {
      mail.MailInfo.Attachment = [{ AssetType: 'Ticket', PayoutAssetId: ticketPayoutId, PayoutAmount: 1, Chance: 1 }]
    }
    const result = buildMailExportJson(mail, 'Global', 'production', FIXED_NOW)
    expect(result.mail.attachments).toHaveLength(1)
    const att = result.mail.attachments[0]
    // PayoutAssetId must be JSON string (not plain ticket ID)
    expect(typeof att.PayoutAssetId).toBe('string')
    const parsed = JSON.parse(att.PayoutAssetId as string) as Record<string, unknown>
    expect(parsed.BlueprintId).toBe('expedition_map_ticket_grass')
    expect(parsed).toHaveProperty('CurrentLevel')
    expect(parsed).toHaveProperty('Rarity')
  })

  it('[SR-41b] Ticket attachment: AssetType preserved as "Ticket"', () => {
    const ticketPayoutId = JSON.stringify({ BlueprintId: 'expedition_map_ticket_forest', CurrentLevel: 1, Rarity: 0, InitialLevel: 1, FromSource: '' })
    const mail = makeGlobalMail()
    if (mail.MailInfo) {
      mail.MailInfo.Attachment = [{ AssetType: 'Ticket', PayoutAssetId: ticketPayoutId, PayoutAmount: 1, Chance: 1 }]
    }
    const result = buildMailExportJson(mail, 'Global', 'production', FIXED_NOW)
    expect(result.mail.attachments[0].AssetType).toBe('Ticket')
  })

  // ─── SR-42: ISA attachment export format ────────────────────────────────────
  // ISA PayoutAssetId must also be a JSON-stringified object.

  it('[SR-42] ISA attachment: PayoutAssetId is a JSON-stringified object in export', () => {
    const isaPayoutId = JSON.stringify({
      BlueprintId: 'W_Dagger',
      CurrentLevel: 5,
      Rarity: 2,
      InitialLevel: 1,
      FromSource: 'loot',
    })
    const mail = makeGlobalMail()
    if (mail.MailInfo) {
      mail.MailInfo.Attachment = [{ AssetType: 'ItemSpecificAsset', PayoutAssetId: isaPayoutId, PayoutAmount: 1, Chance: 0.5 }]
    }
    const result = buildMailExportJson(mail, 'Global', 'production', FIXED_NOW)
    expect(result.mail.attachments).toHaveLength(1)
    const att = result.mail.attachments[0]
    const parsed = JSON.parse(att.PayoutAssetId as string) as Record<string, unknown>
    expect(parsed.BlueprintId).toBe('W_Dagger')
    expect(parsed.CurrentLevel).toBe(5)
    expect(parsed.Rarity).toBe(2)
  })

  it('[SR-42b] ISA attachment: AssetType preserved as "ItemSpecificAsset"', () => {
    const isaPayoutId = JSON.stringify({ BlueprintId: 'W_Bow', CurrentLevel: 1, Rarity: 0, InitialLevel: 1, FromSource: '' })
    const mail = makeGlobalMail()
    if (mail.MailInfo) {
      mail.MailInfo.Attachment = [{ AssetType: 'ItemSpecificAsset', PayoutAssetId: isaPayoutId, PayoutAmount: 1, Chance: 1 }]
    }
    const result = buildMailExportJson(mail, 'Global', 'production', FIXED_NOW)
    expect(result.mail.attachments[0].AssetType).toBe('ItemSpecificAsset')
  })
})
