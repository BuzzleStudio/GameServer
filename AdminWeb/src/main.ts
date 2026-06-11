// ─── Admin Mail SPA — main.ts (proxy model) ──────────────────────────────────────
// Mirrors the Unity Editor AdminMailWindow for browser use.
// All Cloud Code calls route through a serverless proxy — no UGS Key/Secret in browser.
import './style.css'

import {
  saveCredentials,
  loadCredentials,
  clearAllCredentials,
  effectiveProxyBase,
  ApiError,
  apiSendGlobalMail,
  apiGetGlobalMails,
  apiGetUserMailsAdmin,
  apiSetMailEndTime,
  apiUpdateGlobalMail,
  apiExpireMail,
  apiDeleteGlobalMail,
  apiPurgeExpired,
} from './api'

import type { ProxyCallArgs } from './api'

import {
  CATEGORY_OPTIONS,
  Rarity,
  RARITY_LABELS,
  mailId,
  mailTitle,
  mailContent,
  mailStartTime,
  mailEndTime,
  mailTargetUsers,
  mailAttachments,
} from './types'

import type {
  AttachmentDraft,
  ItemSpecificAsset,
  MailRecord,
  MailAttachment,
  MailScope,
  RarityValue,
} from './types'

import { CURRENCY_IDS, ITEM_IDS, TICKET_IDS, CURRENCY_OPTIONS } from './generated/lookup-data'
import { buildMailExportJson } from './mail-export'
import { validateAndImport } from './mail-import'
import type { ImportedDraft } from './mail-import'
import {
  defaultConnectionState,
  defaultSendFormState,
  defaultManageTabState,
} from './state'
import type {
  ConnectionState,
  SendFormState,
  ManageTabState,
} from './state'
import { mountManageTab } from './modules/mail-list'
import type { ManageTabHandle } from './modules/mail-list'
import type { ComboboxOption } from './modules/asset-selector'

// ─── App state ────────────────────────────────────────────────────────────────────
interface AppState {
  // Connection fields (sessionStorage for non-secrets)
  projectId:   string
  environment: string   // name ("production"/"testing") or UUID — proxy resolves
  moduleName:  string   // default "BackpackAdventuresModule"
  operatorId:  string
  // In-memory only — NEVER stored anywhere
  proxyToken:  string
  // Tabs
  activeTab:        'send' | 'manage'
  activeSendSubTab: 'global' | 'targeted'
  // Send Global form
  globalSubject:     string
  globalBody:        string
  globalUseEndTime:  boolean
  globalEndDate:     string
  globalEndTime:     string
  globalCategory:    number
  globalSenderName:  string
  globalDedupKey:    string
  globalAttachments: AttachmentDraft[]
  // Send Targeted form
  targetUserIds:    string[]
  userSubject:      string
  userBody:         string
  userUseEndTime:   boolean
  userEndDate:      string
  userEndTime:      string
  userCategory:     number
  userSenderName:   string
  userDedupKey:     string
  userAttachments:  AttachmentDraft[]
  // Manage form (direct operations)
  manageMailId:     string
  manageUseEndTime: boolean
  manageEndDate:    string
  manageEndTime:    string
  // Mail list (global)
  mailList:       MailRecord[] | null
  mailPage:       number
  mailTotalCount: number
  globalMailError: string
  // Inline end-time editing per mail row (by MessageId)
  inlineEndDate: Record<string, string>
  inlineEndTime: Record<string, string>
  inlineAttachments: Record<string, AttachmentDraft[]>
  inlineTargetUserIds: Record<string, string[]>
  // User Mail Lookup
  userMailLookupPlayerId: string
  userMailList:  MailRecord[] | null
  userMailError: string
  // Import / paste panels (Send Global + Send Targeted tabs)
  globalImportExpanded: boolean
  globalImportText:     string
  globalImportErrors:   string[]
  globalImportWarnings: string[]
  userImportExpanded:   boolean
  userImportText:       string
  userImportErrors:     string[]
  userImportWarnings:   string[]
  // Status
  busy:        boolean
  statusMsg:   string
  statusType:  'success' | 'error' | 'warning' | 'info' | ''
  rawJson:     string
  // ── Step 1: new typed sub-state objects (canonical data in Steps 3–4) ──────────
  connection:  ConnectionState
  sendForm:    SendFormState
  manageTab:   ManageTabState
}

const defaultItemRow = (): ItemSpecificAsset => ({
  BlueprintId: '',
  CurrentLevel: 1,
  Rarity: Rarity.Common,
  InitialLevel: 1,
  FromSource: '',
})

const defaultAttachment = (): AttachmentDraft => ({
  payoutAssetId: '',
  assetType: 'Currency',
  payoutAmount: 1,
  chance: 1,
  itemRows: [defaultItemRow()],
})

const MAILS_PER_PAGE = 5
const FETCH_PAGE_SIZE = 50
const MAX_FETCH_PAGES = 20

// ── Step 3: active manage-tab handle (drawer persists across refreshMainPanel) ──
let _manageTabHandle: ManageTabHandle | null = null

// ── Step 3: combobox options from generated lookup data ───────────────────────
const CURRENCY_COMBOBOX_OPTIONS: ComboboxOption[] = CURRENCY_OPTIONS.map(o => ({
  id:    o.id,
  label: `${o.name} — ${o.id}`,
}))
const ITEM_COMBOBOX_OPTIONS: ComboboxOption[]     = ITEM_IDS.map(id => ({ id }))
const TICKET_COMBOBOX_OPTIONS: ComboboxOption[]   = TICKET_IDS.map(id => ({ id }))

const state: AppState = {
  projectId:   '',
  environment: '',
  moduleName:  'BackpackAdventuresModule',
  operatorId:  '',
  proxyToken:  '',
  activeTab:        'send',
  activeSendSubTab: 'global',
  globalSubject:    '',
  globalBody:       '',
  globalUseEndTime: false,
  globalEndDate:    '',
  globalEndTime:    '',
  globalCategory:   0,
  globalSenderName: '',
  globalDedupKey:   '',
  globalAttachments: [defaultAttachment()],
  targetUserIds:  [''],
  userSubject:    '',
  userBody:       '',
  userUseEndTime: false,
  userEndDate:    '',
  userEndTime:    '',
  userCategory:   0,
  userSenderName: '',
  userDedupKey:   '',
  userAttachments: [defaultAttachment()],
  manageMailId:     '',
  manageUseEndTime: false,
  manageEndDate:    '',
  manageEndTime:    '',
  mailList:       null,
  mailPage:       0,
  mailTotalCount: 0,
  globalMailError: '',
  inlineEndDate:  {},
  inlineEndTime:  {},
  inlineAttachments: {},
  inlineTargetUserIds: {},
  userMailLookupPlayerId: '',
  userMailList:  null,
  userMailError: '',
  globalImportExpanded: false,
  globalImportText:     '',
  globalImportErrors:   [],
  globalImportWarnings: [],
  userImportExpanded:   false,
  userImportText:       '',
  userImportErrors:     [],
  userImportWarnings:   [],
  busy:       false,
  statusMsg:  '',
  statusType: '',
  rawJson:    '',
  // Step 1: new sub-state objects (old flat fields above are compatibility shims deleted in Step 4)
  connection:  defaultConnectionState(),
  sendForm:    defaultSendFormState(),
  manageTab:   defaultManageTabState(),
}

// ─── Utility helpers ──────────────────────────────────────────────────────────────
function esc(s: unknown): string {
  return String(s ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

function el<T extends HTMLElement>(id: string): T {
  const e = document.getElementById(id)
  if (!e) throw new Error(`Element #${id} not found`)
  return e as T
}

function val(id: string): string {
  return (document.getElementById(id) as HTMLInputElement | null)?.value ?? ''
}

function buildEndTimeIso(useEndTime: boolean, endDate: string, endTime: string): string | null {
  if (!useEndTime) return null
  const raw = `${endDate.trim()}T${endTime.trim()}:00Z`
  const d = new Date(raw)
  if (isNaN(d.getTime())) throw new Error('End Time: invalid date/time. Use yyyy-MM-dd and HH:mm.')
  return d.toISOString()
}

function presetEndTime(days: number): { date: string; time: string } {
  const d = new Date(Date.now() + days * 86400000)
  const pad = (n: number) => String(n).padStart(2, '0')
  return {
    date: `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`,
    time: `${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}`,
  }
}

function setEndTimePreset(days: number, dateId: string, timeId: string) {
  const p = presetEndTime(days)
  ;(el<HTMLInputElement>(dateId)).value = p.date
  ;(el<HTMLInputElement>(timeId)).value = p.time
}

const ASSET_TYPE_OPTIONS = ['Currency', 'Item', 'Ticket', 'ItemSpecificAsset'] as const

function isItemSpecificAssetType(assetType: string): boolean {
  return assetType.trim().toLowerCase() === 'itemspecificasset'
}

function isTicketType(assetType: string): boolean {
  return assetType.trim().toLowerCase() === 'ticket'
}

/** Returns true for types that use a JSON object as PayoutAssetId (not a plain string). */
function isJsonObjectType(assetType: string): boolean {
  return isItemSpecificAssetType(assetType) || isTicketType(assetType)
}

function buildAttachments(drafts: AttachmentDraft[]): MailAttachment[] | null {
  const result: MailAttachment[] = []
  for (const d of drafts) {
    const isJsonObj = isJsonObjectType(d.assetType)
    if (!isJsonObj && !d.payoutAssetId.trim()) continue
    if (d.payoutAmount <= 0) throw new Error(`Attachment: PayoutAmount must be > 0`)
    if (d.chance <= 0) throw new Error(`Attachment: Chance must be > 0`)

    let payoutAssetId: string
    if (isJsonObj) {
      const row = d.itemRows?.[0] ?? defaultItemRow()
      payoutAssetId = JSON.stringify({
        BlueprintId: row.BlueprintId,
        CurrentLevel: row.CurrentLevel,
        Rarity: row.Rarity,
        InitialLevel: row.InitialLevel,
        FromSource: row.FromSource,
      })
    } else {
      payoutAssetId = d.payoutAssetId.trim()
    }

    const typeStr = d.assetType.trim() || 'Currency'

    result.push({
      type:     typeStr,
      id:       payoutAssetId,
      itemId:   payoutAssetId,
      amount:   d.payoutAmount,
      quantity: d.payoutAmount,
      chance:   d.chance,
    })
  }
  return result.length > 0 ? result : null
}

function parseItemRow(payoutAssetId: string): ItemSpecificAsset {
  try {
    const parsed = JSON.parse(payoutAssetId)
    // Support both single object {} and legacy array [{...}]
    const r: Record<string, unknown> = Array.isArray(parsed) ? (parsed[0] ?? {}) : (parsed ?? {})
    return {
      BlueprintId: typeof r['BlueprintId'] === 'string' ? r['BlueprintId'] : '',
      CurrentLevel: typeof r['CurrentLevel'] === 'number' ? r['CurrentLevel'] : 1,
      Rarity: (typeof r['Rarity'] === 'number' ? r['Rarity'] : Rarity.Common) as RarityValue,
      InitialLevel: typeof r['InitialLevel'] === 'number' ? r['InitialLevel'] : 1,
      FromSource: typeof r['FromSource'] === 'string' ? r['FromSource'] : '',
    }
  } catch {
    return defaultItemRow()
  }
}

function attachmentInfoToDraft(att: ReturnType<typeof mailAttachments>[number]): AttachmentDraft {
  const assetType     = att.AssetType ?? att.assetType ?? 'Currency'
  const payoutAssetId = att.PayoutAssetId ?? att.payoutAssetId ?? ''
  const isISA    = isItemSpecificAssetType(assetType)
  const isTicket = isTicketType(assetType)

  if (isISA || isTicket) {
    // Attempt JSON parse; if it fails, show legacy marker for Ticket
    try {
      JSON.parse(payoutAssetId)  // validate before passing to parseItemRow
      return {
        payoutAssetId: '',
        assetType,
        payoutAmount: att.PayoutAmount ?? att.payoutAmount ?? 1,
        chance: att.Chance ?? att.chance ?? 1,
        itemRows: [parseItemRow(payoutAssetId)],
      }
    } catch {
      // Legacy plain-string Ticket: keep BlueprintId as the plain value
      const legacyWarning = isTicket
        ? `⚠ legacy format: plain-string PayoutAssetId "${payoutAssetId}"`
        : undefined
      return {
        payoutAssetId: '',
        assetType,
        payoutAmount: att.PayoutAmount ?? att.payoutAmount ?? 1,
        chance: att.Chance ?? att.chance ?? 1,
        itemRows: [{ ...defaultItemRow(), BlueprintId: payoutAssetId }],
        _legacyWarning: legacyWarning,
      }
    }
  }

  return {
    payoutAssetId,
    assetType,
    payoutAmount: att.PayoutAmount ?? att.payoutAmount ?? 1,
    chance: att.Chance ?? att.chance ?? 1,
    itemRows: [defaultItemRow()],
  }
}

function resolveProxyArgs(): ProxyCallArgs {
  const pb = effectiveProxyBase()
  if (!state.proxyToken) {
    throw new Error('Proxy access token is required. Enter it in the connection form.')
  }
  return {
    proxyBase:   pb,
    proxyToken:  state.proxyToken,
    projectId:   state.projectId,
    environment: state.environment,
    moduleName:  state.moduleName || 'BackpackAdventuresModule',
  }
}

function isConnected(): boolean {
  return (
    !!state.projectId &&
    !!state.environment &&
    !!state.moduleName &&
    !!state.operatorId &&
    !!state.proxyToken
  )
}

// ─── Status bar ───────────────────────────────────────────────────────────────────
function setStatus(msg: string, type: AppState['statusType'], raw = '') {
  state.statusMsg  = msg
  state.statusType = type
  state.rawJson    = raw
  renderStatus()
}

function renderStatus() {
  const bar = document.getElementById('status-bar')
  if (!bar) return
  let alertHtml = ''
  if (state.statusMsg) {
    const cls = state.statusType ? `alert-${state.statusType}` : 'alert-info'
    alertHtml = `<div class="alert ${cls}">${esc(state.statusMsg)}</div>`
  }
  const rawHtml = state.rawJson
    ? `<details><summary>Server response JSON</summary><div class="json-box">${esc(state.rawJson)}</div></details>`
    : ''
  bar.innerHTML = alertHtml + rawHtml
}

// ─── Connection status dot ────────────────────────────────────────────────────────
function renderConnectionDot() {
  const dot = document.getElementById('conn-dot')
  if (!dot) return
  if (isConnected()) {
    dot.className = 'status-dot ready'
    dot.title = `Connected — ${state.moduleName} / ${state.environment}`
  } else if (state.proxyToken || state.projectId) {
    dot.className = 'status-dot partial'
    dot.title = 'Partial credentials'
  } else {
    dot.className = 'status-dot'
    dot.title = 'Not connected'
  }
}

// ─── Rarity select HTML helper ────────────────────────────────────────────────────
function raritySelectHtml(id: string, value: RarityValue): string {
  return `<select id="${id}">${(Object.entries(RARITY_LABELS) as [string, string][]).map(
    ([v, label]) => `<option value="${v}" ${Number(v) === value ? 'selected' : ''}>${label}</option>`
  ).join('')}</select>`
}

// ─── Single item row editor HTML (ItemSpecificAsset / Ticket = one object, not a list) ─
function itemRowHtml(prefix: string, attIdx: number, r: ItemSpecificAsset, bpDatalist: string): string {
  return `
    <table class="item-rows-compact">
      <thead><tr><th>BlueprintId</th><th>Lvl</th><th>Rarity</th><th>Init</th><th>Source</th></tr></thead>
      <tbody><tr>
        <td><input type="text" id="${prefix}-att-${attIdx}-row-bp-0" value="${esc(r.BlueprintId)}" placeholder="blueprint_id" list="${bpDatalist}" /></td>
        <td><input type="number" id="${prefix}-att-${attIdx}-row-cl-0" value="${r.CurrentLevel}" min="1" /></td>
        <td>${raritySelectHtml(`${prefix}-att-${attIdx}-row-rar-0`, r.Rarity)}</td>
        <td><input type="number" id="${prefix}-att-${attIdx}-row-il-0" value="${r.InitialLevel}" min="1" /></td>
        <td><input type="text" id="${prefix}-att-${attIdx}-row-fs-0" value="${esc(r.FromSource)}" placeholder="source" /></td>
      </tr></tbody>
    </table>`
}

// ─── Attachment editor ────────────────────────────────────────────────────────────
function renderAttachmentList(listId: string, prefix: string, drafts: AttachmentDraft[]) {
  const container = document.getElementById(listId)
  if (!container) return
  if (drafts.length === 0) {
    container.innerHTML = '<div class="empty" style="padding:12px">No attachments. Click Add.</div>'
    return
  }
  container.innerHTML = drafts.map((d, i) => {
    const isISA    = isItemSpecificAssetType(d.assetType)
    const isTicket = isTicketType(d.assetType)
    const isJsonObj = isISA || isTicket
    const rows = d.itemRows ?? [defaultItemRow()]

    // Datalist for PayoutAssetId plain-string input
    const plainDatalist = isTicketType(d.assetType)
      ? '' // Ticket uses JSON object — no plain datalist
      : d.assetType.toLowerCase() === 'currency' ? 'list="currency-ids-list"'
      : d.assetType.toLowerCase() === 'item' ? 'list="item-ids-list"'
      : ''

    // Datalist for BlueprintId in JSON-object editor
    const bpDatalist = isTicket ? 'ticket-ids-list' : 'item-ids-list'

    const legacyBadge = d._legacyWarning
      ? `<span style="color:orange;font-size:11px;margin-left:4px" title="${esc(d._legacyWarning)}">⚠ legacy format</span>`
      : ''
    const unknownBadge = d._unknownIdWarning
      ? `<span style="color:orange;font-size:11px;margin-left:4px" title="${esc(d._unknownIdWarning)}">${esc(d._unknownIdWarning)}</span>`
      : ''

    return `
    <div class="attachment-row" id="${prefix}-att-${i}" style="display:block">
      <div style="display:flex;gap:8px;align-items:end;flex-wrap:wrap;margin-bottom:8px">
        <div class="form-group" style="flex:1;min-width:140px;margin-bottom:0">
          <label>AssetType</label>
          <input type="text" id="${prefix}-att-type-${i}" class="att-type-input asset-type-input" list="asset-type-list"
            value="${esc(d.assetType)}" placeholder="Currency, Item, Ticket, ItemSpecificAsset, or custom..." data-prefix="${prefix}" data-idx="${i}" />
        </div>
        <div class="form-group" style="width:100px;margin-bottom:0">
          <label>Amount</label>
          <input type="number" id="${prefix}-att-amt-${i}" value="${d.payoutAmount}" min="1" />
        </div>
        <div class="form-group" style="width:120px;margin-bottom:0">
          <label>Chance: <span id="${prefix}-att-chance-lbl-${i}">${d.chance.toFixed(2)}</span></label>
          <input type="range" id="${prefix}-att-chance-${i}" min="0" max="1" step="0.01" value="${d.chance}" />
        </div>
        <button class="btn btn-danger btn-sm att-remove" data-prefix="${prefix}" data-idx="${i}" style="margin-bottom:2px">Remove</button>
      </div>
      ${legacyBadge}${unknownBadge}
      <div id="${prefix}-att-plainid-${i}" ${isJsonObj ? 'style="display:none"' : ''}>
        <div class="form-group">
          <label>PayoutAssetId</label>
          <input type="text" id="${prefix}-att-id-${i}" value="${esc(d.payoutAssetId)}" placeholder="e.g. mine_1" ${plainDatalist} />
        </div>
      </div>
      <div id="${prefix}-att-item-${i}" ${!isJsonObj ? 'style="display:none"' : ''}>
        <div style="font-size:12px;font-weight:bold;margin:4px 0 2px">${isTicket ? 'Ticket (serialized as JSON object)' : 'ItemSpecificAsset (serialized as JSON object)'}</div>
        ${itemRowHtml(prefix, i, rows[0] ?? defaultItemRow(), bpDatalist)}
      </div>
    </div>`
  }).join('')

  drafts.forEach((d, i) => {
    const slider = document.getElementById(`${prefix}-att-chance-${i}`) as HTMLInputElement
    const label  = document.getElementById(`${prefix}-att-chance-lbl-${i}`)
    if (slider && label) slider.oninput = () => { label.textContent = parseFloat(slider.value).toFixed(2) }

    const typeInput = document.getElementById(`${prefix}-att-type-${i}`) as HTMLInputElement | null
    if (typeInput) {
      typeInput.oninput = () => {
        const nowIsJson = isJsonObjectType(typeInput.value)
        const plainDiv = document.getElementById(`${prefix}-att-plainid-${i}`)
        const itemDiv  = document.getElementById(`${prefix}-att-item-${i}`)
        if (plainDiv) plainDiv.style.display = nowIsJson ? 'none' : ''
        if (itemDiv)  itemDiv.style.display  = nowIsJson ? '' : 'none'
        // Update BlueprintId datalist when switching between Ticket and ISA
        const bpInput = document.getElementById(`${prefix}-att-${i}-row-bp-0`) as HTMLInputElement | null
        if (bpInput) bpInput.setAttribute('list', isTicketType(typeInput.value) ? 'ticket-ids-list' : 'item-ids-list')
        // Update plain id datalist
        const plainInput = document.getElementById(`${prefix}-att-id-${i}`) as HTMLInputElement | null
        if (plainInput) {
          const lt = typeInput.value.toLowerCase()
          plainInput.setAttribute('list', lt === 'currency' ? 'currency-ids-list' : lt === 'item' ? 'item-ids-list' : '')
        }
      }
    }
  })
}

function syncItemRowFromDom(prefix: string, attIdx: number, fallback: ItemSpecificAsset): ItemSpecificAsset {
  return {
    BlueprintId: val(`${prefix}-att-${attIdx}-row-bp-0`),
    CurrentLevel: parseInt(val(`${prefix}-att-${attIdx}-row-cl-0`), 10) || fallback.CurrentLevel,
    Rarity: (parseInt(val(`${prefix}-att-${attIdx}-row-rar-0`), 10) || Rarity.Common) as RarityValue,
    InitialLevel: parseInt(val(`${prefix}-att-${attIdx}-row-il-0`), 10) || fallback.InitialLevel,
    FromSource: val(`${prefix}-att-${attIdx}-row-fs-0`),
  }
}

function syncAttachmentsFromDom(prefix: string, drafts: AttachmentDraft[]): AttachmentDraft[] {
  return drafts.map((d, i) => {
    const assetType = (document.getElementById(`${prefix}-att-type-${i}`) as HTMLInputElement | null)?.value ?? d.assetType
    const isJsonObj = isJsonObjectType(assetType)
    return {
      payoutAssetId: isJsonObj ? '' : val(`${prefix}-att-id-${i}`),
      assetType,
      payoutAmount: parseInt(val(`${prefix}-att-amt-${i}`), 10) || d.payoutAmount,
      chance: parseFloat(
        (document.getElementById(`${prefix}-att-chance-${i}`) as HTMLInputElement | null)?.value ?? String(d.chance),
      ),
      itemRows: isJsonObj ? [syncItemRowFromDom(prefix, i, d.itemRows?.[0] ?? defaultItemRow())] : [defaultItemRow()],
    }
  })
}

// ─── Target user IDs editor ───────────────────────────────────────────────────────
function renderTargetUserIds() {
  const container = document.getElementById('target-user-ids')
  if (!container) return
  container.innerHTML = state.targetUserIds.map((uid, i) => `
    <div class="user-id-row">
      <input type="text" id="uid-${i}" value="${esc(uid)}" placeholder="Player UUID or ID" />
      <button class="btn btn-ghost btn-sm uid-remove" data-idx="${i}" ${state.targetUserIds.length <= 1 ? 'disabled' : ''}>✕</button>
    </div>`).join('')
}

function syncUserIdsFromDom(): string[] {
  return state.targetUserIds.map((_, i) => val(`uid-${i}`))
}

// ─── End-time editor ──────────────────────────────────────────────────────────────
function endTimeEditorHtml(prefix: string, useEndTime: boolean, endDate: string, endTime: string): string {
  return `
  <div class="form-group">
    <label>End Time (expiresAt)</label>
    <div style="display:flex;gap:6px;margin-bottom:4px">
      <label style="display:flex;align-items:center;gap:4px;cursor:pointer">
        <input type="radio" name="${prefix}-et-mode" value="none" ${!useEndTime ? 'checked' : ''} /> None (no expiry)
      </label>
      <label style="display:flex;align-items:center;gap:4px;cursor:pointer">
        <input type="radio" name="${prefix}-et-mode" value="use" ${useEndTime ? 'checked' : ''} /> Use UTC time
      </label>
    </div>
    <div id="${prefix}-endtime-editor" class="endtime-editor" ${!useEndTime ? 'style="display:none"' : ''}>
      <div class="endtime-row">
        <div class="form-group"><label>Date UTC (yyyy-MM-dd)</label><input type="date" id="${prefix}-end-date" value="${esc(endDate)}" /></div>
        <div class="form-group"><label>Time UTC (HH:mm)</label><input type="time" id="${prefix}-end-time" value="${esc(endTime)}" /></div>
      </div>
      <div class="preset-row">
        <button class="btn btn-ghost btn-sm" data-preset-days="1" data-prefix="${prefix}">+1d</button>
        <button class="btn btn-ghost btn-sm" data-preset-days="7" data-prefix="${prefix}">+7d</button>
        <button class="btn btn-ghost btn-sm" data-preset-days="30" data-prefix="${prefix}">+30d</button>
      </div>
    </div>
  </div>`
}

// ─── Import / paste panel ─────────────────────────────────────────────────────────
function importPanelHtml(prefix: 'g' | 'u'): string {
  const expanded = prefix === 'g' ? state.globalImportExpanded : state.userImportExpanded
  const text     = prefix === 'g' ? state.globalImportText     : state.userImportText
  const errors   = prefix === 'g' ? state.globalImportErrors   : state.userImportErrors
  const warnings = prefix === 'g' ? state.globalImportWarnings : state.userImportWarnings

  const errorHtml = errors.length > 0
    ? `<div class="alert alert-error" style="margin-top:6px;font-size:12px">${errors.map(e => esc(e)).join('<br>')}</div>`
    : ''
  const warnHtml = warnings.length > 0
    ? `<div class="alert alert-warning" style="margin-top:4px;font-size:12px">${warnings.map(w => esc(w)).join('<br>')}</div>`
    : ''

  return `
  <div class="card" style="margin-top:8px">
    <div style="display:flex;align-items:center;gap:8px;cursor:pointer" id="${prefix}-import-toggle">
      <span style="font-size:13px;font-weight:bold">${expanded ? '▼' : '►'} 📋 Paste from JSON (import draft)</span>
    </div>
    ${expanded ? `
    <div style="margin-top:8px">
      <textarea id="${prefix}-import-textarea" rows="6"
        style="width:100%;font-size:11px;font-family:monospace;resize:vertical"
        placeholder='Paste exported mail JSON here ({"schemaVersion":1,"mail":{...}})'>${esc(text)}</textarea>
      ${errorHtml}${warnHtml}
      <div class="btn-row" style="margin-top:6px">
        <button class="btn btn-secondary btn-sm" id="${prefix}-import-apply" ${!isConnected() ? 'disabled' : ''}>
          Apply as Draft
        </button>
        <small style="color:var(--text-dim);font-size:11px">Import fills the form only — does NOT send automatically.</small>
      </div>
    </div>` : ''}
  </div>`
}

// ─── Send Global tab ──────────────────────────────────────────────────────────────
function renderSendGlobalTab(): string {
  return `
  <div class="card">
    <div class="card-title">📨 Send Global Mail</div>
    <div class="form-group">
      <label>Subject (Title) <span style="color:var(--text-dim)">[1-128 chars]</span></label>
      <input type="text" id="g-subject" value="${esc(state.globalSubject)}" maxlength="128" placeholder="Mail title" />
    </div>
    <div class="form-group">
      <label>Body (Content) <span style="color:var(--text-dim)">[1-1024 chars]</span></label>
      <textarea id="g-body" maxlength="1024" placeholder="Mail body text">${esc(state.globalBody)}</textarea>
    </div>
    ${endTimeEditorHtml('g', state.globalUseEndTime, state.globalEndDate, state.globalEndTime)}
    <div class="form-group">
      <label>Category</label>
      <select id="g-category">
        ${CATEGORY_OPTIONS.map((c, i) => `<option value="${i}" ${i === state.globalCategory ? 'selected' : ''}>${c}</option>`).join('')}
      </select>
    </div>
    <div class="form-group">
      <label>Sender Name <span style="color:var(--text-dim)">(optional)</span></label>
      <input type="text" id="g-sender" value="${esc(state.globalSenderName)}" placeholder="e.g. System" />
    </div>
    <div class="form-group">
      <label>Dedup Key <span style="color:var(--text-dim)">(optional)</span></label>
      <input type="text" id="g-dedup" value="${esc(state.globalDedupKey)}" placeholder="Unique key to prevent duplicate sends" />
    </div>
    <div class="card-title" style="margin-top:12px">📎 Attachments</div>
    <div id="global-att-list"></div>
    <button class="btn btn-ghost btn-sm" id="g-att-add" style="margin-top:6px">+ Add Attachment</button>
    <div class="btn-row" style="margin-top:16px">
      <button class="btn btn-primary" id="g-send" ${!isConnected() || state.busy ? 'disabled' : ''}>
        ${state.busy ? '<span class="spinner"></span>' : ''} Send Global Mail
      </button>
    </div>
  </div>
  ${importPanelHtml('g')}`
}

// ─── Send Targeted tab ────────────────────────────────────────────────────────────
function renderSendTargetedTab(): string {
  return `
  <div class="card">
    <div class="card-title">🎯 Send Targeted Mail</div>
    <div class="form-group">
      <label>Target User IDs</label>
      <div id="target-user-ids"></div>
      <button class="btn btn-ghost btn-sm" id="uid-add" style="margin-top:4px">+ Add User ID</button>
    </div>
    <div class="form-group" style="margin-top:8px">
      <label>Subject (Title) <span style="color:var(--text-dim)">[1-128 chars]</span></label>
      <input type="text" id="u-subject" value="${esc(state.userSubject)}" maxlength="128" placeholder="Mail title" />
    </div>
    <div class="form-group">
      <label>Body (Content) <span style="color:var(--text-dim)">[1-1024 chars]</span></label>
      <textarea id="u-body" maxlength="1024" placeholder="Mail body text">${esc(state.userBody)}</textarea>
    </div>
    ${endTimeEditorHtml('u', state.userUseEndTime, state.userEndDate, state.userEndTime)}
    <div class="form-group">
      <label>Category</label>
      <select id="u-category">
        ${CATEGORY_OPTIONS.map((c, i) => `<option value="${i}" ${i === state.userCategory ? 'selected' : ''}>${c}</option>`).join('')}
      </select>
    </div>
    <div class="form-group">
      <label>Sender Name <span style="color:var(--text-dim)">(optional)</span></label>
      <input type="text" id="u-sender" value="${esc(state.userSenderName)}" placeholder="e.g. Admin" />
    </div>
    <div class="form-group">
      <label>Dedup Key <span style="color:var(--text-dim)">(optional)</span></label>
      <input type="text" id="u-dedup" value="${esc(state.userDedupKey)}" placeholder="Unique key to prevent duplicate sends" />
    </div>
    <div class="card-title" style="margin-top:12px">📎 Attachments</div>
    <div id="user-att-list"></div>
    <button class="btn btn-ghost btn-sm" id="u-att-add" style="margin-top:6px">+ Add Attachment</button>
    <div class="btn-row" style="margin-top:16px">
      <button class="btn btn-primary" id="u-send" ${!isConnected() || state.busy ? 'disabled' : ''}>
        ${state.busy ? '<span class="spinner"></span>' : ''} Send Targeted Mail
      </button>
    </div>
  </div>
  ${importPanelHtml('u')}`
}

function ensureInlineAttachmentDrafts(mail: MailRecord): AttachmentDraft[] {
  const id = mailId(mail)
  if (!state.inlineAttachments[id]) {
    state.inlineAttachments[id] = mailAttachments(mail).map(attachmentInfoToDraft)
  }
  return state.inlineAttachments[id]
}

function renderInlineAttachments(mail: MailRecord, idKey: string): string {
  const id = mailId(mail)
  const drafts = ensureInlineAttachmentDrafts(mail)
  const rows = drafts.length === 0
    ? '<div class="empty" style="padding:8px">No attachments.</div>'
    : drafts.map((d, i) => {
        const isISA    = isItemSpecificAssetType(d.assetType)
        const isTicket = isTicketType(d.assetType)
        const isJsonObj = isISA || isTicket
        const itemRows = d.itemRows ?? [defaultItemRow()]
        const bpDatalist = isTicket ? 'ticket-ids-list' : 'item-ids-list'
        const itemRowsHtmlStr = isJsonObj ? itemRowHtml(`mia-${idKey}`, i, itemRows[0] ?? defaultItemRow(), bpDatalist) : ''
        const legacyBadge = d._legacyWarning
          ? `<span style="color:orange;font-size:10px" title="${esc(d._legacyWarning)}">⚠ legacy</span>`
          : ''
        return `
      <div class="inline-att-row" style="flex-direction:column;align-items:stretch">
        <div style="display:flex;gap:4px;align-items:center;flex-wrap:wrap">
          <input type="text" id="mia-type-${idKey}-${i}" list="asset-type-list" value="${esc(d.assetType)}" style="width:130px" placeholder="AssetType" />
          <input type="number" id="mia-amt-${idKey}-${i}" value="${d.payoutAmount}" min="1" title="Amount" style="width:56px" />
          <input type="number" id="mia-chance-${idKey}-${i}" value="${d.chance}" min="0.01" max="1" step="0.01" title="Chance" style="width:56px" />
          ${legacyBadge}
          <button class="btn btn-danger btn-sm inline-att-remove" data-mail-id="${esc(id)}" data-id-key="${idKey}" data-idx="${i}" ${state.busy ? 'disabled' : ''}>✕</button>
        </div>
        <div id="mia-plainid-${idKey}-${i}" ${isJsonObj ? 'style="display:none"' : ''}>
          <input type="text" id="mia-id-${idKey}-${i}" value="${esc(d.payoutAssetId)}" placeholder="asset id" style="width:100%;margin-top:4px" />
        </div>
        <div id="mia-item-${idKey}-${i}" ${!isJsonObj ? 'style="display:none"' : ''}>
          ${itemRowsHtmlStr}
        </div>
      </div>`
      }).join('')

  return `
    <div class="inline-att-list" id="mia-list-${idKey}">${rows}</div>
    <button class="btn btn-ghost btn-sm inline-att-add" data-mail-id="${esc(id)}" data-id-key="${idKey}" ${state.busy ? 'disabled' : ''}>+ Add</button>`
}

function syncInlineAttachmentsFromDom(mailIdValue: string, idKey: string): AttachmentDraft[] {
  const drafts = state.inlineAttachments[mailIdValue] ?? []
  state.inlineAttachments[mailIdValue] = drafts.map((d, i) => {
    const assetType = (document.getElementById(`mia-type-${idKey}-${i}`) as HTMLInputElement | null)?.value ?? d.assetType
    const isJsonObj = isJsonObjectType(assetType)
    const syncedRows: ItemSpecificAsset[] = isJsonObj
      ? [syncItemRowFromDom(`mia-${idKey}`, i, d.itemRows?.[0] ?? defaultItemRow())]
      : [defaultItemRow()]
    return {
      payoutAssetId: isJsonObj ? '' : val(`mia-id-${idKey}-${i}`),
      assetType,
      payoutAmount: parseInt(val(`mia-amt-${idKey}-${i}`), 10) || d.payoutAmount,
      chance: parseFloat(val(`mia-chance-${idKey}-${i}`)) || d.chance,
      itemRows: syncedRows,
    }
  })
  return state.inlineAttachments[mailIdValue]
}

// ─── Inline target user IDs editor ───────────────────────────────────────────────
function ensureInlineTargetUserIds(mail: MailRecord): string[] {
  const id = mailId(mail)
  if (!state.inlineTargetUserIds[id]) {
    const existing = mailTargetUsers(mail)
    state.inlineTargetUserIds[id] = existing.length > 0 ? [...existing] : []
  }
  return state.inlineTargetUserIds[id]
}

function renderInlineTargetUserIds(mail: MailRecord, idKey: string): string {
  const id = mailId(mail)
  const uids = ensureInlineTargetUserIds(mail)
  const rowsHtml = uids.length === 0
    ? '<div style="font-size:11px;color:var(--text-dim);padding:2px">Global (all players)</div>'
    : uids.map((uid, i) => `
        <div style="display:flex;gap:3px;align-items:center;margin-bottom:2px">
          <input type="text" id="mtu-${idKey}-${i}" value="${esc(uid)}" placeholder="Player ID" style="flex:1;font-size:11px;padding:3px 5px" />
          <button class="btn btn-danger btn-sm inline-uid-remove" data-mail-id="${esc(id)}" data-id-key="${idKey}" data-idx="${i}" style="padding:1px 5px;font-size:10px" ${state.busy ? 'disabled' : ''}>✕</button>
        </div>`).join('')
  return `
    <div id="mtu-list-${idKey}" style="min-width:140px">${rowsHtml}</div>
    <button class="btn btn-ghost btn-sm inline-uid-add" data-mail-id="${esc(id)}" data-id-key="${idKey}" style="font-size:10px;padding:1px 6px;margin-top:2px" ${state.busy ? 'disabled' : ''}>+ User</button>`
}

function syncInlineTargetUserIdsFromDom(mailIdValue: string, idKey: string): string[] {
  const uids = state.inlineTargetUserIds[mailIdValue] ?? []
  state.inlineTargetUserIds[mailIdValue] = uids.map((_, i) => val(`mtu-${idKey}-${i}`))
  return state.inlineTargetUserIds[mailIdValue]
}

// ─── Scope badge ──────────────────────────────────────────────────────────────────
function getScopeLabel(m: MailRecord, source: 'global' | 'user'): MailScope {
  if (source === 'user') return 'User'
  const targets = mailTargetUsers(m)
  return targets.length > 0 ? 'Global-targeted' : 'Global'
}

function scopeBadgeHtml(scope: MailScope): string {
  const colors: Record<MailScope, string> = {
    'Global':          'background:#2d6a4f;color:#fff',
    'Global-targeted': 'background:#b5451b;color:#fff',
    'User':            'background:#5a3e85;color:#fff',
  }
  return `<span style="${colors[scope]};padding:2px 6px;border-radius:3px;font-size:10px;white-space:nowrap">${scope}</span>`
}

// ─── Mail list table ──────────────────────────────────────────────────────────────
function renderMailRow(m: MailRecord, source: 'global' | 'user' = 'global', readOnly = false): string {
  const id    = mailId(m)
  const endT  = mailEndTime(m)
  const idKey = id.replace(/[^a-z0-9]/gi, '_')
  const ied   = state.inlineEndDate[id] ?? (endT ? endT.substring(0, 10) : '')
  const iet   = state.inlineEndTime[id] ?? (endT ? endT.substring(11, 16) : '')
  const scope = getScopeLabel(m, source)

  if (readOnly) {
    // Read-only row for user mail lookup (no edit actions)
    return `
  <tr>
    <td><div class="mail-id">${esc(id)}</div></td>
    <td>${scopeBadgeHtml(scope)}</td>
    <td>${esc(mailTitle(m))}</td>
    <td>${esc(mailContent(m))}</td>
    <td>${esc(mailStartTime(m))}</td>
    <td>${esc(endT ?? 'none')}</td>
    <td class="actions-cell">
      <button class="btn btn-ghost btn-sm copy-mail-json" data-mail-id="${esc(id)}" data-source="${source}" title="Copy as JSON">📋 Copy</button>
    </td>
  </tr>`
  }

  return `
  <tr>
    <td><div class="mail-id">${esc(id)}</div></td>
    <td>${scopeBadgeHtml(scope)}</td>
    <td><input type="text" id="mt-${idKey}" value="${esc(mailTitle(m))}" maxlength="128" /></td>
    <td><textarea id="mc-${idKey}" maxlength="1024">${esc(mailContent(m))}</textarea></td>
    <td>${esc(mailStartTime(m))}</td>
    <td>
      <div>${esc(endT ?? 'none')}</div>
      <div class="mail-endtime-edit">
        <input type="date" id="ied-${idKey}" value="${esc(ied)}" />
        <input type="time" id="iet-${idKey}" value="${esc(iet)}" />
        <button class="btn btn-ghost btn-sm set-et" data-mail-id="${esc(id)}" data-id-key="${idKey}">✓</button>
      </div>
    </td>
    <td>${renderInlineTargetUserIds(m, idKey)}</td>
    <td>${renderInlineAttachments(m, idKey)}</td>
    <td class="actions-cell">
      <button class="btn btn-ghost btn-sm copy-mail-json" data-mail-id="${esc(id)}" data-source="${source}" title="Copy as JSON">📋</button>
      <button class="btn btn-secondary btn-sm save-mail" data-mail-id="${esc(id)}" data-id-key="${idKey}" ${!isConnected() || state.busy ? 'disabled' : ''}>Save</button>
      <button class="btn btn-ghost btn-sm expire-mail" data-mail-id="${esc(id)}" ${!isConnected() || state.busy ? 'disabled' : ''}>Expire</button>
      <button class="btn btn-danger btn-sm del-mail"   data-mail-id="${esc(id)}" ${!isConnected() || state.busy ? 'disabled' : ''}>Delete</button>
    </td>
  </tr>`
}

// ─── Manage tab ───────────────────────────────────────────────────────────────────
function renderManageTab(): string {
  const allMails = state.mailList
  const total = allMails?.length ?? state.mailTotalCount
  const page  = state.mailPage
  const pageCount = total === 0 ? 1 : Math.ceil(total / MAILS_PER_PAGE)
  const pageStart = page * MAILS_PER_PAGE
  const mails = allMails?.slice(pageStart, pageStart + MAILS_PER_PAGE) ?? null

  let tableHtml = ''
  if (mails === null) {
    tableHtml = `<div class="empty">Click "Load All Global Mails" to fetch.</div>`
    if (state.globalMailError) {
      tableHtml += `<div class="alert alert-error" style="margin-top:8px">${esc(state.globalMailError)}</div>`
    }
  } else if (mails.length === 0) {
    tableHtml = '<div class="empty">No mails found.</div>'
  } else {
    tableHtml = `
    <div class="mail-table-wrap">
      <table class="mail-table">
        <thead><tr>
          <th>Message ID</th><th>Scope</th><th>Title</th><th>Content</th>
          <th>Start Time</th><th>End Time / Set New</th><th>Target Users</th><th>Attachments</th><th>Actions</th>
        </tr></thead>
        <tbody id="mail-tbody">${mails.map(m => renderMailRow(m, 'global')).join('')}</tbody>
      </table>
    </div>
    <div class="pager">
      <button class="btn btn-ghost btn-sm" id="pg-prev" ${page <= 0 || state.busy ? 'disabled' : ''}>← Prev</button>
      <span>Page ${page + 1} / ${pageCount} · ${total} total · ${MAILS_PER_PAGE} per page</span>
      <button class="btn btn-ghost btn-sm" id="pg-next" ${page >= pageCount - 1 || state.busy ? 'disabled' : ''}>Next →</button>
    </div>`
  }

  // ── User Mail Lookup ──────────────────────────────────────────────────────────
  let userMailHtml = ''
  if (state.userMailError) {
    userMailHtml = `<div class="alert alert-error" style="margin-top:8px">${esc(state.userMailError)}</div>`
  } else if (state.userMailList === null) {
    userMailHtml = '<div class="empty" style="font-size:12px">Enter a Player ID above and click Fetch.</div>'
  } else if (state.userMailList.length === 0) {
    userMailHtml = '<div class="empty">No user mails found for this player.</div>'
  } else {
    userMailHtml = `
    <div class="mail-table-wrap" style="margin-top:8px">
      <table class="mail-table">
        <thead><tr>
          <th>Message ID</th><th>Scope</th><th>Title</th><th>Content</th><th>Start Time</th><th>End Time</th><th>Actions</th>
        </tr></thead>
        <tbody id="user-mail-tbody">${state.userMailList.map(m => renderMailRow(m, 'user', true)).join('')}</tbody>
      </table>
    </div>`
  }

  return `
  <div class="card">
    <div class="card-title">📋 Load All Global Mails (admin mode)</div>
    <div style="display:flex;gap:8px;align-items:flex-end">
      <button class="btn btn-secondary" id="m-load" ${!isConnected() || state.busy ? 'disabled' : ''}>
        ${state.busy ? '<span class="spinner"></span>' : '🔄'} Load All Global Mails
      </button>
    </div>
    <div style="margin-top:12px">${tableHtml}</div>
  </div>

  <div class="card">
    <div class="card-title">🔍 User Mail Lookup</div>
    <div style="display:flex;gap:8px;align-items:flex-end">
      <div class="form-group" style="flex:1;margin-bottom:0">
        <label>Player ID</label>
        <input type="text" id="user-lookup-id" value="${esc(state.userMailLookupPlayerId)}" placeholder="player UUID" />
      </div>
      <button class="btn btn-secondary" id="m-lookup-user" ${!isConnected() || state.busy ? 'disabled' : ''}>
        ${state.busy ? '<span class="spinner"></span>' : '🔍'} Fetch User Mail
      </button>
    </div>
    <div style="margin-top:8px">${userMailHtml}</div>
  </div>

  <div class="card">
    <div class="card-title">⚙️ Direct Mail Operations</div>
    <div class="form-group">
      <label>Mail ID</label>
      <input type="text" id="m-mail-id" value="${esc(state.manageMailId)}" placeholder="Global mail UUID" />
    </div>
    ${endTimeEditorHtml('m', state.manageUseEndTime, state.manageEndDate, state.manageEndTime)}
    <div class="btn-row" style="margin-top:8px">
      <button class="btn btn-secondary" id="m-set-et" ${!isConnected() || state.busy ? 'disabled' : ''}>Set EndTime</button>
      <button class="btn btn-ghost"     id="m-expire" ${!isConnected() || state.busy ? 'disabled' : ''}>Expire Mail</button>
      <button class="btn btn-danger"    id="m-delete" ${!isConnected() || state.busy ? 'disabled' : ''}>Delete Mail</button>
    </div>
  </div>

  <div class="card">
    <div class="card-title">🗑️ Purge Expired</div>
    <p style="font-size:12px;color:var(--text-dim);margin-bottom:10px">Removes all expired global mails. Irreversible.</p>
    <button class="btn btn-danger" id="m-purge" ${!isConnected() || state.busy ? 'disabled' : ''}>
      ${state.busy ? '<span class="spinner"></span>' : ''} Purge All Expired
    </button>
  </div>`
}

// ─── Sidebar — Connection form ────────────────────────────────────────────────────
function renderSidebar(): string {
  return `
  <div class="alert alert-security">
    🔒 <strong>Proxy model:</strong> UGS service-account credentials are handled server-side
    by the same-origin Cloudflare Pages Function. This form never receives, stores, or transmits your Key or Secret.
    Only the <strong>Proxy Access Token</strong> is sensitive — it is kept in memory only
    and cleared on "Logout". All other fields are session-stored for convenience.
  </div>

  <div class="card">
    <div class="card-title">🔑 Connection</div>
    <div class="form-group">
      <label>Module Name</label>
      <input type="text" id="c-module" value="${esc(state.moduleName)}" placeholder="BackpackAdventuresModule" autocomplete="off" />
    </div>
    <div class="form-group">
      <label>Project ID</label>
      <input type="text" id="c-project-id" value="${esc(state.projectId)}" placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" autocomplete="off" />
    </div>
    <div class="form-group">
      <label>Environment (name or UUID)</label>
      <input type="text" id="c-env" value="${esc(state.environment)}" placeholder="production / testing / UUID" autocomplete="off" />
      <small style="color:var(--text-dim);font-size:11px">The proxy resolves names to UUIDs — no browser API call needed</small>
    </div>
    <div class="form-group">
      <label>Operator ID (email)</label>
      <input type="text" id="c-operator" value="${esc(state.operatorId)}" placeholder="admin@example.com" autocomplete="off" />
    </div>
    <div class="form-group">
      <label>Proxy Access Token <span style="color:var(--accent)">★ session-only</span></label>
      <input type="password" id="c-token" value="" placeholder="Proxy bearer token (not stored)" autocomplete="new-password" />
      ${state.proxyToken ? '<small style="color:var(--success);font-size:11px">✓ Token in memory</small>' : ''}
    </div>
    ${isConnected()
      ? `<div class="alert alert-success" style="font-size:12px">✓ Connected — ${esc(state.moduleName)} / ${esc(state.environment)}</div>`
      : ''}
    <div class="btn-row">
      <button class="btn btn-primary" id="c-connect" ${state.busy ? 'disabled' : ''}>
        ${state.busy ? '<span class="spinner"></span>' : '⚡'} Connect
      </button>
      <button class="btn btn-ghost" id="c-logout">🚪 Logout / Clear</button>
    </div>
  </div>

  <div id="status-bar"></div>`
}

// ─── Full page render ─────────────────────────────────────────────────────────────
function renderApp() {
  const sendTabHtml = state.activeSendSubTab === 'global'
    ? renderSendGlobalTab()
    : renderSendTargetedTab()

  const isManage = state.activeTab === 'manage'
  const mainContent = isManage
    ? `<div id="manage-panel"></div>`
    : `<div class="tabs">
        <button class="tab-btn ${state.activeSendSubTab === 'global'   ? 'active' : ''}" id="sub-global">Send Global</button>
        <button class="tab-btn ${state.activeSendSubTab === 'targeted' ? 'active' : ''}" id="sub-targeted">Send Targeted</button>
       </div>
       ${sendTabHtml}`

  // Build ID datalist options (rendered once per page)
  const currencyOptions = CURRENCY_IDS.map(id => `<option value="${esc(id)}"></option>`).join('')
  const itemOptions     = ITEM_IDS.map(id => `<option value="${esc(id)}"></option>`).join('')
  const ticketOptions   = TICKET_IDS.map(id => `<option value="${esc(id)}"></option>`).join('')

  el<HTMLDivElement>('app').innerHTML = `
  <datalist id="asset-type-list">
    <option value="Currency"></option>
    <option value="Item"></option>
    <option value="Ticket"></option>
    <option value="ItemSpecificAsset"></option>
  </datalist>
  <datalist id="currency-ids-list">${currencyOptions}</datalist>
  <datalist id="item-ids-list">${itemOptions}</datalist>
  <datalist id="ticket-ids-list">${ticketOptions}</datalist>
  <header class="app-header">
    <span id="conn-dot" class="status-dot"></span>
    <h1>Admin Mail</h1>
    <span class="badge">BackpackAdventures</span>
    <div style="margin-left:auto;font-size:12px;color:var(--text-dim)">via Proxy → UGS Cloud Code</div>
  </header>
  <div class="app-body">
    <aside class="sidebar" id="sidebar">${renderSidebar()}</aside>
    <main class="main-content">
      <div class="tabs">
        <button class="tab-btn ${state.activeTab === 'send'   ? 'active' : ''}" id="tab-send">✉️ Send Mail</button>
        <button class="tab-btn ${state.activeTab === 'manage' ? 'active' : ''}" id="tab-manage">📋 Manage Mails</button>
      </div>
      <div id="main-panel">${mainContent}</div>
    </main>
  </div>`

  renderConnectionDot()
  renderStatus()
  attachAllListeners()
  if (isManage) {
    _manageTabHandle?.destroy()
    _manageTabHandle = null
    const mp = document.getElementById('manage-panel')
    if (mp) _manageTabHandle = mountManageTab(mp, _buildManageTabDeps())
  } else {
    if (state.activeSendSubTab === 'global') {
      renderAttachmentList('global-att-list', 'g', state.globalAttachments)
    } else {
      renderTargetUserIds()
      renderAttachmentList('user-att-list', 'u', state.userAttachments)
    }
  }
}

// ─── Partial re-renders ───────────────────────────────────────────────────────────
function refreshSidebar() {
  const sb = document.getElementById('sidebar')
  if (!sb) { renderApp(); return }
  sb.innerHTML = renderSidebar()
  renderConnectionDot()
  renderStatus()
  attachSidebarListeners()
}

function refreshMainPanel() {
  const panel = document.getElementById('main-panel')
  if (!panel) { renderApp(); return }

  if (state.activeTab === 'manage') {
    // Destroy old handle (also destroys its drawer)
    _manageTabHandle?.destroy()
    _manageTabHandle = null
    panel.innerHTML = `<div id="manage-panel"></div>`
    attachMainListeners()
    const mp = document.getElementById('manage-panel')
    if (mp) {
      _manageTabHandle = mountManageTab(mp, _buildManageTabDeps())
    }
    return
  }

  const sendTabHtml = state.activeSendSubTab === 'global'
    ? renderSendGlobalTab() : renderSendTargetedTab()

  panel.innerHTML = `<div class="tabs">
      <button class="tab-btn ${state.activeSendSubTab === 'global'   ? 'active' : ''}" id="sub-global">Send Global</button>
      <button class="tab-btn ${state.activeSendSubTab === 'targeted' ? 'active' : ''}" id="sub-targeted">Send Targeted</button>
     </div>${sendTabHtml}`

  attachMainListeners()
  if (state.activeSendSubTab === 'global') {
    renderAttachmentList('global-att-list', 'g', state.globalAttachments)
  } else {
    renderTargetUserIds()
    renderAttachmentList('user-att-list', 'u', state.userAttachments)
  }
}

function _buildManageTabDeps() {
  return {
    getMails:              () => state.mailList,
    getUserMails:          () => state.userMailList,
    getMailPage:           () => state.mailPage,
    getMailTotalCount:     () => state.mailTotalCount,
    getMailError:          () => state.globalMailError,
    getUserMailError:      () => state.userMailError,
    getUserLookupPlayerId: () => state.userMailLookupPlayerId,
    isBusy:                () => state.busy,
    isConnected,
    getEnv:                () => state.environment,
    currencyOptions:       CURRENCY_COMBOBOX_OPTIONS,
    itemOptions:           ITEM_COMBOBOX_OPTIONS,
    ticketOptions:         TICKET_COMBOBOX_OPTIONS,
    onLoad() { void run(() => doLoadMails(true, true)) },
    onLookupUser(pid: string) {
      state.userMailLookupPlayerId = pid
      void run(() => _doFetchUserMailById(pid))
    },
    onSave(mId: string, subject: string, body: string, drafts: AttachmentDraft[], targetUserIds: string[] | null, expiresAt: string | null) {
      void run(async () => {
        const args = resolveProxyArgs()
        const atts = buildAttachments(drafts)
        const { data, rawResponse } = await apiUpdateGlobalMail(args, {
          mailId: mId, subject, body, attachments: atts,
          targetUserIds: targetUserIds ?? null,
          operatorId: state.operatorId, adminToken: null,
        })
        // If expiresAt differs from current, also set end time
        const curr = state.mailList?.find(m => mailId(m) === mId)
        const currEnd = curr ? mailEndTime(curr) : null
        if (expiresAt !== currEnd) {
          await apiSetMailEndTime(args, {
            mailId: mId, endTime: expiresAt, operatorId: state.operatorId, adminToken: null,
          })
        }
        setStatus(`Updated: mailId=${data.mailId ?? mId}`, 'success', rawResponse)
        await refreshLoadedMails()
      })
    },
    onExpire(mId: string) {
      void run(async () => {
        if (!window.confirm(`Expire mail ${mId}? Sets expiry to now.`)) return
        const args = resolveProxyArgs()
        const { data, rawResponse } = await apiExpireMail(args, {
          mailId: mId, operatorId: state.operatorId, adminToken: null,
        })
        setStatus(`ExpireMail: mailId=${data.mailId ?? mId} expiredAt=${data.expiredAt ?? '-'}`, 'success', rawResponse)
        await refreshLoadedMails()
      })
    },
    onDelete(mId: string) {
      void run(async () => {
        if (!window.confirm(`Delete mail ${mId}? Irreversible.`)) return
        const args = resolveProxyArgs()
        const { data, rawResponse } = await apiDeleteGlobalMail(args, {
          mailId: mId, operatorId: state.operatorId, adminToken: null,
        })
        setStatus(`DeleteGlobalMail: mailId=${data.mailId ?? mId}`, 'success', rawResponse)
        await refreshLoadedMails()
      })
    },
    onCopyJson(mId: string, source: 'global' | 'user') {
      void run(() => doCopyMailAsJson(mId, source))
    },
    onPurge() { void run(doPurgeExpired) },
    onPageChange(delta: number) {
      state.mailPage = Math.max(0, state.mailPage + delta)
      refreshMainPanel()
    },
    onSetEndTime(mId: string, endTime: string | null) {
      void run(async () => {
        const args = resolveProxyArgs()
        const { data, rawResponse } = await apiSetMailEndTime(args, {
          mailId: mId, endTime, operatorId: state.operatorId, adminToken: null,
        })
        setStatus(`SetMailEndTime: mailId=${data.mailId ?? mId} endTime=${data.endTime ?? 'null'}`, 'success', rawResponse)
        await refreshLoadedMails()
      })
    },
  }
}

// ─── Async action runner ──────────────────────────────────────────────────────────
async function run(action: () => Promise<void>) {
  if (state.busy) return
  state.busy = true
  setStatus('Working…', 'info')
  try {
    await action()
  } catch (err) {
    const msg = err instanceof ApiError
      ? `Error ${err.status}: ${err.message}` : err instanceof Error ? err.message : String(err)
    const raw = err instanceof ApiError ? (err.body ?? '') : ''
    setStatus(msg, 'error', raw)
  } finally {
    state.busy = false
    refreshMainPanel()
  }
}

// ─── Actions ──────────────────────────────────────────────────────────────────────
async function doConnect() {
  const moduleName  = val('c-module').trim()   || 'BackpackAdventuresModule'
  const projectId   = val('c-project-id').trim()
  const environment = val('c-env').trim()
  const operatorId  = val('c-operator').trim()
  const proxyToken  = (document.getElementById('c-token') as HTMLInputElement | null)?.value ?? ''

  if (!projectId || !environment || !operatorId || !proxyToken) {
    setStatus('Project ID, Environment, Operator ID, and Proxy Token are required.', 'error')
    refreshSidebar()
    return
  }

  // Update state
  state.moduleName  = moduleName
  state.projectId   = projectId
  state.environment = environment
  state.operatorId  = operatorId
  state.proxyToken  = proxyToken   // memory only — NOT saved to sessionStorage

  saveCredentials({ projectId, environment, moduleName, operatorId })

  setStatus(
    `Connected — ${moduleName} / ${environment} via same-origin proxy`,
    'success',
  )
  refreshSidebar()
  refreshMainPanel()
}

async function doLogout() {
  clearAllCredentials()
  state.projectId   = ''
  state.environment = ''
  state.moduleName  = 'BackpackAdventuresModule'
  state.operatorId  = ''
  state.proxyToken  = ''    // cleared from memory
  setStatus('Logged out — all credentials cleared.', 'info')
  renderApp()
}

async function doSendGlobalMail(targeted = false) {
  const args   = resolveProxyArgs()
  const prefix = targeted ? 'u' : 'g'

  const subject     = val(`${prefix}-subject`).trim()
  const body        = val(`${prefix}-body`).trim()
  const useEndTime  = (document.querySelector(`input[name="${prefix}-et-mode"]:checked`) as HTMLInputElement | null)?.value === 'use'
  const endDate     = val(`${prefix}-end-date`)
  const endTime     = val(`${prefix}-end-time`)
  const category    = parseInt(val(`${prefix}-category`), 10) || 0
  const senderName  = val(`${prefix}-sender`).trim() || null
  const dedupKey    = val(`${prefix}-dedup`).trim() || null

  if (!subject || subject.length > 128) throw new Error('Title must be 1-128 characters.')
  if (!body || body.length > 1024)     throw new Error('Content must be 1-1024 characters.')

  const expiresAt   = buildEndTimeIso(useEndTime, endDate, endTime)
  const drafts      = targeted ? state.userAttachments : state.globalAttachments
  const synced      = syncAttachmentsFromDom(prefix, drafts)
  const attachments = buildAttachments(synced)

  let targetUserIds: string[] | null = null
  if (targeted) {
    const ids = syncUserIdsFromDom().map((s) => s.trim()).filter(Boolean)
    if (ids.length === 0) throw new Error('At least one Target User ID is required.')
    targetUserIds = ids
  }

  const { data, rawResponse } = await apiSendGlobalMail(args, {
    subject, body, expiresAt,
    mailCategory: CATEGORY_OPTIONS[category] ?? null,
    senderName, dedupKey, attachments,
    operatorId: state.operatorId,
    targetUserIds, adminToken: null,
  })

  const mId = data.mailId ?? data.globalMailId ?? '(unknown)'
  setStatus(
    `${targeted ? 'SendTargetedMail' : 'SendGlobalMail'}: mailId=${mId} sentAt=${data.sentAt ?? '-'}`,
    'success', rawResponse,
  )

  if (targeted) { state.userSubject = ''; state.userBody = ''; state.userAttachments = [defaultAttachment()] }
  else          { state.globalSubject = ''; state.globalBody = ''; state.globalAttachments = [defaultAttachment()] }
}

async function doLoadMails(resetPage = false, showStatus = true) {
  const args = resolveProxyArgs()
  if (resetPage) state.mailPage = 0
  state.globalMailError = ''
  const all: MailRecord[] = []
  let page = 0
  let hasMore = true
  let lastRawResponse = ''

  while (hasMore) {
    const { data, rawResponse } = await apiGetGlobalMails(args, {
      page,
      pageSize: FETCH_PAGE_SIZE,
      adminMode: true,
      operatorId: state.operatorId,
      adminToken: null,
    })
    const mails = data.Mails ?? data.mails ?? []
    all.push(...mails)
    hasMore = data.HasMore ?? data.hasMore ?? false
    lastRawResponse = rawResponse
    page++
    if (page >= MAX_FETCH_PAGES) break
  }

  state.mailList = all
  state.mailTotalCount = all.length
  state.inlineEndDate = {}
  state.inlineEndTime = {}
  state.inlineAttachments = {}
  state.inlineTargetUserIds = {}
  const maxPage = Math.max(0, Math.ceil(all.length / MAILS_PER_PAGE) - 1)
  if (state.mailPage > maxPage) state.mailPage = maxPage
  if (showStatus) {
    setStatus(`GetGlobalMails: loaded ${all.length} mails (${MAILS_PER_PAGE} per page)`, 'success', lastRawResponse)
  }
}

async function refreshLoadedMails() {
  if (state.mailList !== null) await doLoadMails(false, false)
}

async function doFetchUserMail() {
  const args      = resolveProxyArgs()
  const playerId  = (document.getElementById('user-lookup-id') as HTMLInputElement | null)?.value?.trim() ?? ''
  state.userMailLookupPlayerId = playerId

  if (!playerId) throw new Error('Player ID is required for user mail lookup.')

  state.userMailList  = null
  state.userMailError = ''

  const all: MailRecord[] = []
  let page = 0
  let hasMore = true
  let lastRawResponse = ''

  while (hasMore) {
    const { data, rawResponse } = await apiGetUserMailsAdmin(args, {
      targetPlayerId: playerId,
      page,
      pageSize: FETCH_PAGE_SIZE,
      operatorId: state.operatorId,
      adminToken: '',
    })
    const mails = data.Mails ?? data.mails ?? []
    all.push(...mails)
    hasMore = data.HasMore ?? data.hasMore ?? false
    lastRawResponse = rawResponse
    page++
    if (page >= MAX_FETCH_PAGES) break
  }

  state.userMailList = all
  setStatus(`GetUserMailsAdmin: loaded ${all.length} user mails for ${playerId}`, 'success', lastRawResponse)
}

// Step 3: player-id-as-argument variant used by the new manage tab deps
async function _doFetchUserMailById(playerId: string) {
  if (!playerId) throw new Error('Player ID is required.')
  const args = resolveProxyArgs()
  state.userMailList  = null
  state.userMailError = ''
  const all: MailRecord[] = []
  let page = 0; let hasMore = true; let lastRaw = ''
  while (hasMore) {
    const { data, rawResponse } = await apiGetUserMailsAdmin(args, {
      targetPlayerId: playerId, page, pageSize: FETCH_PAGE_SIZE,
      operatorId: state.operatorId, adminToken: '',
    })
    all.push(...(data.Mails ?? data.mails ?? []))
    hasMore = data.HasMore ?? data.hasMore ?? false
    lastRaw = rawResponse; page++
    if (page >= MAX_FETCH_PAGES) break
  }
  state.userMailList = all
  setStatus(`GetUserMailsAdmin: loaded ${all.length} user mails for ${playerId}`, 'success', lastRaw)
}

async function doUpdateGlobalMail(mailIdArg: string, idKey: string) {
  const args = resolveProxyArgs()
  const mId = mailIdArg.trim()
  if (!mId) throw new Error('Mail ID is required.')
  const subject = val(`mt-${idKey}`).trim()
  const body = val(`mc-${idKey}`).trim()
  if (!subject || subject.length > 128) throw new Error('Title must be 1-128 characters.')
  if (!body || body.length > 1024) throw new Error('Content must be 1-1024 characters.')

  const drafts = syncInlineAttachmentsFromDom(mId, idKey)
  const attachments = buildAttachments(drafts)
  const syncedUids = syncInlineTargetUserIdsFromDom(mId, idKey)
  const targetUserIds = syncedUids.map(s => s.trim()).filter(Boolean)
  const { data, rawResponse } = await apiUpdateGlobalMail(args, {
    mailId: mId,
    subject,
    body,
    attachments,
    targetUserIds: targetUserIds.length > 0 ? targetUserIds : null,
    operatorId: state.operatorId,
    adminToken: null,
  })
  setStatus(`UpdateGlobalMail: mailId=${data.mailId ?? mId}`, 'success', rawResponse)
  await refreshLoadedMails()
}

async function doSetMailEndTime(mailIdArg?: string, idKey?: string) {
  const args = resolveProxyArgs()
  const mId  = mailIdArg ?? val('m-mail-id').trim()
  if (!mId) throw new Error('Mail ID is required.')

  const useEndTime = idKey
    ? true
    : (document.querySelector('input[name="m-et-mode"]:checked') as HTMLInputElement | null)?.value === 'use'
  const endDate = idKey ? val(`ied-${idKey}`) : val('m-end-date')
  const endTime = idKey ? val(`iet-${idKey}`) : val('m-end-time')
  const endTimeIso = buildEndTimeIso(useEndTime, endDate, endTime)

  const { data, rawResponse } = await apiSetMailEndTime(args, {
    mailId: mId, endTime: endTimeIso, operatorId: state.operatorId, adminToken: null,
  })
  setStatus(`SetMailEndTime: mailId=${data.mailId ?? mId} endTime=${data.endTime ?? 'null'}`, 'success', rawResponse)
  await refreshLoadedMails()
}

async function doExpireMail(mailIdArg?: string) {
  const args = resolveProxyArgs()
  const mId  = mailIdArg ?? val('m-mail-id').trim()
  if (!mId) throw new Error('Mail ID is required.')
  if (!confirm(`Expire mail ${mId}?`)) return

  const { data, rawResponse } = await apiExpireMail(args, {
    mailId: mId, operatorId: state.operatorId, adminToken: null,
  })
  setStatus(`ExpireMail: mailId=${data.mailId ?? mId} expiredAt=${data.expiredAt ?? '-'}`, 'success', rawResponse)
  await refreshLoadedMails()
}

async function doDeleteMail(mailIdArg?: string) {
  const args = resolveProxyArgs()
  const mId  = mailIdArg ?? val('m-mail-id').trim()
  if (!mId) throw new Error('Mail ID is required.')
  if (!confirm(`Delete mail ${mId}? Irreversible.`)) return

  const { data, rawResponse } = await apiDeleteGlobalMail(args, {
    mailId: mId, operatorId: state.operatorId, adminToken: null,
  })
  setStatus(`DeleteGlobalMail: mailId=${data.mailId ?? mId}`, 'success', rawResponse)
  await refreshLoadedMails()
}

async function doPurgeExpired() {
  const args = resolveProxyArgs()
  if (!confirm('Purge all expired global mails? Irreversible.')) return

  const { data, rawResponse } = await apiPurgeExpired(args, {
    operatorId: state.operatorId, adminToken: null,
  })
  setStatus(`PurgeExpired: purgedCount=${data.purgedCount ?? 0} at ${data.purgedAt ?? '-'}`, 'success', rawResponse)
  await refreshLoadedMails()
}

// ─── Copy mail as JSON ────────────────────────────────────────────────────────────
function findMailById(mailIdValue: string, source: 'global' | 'user'): MailRecord | undefined {
  const list = source === 'user' ? state.userMailList : state.mailList
  return list?.find(m => mailId(m) === mailIdValue)
}

async function doCopyMailAsJson(mailIdValue: string, source: 'global' | 'user') {
  const m = findMailById(mailIdValue, source)
  if (!m) throw new Error(`Mail not found: ${mailIdValue}`)

  const scope: MailScope = getScopeLabel(m, source)
  const exported = buildMailExportJson(m, scope, state.environment, new Date().toISOString())
  const json = JSON.stringify(exported, null, 2)

  try {
    await navigator.clipboard.writeText(json)
    setStatus(`Copied mail ${mailIdValue} to clipboard (schemaVersion:1)`, 'success')
  } catch {
    // Fallback: show in status bar for manual copy
    setStatus(`Clipboard unavailable — copy the JSON below manually`, 'warning', json)
  }
}

// ─── Import (paste) panel actions ────────────────────────────────────────────────
function doApplyImport(prefix: 'g' | 'u') {
  const textarea = document.getElementById(`${prefix}-import-textarea`) as HTMLTextAreaElement | null
  const raw = textarea?.value?.trim() ?? ''

  // Size limit before parsing
  const maxBytes = 256 * 1024
  if (raw.length > maxBytes) {
    if (prefix === 'g') {
      state.globalImportErrors = [`Import too large: ${raw.length} bytes (max ${maxBytes})`]
      state.globalImportWarnings = []
    } else {
      state.userImportErrors = [`Import too large: ${raw.length} bytes (max ${maxBytes})`]
      state.userImportWarnings = []
    }
    refreshMainPanel()
    return
  }

  let parsed: unknown
  try {
    parsed = JSON.parse(raw)
  } catch {
    if (prefix === 'g') {
      state.globalImportErrors = ['Invalid JSON: ' + (raw.length < 100 ? raw : raw.substring(0, 100) + '…')]
      state.globalImportWarnings = []
    } else {
      state.userImportErrors = ['Invalid JSON: ' + (raw.length < 100 ? raw : raw.substring(0, 100) + '…')]
      state.userImportWarnings = []
    }
    refreshMainPanel()
    return
  }

  const result = validateAndImport(parsed, CURRENCY_IDS, ITEM_IDS, TICKET_IDS, raw, maxBytes)

  if (prefix === 'g') {
    state.globalImportErrors   = result.errors
    state.globalImportWarnings = result.warnings
    state.globalImportText     = raw
  } else {
    state.userImportErrors   = result.errors
    state.userImportWarnings = result.warnings
    state.userImportText     = raw
  }

  if (!result.ok || !result.draft) {
    refreshMainPanel()
    return
  }

  // Apply to form — NEVER touch: proxyToken, environment, projectId, operatorId, targetUserIds (for global)
  applyImportedDraft(result.draft, prefix)
  refreshMainPanel()
  setStatus(`Import applied: "${result.draft.title}"${result.warnings.length > 0 ? ` (${result.warnings.length} warning(s))` : ''}`, result.warnings.length > 0 ? 'warning' : 'success')
}

function applyImportedDraft(draft: ImportedDraft, prefix: 'g' | 'u') {
  // Safety: never overwrite the protected fields
  // FORBIDDEN from import: targetUserIds (for Global — it's broadcast), env, projectId, proxyToken
  if (prefix === 'g') {
    state.globalSubject    = draft.title
    state.globalBody       = draft.content
    state.globalUseEndTime = !!draft.endTime
    if (draft.endTime) {
      const d = new Date(draft.endTime)
      const pad = (n: number) => String(n).padStart(2, '0')
      state.globalEndDate = `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`
      state.globalEndTime = `${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}`
    }
    state.globalAttachments = draft.attachments.length > 0 ? draft.attachments : [defaultAttachment()]
    // NOTE: targetUserIds NOT applied for global tab — it's broadcast
  } else {
    state.userSubject    = draft.title
    state.userBody       = draft.content
    state.userUseEndTime = !!draft.endTime
    if (draft.endTime) {
      const d = new Date(draft.endTime)
      const pad = (n: number) => String(n).padStart(2, '0')
      state.userEndDate = `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`
      state.userEndTime = `${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}`
    }
    state.userAttachments = draft.attachments.length > 0 ? draft.attachments : [defaultAttachment()]
    // NOTE: targetUserIds from import is informational only — NOT applied to state.targetUserIds
    // (operator must manually enter target users to avoid accidental mass-sends)
  }
}

// ─── Event listeners ──────────────────────────────────────────────────────────────
function attachSidebarListeners() {
  document.getElementById('c-connect')?.addEventListener('click', () => run(doConnect))
  document.getElementById('c-logout')?.addEventListener('click',  () => run(doLogout))
}

function attachMainListeners() {
  document.getElementById('tab-send')?.addEventListener('click', () => { state.activeTab = 'send'; renderApp() })
  document.getElementById('tab-manage')?.addEventListener('click', () => { state.activeTab = 'manage'; renderApp() })
  document.getElementById('sub-global')?.addEventListener('click', () => { state.activeSendSubTab = 'global'; refreshMainPanel() })
  document.getElementById('sub-targeted')?.addEventListener('click', () => { state.activeSendSubTab = 'targeted'; refreshMainPanel() })

  document.getElementById('g-send')?.addEventListener('click', () => run(() => doSendGlobalMail(false)))
  document.getElementById('u-send')?.addEventListener('click', () => run(() => doSendGlobalMail(true)))

  // End-time mode toggles
  const bindEtToggle = (prefix: string) => {
    document.querySelectorAll<HTMLInputElement>(`input[name="${prefix}-et-mode"]`).forEach((r) => {
      r.addEventListener('change', () => {
        const use = r.value === 'use'
        const editor = document.getElementById(`${prefix}-endtime-editor`)
        if (editor) editor.style.display = use ? '' : 'none'
        if (use && !val(`${prefix}-end-date`)) {
          const p = presetEndTime(7)
          ;(el<HTMLInputElement>(`${prefix}-end-date`)).value = p.date
          ;(el<HTMLInputElement>(`${prefix}-end-time`)).value = p.time
        }
      })
    })
  }
  bindEtToggle('g'); bindEtToggle('u'); bindEtToggle('m')

  // Preset buttons
  document.querySelectorAll<HTMLElement>('[data-preset-days]').forEach((btn) => {
    btn.addEventListener('click', () => {
      const days   = parseInt(btn.dataset['presetDays'] ?? '7', 10)
      const prefix = btn.dataset['prefix'] ?? 'g'
      setEndTimePreset(days, `${prefix}-end-date`, `${prefix}-end-time`)
    })
  })

  function handleAttListClick(prefix: string, draftsRef: () => AttachmentDraft[], setDrafts: (d: AttachmentDraft[]) => void, listId: string) {
    return (e: Event) => {
      const target = e.target as HTMLElement
      const removeBtn = target.closest<HTMLElement>('.att-remove')
      if (removeBtn && removeBtn.dataset['prefix'] === prefix) {
        const idx = parseInt(removeBtn.dataset['idx'] ?? '0', 10)
        const synced = syncAttachmentsFromDom(prefix, draftsRef())
        synced.splice(idx, 1)
        setDrafts(synced)
        renderAttachmentList(listId, prefix, draftsRef())
      }
    }
  }

  // Global attachments
  document.getElementById('g-att-add')?.addEventListener('click', () => {
    state.globalAttachments = syncAttachmentsFromDom('g', state.globalAttachments)
    state.globalAttachments.push(defaultAttachment())
    renderAttachmentList('global-att-list', 'g', state.globalAttachments)
  })
  document.getElementById('global-att-list')?.addEventListener('click', handleAttListClick(
    'g',
    () => state.globalAttachments,
    (d) => { state.globalAttachments = d },
    'global-att-list',
  ))

  // User attachments
  document.getElementById('u-att-add')?.addEventListener('click', () => {
    state.userAttachments = syncAttachmentsFromDom('u', state.userAttachments)
    state.userAttachments.push(defaultAttachment())
    renderAttachmentList('user-att-list', 'u', state.userAttachments)
  })
  document.getElementById('user-att-list')?.addEventListener('click', handleAttListClick(
    'u',
    () => state.userAttachments,
    (d) => { state.userAttachments = d },
    'user-att-list',
  ))

  // Target user IDs
  document.getElementById('uid-add')?.addEventListener('click', () => {
    state.targetUserIds = syncUserIdsFromDom()
    state.targetUserIds.push('')
    renderTargetUserIds()
    attachUserIdListeners()
  })
  attachUserIdListeners()

  // Manage
  document.getElementById('m-load')?.addEventListener('click',   () => run(() => doLoadMails(true)))
  document.getElementById('m-set-et')?.addEventListener('click', () => run(() => doSetMailEndTime()))
  document.getElementById('m-expire')?.addEventListener('click', () => run(() => doExpireMail()))
  document.getElementById('m-delete')?.addEventListener('click', () => run(() => doDeleteMail()))
  document.getElementById('m-purge')?.addEventListener('click',  () => run(doPurgeExpired))

  // User mail lookup
  document.getElementById('m-lookup-user')?.addEventListener('click', () => run(doFetchUserMail))

  // Import panel toggles
  document.getElementById('g-import-toggle')?.addEventListener('click', () => {
    state.globalImportExpanded = !state.globalImportExpanded
    refreshMainPanel()
  })
  document.getElementById('u-import-toggle')?.addEventListener('click', () => {
    state.userImportExpanded = !state.userImportExpanded
    refreshMainPanel()
  })

  // Import apply buttons
  document.getElementById('g-import-apply')?.addEventListener('click', () => {
    state.globalImportText = (document.getElementById('g-import-textarea') as HTMLTextAreaElement | null)?.value ?? ''
    doApplyImport('g')
  })
  document.getElementById('u-import-apply')?.addEventListener('click', () => {
    state.userImportText = (document.getElementById('u-import-textarea') as HTMLTextAreaElement | null)?.value ?? ''
    doApplyImport('u')
  })

  // Pagination
  document.getElementById('pg-prev')?.addEventListener('click', () => {
    if (state.mailPage > 0) {
      state.mailPage--
      refreshMainPanel()
    }
  })
  document.getElementById('pg-next')?.addEventListener('click', () => {
    const total = state.mailList?.length ?? 0
    const pageCount = total === 0 ? 1 : Math.ceil(total / MAILS_PER_PAGE)
    if (state.mailPage < pageCount - 1) {
      state.mailPage++
      refreshMainPanel()
    }
  })

  // Global mail table click handler
  document.getElementById('mail-tbody')?.addEventListener('click', (e) => {
    const target    = e.target as HTMLElement
    const saveBtn   = target.closest<HTMLElement>('.save-mail')
    const addAttBtn = target.closest<HTMLElement>('.inline-att-add')
    const removeAttBtn = target.closest<HTMLElement>('.inline-att-remove')
    const addUidBtn = target.closest<HTMLElement>('.inline-uid-add')
    const removeUidBtn = target.closest<HTMLElement>('.inline-uid-remove')
    const expireBtn = target.closest<HTMLElement>('.expire-mail')
    const deleteBtn = target.closest<HTMLElement>('.del-mail')
    const setEtBtn  = target.closest<HTMLElement>('.set-et')
    const copyBtn   = target.closest<HTMLElement>('.copy-mail-json')

    if (copyBtn) {
      const mId    = copyBtn.dataset['mailId'] ?? ''
      const source = (copyBtn.dataset['source'] ?? 'global') as 'global' | 'user'
      run(() => doCopyMailAsJson(mId, source))
    }
    else if (saveBtn) run(() => doUpdateGlobalMail(saveBtn.dataset['mailId'] ?? '', saveBtn.dataset['idKey'] ?? ''))
    else if (addAttBtn) {
      const mId = addAttBtn.dataset['mailId'] ?? ''
      const idKey = addAttBtn.dataset['idKey'] ?? ''
      syncInlineAttachmentsFromDom(mId, idKey)
      state.inlineAttachments[mId].push(defaultAttachment())
      refreshMainPanel()
    }
    else if (removeAttBtn) {
      const mId = removeAttBtn.dataset['mailId'] ?? ''
      const idKey = removeAttBtn.dataset['idKey'] ?? ''
      const idx = parseInt(removeAttBtn.dataset['idx'] ?? '0', 10)
      syncInlineAttachmentsFromDom(mId, idKey)
      state.inlineAttachments[mId].splice(idx, 1)
      refreshMainPanel()
    }
    else if (addUidBtn) {
      const mId = addUidBtn.dataset['mailId'] ?? ''
      const idKey = addUidBtn.dataset['idKey'] ?? ''
      syncInlineTargetUserIdsFromDom(mId, idKey)
      state.inlineTargetUserIds[mId].push('')
      refreshMainPanel()
    }
    else if (removeUidBtn) {
      const mId = removeUidBtn.dataset['mailId'] ?? ''
      const idKey = removeUidBtn.dataset['idKey'] ?? ''
      const idx = parseInt(removeUidBtn.dataset['idx'] ?? '0', 10)
      syncInlineTargetUserIdsFromDom(mId, idKey)
      state.inlineTargetUserIds[mId].splice(idx, 1)
      refreshMainPanel()
    }
    else if (expireBtn) run(() => doExpireMail(expireBtn.dataset['mailId'] ?? ''))
    else if (deleteBtn) run(() => doDeleteMail(deleteBtn.dataset['mailId'] ?? ''))
    else if (setEtBtn)  run(() => doSetMailEndTime(setEtBtn.dataset['mailId'] ?? '', setEtBtn.dataset['idKey'] ?? ''))
  })

  // User mail tbody click (read-only, only Copy)
  document.getElementById('user-mail-tbody')?.addEventListener('click', (e) => {
    const target  = e.target as HTMLElement
    const copyBtn = target.closest<HTMLElement>('.copy-mail-json')
    if (copyBtn) {
      const mId    = copyBtn.dataset['mailId'] ?? ''
      const source = (copyBtn.dataset['source'] ?? 'user') as 'global' | 'user'
      run(() => doCopyMailAsJson(mId, source))
    }
  })
}

function attachUserIdListeners() {
  document.getElementById('target-user-ids')?.addEventListener('click', (e) => {
    const btn = (e.target as HTMLElement).closest<HTMLElement>('.uid-remove')
    if (!btn) return
    const idx = parseInt(btn.dataset['idx'] ?? '0', 10)
    state.targetUserIds = syncUserIdsFromDom()
    state.targetUserIds.splice(idx, 1)
    if (state.targetUserIds.length === 0) state.targetUserIds = ['']
    renderTargetUserIds()
    attachUserIdListeners()
  })
}

function attachAllListeners() {
  attachSidebarListeners()
  attachMainListeners()
}

// ─── Init ─────────────────────────────────────────────────────────────────────────
function init() {
  const saved = loadCredentials()
  state.projectId   = saved.projectId
  state.environment = saved.environment
  state.moduleName  = saved.moduleName || 'BackpackAdventuresModule'
  state.operatorId  = saved.operatorId
  // proxyToken always starts empty — operator must re-enter each session
  renderApp()
}

init()
