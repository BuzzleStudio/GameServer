// src/modules/attachment-editor.ts
// Collapsible attachment card editor (design §3–§9).
// Public API unchanged: mountAttachmentEditor / AttachmentEditorHandle / AttachmentEditorOptions.

import { mountCombobox } from './asset-selector'
import type { ComboboxOption, ComboboxHandle } from './asset-selector'
import type { AttachmentDraft, ItemSpecificAsset, RarityValue } from '../types'
import { Rarity, RARITY_LABELS } from '../types'
import {
  validateAttachmentType,
  validateAttachmentId,
  validateAttachmentAmount,
  validateAttachmentChance,
  validateBlueprintId,
  validateLevel,
} from './validation'

// ─── Public types ─────────────────────────────────────────────────────────────

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

// ─── Type configuration (§5) ─────────────────────────────────────────────────

interface TypeConfig {
  idLabel:        string
  showIdCombobox: boolean
  subSectionLabel: string | null
}

const TYPE_CONFIGS: Record<string, TypeConfig> = {
  Currency:        { idLabel: 'Currency ID',    showIdCombobox: true,  subSectionLabel: null },
  Item:            { idLabel: 'Payout Asset ID', showIdCombobox: true,  subSectionLabel: null },
  ItemSpecificAsset: { idLabel: '',             showIdCombobox: false, subSectionLabel: 'Item configuration' },
  Ticket:          { idLabel: '',               showIdCombobox: false, subSectionLabel: 'Ticket configuration' },
}

function _getTypeConfig(assetType: string): TypeConfig {
  return TYPE_CONFIGS[assetType] ?? { idLabel: 'Asset ID', showIdCombobox: true, subSectionLabel: null }
}

function _isJsonObjType(t: string): boolean {
  const l = t.trim().toLowerCase()
  return l === 'itemspecificasset' || l === 'ticket'
}

// ─── Summary helpers (§3.2) ──────────────────────────────────────────────────

function _summaryId(d: AttachmentDraft): string {
  const lt = d.assetType.toLowerCase()
  if (lt === 'itemspecificasset' || lt === 'ticket') {
    return d.itemRows[0]?.BlueprintId || '(no id)'
  }
  return d.payoutAssetId || '(no id)'
}

function _summaryText(n: number, d: AttachmentDraft): string {
  const id  = _summaryId(d)
  const pct = Math.round(d.chance * 100)
  return `Attachment #${n} · ${d.assetType} · ${id} · x${d.payoutAmount} · ${pct}%`
}

// ─── Meaningful draft check (§8.1) ───────────────────────────────────────────

function _isDraftMeaningful(d: AttachmentDraft): boolean {
  return d.payoutAssetId !== '' ||
    (d.itemRows[0]?.BlueprintId ?? '') !== '' ||
    d.payoutAmount !== 1 ||
    d.chance !== 1
}

// ─── Default draft / row ─────────────────────────────────────────────────────

function _defaultItemRow(): ItemSpecificAsset {
  return { BlueprintId: '', CurrentLevel: 1, Rarity: Rarity.Common, InitialLevel: 1, FromSource: '' }
}

function _defaultDraft(assetType = 'Currency'): AttachmentDraft {
  return { payoutAssetId: '', assetType, payoutAmount: 1, chance: 1, itemRows: [_defaultItemRow()] }
}

// ─── Escape helper ────────────────────────────────────────────────────────────

function _esc(s: unknown): string {
  return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
}

// ─── Rarity select options ───────────────────────────────────────────────────

function _rarityOpts(selected: number): string {
  return (Object.entries(RARITY_LABELS) as [string, string][])
    .map(([v, lbl]) => `<option value="${v}" ${Number(v) === selected ? 'selected' : ''}>${lbl}</option>`)
    .join('')
}

// ─── Rendering ────────────────────────────────────────────────────────────────

const ADD_TYPES = [
  { type: 'Currency',          label: 'Currency' },
  { type: 'ItemSpecificAsset', label: 'Item' },
  { type: 'Ticket',            label: 'Ticket' },
]

function _renderAddGroup(prefix: string, dis: string): string {
  const btns = ADD_TYPES
    .map(t => `<button type="button" class="btn btn-ghost btn-sm" data-action="att-add" data-prefix="${prefix}" data-assettype="${t.type}" ${dis}>${t.label}</button>`)
    .join('')
  return `<div class="att-add-group" data-prefix="${prefix}"><span class="att-add-label">+ Add:</span>${btns}</div>`
}

function _renderAll(prefix: string, drafts: AttachmentDraft[], disabled: boolean): string {
  const dis = disabled ? 'disabled' : ''
  const cards = drafts.map((d, i) => _renderCard(prefix, i, d, disabled)).join('')
  return `${cards}${_renderAddGroup(prefix, dis)}`
}

function _renderCard(prefix: string, i: number, d: AttachmentDraft, disabled: boolean): string {
  const dis     = disabled ? 'disabled' : ''
  const cfg     = _getTypeConfig(d.assetType)
  const isJson  = _isJsonObjType(d.assetType)
  const summary = _summaryText(i + 1, d)
  const itemRow = d.itemRows?.[0] ?? _defaultItemRow()

  // Inline warning banners
  const legacyWarn = d._legacyWarning
    ? `<span class="att-legacy-warn" title="${_esc(d._legacyWarning)}">⚠ legacy format</span>`
    : ''
  const unknownWarn = d._unknownIdWarning
    ? `<div class="alert alert-warning" style="margin:6px 0;font-size:12px">⚠ ${_esc(d._unknownIdWarning)}</div>`
    : ''

  // Common fields row (§4.3)
  const typeField = `
<div class="form-group att-type-field">
  <label>Type</label>
  <div id="${prefix}-att-type-wrap-${i}" class="combobox-container"></div>
  <span class="field-error" id="${prefix}-att-err-type-${i}"></span>
</div>`

  const idField = cfg.showIdCombobox ? `
<div class="form-group att-id-field" id="${prefix}-att-plainid-${i}">
  <label>${_esc(cfg.idLabel || 'Asset ID')}</label>
  <div id="${prefix}-att-id-wrap-${i}" class="combobox-container"></div>
  <span class="field-error" id="${prefix}-att-err-id-${i}"></span>
</div>` : `<div class="form-group att-id-field" id="${prefix}-att-plainid-${i}" hidden>
  <label>${_esc(cfg.idLabel || 'Asset ID')}</label>
  <div id="${prefix}-att-id-wrap-${i}" class="combobox-container"></div>
  <span class="field-error" id="${prefix}-att-err-id-${i}"></span>
</div>`

  const amountField = `
<div class="form-group att-amount-field">
  <label>Amount</label>
  <input type="number" id="${prefix}-att-amt-${i}" value="${d.payoutAmount}" min="1" ${dis} />
  <span class="field-error" id="${prefix}-att-err-amt-${i}"></span>
</div>`

  const chanceField = `
<div class="form-group att-chance-field">
  <label>Chance</label>
  <input type="number" id="${prefix}-att-chance-num-${i}" min="0.01" max="1" step="0.01" value="${d.chance.toFixed(2)}" ${dis} />
  <input type="range"  id="${prefix}-att-chance-${i}"     min="0.01" max="1" step="0.01" value="${d.chance}" ${dis} style="width:100%;margin-top:4px" />
  <small style="color:var(--text-dim);font-size:11px">0–1, where 1.00 = 100%</small>
  <span class="field-error" id="${prefix}-att-err-chance-${i}"></span>
</div>`

  // ISA / Ticket sub-section (§4.4, §5)
  const subSection = isJson ? `
<div id="${prefix}-att-item-${i}" class="att-subsection">
  <div class="att-subsection-label">${_esc(cfg.subSectionLabel ?? '')}</div>
  <div class="att-isa-fields">
    <div class="form-group">
      <label>Blueprint ID</label>
      <div id="${prefix}-att-bp-wrap-${i}" class="combobox-container"></div>
      <span class="field-error" id="${prefix}-att-err-bp-${i}"></span>
    </div>
    <div class="form-group" style="min-width:70px;max-width:80px">
      <label>Level</label>
      <input type="number" id="${prefix}-att-cl-${i}" value="${itemRow.CurrentLevel}" min="1" ${dis} />
      <span class="field-error" id="${prefix}-att-err-cl-${i}"></span>
    </div>
    <div class="form-group" style="min-width:90px">
      <label>Rarity</label>
      <select id="${prefix}-att-rar-${i}" ${dis}>${_rarityOpts(itemRow.Rarity)}</select>
    </div>
    <div class="form-group" style="min-width:70px;max-width:90px">
      <label>Initial Level</label>
      <input type="number" id="${prefix}-att-il-${i}" value="${itemRow.InitialLevel}" min="1" ${dis} />
      <span class="field-error" id="${prefix}-att-err-il-${i}"></span>
    </div>
    <div class="form-group" style="min-width:100px">
      <label>Source</label>
      <input type="text" id="${prefix}-att-fs-${i}" value="${_esc(itemRow.FromSource)}" placeholder="source" ${dis} />
    </div>
  </div>
</div>` : `<div id="${prefix}-att-item-${i}" hidden></div>`

  return `
<div class="att-card" id="${prefix}-att-card-${i}">
  <details open class="attachment-row" id="${prefix}-att-row-${i}">
    <summary class="att-card-header">
      <span class="att-card-summary-text">${_esc(summary)}</span>
      <span class="att-card-err-marker" id="${prefix}-att-err-marker-${i}" hidden> ⚠</span>
      <span class="att-card-actions">
        <button type="button" class="btn btn-ghost btn-sm" data-action="att-duplicate" data-prefix="${prefix}" data-idx="${i}" ${dis}>Duplicate</button>
        <button type="button" class="btn btn-ghost btn-sm att-remove-btn" data-action="att-remove" data-prefix="${prefix}" data-idx="${i}" ${dis}>🗑 Delete</button>
      </span>
    </summary>
    <div class="att-card-body">
      ${legacyWarn}
      ${unknownWarn}
      <div class="att-common-row">
        ${typeField}
        ${idField}
        ${amountField}
        ${chanceField}
      </div>
      ${subSection}
    </div>
  </details>
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
        const isJson   = _isJsonObjType(newType)
        const plainEl  = document.getElementById(`${prefix}-att-plainid-${i}`)
        const itemEl   = document.getElementById(`${prefix}-att-item-${i}`)
        if (plainEl) plainEl.hidden = isJson
        if (itemEl)  itemEl.hidden  = !isJson

        // Update label for contextual ID
        const cfg = _getTypeConfig(newType)
        const labelEl = plainEl?.querySelector('label')
        if (labelEl) labelEl.textContent = cfg.idLabel || 'Asset ID'

        // Re-mount ID combobox for new type
        _mountIdCombobox(i, newType, prefix, currencyOptions, itemOptions, ticketOptions, comboboxes, onChange)
        // Re-mount Blueprint combobox if switching to/from JSON type
        _mountBlueprintCombobox(i, newType, prefix, itemOptions, ticketOptions, comboboxes, onChange)
        onChange()
      },
    })
    comboboxes.set(`type-${i}`, typeHandle)
  }

  // ID combobox (non-JSON types)
  _mountIdCombobox(i, d.assetType, prefix, currencyOptions, itemOptions, ticketOptions, comboboxes, onChange)

  // Blueprint combobox (JSON types) — pass draft's initial BlueprintId on first mount
  _mountBlueprintCombobox(i, d.assetType, prefix, itemOptions, ticketOptions, comboboxes, onChange,
    d.itemRows[0]?.BlueprintId ?? '')

  // Chance: numeric ↔ slider sync (§6.2)
  const numEl    = document.getElementById(`${prefix}-att-chance-num-${i}`) as HTMLInputElement | null
  const sliderEl = document.getElementById(`${prefix}-att-chance-${i}`)     as HTMLInputElement | null
  if (numEl && sliderEl) {
    numEl.addEventListener('input', () => {
      const v = parseFloat(numEl.value)
      if (Number.isFinite(v)) sliderEl.value = String(v)
      onChange()
    })
    numEl.addEventListener('blur', () => {
      // Clamp on blur (§6.3)
      const v = parseFloat(numEl.value)
      if (!Number.isFinite(v) || v < 0.01) { numEl.value = '0.01'; sliderEl.value = '0.01' }
      else if (v > 1)                       { numEl.value = '1.00'; sliderEl.value = '1' }
      onChange()
    })
    sliderEl.addEventListener('input', () => {
      numEl.value = parseFloat(sliderEl.value).toFixed(2)
      onChange()
    })
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
  if (_isJsonObjType(assetType)) return

  const wrapId  = `${prefix}-att-id-wrap-${i}`
  const wrapEl  = document.getElementById(wrapId)
  if (!wrapEl) return

  const existing = comboboxes.get(`id-${i}`)
  if (existing) { existing.destroy(); comboboxes.delete(`id-${i}`) }

  const lt   = assetType.toLowerCase()
  const opts = lt === 'currency' ? currencyOptions
    : lt === 'item'   ? itemOptions
    : lt === 'ticket' ? ticketOptions
    : []

  const existingInput = document.getElementById(`${prefix}-att-id-${i}`) as HTMLInputElement | null
  const currentVal    = existingInput?.value ?? ''

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

function _mountBlueprintCombobox(
  i: number,
  assetType: string,
  prefix: string,
  itemOptions:        ComboboxOption[],
  ticketOptions:      ComboboxOption[],
  comboboxes:         Map<string, ComboboxHandle>,
  onChange:           () => void,
  initialBlueprintId?: string,   // provided on first mount; on re-mount reads existing input
): void {
  if (!_isJsonObjType(assetType)) return

  const wrapId = `${prefix}-att-bp-wrap-${i}`
  const wrapEl = document.getElementById(wrapId)
  if (!wrapEl) return

  const existing = comboboxes.get(`bp-${i}`)
  if (existing) { existing.destroy(); comboboxes.delete(`bp-${i}`) }

  const lt   = assetType.toLowerCase()
  const opts = lt === 'ticket' ? ticketOptions : itemOptions

  // On first mount use the draft value; on type-switch re-mount use whatever is in the input
  const existingInput = document.getElementById(`${prefix}-att-bp-${i}`) as HTMLInputElement | null
  const currentVal    = initialBlueprintId !== undefined ? initialBlueprintId : (existingInput?.value ?? '')

  const bpHandle = mountCombobox({
    containerId:  wrapId,
    inputId:      `${prefix}-att-bp-${i}`,
    options:      opts,
    initialValue: currentVal,
    placeholder:  'blueprint_id',
    allowUnknown: true,
    onChange,
  })
  comboboxes.set(`bp-${i}`, bpHandle)
}

// ─── Inline validation (§7) ──────────────────────────────────────────────────

function _setFieldError(id: string, msg: string | null): void {
  const el = document.getElementById(id)
  if (!el) return
  el.textContent = msg ?? ''
}

function _runValidation(prefix: string, i: number, d: AttachmentDraft, comboboxes: Map<string, ComboboxHandle>): boolean {
  const typeVal = comboboxes.get(`type-${i}`)?.getValue() ?? d.assetType
  const isJson  = _isJsonObjType(typeVal)
  const cfg     = _getTypeConfig(typeVal)

  let hasError = false

  // Type
  const typeErr = validateAttachmentType(typeVal)
  _setFieldError(`${prefix}-att-err-type-${i}`, typeErr)
  if (typeErr) hasError = true

  // ID (non-JSON types)
  if (cfg.showIdCombobox) {
    const idVal  = comboboxes.get(`id-${i}`)?.getValue() ?? ''
    const idErr  = validateAttachmentId(idVal, cfg.idLabel || 'Asset ID')
    _setFieldError(`${prefix}-att-err-id-${i}`, idErr)
    if (idErr) hasError = true
  } else {
    _setFieldError(`${prefix}-att-err-id-${i}`, null)
  }

  // Amount
  const amtEl  = document.getElementById(`${prefix}-att-amt-${i}`) as HTMLInputElement | null
  const amount = parseInt(amtEl?.value ?? '1', 10)
  const amtErr = validateAttachmentAmount(isNaN(amount) ? 0 : amount)
  _setFieldError(`${prefix}-att-err-amt-${i}`, amtErr)
  if (amtErr) hasError = true

  // Chance
  const numEl  = document.getElementById(`${prefix}-att-chance-num-${i}`) as HTMLInputElement | null
  const sliderEl = document.getElementById(`${prefix}-att-chance-${i}`) as HTMLInputElement | null
  const rawChance  = parseFloat(numEl?.value ?? sliderEl?.value ?? '')
  const chanceErr  = validateAttachmentChance(isNaN(rawChance) ? 0 : rawChance)
  _setFieldError(`${prefix}-att-err-chance-${i}`, chanceErr)
  if (chanceErr) hasError = true

  // Blueprint / Level for JSON types
  if (isJson) {
    const bpVal = comboboxes.get(`bp-${i}`)?.getValue() ?? ''
    const bpErr = validateBlueprintId(bpVal)
    _setFieldError(`${prefix}-att-err-bp-${i}`, bpErr)
    if (bpErr) hasError = true

    const clEl  = document.getElementById(`${prefix}-att-cl-${i}`) as HTMLInputElement | null
    const ilEl  = document.getElementById(`${prefix}-att-il-${i}`) as HTMLInputElement | null
    const clErr = validateLevel(parseInt(clEl?.value ?? '1', 10), 'Level')
    const ilErr = validateLevel(parseInt(ilEl?.value ?? '1', 10), 'Initial Level')
    _setFieldError(`${prefix}-att-err-cl-${i}`, clErr)
    _setFieldError(`${prefix}-att-err-il-${i}`, ilErr)
    if (clErr || ilErr) hasError = true
  } else {
    _setFieldError(`${prefix}-att-err-bp-${i}`, null)
    _setFieldError(`${prefix}-att-err-cl-${i}`, null)
    _setFieldError(`${prefix}-att-err-il-${i}`, null)
  }

  // Card header marker (§7.3)
  const card   = document.getElementById(`${prefix}-att-card-${i}`)
  const marker = document.getElementById(`${prefix}-att-err-marker-${i}`)
  if (card)   card.classList.toggle('att-card-invalid', hasError)
  if (marker) marker.hidden = !hasError

  return hasError
}

// ─── Read drafts from DOM (§3.4 read contract) ────────────────────────────────

function _readDrafts(
  prefix: string,
  drafts: AttachmentDraft[],
  comboboxes: Map<string, ComboboxHandle>,
): AttachmentDraft[] {
  return drafts.map((d, i) => {
    const typeHandle = comboboxes.get(`type-${i}`)
    const idHandle   = comboboxes.get(`id-${i}`)
    const bpHandle   = comboboxes.get(`bp-${i}`)
    const assetType  = typeHandle?.getValue() ?? d.assetType
    const isJson     = _isJsonObjType(assetType)
    const payoutAssetId = isJson ? '' : (idHandle?.getValue() ?? d.payoutAssetId)

    const amtEl    = document.getElementById(`${prefix}-att-amt-${i}`)         as HTMLInputElement | null
    const numEl    = document.getElementById(`${prefix}-att-chance-num-${i}`)   as HTMLInputElement | null
    const sliderEl = document.getElementById(`${prefix}-att-chance-${i}`)       as HTMLInputElement | null

    const payoutAmount = parseInt(amtEl?.value ?? '1', 10) || d.payoutAmount

    // §6.3 read contract: prefer numeric input when valid, fall back to slider, then stored
    const rawNum    = parseFloat(numEl?.value ?? '')
    const rawSlider = parseFloat(sliderEl?.value ?? '')
    const chance    = (Number.isFinite(rawNum) && rawNum > 0 && rawNum <= 1)
      ? rawNum
      : (Number.isFinite(rawSlider) && rawSlider > 0)
        ? rawSlider
        : d.chance

    let itemRows: ItemSpecificAsset[] = d.itemRows?.length ? d.itemRows : [_defaultItemRow()]
    if (isJson) {
      const bpVal = bpHandle?.getValue() ?? (document.getElementById(`${prefix}-att-bp-${i}`) as HTMLInputElement | null)?.value ?? ''
      const clEl  = document.getElementById(`${prefix}-att-cl-${i}`)  as HTMLInputElement | null
      const rarEl = document.getElementById(`${prefix}-att-rar-${i}`) as HTMLSelectElement | null
      const ilEl  = document.getElementById(`${prefix}-att-il-${i}`)  as HTMLInputElement | null
      const fsEl  = document.getElementById(`${prefix}-att-fs-${i}`)  as HTMLInputElement | null
      const base  = d.itemRows?.[0] ?? _defaultItemRow()
      itemRows = [{
        BlueprintId:  bpVal !== '' ? bpVal : base.BlueprintId,
        CurrentLevel: parseInt(clEl?.value ?? '1', 10)  || base.CurrentLevel,
        Rarity:       (parseInt(rarEl?.value ?? String(base.Rarity), 10) || base.Rarity) as RarityValue,
        InitialLevel: parseInt(ilEl?.value ?? '1', 10)  || base.InitialLevel,
        FromSource:   fsEl?.value ?? base.FromSource,
      }]
    }

    return { payoutAssetId, assetType, payoutAmount, chance, itemRows }
  })
}

// ─── Mount (public entry point) ───────────────────────────────────────────────

export function mountAttachmentEditor(
  container: HTMLElement,
  initialDrafts: AttachmentDraft[],
  opts: AttachmentEditorOptions,
  onChange: (drafts: AttachmentDraft[]) => void,
): AttachmentEditorHandle {
  const { prefix, currencyOptions, itemOptions, ticketOptions } = opts
  let drafts: AttachmentDraft[] = [...initialDrafts]
  const comboboxes = new Map<string, ComboboxHandle>()

  function render(): void {
    comboboxes.forEach(h => h.destroy())
    comboboxes.clear()
    container.innerHTML = _renderAll(prefix, drafts, opts.disabled ?? false)

    drafts.forEach((d, i) => {
      _mountRowComboboxes(i, d, prefix, currencyOptions, itemOptions, ticketOptions, comboboxes, () => {
        _runValidation(prefix, i, _readDrafts(prefix, drafts, comboboxes)[i] ?? d, comboboxes)
        onChange(_readDrafts(prefix, drafts, comboboxes))
      })
      // Run initial validation pass (without blocking)
      _runValidation(prefix, i, d, comboboxes)
    })
  }

  // Delegated click handler (stable across re-renders)
  const clickHandler = (e: Event) => {
    const target    = e.target as HTMLElement
    const addBtn    = target.closest<HTMLElement>('[data-action="att-add"]')
    const remBtn    = target.closest<HTMLElement>('[data-action="att-remove"]')
    const dupBtn    = target.closest<HTMLElement>('[data-action="att-duplicate"]')

    // Stop summary buttons from toggling <details> (§8.1 click propagation)
    if (remBtn || dupBtn) {
      e.stopPropagation()
      e.preventDefault()
    }

    if (addBtn && addBtn.dataset['prefix'] === prefix) {
      const assetType = addBtn.dataset['assettype'] ?? 'Currency'
      drafts = _readDrafts(prefix, drafts, comboboxes)
      drafts.push(_defaultDraft(assetType))
      onChange(drafts)
      render()
    } else if (remBtn && remBtn.dataset['prefix'] === prefix) {
      const idx = parseInt(remBtn.dataset['idx'] ?? '0', 10)
      drafts = _readDrafts(prefix, drafts, comboboxes)
      const target = drafts[idx]
      if (target && _isDraftMeaningful(target)) {
        if (!window.confirm('Delete this attachment? This cannot be undone.')) return
      }
      drafts.splice(idx, 1)
      onChange(drafts)
      render()
    } else if (dupBtn && dupBtn.dataset['prefix'] === prefix) {
      const idx = parseInt(dupBtn.dataset['idx'] ?? '0', 10)
      drafts = _readDrafts(prefix, drafts, comboboxes)
      const src = drafts[idx]
      if (!src) return
      const clone: AttachmentDraft = {
        payoutAssetId: src.payoutAssetId,
        assetType:     src.assetType,
        payoutAmount:  src.payoutAmount,
        chance:        src.chance,
        itemRows:      src.itemRows.map(r => ({ ...r })),
        // _legacyWarning / _unknownIdWarning intentionally NOT copied
      }
      drafts.splice(idx + 1, 0, clone)
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
