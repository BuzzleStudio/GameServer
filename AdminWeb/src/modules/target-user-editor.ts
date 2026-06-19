// src/modules/target-user-editor.ts
// Target-user textarea with dedup stats.

import { validateTargetUserIds } from './validation'

function _esc(s: string): string {
  return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
}

export interface TargetUserEditorState {
  targetMode: 'all' | 'specific'
  targetText: string
}

/** Render the target-user section HTML. No side effects. */
export function renderTargetUserEditor(
  prefix: string,
  state: TargetUserEditorState,
  disabled = false,
): string {
  const isAll = state.targetMode === 'all'
  const dis   = disabled ? 'disabled' : ''
  const stats = !isAll && state.targetText.trim() ? _statsHtml(state.targetText) : ''
  return `
<div class="target-user-editor" id="${prefix}-target">
  <label class="section-label">Target Users</label>
  <div class="radio-group">
    <label><input type="radio" name="${prefix}-target-mode" value="all"      ${isAll ? 'checked' : ''} ${dis}> All players (global)</label>
    <label><input type="radio" name="${prefix}-target-mode" value="specific" ${!isAll ? 'checked' : ''} ${dis}> Specific players</label>
  </div>
  <div id="${prefix}-target-specific" ${isAll ? 'hidden' : ''}>
    <textarea id="${prefix}-target-text" rows="4"
      placeholder="One player UUID per line"
      ${dis}>${_esc(state.targetText)}</textarea>
    <div class="target-user-stats" id="${prefix}-target-stats">${stats}</div>
    <div class="btn-row">
      <button type="button" class="btn btn-ghost btn-sm" data-action="copy-uids" data-prefix="${prefix}" ${dis}>📋 Copy</button>
      <button type="button" class="btn btn-ghost btn-sm" data-action="clear-uids" data-prefix="${prefix}" ${dis}>✕ Clear</button>
    </div>
  </div>
</div>`
}

/** Read current editor values from the DOM. */
export function readTargetUserEditor(prefix: string): TargetUserEditorState {
  const modeEl = document.querySelector<HTMLInputElement>(`input[name="${prefix}-target-mode"]:checked`)
  const targetMode: 'all' | 'specific' = modeEl?.value === 'specific' ? 'specific' : 'all'
  const textEl = document.getElementById(`${prefix}-target-text`) as HTMLTextAreaElement | null
  return { targetMode, targetText: textEl?.value ?? '' }
}

/** Wire event listeners. Call after HTML is in the DOM. */
export function attachTargetUserListeners(
  prefix: string,
  onChange: (s: TargetUserEditorState) => void,
): void {
  const container = document.getElementById(`${prefix}-target`)
  if (!container) return

  // Radio toggles
  container.querySelectorAll<HTMLInputElement>(`input[name="${prefix}-target-mode"]`).forEach(r => {
    r.addEventListener('change', () => {
      const specific = document.getElementById(`${prefix}-target-specific`)
      if (specific) specific.hidden = r.value === 'all'
      onChange(readTargetUserEditor(prefix))
    })
  })

  // Textarea input
  const textarea = document.getElementById(`${prefix}-target-text`) as HTMLTextAreaElement | null
  if (textarea) {
    textarea.addEventListener('input', () => {
      const stats = document.getElementById(`${prefix}-target-stats`)
      if (stats) stats.innerHTML = textarea.value.trim() ? _statsHtml(textarea.value) : ''
      onChange(readTargetUserEditor(prefix))
    })
  }

  // Copy / Clear buttons (delegated)
  container.addEventListener('click', (e) => {
    const btn = (e.target as HTMLElement).closest<HTMLElement>('[data-action]')
    if (!btn || btn.dataset['prefix'] !== prefix) return
    const ta = document.getElementById(`${prefix}-target-text`) as HTMLTextAreaElement | null

    if (btn.dataset['action'] === 'copy-uids' && ta) {
      navigator.clipboard.writeText(ta.value).catch(() => {})
    } else if (btn.dataset['action'] === 'clear-uids' && ta) {
      ta.value = ''
      const stats = document.getElementById(`${prefix}-target-stats`)
      if (stats) stats.innerHTML = ''
      onChange(readTargetUserEditor(prefix))
    }
  })
}

// ─── Internal helpers ─────────────────────────────────────────────────────────────

function _statsHtml(text: string): string {
  const { valid, duplicates } = validateTargetUserIds(text)
  const parts: string[] = [
    `<span class="tus-valid">${valid.length} user${valid.length !== 1 ? 's' : ''}</span>`,
  ]
  if (duplicates.length > 0) {
    parts.push(
      `<span class="tus-dup">${duplicates.length} duplicate${duplicates.length !== 1 ? 's' : ''} removed</span>`,
    )
  }
  return parts.join(' · ')
}
