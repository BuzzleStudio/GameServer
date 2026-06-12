// src/modules/drawer-resize.ts
// Left-edge drag-to-resize controller for #mail-drawer.
// Pure math functions are exported for unit testing without DOM.

// ── Constants ──────────────────────────────────────────────────────────────────

export const MIN_W       = 620   // px — comfortable form floor
export const MAX_W_ABS   = 1200  // px — hard cap
export const DESKTOP_BP  = 768   // px — mirrors CSS @media breakpoint
export const STEP        = 32    // px — keyboard arrow step
export const STORAGE_KEY = 'adminweb.mailModal.width'

// ── Pure math (unit-testable, no DOM) ─────────────────────────────────────────

/**
 * Clamp raw pixel width to [MIN_W, min(MAX_W_ABS, viewportW-32)].
 * Guard: when maxAvail < MIN_W (tiny viewport), viewport bound wins —
 * returning MIN_W would exceed the viewport which is never acceptable.
 */
export function clampDrawerWidth(raw: number, viewportW: number): number {
  const maxAvail = Math.min(MAX_W_ABS, viewportW - 32)
  if (maxAvail < MIN_W) return maxAvail   // tiny viewport: don't exceed it
  return Math.max(MIN_W, Math.min(raw, maxAvail))
}

/**
 * Compute next drawer width from a drag gesture.
 * RIGHT edge is fixed → dragging handle LEFT grows the drawer.
 *   delta = startX − currentX  (positive = moved left = grow)
 */
export function computeNextWidth(
  startWidth: number,
  startX: number,
  currentX: number,
  viewportW: number,
): number {
  const delta = startX - currentX   // positive = moved left = expand
  return clampDrawerWidth(startWidth + delta, viewportW)
}

/**
 * Read, validate, and return a persisted width from localStorage.
 * Returns null when: mobile (< DESKTOP_BP), key absent, unparseable,
 * or the stored value would be clamped by more than 1px (stale).
 */
export function readPersistedWidth(viewportW: number): number | null {
  if (viewportW < DESKTOP_BP) return null
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return null
    const n = parseInt(raw, 10)
    if (!isFinite(n)) return null
    const clamped = clampDrawerWidth(n, viewportW)
    return Math.abs(clamped - n) > 1 ? null : clamped
  } catch { return null }
}

/**
 * Persist drawer width to localStorage (call on drag-end only, never per-move).
 */
export function persistWidth(w: number): void {
  try { localStorage.setItem(STORAGE_KEY, String(Math.round(w))) } catch { /* storage quota */ }
}

// ── Types ──────────────────────────────────────────────────────────────────────

export interface DrawerResizeHandle {
  /** Call from open() — applies stored width if valid and on desktop. */
  applyPersistedWidth(): void
  /** Remove --drawer-width property and clear localStorage. */
  resetWidth(): void
  /** Remove handle element and all event listeners. Call from destroy(). */
  destroy(): void
}

// ── DOM controller ─────────────────────────────────────────────────────────────

/**
 * Attach a left-edge resize handle to drawerEl.
 * The handle element is prepended as the first child of drawerEl so it
 * sits above all other content in the drawer's flex column.
 */
export function createDrawerResize(drawerEl: HTMLElement): DrawerResizeHandle {
  // ── Handle element ───────────────────────────────────────────────────────────
  const handle = document.createElement('div')
  handle.className = 'drawer-resize-handle'
  handle.setAttribute('role', 'separator')
  handle.setAttribute('aria-label', 'Resize mail dialog')
  handle.setAttribute('aria-orientation', 'vertical')
  handle.setAttribute('tabindex', '0')
  handle.setAttribute('title', 'Drag to resize. Double-click to reset.')
  drawerEl.insertBefore(handle, drawerEl.firstChild)

  // ── Drag state ───────────────────────────────────────────────────────────────
  let startX      = 0
  let startWidth  = 0
  let pendingRaf: number | null = null
  let lastKnownW: number | null = null
  let dragging    = false

  // ── Private helpers ──────────────────────────────────────────────────────────

  function _applyWidth(w: number): void {
    drawerEl.style.setProperty('--drawer-width', `${w}px`)
    lastKnownW = w
  }

  function _endDrag(pointerId: number, doPersist: boolean): void {
    if (!dragging) return
    dragging = false
    if (pendingRaf !== null) {
      cancelAnimationFrame(pendingRaf)
      pendingRaf = null
    }
    handle.removeEventListener('pointermove',   _onPointerMove)
    handle.removeEventListener('pointerup',     _onPointerUp)
    handle.removeEventListener('pointercancel', _onPointerCancel)
    // happy-dom does not implement pointer capture — guard with optional chaining
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    ;(handle as any).releasePointerCapture?.(pointerId)
    document.body.style.userSelect = ''
    drawerEl.removeAttribute('data-dragging')
    handle.removeAttribute('data-dragging')
    if (doPersist && lastKnownW !== null) {
      persistWidth(lastKnownW)
    }
  }

  // ── Drag event handlers (named so removeEventListener works) ─────────────────

  function _onPointerMove(e: PointerEvent): void {
    const next = computeNextWidth(startWidth, startX, e.clientX, window.innerWidth)
    if (pendingRaf !== null) cancelAnimationFrame(pendingRaf)
    pendingRaf = requestAnimationFrame(() => {
      pendingRaf = null
      _applyWidth(next)
    })
  }

  function _onPointerUp(e: PointerEvent): void {
    _endDrag(e.pointerId, true)
  }

  function _onPointerCancel(e: PointerEvent): void {
    _endDrag(e.pointerId, false)
  }

  // ── Permanent listeners (lifetime = this resize instance) ────────────────────

  function _onPointerDown(e: PointerEvent): void {
    if (window.innerWidth < DESKTOP_BP) return   // no-op on mobile
    if (e.button !== 0) return                   // left-button only
    dragging   = true
    startX     = e.clientX
    startWidth = drawerEl.getBoundingClientRect().width
    lastKnownW = startWidth
    // happy-dom does not implement pointer capture — guard with optional chaining
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    ;(handle as any).setPointerCapture?.(e.pointerId)
    document.body.style.userSelect = 'none'
    drawerEl.setAttribute('data-dragging', '')
    handle.setAttribute('data-dragging', '')
    handle.addEventListener('pointermove',   _onPointerMove)
    handle.addEventListener('pointerup',     _onPointerUp)
    handle.addEventListener('pointercancel', _onPointerCancel)
  }

  function _onDblClick(): void {
    resetWidth()
  }

  function _onKeyDown(e: KeyboardEvent): void {
    if (window.innerWidth < DESKTOP_BP) return
    const cur = drawerEl.getBoundingClientRect().width
    let next: number | null = null
    if (e.key === 'ArrowLeft')  { e.preventDefault(); next = clampDrawerWidth(cur + STEP, window.innerWidth) }
    if (e.key === 'ArrowRight') { e.preventDefault(); next = clampDrawerWidth(cur - STEP, window.innerWidth) }
    if (e.key === 'Home')       { e.preventDefault(); resetWidth(); return }
    if (next !== null) { _applyWidth(next); persistWidth(next) }
  }

  handle.addEventListener('pointerdown', _onPointerDown)
  handle.addEventListener('dblclick',    _onDblClick)
  handle.addEventListener('keydown',     _onKeyDown)

  // ── Public API ────────────────────────────────────────────────────────────────

  function applyPersistedWidth(): void {
    const vw    = window.innerWidth
    const saved = readPersistedWidth(vw)
    if (saved !== null) _applyWidth(saved)
  }

  function resetWidth(): void {
    drawerEl.style.removeProperty('--drawer-width')
    lastKnownW = null
    try { localStorage.removeItem(STORAGE_KEY) } catch { /* storage quota */ }
  }

  function destroy(): void {
    if (dragging) {
      // Abort any in-progress drag without persisting
      dragging = false
      handle.removeEventListener('pointermove',   _onPointerMove)
      handle.removeEventListener('pointerup',     _onPointerUp)
      handle.removeEventListener('pointercancel', _onPointerCancel)
      if (pendingRaf !== null) { cancelAnimationFrame(pendingRaf); pendingRaf = null }
      document.body.style.userSelect = ''
      drawerEl.removeAttribute('data-dragging')
      handle.removeAttribute('data-dragging')
    }
    handle.removeEventListener('pointerdown', _onPointerDown)
    handle.removeEventListener('dblclick',    _onDblClick)
    handle.removeEventListener('keydown',     _onKeyDown)
    handle.remove()
  }

  return { applyPersistedWidth, resetWidth, destroy }
}
