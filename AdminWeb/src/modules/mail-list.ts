// src/modules/mail-list.ts
// Manage-tab content: paginated mail list + user lookup + purge.
// Mounts a persistent mail-editor drawer on the body.

import { createMailEditorDrawer } from './mail-editor-drawer'
import type { DrawerDeps } from './mail-editor-drawer'
import type { ComboboxOption } from './asset-selector'
import { statusBadgeHtml, deriveMailStatus } from './status'
import { formatDateShort } from './date-format'
import {
  mailId,
  mailTitle,
  mailContent,
  mailStartTime,
  mailEndTime,
  mailTargetUsers,
  mailAttachments,
} from '../types'
import type { MailRecord, AttachmentDraft } from '../types'

// ─── Types ────────────────────────────────────────────────────────────────────

const MAILS_PER_PAGE = 10

export interface ManageTabDeps {
  // State getters
  getMails():             MailRecord[] | null
  getUserMails():         MailRecord[] | null
  getMailPage():          number
  getMailTotalCount():    number
  getMailError():         string
  getUserMailError():     string
  getUserLookupPlayerId(): string
  isBusy():               boolean
  isConnected():          boolean
  getEnv():               string
  // Combobox data
  currencyOptions: ComboboxOption[]
  itemOptions:     ComboboxOption[]
  ticketOptions:   ComboboxOption[]
  // Actions
  onLoad():                                                          void
  onLookupUser(playerId: string):                                    void
  onSave(
    mailId: string, subject: string, body: string,
    drafts: AttachmentDraft[], targetUserIds: string[] | null,
    expiresAt: string | null,
  ):                                                                 void
  onExpire(mailId: string):                                          void
  onDelete(mailId: string):                                          void
  onCopyJson(mailId: string, source: 'global' | 'user'):            void
  onPurge():                                                         void
  onPageChange(delta: number):                                       void
  onSetEndTime(mailId: string, endTime: string | null):              void
}

export interface ManageTabHandle {
  destroy(): void
}

// ─── Mount ────────────────────────────────────────────────────────────────────

export function mountManageTab(container: HTMLElement, deps: ManageTabDeps): ManageTabHandle {
  // Build drawer deps
  const drawerDeps: DrawerDeps = {
    getEnv:          deps.getEnv,
    isBusy:          deps.isBusy,
    isConnected:     deps.isConnected,
    currencyOptions: deps.currencyOptions,
    itemOptions:     deps.itemOptions,
    ticketOptions:   deps.ticketOptions,
    onSave:          deps.onSave,
    onExpire(mId) { deps.onExpire(mId) },
    onDelete(mId) { deps.onDelete(mId) },
    onCopyJson(mId) { deps.onCopyJson(mId, 'global') },
  }

  const drawer = createMailEditorDrawer(drawerDeps)

  function openMail(m: MailRecord) { drawer.open(m) }

  function render() {
    container.innerHTML = _renderContent(deps)
    _attachListeners(container, deps, openMail)
  }

  render()

  return {
    destroy() {
      drawer.destroy()
      container.innerHTML = ''
    },
  }
}

// ─── Render ───────────────────────────────────────────────────────────────────

function _esc(s: string): string {
  return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
}

function _scopeLabel(m: MailRecord, source: 'global' | 'user'): string {
  if (source === 'user') return 'User'
  return mailTargetUsers(m).length > 0 ? 'Global-targeted' : 'Global'
}

function _scopeBadge(scope: string): string {
  const cls = scope === 'Global' ? 'scope-global'
    : scope === 'Global-targeted' ? 'scope-targeted'
    : 'scope-user'
  return `<span class="scope-badge ${cls}">${_esc(scope)}</span>`
}

function _renderMailRow(m: MailRecord, source: 'global' | 'user', readOnly = false): string {
  const id     = mailId(m)
  const startT = mailStartTime(m)
  const endT   = mailEndTime(m)
  const now    = Date.now()
  const status = deriveMailStatus(startT, endT, now)
  const scope  = _scopeLabel(m, source)
  const attLen = mailAttachments(m).length
  const targets = mailTargetUsers(m)

  const scopeB  = _scopeBadge(scope)
  const statusB = statusBadgeHtml(status)
  const startFmt = startT ? _esc(formatDateShort(startT)) : '—'
  const endFmt   = endT   ? _esc(formatDateShort(endT))   : '—'
  const titleStr = _esc(mailTitle(m) || '(no title)')
  const bodyStr  = _esc((mailContent(m) || '').slice(0, 48))

  if (readOnly) {
    return `
<tr>
  <td class="mail-id-cell" title="${_esc(id)}">${_esc(id.slice(0, 18))}${id.length > 18 ? '…' : ''}</td>
  <td>${scopeB}</td>
  <td>${statusB}</td>
  <td class="mail-title-cell" title="${_esc(mailTitle(m))}">${titleStr}</td>
  <td>${startFmt}</td>
  <td>${endFmt}</td>
  <td>${attLen > 0 ? `${attLen} att.` : '—'}</td>
  <td>
    <button type="button" class="btn btn-ghost btn-sm"
      data-action="copy-mail" data-mail-id="${_esc(id)}" data-source="user"
      title="Copy as JSON">📋</button>
  </td>
</tr>`
  }

  return `
<tr class="mail-row" role="button" tabindex="0"
  data-action="open-mail" data-mail-id="${_esc(id)}"
  title="Click to edit this mail">
  <td class="mail-id-cell" title="${_esc(id)}">${_esc(id.slice(0, 18))}${id.length > 18 ? '…' : ''}</td>
  <td>${scopeB}</td>
  <td>${statusB}</td>
  <td class="mail-title-cell">
    <div class="mail-title">${titleStr}</div>
    <div class="mail-body-preview">${bodyStr}</div>
  </td>
  <td>${startFmt}</td>
  <td>${endFmt}</td>
  <td class="mail-meta-cell">
    ${targets.length > 0 ? `<span class="meta-pill">${targets.length} users</span>` : '<span class="meta-dim">Global</span>'}
    ${attLen > 0 ? `<span class="meta-pill">${attLen} att</span>` : ''}
  </td>
  <td class="actions-cell" data-stop-propagation="1">
    <button type="button" class="btn btn-ghost btn-sm"
      data-action="copy-mail" data-mail-id="${_esc(id)}" data-source="global"
      title="Copy as JSON">📋</button>
    <button type="button" class="btn btn-primary btn-sm"
      data-action="open-mail" data-mail-id="${_esc(id)}"
      title="Edit">✎ Edit</button>
  </td>
</tr>`
}

function _renderMailTable(deps: ManageTabDeps): string {
  const all    = deps.getMails()
  const total  = deps.getMailTotalCount()
  const page   = deps.getMailPage()
  const isBusy = deps.isBusy()

  if (all === null) {
    const err = deps.getMailError()
    return `
<div class="empty">Click "Load All Global Mails" to fetch.</div>
${err ? `<div class="alert alert-error" style="margin-top:8px">${_esc(err)}</div>` : ''}`
  }

  const pageCount = Math.max(1, Math.ceil(total / MAILS_PER_PAGE))
  const start     = page * MAILS_PER_PAGE
  const mails     = all.slice(start, start + MAILS_PER_PAGE)

  if (mails.length === 0) {
    return '<div class="empty">No mails found.</div>'
  }

  const rows = mails.map(m => _renderMailRow(m, 'global')).join('')

  return `
<div class="mail-table-wrap">
  <table class="mail-table">
    <thead><tr>
      <th>Message ID</th>
      <th>Scope</th>
      <th>Status</th>
      <th>Title / Body</th>
      <th>Start (UTC)</th>
      <th>Expiry (UTC)</th>
      <th>Targets / Att.</th>
      <th>Actions</th>
    </tr></thead>
    <tbody>${rows}</tbody>
  </table>
</div>
<div class="pager">
  <button type="button" class="btn btn-ghost btn-sm" data-action="page-prev"
    ${page <= 0 || isBusy ? 'disabled' : ''}>← Prev</button>
  <span>Page ${page + 1} / ${pageCount} · ${total} total</span>
  <button type="button" class="btn btn-ghost btn-sm" data-action="page-next"
    ${page >= pageCount - 1 || isBusy ? 'disabled' : ''}>Next →</button>
</div>`
}

function _renderUserMailTable(deps: ManageTabDeps): string {
  const list = deps.getUserMails()
  const err  = deps.getUserMailError()

  if (err) return `<div class="alert alert-error" style="margin-top:8px">${_esc(err)}</div>`
  if (list === null) {
    return '<div class="empty" style="font-size:12px">Enter a Player ID above and click Fetch.</div>'
  }
  if (list.length === 0) {
    return '<div class="empty">No user mails found for this player.</div>'
  }

  const rows = list.map(m => _renderMailRow(m, 'user', true)).join('')
  return `
<div class="mail-table-wrap" style="margin-top:8px">
  <table class="mail-table">
    <thead><tr>
      <th>Message ID</th><th>Scope</th><th>Status</th><th>Title</th>
      <th>Start (UTC)</th><th>Expiry (UTC)</th><th>Att.</th><th>Actions</th>
    </tr></thead>
    <tbody>${rows}</tbody>
  </table>
</div>`
}

function _renderContent(deps: ManageTabDeps): string {
  const isBusy    = deps.isBusy()
  const connected = deps.isConnected()
  const dis       = isBusy || !connected ? 'disabled' : ''

  return `
<div class="card">
  <div class="card-title">📋 Global Mail List (admin mode)</div>
  <div class="toolbar">
    <button type="button" class="btn btn-secondary" data-action="load-mails" ${dis}>
      ${isBusy ? '<span class="spinner"></span>' : '🔄'} Load All Global Mails
    </button>
  </div>
  <div id="ml-table-area" style="margin-top:12px">${_renderMailTable(deps)}</div>
</div>

<div class="card">
  <div class="card-title">🔍 User Mail Lookup</div>
  <div style="display:flex;gap:8px;align-items:flex-end">
    <div class="form-group" style="flex:1;margin-bottom:0">
      <label>Player ID</label>
      <input type="text" id="ml-user-id" value="${_esc(deps.getUserLookupPlayerId())}"
        placeholder="player UUID" />
    </div>
    <button type="button" class="btn btn-secondary" data-action="lookup-user" ${dis}>
      ${isBusy ? '<span class="spinner"></span>' : '🔍'} Fetch User Mail
    </button>
  </div>
  <div id="ml-user-table">${_renderUserMailTable(deps)}</div>
</div>

<div class="card">
  <div class="card-title">⚙️ Direct Mail Operations</div>
  <div class="form-group">
    <label>Mail ID</label>
    <input type="text" id="ml-direct-id" placeholder="Global mail UUID" />
  </div>
  <div class="btn-row" style="margin-top:8px">
    <button type="button" class="btn btn-ghost"  data-action="direct-expire" ${dis}>Expire Mail</button>
    <button type="button" class="btn btn-danger"  data-action="direct-delete" ${dis}>Delete Mail</button>
  </div>
</div>

<div class="card">
  <div class="card-title">🗑️ Purge Expired</div>
  <p style="font-size:12px;color:var(--text-dim);margin-bottom:10px">
    Removes all expired global mails. Irreversible.
  </p>
  <button type="button" class="btn btn-danger" data-action="purge" ${dis}>
    ${isBusy ? '<span class="spinner"></span>' : ''} Purge All Expired
  </button>
</div>`
}

// ─── Event wiring ─────────────────────────────────────────────────────────────

function _attachListeners(
  container: HTMLElement,
  deps: ManageTabDeps,
  openMail: (m: MailRecord) => void,
): void {
  container.addEventListener('click', (e) => {
    const target = e.target as HTMLElement

    // Stop propagation from action buttons inside mail rows (avoid double-trigger)
    const stopEl = target.closest<HTMLElement>('[data-stop-propagation]')
    if (stopEl) {
      // let through: only handle the actual button data-action, not the row
    }

    const btn    = target.closest<HTMLElement>('[data-action]')
    if (!btn) return
    const action  = btn.dataset['action'] ?? ''
    const mailIdV = btn.dataset['mailId']
    const source  = (btn.dataset['source'] ?? 'global') as 'global' | 'user'

    switch (action) {
      case 'load-mails': {
        deps.onLoad()
        break
      }
      case 'lookup-user': {
        const pid = (document.getElementById('ml-user-id') as HTMLInputElement | null)?.value.trim() ?? ''
        deps.onLookupUser(pid)
        break
      }
      case 'page-prev': {
        deps.onPageChange(-1)
        break
      }
      case 'page-next': {
        deps.onPageChange(+1)
        break
      }
      case 'open-mail': {
        if (mailIdV) {
          const mail = _findMail(deps, mailIdV, source)
          if (mail) openMail(mail)
        }
        break
      }
      case 'copy-mail': {
        if (mailIdV) deps.onCopyJson(mailIdV, source)
        break
      }
      case 'direct-expire': {
        const mId = (document.getElementById('ml-direct-id') as HTMLInputElement | null)?.value.trim()
        if (mId) deps.onExpire(mId)
        break
      }
      case 'direct-delete': {
        const mId = (document.getElementById('ml-direct-id') as HTMLInputElement | null)?.value.trim()
        if (mId) deps.onDelete(mId)
        break
      }
      case 'purge': {
        deps.onPurge()
        break
      }
    }
  })

  // Keyboard: Enter on mail rows opens drawer
  container.addEventListener('keydown', (e) => {
    if (e.key !== 'Enter' && e.key !== ' ') return
    const row = (e.target as HTMLElement).closest<HTMLElement>('[data-action="open-mail"]')
    if (!row) return
    e.preventDefault()
    const mId = row.dataset['mailId']
    if (mId) {
      const mail = _findMail(deps, mId, 'global')
      if (mail) openMail(mail)
    }
  })
}

function _findMail(deps: ManageTabDeps, mailIdV: string, source: 'global' | 'user'): MailRecord | undefined {
  const list = source === 'user' ? deps.getUserMails() : deps.getMails()
  return list?.find(m => mailId(m) === mailIdV)
}
