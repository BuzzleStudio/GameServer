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
import { mountAttachmentList } from './attachment-list'
import type { AttachmentListHandle } from './attachment-list'
import { mountAttachmentModal } from './attachment-modal'
import type { AttachmentModalHandle } from './attachment-modal'
import { deserializeAttachmentToForm } from './attachment-serde'
import type { ComboboxOption } from './asset-selector'
import { createUnsavedGuard } from './unsaved-guard'
import { createDrawerResize } from './drawer-resize'
import type { DrawerResizeHandle } from './drawer-resize'
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
import type { MailRecord, AttachmentDraft, MailAttachmentInfo } from '../types'

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
  const guard    = createUnsavedGuard()
  let listHandle: AttachmentListHandle   | null = null
  let modalHandle: AttachmentModalHandle | null = null
  let resizeHandle: DrawerResizeHandle   | null = null
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

  // Attach left-edge resize handle (desktop only; no-op on mobile via innerWidth guard)
  resizeHandle = createDrawerResize(drawerEl)

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
    resizeHandle?.applyPersistedWidth()
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
    resizeHandle?.destroy()
    resizeHandle = null
    drawerEl.remove()
    backdropEl.remove()
    _open = false
  }

  function _destroySubcomponents(): void {
    if (listHandle)  { listHandle.destroy();  listHandle  = null }
    if (modalHandle) { modalHandle.destroy(); modalHandle = null }
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
    const attDrafts = mailAttachments(mail).map(deserializeAttachmentToForm)

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

    // Mount modal (singleton — create once if not already created)
    if (!modalHandle) {
      modalHandle = mountAttachmentModal({
        currencyOptions: deps.currencyOptions,
        itemOptions:     deps.itemOptions,
        ticketOptions:   deps.ticketOptions,
        onCommit(draft, mode, sourceUid) {
          if (!listHandle) return
          if (mode === 'edit' && sourceUid) {
            listHandle.replaceDraft(sourceUid, draft)
          } else {
            listHandle.addDraft(draft)
          }
          guard.markDirty()
        },
      })
    }

    // Mount attachment list
    const attContainer = document.getElementById('drawer-att-container')
    if (attContainer) {
      listHandle = mountAttachmentList(
        attContainer, attDrafts,
        {
          currencyOptions: deps.currencyOptions,
          itemOptions:     deps.itemOptions,
          ticketOptions:   deps.ticketOptions,
          disabled: isBusy || !connected,
          onOpenAdd() {
            modalHandle?.openAdd(attContainer.querySelector<HTMLElement>('[data-action="att-list-add"]') ?? null)
          },
          onOpenEdit(uid) {
            const draft = listHandle?.getDrafts().find(d => d._id === uid)
            if (!draft) return
            const rowEl = attContainer.querySelector<HTMLElement>(`[data-uid="${uid}"]`)
            modalHandle?.openEdit(draft, rowEl)
          },
          onOpenDuplicate(uid) {
            const draft = listHandle?.getDrafts().find(d => d._id === uid)
            if (!draft) return
            const rowEl = attContainer.querySelector<HTMLElement>(`[data-uid="${uid}"]`)
            modalHandle?.openDuplicate(draft, rowEl)
          },
          onDeleteConfirm() {
            guard.markDirty()
          },
        },
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
    const drafts = listHandle ? listHandle.getDrafts() : []

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

