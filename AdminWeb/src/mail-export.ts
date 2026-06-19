// ─── Mail export — pure logic, no DOM deps ───────────────────────────────────────
import type { MailRecord, MailExportJson, MailScope } from './types'
import {
  mailId,
  mailTitle,
  mailContent,
  mailStartTime,
  mailEndTime,
  mailTargetUsers,
  mailAttachments,
} from './types'

export type { MailScope }

/**
 * Build a schemaVersion:1 export JSON for a mail record.
 * @param mail     The mail record from the server.
 * @param scope    'Global', 'Global-targeted', or 'User'.
 * @param sourceEnv The env name/UUID the data came from.
 * @param exportedAt ISO timestamp string — caller provides so this fn stays pure.
 */
export function buildMailExportJson(
  mail: MailRecord,
  scope: MailScope,
  sourceEnv: string,
  exportedAt: string,
): MailExportJson {
  return {
    schemaVersion: 1,
    scope,
    sourceEnv,
    sourceMailId: mailId(mail),   // server-generated; top-level metadata only
    exportedAt,
    mail: {
      title: mailTitle(mail),
      content: mailContent(mail),
      // startTime omitted — server re-stamps; cannot safely round-trip
      endTime: mailEndTime(mail),
      targetUserIds: mailTargetUsers(mail),
      attachments: mailAttachments(mail),
    },
  }
}
