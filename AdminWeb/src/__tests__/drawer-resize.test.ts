// @vitest-environment happy-dom
/**
 * drawer-resize.test.ts — drawer-resize.ts pure math + DOM smoke
 *
 * Design ref: workspace/modal-resize-design.md §3–§10, §12, §15
 * Module:     src/modules/drawer-resize.ts
 *
 * Sections:
 *   A. clampDrawerWidth — pure, no DOM
 *   B. computeNextWidth — pure, no DOM
 *   C. readPersistedWidth / persistWidth — pure (localStorage)
 *   D. createDrawerResize — DOM smoke (happy-dom)
 *   E. Keyboard a11y
 *   F. Mobile guard (window.innerWidth stub)
 */
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import {
  clampDrawerWidth,
  computeNextWidth,
  readPersistedWidth,
  persistWidth,
  createDrawerResize,
  MIN_W,
  MAX_W_ABS,
  DESKTOP_BP,
  STEP,
  STORAGE_KEY,
} from '../modules/drawer-resize'

// ─── A. clampDrawerWidth ──────────────────────────────────────────────────────

describe('clampDrawerWidth', () => {
  it('clamps raw below MIN_W up to MIN_W', () => {
    expect(clampDrawerWidth(500, 1440)).toBe(MIN_W)   // 500 < 620 → 620
  })

  it('passes through in-range value unchanged', () => {
    expect(clampDrawerWidth(800, 1440)).toBe(800)
  })

  it('clamps raw above MAX_W_ABS down to MAX_W_ABS', () => {
    // maxAvail = min(1200, 1440-32) = min(1200, 1408) = 1200
    expect(clampDrawerWidth(1300, 1440)).toBe(MAX_W_ABS)
  })

  it('clamps to viewport-32 when viewport-32 < MAX_W_ABS', () => {
    // viewportW = 900 → maxAvail = min(1200, 868) = 868; 868 >= 620
    expect(clampDrawerWidth(1000, 900)).toBe(868)
  })

  it('tiny viewport: maxAvail < MIN_W → returns maxAvail (NOT MIN_W)', () => {
    // viewportW = 400 → maxAvail = min(1200, 368) = 368; 368 < 620 → guard fires
    expect(clampDrawerWidth(850, 400)).toBe(368)
    // result must be ≤ viewportW - 32
    expect(clampDrawerWidth(850, 400)).toBeLessThanOrEqual(400 - 32)
  })

  it('tiny viewport with very small raw: same guard fires → returns maxAvail', () => {
    expect(clampDrawerWidth(100, 400)).toBe(368)
  })

  it('exact MIN_W passes unchanged', () => {
    expect(clampDrawerWidth(MIN_W, 1440)).toBe(MIN_W)
  })

  it('exact maxAvail passes unchanged', () => {
    // viewportW = 1440, maxAvail = 1200
    expect(clampDrawerWidth(MAX_W_ABS, 1440)).toBe(MAX_W_ABS)
  })
})

// ─── B. computeNextWidth ──────────────────────────────────────────────────────

describe('computeNextWidth', () => {
  it('drag left (startX > currentX) → delta positive → grows drawer', () => {
    // startX=800, curX=700 → delta=100 → raw=900+100=1000 → 1000 in range
    expect(computeNextWidth(900, 800, 700, 1440)).toBe(1000)
  })

  it('drag right (currentX > startX) → delta negative → shrinks drawer', () => {
    // startX=800, curX=900 → delta=-100 → raw=900-100=800
    expect(computeNextWidth(900, 800, 900, 1440)).toBe(800)
  })

  it('large right drag shrinks and clamps to MIN_W', () => {
    // startX=800, curX=1500 → delta=-700 → raw=900-700=200 → clamped to MIN_W
    expect(computeNextWidth(900, 800, 1500, 1440)).toBe(MIN_W)
  })

  it('no movement returns startWidth (clamped if needed)', () => {
    expect(computeNextWidth(900, 500, 500, 1440)).toBe(900)
  })

  it('large left drag clamps to maxAvail', () => {
    // startW=800, startX=1000, curX=0 → delta=1000 → raw=1800 → clamped to 1200
    expect(computeNextWidth(800, 1000, 0, 1440)).toBe(MAX_W_ABS)
  })
})

// ─── C. readPersistedWidth / persistWidth ────────────────────────────────────

describe('readPersistedWidth / persistWidth', () => {
  beforeEach(() => { localStorage.clear() })
  afterEach(() => { localStorage.clear() })

  it('returns null when no value stored', () => {
    expect(readPersistedWidth(1440)).toBeNull()
  })

  it('returns null on mobile viewport (< DESKTOP_BP)', () => {
    localStorage.setItem(STORAGE_KEY, '800')
    expect(readPersistedWidth(400)).toBeNull()
    expect(readPersistedWidth(DESKTOP_BP - 1)).toBeNull()
  })

  it('returns valid stored value on desktop', () => {
    localStorage.setItem(STORAGE_KEY, '800')
    expect(readPersistedWidth(1440)).toBe(800)
  })

  it('returns null for NaN stored value', () => {
    localStorage.setItem(STORAGE_KEY, 'NaN')
    expect(readPersistedWidth(1440)).toBeNull()
  })

  it('returns null for non-numeric string', () => {
    localStorage.setItem(STORAGE_KEY, 'abc')
    expect(readPersistedWidth(1440)).toBeNull()
  })

  it('returns null when stored value would clamp by > 1px (stale below MIN_W)', () => {
    // 400 is far below MIN_W (620); after clamp diff is 220 > 1 → discard
    localStorage.setItem(STORAGE_KEY, '400')
    expect(readPersistedWidth(1440)).toBeNull()
  })

  it('returns null when stored value would clamp by > 1px (stale above maxAvail)', () => {
    // Store 1500, maxAvail for vw=1440 = 1200; diff = 300 > 1 → discard
    localStorage.setItem(STORAGE_KEY, '1500')
    expect(readPersistedWidth(1440)).toBeNull()
  })

  it('returns clamped value when within 1px tolerance', () => {
    // Store exactly MAX_W_ABS = 1200; clamp(1200, 1440) = 1200; diff = 0 ≤ 1 → keep
    localStorage.setItem(STORAGE_KEY, String(MAX_W_ABS))
    expect(readPersistedWidth(1440)).toBe(MAX_W_ABS)
  })

  it('persistWidth stores rounded integer', () => {
    persistWidth(832.7)
    expect(localStorage.getItem(STORAGE_KEY)).toBe('833')
  })

  it('persistWidth stores exact integer unchanged', () => {
    persistWidth(900)
    expect(localStorage.getItem(STORAGE_KEY)).toBe('900')
  })
})

// ─── D. createDrawerResize — DOM smoke ────────────────────────────────────────

describe('createDrawerResize — DOM smoke', () => {
  let drawerEl: HTMLElement

  beforeEach(() => {
    drawerEl = document.createElement('aside')
    document.body.appendChild(drawerEl)
    localStorage.clear()
    // Default desktop viewport
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1440 })
  })

  afterEach(() => {
    drawerEl.remove()
    localStorage.clear()
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 })
  })

  it('prepends handle as first child of drawerEl', () => {
    const resize = createDrawerResize(drawerEl)
    expect(drawerEl.firstChild).not.toBeNull()
    expect((drawerEl.firstChild as HTMLElement).className).toBe('drawer-resize-handle')
    resize.destroy()
  })

  it('handle has role="separator"', () => {
    const resize = createDrawerResize(drawerEl)
    const handle = drawerEl.querySelector('.drawer-resize-handle')!
    expect(handle.getAttribute('role')).toBe('separator')
    resize.destroy()
  })

  it('handle has aria-label="Resize mail dialog"', () => {
    const resize = createDrawerResize(drawerEl)
    const handle = drawerEl.querySelector('.drawer-resize-handle')!
    expect(handle.getAttribute('aria-label')).toBe('Resize mail dialog')
    resize.destroy()
  })

  it('handle has tabindex="0"', () => {
    const resize = createDrawerResize(drawerEl)
    const handle = drawerEl.querySelector('.drawer-resize-handle')!
    expect(handle.getAttribute('tabindex')).toBe('0')
    resize.destroy()
  })

  it('handle has aria-orientation="vertical"', () => {
    const resize = createDrawerResize(drawerEl)
    const handle = drawerEl.querySelector('.drawer-resize-handle')!
    expect(handle.getAttribute('aria-orientation')).toBe('vertical')
    resize.destroy()
  })

  it('applyPersistedWidth does nothing when no value stored', () => {
    const resize = createDrawerResize(drawerEl)
    resize.applyPersistedWidth()
    expect(drawerEl.style.getPropertyValue('--drawer-width')).toBe('')
    resize.destroy()
  })

  it('applyPersistedWidth applies stored valid width', () => {
    localStorage.setItem(STORAGE_KEY, '850')
    const resize = createDrawerResize(drawerEl)
    resize.applyPersistedWidth()
    expect(drawerEl.style.getPropertyValue('--drawer-width')).toBe('850px')
    resize.destroy()
  })

  it('destroy() removes handle element from DOM', () => {
    const resize = createDrawerResize(drawerEl)
    expect(drawerEl.querySelector('.drawer-resize-handle')).not.toBeNull()
    resize.destroy()
    expect(drawerEl.querySelector('.drawer-resize-handle')).toBeNull()
  })

  it('resetWidth() removes --drawer-width property', () => {
    const resize = createDrawerResize(drawerEl)
    drawerEl.style.setProperty('--drawer-width', '800px')
    resize.resetWidth()
    expect(drawerEl.style.getPropertyValue('--drawer-width')).toBe('')
    resize.destroy()
  })

  it('resetWidth() clears localStorage', () => {
    localStorage.setItem(STORAGE_KEY, '800')
    const resize = createDrawerResize(drawerEl)
    resize.resetWidth()
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull()
    resize.destroy()
  })

  it('double-click on handle calls resetWidth (clears --drawer-width)', () => {
    const resize = createDrawerResize(drawerEl)
    drawerEl.style.setProperty('--drawer-width', '800px')
    const handle = drawerEl.querySelector('.drawer-resize-handle')!
    handle.dispatchEvent(new MouseEvent('dblclick', { bubbles: true }))
    expect(drawerEl.style.getPropertyValue('--drawer-width')).toBe('')
    resize.destroy()
  })

  it('pointerdown with missing setPointerCapture does not throw', () => {
    const resize = createDrawerResize(drawerEl)
    const handle = drawerEl.querySelector<HTMLElement>('.drawer-resize-handle')!
    // Stub getBoundingClientRect to return a valid width
    vi.spyOn(drawerEl, 'getBoundingClientRect').mockReturnValue({
      width: 800, height: 600, top: 0, left: 0, right: 800, bottom: 600, x: 0, y: 0,
      toJSON() { return {} },
    } as DOMRect)
    // Remove setPointerCapture to simulate environments lacking it
    const origSet = handle.setPointerCapture?.bind(handle)
    delete (handle as unknown as Record<string, unknown>).setPointerCapture
    expect(() => {
      handle.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, button: 0, pointerId: 1, clientX: 500 }))
    }).not.toThrow()
    // Restore
    if (origSet) handle.setPointerCapture = origSet
    resize.destroy()
  })

  it('pointerdown sets data-dragging on drawer and handle', () => {
    const resize = createDrawerResize(drawerEl)
    const handle = drawerEl.querySelector<HTMLElement>('.drawer-resize-handle')!
    vi.spyOn(drawerEl, 'getBoundingClientRect').mockReturnValue({
      width: 800, height: 600, top: 0, left: 0, right: 800, bottom: 600, x: 0, y: 0,
      toJSON() { return {} },
    } as DOMRect)
    handle.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, button: 0, pointerId: 1, clientX: 500 }))
    expect(drawerEl.hasAttribute('data-dragging')).toBe(true)
    expect(handle.hasAttribute('data-dragging')).toBe(true)
    resize.destroy()
  })

  it('destroy() cleans up data-dragging if drag was in progress', () => {
    const resize = createDrawerResize(drawerEl)
    const handle = drawerEl.querySelector<HTMLElement>('.drawer-resize-handle')!
    vi.spyOn(drawerEl, 'getBoundingClientRect').mockReturnValue({
      width: 800, height: 600, top: 0, left: 0, right: 800, bottom: 600, x: 0, y: 0,
      toJSON() { return {} },
    } as DOMRect)
    handle.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, button: 0, pointerId: 1, clientX: 500 }))
    expect(drawerEl.hasAttribute('data-dragging')).toBe(true)
    resize.destroy()
    expect(drawerEl.hasAttribute('data-dragging')).toBe(false)
    expect(document.body.style.userSelect).toBe('')
  })
})

// ─── E. Keyboard a11y ────────────────────────────────────────────────────────

describe('createDrawerResize — keyboard a11y', () => {
  let drawerEl: HTMLElement

  beforeEach(() => {
    drawerEl = document.createElement('aside')
    document.body.appendChild(drawerEl)
    localStorage.clear()
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1440 })
  })

  afterEach(() => {
    drawerEl.remove()
    localStorage.clear()
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 })
  })

  function mockBcr(width: number) {
    vi.spyOn(drawerEl, 'getBoundingClientRect').mockReturnValue({
      width, height: 600, top: 0, left: 0, right: width, bottom: 600, x: 0, y: 0,
      toJSON() { return {} },
    } as DOMRect)
  }

  it('ArrowLeft grows drawer by STEP', () => {
    mockBcr(800)
    const resize = createDrawerResize(drawerEl)
    const handle = drawerEl.querySelector<HTMLElement>('.drawer-resize-handle')!
    handle.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }))
    expect(drawerEl.style.getPropertyValue('--drawer-width')).toBe(`${800 + STEP}px`)
    resize.destroy()
  })

  it('ArrowRight shrinks drawer by STEP', () => {
    mockBcr(900)
    const resize = createDrawerResize(drawerEl)
    const handle = drawerEl.querySelector<HTMLElement>('.drawer-resize-handle')!
    handle.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }))
    expect(drawerEl.style.getPropertyValue('--drawer-width')).toBe(`${900 - STEP}px`)
    resize.destroy()
  })

  it('ArrowRight at MIN_W clamps to MIN_W', () => {
    mockBcr(MIN_W)
    const resize = createDrawerResize(drawerEl)
    const handle = drawerEl.querySelector<HTMLElement>('.drawer-resize-handle')!
    handle.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }))
    expect(drawerEl.style.getPropertyValue('--drawer-width')).toBe(`${MIN_W}px`)
    resize.destroy()
  })

  it('ArrowLeft persists the new width', () => {
    mockBcr(800)
    const resize = createDrawerResize(drawerEl)
    const handle = drawerEl.querySelector<HTMLElement>('.drawer-resize-handle')!
    handle.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }))
    expect(localStorage.getItem(STORAGE_KEY)).toBe(String(800 + STEP))
    resize.destroy()
  })

  it('Home resets --drawer-width and clears localStorage', () => {
    localStorage.setItem(STORAGE_KEY, '850')
    const resize = createDrawerResize(drawerEl)
    drawerEl.style.setProperty('--drawer-width', '850px')
    const handle = drawerEl.querySelector<HTMLElement>('.drawer-resize-handle')!
    handle.dispatchEvent(new KeyboardEvent('keydown', { key: 'Home', bubbles: true }))
    expect(drawerEl.style.getPropertyValue('--drawer-width')).toBe('')
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull()
    resize.destroy()
  })

  it('unrecognised key does nothing', () => {
    mockBcr(800)
    const resize = createDrawerResize(drawerEl)
    drawerEl.style.setProperty('--drawer-width', '800px')
    const handle = drawerEl.querySelector<HTMLElement>('.drawer-resize-handle')!
    handle.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }))
    expect(drawerEl.style.getPropertyValue('--drawer-width')).toBe('800px')
    resize.destroy()
  })
})

// ─── F. Mobile guard (window.innerWidth stub) ─────────────────────────────────

describe('createDrawerResize — mobile guard', () => {
  let drawerEl: HTMLElement

  beforeEach(() => {
    drawerEl = document.createElement('aside')
    document.body.appendChild(drawerEl)
    localStorage.clear()
  })

  afterEach(() => {
    drawerEl.remove()
    localStorage.clear()
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 })
  })

  it('readPersistedWidth returns null on mobile viewport', () => {
    localStorage.setItem(STORAGE_KEY, '800')
    expect(readPersistedWidth(400)).toBeNull()
    expect(readPersistedWidth(DESKTOP_BP - 1)).toBeNull()
  })

  it('applyPersistedWidth does not set --drawer-width on mobile', () => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 600 })
    localStorage.setItem(STORAGE_KEY, '800')
    const resize = createDrawerResize(drawerEl)
    resize.applyPersistedWidth()
    expect(drawerEl.style.getPropertyValue('--drawer-width')).toBe('')
    resize.destroy()
  })

  it('pointerdown returns early on mobile — no data-dragging set', () => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 600 })
    const resize = createDrawerResize(drawerEl)
    const handle = drawerEl.querySelector<HTMLElement>('.drawer-resize-handle')!
    handle.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, button: 0, pointerId: 1, clientX: 300 }))
    expect(drawerEl.hasAttribute('data-dragging')).toBe(false)
    resize.destroy()
  })

  it('keyboard ArrowLeft does nothing on mobile', () => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 600 })
    const resize = createDrawerResize(drawerEl)
    const handle = drawerEl.querySelector<HTMLElement>('.drawer-resize-handle')!
    handle.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }))
    expect(drawerEl.style.getPropertyValue('--drawer-width')).toBe('')
    resize.destroy()
  })
})

// ─── G. Pointer drag simulation ───────────────────────────────────────────────
// QA-TESTER: DRAG-01..14 — pointermove → width change + localStorage on dragend.
// rAF is mocked synchronous per describe block. Events dispatched on handle (impl
// uses setPointerCapture to route events to handle element).

describe('createDrawerResize — pointer drag simulation (DRAG)', () => {
  let drawerEl: HTMLElement
  let resize: ReturnType<typeof createDrawerResize>
  let handle: HTMLElement
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
let bcrSpy: any
  let pendingRafCb: FrameRequestCallback | null = null

  beforeEach(() => {
    // Synchronous rAF: callback stored for manual flush via rafFlush()
    pendingRafCb = null
    vi.stubGlobal('requestAnimationFrame', (cb: FrameRequestCallback) => {
      pendingRafCb = cb
      return 1
    })
    vi.stubGlobal('cancelAnimationFrame', () => { pendingRafCb = null })

    drawerEl = document.createElement('aside')
    document.body.appendChild(drawerEl)
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1440 })
    bcrSpy = vi.spyOn(drawerEl, 'getBoundingClientRect').mockReturnValue({
      width: 900, height: 600, top: 0, left: 0, right: 900, bottom: 600, x: 0, y: 0,
      toJSON() { return {} },
    } as DOMRect)
    resize = createDrawerResize(drawerEl)
    handle = drawerEl.querySelector<HTMLElement>('.drawer-resize-handle')!
    localStorage.clear()
  })

  afterEach(() => {
    resize.destroy()
    drawerEl.remove()
    localStorage.clear()
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 })
    pendingRafCb = null
  })

  function rafFlush(): void {
    if (pendingRafCb !== null) { const cb = pendingRafCb; pendingRafCb = null; cb(0) }
  }

  function pd(clientX: number): void {
    handle.dispatchEvent(new PointerEvent('pointerdown', { button: 0, clientX, pointerId: 1, bubbles: true }))
  }
  function pm(clientX: number): void {
    handle.dispatchEvent(new PointerEvent('pointermove', { clientX, pointerId: 1, bubbles: true }))
    rafFlush()
  }
  function pu(clientX = 0): void {
    handle.dispatchEvent(new PointerEvent('pointerup', { clientX, pointerId: 1, bubbles: true }))
  }
  function pc(): void {
    handle.dispatchEvent(new PointerEvent('pointercancel', { pointerId: 1, bubbles: true }))
  }

  it('DRAG-01: drag left expands drawer (width increases above startWidth)', () => {
    pd(500); pm(400)
    const w = parseInt(drawerEl.style.getPropertyValue('--drawer-width'))
    expect(w).toBeGreaterThan(900)
  })

  it('DRAG-02: drag right shrinks drawer (width decreases below startWidth)', () => {
    pd(500); pm(600)
    const w = parseInt(drawerEl.style.getPropertyValue('--drawer-width'))
    expect(w).toBeLessThan(900)
  })

  it('DRAG-03: right edge fixed — 100px leftward drag on 900px drawer → 1000px', () => {
    // delta = 500-400 = 100; raw = 900+100 = 1000
    pd(500); pm(400)
    expect(drawerEl.style.getPropertyValue('--drawer-width')).toBe('1000px')
  })

  it('DRAG-04: drag right past MIN_W → clamped to MIN_W', () => {
    // delta = 500-900 = -400; raw = 500 → MIN_W(620)
    pd(500); pm(900)
    expect(drawerEl.style.getPropertyValue('--drawer-width')).toBe(`${MIN_W}px`)
  })

  it('DRAG-05: drag left past MAX_W_ABS → clamped to MAX_W_ABS', () => {
    bcrSpy.mockReturnValue({
      width: 1100, height: 600, top: 0, left: 0, right: 1100, bottom: 600, x: 0, y: 0,
      toJSON() { return {} },
    } as DOMRect)
    pd(500); pm(200)  // delta=300; raw=1400 → MAX_W_ABS(1200)
    expect(drawerEl.style.getPropertyValue('--drawer-width')).toBe(`${MAX_W_ABS}px`)
  })

  it('DRAG-06: pointerdown without setPointerCapture does not throw (optional chain)', () => {
    expect(() => pd(500)).not.toThrow()
  })

  it('DRAG-07: pointerup without releasePointerCapture does not throw', () => {
    pd(500); expect(() => pu(500)).not.toThrow()
  })

  it('DRAG-09: data-dragging set on handle during drag', () => {
    pd(500)
    expect(handle.hasAttribute('data-dragging')).toBe(true)
  })

  it('DRAG-10: data-dragging removed from handle on pointerup', () => {
    pd(500); pu()
    expect(handle.hasAttribute('data-dragging')).toBe(false)
  })

  it('DRAG-11: data-dragging set on drawerEl during drag', () => {
    pd(500)
    expect(drawerEl.hasAttribute('data-dragging')).toBe(true)
  })

  it('DRAG-12: data-dragging removed from drawerEl on pointerup', () => {
    pd(500); pu()
    expect(drawerEl.hasAttribute('data-dragging')).toBe(false)
  })

  it('DRAG-13: localStorage written with final width on pointerup (not before)', () => {
    const setSpy = vi.spyOn(Storage.prototype, 'setItem')
    pd(500); pm(400)
    // No persist yet
    expect(setSpy.mock.calls.filter(([k]) => k === STORAGE_KEY).length).toBe(0)
    pu(400)
    // Persisted once on pointerup
    expect(setSpy.mock.calls.filter(([k]) => k === STORAGE_KEY).length).toBe(1)
    expect(localStorage.getItem(STORAGE_KEY)).toBe('1000')
  })

  it('DRAG-14: multiple pointermove events — width reflects last move', () => {
    pd(500)
    pm(450)  // delta=50  → raw=950
    pm(400)  // delta=100 → raw=1000
    pm(350)  // delta=150 → raw=1050
    expect(drawerEl.style.getPropertyValue('--drawer-width')).toBe('1050px')
  })

  it('LIFE-03: localStorage NOT written on pointermove, only on pointerup', () => {
    const setSpy = vi.spyOn(Storage.prototype, 'setItem')
    pd(500)
    pm(450); pm(400); pm(350)
    expect(setSpy.mock.calls.filter(([k]) => k === STORAGE_KEY).length).toBe(0)
    pu(350)
    expect(setSpy.mock.calls.filter(([k]) => k === STORAGE_KEY).length).toBe(1)
  })

  it('LIFE-04: localStorage NOT written on pointercancel (doPersist=false)', () => {
    const setSpy = vi.spyOn(Storage.prototype, 'setItem')
    pd(500); pm(400); pc()
    expect(setSpy.mock.calls.filter(([k]) => k === STORAGE_KEY).length).toBe(0)
  })
})

// ─── H. Listener leak ─────────────────────────────────────────────────────────
// QA-TESTER: LEAK-01..07 — spy add/removeEventListener; no lingering listeners.

describe('createDrawerResize — listener leak (LEAK)', () => {
  let drawerEl: HTMLElement
  let resize: ReturnType<typeof createDrawerResize>
  let handle: HTMLElement
  let pendingRafCb: FrameRequestCallback | null = null

  beforeEach(() => {
    pendingRafCb = null
    vi.stubGlobal('requestAnimationFrame', (cb: FrameRequestCallback) => { pendingRafCb = cb; return 1 })
    vi.stubGlobal('cancelAnimationFrame', () => { pendingRafCb = null })

    drawerEl = document.createElement('aside')
    document.body.appendChild(drawerEl)
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1440 })
    vi.spyOn(drawerEl, 'getBoundingClientRect').mockReturnValue({
      width: 900, height: 600, top: 0, left: 0, right: 900, bottom: 600, x: 0, y: 0,
      toJSON() { return {} },
    } as DOMRect)
    resize = createDrawerResize(drawerEl)
    handle = drawerEl.querySelector<HTMLElement>('.drawer-resize-handle')!
    localStorage.clear()
  })

  afterEach(() => {
    resize.destroy()
    drawerEl.remove()
    localStorage.clear()
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 })
    pendingRafCb = null
  })

  function pd(clientX: number): void {
    handle.dispatchEvent(new PointerEvent('pointerdown', { button: 0, clientX, pointerId: 1, bubbles: true }))
  }
  function pu(): void {
    handle.dispatchEvent(new PointerEvent('pointerup', { pointerId: 1, bubbles: true }))
  }
  function pc(): void {
    handle.dispatchEvent(new PointerEvent('pointercancel', { pointerId: 1, bubbles: true }))
  }

  it('LEAK-01: pointermove listener added on pointerdown', () => {
    const addSpy = vi.spyOn(handle, 'addEventListener')
    pd(500)
    expect(addSpy.mock.calls.filter(([ev]) => ev === 'pointermove').length).toBe(1)
  })

  it('LEAK-02: pointermove listener removed on pointerup', () => {
    pd(500)
    const remSpy = vi.spyOn(handle, 'removeEventListener')
    pu()
    expect(remSpy.mock.calls.filter(([ev]) => ev === 'pointermove').length).toBe(1)
  })

  it('LEAK-03: pointerup listener self-removed on pointerup', () => {
    pd(500)
    const remSpy = vi.spyOn(handle, 'removeEventListener')
    pu()
    expect(remSpy.mock.calls.filter(([ev]) => ev === 'pointerup').length).toBe(1)
  })

  it('LEAK-04: pointercancel listener removed on pointercancel', () => {
    pd(500)
    const remSpy = vi.spyOn(handle, 'removeEventListener')
    pc()
    expect(remSpy.mock.calls.filter(([ev]) => ev === 'pointercancel').length).toBe(1)
  })

  it('LEAK-05: destroy() during active drag cleans all state, no throw', () => {
    pd(500)
    expect(drawerEl.hasAttribute('data-dragging')).toBe(true)
    expect(() => resize.destroy()).not.toThrow()
    expect(drawerEl.hasAttribute('data-dragging')).toBe(false)
    expect(document.body.style.userSelect).toBe('')
  })

  it('LEAK-06: no pointermove listener added before any drag', () => {
    const addSpy = vi.spyOn(handle, 'addEventListener')
    expect(addSpy.mock.calls.filter(([ev]) => ev === 'pointermove').length).toBe(0)
  })

  it('LEAK-07: two complete drag cycles — pointermove add/remove counts balanced', () => {
    const addSpy = vi.spyOn(handle, 'addEventListener')
    const remSpy = vi.spyOn(handle, 'removeEventListener')
    // Cycle 1
    pd(500); pu()
    // Cycle 2
    pd(500); pu()
    const adds = addSpy.mock.calls.filter(([ev]) => ev === 'pointermove').length
    const rems = remSpy.mock.calls.filter(([ev]) => ev === 'pointermove').length
    expect(adds).toBe(2)
    expect(rems).toBe(2)
  })
})

// ─── I. Integration via createMailEditorDrawer ────────────────────────────────
// QA-TESTER: LIFE-01/02, ATT-01..05, MOB-04, NEST-03, BUG-01 exposure.

import {
  createMailEditorDrawer,
  type DrawerDeps,
} from '../modules/mail-editor-drawer'
import type { MailRecord } from '../types'

const MAIL_A: MailRecord = {
  MessageId: 'msg-aaa-001',
  MailInfo: { Title: 'Hello World', Content: 'Mail body here.', Attachment: [] },
}
const MAIL_WITH_ATTS: MailRecord = {
  MessageId: 'msg-atts-001',
  MailInfo: {
    Title: 'With Attachments', Content: 'Body',
    Attachment: [
      { AssetType: 'Currency', PayoutAssetId: 'gem',  PayoutAmount: 5,  Chance: 1 },
      { AssetType: 'Currency', PayoutAssetId: 'gold', PayoutAmount: 10, Chance: 1 },
    ],
  },
}

function makeDeps(overrides: Partial<DrawerDeps> = {}): DrawerDeps {
  return {
    getEnv:          () => 'test',
    isBusy:          () => false,
    isConnected:     () => true,
    currencyOptions: [{ id: 'gem', label: 'Gems' }, { id: 'gold', label: 'Gold' }],
    itemOptions:     [],
    ticketOptions:   [],
    onSave:          vi.fn(),
    onExpire:        vi.fn(),
    onDelete:        vi.fn(),
    onCopyJson:      vi.fn(),
    ...overrides,
  }
}

describe('Integration — createMailEditorDrawer width persistence (LIFE)', () => {
  beforeEach(() => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1440 })
    localStorage.clear()
  })
  afterEach(() => {
    document.body.innerHTML = ''
    document.body.className = ''
    localStorage.clear()
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 })
  })

  it('LIFE-01: open() with persisted width applies --drawer-width on drawerEl', () => {
    localStorage.setItem(STORAGE_KEY, '850')
    const drawer = createMailEditorDrawer(makeDeps())
    drawer.open(MAIL_A)
    const drawerEl = document.getElementById('mail-drawer') as HTMLElement
    // applyPersistedWidth() runs after _render(); sets CSS prop on drawerEl directly
    expect(drawerEl.style.getPropertyValue('--drawer-width')).toBe('850px')
    drawer.destroy()
  })

  it('LIFE-02: close() + reopen() restores width from localStorage', () => {
    const drawer = createMailEditorDrawer(makeDeps())
    drawer.open(MAIL_A)
    drawer.close()
    localStorage.setItem(STORAGE_KEY, '920')  // simulate what dragend wrote
    drawer.open(MAIL_A)
    const drawerEl = document.getElementById('mail-drawer') as HTMLElement
    expect(drawerEl.style.getPropertyValue('--drawer-width')).toBe('920px')
    drawer.destroy()
  })

  it('MOB-04: drawer.open() on mobile → --drawer-width NOT set', () => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 600 })
    localStorage.setItem(STORAGE_KEY, '850')
    const drawer = createMailEditorDrawer(makeDeps())
    drawer.open(MAIL_A)
    const drawerEl = document.getElementById('mail-drawer') as HTMLElement
    expect(drawerEl.style.getPropertyValue('--drawer-width')).toBe('')
    drawer.destroy()
  })
})

describe('Integration — attachment list structural after open (ATT)', () => {
  beforeEach(() => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1440 })
    localStorage.clear()
  })
  afterEach(() => {
    document.body.innerHTML = ''
    document.body.className = ''
    localStorage.clear()
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 })
  })

  it('ATT-01: att-list-row elements present in DOM (one per attachment)', () => {
    const drawer = createMailEditorDrawer(makeDeps())
    drawer.open(MAIL_WITH_ATTS)
    expect(document.querySelectorAll('.att-list-row').length).toBe(2)
    drawer.destroy()
  })

  it('ATT-02: Edit button present in attachment rows', () => {
    const drawer = createMailEditorDrawer(makeDeps())
    drawer.open(MAIL_WITH_ATTS)
    expect(document.querySelectorAll('[data-action="att-edit"]').length).toBeGreaterThan(0)
    drawer.destroy()
  })

  it('ATT-03: Duplicate button present in attachment rows', () => {
    const drawer = createMailEditorDrawer(makeDeps())
    drawer.open(MAIL_WITH_ATTS)
    expect(document.querySelectorAll('[data-action="att-duplicate"]').length).toBeGreaterThan(0)
    drawer.destroy()
  })

  it('ATT-04: Delete button present in attachment rows', () => {
    const drawer = createMailEditorDrawer(makeDeps())
    drawer.open(MAIL_WITH_ATTS)
    expect(document.querySelectorAll('[data-action="att-delete"]').length).toBeGreaterThan(0)
    drawer.destroy()
  })

  it('ATT-05: .drawer-footer present in DOM after open', () => {
    const drawer = createMailEditorDrawer(makeDeps())
    drawer.open(MAIL_WITH_ATTS)
    expect(document.querySelector('.drawer-footer')).not.toBeNull()
    drawer.destroy()
  })
})

describe('NEST-03 — inert attribute does NOT remove --drawer-width', () => {
  afterEach(() => { document.body.innerHTML = '' })

  it('CSS custom property survives modal-shell inert add/remove cycle', () => {
    const aside = document.createElement('aside')
    document.body.appendChild(aside)
    const rh = createDrawerResize(aside)
    aside.style.setProperty('--drawer-width', '950px')

    // modal-shell.ts sets inert on background elements when modal opens
    aside.setAttribute('inert', '')
    expect(aside.style.getPropertyValue('--drawer-width')).toBe('950px')
    aside.removeAttribute('inert')
    expect(aside.style.getPropertyValue('--drawer-width')).toBe('950px')

    rh.destroy()
  })
})

// ─── J. BUG-01 exposure ───────────────────────────────────────────────────────
// QA-TESTER: _render() in mail-editor-drawer.ts calls drawerEl.innerHTML = '...'
// which removes the resize handle from the DOM on every open(). After open(),
// the handle element is detached; user cannot see or interact with it.
//
// These tests assert CORRECT behavior — they FAIL while BUG-01 is unpatched.
// Fix: re-prepend handle after innerHTML assignment (or restructure to avoid it).

describe('BUG-01 — handle detached from DOM after open() (FAILS until fixed)', () => {
  beforeEach(() => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1440 })
    localStorage.clear()
  })
  afterEach(() => {
    document.body.innerHTML = ''
    document.body.className = ''
    localStorage.clear()
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 })
  })

  it('[BUG-01] handle remains in DOM as first child of drawerEl after open()', () => {
    // EXPECT: handle visible after open()
    // ACTUAL: _render() executes drawerEl.innerHTML = '...' which wipes the handle
    const drawer = createMailEditorDrawer(makeDeps())
    drawer.open(MAIL_A)
    const drawerEl = document.getElementById('mail-drawer') as HTMLElement
    const handle = drawerEl.querySelector('.drawer-resize-handle')
    expect(handle).not.toBeNull()  // FAILS: handle is null after _render()
    drawer.destroy()
  })

  it('[BUG-01] drawerEl.firstElementChild is .drawer-resize-handle after open()', () => {
    const drawer = createMailEditorDrawer(makeDeps())
    drawer.open(MAIL_A)
    const drawerEl = document.getElementById('mail-drawer') as HTMLElement
    // FAILS: firstElementChild is .drawer-header, not .drawer-resize-handle
    expect(drawerEl.firstElementChild?.classList.contains('drawer-resize-handle')).toBe(true)
    drawer.destroy()
  })

  it('[BUG-01] handle a11y attrs accessible after open()', () => {
    const drawer = createMailEditorDrawer(makeDeps())
    drawer.open(MAIL_A)
    const handle = document.querySelector('.drawer-resize-handle')
    // FAILS: handle not in DOM after open()
    expect(handle?.getAttribute('role')).toBe('separator')
    expect(handle?.getAttribute('aria-label')).toBe('Resize mail dialog')
    drawer.destroy()
  })
})

// ─── K. Additional pure-fn edge cases ────────────────────────────────────────

describe('readPersistedWidth — additional edge cases', () => {
  beforeEach(() => { localStorage.clear() })
  afterEach(() => { localStorage.clear() })

  it('PERS-13: localStorage.getItem throws → null (no rethrow)', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('quota') })
    expect(readPersistedWidth(1440)).toBeNull()
    vi.restoreAllMocks()
  })

  it('PERS-12: exactly DESKTOP_BP viewport + in-range value → returned', () => {
    // vw=768: maxAvail = min(1200, 736) = 736; 700 is in [620,736] → 700
    localStorage.setItem(STORAGE_KEY, '700')
    expect(readPersistedWidth(DESKTOP_BP)).toBe(700)
  })
})

describe('persistWidth — additional edge cases', () => {
  beforeEach(() => { localStorage.clear() })
  afterEach(() => { localStorage.clear() })

  it('SAVE-03: localStorage.setItem throws → no rethrow (quota guard)', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota') })
    expect(() => persistWidth(800)).not.toThrow()
    vi.restoreAllMocks()
  })
})

describe('clampDrawerWidth — additional edge cases', () => {
  it('CLAMP-08: result never exceeds viewportW-32 regardless of MIN_W', () => {
    // All test cases must satisfy this invariant
    ;[400, 500, 600, 700, 1024, 1440, 1920].forEach(vw => {
      const result = clampDrawerWidth(1500, vw)
      expect(result).toBeLessThanOrEqual(vw - 32)
    })
  })
})
