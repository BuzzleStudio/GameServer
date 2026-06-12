// src/modules/attachment-editor.ts
// Compact attachment editor rows with combobox support (Step 3).
// Replaces the inline datalist approach in the drawer.

import { mountCombobox } from './asset-selector'
import type { ComboboxOption, ComboboxHandle } from './asset-selector'
import type { AttachmentDraft, ItemSpecificAsset, RarityValue } from '../types'
import { Rarity, RARITY_LABELS } from '../types'

// ─── Types ────────────────────────────────────────────────────────────────────

export interface AttachmentEditorOptions {
  prefix:          string
  currencyOptions: ComboboxOption[]
  itemOptions:     ComboboxOption[]
  ticketOptions:   ComboboxOption[]
  disabled?:       boolean
}

export interface AttachmentEditorHandle {
  getDrafts(): AttachmentDraft[]
  setDrafts(drafts: AttachmentDraft[]): void
  destroy(): void
}

// ─── Mount ────────────────────────────────────────────────────────────────────

export function mountAttachmentEditor(
  container: HTMLElement,
  initialDrafts: AttachmentDraft[],
  opts: AttachmentEditorOptions,
  onChange: (drafts: AttachmentDraft[]) => void,
): AttachmentEditorHandle {
  const { prefix, currencyOptions, itemOptions, ticketOptions } = opts
  let drafts: AttachmentDraft[] = [...initialDrafts]
  const comboboxes = new Map<string, ComboboxHandle>()

  function render() {
    // Destroy any existing comboboxes
    comboboxes.forEach(h => h.destroy())
    comboboxes.clear()

    container.innerHTML = _renderAll(prefix, drafts, opts.disabled ?? false)

    // Mount comboboxes per row
    drafts.forEach((d, i) => {
      _mountRowComboboxes(i, d, prefix, currencyOptions, itemOptions, ticketOptions, comboboxes, () => {
        onChange(_readDrafts(prefix, drafts, comboboxes))
      })
    })
  }

  // Delegated click handler — added once, stable across re-renders
  const clickHandler = (e: Event) => {
    const target  = e.target as HTMLElement
    const addBtn  = target.closest<HTMLElement>('[data-action="att-add"]')
    const remBtn  = target.closest<HTMLElement>('[data-action="att-remove"]')

    if (addBtn && addBtn.dataset['prefix'] === prefix) {
      drafts = _readDrafts(prefix, drafts, comboboxes)
      drafts.push(_defaultDraft())
      onChange(drafts)
      render()
    } else if (remBtn && remBtn.dataset['prefix'] === prefix) {
      const idx = parseInt(remBtn.dataset['idx'] ?? '0', 10)
      drafts = _readDrafts(prefix, drafts, comboboxes)
      drafts.splice(idx, 1)
      onChange(drafts)
      render()
    }
  }

  container.addEventListener('click', clickHandler)
  render()

  return {
    getDrafts:  () => _readDrafts(prefix, drafts, comboboxes),
    setDrafts:  (d) => { drafts = [...d]; render() },
    destroy:    () => {
      comboboxes.forEach(h => h.destroy())
      comboboxes.clear()
      container.removeEventListener('click', clickHandler)
      container.innerHTML = ''
    },
  }
}

// ─── Rendering helpers ────────────────────────────────────────────────────────

function _esc(s: string): string {
  return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
}

function _defaultItemRow(): ItemSpecificAsset {
  return { BlueprintId: '', CurrentLevel: 1, Rarity: Rarity.Common, InitialLevel: 1, FromSource: '' }
}

function _defaultDraft(): AttachmentDraft {
  return { payoutAssetId: '', assetType: 'Currency', payoutAmount: 1, chance: 1, itemRows: [_defaultItemRow()] }
}

function _isJsonObjType(t: string): boolean {
  const l = t.trim().toLowerCase()
  return l === 'itemspecificasset' || l === 'ticket'
}

function _renderAll(prefix: string, drafts: AttachmentDraft[], disabled: boolean): string {
  const dis = disabled ? 'disabled' : ''
  const rows = drafts.map((d, i) => _renderRow(prefix, i, d, disabled)).join('')
  return `${rows}<button type="button" class="btn btn-ghost btn-sm" data-action="att-add" data-prefix="${prefix}" ${dis}>+ Add Attachment</button>`
}

function _renderRow(prefix: string, i: number, d: AttachmentDraft, disabled: boolean): string {
  const isJson    = _isJsonObjType(d.assetType)
  const dis       = disabled ? 'disabled' : ''
  const itemRow   = d.itemRows?.[0] ?? _defaultItemRow()
  const rarityOpts = (Object.entries(RARITY_LABELS) as [string, string][])
    .map(([v, lbl]) => `<option value="${v}" ${Number(v) === itemRow.Rarity ? 'selected' : ''}>${lbl}</option>`)
    .join('')
  const legacyWarn = d._legacyWarning
    ? `<span class="att-legacy-warn" title="${_esc(d._legacyWarning)}">⚠ legacy format</span>`
    : ''

  return `
<div class="attachment-row" id="${prefix}-att-row-${i}">
  <div class="att-row-controls">
    <div class="form-group att-type-field">
      <label>Type</label>
      <div id="${prefix}-att-type-wrap-${i}" class="combobox-container"></div>
    </div>
    <div class="form-group att-amount-field">
      <label>Amount</label>
      <input type="number" id="${prefix}-att-amt-${i}" value="${d.payoutAmount}" min="1" style="width:80px" ${dis} />
    </div>
    <div class="form-group att-chance-field">
      <label>Chance <span id="${prefix}-att-clbl-${i}">${d.chance.toFixed(2)}</span></label>
      <input type="range" id="${prefix}-att-chance-${i}" min="0.01" max="1" step="0.01" value="${d.chance}" ${dis} />
    </div>
    <button type="button" class="btn btn-ghost btn-sm att-remove-btn" data-action="att-remove" data-prefix="${prefix}" data-idx="${i}" ${dis}>✕</button>
  </div>
  ${legacyWarn}
  <div id="${prefix}-att-plainid-${i}" ${isJson ? 'hidden' : ''} class="form-group">
    <label>ID</label>
    <div id="${prefix}-att-id-wrap-${i}" class="combobox-container"></div>
  </div>
  <div id="${prefix}-att-item-${i}" ${!isJson ? 'hidden' : ''}>
    <div class="att-isa-label">${d.assetType.toLowerCase() === 'ticket' ? 'Ticket' : 'ItemSpecificAsset'} (JSON object)</div>
    <div class="att-isa-fields">
      <div class="form-group" style="min-width:150px"><label>BlueprintId</label><input type="text" id="${prefix}-att-bp-${i}" value="${_esc(itemRow.BlueprintId)}" placeholder="blueprint_id" ${dis} /></div>
      <div class="form-group" style="width:65px"><label>Level</label><input type="number" id="${prefix}-att-cl-${i}" value="${itemRow.CurrentLevel}" min="1" ${dis} /></div>
      <div class="form-group"><label>Rarity</label><select id="${prefix}-att-rar-${i}" ${dis}>${rarityOpts}</select></div>
      <div class="form-group" style="width:65px"><label>InitLvl</label><input type="number" id="${prefix}-att-il-${i}" value="${itemRow.InitialLevel}" min="1" ${dis} /></div>
      <div class="form-group"><label>Source</label><input type="text" id="${prefix}-att-fs-${i}" value="${_esc(itemRow.FromSource)}" placeholder="source" ${dis} /></div>
    </div>
  </div>
</div>`
}

// ─── Combobox mounting ────────────────────────────────────────────────────────

const ASSET_TYPE_OPTIONS: ComboboxOption[] = [
  { id: 'Currency' }, { id: 'Item' }, { id: 'Ticket' }, { id: 'ItemSpecificAsset' },
]

function _mountRowComboboxes(
  i: number,
  d: AttachmentDraft,
  prefix: string,
  currencyOptions: ComboboxOption[],
  itemOptions:     ComboboxOption[],
  ticketOptions:   ComboboxOption[],
  comboboxes:      Map<string, ComboboxHandle>,
  onChange:        () => void,
): void {
  // Type combobox
  const typeWrap = document.getElementById(`${prefix}-att-type-wrap-${i}`)
  if (typeWrap) {
    const typeHandle = mountCombobox({
      containerId:  `${prefix}-att-type-wrap-${i}`,
      inputId:      `${prefix}-att-type-${i}`,
      options:      ASSET_TYPE_OPTIONS,
      initialValue: d.assetType,
      placeholder:  'Currency, Item, …',
      allowUnknown: true,
      onChange: (newType) => {
        const isJson  = _isJsonObjType(newType)
        const plainEl = document.getElementById(`${prefix}-att-plainid-${i}`)
        const itemEl  = document.getElementById(`${prefix}-att-item-${i}`)
        if (plainEl) plainEl.hidden = isJson
        if (itemEl)  itemEl.hidden  = !isJson
        // Re-mount ID combobox for new type
        _mountIdCombobox(i, newType, prefix, currencyOptions, itemOptions, ticketOptions, comboboxes, onChange)
        onChange()
      },
    })
    comboboxes.set(`type-${i}`, typeHandle)
  }

  // ID combobox (for non-JSON-object types)
  _mountIdCombobox(i, d.assetType, prefix, currencyOptions, itemOptions, ticketOptions, comboboxes, onChange)

  // Chance slider label
  const slider = document.getElementById(`${prefix}-att-chance-${i}`) as HTMLInputElement | null
  const label  = document.getElementById(`${prefix}-att-clbl-${i}`)
  if (slider && label) {
    slider.addEventListener('input', () => { label.textContent = parseFloat(slider.value).toFixed(2); onChange() })
  }
}

function _mountIdCombobox(
  i: number,
  assetType: string,
  prefix: string,
  currencyOptions: ComboboxOption[],
  itemOptions:     ComboboxOption[],
  ticketOptions:   ComboboxOption[],
  comboboxes:      Map<string, ComboboxHandle>,
  onChange:        () => void,
): void {
  if (_isJsonObjType(assetType)) return   // JSON-object types have no ID combobox

  const wrapId  = `${prefix}-att-id-wrap-${i}`
  const wrapEl  = document.getElementById(wrapId)
  if (!wrapEl) return

  // Destroy existing
  const existing = comboboxes.get(`id-${i}`)
  if (existing) { existing.destroy(); comboboxes.delete(`id-${i}`) }

  const lt   = assetType.toLowerCase()
  const opts = lt === 'currency' ? currencyOptions
    : lt === 'item'   ? itemOptions
    : lt === 'ticket' ? ticketOptions
    : []  // custom type: empty, allow unknown

  // Preserve current value if input already exists
  const existingInput = document.getElementById(`${prefix}-att-id-${i}`) as HTMLInputElement | null
  const currentVal = existingInput?.value ?? ''

  const idHandle = mountCombobox({
    containerId:  wrapId,
    inputId:      `${prefix}-att-id-${i}`,
    options:      opts,
    initialValue: currentVal,
    placeholder:  'asset ID',
    allowUnknown: true,
    onChange,
  })
  comboboxes.set(`id-${i}`, idHandle)
}

// ─── Read drafts from DOM ─────────────────────────────────────────────────────

function _readDrafts(
  prefix: string,
  drafts: AttachmentDraft[],
  comboboxes: Map<string, ComboboxHandle>,
): AttachmentDraft[] {
  return drafts.map((d, i) => {
    const typeHandle = comboboxes.get(`type-${i}`)
    const idHandle   = comboboxes.get(`id-${i}`)
    const assetType  = typeHandle?.getValue() ?? d.assetType
    const isJson     = _isJsonObjType(assetType)
    const payoutAssetId = isJson ? '' : (idHandle?.getValue() ?? d.payoutAssetId)

    const amtEl    = document.getElementById(`${prefix}-att-amt-${i}`)    as HTMLInputElement | null
    const chanceEl = document.getElementById(`${prefix}-att-chance-${i}`) as HTMLInputElement | null
    const payoutAmount = parseInt(amtEl?.value ?? '1', 10)    || d.payoutAmount
    const chance       = parseFloat(chanceEl?.value ?? String(d.chance)) || d.chance

    let itemRows: ItemSpecificAsset[] = [_defaultItemRow()]
    if (isJson) {
      const bpEl  = document.getElementById(`${prefix}-att-bp-${i}`)  as HTMLInputElement | null
      const clEl  = document.getElementById(`${prefix}-att-cl-${i}`)  as HTMLInputElement | null
      const rarEl = document.getElementById(`${prefix}-att-rar-${i}`) as HTMLSelectElement | null
      const ilEl  = document.getElementById(`${prefix}-att-il-${i}`)  as HTMLInputElement | null
      const fsEl  = document.getElementById(`${prefix}-att-fs-${i}`)  as HTMLInputElement | null
      itemRows = [{
        BlueprintId:  bpEl?.value ?? '',
        CurrentLevel: parseInt(clEl?.value ?? '1', 10) || 1,
        Rarity:       (parseInt(rarEl?.value ?? '1', 10) || Rarity.Common) as RarityValue,
        InitialLevel: parseInt(ilEl?.value ?? '1', 10) || 1,
        FromSource:   fsEl?.value ?? '',
      }]
    }

    return { payoutAssetId, assetType, payoutAmount, chance, itemRows }
  })
}
