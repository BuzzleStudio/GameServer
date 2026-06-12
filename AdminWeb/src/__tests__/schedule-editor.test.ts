// @vitest-environment happy-dom
/**
 * schedule-editor.test.ts — renderScheduleEditor / readScheduleEditor / attachScheduleListeners
 *
 * Design ref: §7 (schedule/expiry), §7.2 (expiry mode: none|set), §7.3 (quick presets)
 * Module:     src/modules/schedule-editor.ts
 *
 * Tests: render output, read round-trip, listener wiring (radio toggle, preset buttons)
 */
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import {
  renderScheduleEditor,
  readScheduleEditor,
  attachScheduleListeners,
  type ScheduleEditorState,
} from '../modules/schedule-editor'

// ─── Setup / teardown ─────────────────────────────────────────────────────────

let container: HTMLDivElement

beforeEach(() => {
  container = document.createElement('div')
  document.body.appendChild(container)
})

afterEach(() => {
  document.body.removeChild(container)
})

function renderInto(state: ScheduleEditorState, prefix = 'test', disabled = false): void {
  container.innerHTML = renderScheduleEditor(prefix, state, disabled)
}

// ─── renderScheduleEditor — HTML output ───────────────────────────────────────

describe('renderScheduleEditor — output structure', () => {
  it('renders a schedule-editor container with correct id', () => {
    renderInto({ expiryMode: 'none', expiryDate: '', expiryTime: '' })
    const el = document.getElementById('test-schedule')
    expect(el).not.toBeNull()
  })

  it('renders two radio buttons: none and set', () => {
    renderInto({ expiryMode: 'none', expiryDate: '', expiryTime: '' })
    const radios = document.querySelectorAll<HTMLInputElement>('input[name="test-expiry-mode"]')
    expect(radios).toHaveLength(2)
    const values = Array.from(radios).map(r => r.value)
    expect(values).toContain('none')
    expect(values).toContain('set')
  })

  it('mode=none → "none" radio is checked, inputs hidden', () => {
    renderInto({ expiryMode: 'none', expiryDate: '', expiryTime: '' })
    const noneR = document.querySelector<HTMLInputElement>('input[name="test-expiry-mode"][value="none"]')!
    expect(noneR.checked).toBe(true)
    const inputsDiv = document.getElementById('test-schedule-inputs')!
    expect(inputsDiv.hidden).toBe(true)
  })

  it('mode=set → "set" radio is checked, inputs visible', () => {
    renderInto({ expiryMode: 'set', expiryDate: '2025-12-31', expiryTime: '23:59' })
    const setR = document.querySelector<HTMLInputElement>('input[name="test-expiry-mode"][value="set"]')!
    expect(setR.checked).toBe(true)
    const inputsDiv = document.getElementById('test-schedule-inputs')!
    expect(inputsDiv.hidden).toBe(false)
  })

  it('mode=set → date input has correct value', () => {
    renderInto({ expiryMode: 'set', expiryDate: '2025-12-31', expiryTime: '23:59' })
    const dateEl = document.getElementById('test-exp-date') as HTMLInputElement
    expect(dateEl.value).toBe('2025-12-31')
  })

  it('mode=set → time input has correct value', () => {
    renderInto({ expiryMode: 'set', expiryDate: '2025-12-31', expiryTime: '23:59' })
    const timeEl = document.getElementById('test-exp-time') as HTMLInputElement
    expect(timeEl.value).toBe('23:59')
  })

  it('disabled=true → radios have disabled attribute', () => {
    renderInto({ expiryMode: 'none', expiryDate: '', expiryTime: '' }, 'test', true)
    const radios = document.querySelectorAll<HTMLInputElement>('input[name="test-expiry-mode"]')
    radios.forEach(r => expect(r.disabled).toBe(true))
  })

  it('renders +1d, +7d, +30d preset buttons', () => {
    renderInto({ expiryMode: 'set', expiryDate: '2025-12-31', expiryTime: '23:59' })
    const presets = document.querySelectorAll('[data-action="preset-days"]')
    const days = Array.from(presets).map(b => (b as HTMLElement).dataset['days'])
    expect(days).toContain('1')
    expect(days).toContain('7')
    expect(days).toContain('30')
  })

  it('renders "✕ None" preset button', () => {
    renderInto({ expiryMode: 'set', expiryDate: '2025-12-31', expiryTime: '23:59' })
    const none = document.querySelector('[data-action="preset-none"]')
    expect(none).not.toBeNull()
  })
})

// ─── readScheduleEditor — round-trip ─────────────────────────────────────────

describe('readScheduleEditor — round-trip', () => {
  it('reads mode=none correctly', () => {
    renderInto({ expiryMode: 'none', expiryDate: '', expiryTime: '' })
    const s = readScheduleEditor('test')
    expect(s.expiryMode).toBe('none')
  })

  it('reads mode=set correctly', () => {
    renderInto({ expiryMode: 'set', expiryDate: '2025-06-15', expiryTime: '12:00' })
    const s = readScheduleEditor('test')
    expect(s.expiryMode).toBe('set')
  })

  it('reads date correctly', () => {
    renderInto({ expiryMode: 'set', expiryDate: '2025-06-15', expiryTime: '12:00' })
    const s = readScheduleEditor('test')
    expect(s.expiryDate).toBe('2025-06-15')
  })

  it('reads time correctly', () => {
    renderInto({ expiryMode: 'set', expiryDate: '2025-06-15', expiryTime: '12:00' })
    const s = readScheduleEditor('test')
    expect(s.expiryTime).toBe('12:00')
  })

  it('returns empty strings when mode=none', () => {
    renderInto({ expiryMode: 'none', expiryDate: '', expiryTime: '' })
    const s = readScheduleEditor('test')
    expect(s.expiryDate).toBe('')
    expect(s.expiryTime).toBe('')
  })
})

// ─── attachScheduleListeners — radio toggle ───────────────────────────────────

describe('attachScheduleListeners — radio toggle', () => {
  it('toggling from none → set fires onChange', () => {
    renderInto({ expiryMode: 'none', expiryDate: '', expiryTime: '' })
    const onChange = vi.fn()
    attachScheduleListeners('test', onChange)

    const setR = document.querySelector<HTMLInputElement>('input[name="test-expiry-mode"][value="set"]')!
    setR.checked = true
    setR.dispatchEvent(new Event('change', { bubbles: true }))

    expect(onChange).toHaveBeenCalled()
  })

  it('toggling from none → set shows inputs div', () => {
    renderInto({ expiryMode: 'none', expiryDate: '', expiryTime: '' })
    attachScheduleListeners('test', vi.fn())

    const setR = document.querySelector<HTMLInputElement>('input[name="test-expiry-mode"][value="set"]')!
    setR.checked = true
    setR.dispatchEvent(new Event('change', { bubbles: true }))

    const inputsDiv = document.getElementById('test-schedule-inputs')!
    expect(inputsDiv.hidden).toBe(false)
  })

  it('toggling from set → none hides inputs div', () => {
    renderInto({ expiryMode: 'set', expiryDate: '2025-12-31', expiryTime: '23:59' })
    attachScheduleListeners('test', vi.fn())

    const noneR = document.querySelector<HTMLInputElement>('input[name="test-expiry-mode"][value="none"]')!
    noneR.checked = true
    noneR.dispatchEvent(new Event('change', { bubbles: true }))

    const inputsDiv = document.getElementById('test-schedule-inputs')!
    expect(inputsDiv.hidden).toBe(true)
  })

  it('onChange receives state with expiryMode after toggle', () => {
    renderInto({ expiryMode: 'none', expiryDate: '', expiryTime: '' })
    const onChange = vi.fn()
    attachScheduleListeners('test', onChange)

    const setR = document.querySelector<HTMLInputElement>('input[name="test-expiry-mode"][value="set"]')!
    setR.checked = true
    setR.dispatchEvent(new Event('change', { bubbles: true }))

    const state: ScheduleEditorState = onChange.mock.calls[0][0]
    expect(state.expiryMode).toBe('set')
  })
})

// ─── Preset buttons ───────────────────────────────────────────────────────────

describe('attachScheduleListeners — preset buttons', () => {
  it('clicking +7d sets mode to "set" and fills date+time', () => {
    renderInto({ expiryMode: 'none', expiryDate: '', expiryTime: '' })
    attachScheduleListeners('test', vi.fn())

    const btn7 = document.querySelector<HTMLElement>('[data-action="preset-days"][data-days="7"]')!
    btn7.click()

    const dateEl = document.getElementById('test-exp-date') as HTMLInputElement
    const timeEl = document.getElementById('test-exp-time') as HTMLInputElement
    expect(dateEl.value).toBeTruthy()
    expect(timeEl.value).toBeTruthy()
    // Mode should switch to set
    const setR = document.querySelector<HTMLInputElement>('input[name="test-expiry-mode"][value="set"]')!
    expect(setR.checked).toBe(true)
  })

  it('clicking ✕ None clears date and time', () => {
    renderInto({ expiryMode: 'set', expiryDate: '2025-12-31', expiryTime: '23:59' })
    attachScheduleListeners('test', vi.fn())

    const noneBtn = document.querySelector<HTMLElement>('[data-action="preset-none"]')!
    noneBtn.click()

    const dateEl = document.getElementById('test-exp-date') as HTMLInputElement
    const timeEl = document.getElementById('test-exp-time') as HTMLInputElement
    expect(dateEl.value).toBe('')
    expect(timeEl.value).toBe('')
  })

  it('clicking ✕ None checks "none" radio', () => {
    renderInto({ expiryMode: 'set', expiryDate: '2025-12-31', expiryTime: '23:59' })
    attachScheduleListeners('test', vi.fn())

    document.querySelector<HTMLElement>('[data-action="preset-none"]')!.click()

    const noneR = document.querySelector<HTMLInputElement>('input[name="test-expiry-mode"][value="none"]')!
    expect(noneR.checked).toBe(true)
  })

  it('clicking +7d fires onChange', () => {
    renderInto({ expiryMode: 'none', expiryDate: '', expiryTime: '' })
    const onChange = vi.fn()
    attachScheduleListeners('test', onChange)

    document.querySelector<HTMLElement>('[data-action="preset-days"][data-days="7"]')!.click()
    expect(onChange).toHaveBeenCalled()
  })
})

// ─── Multi-prefix isolation ───────────────────────────────────────────────────

describe('renderScheduleEditor — prefix isolation', () => {
  it('two editors with different prefixes do not share element IDs', () => {
    const div1 = document.createElement('div')
    const div2 = document.createElement('div')
    document.body.appendChild(div1)
    document.body.appendChild(div2)

    div1.innerHTML = renderScheduleEditor('p1', { expiryMode: 'none', expiryDate: '', expiryTime: '' })
    div2.innerHTML = renderScheduleEditor('p2', { expiryMode: 'set',  expiryDate: '2025-12-31', expiryTime: '23:59' })

    const p1 = document.getElementById('p1-schedule')
    const p2 = document.getElementById('p2-schedule')
    expect(p1).not.toBeNull()
    expect(p2).not.toBeNull()
    expect(p1).not.toBe(p2)

    document.body.removeChild(div1)
    document.body.removeChild(div2)
  })
})
