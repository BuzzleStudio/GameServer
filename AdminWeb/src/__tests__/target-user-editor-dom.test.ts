// @vitest-environment happy-dom
/**
 * target-user-editor-dom.test.ts — DOM functions in src/modules/target-user-editor.ts
 *
 * Design ref: §8 (target user textarea), §5.1 (canonical module locations)
 * Module:     src/modules/target-user-editor.ts
 *   exports:  renderTargetUserEditor, readTargetUserEditor, attachTargetUserListeners
 *
 * Note: validateTargetUserIds (pure logic) is tested in target-user-editor.test.ts.
 * This file tests only the DOM rendering and read-back behavior.
 */
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import {
  renderTargetUserEditor,
  readTargetUserEditor,
  attachTargetUserListeners,
  type TargetUserEditorState,
} from '../modules/target-user-editor'

// ─── Setup / teardown ─────────────────────────────────────────────────────────

let container: HTMLDivElement

beforeEach(() => {
  container = document.createElement('div')
  document.body.appendChild(container)
})

afterEach(() => {
  document.body.removeChild(container)
})

function renderInto(state: TargetUserEditorState, prefix = 'test', disabled = false): void {
  container.innerHTML = renderTargetUserEditor(prefix, state, disabled)
}

// ─── renderTargetUserEditor — HTML output ─────────────────────────────────────

describe('renderTargetUserEditor — output structure', () => {
  it('renders container with correct id', () => {
    renderInto({ targetMode: 'all', targetText: '' })
    expect(document.getElementById('test-target')).not.toBeNull()
  })

  it('renders two radios: "all" and "specific"', () => {
    renderInto({ targetMode: 'all', targetText: '' })
    const radios = document.querySelectorAll<HTMLInputElement>('input[name="test-target-mode"]')
    expect(radios).toHaveLength(2)
    const values = Array.from(radios).map(r => r.value)
    expect(values).toContain('all')
    expect(values).toContain('specific')
  })

  it('mode=all → "all" radio checked, specific panel hidden', () => {
    renderInto({ targetMode: 'all', targetText: '' })
    const allR = document.querySelector<HTMLInputElement>('input[name="test-target-mode"][value="all"]')!
    expect(allR.checked).toBe(true)
    const specificDiv = document.getElementById('test-target-specific')!
    expect(specificDiv.hidden).toBe(true)
  })

  it('mode=specific → "specific" radio checked, specific panel visible', () => {
    renderInto({ targetMode: 'specific', targetText: 'uuid-a\nuuid-b' })
    const specR = document.querySelector<HTMLInputElement>('input[name="test-target-mode"][value="specific"]')!
    expect(specR.checked).toBe(true)
    const specificDiv = document.getElementById('test-target-specific')!
    expect(specificDiv.hidden).toBe(false)
  })

  it('textarea id is correct', () => {
    renderInto({ targetMode: 'specific', targetText: 'uuid-a' })
    const ta = document.getElementById('test-target-text') as HTMLTextAreaElement
    expect(ta).not.toBeNull()
  })

  it('textarea pre-populated with targetText', () => {
    renderInto({ targetMode: 'specific', targetText: 'uuid-a\nuuid-b' })
    const ta = document.getElementById('test-target-text') as HTMLTextAreaElement
    expect(ta.value).toBe('uuid-a\nuuid-b')
  })

  it('disabled=true → radios disabled', () => {
    renderInto({ targetMode: 'all', targetText: '' }, 'test', true)
    const radios = document.querySelectorAll<HTMLInputElement>('input[name="test-target-mode"]')
    radios.forEach(r => expect(r.disabled).toBe(true))
  })

  it('disabled=true → textarea disabled', () => {
    renderInto({ targetMode: 'specific', targetText: 'x' }, 'test', true)
    const ta = document.getElementById('test-target-text') as HTMLTextAreaElement
    expect(ta.disabled).toBe(true)
  })

  it('renders Copy and Clear buttons', () => {
    renderInto({ targetMode: 'specific', targetText: '' })
    const copy  = document.querySelector('[data-action="copy-uids"]')
    const clear = document.querySelector('[data-action="clear-uids"]')
    expect(copy).not.toBeNull()
    expect(clear).not.toBeNull()
  })
})

// ─── readTargetUserEditor — round-trip ────────────────────────────────────────

describe('readTargetUserEditor — round-trip', () => {
  it('reads targetMode=all correctly', () => {
    renderInto({ targetMode: 'all', targetText: '' })
    const s = readTargetUserEditor('test')
    expect(s.targetMode).toBe('all')
  })

  it('reads targetMode=specific correctly', () => {
    renderInto({ targetMode: 'specific', targetText: 'uuid-a' })
    const s = readTargetUserEditor('test')
    expect(s.targetMode).toBe('specific')
  })

  it('reads targetText correctly', () => {
    renderInto({ targetMode: 'specific', targetText: 'uuid-a\nuuid-b' })
    const s = readTargetUserEditor('test')
    expect(s.targetText).toBe('uuid-a\nuuid-b')
  })

  it('reads empty targetText for mode=all', () => {
    renderInto({ targetMode: 'all', targetText: '' })
    const s = readTargetUserEditor('test')
    expect(s.targetText).toBe('')
  })
})

// ─── attachTargetUserListeners — radio toggle ─────────────────────────────────

describe('attachTargetUserListeners — radio toggle', () => {
  it('toggle from all → specific fires onChange', () => {
    renderInto({ targetMode: 'all', targetText: '' })
    const onChange = vi.fn()
    attachTargetUserListeners('test', onChange)

    const specR = document.querySelector<HTMLInputElement>('input[name="test-target-mode"][value="specific"]')!
    specR.checked = true
    specR.dispatchEvent(new Event('change', { bubbles: true }))

    expect(onChange).toHaveBeenCalled()
  })

  it('toggle from all → specific shows specific div', () => {
    renderInto({ targetMode: 'all', targetText: '' })
    attachTargetUserListeners('test', vi.fn())

    const specR = document.querySelector<HTMLInputElement>('input[name="test-target-mode"][value="specific"]')!
    specR.checked = true
    specR.dispatchEvent(new Event('change', { bubbles: true }))

    const specificDiv = document.getElementById('test-target-specific')!
    expect(specificDiv.hidden).toBe(false)
  })

  it('toggle from specific → all hides specific div', () => {
    renderInto({ targetMode: 'specific', targetText: 'uuid-a' })
    attachTargetUserListeners('test', vi.fn())

    const allR = document.querySelector<HTMLInputElement>('input[name="test-target-mode"][value="all"]')!
    allR.checked = true
    allR.dispatchEvent(new Event('change', { bubbles: true }))

    const specificDiv = document.getElementById('test-target-specific')!
    expect(specificDiv.hidden).toBe(true)
  })

  it('onChange receives state with correct targetMode', () => {
    renderInto({ targetMode: 'all', targetText: '' })
    const onChange = vi.fn()
    attachTargetUserListeners('test', onChange)

    const specR = document.querySelector<HTMLInputElement>('input[name="test-target-mode"][value="specific"]')!
    specR.checked = true
    specR.dispatchEvent(new Event('change', { bubbles: true }))

    const state: TargetUserEditorState = onChange.mock.calls[0][0]
    expect(state.targetMode).toBe('specific')
  })
})

// ─── attachTargetUserListeners — textarea input ───────────────────────────────

describe('attachTargetUserListeners — textarea input', () => {
  it('typing in textarea fires onChange', () => {
    renderInto({ targetMode: 'specific', targetText: '' })
    const onChange = vi.fn()
    attachTargetUserListeners('test', onChange)

    const ta = document.getElementById('test-target-text') as HTMLTextAreaElement
    ta.value = 'uuid-new'
    ta.dispatchEvent(new Event('input', { bubbles: true }))

    expect(onChange).toHaveBeenCalled()
  })

  it('onChange receives current textarea value after input', () => {
    renderInto({ targetMode: 'specific', targetText: '' })
    const onChange = vi.fn()
    attachTargetUserListeners('test', onChange)

    const ta = document.getElementById('test-target-text') as HTMLTextAreaElement
    ta.value = 'uuid-typed'
    ta.dispatchEvent(new Event('input', { bubbles: true }))

    const state: TargetUserEditorState = onChange.mock.calls[0][0]
    expect(state.targetText).toBe('uuid-typed')
  })

  it('typing shows stats element update (non-empty text)', () => {
    renderInto({ targetMode: 'specific', targetText: '' })
    attachTargetUserListeners('test', vi.fn())

    const ta = document.getElementById('test-target-text') as HTMLTextAreaElement
    ta.value = 'uuid-a\nuuid-b'
    ta.dispatchEvent(new Event('input', { bubbles: true }))

    const stats = document.getElementById('test-target-stats')!
    // Stats should show user count
    expect(stats.textContent).toContain('2')
  })
})

// ─── attachTargetUserListeners — Clear button ─────────────────────────────────

describe('attachTargetUserListeners — Clear button', () => {
  it('clicking Clear empties textarea', () => {
    renderInto({ targetMode: 'specific', targetText: 'uuid-a\nuuid-b' })
    attachTargetUserListeners('test', vi.fn())

    const clearBtn = document.querySelector<HTMLElement>('[data-action="clear-uids"]')!
    clearBtn.click()

    const ta = document.getElementById('test-target-text') as HTMLTextAreaElement
    expect(ta.value).toBe('')
  })

  it('clicking Clear fires onChange with empty text', () => {
    renderInto({ targetMode: 'specific', targetText: 'uuid-a' })
    const onChange = vi.fn()
    attachTargetUserListeners('test', onChange)

    document.querySelector<HTMLElement>('[data-action="clear-uids"]')!.click()

    const state: TargetUserEditorState = onChange.mock.calls[0][0]
    expect(state.targetText).toBe('')
  })

  it('clicking Clear clears stats', () => {
    renderInto({ targetMode: 'specific', targetText: 'uuid-a\nuuid-b' })
    attachTargetUserListeners('test', vi.fn())

    document.querySelector<HTMLElement>('[data-action="clear-uids"]')!.click()

    const stats = document.getElementById('test-target-stats')!
    expect(stats.innerHTML).toBe('')
  })
})

// ─── Prefix isolation ─────────────────────────────────────────────────────────

describe('renderTargetUserEditor — prefix isolation', () => {
  it('two editors with different prefixes have independent radio groups', () => {
    const d1 = document.createElement('div')
    const d2 = document.createElement('div')
    document.body.appendChild(d1)
    document.body.appendChild(d2)

    d1.innerHTML = renderTargetUserEditor('pr1', { targetMode: 'all',      targetText: '' })
    d2.innerHTML = renderTargetUserEditor('pr2', { targetMode: 'specific', targetText: 'uuid-x' })

    const pr1All = document.querySelector<HTMLInputElement>('input[name="pr1-target-mode"][value="all"]')!
    const pr2Sp  = document.querySelector<HTMLInputElement>('input[name="pr2-target-mode"][value="specific"]')!
    expect(pr1All.checked).toBe(true)
    expect(pr2Sp.checked).toBe(true)

    document.body.removeChild(d1)
    document.body.removeChild(d2)
  })
})
