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
})
