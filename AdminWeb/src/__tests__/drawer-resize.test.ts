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
