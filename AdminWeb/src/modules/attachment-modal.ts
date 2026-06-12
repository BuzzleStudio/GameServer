// src/modules/attachment-modal.ts
// AttachmentEditorModal: type-select view → form view → commit.
// Uses modal-shell.ts (singleton <dialog>).

import { mountModalShell, openConfirmDialog } from './modal-shell'
import { mountCombobox } from './asset-selector'
import type { ComboboxHandle, ComboboxOption } from './asset-selector'
import {
  createDefaultDraft,
  draftToFormState,
  serializeAttachmentForm,
  validateAttachmentForm,
  _isJsonObjType,
  _defaultItemRow,
  RARITY_LABELS,
  Rarity,
} from './attachment-serde'
import type { AttachmentFormState, AttachmentFormErrors } from './attachment-serde'
import type { AttachmentDraft, RarityValue } from '../types'

// ─── Types ────────────────────────────────────────────────────────────────────

export interface AttachmentModalDeps {
  currencyOptions: ComboboxOption[]
  itemOptions:     ComboboxOption[]
  ticketOptions:   ComboboxOption[]
  disabled?:       boolean
  onCommit(draft: AttachmentDraft, mode: 'add' | 'edit' | 'duplicate', sourceUid: string | null): void
}

export interface AttachmentModalHandle {
  openAdd(openerEl?: Element | null):                          void
  openEdit(draft: AttachmentDraft, openerEl?: Element | null): void
  openDuplicate(draft: AttachmentDraft, openerEl?: Element | null): void
  destroy(): void
}

// ─── Internal helpers ─────────────────────────────────────────────────────────

function _esc(s: unknown): string {
  return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
}

function _deepCopyDraft(d: AttachmentDraft): AttachmentDraft {
  return {
    ...d,
    itemRows: d.itemRows.map(r => ({ ...r })),
  }
}

// ─── Type-select view HTML ────────────────────────────────────────────────────

function _typeSelectHtml(): string {
  return `<div class="att-type-select">
  <p class="att-type-select-hint">Choose attachment type</p>
  <div class="att-type-cards">
    <button class="att-type-card" type="button" data-type="Currency">
      <span class="att-type-card-icon">💰</span>
      <span class="att-type-card-label">Currency</span>
    </button>
    <button class="att-type-card" type="button" data-type="ItemSpecificAsset">
      <span class="att-type-card-icon">🎒</span>
      <span class="att-type-card-label">Item</span>
    </button>
    <button class="att-type-card" type="button" data-type="Ticket">
      <span class="att-type-card-icon">🎫</span>
      <span class="att-type-card-label">Ticket</span>
    </button>
  </div>
</div>`
}

function _typeSelectFooterHtml(): string {
  return `<button type="button" class="btn btn-ghost" id="modal-cancel-btn">Cancel</button>`
}

// ─── Form view HTML ───────────────────────────────────────────────────────────

const ASSET_TYPE_OPTIONS: ComboboxOption[] = [
  { id: 'Currency' },
  { id: 'ItemSpecificAsset', label: 'Item' },
  { id: 'Ticket' },
  { id: 'Item', label: 'Item (legacy)' },
]

function _idLabelForType(assetType: string): string {
  const l = assetType.trim().toLowerCase()
  if (l === 'currency') return 'Currency ID'
  if (l === 'item') return 'Payout Asset ID'
  return 'Asset ID'
}

function _rarityOpts(selected: number): string {
  return (Object.entries(RARITY_LABELS) as [string, string][])
    .map(([v, lbl]) => `<option value="${v}" ${Number(v) === selected ? 'selected' : ''}>${lbl}</option>`)
    .join('')
}

function _formHtml(
  form: AttachmentFormState,
  errors: AttachmentFormErrors,
  allowUnknownType: boolean,
): string {
  const isJson = _isJsonObjType(form.assetType)
  const dis    = ''  // fields are always enabled in the modal; the modal itself is disabled-guarded by caller

  // Warnings
  const legacyWarn = form._legacyWarning
    ? `<div class="alert alert-warning att-legacy-warn">⚠ ${_esc(form._legacyWarning)}</div>`
    : ''
  const unknownWarn = form._unknownIdWarning
    ? `<div class="alert alert-warning att-unknown-warn">⚠ ${_esc(form._unknownIdWarning)}</div>`
    : ''

  // Type display label (for combobox initial value)
  const typeDisplayVal = form.assetType

  const idLabel  = _idLabelForType(form.assetType)
  const idHidden = isJson ? 'hidden' : ''

  // ISA/Ticket section
  const isaSectionHtml = isJson ? `
<div class="att-form-section">
  <div class="att-form-section-label">${form.assetType === 'Ticket' ? 'Ticket' : 'Item'} Configuration</div>
  <div class="att-isa-fields">
    <div class="form-group">
      <label for="modal-att-bp">Blueprint ID</label>
      <div id="modal-att-bp-wrap" class="combobox-container"></div>
      <span class="field-error" id="modal-att-err-bp" data-field-error="blueprintId">${_esc(errors.blueprintId ?? '')}</span>
    </div>
    <div class="form-group" style="min-width:70px;max-width:90px">
      <label for="modal-att-cl">Level</label>
      <input type="number" id="modal-att-cl" data-field="currentLevel"
             value="${form.currentLevel}" min="1" ${dis} />
      <span class="field-error" id="modal-att-err-cl" data-field-error="currentLevel">${_esc(errors.currentLevel ?? '')}</span>
    </div>
    <div class="form-group" style="min-width:90px">
      <label for="modal-att-rar">Rarity</label>
      <select id="modal-att-rar" data-field="rarity" ${dis}>${_rarityOpts(form.rarity)}</select>
    </div>
    <div class="form-group" style="min-width:70px;max-width:90px">
      <label for="modal-att-il">Initial Level</label>
      <input type="number" id="modal-att-il" data-field="initialLevel"
             value="${form.initialLevel}" min="1" ${dis} />
      <span class="field-error" id="modal-att-err-il" data-field-error="initialLevel">${_esc(errors.initialLevel ?? '')}</span>
    </div>
    <div class="form-group">
      <label for="modal-att-fs">Source</label>
      <input type="text" id="modal-att-fs" data-field="fromSource"
             value="${_esc(form.fromSource)}" placeholder="source" ${dis} />
    </div>
  </div>
</div>` : ''

  return `${legacyWarn}${unknownWarn}
<div class="att-form-section">
  <div class="att-form-section-label">General</div>

  <div class="form-group att-type-field">
    <label for="modal-att-type">Type</label>
    <div id="modal-att-type-wrap" class="combobox-container" aria-describedby="modal-att-err-type"></div>
    <span class="field-error" id="modal-att-err-type" data-field-error="assetType">${_esc(errors.assetType ?? '')}</span>
  </div>

  <div class="form-group att-id-field" id="modal-att-id-group" ${idHidden}>
    <label for="modal-att-id">${_esc(idLabel)}</label>
    <div id="modal-att-id-wrap" class="combobox-container" aria-describedby="modal-att-err-id"></div>
    <span class="field-error" id="modal-att-err-id" data-field-error="payoutAssetId">${_esc(errors.payoutAssetId ?? '')}</span>
  </div>

  <div class="att-common-row">
    <div class="form-group att-amount-field">
      <label for="modal-att-amt">Amount</label>
      <input type="number" id="modal-att-amt" data-field="payoutAmount"
             value="${form.payoutAmount}" min="1" ${dis} />
      <span class="field-error" id="modal-att-err-amt" data-field-error="payoutAmount">${_esc(errors.payoutAmount ?? '')}</span>
    </div>
    <div class="form-group att-chance-field">
      <label for="modal-att-chance-num">Chance</label>
      <input type="number" id="modal-att-chance-num" data-field="chance"
             min="0.01" max="1" step="0.01" value="${form.chance.toFixed(2)}" ${dis} />
      <input type="range" id="modal-att-chance-slider"
             min="0.01" max="1" step="0.01" value="${form.chance}" ${dis}
             style="width:100%;margin-top:4px" aria-label="Chance slider (0.01–1.00)" />
      <small style="color:var(--text-dim);font-size:11px">0 to 1, 0.5 = 50%</small>
      <span class="field-error" id="modal-att-err-chance" data-field-error="chance">${_esc(errors.chance ?? '')}</span>
    </div>
  </div>
</div>
${isaSectionHtml}`
}

function _formFooterHtml(mode: 'add' | 'edit' | 'duplicate', isValid: boolean): string {
  const primaryLabel = mode === 'edit' ? 'Save Changes' : 'Add Attachment'
  const dis          = isValid ? '' : 'disabled'
  return `
<button type="button" class="btn btn-ghost" id="modal-cancel-btn">Cancel</button>
<button type="button" class="btn btn-primary" id="modal-primary-btn" ${dis}
        aria-disabled="${!isValid}">${_esc(primaryLabel)}</button>`
}

// ─── Header title ─────────────────────────────────────────────────────────────

function _headerTitle(mode: 'add' | 'edit' | 'duplicate'): string {
  if (mode === 'edit')      return 'Edit Attachment'
  if (mode === 'duplicate') return 'Add Attachment (copy)'
  return 'Add Attachment'
}

// ─── Mount ────────────────────────────────────────────────────────────────────

export function mountAttachmentModal(deps: AttachmentModalDeps): AttachmentModalHandle {
  const shell = mountModalShell()

  let _mode:      'add' | 'edit' | 'duplicate' = 'add'
  let _sourceUid: string | null = null
  let _formState: AttachmentFormState = draftToFormState(createDefaultDraft())
  let _initialFormState: AttachmentFormState = { ..._formState }
  let _errors: AttachmentFormErrors = { isValid: false }
  let _view: 'type-select' | 'form' = 'type-select'

  const _comboboxes = new Map<string, ComboboxHandle>()

  // ── Dirty guard ─────────────────────────────────────────────────────────────
  shell.setDirtyGuard(() => _isDirty())

  function _isDirty(): boolean {
    return JSON.stringify(_formState) !== JSON.stringify(_initialFormState)
  }

  // ── Combobox lifecycle ───────────────────────────────────────────────────────
  function _destroyComboboxes(): void {
    _comboboxes.forEach(h => h.destroy())
    _comboboxes.clear()
  }

  // ── Render type-select view ──────────────────────────────────────────────────
  function _renderTypeSelect(): void {
    shell.setContent(
      _headerTitle('add'),
      _typeSelectHtml(),
      _typeSelectFooterHtml(),
    )

    const body = document.getElementById('modal-body')
    body?.querySelector<HTMLElement>('.att-type-card')?.focus()

    body?.addEventListener('click', (e) => {
      const card = (e.target as HTMLElement).closest<HTMLElement>('[data-type]')
      if (!card) return
      const assetType = card.dataset['type'] ?? 'Currency'
      _formState = draftToFormState(createDefaultDraft(assetType))
      _initialFormState = { ..._formState }
      _errors = validateAttachmentForm(_formState)
      _view = 'form'
      _renderForm()
    })

    document.getElementById('modal-cancel-btn')?.addEventListener('click', () => {
      shell.close()
    })
  }

  // ── Render form view ─────────────────────────────────────────────────────────
  function _renderForm(): void {
    _destroyComboboxes()

    const allowUnknownType = _mode !== 'add'  // in edit/dup: allow unknown legacy types

    shell.setContent(
      _headerTitle(_mode),
      _formHtml(_formState, _errors, allowUnknownType),
      _formFooterHtml(_mode, _errors.isValid),
    )

    // ── Mount type combobox ──────────────────────────────────────────────────
    const typeWrap = document.getElementById('modal-att-type-wrap')
    if (typeWrap) {
      const typeHandle = mountCombobox({
        containerId:  'modal-att-type-wrap',
        inputId:      'modal-att-type',
        options:      ASSET_TYPE_OPTIONS,
        initialValue: _formState.assetType,
        placeholder:  'Currency, Item, Ticket…',
        allowUnknown: allowUnknownType,
        onChange: (newType) => _handleTypeChange(newType),
      })
      _comboboxes.set('type', typeHandle)
    }

    // ── Mount ID combobox (non-JSON types) ───────────────────────────────────
    _mountIdCombobox()

    // ── Mount blueprint combobox (JSON types) ────────────────────────────────
    _mountBlueprintCombobox()

    // ── Chance sync ──────────────────────────────────────────────────────────
    const numEl    = document.getElementById('modal-att-chance-num')    as HTMLInputElement | null
    const sliderEl = document.getElementById('modal-att-chance-slider') as HTMLInputElement | null
    if (numEl && sliderEl) {
      numEl.addEventListener('input', () => {
        const v = parseFloat(numEl.value)
        if (Number.isFinite(v)) sliderEl.value = String(v)
        _formState.chance = Number.isFinite(v) ? v : _formState.chance
        _revalidate()
      })
      numEl.addEventListener('blur', () => {
        let v = parseFloat(numEl.value)
        if (!Number.isFinite(v) || v < 0.01) v = 0.01
        else if (v > 1) v = 1
        numEl.value = v.toFixed(2)
        sliderEl.value = String(v)
        _formState.chance = v
        _revalidate()
      })
      sliderEl.addEventListener('input', () => {
        const v = parseFloat(sliderEl.value)
        numEl.value = v.toFixed(2)
        _formState.chance = v
        _revalidate()
      })
    }

    // ── Amount ────────────────────────────────────────────────────────────────
    document.getElementById('modal-att-amt')?.addEventListener('input', (e) => {
      _formState.payoutAmount = parseInt((e.target as HTMLInputElement).value, 10) || 0
      _revalidate()
    })

    // ── ISA/Ticket fields ─────────────────────────────────────────────────────
    if (_isJsonObjType(_formState.assetType)) {
      document.getElementById('modal-att-cl')?.addEventListener('input', (e) => {
        _formState.currentLevel = parseInt((e.target as HTMLInputElement).value, 10) || 0
        _revalidate()
      })
      document.getElementById('modal-att-il')?.addEventListener('input', (e) => {
        _formState.initialLevel = parseInt((e.target as HTMLInputElement).value, 10) || 0
        _revalidate()
      })
      document.getElementById('modal-att-rar')?.addEventListener('change', (e) => {
        _formState.rarity = parseInt((e.target as HTMLSelectElement).value, 10) as RarityValue
        _revalidate()
      })
      document.getElementById('modal-att-fs')?.addEventListener('input', (e) => {
        _formState.fromSource = (e.target as HTMLInputElement).value
      })
    }

    // ── Cancel / primary buttons ─────────────────────────────────────────────
    document.getElementById('modal-cancel-btn')?.addEventListener('click', () => {
      shell.close()
    })
    document.getElementById('modal-primary-btn')?.addEventListener('click', () => {
      _handleSubmit()
    })

    // ── Scroll → close comboboxes ────────────────────────────────────────────
    const modalBody = document.getElementById('modal-body')
    modalBody?.addEventListener('scroll', () => {
      _comboboxes.forEach(h => h.close())
    }, { passive: true })
  }

  // ── Mount ID combobox ────────────────────────────────────────────────────────
  function _mountIdCombobox(initialVal?: string): void {
    if (_isJsonObjType(_formState.assetType)) return
    const wrapId = 'modal-att-id-wrap'
    const wrapEl = document.getElementById(wrapId)
    if (!wrapEl) return

    const existing = _comboboxes.get('id')
    if (existing) { existing.destroy(); _comboboxes.delete('id') }

    const lt   = _formState.assetType.trim().toLowerCase()
    const opts = lt === 'currency' ? deps.currencyOptions
      : lt === 'item' ? deps.itemOptions
      : []

    const val = initialVal !== undefined ? initialVal : _formState.payoutAssetId

    const idHandle = mountCombobox({
      containerId:  wrapId,
      inputId:      'modal-att-id',
      options:      opts,
      initialValue: val,
      placeholder:  'asset ID',
      allowUnknown: true,
      onChange: (v) => {
        _formState.payoutAssetId = v
        _revalidate()
      },
    })
    _comboboxes.set('id', idHandle)
  }

  // ── Mount blueprint combobox ─────────────────────────────────────────────────
  function _mountBlueprintCombobox(initialVal?: string): void {
    if (!_isJsonObjType(_formState.assetType)) return
    const wrapId = 'modal-att-bp-wrap'
    const wrapEl = document.getElementById(wrapId)
    if (!wrapEl) return

    const existing = _comboboxes.get('bp')
    if (existing) { existing.destroy(); _comboboxes.delete('bp') }

    const lt   = _formState.assetType.trim().toLowerCase()
    const opts = lt === 'ticket' ? deps.ticketOptions : deps.itemOptions

    const val = initialVal !== undefined ? initialVal : _formState.blueprintId

    const bpHandle = mountCombobox({
      containerId:  wrapId,
      inputId:      'modal-att-bp',
      options:      opts,
      initialValue: val,
      placeholder:  'blueprint_id',
      allowUnknown: true,
      onChange: (v) => {
        _formState.blueprintId = v
        _revalidate()
      },
    })
    _comboboxes.set('bp', bpHandle)
  }

  // ── Handle type change ────────────────────────────────────────────────────────
  function _handleTypeChange(newType: string): void {
    const prevType = _formState.assetType
    if (newType === prevType) return

    // Check if there's meaningful data to lose
    const hasMeaningfulData =
      _formState.payoutAssetId.trim() !== '' ||
      _formState.blueprintId.trim()   !== '' ||
      _formState.payoutAmount !== 1   ||
      _formState.chance !== 1

    if (!hasMeaningfulData) {
      _applyTypeChange(newType)
      return
    }

    openConfirmDialog({
      title:        'Change Type?',
      message:      'Changing the attachment type will reset the asset ID and type-specific fields.',
      confirmLabel: 'Change Type',
    }).then(confirmed => {
      if (!confirmed) {
        // Revert the combobox
        _comboboxes.get('type')?.setValue(prevType)
        return
      }
      _applyTypeChange(newType)
    })
  }

  function _applyTypeChange(newType: string): void {
    const prevAmount = _formState.payoutAmount
    const prevChance = _formState.chance
    const row = _defaultItemRow()
    _formState = {
      ..._formState,
      assetType:     newType,
      payoutAssetId: '',
      blueprintId:   row.BlueprintId,
      currentLevel:  row.CurrentLevel,
      rarity:        row.Rarity,
      initialLevel:  row.InitialLevel,
      fromSource:    row.FromSource,
      payoutAmount:  prevAmount,
      chance:        prevChance,
      _legacyWarning:    undefined,
      _unknownIdWarning: undefined,
    }
    _errors = validateAttachmentForm(_formState)
    _renderForm()
  }

  // ── Revalidate ────────────────────────────────────────────────────────────────
  function _revalidate(): void {
    _errors = validateAttachmentForm(_formState)
    _updateInlineErrors()
    _updatePrimaryButton()
  }

  function _updateInlineErrors(): void {
    const fields: Array<[string, keyof typeof _errors]> = [
      ['modal-att-err-type',   'assetType'],
      ['modal-att-err-id',     'payoutAssetId'],
      ['modal-att-err-amt',    'payoutAmount'],
      ['modal-att-err-chance', 'chance'],
      ['modal-att-err-bp',     'blueprintId'],
      ['modal-att-err-cl',     'currentLevel'],
      ['modal-att-err-il',     'initialLevel'],
    ]
    for (const [elId, key] of fields) {
      const el = document.getElementById(elId)
      if (el) el.textContent = (_errors[key] as string | null | undefined) ?? ''
    }
  }

  function _updatePrimaryButton(): void {
    const btn = document.getElementById('modal-primary-btn') as HTMLButtonElement | null
    if (!btn) return
    btn.disabled = !_errors.isValid
    btn.setAttribute('aria-disabled', String(!_errors.isValid))
  }

  // ── Submit ────────────────────────────────────────────────────────────────────
  function _handleSubmit(): void {
    // Read latest combobox values into formState
    const typeVal = _comboboxes.get('type')?.getValue()
    if (typeVal !== undefined) _formState.assetType = typeVal

    const idVal = _comboboxes.get('id')?.getValue()
    if (idVal !== undefined) _formState.payoutAssetId = idVal

    const bpVal = _comboboxes.get('bp')?.getValue()
    if (bpVal !== undefined) _formState.blueprintId = bpVal

    _errors = validateAttachmentForm(_formState)
    if (!_errors.isValid) {
      _updateInlineErrors()
      _updatePrimaryButton()
      _focusFirstInvalidField()
      return
    }

    const draft = serializeAttachmentForm(_formState)
    _destroyComboboxes()
    shell.forceClose()
    deps.onCommit(draft, _mode, _sourceUid)
  }

  // ── Focus first invalid field ─────────────────────────────────────────────────
  function _focusFirstInvalidField(): void {
    const fieldOrder: Array<keyof AttachmentFormErrors> = [
      'assetType', 'payoutAssetId', 'blueprintId', 'payoutAmount', 'chance', 'currentLevel', 'initialLevel',
    ]
    for (const field of fieldOrder) {
      if (_errors[field]) {
        // Try data-field attribute on inputs or combobox inputs
        const el = document.querySelector<HTMLElement>(`[data-field="${field}"], #modal-att-type, #modal-att-id, #modal-att-bp`)
        if (el) { el.scrollIntoView({ block: 'nearest' }); el.focus(); break }
      }
    }
  }

  // ── Public API ────────────────────────────────────────────────────────────────

  function _openShell(openerEl?: Element | null): void {
    _destroyComboboxes()
    if (_view === 'type-select') {
      _renderTypeSelect()
    } else {
      _renderForm()
    }
    shell.open(openerEl)
  }

  return {
    openAdd(openerEl?: Element | null): void {
      _mode      = 'add'
      _sourceUid = null
      _view      = 'type-select'
      _formState = draftToFormState(createDefaultDraft())
      _initialFormState = { ..._formState }
      _errors    = validateAttachmentForm(_formState)
      _openShell(openerEl)
    },

    openEdit(draft: AttachmentDraft, openerEl?: Element | null): void {
      _mode      = 'edit'
      _sourceUid = draft._id ?? null
      _view      = 'form'
      const copy = _deepCopyDraft(draft)
      _formState = draftToFormState(copy)
      _initialFormState = { ..._formState }
      _errors    = validateAttachmentForm(_formState)
      _openShell(openerEl)
      // Focus first invalid field if already invalid on open
      if (!_errors.isValid) {
        requestAnimationFrame(() => _focusFirstInvalidField())
      }
    },

    openDuplicate(draft: AttachmentDraft, openerEl?: Element | null): void {
      _mode      = 'duplicate'
      _sourceUid = null  // duplicate creates a new item
      _view      = 'form'
      const copy    = _deepCopyDraft(draft)
      copy._id      = crypto.randomUUID()  // new identity
      _formState    = draftToFormState(copy)
      _initialFormState = { ..._formState }
      _errors       = validateAttachmentForm(_formState)
      _openShell(openerEl)
    },

    destroy(): void {
      _destroyComboboxes()
      shell.destroy()
    },
  }
}
