// src/modules/build-attachments.ts
// Pure serialization: AttachmentDraft[] → MailAttachment[] | null
// No DOM dependencies. Extracted from main.ts for testability.
// Zero behaviour change — verbatim copy of original buildAttachments().

import type { AttachmentDraft, MailAttachment, ItemSpecificAsset } from '../types'
import { Rarity } from '../types'

function _isItemSpecificAssetType(t: string): boolean {
  return t.trim().toLowerCase() === 'itemspecificasset'
}
function _isTicketType(t: string): boolean {
  return t.trim().toLowerCase() === 'ticket'
}
function _isJsonObjectType(t: string): boolean {
  return _isItemSpecificAssetType(t) || _isTicketType(t)
}
function _defaultItemRow(): ItemSpecificAsset {
  return { BlueprintId: '', CurrentLevel: 1, Rarity: Rarity.Common, InitialLevel: 1, FromSource: '' }
}

export function buildAttachments(drafts: AttachmentDraft[]): MailAttachment[] | null {
  const result: MailAttachment[] = []
  for (const d of drafts) {
    const isJsonObj = _isJsonObjectType(d.assetType)
    if (!isJsonObj && !d.payoutAssetId.trim()) continue
    if (d.payoutAmount <= 0) throw new Error(`Attachment: PayoutAmount must be > 0`)
    if (d.chance <= 0) throw new Error(`Attachment: Chance must be > 0`)

    let payoutAssetId: string
    if (isJsonObj) {
      const row = d.itemRows?.[0] ?? _defaultItemRow()
      payoutAssetId = JSON.stringify({
        BlueprintId:  row.BlueprintId,
        CurrentLevel: row.CurrentLevel,
        Rarity:       row.Rarity,
        InitialLevel: row.InitialLevel,
        FromSource:   row.FromSource,
      })
    } else {
      payoutAssetId = d.payoutAssetId.trim()
    }

    const typeStr = d.assetType.trim() || 'Currency'
    result.push({
      type:     typeStr,
      id:       payoutAssetId,
      itemId:   payoutAssetId,
      amount:   d.payoutAmount,
      quantity: d.payoutAmount,
      chance:   d.chance,
    })
  }
  return result.length > 0 ? result : null
}
