// src/modules/json-import-dialog.ts
// Reusable JSON import / paste panel (expandable collapsible).
// Used by both Send form (main.ts) and mail-editor-drawer.ts.

import { validateAndImport } from '../mail-import'
import type { ImportedDraft } from '../mail-import'

// ─── Types ────────────────────────────────────────────────────────────────────

export type { ImportedDraft }

export interface ImportPanelDeps {
  /** Unique prefix for element IDs — keep distinct per usage site */
  prefix:       string
  isConnected:  () => boolean
  currencyIds:  string[]
  itemIds:      string[]
  ticketIds:    string[]
  /**
   * Called once a valid draft is parsed.
   * The consumer applies it to form state and re-renders.
   */
  onApply(draft: ImportedDraft, warnings: string[]): void
}

export interface ImportPanelHandle {
  destroy(): void
}

// ─── Mount ────────────────────────────────────────────────────────────────────

const MAX_IMPORT_BYTES = 256 * 1024

export function mountImportPanel(
  container: HTMLElement,
  deps: ImportPanelDeps,
): ImportPanelHandle {
  const { prefix } = deps

  let expanded = false
  let text     = ''
  let errors:   string[] = []
  let warnings: string[] = []

  function render() {
    const dis = deps.isConnected() ? '' : 'disabled'
    const errorHtml = errors.length > 0
      ? `<div class="alert alert-error" style="margin-top:6px;font-size:12px">${errors.map(_esc).join('<br>')}</div>`
      : ''
    const warnHtml = warnings.length > 0
      ? `<div class="alert alert-warning" style="margin-top:4px;font-size:12px">${warnings.map(_esc).join('<br>')}</div>`
      : ''

    container.innerHTML = `
<div class="card" style="margin-top:8px">
  <div style="display:flex;align-items:center;gap:8px;cursor:pointer" id="${prefix}-import-toggle">
    <span style="font-size:13px;font-weight:bold">${expanded ? '▼' : '►'} 📋 Paste from JSON (import draft)</span>
  </div>
  ${expanded ? `
  <div style="margin-top:8px">
    <textarea id="${prefix}-import-textarea" rows="6"
      style="width:100%;font-size:11px;font-family:monospace;resize:vertical"
      placeholder='Paste exported mail JSON here ({"schemaVersion":1,"mail":{...}})'>${_esc(text)}</textarea>
    ${errorHtml}${warnHtml}
    <div class="btn-row" style="margin-top:6px">
      <button class="btn btn-secondary btn-sm" id="${prefix}-import-apply" ${dis}>
        Apply as Draft
      </button>
      <small style="color:var(--text-dim);font-size:11px">Import fills the form only — does NOT send automatically.</small>
    </div>
  </div>` : ''}
</div>`

    // Attach listeners after render
    document.getElementById(`${prefix}-import-toggle`)?.addEventListener('click', () => {
      // Preserve text before toggling
      if (expanded) {
        text = (document.getElementById(`${prefix}-import-textarea`) as HTMLTextAreaElement | null)?.value ?? text
      }
      expanded = !expanded
      render()
    })

    document.getElementById(`${prefix}-import-apply`)?.addEventListener('click', () => {
      const ta = document.getElementById(`${prefix}-import-textarea`) as HTMLTextAreaElement | null
      const raw = ta?.value?.trim() ?? ''
      text = raw

      if (raw.length > MAX_IMPORT_BYTES) {
        errors   = [`Import too large: ${raw.length} bytes (max ${MAX_IMPORT_BYTES})`]
        warnings = []
        render()
        return
      }

      let parsed: unknown
      try {
        parsed = JSON.parse(raw)
      } catch {
        errors   = ['Invalid JSON: ' + (raw.length < 100 ? raw : raw.substring(0, 100) + '…')]
        warnings = []
        render()
        return
      }

      const result = validateAndImport(
        parsed,
        deps.currencyIds,
        deps.itemIds,
        deps.ticketIds,
        raw,
        MAX_IMPORT_BYTES,
      )

      errors   = result.errors
      warnings = result.warnings

      if (!result.ok || !result.draft) {
        render()
        return
      }

      // Success — notify consumer, then collapse
      deps.onApply(result.draft, result.warnings)
      expanded = false
      text     = ''
      errors   = []
      warnings = []
      render()
    })
  }

  render()

  return {
    destroy() { container.innerHTML = '' },
  }
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

function _esc(s: string): string {
  return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
}
