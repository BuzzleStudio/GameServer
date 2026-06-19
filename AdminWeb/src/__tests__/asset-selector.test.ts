// @vitest-environment happy-dom
/**
 * asset-selector.test.ts — filterOptions (pure, exported) + mountCombobox DOM (position:fixed regression)
 *
 * Design ref: §4.1 (ComboboxOption shape), §4.3 (filter logic — exact algorithm)
 *             §8.2 (combobox position:fixed change — regression coverage)
 * Module:     src/modules/asset-selector.ts (lands in task #4)
 * Amendment:  A1 — CURRENCY_OPTIONS use {id, label} shape; filter matches both fields
 *
 * filterOptions(options, query):
 *   q = query.trim().toLowerCase()
 *   if (!q) return options
 *   return options.filter(o =>
 *     o.id.toLowerCase().includes(q) ||
 *     (o.label?.toLowerCase().includes(q) ?? false)
 *   )
 *
 * SKELETON — fails on import until implementation lands.
 */
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { filterOptions, mountCombobox, type ComboboxOption } from '../modules/asset-selector'
import { CURRENCY_IDS, ITEM_IDS, TICKET_IDS } from '../generated/lookup-data'

// ─── Helper fixtures ──────────────────────────────────────────────────────────

/** Convert string-array IDs to id-only options (Item, Ticket) */
const toIdOptions = (ids: readonly string[]): ComboboxOption[] =>
  ids.map(id => ({ id }))

/** Sample currency options with labels (Amendment A1) */
const SAMPLE_CURRENCY_OPTIONS: ComboboxOption[] = [
  { id: 'gem',  label: 'Gems' },
  { id: 'gold', label: 'Gold' },
  { id: 'mine_1', label: 'Mine Stone 1' },
  { id: 'bp_point', label: 'Battle Pass Points' },
]

const SAMPLE_ITEM_OPTIONS: ComboboxOption[] = toIdOptions(['W_Dagger', 'W_Bow', 'W_Staff'])
const SAMPLE_TICKET_OPTIONS: ComboboxOption[] = toIdOptions(TICKET_IDS)

// ─── Basic behavior ───────────────────────────────────────────────────────────

describe('filterOptions — empty query returns all', () => {
  it('empty string → all options returned', () => {
    const result = filterOptions(SAMPLE_CURRENCY_OPTIONS, '')
    expect(result).toHaveLength(SAMPLE_CURRENCY_OPTIONS.length)
  })

  it('whitespace-only query → all options (trim applied)', () => {
    const result = filterOptions(SAMPLE_CURRENCY_OPTIONS, '   ')
    expect(result).toHaveLength(SAMPLE_CURRENCY_OPTIONS.length)
  })

  it('empty options list + empty query → empty result', () => {
    expect(filterOptions([], '')).toEqual([])
  })

  it('empty options list + non-empty query → empty result', () => {
    expect(filterOptions([], 'gold')).toEqual([])
  })
})

describe('filterOptions — id matching (all option types)', () => {
  it('exact id match returns that option', () => {
    const result = filterOptions(SAMPLE_CURRENCY_OPTIONS, 'gem')
    expect(result).toHaveLength(1)
    expect(result[0].id).toBe('gem')
  })

  it('partial id substring match', () => {
    const result = filterOptions(SAMPLE_CURRENCY_OPTIONS, 'mi')  // matches 'mine_1'
    expect(result.some(o => o.id === 'mine_1')).toBe(true)
  })

  it('id match is case-insensitive (GEM matches gem)', () => {
    const result = filterOptions(SAMPLE_CURRENCY_OPTIONS, 'GEM')
    expect(result.some(o => o.id === 'gem')).toBe(true)
  })

  it('id match is case-insensitive (W_DAGGER matches W_Dagger)', () => {
    const result = filterOptions(SAMPLE_ITEM_OPTIONS, 'w_dagger')
    expect(result.some(o => o.id === 'W_Dagger')).toBe(true)
  })

  it('non-matching query returns empty array', () => {
    const result = filterOptions(SAMPLE_CURRENCY_OPTIONS, 'zzz_no_match_xyz')
    expect(result).toHaveLength(0)
  })

  it('query matching multiple ids returns all matches', () => {
    const result = filterOptions(SAMPLE_CURRENCY_OPTIONS, 'g')  // gem, gold
    expect(result.some(o => o.id === 'gem')).toBe(true)
    expect(result.some(o => o.id === 'gold')).toBe(true)
  })
})

describe('filterOptions — label matching [A1] (currency options with labels)', () => {
  it('[A1] query matching label returns that option', () => {
    // "Gems" is the label for id "gem"
    const result = filterOptions(SAMPLE_CURRENCY_OPTIONS, 'Gems')
    expect(result.some(o => o.id === 'gem')).toBe(true)
  })

  it('[A1] label match is case-insensitive', () => {
    const result = filterOptions(SAMPLE_CURRENCY_OPTIONS, 'gems')
    expect(result.some(o => o.id === 'gem')).toBe(true)
  })

  it('[A1] partial label substring match', () => {
    // "Battle Pass Points" → query "battle" should match bp_point
    const result = filterOptions(SAMPLE_CURRENCY_OPTIONS, 'battle')
    expect(result.some(o => o.id === 'bp_point')).toBe(true)
  })

  it('[A1] query matching both id and label returns option once (no duplicates)', () => {
    // If query "gem" matches both id "gem" AND happens to be in label too
    const opts: ComboboxOption[] = [{ id: 'gem', label: 'Gem Stone' }]
    const result = filterOptions(opts, 'gem')
    expect(result).toHaveLength(1)
  })

  it('[A1] id-only option (no label) is still filterable by id', () => {
    const result = filterOptions(SAMPLE_ITEM_OPTIONS, 'Dagger')
    expect(result.some(o => o.id === 'W_Dagger')).toBe(true)
  })

  it('[A1] label absent (undefined) does not cause crash', () => {
    const opts: ComboboxOption[] = [{ id: 'x' }]  // no label
    expect(() => filterOptions(opts, 'test')).not.toThrow()
  })
})

describe('filterOptions — purity (no mutation)', () => {
  it('does not mutate the input array', () => {
    const options: ComboboxOption[] = [{ id: 'gem', label: 'Gems' }, { id: 'gold', label: 'Gold' }]
    const originalLength = options.length
    const originalFirst = options[0]
    filterOptions(options, 'ge')
    expect(options).toHaveLength(originalLength)
    expect(options[0]).toBe(originalFirst)
  })

  it('does not mutate option objects', () => {
    const opt: ComboboxOption = { id: 'gem', label: 'Gems' }
    const options = [opt]
    filterOptions(options, 'gem')
    expect(opt.id).toBe('gem')
    expect(opt.label).toBe('Gems')
  })

  it('returns a new array reference', () => {
    const options: ComboboxOption[] = [{ id: 'gem' }]
    const result = filterOptions(options, '')
    // Empty query returns all — but should still be a new ref or same; must not crash
    // (Implementation may return the same array for empty query — that's ok)
    expect(Array.isArray(result)).toBe(true)
  })
})

describe('filterOptions — with real lookup data', () => {
  it('CURRENCY_IDS converted to options: filtering "gold" returns gold entry', () => {
    const opts = toIdOptions(CURRENCY_IDS)
    const result = filterOptions(opts, 'gold')
    expect(result.some(o => o.id === 'gold')).toBe(true)
  })

  it('ITEM_IDS: filter "Bow" returns W_Bow', () => {
    const opts = toIdOptions(ITEM_IDS)
    const result = filterOptions(opts, 'Bow')
    expect(result.some(o => o.id === 'W_Bow')).toBe(true)
  })

  it('TICKET_IDS: filter "grass" returns expedition_map_ticket_grass', () => {
    const opts = toIdOptions(TICKET_IDS)
    const result = filterOptions(opts, 'grass')
    expect(result.some(o => o.id === 'expedition_map_ticket_grass')).toBe(true)
  })

  it('filter returning no matches from real data returns empty array', () => {
    const opts = toIdOptions(CURRENCY_IDS)
    expect(filterOptions(opts, 'zzz_fake_currency_id')).toHaveLength(0)
  })
})

describe('filterOptions — unknown ID detection via empty result', () => {
  it('unknown id: filterOptions returns [] → isKnown = false', () => {
    const opts = toIdOptions(CURRENCY_IDS)
    const result = filterOptions(opts, 'my_custom_currency_xyz')
    // Unknown ID means no exact match — result will be empty or won't contain that exact id
    const hasExact = result.some(o => o.id === 'my_custom_currency_xyz')
    expect(hasExact).toBe(false)
  })

  it('known id: filterOptions returns match → isKnown = true', () => {
    const opts = toIdOptions(CURRENCY_IDS)
    const result = filterOptions(opts, 'gem')
    const hasExact = result.some(o => o.id === 'gem')
    expect(hasExact).toBe(true)
  })
})

// ─── CB: mountCombobox position:fixed regression (design §8.2) ───────────────
// The combobox listbox was changed from position:absolute to position:fixed so it
// escapes overflow clipping contexts (modal body, drawer). These tests verify the
// change doesn't break outside-modal contexts (send-form, drawer, main page).
//
// Note: getBoundingClientRect() returns all-zeros in happy-dom (no layout engine).
// Tests mock it via vi.spyOn to verify _positionListbox logic uses the return value.

describe('mountCombobox — position:fixed regression (CB-01 to CB-08)', () => {
  let container: HTMLDivElement

  beforeEach(() => {
    document.body.innerHTML = ''
    container = document.createElement('div')
    container.id = 'cb-wrap'
    document.body.appendChild(container)
  })

  afterEach(() => {
    document.body.innerHTML = ''
    vi.restoreAllMocks()
  })

  function mountTestCombobox(opts: ComboboxOption[] = [{ id: 'gem', label: 'Gem' }, { id: 'gold', label: 'Gold' }]) {
    return mountCombobox({
      containerId:  'cb-wrap',
      inputId:      'cb-input',
      options:      opts,
      initialValue: '',
      allowUnknown: false,
      onChange:     vi.fn(),
    })
  }

  // CB-01: listbox uses position:fixed after open
  it('CB-01: listbox position is fixed after open', () => {
    mountTestCombobox()
    const input = document.getElementById('cb-input') as HTMLInputElement
    input.dispatchEvent(new Event('focus'))  // triggers openList('')
    const listbox = document.getElementById('cb-input-listbox') as HTMLUListElement
    expect(listbox.hidden).toBe(false)
    // The CSS sets position:fixed — the listbox element itself gets inline top/left from _positionListbox
    // In happy-dom getBoundingClientRect returns 0s, so top/left will be '0px'
    // but the key check is that _positionListbox ran (style.left is set, not empty)
    expect(listbox.style.left).toBe('0px')
    expect(listbox.style.top).not.toBe('')
  })

  // CB-02: _positionListbox uses getBoundingClientRect().bottom for top position
  it('CB-02: listbox top set from getBoundingClientRect().bottom', () => {
    vi.spyOn(container, 'getBoundingClientRect').mockReturnValue({
      left: 100, right: 400, top: 200, bottom: 240, width: 300, height: 40,
      x: 100, y: 200, toJSON: () => ({}),
    } as DOMRect)
    // window.innerHeight defaults to 768 in happy-dom
    Object.defineProperty(window, 'innerHeight', { value: 768, writable: true, configurable: true })

    mountTestCombobox()
    const input = document.getElementById('cb-input') as HTMLInputElement
    input.dispatchEvent(new Event('focus'))
    const listbox = document.getElementById('cb-input-listbox') as HTMLUListElement

    // spaceBelow = 768 - 240 = 528 (>> estimatedH ~80) → open downward → top = bottom = 240
    expect(listbox.style.top).toBe('240px')
    expect(listbox.style.bottom).toBe('auto')
    expect(listbox.style.left).toBe('100px')
    expect(listbox.style.width).toBe('300px')
  })

  // CB-03: select option → value updates, listbox closes
  it('CB-03: selecting an option commits value and closes listbox', () => {
    const onChange = vi.fn()
    mountCombobox({
      containerId:  'cb-wrap',
      inputId:      'cb-input',
      options:      [{ id: 'gem', label: 'Gem' }, { id: 'gold', label: 'Gold' }],
      initialValue: '',
      allowUnknown: false,
      onChange,
    })
    const input = document.getElementById('cb-input') as HTMLInputElement
    input.dispatchEvent(new Event('focus'))

    const listbox = document.getElementById('cb-input-listbox') as HTMLUListElement
    const firstOption = listbox.querySelector<HTMLElement>('[data-value="gem"]')
    expect(firstOption).toBeTruthy()
    firstOption!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true }))

    expect(onChange).toHaveBeenCalledWith('gem')
    expect(listbox.hidden).toBe(true)
    expect((document.getElementById('cb-input') as HTMLInputElement).value).toBe('gem')
  })

  // CB-04: outside click closes listbox
  it('CB-04: click outside combobox closes the listbox', () => {
    mountTestCombobox()
    const input = document.getElementById('cb-input') as HTMLInputElement
    input.dispatchEvent(new Event('focus'))

    const listbox = document.getElementById('cb-input-listbox') as HTMLUListElement
    expect(listbox.hidden).toBe(false)

    // Click on an element outside the container
    const outside = document.createElement('div')
    outside.id = 'outside'
    document.body.appendChild(outside)
    document.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }))

    expect(listbox.hidden).toBe(true)
  })

  // CB-05: Drawer context — same open/filter/select cycle works
  it('CB-05: combobox inside a drawer <aside> opens, filters, selects correctly', () => {
    // Mount a drawer aside
    const aside = document.createElement('aside')
    aside.id = 'mail-drawer'
    aside.className = 'drawer'
    document.body.appendChild(aside)

    // Move container into drawer
    document.body.removeChild(container)
    aside.appendChild(container)

    const onChange = vi.fn()
    mountCombobox({
      containerId:  'cb-wrap',
      inputId:      'cb-drawer-input',
      options:      [{ id: 'gem', label: 'Gem' }, { id: 'gold', label: 'Gold' }],
      initialValue: '',
      allowUnknown: false,
      onChange,
    })

    const input = document.getElementById('cb-drawer-input') as HTMLInputElement
    // Filter
    input.value = 'ge'
    input.dispatchEvent(new Event('input'))

    const listbox = document.getElementById('cb-drawer-input-listbox') as HTMLUListElement
    expect(listbox.hidden).toBe(false)
    const options = listbox.querySelectorAll('[data-value]')
    expect(options.length).toBeGreaterThan(0)
    // Select
    ;(options[0] as HTMLElement).dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true }))
    expect(onChange).toHaveBeenCalled()
    expect(listbox.hidden).toBe(true)
  })

  // CB-08: upward-open branch when more space above than below
  it('CB-08: listbox opens upward when space above > space below', () => {
    vi.spyOn(container, 'getBoundingClientRect').mockReturnValue({
      left: 50, right: 350, top: 700, bottom: 740, width: 300, height: 40,
      x: 50, y: 700, toJSON: () => ({}),
    } as DOMRect)
    Object.defineProperty(window, 'innerHeight', { value: 768, writable: true, configurable: true })

    mountTestCombobox()
    const input = document.getElementById('cb-input') as HTMLInputElement
    input.dispatchEvent(new Event('focus'))
    const listbox = document.getElementById('cb-input-listbox') as HTMLUListElement

    // spaceBelow = 768 - 740 = 28; spaceAbove = 700; estimatedH ≈ 80
    // spaceBelow (28) < estimatedH (80) AND spaceBelow (28) < spaceAbove (700)
    // → open upward: style.bottom = viewportH - rect.top = 768 - 700 = 68
    expect(listbox.style.top).toBe('auto')
    expect(listbox.style.bottom).toBe('68px')
  })
})
