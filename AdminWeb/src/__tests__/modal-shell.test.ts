// @vitest-environment happy-dom
/**
 * modal-shell.test.ts — mountModalShell + openConfirmDialog
 *
 * Design ref: §9 (Escape race guard), §10 (focus trap), §7.4 (inert/scroll-lock)
 * Env: happy-dom
 *
 * Tested behaviors:
 *   - open(): dialog in DOM, body-noscroll, inert on siblings, isOpen()=true
 *   - close(): body-noscroll removed, inert removed, isOpen()=false
 *   - forceClose(): bypasses dirty guard
 *   - Escape (no combobox): _handleCloseRequest fires
 *   - Escape (combobox open): deferred to combobox (no close)
 *   - cancel event: preventDefault called
 *   - Tab: trapFocusStep wraps around
 *   - setDirtyGuard: prevent close when dirty (needs confirm)
 *   - setContent/setBody/setFooter
 *   - destroy(): removes from DOM
 *   - openConfirmDialog: resolves true on OK, false on Cancel
 */

import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mountModalShell, openConfirmDialog } from '../modules/modal-shell'

function fireKeydown(target: Element | Document, key: string, opts: KeyboardEventInit = {}): void {
  const ev = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...opts })
  target.dispatchEvent(ev)
}

let app: HTMLDivElement

beforeEach(() => {
  document.body.innerHTML = ''
  app = document.createElement('div')
  app.id = 'app'
  document.body.appendChild(app)
})

afterEach(() => {
  // clean up any remaining dialogs
  document.querySelectorAll('dialog').forEach(d => d.remove())
  document.body.className = ''
  document.body.innerHTML = ''
})

// ─── open / close ─────────────────────────────────────────────────────────────

describe('mountModalShell — open', () => {
  it('appends <dialog> to body', () => {
    const shell = mountModalShell()
    expect(document.querySelector('#modal-portal')).toBeTruthy()
    shell.destroy()
  })

  it('isOpen() = false before open()', () => {
    const shell = mountModalShell()
    expect(shell.isOpen()).toBe(false)
    shell.destroy()
  })

  it('open() sets isOpen()=true and adds body-noscroll', () => {
    const shell = mountModalShell()
    shell.open()
    expect(shell.isOpen()).toBe(true)
    expect(document.body.classList.contains('body-noscroll')).toBe(true)
    shell.destroy()
  })

  it('open() marks siblings as inert with data-modal-inert', () => {
    const shell = mountModalShell()
    const sibling = document.createElement('div')
    document.body.appendChild(sibling)
    shell.open()
    expect(sibling.hasAttribute('inert')).toBe(true)
    expect(sibling.hasAttribute('data-modal-inert')).toBe(true)
    shell.destroy()
  })

  it('open() does NOT re-mark already-inert elements', () => {
    const alreadyInert = document.createElement('div')
    alreadyInert.setAttribute('inert', '')
    document.body.appendChild(alreadyInert)
    const shell = mountModalShell()
    shell.open()
    // already-inert element should NOT have data-modal-inert sentinel
    expect(alreadyInert.hasAttribute('data-modal-inert')).toBe(false)
    shell.destroy()
  })

  it('second open() call is no-op', () => {
    const shell = mountModalShell()
    shell.open()
    shell.open()
    expect(shell.isOpen()).toBe(true)
    shell.destroy()
  })
})

describe('mountModalShell — close', () => {
  it('forceClose(): isOpen()=false, body-noscroll removed, inert removed', () => {
    const shell = mountModalShell()
    const sibling = document.createElement('div')
    document.body.appendChild(sibling)
    shell.open()
    shell.forceClose()
    expect(shell.isOpen()).toBe(false)
    expect(document.body.classList.contains('body-noscroll')).toBe(false)
    expect(sibling.hasAttribute('inert')).toBe(false)
    expect(sibling.hasAttribute('data-modal-inert')).toBe(false)
    shell.destroy()
  })

  it('forceClose() is no-op when already closed', () => {
    const shell = mountModalShell()
    expect(() => shell.forceClose()).not.toThrow()
    shell.destroy()
  })

  it('onAfterClose callback fires', () => {
    const cb = vi.fn()
    const shell = mountModalShell({ onAfterClose: cb })
    shell.open()
    shell.forceClose()
    expect(cb).toHaveBeenCalledTimes(1)
    shell.destroy()
  })

  it('close() with no dirty guard calls forceClose path', () => {
    const shell = mountModalShell()
    shell.open()
    shell.close()
    expect(shell.isOpen()).toBe(false)
    shell.destroy()
  })
})

// ─── setContent / setBody / setFooter ────────────────────────────────────────

describe('mountModalShell — content setters', () => {
  it('setContent() sets title and body', () => {
    const shell = mountModalShell()
    shell.setContent('Test Title', '<p id="bp">body</p>', '<button id="fp">ok</button>')
    expect(document.querySelector('#modal-title')?.textContent).toBe('Test Title')
    expect(document.querySelector('#bp')).toBeTruthy()
    expect(document.querySelector('#fp')).toBeTruthy()
    shell.destroy()
  })

  it('setBody() replaces only body', () => {
    const shell = mountModalShell()
    shell.setContent('T', '<p>old</p>')
    shell.setBody('<p id="new-body">new</p>')
    expect(document.querySelector('#new-body')).toBeTruthy()
    expect(document.querySelector('#modal-title')?.textContent).toBe('T')
    shell.destroy()
  })

  it('setFooter() replaces only footer', () => {
    const shell = mountModalShell()
    shell.setContent('T', '', '<button id="old-foot">old</button>')
    shell.setFooter('<button id="new-foot">new</button>')
    expect(document.querySelector('#new-foot')).toBeTruthy()
    expect(document.querySelector('#old-foot')).toBeFalsy()
    shell.destroy()
  })
})

// ─── cancel event ─────────────────────────────────────────────────────────────

describe('mountModalShell — cancel event', () => {
  it('cancel event is preventDefault()\'d', () => {
    const shell = mountModalShell()
    shell.open()
    const dialog = document.querySelector('#modal-portal') as HTMLDialogElement
    const ev = new Event('cancel', { cancelable: true })
    dialog.dispatchEvent(ev)
    expect(ev.defaultPrevented).toBe(true)
    shell.destroy()
  })
})

// ─── Escape key ───────────────────────────────────────────────────────────────

describe('mountModalShell — Escape key', () => {
  it('Escape (no open combobox) calls close', () => {
    const shell = mountModalShell()
    shell.open()
    const dialog = document.querySelector('#modal-portal') as HTMLDialogElement
    fireKeydown(dialog, 'Escape')
    expect(shell.isOpen()).toBe(false)
    shell.destroy()
  })

  it('Escape with open combobox does NOT close modal', () => {
    const shell = mountModalShell()
    shell.setBody('<ul class="combobox-listbox" id="cb-lb" role="listbox"></ul>')
    shell.open()

    const dialog = document.querySelector('#modal-portal') as HTMLDialogElement
    const listbox = dialog.querySelector<HTMLElement>('.combobox-listbox')!
    // listbox is visible (not hidden)
    listbox.hidden = false

    fireKeydown(dialog, 'Escape')
    // modal should NOT close — combobox was open
    expect(shell.isOpen()).toBe(true)

    shell.destroy()
  })

  it('Escape with hidden combobox closes modal', () => {
    const shell = mountModalShell()
    shell.setBody('<ul class="combobox-listbox" id="cb-lb" role="listbox" hidden></ul>')
    shell.open()

    const dialog = document.querySelector('#modal-portal') as HTMLDialogElement
    fireKeydown(dialog, 'Escape')
    expect(shell.isOpen()).toBe(false)
    shell.destroy()
  })
})

// ─── dirty guard ──────────────────────────────────────────────────────────────

describe('mountModalShell — dirty guard', () => {
  it('setDirtyGuard(fn): close() with dirty=false → closes immediately', () => {
    const shell = mountModalShell()
    shell.setDirtyGuard(() => false)
    shell.open()
    shell.close()
    expect(shell.isOpen()).toBe(false)
    shell.destroy()
  })

  it('setDirtyGuard(fn): dirty=true → opens confirm dialog instead of closing', async () => {
    const shell = mountModalShell()
    shell.setDirtyGuard(() => true)
    shell.open()

    // trigger close — confirm dialog should appear
    shell.close()
    // modal should still be open (pending confirm)
    expect(shell.isOpen()).toBe(true)

    // confirm dialog should be in DOM
    const confirmDlg = document.querySelector('.confirm-dialog') as HTMLDialogElement | null
    expect(confirmDlg).toBeTruthy()

    // click Cancel on confirm → modal stays open
    const cancelBtn = confirmDlg!.querySelector<HTMLButtonElement>('#confirm-cancel')
    cancelBtn?.click()
    // allow microtask
    await Promise.resolve()
    expect(shell.isOpen()).toBe(true)

    shell.forceClose()
    shell.destroy()
  })

  it('setDirtyGuard(fn): dirty=true → confirm OK → modal closes', async () => {
    const shell = mountModalShell()
    shell.setDirtyGuard(() => true)
    shell.open()
    shell.close()

    const confirmDlg = document.querySelector('.confirm-dialog') as HTMLDialogElement | null
    const okBtn = confirmDlg!.querySelector<HTMLButtonElement>('#confirm-ok')
    okBtn?.click()
    await Promise.resolve()
    expect(shell.isOpen()).toBe(false)
    shell.destroy()
  })

  it('forceClose() bypasses dirty guard', () => {
    const shell = mountModalShell()
    shell.setDirtyGuard(() => true)
    shell.open()
    shell.forceClose()
    expect(shell.isOpen()).toBe(false)
    shell.destroy()
  })
})

// ─── trapFocusStep ────────────────────────────────────────────────────────────

describe('mountModalShell — trapFocusStep', () => {
  it('forward wraps from last to first', () => {
    const shell = mountModalShell()
    shell.setBody(`
      <button id="btn1">One</button>
      <button id="btn2">Two</button>
      <button id="btn3">Three</button>
    `)
    shell.open()

    const btn3 = document.querySelector<HTMLButtonElement>('#btn3')!
    btn3.focus()
    shell.trapFocusStep('forward')
    // should wrap to first focusable (modal-close-btn or btn1)
    const focused = document.activeElement as HTMLElement
    expect(['modal-close-btn', 'btn1'].includes(focused?.id ?? '')).toBe(true)
    shell.destroy()
  })

  it('backward wraps from first to last', () => {
    const shell = mountModalShell()
    shell.setBody(`<button id="btnA">A</button>`)
    shell.open()

    // focus close button (first in modal)
    const closeBtn = document.querySelector<HTMLButtonElement>('#modal-close-btn')!
    closeBtn.focus()
    shell.trapFocusStep('backward')
    // should wrap to last focusable
    const focused = document.activeElement as HTMLElement
    expect(focused?.id).toBe('btnA')
    shell.destroy()
  })
})

// ─── destroy ─────────────────────────────────────────────────────────────────

describe('mountModalShell — destroy', () => {
  it('removes dialog element from DOM', () => {
    const shell = mountModalShell()
    expect(document.querySelector('#modal-portal')).toBeTruthy()
    shell.destroy()
    expect(document.querySelector('#modal-portal')).toBeFalsy()
  })

  it('destroy while open: closes first then removes', () => {
    const shell = mountModalShell()
    shell.open()
    shell.destroy()
    expect(document.querySelector('#modal-portal')).toBeFalsy()
    expect(document.body.classList.contains('body-noscroll')).toBe(false)
  })
})

// ─── openConfirmDialog ────────────────────────────────────────────────────────

describe('openConfirmDialog', () => {
  it('resolves true when OK clicked', async () => {
    const promise = openConfirmDialog({ title: 'Test', message: 'Are you sure?' })
    const dlg = document.querySelector('.confirm-dialog') as HTMLDialogElement
    expect(dlg).toBeTruthy()
    dlg.querySelector<HTMLButtonElement>('#confirm-ok')?.click()
    const result = await promise
    expect(result).toBe(true)
    expect(document.querySelector('.confirm-dialog')).toBeFalsy()
  })

  it('resolves false when Cancel clicked', async () => {
    const promise = openConfirmDialog({ title: 'Test', message: 'msg' })
    const dlg = document.querySelector('.confirm-dialog') as HTMLDialogElement
    dlg.querySelector<HTMLButtonElement>('#confirm-cancel')?.click()
    const result = await promise
    expect(result).toBe(false)
  })

  it('resolves false on Escape', async () => {
    const promise = openConfirmDialog({ title: 'T', message: 'm' })
    const dlg = document.querySelector('.confirm-dialog') as HTMLDialogElement
    fireKeydown(dlg, 'Escape')
    const result = await promise
    expect(result).toBe(false)
  })

  it('uses custom labels', () => {
    const p = openConfirmDialog({ title: 'T', message: 'm', confirmLabel: 'Yes', cancelLabel: 'No', danger: true })
    const dlg = document.querySelector('.confirm-dialog')!
    expect(dlg.querySelector('#confirm-ok')?.textContent?.trim()).toBe('Yes')
    expect(dlg.querySelector('#confirm-cancel')?.textContent?.trim()).toBe('No')
    expect(dlg.querySelector('#confirm-ok')?.classList.contains('btn-danger')).toBe(true)
    dlg.querySelector<HTMLButtonElement>('#confirm-cancel')?.click()
    return p
  })

  it('removes dialog from DOM after resolution', async () => {
    const p = openConfirmDialog({ title: 'T', message: 'm' })
    document.querySelector<HTMLButtonElement>('#confirm-ok')?.click()
    await p
    expect(document.querySelector('.confirm-dialog')).toBeFalsy()
  })
})
