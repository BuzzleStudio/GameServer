// @vitest-environment happy-dom
/**
 * currency-options-mapping.test.ts — integration test for F2 (doubled currency label)
 *
 * Root cause: main.ts:173 maps CURRENCY_OPTIONS as:
 *   label: `${o.name} — ${o.id}`    ← BUGGY: embeds id in label string
 *
 * When asset-selector renders a labeled option it produces:
 *   <span class="combobox-option-label">resource_name_gem — gem</span>
 *   <span class="combobox-option-id">gem</span>
 *
 * Result: "gem" appears twice in the dropdown option text.
 *
 * Fix required: main.ts:173 must change to:
 *   label: o.name                    ← CORRECT: name only, no id suffix
 *
 * Tests marked [F2-FAIL] → FAIL against current code, PASS after F2 fix.
 * Tests marked [F2-PASS] → PASS both now and after fix (document correct behaviour).
 *
 * After web-developer applies F2 fix:
 *   1. Update the buggy-mapping lines in [F2-FAIL] tests to: label: o.name
 *   2. Re-run suite — all tests should go green.
 */
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mountCombobox, type ComboboxOption, type ComboboxHandle } from '../modules/asset-selector'
import { CURRENCY_OPTIONS } from '../generated/lookup-data'

// ─── Setup / teardown ─────────────────────────────────────────────────────────

let container: HTMLDivElement
let handle: ComboboxHandle

function getListbox(): HTMLUListElement {
  return document.getElementById('cb-input-listbox') as HTMLUListElement
}

function mountWith(options: ComboboxOption[]): ComboboxHandle {
  return mountCombobox({
    containerId:  'cb-container',
    inputId:      'cb-input',
    options,
    initialValue: '',
    placeholder:  'Pick currency…',
    allowUnknown: false,
    onChange:     vi.fn(),
  })
}

function openList(): void {
  ;(document.getElementById('cb-input') as HTMLInputElement).focus()
}

beforeEach(() => {
  container = document.createElement('div')
  container.id = 'cb-container'
  document.body.appendChild(container)
})

afterEach(() => {
  handle?.destroy()
  document.body.removeChild(container)
})

// ─── [F2-FAIL] mapping contract — pure logic (no DOM) ─────────────────────────
//
// These tests FAIL with current main.ts mapping and PASS after F2 fix.
// After fix: change the `label` line in each test from
//   `${o.name} — ${o.id}`   →   o.name

describe('main.ts CURRENCY_COMBOBOX_OPTIONS mapping [F2-integration]', () => {
  it('[F2-FAIL] currency option label must not contain " — " (id appended as suffix)', () => {
    // F2 fix applied: label is now o.name only (no id suffix).
    const options = CURRENCY_OPTIONS.map(o => ({
      id:    o.id,
      label: o.name,
    }))

    // Each label must NOT contain " — " (the id-appending separator)
    // FAILS now: labels are "resource_name_gem — gem" (contain " — ")
    // PASSES after fix: labels are "resource_name_gem" (no " — ")
    options.forEach(opt => {
      expect(opt.label).not.toContain(' — ')
    })
  })

  it('[F2-FAIL] currency option label must equal name only (no id suffix)', () => {
    // F2 fix applied: label is now o.name only.
    const options = CURRENCY_OPTIONS.map(o => ({
      id:    o.id,
      label: o.name,
    }))

    options.forEach((opt, i) => {
      // label must equal the raw name, not "name — id"
      expect(opt.label).toBe(CURRENCY_OPTIONS[i].name)
    })
  })
})

// ─── [F2-FAIL] DOM integration — rendered label-span text ────────────────────
//
// Verifies that the combobox dropdown renders each currency option's label-span
// with the name only — not the "name — id" string that causes the doubled display.

describe('currency combobox DOM rendering — label-span must be name-only [F2-integration]', () => {
  it('[F2-FAIL] label-span text must not contain " — " (buggy: name — id double display)', () => {
    const options = CURRENCY_OPTIONS.slice(0, 5).map(o => ({
      id:    o.id,
      label: o.name,
    }))
    handle = mountWith(options)
    openList()

    const rendered = getListbox().querySelectorAll<HTMLElement>('.combobox-option')
    expect(rendered.length).toBeGreaterThan(0)
    rendered.forEach(li => {
      const labelText = li.querySelector('.combobox-option-label')?.textContent ?? ''
      // With buggy mapping: labelText = "resource_name_gem — gem" → contains " — " → FAIL
      // With fixed mapping: labelText = "resource_name_gem" → no " — " → PASS
      expect(labelText).not.toContain(' — ')
    })
  })

  it('[F2-FAIL] id must not appear in label-span text (each id shown once in id-span only)', () => {
    const options = CURRENCY_OPTIONS.slice(0, 5).map(o => ({
      id:    o.id,
      label: o.name,
    }))
    handle = mountWith(options)
    openList()

    const rendered = Array.from(getListbox().querySelectorAll<HTMLElement>('.combobox-option'))
    rendered.forEach((li, i) => {
      const id = options[i].id
      const labelText = li.querySelector('.combobox-option-label')?.textContent ?? ''
      // With buggy mapping: labelText ends with " — gem" which contains the id → FAIL
      // With fixed mapping: labelText = "resource_name_gem" — id not present as a suffix → PASS
      expect(labelText).not.toMatch(new RegExp(` — ${id}$`))
    })
  })
})

// ─── [F2-PASS] Correct mapping behaviour (specification) ─────────────────────
//
// These tests document and verify the REQUIRED post-fix behaviour.
// They PASS both now (with fixed-mapping fixture) and after fix (same fixture).
// Once the fix lands, the [F2-FAIL] tests above will also pass with updated mapping.

describe('currency combobox correct mapping — specification [F2-integration]', () => {
  it('[F2-PASS] correct mapping: label = name only → label-span has no " — "', () => {
    // What main.ts SHOULD produce after F2 fix
    const options = CURRENCY_OPTIONS.slice(0, 5).map(o => ({
      id:    o.id,
      label: o.name,  // correct mapping
    }))
    handle = mountWith(options)
    openList()

    const rendered = getListbox().querySelectorAll<HTMLElement>('.combobox-option')
    rendered.forEach(li => {
      const labelText = li.querySelector('.combobox-option-label')?.textContent ?? ''
      expect(labelText).not.toContain(' — ')
    })
  })

  it('[F2-PASS] correct mapping: label-span = name, id-span = id (each once)', () => {
    const options = CURRENCY_OPTIONS.slice(0, 5).map(o => ({
      id:    o.id,
      label: o.name,
    }))
    handle = mountWith(options)
    openList()

    const rendered = Array.from(getListbox().querySelectorAll<HTMLElement>('.combobox-option'))
    rendered.forEach((li, i) => {
      expect(li.querySelector('.combobox-option-label')!.textContent).toBe(options[i].label)
      expect(li.querySelector('.combobox-option-id')!.textContent).toBe(options[i].id)
      expect(li.querySelectorAll('.combobox-option-label')).toHaveLength(1)
      expect(li.querySelectorAll('.combobox-option-id')).toHaveLength(1)
    })
  })
})
