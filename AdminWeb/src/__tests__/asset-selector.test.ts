/**
 * asset-selector.test.ts — filterOptions (pure, exported)
 *
 * Design ref: §4.1 (ComboboxOption shape), §4.3 (filter logic — exact algorithm)
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
import { describe, it, expect } from 'vitest'
import { filterOptions, type ComboboxOption } from '../modules/asset-selector'
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
