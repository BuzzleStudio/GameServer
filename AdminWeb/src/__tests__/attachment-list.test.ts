// @vitest-environment happy-dom
/**
 * attachment-list.test.ts — mountAttachmentList component
 *
 * Design ref: §12 (compact list), §13.3 (row actions, stopPropagation)
 * Env: happy-dom
 *
 * Tested behaviors:
 *   - render: Add button always present
 *   - render: empty state when no drafts
 *   - render: rows show type/name/amount/chance/⚠invalid
 *   - row click → onOpenEdit
 *   - Edit button → onOpenEdit (stopPropagation: no double-fire)
 *   - Duplicate → onOpenDuplicate
 *   - Delete → confirm dialog → deleteDraft + onDeleteConfirm
 *   - Delete → confirm cancel → no deletion
 *   - ⚠invalid span: visible for invalid draft, hidden for valid
 *   - keyboard Enter on row → onOpenEdit
 *   - getDrafts() returns shallow copy
 *   - addDraft / replaceDraft / deleteDraft / setDrafts
 *   - destroy(): clears container, removes listeners
 */

import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mountAttachmentList } from '../modules/attachment-list'
import { createDefaultDraft } from '../modules/attachment-serde'
import type { AttachmentListDeps } from '../modules/attachment-list'
import type { AttachmentDraft } from '../types'

function makeDraft(assetType = 'Currency', payoutAssetId = 'gem'): AttachmentDraft {
  return { ...createDefaultDraft(assetType), payoutAssetId }
}

function makeDeps(overrides: Partial<AttachmentListDeps> = {}): AttachmentListDeps {
  return {
    currencyOptions: [{ id: 'gem', label: 'Gem' }],
    itemOptions: [],
    ticketOptions: [],
    onOpenAdd:       vi.fn(),
    onOpenEdit:      vi.fn(),
    onOpenDuplicate: vi.fn(),
    onDeleteConfirm: vi.fn(),
    ...overrides,
  }
}

let container: HTMLDivElement

beforeEach(() => {
  document.body.innerHTML = ''
  container = document.createElement('div')
  container.id = 'list-container'
  document.body.appendChild(container)
})

afterEach(() => {
  document.querySelectorAll('dialog').forEach(d => d.remove())
  document.body.innerHTML = ''
})

// ─── Render ───────────────────────────────────────────────────────────────────

describe('mountAttachmentList — render', () => {
  it('always renders Add Attachment button', () => {
    mountAttachmentList(container, [], makeDeps())
    expect(container.querySelector('[data-action="att-list-add"]')).toBeTruthy()
  })

  it('empty state message when no drafts', () => {
    mountAttachmentList(container, [], makeDeps())
    expect(container.querySelector('.att-list-empty')).toBeTruthy()
  })

  it('renders a row per draft', () => {
    const drafts = [makeDraft(), makeDraft('Currency', 'gold')]
    mountAttachmentList(container, drafts, makeDeps())
    expect(container.querySelectorAll('.att-list-row').length).toBe(2)
  })

  it('row shows type label', () => {
    mountAttachmentList(container, [makeDraft()], makeDeps())
    const row = container.querySelector('.att-list-row')!
    expect(row.querySelector('.att-list-type')?.textContent).toBe('Currency')
  })

  it('row shows resolved asset name from options', () => {
    mountAttachmentList(container, [makeDraft()], makeDeps({
      currencyOptions: [{ id: 'gem', label: 'Big Gem' }],
    }))
    expect(container.querySelector('.att-list-name')?.textContent).toBe('Big Gem')
  })

  it('row shows amount and chance', () => {
    const draft = { ...makeDraft(), payoutAmount: 5, chance: 0.5 }
    mountAttachmentList(container, [draft], makeDeps())
    const row = container.querySelector('.att-list-row')!
    expect(row.querySelector('.att-list-amount')?.textContent).toBe('×5')
    expect(row.querySelector('.att-list-chance')?.textContent).toBe('50%')
  })

  it('⚠ indicator hidden for valid draft', () => {
    mountAttachmentList(container, [makeDraft()], makeDeps())
    const invalid = container.querySelector('.att-list-invalid')
    // valid draft: span should have hidden attribute or be hidden
    expect(invalid?.hasAttribute('hidden') ?? false).toBe(true)
  })

  it('⚠ indicator visible for invalid draft (empty payoutAssetId)', () => {
    const draft = { ...makeDraft(), payoutAssetId: '' }
    mountAttachmentList(container, [draft], makeDeps())
    // find the visible ⚠ span (no hidden attr)
    const spans = Array.from(container.querySelectorAll('.att-list-invalid'))
    const visible = spans.find(s => !s.hasAttribute('hidden'))
    expect(visible).toBeTruthy()
  })

  it('row has data-uid matching draft._id', () => {
    const draft = makeDraft()
    mountAttachmentList(container, [draft], makeDeps())
    const row = container.querySelector<HTMLElement>('.att-list-row')
    expect(row?.dataset['uid']).toBe(draft._id)
  })
})

// ─── Row click / action buttons ───────────────────────────────────────────────

describe('mountAttachmentList — click handling', () => {
  it('Add Attachment button → onOpenAdd', () => {
    const deps = makeDeps()
    mountAttachmentList(container, [], deps)
    container.querySelector<HTMLButtonElement>('[data-action="att-list-add"]')?.click()
    expect(deps.onOpenAdd).toHaveBeenCalledTimes(1)
  })

  it('row click → onOpenEdit with uid', () => {
    const draft = makeDraft()
    const deps  = makeDeps()
    mountAttachmentList(container, [draft], deps)
    const row = container.querySelector<HTMLElement>('.att-list-row')!
    // click on the row itself (not a button)
    const typeLbl = row.querySelector<HTMLElement>('.att-list-type')!
    typeLbl.click()
    expect(deps.onOpenEdit).toHaveBeenCalledWith(draft._id)
  })

  it('Edit button → onOpenEdit, does NOT also fire row-click handler', () => {
    const draft = makeDraft()
    const deps  = makeDeps()
    mountAttachmentList(container, [draft], deps)
    container.querySelector<HTMLButtonElement>('[data-action="att-edit"]')?.click()
    expect(deps.onOpenEdit).toHaveBeenCalledTimes(1)
    expect(deps.onOpenEdit).toHaveBeenCalledWith(draft._id)
  })

  it('Duplicate button → onOpenDuplicate', () => {
    const draft = makeDraft()
    const deps  = makeDeps()
    mountAttachmentList(container, [draft], deps)
    container.querySelector<HTMLButtonElement>('[data-action="att-duplicate"]')?.click()
    expect(deps.onOpenDuplicate).toHaveBeenCalledWith(draft._id)
  })

  it('Delete → confirm OK → deleteDraft + onDeleteConfirm', async () => {
    const draft = makeDraft()
    const deps  = makeDeps()
    const handle = mountAttachmentList(container, [draft], deps)

    container.querySelector<HTMLButtonElement>('[data-action="att-delete"]')?.click()
    // confirm dialog appears
    const dlg = document.querySelector('.confirm-dialog') as HTMLDialogElement
    expect(dlg).toBeTruthy()
    dlg.querySelector<HTMLButtonElement>('#confirm-ok')?.click()
    await Promise.resolve()

    expect(handle.getDrafts()).toHaveLength(0)
    expect(deps.onDeleteConfirm).toHaveBeenCalledWith(draft._id)
  })

  it('Delete → confirm Cancel → no deletion', async () => {
    const draft = makeDraft()
    const deps  = makeDeps()
    const handle = mountAttachmentList(container, [draft], deps)

    container.querySelector<HTMLButtonElement>('[data-action="att-delete"]')?.click()
    const dlg = document.querySelector('.confirm-dialog') as HTMLDialogElement
    dlg.querySelector<HTMLButtonElement>('#confirm-cancel')?.click()
    await Promise.resolve()

    expect(handle.getDrafts()).toHaveLength(1)
    expect(deps.onDeleteConfirm).not.toHaveBeenCalled()
  })

  it('⚠ click → onOpenEdit', () => {
    const draft = { ...makeDraft(), payoutAssetId: '' }
    const deps  = makeDeps()
    mountAttachmentList(container, [draft], deps)
    const invalidBtn = container.querySelector<HTMLElement>('[data-action="att-edit-invalid"]')
    expect(invalidBtn).toBeTruthy()
    invalidBtn?.click()
    expect(deps.onOpenEdit).toHaveBeenCalledWith(draft._id)
  })
})

// ─── Keyboard activation ──────────────────────────────────────────────────────

describe('mountAttachmentList — keyboard', () => {
  it('Enter on row → onOpenEdit', () => {
    const draft = makeDraft()
    const deps  = makeDeps()
    mountAttachmentList(container, [draft], deps)
    const row = container.querySelector<HTMLElement>('.att-list-row')!
    row.focus()
    const ev = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })
    row.dispatchEvent(ev)
    expect(deps.onOpenEdit).toHaveBeenCalledWith(draft._id)
  })

  it('Space on row → onOpenEdit', () => {
    const draft = makeDraft()
    const deps  = makeDeps()
    mountAttachmentList(container, [draft], deps)
    const row = container.querySelector<HTMLElement>('.att-list-row')!
    row.focus()
    const ev = new KeyboardEvent('keydown', { key: ' ', bubbles: true, cancelable: true })
    row.dispatchEvent(ev)
    expect(deps.onOpenEdit).toHaveBeenCalledWith(draft._id)
  })
})

// ─── Handle API ───────────────────────────────────────────────────────────────

describe('mountAttachmentList — handle API', () => {
  it('getDrafts() returns copy (not same reference)', () => {
    const draft = makeDraft()
    const handle = mountAttachmentList(container, [draft], makeDeps())
    const copy1 = handle.getDrafts()
    const copy2 = handle.getDrafts()
    expect(copy1).not.toBe(copy2)
    expect(copy1[0]._id).toBe(draft._id)
  })

  it('addDraft() appends and re-renders', () => {
    const handle = mountAttachmentList(container, [], makeDeps())
    const d = makeDraft()
    handle.addDraft(d)
    expect(handle.getDrafts()).toHaveLength(1)
    expect(container.querySelectorAll('.att-list-row').length).toBe(1)
  })

  it('replaceDraft() replaces by uid', () => {
    const d1 = makeDraft('Currency', 'gem')
    const d2: AttachmentDraft = { ...makeDraft('Currency', 'gold'), _id: d1._id }
    const handle = mountAttachmentList(container, [d1], makeDeps())
    handle.replaceDraft(d1._id!, d2)
    expect(handle.getDrafts()[0].payoutAssetId).toBe('gold')
  })

  it('replaceDraft() no-ops for unknown uid', () => {
    const d1 = makeDraft()
    const handle = mountAttachmentList(container, [d1], makeDeps())
    handle.replaceDraft('nonexistent', makeDraft())
    expect(handle.getDrafts()[0]._id).toBe(d1._id)
  })

  it('deleteDraft() removes by uid', () => {
    const d1 = makeDraft('Currency', 'gem')
    const d2 = makeDraft('Currency', 'gold')
    const handle = mountAttachmentList(container, [d1, d2], makeDeps())
    handle.deleteDraft(d1._id!)
    const remaining = handle.getDrafts()
    expect(remaining).toHaveLength(1)
    expect(remaining[0]._id).toBe(d2._id)
  })

  it('setDrafts() replaces full list', () => {
    const handle = mountAttachmentList(container, [makeDraft()], makeDeps())
    const newDrafts = [makeDraft('Currency', 'gold'), makeDraft('Currency', 'gem')]
    handle.setDrafts(newDrafts)
    expect(handle.getDrafts()).toHaveLength(2)
    expect(container.querySelectorAll('.att-list-row').length).toBe(2)
  })

  it('destroy() clears container', () => {
    const handle = mountAttachmentList(container, [makeDraft()], makeDeps())
    handle.destroy()
    expect(container.innerHTML).toBe('')
  })

  it('destroy() removes click listeners (no callback after destroy)', () => {
    const deps = makeDeps()
    const handle = mountAttachmentList(container, [], deps)
    handle.destroy()
    // Re-add button manually and click — should NOT call onOpenAdd
    container.innerHTML = '<button data-action="att-list-add">Add</button>'
    container.querySelector<HTMLButtonElement>('[data-action="att-list-add"]')?.click()
    expect(deps.onOpenAdd).not.toHaveBeenCalled()
  })
})
