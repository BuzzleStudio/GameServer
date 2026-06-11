// src/modules/mail-editor-drawer.ts
// Slide-in drawer for viewing and editing a single mail record.
// Appended to document.body — persists across refreshMainPanel().

import {
  renderScheduleEditor,
  readScheduleEditor,
  attachScheduleListeners,
} from './schedule-editor'
import {
  renderTargetUserEditor,
  readTargetUserEditor,
  attachTargetUserListeners,
} from './target-user-editor'
import {
  mountAttachmentEditor,
} from './attachment-editor'
import type { AttachmentEditorHandle } from './attachment-editor'
import type { ComboboxOption } from './asset-selector'
import { createUnsavedGuard } from './unsaved-guard'
import { isoToEditInputs, formatDateUtc } from './date-format'
import { deriveMailStatus, statusBadgeHtml } from './status'
import {
  mailId,
  mailTitle,
  mailContent,
  mailStartTime,
  mailEndTime,
  mailTargetUsers,
  mailAttachments,
} from '../types'
import type { MailRecord, AttachmentDraft, MailAttachmentInfo, ItemSpecificAsset, RarityValue } from '../types'
import { Rarity } from '../types'

// ─── Types ────────────────────────────────────────────────────────────────────

export interface DrawerDeps {
  getEnv():      string
  isBusy():      boolean
  isConnected(): boolean
  currencyOptions: ComboboxOption[]
  itemOptions:     ComboboxOption[]
  ticketOptions:   ComboboxOption[]
  onSave(
    mailId:        string,
    subject:       string,
    body:          string,
    attachments:   AttachmentDraft[],
    targetUserIds: string[] | null,
    expiresAt:     string | null,
  ): void
  onExpire(mailId: string): void
  onDelete(mailId: string): void
  onCopyJson(mailId: string): void
}

export interface MailEditorDrawerHandle {
  open(mail: MailRecord): void
  close(): void
  isOpen(): boolean
  destroy(): void
}

// ─── Entry point ──────────────────────────────────────────────────────────────

export function createMailEditorDrawer(deps: DrawerDeps): MailEditorDrawerHandle {
  const guard  = createUnsavedGuard()
  let attEditor: AttachmentEditorHandle | null = null
  let currentMail: MailRecord | null = null
  let _open = false

  // Create and append drawer element to body
  const drawerEl = document.createElement('aside')
  drawerEl.id   = 'mail-drawer'
  drawerEl.className = 'drawer'
  drawerEl.setAttribute('role', 'dialog')
  drawerEl.setAttribute('aria-modal', 'true')
  drawerEl.setAttribute('aria-label', 'Mail editor')
  drawerEl.hidden = true
  document.body.appendChild(drawerEl)

  // Backdrop for mobile / click-outside close
  const backdropEl = document.createElement('div')
  backdropEl.className = 'drawer-backdrop'
  backdropEl.hidden = true
  document.body.appendChild(backdropEl)
  backdropEl.addEventListener('click', () => close())

  function open(mail: MailRecord): void {
    currentMail = mail
    guard.clearDirty()
    _open = true
    _render(mail)
    drawerEl.hidden  = false
    backdropEl.hidden = false
    document.body.classList.add('drawer-open')
    // Focus subject on open
    const subj = document.getElementById('drawer-subject') as HTMLInputElement | null
    if (subj) subj.focus()
  }

  function close(): void {
    if (!guard.confirmNavigate()) return
    _destroySubcomponents()
    drawerEl.hidden   = true
    backdropEl.hidden = true
    document.body.classList.remove('drawer-open')
    currentMail = null
    _open = false
  }

  function destroy(): void {
    _destroySubcomponents()
    drawerEl.remove()
    backdropEl.remove()
    _open = false
  }

  function _destroySubcomponents(): void {
    if (attEditor) { attEditor.destroy(); attEditor = null }
    guard.clearDirty()
  }

  function _render(mail: MailRecord): void {
    _destroySubcomponents()

    const mId      = mailId(mail)
    const endT     = mailEndTime(mail)
    const startT   = mailStartTime(mail)
    const status   = deriveMailStatus(startT, endT, Date.now())
    const expInputs = isoToEditInputs(endT)
    const targets  = mailTargetUsers(mail)

    const schedState = {
      expiryMode: endT ? 'set' as const : 'none' as const,
      expiryDate: expInputs.date,
      expiryTime: expInputs.time,
    }
    const targetState = {
      targetMode:  targets.length > 0 ? 'specific' as const : 'all' as const,
      targetText:  targets.join('\n'),
    }
    const attDrafts = mailAttachments(mail).map(_attInfoToDraft)

    const isBusy = deps.isBusy()
    const connected = deps.isConnected()
    const dis = (isBusy || !connected) ? 'disabled' : ''
    const env = deps.getEnv()
    const envBadge = env
      ? `<span class="env-badge ${env.toLowerCase().includes('prod') ? 'env-badge-prod' : 'env-badge-other'}">${_esc(env)}</span>`
      : ''

    const meta = _mailMeta(mail)

    drawerEl.innerHTML = `
<div class="drawer-header">
  <div class="drawer-title-area">
    <h2 class="drawer-title" title="${_esc(mId)}">✉ ${_esc(mId.length > 22 ? mId.slice(0, 22) + '…' : mId)}</h2>
    ${envBadge}
    ${statusBadgeHtml(status)}
  </div>
  <div class="drawer-header-actions">
    <button type="button" class="btn btn-ghost btn-sm" id="drawer-copy-json" title="Copy as JSON export">📋 Copy JSON</button>
    <button type="button" class="btn btn-icon drawer-close" id="drawer-close-btn" aria-label="Close drawer">✕</button>
  </div>
</div>

<div class="drawer-body" id="drawer-body-inner">
  ${meta ? `<div class="drawer-meta">${meta}</div>` : ''}

  <div class="form-group">
    <label>Subject <span class="char-limit">[1–128 chars]</span></label>
    <input type="text" id="drawer-subject" value="${_esc(mailTitle(mail))}" maxlength="128" ${dis} />
  </div>

  <div class="form-group">
    <label>Body <span class="char-limit">[1–1024 chars]</span></label>
    <textarea id="drawer-body-text" maxlength="1024" rows="4" ${dis}>${_esc(mailContent(mail))}</textarea>
  </div>

  ${renderScheduleEditor('drawer', schedState, isBusy || !connected)}

  ${renderTargetUserEditor('drawer', targetState, isBusy || !connected)}

  <div class="form-group">
    <label class="section-label">Attachments</label>
    <div id="drawer-att-container"></div>
  </div>
</div>

<div class="drawer-footer">
  <div id="drawer-status" class="drawer-status"></div>
  <div class="btn-row">
    <button type="button" class="btn btn-primary"  id="drawer-save"   ${dis}>Save</button>
    <button type="button" class="btn btn-ghost"    id="drawer-expire" ${dis}>Expire</button>
    <button type="button" class="btn btn-danger"   id="drawer-delete" ${dis}>Delete</button>
  </div>
</div>`

    // Mount attachment editor
    const attContainer = document.getElementById('drawer-att-container')
    if (attContainer) {
      attEditor = mountAttachmentEditor(
        attContainer, attDrafts,
        {
          prefix: 'drawer',
          currencyOptions: deps.currencyOptions,
          itemOptions:     deps.itemOptions,
          ticketOptions:   deps.ticketOptions,
          disabled: isBusy || !connected,
        },
        () => guard.markDirty(),
      )
    }

    // Attach schedule / target listeners
    attachScheduleListeners('drawer', () => guard.markDirty())
    attachTargetUserListeners('drawer', () => guard.markDirty())

    // Mark dirty on subject / body edits
    const subjectEl = document.getElementById('drawer-subject') as HTMLInputElement | null
    const bodyEl    = document.getElementById('drawer-body-text') as HTMLTextAreaElement | null
    subjectEl?.addEventListener('input', () => guard.markDirty())
    bodyEl?.addEventListener('input',    () => guard.markDirty())

    // Wire action buttons
    document.getElementById('drawer-close-btn')?.addEventListener('click', () => close())

    document.getElementById('drawer-copy-json')?.addEventListener('click', () => {
      deps.onCopyJson(mId)
    })

    document.getElementById('drawer-save')?.addEventListener('click', () => {
      _handleSave(mail)
    })

    document.getElementById('drawer-expire')?.addEventListener('click', () => {
      if (!window.confirm(`Expire mail ${mId}? Sets expiry to now.`)) return
      deps.onExpire(mId)
    })

    document.getElementById('drawer-delete')?.addEventListener('click', () => {
      if (!window.confirm(`Delete mail ${mId}? Irreversible.`)) return
      deps.onDelete(mId)
    })
  }

  function _handleSave(mail: MailRecord): void {
    const mId   = mailId(mail)
    const subj  = (document.getElementById('drawer-subject')   as HTMLInputElement   | null)?.value.trim() ?? ''
    const body  = (document.getElementById('drawer-body-text') as HTMLTextAreaElement | null)?.value.trim() ?? ''

    // Validate
    if (!subj || subj.length > 128) {
      _setDrawerStatus('Subject must be 1–128 characters.', 'error'); return
    }
    if (!body || body.length > 1024) {
      _setDrawerStatus('Body must be 1–1024 characters.', 'error'); return
    }

    const sched  = readScheduleEditor('drawer')
    const target = readTargetUserEditor('drawer')
    const drafts = attEditor ? attEditor.getDrafts() : []

    // Build expiry ISO (null = clear)
    let expiresAt: string | null = null
    if (sched.expiryMode === 'set' && sched.expiryDate && sched.expiryTime) {
      const d = new Date(`${sched.expiryDate}T${sched.expiryTime}:00Z`)
      if (!isNaN(d.getTime())) expiresAt = d.toISOString()
      else { _setDrawerStatus('Invalid expiry date/time.', 'error'); return }
    }

    // Build target user IDs (null = global)
    let targetUserIds: string[] | null = null
    if (target.targetMode === 'specific' && target.targetText.trim()) {
      const lines = target.targetText.replace(/\r\n/g, '\n').replace(/\r/g, '\n').split('\n')
      const ids   = [...new Set(lines.map(s => s.trim()).filter(Boolean))]
      if (ids.length > 0) targetUserIds = ids
    }

    _setDrawerStatus('Saving…', 'info')
    guard.clearDirty()
    deps.onSave(mId, subj, body, drafts, targetUserIds, expiresAt)
  }

  function _setDrawerStatus(msg: string, type: 'info' | 'success' | 'error' | 'warning'): void {
    const el = document.getElementById('drawer-status')
    if (!el) return
    el.innerHTML = msg
      ? `<div class="alert alert-${type}" style="margin:0;font-size:12px">${_esc(msg)}</div>`
      : ''
  }

  return { open, close, isOpen: () => _open, destroy }
}

// ─── Internal helpers ─────────────────────────────────────────────────────────

function _esc(s: string): string {
  return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
}

function _mailMeta(mail: MailRecord): string {
  const meta  = mail.MailMetaData ?? mail.mailMetaData
  const start = mailStartTime(mail)
  const parts: string[] = []
  if (start) parts.push(`Started: ${_esc(formatDateUtc(start))}`)
  const senderName = meta?.Sender ?? meta?.sender ?? ''
  const senderType = meta?.SenderType ?? meta?.senderType ?? ''
  if (senderName) parts.push(`Sender: ${_esc(senderName)}`)
  if (senderType) parts.push(`Type: ${_esc(senderType)}`)
  const dedup = meta?.DedupKey ?? meta?.dedupKey ?? ''
  if (dedup) parts.push(`DedupKey: ${_esc(dedup)}`)
  return parts.length > 0
    ? parts.map(p => `<span class="meta-item">${p}</span>`).join('')
    : ''
}

function _defaultItemRow(): ItemSpecificAsset {
  return { BlueprintId: '', CurrentLevel: 1, Rarity: Rarity.Common, InitialLevel: 1, FromSource: '' }
}

function _isJsonObjType(t: string): boolean {
  const l = t.trim().toLowerCase()
  return l === 'itemspecificasset' || l === 'ticket'
}

function _attInfoToDraft(att: MailAttachmentInfo): AttachmentDraft {
  const assetType     = att.AssetType ?? att.assetType ?? 'Currency'
  const payoutAssetId = att.PayoutAssetId ?? att.payoutAssetId ?? ''
  const isJson = _isJsonObjType(assetType)

  if (isJson) {
    try {
      const parsed = JSON.parse(payoutAssetId)
      const r: Record<string, unknown> = Array.isArray(parsed) ? (parsed[0] ?? {}) : (parsed ?? {})
      return {
        payoutAssetId: '',
        assetType,
        payoutAmount: att.PayoutAmount ?? att.payoutAmount ?? 1,
        chance:       att.Chance       ?? att.chance       ?? 1,
        itemRows: [{
          BlueprintId:  typeof r['BlueprintId']  === 'string' ? r['BlueprintId']  as string : '',
          CurrentLevel: typeof r['CurrentLevel'] === 'number' ? r['CurrentLevel'] as number : 1,
          Rarity:       (typeof r['Rarity']       === 'number' ? r['Rarity']       as number : Rarity.Common) as RarityValue,
          InitialLevel: typeof r['InitialLevel'] === 'number' ? r['InitialLevel'] as number : 1,
          FromSource:   typeof r['FromSource']   === 'string' ? r['FromSource']   as string : '',
        }],
      }
    } catch {
      const legacyWarning = `⚠ legacy format: plain-string PayoutAssetId "${payoutAssetId}"`
      return {
        payoutAssetId: '',
        assetType,
        payoutAmount: att.PayoutAmount ?? att.payoutAmount ?? 1,
        chance:       att.Chance       ?? att.chance       ?? 1,
        itemRows:     [{ ..._defaultItemRow(), BlueprintId: payoutAssetId }],
        _legacyWarning: legacyWarning,
      }
    }
  }

  return {
    payoutAssetId,
    assetType,
    payoutAmount: att.PayoutAmount ?? att.payoutAmount ?? 1,
    chance:       att.Chance       ?? att.chance       ?? 1,
    itemRows:     [_defaultItemRow()],
  }
}
