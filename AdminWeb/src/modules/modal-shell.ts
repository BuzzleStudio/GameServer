// src/modules/modal-shell.ts
// Singleton <dialog> wrapper: open/close, focus trap, Escape race guard,
// inert for background, scroll-lock, openConfirmDialog.

// ─── Types ────────────────────────────────────────────────────────────────────

export interface ModalShellConfig {
  /** Called after the dialog closes (after inert/scroll cleanup). */
  onAfterClose?: () => void
}

export interface ModalShellHandle {
  setContent(title: string, bodyHtml: string, footerHtml?: string): void
  setBody(bodyHtml: string): void
  setFooter(footerHtml: string): void
  open(openerEl?: Element | null): void
  close(): void
  forceClose(): void
  setDirtyGuard(fn: () => boolean): void
  isOpen(): boolean
  /**
   * Manually advance focus to the next/previous focusable element.
   * Exposed so tests (happy-dom does not move activeElement on Tab) can
   * call this directly to verify focus-trap logic.
   */
  trapFocusStep(direction: 'forward' | 'backward'): void
  destroy(): void
}

export interface ConfirmDialogOpts {
  title:         string
  message:       string
  confirmLabel?: string
  cancelLabel?:  string
  danger?:       boolean
}

// ─── Constants ────────────────────────────────────────────────────────────────

const FOCUSABLE_SEL =
  'button:not([disabled]), input:not([disabled]), select:not([disabled]), ' +
  'textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

// ─── openConfirmDialog ────────────────────────────────────────────────────────

/**
 * Opens a small confirm <dialog> above any open modal.
 * Returns Promise<boolean> — true = confirmed, false = cancelled.
 */
export function openConfirmDialog(opts: ConfirmDialogOpts): Promise<boolean> {
  return new Promise<boolean>((resolve) => {
    const dlg = document.createElement('dialog')
    dlg.className = 'confirm-dialog'
    dlg.setAttribute('aria-modal', 'true')
    dlg.setAttribute('aria-labelledby', 'confirm-dialog-title')

    const confirmLabel = opts.confirmLabel ?? 'Confirm'
    const cancelLabel  = opts.cancelLabel  ?? 'Cancel'
    const dangerClass  = opts.danger ? 'btn-danger' : 'btn-primary'

    dlg.innerHTML = `
<h2 class="confirm-dialog-title" id="confirm-dialog-title">${_esc(opts.title)}</h2>
<p  class="confirm-dialog-message">${_esc(opts.message)}</p>
<div class="confirm-dialog-actions">
  <button type="button" class="btn btn-ghost" id="confirm-cancel">${_esc(cancelLabel)}</button>
  <button type="button" class="btn ${dangerClass}" id="confirm-ok">${_esc(confirmLabel)}</button>
</div>`

    document.body.appendChild(dlg)
    dlg.showModal()

    // Focus Cancel by default (safe)
    const cancelBtn = dlg.querySelector<HTMLButtonElement>('#confirm-cancel')
    cancelBtn?.focus()

    // Escape → cancel (no dirty guard for confirm dialog itself)
    dlg.addEventListener('cancel', (e) => { e.preventDefault() })
    dlg.addEventListener('keydown', (e) => {
      if (e.key === 'Escape') { e.preventDefault(); finish(false) }
    }, true)

    function finish(result: boolean): void {
      dlg.close()
      dlg.remove()
      resolve(result)
    }

    dlg.querySelector('#confirm-ok')?.addEventListener('click', () => finish(true))
    dlg.querySelector('#confirm-cancel')?.addEventListener('click', () => finish(false))
  })
}

// ─── mountModalShell ─────────────────────────────────────────────────────────

export function mountModalShell(config?: ModalShellConfig): ModalShellHandle {
  let _openerEl:   Element | null = null
  let _isOpen      = false
  let _dirtyGuard: (() => boolean) | null = null

  // Enforce singleton portal — remove any existing portal before creating a new one.
  // In production both callers (send-form, drawer) guard with `if (!_modalHandle)` so
  // this path is only hit during testing or if the guard is bypassed.
  document.getElementById('modal-portal')?.remove()

  const dialogEl = document.createElement('dialog')
  dialogEl.id        = 'modal-portal'
  dialogEl.className = 'modal-shell'
  dialogEl.setAttribute('aria-modal', 'true')
  dialogEl.setAttribute('aria-labelledby', 'modal-title')
  dialogEl.innerHTML = `
<div class="modal-inner">
  <div class="modal-header" id="modal-header">
    <h2 class="modal-title" id="modal-title"></h2>
    <button type="button" class="btn btn-icon modal-close-btn"
            id="modal-close-btn" aria-label="Close">✕</button>
  </div>
  <div class="modal-body" id="modal-body"></div>
  <div class="modal-footer" id="modal-footer"></div>
</div>`

  document.body.appendChild(dialogEl)

  // ── Close button ────────────────────────────────────────────────────────────
  dialogEl.querySelector('#modal-close-btn')?.addEventListener('click', () => {
    _handleCloseRequest()
  })

  // ── Cancel event — always preventDefault (we handle Escape ourselves) ───────
  dialogEl.addEventListener('cancel', (e) => { e.preventDefault() })

  // ── Escape — capture phase (fires before combobox bubble-phase handler) ─────
  dialogEl.addEventListener('keydown', (e: KeyboardEvent) => {
    if (e.key !== 'Escape') return

    // At capture time the listbox is still visible if a combobox was open.
    const openListbox = dialogEl.querySelector('.combobox-listbox:not([hidden])')
    if (openListbox) {
      // A combobox is open — let it close via its own bubble-phase handler.
      return
    }

    e.preventDefault()
    _handleCloseRequest()
  }, true /* capture phase */)

  // ── Tab focus trap ──────────────────────────────────────────────────────────
  dialogEl.addEventListener('keydown', (e: KeyboardEvent) => {
    if (e.key !== 'Tab') return
    e.preventDefault()
    trapFocusStep(e.shiftKey ? 'backward' : 'forward')
  })

  // ── Helpers ─────────────────────────────────────────────────────────────────

  function _getFocusable(): HTMLElement[] {
    return Array.from(dialogEl.querySelectorAll<HTMLElement>(FOCUSABLE_SEL))
  }

  function _focusFirst(): void {
    const items = _getFocusable()
    if (items.length > 0) items[0].focus()
  }

  function trapFocusStep(direction: 'forward' | 'backward'): void {
    const items = _getFocusable()
    if (items.length === 0) return
    const current = document.activeElement
    const idx = items.indexOf(current as HTMLElement)
    if (direction === 'forward') {
      items[idx < items.length - 1 ? idx + 1 : 0].focus()
    } else {
      items[idx > 0 ? idx - 1 : items.length - 1].focus()
    }
  }

  function _handleCloseRequest(): void {
    if (_dirtyGuard && _dirtyGuard()) {
      // dirty guard returns true = form is dirty — ask user
      openConfirmDialog({
        title:        'Discard Changes?',
        message:      'Your attachment changes have not been saved.',
        confirmLabel: 'Discard',
        danger:       true,
      }).then((confirmed) => {
        if (confirmed) _doClose()
      })
      return
    }
    _doClose()
  }

  function _doClose(): void {
    if (!_isOpen) return
    dialogEl.close()
    _isOpen = false

    // Restore inert on all body children
    document.querySelectorAll<HTMLElement>('[data-modal-inert]').forEach(el => {
      el.removeAttribute('inert')
      el.removeAttribute('data-modal-inert')
    })
    document.body.classList.remove('body-noscroll')

    // Restore focus
    if (_openerEl && typeof (_openerEl as HTMLElement).focus === 'function') {
      (_openerEl as HTMLElement).focus()
    }

    config?.onAfterClose?.()
  }

  // ── Public API ───────────────────────────────────────────────────────────────

  function setContent(title: string, bodyHtml: string, footerHtml = ''): void {
    const titleEl  = dialogEl.querySelector('#modal-title')
    const bodyEl   = dialogEl.querySelector('#modal-body')
    const footerEl = dialogEl.querySelector('#modal-footer')
    if (titleEl)  titleEl.textContent  = title
    if (bodyEl)   bodyEl.innerHTML     = bodyHtml
    if (footerEl) footerEl.innerHTML   = footerHtml
  }

  function setBody(bodyHtml: string): void {
    const bodyEl = dialogEl.querySelector('#modal-body')
    if (bodyEl) bodyEl.innerHTML = bodyHtml
  }

  function setFooter(footerHtml: string): void {
    const footerEl = dialogEl.querySelector('#modal-footer')
    if (footerEl) footerEl.innerHTML = footerHtml
  }

  function open(openerEl?: Element | null): void {
    if (_isOpen) return
    _openerEl = openerEl ?? null
    _isOpen   = true

    // Mark all other body children as inert
    Array.from(document.body.children).forEach(el => {
      if (el !== dialogEl && !el.hasAttribute('inert')) {
        el.setAttribute('inert', '')
        el.setAttribute('data-modal-inert', '')
      }
    })

    dialogEl.showModal()
    document.body.classList.add('body-noscroll')
    _focusFirst()
  }

  function close(): void {
    _handleCloseRequest()
  }

  function forceClose(): void {
    _doClose()
  }

  function setDirtyGuard(fn: () => boolean): void {
    _dirtyGuard = fn
  }

  function isOpen(): boolean {
    return _isOpen
  }

  function destroy(): void {
    if (_isOpen) _doClose()
    dialogEl.remove()
  }

  return {
    setContent, setBody, setFooter,
    open, close, forceClose,
    setDirtyGuard, isOpen,
    trapFocusStep, destroy,
  }
}

// ─── Internal helper ─────────────────────────────────────────────────────────

function _esc(s: string): string {
  return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
}
