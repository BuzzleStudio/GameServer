// @vitest-environment happy-dom
/**
 * attachment-editor.test.ts — mountAttachmentEditor DOM + byte-compat
 *
 * Design ref: §6 (attachments), §6.3 (Ticket/ISA as JSON object, Currency/Item as plain string)
 * Module:     src/modules/attachment-editor.ts
 *
 * Byte-compat gates (critical):
 *   Currency/Item:      getDrafts() → payoutAssetId = plain string (e.g. "gem")
 *   Ticket/ISA:         getDrafts() → payoutAssetId = "", itemRows[0] has BlueprintId/Rarity/etc.
 *
 * The wire serialization encodes:
 *   Ticket/ISA  → PayoutAssetId = JSON.stringify(itemRows[0])
 *   Currency    → PayoutAssetId = payoutAssetId (plain string)
 */
import { describe, it, expect, beforeEach, afterEach, vi, type MockInstance } from 'vitest'
import { mountAttachmentEditor, type AttachmentEditorHandle } from '../modules/attachment-editor'
import type { AttachmentDraft, ItemSpecificAsset } from '../types'
import { Rarity } from '../types'

// ─── Fixtures ─────────────────────────────────────────────────────────────────

const DEFAULT_ITEM_ROW: ItemSpecificAsset = {
  BlueprintId: '',
  CurrentLevel: 1,
  Rarity: Rarity.Common,
  InitialLevel: 1,
  FromSource: '',
}

const CURRENCY_DRAFT: AttachmentDraft = {
  payoutAssetId: 'gem',
  assetType: 'Currency',
  payoutAmount: 10,
  chance: 1,
  itemRows: [DEFAULT_ITEM_ROW],
}

const ITEM_DRAFT: AttachmentDraft = {
  payoutAssetId: 'W_Dagger',
  assetType: 'Item',
  payoutAmount: 1,
  chance: 0.5,
  itemRows: [DEFAULT_ITEM_ROW],
}

const TICKET_DRAFT: AttachmentDraft = {
  payoutAssetId: '',
  assetType: 'Ticket',
  payoutAmount: 1,
  chance: 1,
  itemRows: [{
    BlueprintId: 'tkt_grass_01',
    CurrentLevel: 1,
    Rarity: Rarity.Rare,
    InitialLevel: 1,
    FromSource: 'daily_reward',
  }],
}

const ISA_DRAFT: AttachmentDraft = {
  payoutAssetId: '',
  assetType: 'ItemSpecificAsset',
  payoutAmount: 1,
  chance: 1,
  itemRows: [{
    BlueprintId: 'W_Sword_Epic',
    CurrentLevel: 5,
    Rarity: Rarity.Epic,
    InitialLevel: 3,
    FromSource: 'event_reward',
  }],
}

const CURRENCY_OPTIONS = [{ id: 'gem', label: 'Gems' }, { id: 'gold', label: 'Gold' }]
const ITEM_OPTIONS     = [{ id: 'W_Dagger' }, { id: 'W_Sword_Epic' }]
const TICKET_OPTIONS   = [{ id: 'tkt_grass_01' }]

// ─── Setup / teardown ─────────────────────────────────────────────────────────

let container: HTMLDivElement
let handle: AttachmentEditorHandle
let confirmSpy: MockInstance

function mount(drafts: AttachmentDraft[], onChange = vi.fn()): AttachmentEditorHandle {
  return mountAttachmentEditor(
    container,
    drafts,
    { prefix: 'test', currencyOptions: CURRENCY_OPTIONS, itemOptions: ITEM_OPTIONS, ticketOptions: TICKET_OPTIONS },
    onChange,
  )
}

beforeEach(() => {
  container = document.createElement('div')
  document.body.appendChild(container)
  // Mock window.confirm for tests that delete meaningful drafts (§8.1)
  confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true)
})

afterEach(() => {
  if (handle) { try { handle.destroy() } catch { /* already destroyed */ } }
  document.body.removeChild(container)
  vi.restoreAllMocks()
})

// ─── Initial render ───────────────────────────────────────────────────────────

describe('mountAttachmentEditor — initial render', () => {
  it('renders with empty drafts — shows only Add button', () => {
    handle = mount([])
    const addBtn = container.querySelector('[data-action="att-add"]')
    expect(addBtn).not.toBeNull()
    expect(container.querySelectorAll('.attachment-row')).toHaveLength(0)
  })

  it('renders 1 row for 1 initial draft', () => {
    handle = mount([CURRENCY_DRAFT])
    expect(container.querySelectorAll('.attachment-row')).toHaveLength(1)
  })

  it('renders 2 rows for 2 initial drafts', () => {
    handle = mount([CURRENCY_DRAFT, TICKET_DRAFT])
    expect(container.querySelectorAll('.attachment-row')).toHaveLength(2)
  })

  it('Add Attachment button group is present', () => {
    handle = mount([])
    // New design: att-add-group with Currency / Item / Ticket buttons (§8.3)
    const addGroup = container.querySelector('.att-add-group')
    expect(addGroup).not.toBeNull()
    const addBtns = container.querySelectorAll('[data-action="att-add"]')
    expect(addBtns.length).toBeGreaterThan(0)
    expect(addGroup!.textContent).toContain('Add')
  })
})

// ─── getDrafts() — Currency byte-compat gate ──────────────────────────────────

describe('mountAttachmentEditor — getDrafts() Currency [byte-compat]', () => {
  it('[BYTE-COMPAT] Currency: payoutAssetId is a plain string (not JSON)', () => {
    handle = mount([CURRENCY_DRAFT])
    const drafts = handle.getDrafts()
    expect(drafts).toHaveLength(1)
    const d = drafts[0]
    // Must be plain string, NOT JSON-encoded object
    expect(typeof d.payoutAssetId).toBe('string')
    expect(d.payoutAssetId).not.toMatch(/^\{/)  // must not start with '{'
    expect(d.assetType).toBe('Currency')
  })

  it('[BYTE-COMPAT] Currency: payoutAmount preserved', () => {
    handle = mount([CURRENCY_DRAFT])
    const d = handle.getDrafts()[0]
    expect(d.payoutAmount).toBe(10)
  })

  it('[BYTE-COMPAT] Currency: chance preserved', () => {
    handle = mount([CURRENCY_DRAFT])
    const d = handle.getDrafts()[0]
    expect(d.chance).toBe(1)
  })
})

// ─── getDrafts() — Item byte-compat gate ──────────────────────────────────────

describe('mountAttachmentEditor — getDrafts() Item [byte-compat]', () => {
  it('[BYTE-COMPAT] Item: payoutAssetId is a plain string', () => {
    handle = mount([ITEM_DRAFT])
    const d = handle.getDrafts()[0]
    expect(typeof d.payoutAssetId).toBe('string')
    expect(d.payoutAssetId).not.toMatch(/^\{/)
    expect(d.assetType).toBe('Item')
  })

  it('[BYTE-COMPAT] Item: chance preserved (0.5)', () => {
    handle = mount([ITEM_DRAFT])
    const d = handle.getDrafts()[0]
    expect(d.chance).toBeCloseTo(0.5)
  })
})

// ─── getDrafts() — Ticket byte-compat gate ───────────────────────────────────

describe('mountAttachmentEditor — getDrafts() Ticket [byte-compat]', () => {
  it('[BYTE-COMPAT] Ticket: payoutAssetId is empty string (not plain ID)', () => {
    handle = mount([TICKET_DRAFT])
    const d = handle.getDrafts()[0]
    expect(d.payoutAssetId).toBe('')
    expect(d.assetType).toBe('Ticket')
  })

  it('[BYTE-COMPAT] Ticket: itemRows[0].BlueprintId preserved', () => {
    handle = mount([TICKET_DRAFT])
    const d = handle.getDrafts()[0]
    expect(d.itemRows).toHaveLength(1)
    expect(d.itemRows[0].BlueprintId).toBe('tkt_grass_01')
  })

  it('[BYTE-COMPAT] Ticket: itemRows[0].Rarity preserved', () => {
    handle = mount([TICKET_DRAFT])
    const d = handle.getDrafts()[0]
    expect(d.itemRows[0].Rarity).toBe(Rarity.Rare)
  })

  it('[BYTE-COMPAT] Ticket: itemRows[0].FromSource preserved', () => {
    handle = mount([TICKET_DRAFT])
    const d = handle.getDrafts()[0]
    expect(d.itemRows[0].FromSource).toBe('daily_reward')
  })

  it('[BYTE-COMPAT] Ticket: itemRows[0] object has required ISA fields', () => {
    handle = mount([TICKET_DRAFT])
    const row = handle.getDrafts()[0].itemRows[0]
    expect('BlueprintId'  in row).toBe(true)
    expect('CurrentLevel' in row).toBe(true)
    expect('Rarity'       in row).toBe(true)
    expect('InitialLevel' in row).toBe(true)
    expect('FromSource'   in row).toBe(true)
  })
})

// ─── getDrafts() — ItemSpecificAsset byte-compat gate ────────────────────────

describe('mountAttachmentEditor — getDrafts() ItemSpecificAsset [byte-compat]', () => {
  it('[BYTE-COMPAT] ISA: payoutAssetId is empty string', () => {
    handle = mount([ISA_DRAFT])
    const d = handle.getDrafts()[0]
    expect(d.payoutAssetId).toBe('')
    expect(d.assetType).toBe('ItemSpecificAsset')
  })

  it('[BYTE-COMPAT] ISA: itemRows[0].BlueprintId = "W_Sword_Epic"', () => {
    handle = mount([ISA_DRAFT])
    const d = handle.getDrafts()[0]
    expect(d.itemRows[0].BlueprintId).toBe('W_Sword_Epic')
  })

  it('[BYTE-COMPAT] ISA: itemRows[0].Rarity = Epic (3)', () => {
    handle = mount([ISA_DRAFT])
    const d = handle.getDrafts()[0]
    expect(d.itemRows[0].Rarity).toBe(Rarity.Epic)
  })

  it('[BYTE-COMPAT] ISA: itemRows[0].CurrentLevel = 5', () => {
    handle = mount([ISA_DRAFT])
    const d = handle.getDrafts()[0]
    expect(d.itemRows[0].CurrentLevel).toBe(5)
  })
})

// ─── setDrafts() + getDrafts() round-trip ─────────────────────────────────────

describe('mountAttachmentEditor — setDrafts/getDrafts round-trip', () => {
  it('setDrafts replaces existing drafts', () => {
    handle = mount([CURRENCY_DRAFT])
    expect(handle.getDrafts()).toHaveLength(1)
    handle.setDrafts([CURRENCY_DRAFT, ITEM_DRAFT])
    expect(handle.getDrafts()).toHaveLength(2)
  })

  it('setDrafts([]) → getDrafts() returns []', () => {
    handle = mount([CURRENCY_DRAFT])
    handle.setDrafts([])
    expect(handle.getDrafts()).toHaveLength(0)
  })

  it('setDrafts re-renders rows correctly', () => {
    handle = mount([])
    handle.setDrafts([TICKET_DRAFT, CURRENCY_DRAFT])
    expect(container.querySelectorAll('.attachment-row')).toHaveLength(2)
  })
})

// ─── Add / Remove buttons ─────────────────────────────────────────────────────

describe('mountAttachmentEditor — add/remove interactions', () => {
  it('clicking Add Attachment adds a row', () => {
    handle = mount([])
    const addBtn = container.querySelector<HTMLButtonElement>('[data-action="att-add"]')!
    addBtn.click()
    expect(container.querySelectorAll('.attachment-row')).toHaveLength(1)
  })

  it('clicking Remove button removes that row', () => {
    handle = mount([CURRENCY_DRAFT, ITEM_DRAFT])
    const removeBtn = container.querySelector<HTMLButtonElement>('[data-action="att-remove"]')!
    removeBtn.click()
    expect(container.querySelectorAll('.attachment-row')).toHaveLength(1)
  })

  it('clicking Add calls onChange callback', () => {
    const onChange = vi.fn()
    handle = mount([], onChange)
    container.querySelector<HTMLButtonElement>('[data-action="att-add"]')!.click()
    expect(onChange).toHaveBeenCalled()
  })
})

// ─── destroy() ────────────────────────────────────────────────────────────────

describe('mountAttachmentEditor — destroy()', () => {
  it('destroy() clears container innerHTML', () => {
    handle = mount([CURRENCY_DRAFT])
    handle.destroy()
    expect(container.innerHTML).toBe('')
  })
})

// ─── ISA/Ticket panel visibility ─────────────────────────────────────────────

describe('mountAttachmentEditor — JSON-object type panel visibility', () => {
  it('Currency type: ID combobox visible, ISA panel hidden', () => {
    handle = mount([CURRENCY_DRAFT])
    const plainIdDiv = container.querySelector<HTMLElement>('[id^="test-att-plainid-"]')
    const itemDiv    = container.querySelector<HTMLElement>('[id^="test-att-item-"]')
    expect(plainIdDiv?.hidden).toBe(false)
    expect(itemDiv?.hidden).toBe(true)
  })

  it('Ticket type: ID combobox hidden, ISA panel visible', () => {
    handle = mount([TICKET_DRAFT])
    const plainIdDiv = container.querySelector<HTMLElement>('[id^="test-att-plainid-"]')
    const itemDiv    = container.querySelector<HTMLElement>('[id^="test-att-item-"]')
    expect(plainIdDiv?.hidden).toBe(true)
    expect(itemDiv?.hidden).toBe(false)
  })

  it('ISA type: ISA sub-section visible, shows "Item configuration" label', () => {
    handle = mount([ISA_DRAFT])
    const itemDiv = container.querySelector<HTMLElement>('[id^="test-att-item-"]')
    expect(itemDiv?.hidden).toBe(false)
    // New design: sub-section label is "Item configuration" (§5.1)
    expect(itemDiv?.textContent).toContain('Item configuration')
  })

  it('Item type: ID combobox visible, ISA panel hidden', () => {
    handle = mount([ITEM_DRAFT])
    const plainIdDiv = container.querySelector<HTMLElement>('[id^="test-att-plainid-"]')
    const itemDiv    = container.querySelector<HTMLElement>('[id^="test-att-item-"]')
    expect(plainIdDiv?.hidden).toBe(false)
    expect(itemDiv?.hidden).toBe(true)
  })
})

// ─── Type change — field swap (CE-10–CE-14) ───────────────────────────────────
//
// Trigger type combobox selection by: focus → type new type → ArrowDown → Enter.
// This matches the combobox keyboard protocol in asset-selector.ts §4.3.

function triggerTypeChange(rowIndex: number, newType: string): void {
  const input = document.getElementById(`test-att-type-${rowIndex}`) as HTMLInputElement
  if (!input) throw new Error(`Type combobox input not found for row ${rowIndex}`)
  input.focus()
  input.value = newType
  input.dispatchEvent(new Event('input', { bubbles: true }))
  input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }))
  input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))
}

describe('mountAttachmentEditor — type change field swap (CE-10–CE-14)', () => {
  it('[CE-10] Currency→Ticket: ISA panel becomes visible, ID combobox becomes hidden', () => {
    handle = mount([CURRENCY_DRAFT])
    triggerTypeChange(0, 'Ticket')
    const plainIdDiv = container.querySelector<HTMLElement>('[id^="test-att-plainid-"]')
    const itemDiv    = container.querySelector<HTMLElement>('[id^="test-att-item-"]')
    expect(plainIdDiv?.hidden).toBe(true)
    expect(itemDiv?.hidden).toBe(false)
  })

  it('[CE-11] Ticket→Currency: ID combobox becomes visible, ISA panel becomes hidden', () => {
    handle = mount([TICKET_DRAFT])
    triggerTypeChange(0, 'Currency')
    const plainIdDiv = container.querySelector<HTMLElement>('[id^="test-att-plainid-"]')
    const itemDiv    = container.querySelector<HTMLElement>('[id^="test-att-item-"]')
    expect(plainIdDiv?.hidden).toBe(false)
    expect(itemDiv?.hidden).toBe(true)
  })

  it('[CE-12] Currency→Item: getDrafts()[0].assetType changes to "Item"', () => {
    handle = mount([CURRENCY_DRAFT])
    triggerTypeChange(0, 'Item')
    const d = handle.getDrafts()[0]
    expect(d.assetType).toBe('Item')
  })

  it('[CE-13] Ticket→Currency: getDrafts()[0].payoutAssetId is "" (stale ISA data not leaked)', () => {
    handle = mount([TICKET_DRAFT])
    triggerTypeChange(0, 'Currency')
    const d = handle.getDrafts()[0]
    expect(d.payoutAssetId).toBe('')
    expect(d.assetType).toBe('Currency')
  })

  it('[CE-14] type change fires onChange callback', () => {
    const onChange = vi.fn()
    handle = mount([CURRENCY_DRAFT], onChange)
    const callsBefore = onChange.mock.calls.length
    triggerTypeChange(0, 'Ticket')
    expect(onChange.mock.calls.length).toBeGreaterThan(callsBefore)
  })
})

// ─── Remove last row (CE-22) ─────────────────────────────────────────────────

describe('mountAttachmentEditor — remove last row (CE-22)', () => {
  it('[CE-22] removing last row → 0 rows; Add button still present', () => {
    handle = mount([CURRENCY_DRAFT])
    const removeBtn = container.querySelector<HTMLButtonElement>('[data-action="att-remove"]')!
    removeBtn.click()
    expect(container.querySelectorAll('.attachment-row')).toHaveLength(0)
    expect(container.querySelector('[data-action="att-add"]')).not.toBeNull()
  })
})

// ─── setDrafts preserves all ISA fields (CE-51–CE-52) ────────────────────────

describe('mountAttachmentEditor — setDrafts preserves all ISA fields (CE-51–CE-52)', () => {
  it('[CE-51] setDrafts with ISA CurrentLevel=5 → getDrafts()[0].itemRows[0].CurrentLevel === 5', () => {
    handle = mount([])
    handle.setDrafts([ISA_DRAFT])
    expect(handle.getDrafts()[0].itemRows[0].CurrentLevel).toBe(5)
  })

  it('[CE-52] setDrafts with ISA InitialLevel=3 → getDrafts()[0].itemRows[0].InitialLevel === 3', () => {
    handle = mount([])
    handle.setDrafts([ISA_DRAFT])
    expect(handle.getDrafts()[0].itemRows[0].InitialLevel).toBe(3)
  })

  it('[CE-52b] setDrafts with Ticket InitialLevel=1 → InitialLevel preserved', () => {
    handle = mount([])
    handle.setDrafts([TICKET_DRAFT])
    expect(handle.getDrafts()[0].itemRows[0].InitialLevel).toBe(1)
  })
})

// ─── InitLvl input field present (CE-73) ─────────────────────────────────────

describe('mountAttachmentEditor — InitLvl input present when ISA panel visible (CE-73)', () => {
  it('[CE-73] ISA type: #test-att-il-0 input exists', () => {
    handle = mount([ISA_DRAFT])
    const ilInput = document.getElementById('test-att-il-0')
    expect(ilInput).not.toBeNull()
  })

  it('[CE-73b] ISA type: InitLvl input value matches ISA_DRAFT.InitialLevel (3)', () => {
    handle = mount([ISA_DRAFT])
    const ilInput = document.getElementById('test-att-il-0') as HTMLInputElement | null
    expect(ilInput?.value).toBe('3')
  })

  it('[CE-73c] Currency type: ISA panel hidden, InitLvl input not visible', () => {
    handle = mount([CURRENCY_DRAFT])
    const itemDiv = container.querySelector<HTMLElement>('[id^="test-att-item-"]')
    // ISA panel is hidden — InitLvl input exists in DOM but inside hidden panel
    expect(itemDiv?.hidden).toBe(true)
  })
})

// ─── Asset options filtered by type (CE-80–CE-81) ────────────────────────────
//
// Verify the ID combobox is mounted with the correct option set per type.
// Strategy: focus the ID input to open the listbox, then check data-value attrs.
// CE-82 (Ticket BlueprintId from ticketOptions) and CE-83 (ISA from itemOptions)
// are [PENDING-IMPL]: current source uses plain text input for ISA/Ticket BlueprintId,
// not a combobox. These rows stay in the matrix awaiting implementation.

function idComboboxValues(): string[] {
  const listbox = document.getElementById('test-att-id-0-listbox') as HTMLUListElement | null
  if (!listbox) return []
  return Array.from(listbox.querySelectorAll<HTMLElement>('[data-value]'))
    .map(li => li.getAttribute('data-value') ?? '')
}

describe('mountAttachmentEditor — asset options filtered by type (CE-80–CE-81)', () => {
  it('[CE-80] Currency type: ID combobox listbox contains gem and gold (currencyOptions)', () => {
    handle = mount([CURRENCY_DRAFT])
    const idInput = document.getElementById('test-att-id-0') as HTMLInputElement | null
    idInput?.focus()
    const vals = idComboboxValues()
    expect(vals).toContain('gem')
    expect(vals).toContain('gold')
  })

  it('[CE-80b] Currency type: ID combobox listbox does NOT contain W_Dagger (itemOption)', () => {
    handle = mount([CURRENCY_DRAFT])
    const idInput = document.getElementById('test-att-id-0') as HTMLInputElement | null
    idInput?.focus()
    const vals = idComboboxValues()
    expect(vals).not.toContain('W_Dagger')
  })

  it('[CE-81] Item type: ID combobox listbox contains W_Dagger (itemOptions)', () => {
    handle = mount([ITEM_DRAFT])
    const idInput = document.getElementById('test-att-id-0') as HTMLInputElement | null
    idInput?.focus()
    const vals = idComboboxValues()
    expect(vals).toContain('W_Dagger')
  })

  it('[CE-81b] Item type: ID combobox listbox does NOT contain gem (currencyOption)', () => {
    handle = mount([ITEM_DRAFT])
    const idInput = document.getElementById('test-att-id-0') as HTMLInputElement | null
    idInput?.focus()
    const vals = idComboboxValues()
    expect(vals).not.toContain('gem')
  })
})

// ─── Legacy warning span (CE-90–CE-91) ───────────────────────────────────────

describe('mountAttachmentEditor — legacy warning span (CE-90–CE-91)', () => {
  const LEGACY_DRAFT: AttachmentDraft = {
    payoutAssetId: '',
    assetType: 'Ticket',
    payoutAmount: 1,
    chance: 1,
    itemRows: [{ BlueprintId: '', CurrentLevel: 1, Rarity: Rarity.Common, InitialLevel: 1, FromSource: '' }],
    _legacyWarning: 'Ticket PayoutAssetId was a plain string in legacy format',
  }

  it('[CE-90] draft with _legacyWarning → .att-legacy-warn span rendered', () => {
    handle = mount([LEGACY_DRAFT])
    const warn = container.querySelector('.att-legacy-warn')
    expect(warn).not.toBeNull()
  })

  it('[CE-90b] legacy warn span has non-empty textContent (warns user)', () => {
    handle = mount([LEGACY_DRAFT])
    const warn = container.querySelector('.att-legacy-warn')
    expect(warn?.textContent?.trim().length).toBeGreaterThan(0)
  })

  it('[CE-91] draft WITHOUT _legacyWarning → no .att-legacy-warn span', () => {
    handle = mount([CURRENCY_DRAFT])
    expect(container.querySelector('.att-legacy-warn')).toBeNull()
  })
})

// ─── Collapsible card structure (§3, §4) ─────────────────────────────────────

describe('mountAttachmentEditor — collapsible card structure', () => {
  it('renders <details> element with .attachment-row class', () => {
    handle = mount([CURRENCY_DRAFT])
    const details = container.querySelector('details.attachment-row')
    expect(details).not.toBeNull()
  })

  it('new card renders with open attribute (expanded by default)', () => {
    handle = mount([CURRENCY_DRAFT])
    const details = container.querySelector('details.attachment-row')
    // happy-dom may not implement HTMLDetailsElement.open; check attribute presence
    expect(details?.hasAttribute('open')).toBe(true)
  })

  it('collapsed <details> keeps inputs in DOM — getDrafts() returns correct values', () => {
    handle = mount([CURRENCY_DRAFT])
    const details = container.querySelector('details.attachment-row') as HTMLDetailsElement | null
    if (details) details.open = false  // collapse
    const drafts = handle.getDrafts()
    expect(drafts[0].assetType).toBe('Currency')
    expect(drafts[0].payoutAmount).toBe(10)
  })

  it('summary text contains type, amount, chance% — R4/R5 format', () => {
    handle = mount([CURRENCY_DRAFT])
    const summary = container.querySelector('summary')
    // "Attachment #1 · Currency · gem · x10 · 100%"
    expect(summary?.textContent).toContain('Currency')
    expect(summary?.textContent).toContain('x10')
    expect(summary?.textContent).toContain('100%')
  })

  it('ISA summary shows BlueprintId as identity', () => {
    handle = mount([ISA_DRAFT])
    const summary = container.querySelector('summary')
    expect(summary?.textContent).toContain('W_Sword_Epic')
  })

  it('Ticket summary shows BlueprintId as identity', () => {
    handle = mount([TICKET_DRAFT])
    const summary = container.querySelector('summary')
    expect(summary?.textContent).toContain('tkt_grass_01')
  })

  it('draft with empty payoutAssetId → summary shows "(no id)"', () => {
    const emptyDraft: AttachmentDraft = { payoutAssetId: '', assetType: 'Currency', payoutAmount: 1, chance: 1, itemRows: [] }
    handle = mount([emptyDraft])
    const summary = container.querySelector('summary')
    expect(summary?.textContent).toContain('(no id)')
  })
})

// ─── Delete: confirm gate (§8.1) ────────────────────────────────────────────

describe('mountAttachmentEditor — delete confirm gate (§8.1)', () => {
  it('meaningful draft: window.confirm called on delete', () => {
    handle = mount([CURRENCY_DRAFT])
    container.querySelector<HTMLButtonElement>('[data-action="att-remove"]')!.click()
    expect(confirmSpy).toHaveBeenCalled()
  })

  it('meaningful draft: confirm=true → row deleted', () => {
    confirmSpy.mockReturnValue(true)
    handle = mount([CURRENCY_DRAFT])
    container.querySelector<HTMLButtonElement>('[data-action="att-remove"]')!.click()
    expect(container.querySelectorAll('.attachment-row')).toHaveLength(0)
  })

  it('meaningful draft: confirm=false → row preserved', () => {
    confirmSpy.mockReturnValue(false)
    handle = mount([CURRENCY_DRAFT])
    container.querySelector<HTMLButtonElement>('[data-action="att-remove"]')!.click()
    expect(container.querySelectorAll('.attachment-row')).toHaveLength(1)
  })

  it('blank/default draft: no confirm, deletes immediately', () => {
    const blankDraft: AttachmentDraft = { payoutAssetId: '', assetType: 'Currency', payoutAmount: 1, chance: 1, itemRows: [] }
    handle = mount([blankDraft])
    container.querySelector<HTMLButtonElement>('[data-action="att-remove"]')!.click()
    expect(confirmSpy).not.toHaveBeenCalled()
    expect(container.querySelectorAll('.attachment-row')).toHaveLength(0)
  })
})

// ─── Duplicate (§8.2) ────────────────────────────────────────────────────────

describe('mountAttachmentEditor — duplicate (§8.2)', () => {
  it('Duplicate button is present in card header', () => {
    handle = mount([CURRENCY_DRAFT])
    const dupBtn = container.querySelector('[data-action="att-duplicate"]')
    expect(dupBtn).not.toBeNull()
  })

  it('clicking Duplicate inserts clone at idx+1', () => {
    handle = mount([CURRENCY_DRAFT, ITEM_DRAFT])
    const firstDupBtn = container.querySelector<HTMLButtonElement>('[data-action="att-duplicate"]')!
    firstDupBtn.click()
    expect(handle.getDrafts()).toHaveLength(3)
    // Clone of CURRENCY_DRAFT should be at index 1
    expect(handle.getDrafts()[1].assetType).toBe('Currency')
    expect(handle.getDrafts()[1].payoutAmount).toBe(10)
    // Original ITEM_DRAFT moved to index 2
    expect(handle.getDrafts()[2].assetType).toBe('Item')
  })

  it('Duplicate clones itemRows as deep copy for ISA', () => {
    handle = mount([ISA_DRAFT])
    container.querySelector<HTMLButtonElement>('[data-action="att-duplicate"]')!.click()
    const [orig, clone] = handle.getDrafts()
    expect(clone.itemRows[0].BlueprintId).toBe(orig.itemRows[0].BlueprintId)
    // Mutation of clone does not affect original
    clone.itemRows[0].BlueprintId = 'mutated'
    expect(orig.itemRows[0].BlueprintId).toBe('W_Sword_Epic')
  })

  it('Duplicate does NOT copy _legacyWarning', () => {
    const legacyDraft: AttachmentDraft = {
      ...TICKET_DRAFT, _legacyWarning: 'legacy',
    }
    handle = mount([legacyDraft])
    container.querySelector<HTMLButtonElement>('[data-action="att-duplicate"]')!.click()
    const clone = handle.getDrafts()[1]
    expect(clone._legacyWarning).toBeUndefined()
  })

  it('Duplicate does NOT copy _unknownIdWarning', () => {
    const unknownDraft: AttachmentDraft = {
      ...CURRENCY_DRAFT, _unknownIdWarning: 'not in list',
    }
    handle = mount([unknownDraft])
    container.querySelector<HTMLButtonElement>('[data-action="att-duplicate"]')!.click()
    const clone = handle.getDrafts()[1]
    expect(clone._unknownIdWarning).toBeUndefined()
  })
})

// ─── Add with type pre-pick (§8.3) ───────────────────────────────────────────

describe('mountAttachmentEditor — add with type pre-pick (§8.3)', () => {
  it('Currency add button creates Currency draft', () => {
    handle = mount([])
    const btn = container.querySelector<HTMLButtonElement>('[data-action="att-add"][data-assettype="Currency"]')!
    btn.click()
    expect(handle.getDrafts()[0].assetType).toBe('Currency')
  })

  it('ISA add button creates ItemSpecificAsset draft', () => {
    handle = mount([])
    const btn = container.querySelector<HTMLButtonElement>('[data-action="att-add"][data-assettype="ItemSpecificAsset"]')!
    btn.click()
    expect(handle.getDrafts()[0].assetType).toBe('ItemSpecificAsset')
  })

  it('Ticket add button creates Ticket draft', () => {
    handle = mount([])
    const btn = container.querySelector<HTMLButtonElement>('[data-action="att-add"][data-assettype="Ticket"]')!
    btn.click()
    expect(handle.getDrafts()[0].assetType).toBe('Ticket')
  })

  it('add button without data-assettype defaults to Currency', () => {
    handle = mount([])
    // Manually trigger click on a synthetic button without data-assettype
    const btn = document.createElement('button')
    btn.dataset['action'] = 'att-add'
    btn.dataset['prefix'] = 'test'
    container.appendChild(btn)
    btn.click()
    // After click, render() replaces container.innerHTML — btn is detached.
    // Just verify a Currency draft was added.
    const drafts = handle.getDrafts()
    expect(drafts[drafts.length - 1].assetType).toBe('Currency')
  })
})

// ─── Chance numeric + slider sync (§6) ───────────────────────────────────────

describe('mountAttachmentEditor — chance numeric + slider (§6)', () => {
  it('numeric chance input renders with correct initial value', () => {
    handle = mount([ITEM_DRAFT])  // chance=0.5
    const numEl = document.getElementById('test-att-chance-num-0') as HTMLInputElement | null
    expect(parseFloat(numEl?.value ?? '')).toBeCloseTo(0.5)
  })

  it('slider renders with correct initial value', () => {
    handle = mount([ITEM_DRAFT])  // chance=0.5
    const slider = document.getElementById('test-att-chance-0') as HTMLInputElement | null
    expect(parseFloat(slider?.value ?? '')).toBeCloseTo(0.5)
  })

  it('_readDrafts prefers numeric input value when valid', () => {
    handle = mount([CURRENCY_DRAFT])  // chance=1
    const numEl = document.getElementById('test-att-chance-num-0') as HTMLInputElement | null
    if (numEl) {
      numEl.value = '0.75'
      numEl.dispatchEvent(new Event('input', { bubbles: true }))
    }
    const drafts = handle.getDrafts()
    expect(drafts[0].chance).toBeCloseTo(0.75)
  })

  it('numeric input syncs slider on input event', () => {
    handle = mount([CURRENCY_DRAFT])
    const numEl    = document.getElementById('test-att-chance-num-0') as HTMLInputElement | null
    const sliderEl = document.getElementById('test-att-chance-0')     as HTMLInputElement | null
    if (numEl) {
      numEl.value = '0.3'
      numEl.dispatchEvent(new Event('input', { bubbles: true }))
    }
    expect(parseFloat(sliderEl?.value ?? '')).toBeCloseTo(0.3)
  })

  it('slider syncs numeric input on input event', () => {
    handle = mount([CURRENCY_DRAFT])
    const sliderEl = document.getElementById('test-att-chance-0')     as HTMLInputElement | null
    const numEl    = document.getElementById('test-att-chance-num-0') as HTMLInputElement | null
    if (sliderEl) {
      sliderEl.value = '0.6'
      sliderEl.dispatchEvent(new Event('input', { bubbles: true }))
    }
    expect(numEl?.value).toBe('0.60')
  })
})

// ─── Blueprint ID combobox for ISA/Ticket (§5.2, §9.3) ───────────────────────

describe('mountAttachmentEditor — Blueprint ID combobox (§5.2)', () => {
  it('ISA type: #test-att-bp-wrap-0 combobox container renders', () => {
    handle = mount([ISA_DRAFT])
    const wrap = document.getElementById('test-att-bp-wrap-0')
    expect(wrap).not.toBeNull()
    // Should have a combobox input inside
    expect(wrap?.querySelector('.combobox-input')).not.toBeNull()
  })

  it('ISA type: Blueprint ID combobox initialised with ISA_DRAFT BlueprintId', () => {
    handle = mount([ISA_DRAFT])
    const bpInput = document.getElementById('test-att-bp-0') as HTMLInputElement | null
    expect(bpInput?.value).toBe('W_Sword_Epic')
  })

  it('Ticket type: #test-att-bp-wrap-0 combobox container renders', () => {
    handle = mount([TICKET_DRAFT])
    const wrap = document.getElementById('test-att-bp-wrap-0')
    expect(wrap).not.toBeNull()
    expect(wrap?.querySelector('.combobox-input')).not.toBeNull()
  })

  it('Ticket type: Blueprint ID combobox initialised with TICKET_DRAFT BlueprintId', () => {
    handle = mount([TICKET_DRAFT])
    const bpInput = document.getElementById('test-att-bp-0') as HTMLInputElement | null
    expect(bpInput?.value).toBe('tkt_grass_01')
  })

  it('ISA getDrafts() reads BlueprintId from bp combobox value', () => {
    handle = mount([ISA_DRAFT])
    const bpInput = document.getElementById('test-att-bp-0') as HTMLInputElement | null
    if (bpInput) {
      // Simulate user typing: set value, fire input (opens list), then Tab to commit
      bpInput.value = 'bp_new'
      bpInput.dispatchEvent(new Event('input', { bubbles: true }))
      bpInput.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }))
    }
    const drafts = handle.getDrafts()
    expect(drafts[0].itemRows[0].BlueprintId).toBe('bp_new')
  })
})

// ─── Contextual ID labels (§5.1) ─────────────────────────────────────────────

describe('mountAttachmentEditor — contextual ID labels', () => {
  it('Currency: ID field label is "Currency ID"', () => {
    handle = mount([CURRENCY_DRAFT])
    const plainId = container.querySelector<HTMLElement>('[id^="test-att-plainid-"]')
    expect(plainId?.querySelector('label')?.textContent).toContain('Currency ID')
  })

  it('Item: ID field label is "Payout Asset ID"', () => {
    handle = mount([ITEM_DRAFT])
    const plainId = container.querySelector<HTMLElement>('[id^="test-att-plainid-"]')
    expect(plainId?.querySelector('label')?.textContent).toContain('Payout Asset ID')
  })
})

// ─── Ticket sub-section label (§5.1) ─────────────────────────────────────────

describe('mountAttachmentEditor — Ticket sub-section label', () => {
  it('Ticket type: sub-section shows "Ticket configuration"', () => {
    handle = mount([TICKET_DRAFT])
    const itemDiv = container.querySelector<HTMLElement>('[id^="test-att-item-"]')
    expect(itemDiv?.textContent).toContain('Ticket configuration')
  })
})

// ─── Inline validation error display (CE-60–CE-61) ───────────────────────────
//
// _runValidation runs an initial pass on mount (attachment-editor.ts:540).
// Invalid initial drafts trigger error span population immediately.

describe('mountAttachmentEditor — inline validation error display (CE-60/CE-61)', () => {
  it('[CE-60] payoutAmount=0: #test-att-err-amt-0 shows error text', () => {
    const invalidDraft: AttachmentDraft = { ...CURRENCY_DRAFT, payoutAmount: 0 }
    handle = mount([invalidDraft])
    const errEl = document.getElementById('test-att-err-amt-0')
    expect(errEl?.textContent?.trim().length).toBeGreaterThan(0)
  })

  it('[CE-60b] payoutAmount=0: card gets att-card-invalid class', () => {
    const invalidDraft: AttachmentDraft = { ...CURRENCY_DRAFT, payoutAmount: 0 }
    handle = mount([invalidDraft])
    // att-card-invalid is toggled on #test-att-card-0 (the outer .att-card div)
    const card = document.getElementById('test-att-card-0')
    expect(card?.classList.contains('att-card-invalid')).toBe(true)
  })

  it('[CE-60c] payoutAmount=0: err-marker is visible (not hidden)', () => {
    const invalidDraft: AttachmentDraft = { ...CURRENCY_DRAFT, payoutAmount: 0 }
    handle = mount([invalidDraft])
    const marker = document.getElementById('test-att-err-marker-0') as HTMLElement | null
    expect(marker?.hidden).toBe(false)
  })

  it('[CE-61] chance=0: #test-att-err-chance-0 shows error text', () => {
    const invalidDraft: AttachmentDraft = { ...CURRENCY_DRAFT, chance: 0 }
    handle = mount([invalidDraft])
    const errEl = document.getElementById('test-att-err-chance-0')
    expect(errEl?.textContent?.trim().length).toBeGreaterThan(0)
  })

  it('[CE-61b] chance>1: #test-att-err-chance-0 shows error text', () => {
    const invalidDraft: AttachmentDraft = { ...CURRENCY_DRAFT, chance: 1.5 }
    handle = mount([invalidDraft])
    const errEl = document.getElementById('test-att-err-chance-0')
    expect(errEl?.textContent?.trim().length).toBeGreaterThan(0)
  })

  it('[CE-60/61 negative] valid amount/chance: no amount or chance error text', () => {
    // CURRENCY_DRAFT has valid payoutAmount=10 and chance=1.
    // Scoped to amount+chance errors only — not card class (ID validation also runs).
    handle = mount([CURRENCY_DRAFT])
    const amtErr    = document.getElementById('test-att-err-amt-0')
    const chanceErr = document.getElementById('test-att-err-chance-0')
    expect(amtErr?.textContent?.trim()).toBe('')
    expect(chanceErr?.textContent?.trim()).toBe('')
  })
})
