// src/modules/send-form.ts
// Unified send form replacing renderSendGlobalTab() + renderSendTargetedTab().
// §3.3 of the mail-UI redesign design doc.

import { renderScheduleEditor, readScheduleEditor, attachScheduleListeners } from './schedule-editor'
import type { ScheduleEditorState } from './schedule-editor'
import { mountAttachmentEditor } from './attachment-editor'
import type { AttachmentEditorHandle } from './attachment-editor'
import { mountImportPanel } from './json-import-dialog'
import type { ImportedDraft } from './json-import-dialog'
import type { ComboboxOption } from './asset-selector'
import { CATEGORY_OPTIONS } from '../types'
import type { AttachmentDraft } from '../types'

// ─── Types ────────────────────────────────────────────────────────────────────

export interface SendFormPayload {
  recipientMode: 'global' | 'targeted'
  subject:       string
  body:          string
  schedule:      ScheduleEditorState
  category:      number
  senderName:    string | null
  dedupKey:      string | null
  /** Non-empty only when recipientMode === 'targeted'. */
  targetUserIds: string[] | null
  attachments:   AttachmentDraft[]
}

export interface SendFormDeps {
  getEnv():          string
  isBusy():          boolean
  isConnected():     boolean
  currencyOptions:   ComboboxOption[]
  itemOptions:       ComboboxOption[]
  ticketOptions:     ComboboxOption[]
  onSend(payload: SendFormPayload): void
  onStatusMessage(msg: string, type: 'success' | 'error' | 'warning' | 'info'): void
}

export interface SendFormHandle {
  /** Clear form back to defaults (call after successful send). */
  reset():   void
  destroy(): void
}

// ─── Internal helpers ─────────────────────────────────────────────────────────

function _esc(s: unknown): string {
  return String(s ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

function _defaultAttachment(): AttachmentDraft {
  return { payoutAssetId: '', assetType: 'Currency', payoutAmount: 1, chance: 1, itemRows: [] }
}

// ─── Mount ────────────────────────────────────────────────────────────────────

interface _S {
  recipientMode: 'global' | 'targeted'
  subject:       string
  body:          string
  schedule:      ScheduleEditorState
  category:      number
  senderName:    string
  dedupKey:      string
  targetText:    string   // raw textarea, one UUID per line
}

export function mountSendForm(
  container: HTMLElement,
  deps: SendFormDeps,
): SendFormHandle {
  const s: _S = {
    recipientMode: 'global',
    subject:       '',
    body:          '',
    schedule:      { expiryMode: 'none', expiryDate: '', expiryTime: '' },
    category:      0,
    senderName:    '',
    dedupKey:      '',
    targetText:    '',
  }
  let _attachments: AttachmentDraft[]                    = [_defaultAttachment()]
  let _attHandle:    AttachmentEditorHandle | null       = null
  let _importHandle: { destroy(): void }         | null = null

  function _applyDraftFields(draft: ImportedDraft): void {
    s.subject = draft.title
    s.body    = draft.content
    if (draft.endTime) {
      const d   = new Date(draft.endTime)
      const pad = (n: number) => String(n).padStart(2, '0')
      s.schedule = {
        expiryMode: 'set',
        expiryDate: `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`,
        expiryTime: `${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}`,
      }
    } else {
      s.schedule = { expiryMode: 'none', expiryDate: '', expiryTime: '' }
    }
    _attachments = draft.attachments.length > 0 ? [...draft.attachments] : [_defaultAttachment()]
  }

  function render(): void {
    _attHandle?.destroy()
    _importHandle?.destroy()

    const dis     = deps.isBusy() || !deps.isConnected()
    const disAttr = dis ? 'disabled' : ''
    const env     = deps.getEnv()
    const isProd  = /prod/i.test(env)
    const envBanner = isProd
      ? `<div class="alert alert-warning" style="font-weight:bold">⚠ Sending to: production</div>`
      : `<div class="alert alert-info">Sending to: ${_esc(env || 'testing')}</div>`

    container.innerHTML = `
<div class="send-form">
  ${envBanner}
  <div class="card">
    <div class="form-group">
      <label class="section-label">Recipient</label>
      <div class="radio-group">
        <label><input type="radio" name="sf-recipient-mode" value="global"
          ${s.recipientMode === 'global' ? 'checked' : ''} ${disAttr}> All players (Global)</label>
        <label><input type="radio" name="sf-recipient-mode" value="targeted"
          ${s.recipientMode === 'targeted' ? 'checked' : ''} ${disAttr}> Specific players (Targeted)</label>
      </div>
    </div>
    <div id="sf-target-section" ${s.recipientMode !== 'targeted' ? 'hidden' : ''}>
      <div class="form-group">
        <label>Target User IDs <span style="color:var(--text-dim)">(one UUID per line)</span></label>
        <textarea id="sf-target-text" rows="4"
          placeholder="One player UUID per line" ${disAttr}>${_esc(s.targetText)}</textarea>
      </div>
    </div>
    <div class="form-group">
      <label>Subject <span style="color:var(--text-dim)">[1-128 chars]</span></label>
      <input type="text" id="sf-subject" value="${_esc(s.subject)}"
        maxlength="128" placeholder="Mail title" ${disAttr}/>
      <small id="sf-subject-count" style="color:var(--text-dim)">${s.subject.length}/128</small>
    </div>
    <div class="form-group">
      <label>Body <span style="color:var(--text-dim)">[1-1024 chars]</span></label>
      <textarea id="sf-body" maxlength="1024"
        placeholder="Mail body text" ${disAttr}>${_esc(s.body)}</textarea>
      <small id="sf-body-count" style="color:var(--text-dim)">${s.body.length}/1024</small>
    </div>
    ${renderScheduleEditor('sf', s.schedule, dis)}
    <div class="form-group">
      <label>Category</label>
      <select id="sf-category" ${disAttr}>
        ${CATEGORY_OPTIONS.map((c, i) =>
          `<option value="${i}" ${i === s.category ? 'selected' : ''}>${_esc(c)}</option>`
        ).join('')}
      </select>
    </div>
    <details>
      <summary style="cursor:pointer;margin:8px 0;font-size:13px;color:var(--text-dim)">▶ Advanced</summary>
      <div style="margin-top:8px">
        <div class="form-group">
          <label>Sender Name <span style="color:var(--text-dim)">(optional)</span></label>
          <input type="text" id="sf-sender" value="${_esc(s.senderName)}"
            placeholder="e.g. System" ${disAttr}/>
        </div>
        <div class="form-group">
          <label>Dedup Key <span style="color:var(--text-dim)">(optional)</span></label>
          <input type="text" id="sf-dedup" value="${_esc(s.dedupKey)}"
            placeholder="Unique key to prevent duplicate sends" ${disAttr}/>
        </div>
      </div>
    </details>
    <div class="card-title" style="margin-top:12px">📎 Attachments</div>
    <div id="sf-att-list"></div>
    <div class="btn-row" style="margin-top:16px">
      <button class="btn btn-primary" id="sf-send" ${disAttr}>
        ${deps.isBusy() ? '<span class="spinner"></span>' : ''}
        ${s.recipientMode === 'targeted' ? 'Send Targeted Mail' : 'Send Global Mail'}
      </button>
    </div>
  </div>
  <div id="sf-import-container"></div>
</div>`

    // ── Sub-components ──────────────────────────────────────────────────
    const attEl = container.querySelector<HTMLElement>('#sf-att-list')
    if (attEl) {
      _attHandle = mountAttachmentEditor(
        attEl,
        _attachments,
        {
          prefix:          'sf',
          currencyOptions: deps.currencyOptions,
          itemOptions:     deps.itemOptions,
          ticketOptions:   deps.ticketOptions,
          disabled:        dis,
        },
        (updated) => { _attachments = updated },
      )
    }

    const importEl = container.querySelector<HTMLElement>('#sf-import-container')
    if (importEl) {
      _importHandle = mountImportPanel(importEl, {
        prefix:      'sf',
        isConnected: deps.isConnected,
        currencyIds: deps.currencyOptions.map(o => o.id),
        itemIds:     deps.itemOptions.map(o => o.id),
        ticketIds:   deps.ticketOptions.map(o => o.id),
        onApply(draft, warnings) {
          _applyDraftFields(draft)
          render()
          deps.onStatusMessage(
            `Import applied: "${draft.title}"` +
            (warnings.length > 0 ? ` (${warnings.length} warning(s))` : ''),
            warnings.length > 0 ? 'warning' : 'success',
          )
        },
      })
    }

    // ── Event listeners ─────────────────────────────────────────────────
    attachScheduleListeners('sf', (sched) => { s.schedule = sched })

    container.querySelectorAll<HTMLInputElement>('input[name="sf-recipient-mode"]')
      .forEach(r => r.addEventListener('change', () => {
        s.recipientMode = r.value as 'global' | 'targeted'
        const sec = container.querySelector<HTMLElement>('#sf-target-section')
        if (sec) sec.hidden = s.recipientMode !== 'targeted'
      }))

    const subjectEl  = container.querySelector<HTMLInputElement>('#sf-subject')
    const subjectCnt = container.querySelector<HTMLElement>('#sf-subject-count')
    subjectEl?.addEventListener('input', () => {
      if (subjectCnt) subjectCnt.textContent = `${subjectEl.value.length}/128`
    })

    const bodyEl  = container.querySelector<HTMLTextAreaElement>('#sf-body')
    const bodyCnt = container.querySelector<HTMLElement>('#sf-body-count')
    bodyEl?.addEventListener('input', () => {
      if (bodyCnt) bodyCnt.textContent = `${bodyEl.value.length}/1024`
    })

    container.querySelector('#sf-send')?.addEventListener('click', () => {
      const subject  = subjectEl?.value?.trim() ?? ''
      const body     = bodyEl?.value?.trim() ?? ''
      const schedule = readScheduleEditor('sf')
      const catEl    = container.querySelector<HTMLSelectElement>('#sf-category')
      const category = parseInt(catEl?.value ?? '0', 10) || 0
      const senderName = (container.querySelector<HTMLInputElement>('#sf-sender'))?.value?.trim() || null
      const dedupKey   = (container.querySelector<HTMLInputElement>('#sf-dedup'))?.value?.trim() || null
      const attachments = _attHandle?.getDrafts() ?? _attachments

      let targetUserIds: string[] | null = null
      if (s.recipientMode === 'targeted') {
        const rawText = (container.querySelector<HTMLTextAreaElement>('#sf-target-text'))?.value ?? ''
        targetUserIds = rawText.split('\n').map(t => t.trim()).filter(Boolean)
      }

      deps.onSend({ recipientMode: s.recipientMode, subject, body, schedule, category, senderName, dedupKey, targetUserIds, attachments })
    })
  }

  render()

  return {
    reset() {
      s.recipientMode = 'global'
      s.subject       = ''
      s.body          = ''
      s.schedule      = { expiryMode: 'none', expiryDate: '', expiryTime: '' }
      s.category      = 0
      s.senderName    = ''
      s.dedupKey      = ''
      s.targetText    = ''
      _attachments    = [_defaultAttachment()]
      render()
    },
    destroy() {
      _attHandle?.destroy()
      _importHandle?.destroy()
      container.innerHTML = ''
    },
  }
}
