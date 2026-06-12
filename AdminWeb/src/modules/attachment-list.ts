// src/modules/attachment-list.ts
// Compact attachment list component: rows + Add button + delete confirm.
// Each row shows type/name/id/amount/chance/⚠invalid/Edit/Duplicate/Delete.

import type { AttachmentDraft } from '../types'
import type { ComboboxOption } from './asset-selector'
import { getAttachmentSummary } from './attachment-serde'
import { openConfirmDialog } from './modal-shell'

// ─── Types ────────────────────────────────────────────────────────────────────

export interface AttachmentListDeps {
  currencyOptions: ComboboxOption[]
  itemOptions:     ComboboxOption[]
  ticketOptions:   ComboboxOption[]
  disabled?:       boolean
  onOpenAdd():                      void
  onOpenEdit(uid: string):          void
  onOpenDuplicate(uid: string):     void
  onDeleteConfirm(uid: string):     void
}

export interface AttachmentListHandle {
  getDrafts():                                  AttachmentDraft[]
  setDrafts(drafts: AttachmentDraft[]):         void
  addDraft(draft: AttachmentDraft):             void
  replaceDraft(uid: string, draft: AttachmentDraft): void
  deleteDraft(uid: string):                     void
  destroy():                                    void
}

// ─── Escape helper ────────────────────────────────────────────────────────────

function _esc(s: unknown): string {
  return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
}

// ─── Rendering ────────────────────────────────────────────────────────────────

function _renderList(
  drafts: AttachmentDraft[],
  deps: AttachmentListDeps,
): string {
  const dis = deps.disabled ? 'disabled' : ''

  const addBtn = `<div class="att-list-add-row">
  <button type="button" class="btn btn-ghost btn-sm" data-action="att-list-add" ${dis}
          aria-label="Add attachment">+ Add Attachment</button>
</div>`

  if (drafts.length === 0) {
    return `${addBtn}<p class="att-list-empty">No attachments.</p>`
  }

  const summaryOpts = {
    currencyOptions: deps.currencyOptions,
    itemOptions:     deps.itemOptions,
    ticketOptions:   deps.ticketOptions,
  }

  const rows = drafts.map(draft => {
    const uid     = draft._id ?? ''
    const summary = getAttachmentSummary(draft, summaryOpts)
    const invalid = !summary.isValid
    const invalidHtml = invalid
      ? `<span class="att-list-invalid" data-action="att-edit-invalid" data-uid="${_esc(uid)}"
             aria-label="Invalid — click to review" title="Invalid — click to review">⚠</span>`
      : `<span class="att-list-invalid" hidden aria-hidden="true">⚠</span>`

    return `<div class="att-list-row${invalid ? ' att-list-row-invalid' : ''}" role="row"
    data-uid="${_esc(uid)}" tabindex="0" aria-label="Edit ${_esc(summary.typeLabel)} ${_esc(summary.assetId)}">
  <span class="att-list-type">${_esc(summary.typeLabel)}</span>
  <span class="att-list-name" title="${_esc(summary.assetId)}">${_esc(summary.assetName || summary.assetId || '(no id)')}</span>
  <span class="att-list-id">${_esc(summary.assetId)}</span>
  <span class="att-list-amount">×${summary.amount}</span>
  <span class="att-list-chance">${_esc(summary.chancePct)}</span>
  ${invalidHtml}
  <div class="att-list-actions">
    <button type="button" class="btn btn-ghost btn-sm" data-action="att-edit" data-uid="${_esc(uid)}"
            aria-label="Edit ${_esc(summary.typeLabel)} ${_esc(summary.assetId)}" ${dis}>Edit</button>
    <button type="button" class="btn btn-ghost btn-sm" data-action="att-duplicate" data-uid="${_esc(uid)}"
            aria-label="Duplicate ${_esc(summary.typeLabel)} ${_esc(summary.assetId)}" ${dis}>Duplicate</button>
    <button type="button" class="btn btn-ghost btn-sm att-delete-btn" data-action="att-delete" data-uid="${_esc(uid)}"
            aria-label="Delete ${_esc(summary.typeLabel)} ${_esc(summary.assetId)}" ${dis}>🗑</button>
  </div>
</div>`
  }).join('')

  return `${addBtn}<div class="att-list-table" role="table">${rows}</div>`
}

// ─── Mount ────────────────────────────────────────────────────────────────────

export function mountAttachmentList(
  container: HTMLElement,
  initialDrafts: AttachmentDraft[],
  deps: AttachmentListDeps,
): AttachmentListHandle {
  let drafts: AttachmentDraft[] = [...initialDrafts]

  function render(): void {
    container.innerHTML = _renderList(drafts, deps)
  }

  // Delegated click handler
  const clickHandler = (e: Event) => {
    const target = e.target as HTMLElement
    const btn    = target.closest<HTMLElement>('[data-action]')
    if (!btn) {
      // Row click (not on a button) → edit
      const row = target.closest<HTMLElement>('.att-list-row')
      if (row) {
        const uid = row.dataset['uid']
        if (uid) deps.onOpenEdit(uid)
      }
      return
    }

    const action = btn.dataset['action']
    const uid    = btn.dataset['uid']

    // Prevent row-click from also firing on action button clicks
    if (action === 'att-edit' || action === 'att-duplicate' || action === 'att-delete' || action === 'att-edit-invalid') {
      e.stopPropagation()
    }

    if (action === 'att-list-add') {
      deps.onOpenAdd()
    } else if ((action === 'att-edit' || action === 'att-edit-invalid') && uid) {
      deps.onOpenEdit(uid)
    } else if (action === 'att-duplicate' && uid) {
      deps.onOpenDuplicate(uid)
    } else if (action === 'att-delete' && uid) {
      const draft   = drafts.find(d => d._id === uid)
      const summary = draft
        ? getAttachmentSummary(draft, deps)
        : null
      const msg = summary
        ? `Delete "${summary.typeLabel}: ${summary.assetName || summary.assetId}"? This cannot be undone.`
        : 'Delete this attachment? This cannot be undone.'

      openConfirmDialog({
        title:        'Delete Attachment',
        message:      msg,
        confirmLabel: 'Delete',
        danger:       true,
      }).then(confirmed => {
        if (confirmed) {
          handle.deleteDraft(uid)
          deps.onDeleteConfirm(uid)
        }
      })
    }
  }

  // Keyboard row activation (Enter/Space on row)
  const keyHandler = (e: KeyboardEvent) => {
    if (e.key !== 'Enter' && e.key !== ' ') return
    const row = (e.target as HTMLElement).closest<HTMLElement>('.att-list-row')
    if (!row) return
    const uid = row.dataset['uid']
    // Only activate if focus is on the row itself, not a button inside it
    if (document.activeElement === row && uid) {
      e.preventDefault()
      deps.onOpenEdit(uid)
    }
  }

  container.addEventListener('click', clickHandler)
  container.addEventListener('keydown', keyHandler)
  render()

  const handle: AttachmentListHandle = {
    getDrafts: () => [...drafts],

    setDrafts(d) {
      drafts = [...d]
      render()
    },

    addDraft(draft) {
      drafts = [...drafts, draft]
      render()
    },

    replaceDraft(uid, draft) {
      drafts = drafts.map(d => d._id === uid ? draft : d)
      render()
    },

    deleteDraft(uid) {
      drafts = drafts.filter(d => d._id !== uid)
      render()
    },

    destroy() {
      container.removeEventListener('click', clickHandler)
      container.removeEventListener('keydown', keyHandler)
      container.innerHTML = ''
    },
  }

  return handle
}
