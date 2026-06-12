// src/modules/schedule-editor.ts
// Reusable expiry date/time section used in the drawer and send form.

import { presetFromNow } from './date-format'

function _esc(s: string): string {
  return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
}

export interface ScheduleEditorState {
  expiryMode: 'none' | 'set'
  expiryDate: string
  expiryTime: string
}

/** Render the schedule-editor section HTML. No side effects. */
export function renderScheduleEditor(
  prefix: string,
  state: ScheduleEditorState,
  disabled = false,
): string {
  const none = state.expiryMode === 'none'
  const dis  = disabled ? 'disabled' : ''
  return `
<div class="schedule-editor" id="${prefix}-schedule">
  <label class="section-label">Expiry</label>
  <div class="radio-group">
    <label><input type="radio" name="${prefix}-expiry-mode" value="none" ${none ? 'checked' : ''} ${dis}> No expiry</label>
    <label><input type="radio" name="${prefix}-expiry-mode" value="set"  ${!none ? 'checked' : ''} ${dis}> Set UTC time</label>
  </div>
  <div id="${prefix}-schedule-inputs" ${none ? 'hidden' : ''} class="schedule-inputs">
    <div class="input-row">
      <div class="form-group">
        <label for="${prefix}-exp-date">Date (UTC)</label>
        <input type="date" id="${prefix}-exp-date" value="${_esc(state.expiryDate)}" ${dis} />
      </div>
      <div class="form-group">
        <label for="${prefix}-exp-time">Time (UTC)</label>
        <input type="time" id="${prefix}-exp-time" value="${_esc(state.expiryTime)}" ${dis} />
      </div>
    </div>
    <div class="btn-row quick-presets">
      <button type="button" class="btn btn-ghost btn-sm" data-action="preset-days" data-prefix="${prefix}" data-days="1" ${dis}>+1d</button>
      <button type="button" class="btn btn-ghost btn-sm" data-action="preset-days" data-prefix="${prefix}" data-days="7" ${dis}>+7d</button>
      <button type="button" class="btn btn-ghost btn-sm" data-action="preset-days" data-prefix="${prefix}" data-days="30" ${dis}>+30d</button>
      <button type="button" class="btn btn-ghost btn-sm" data-action="preset-none" data-prefix="${prefix}" ${dis}>✕ None</button>
    </div>
  </div>
</div>`
}

/** Read current editor values from the DOM. */
export function readScheduleEditor(prefix: string): ScheduleEditorState {
  const modeEl = document.querySelector<HTMLInputElement>(`input[name="${prefix}-expiry-mode"]:checked`)
  const expiryMode: 'none' | 'set' = modeEl?.value === 'set' ? 'set' : 'none'
  const dateEl = document.getElementById(`${prefix}-exp-date`) as HTMLInputElement | null
  const timeEl = document.getElementById(`${prefix}-exp-time`) as HTMLInputElement | null
  return {
    expiryMode,
    expiryDate: dateEl?.value ?? '',
    expiryTime: timeEl?.value ?? '',
  }
}

/** Wire event listeners for the schedule editor. Call after the HTML is in the DOM. */
export function attachScheduleListeners(
  prefix: string,
  onChange: (s: ScheduleEditorState) => void,
): void {
  const container = document.getElementById(`${prefix}-schedule`)
  if (!container) return

  // Radio toggles
  container.querySelectorAll<HTMLInputElement>(`input[name="${prefix}-expiry-mode"]`).forEach(r => {
    r.addEventListener('change', () => {
      const inputsDiv = document.getElementById(`${prefix}-schedule-inputs`)
      if (inputsDiv) inputsDiv.hidden = r.value === 'none'
      if (r.value === 'set') {
        const dateEl = document.getElementById(`${prefix}-exp-date`) as HTMLInputElement | null
        if (dateEl && !dateEl.value) {
          const p = presetFromNow(7)
          dateEl.value = p.date
          const timeEl = document.getElementById(`${prefix}-exp-time`) as HTMLInputElement | null
          if (timeEl) timeEl.value = p.time
        }
      }
      onChange(readScheduleEditor(prefix))
    })
  })

  // Preset/clear buttons (delegated)
  container.addEventListener('click', (e) => {
    const btn = (e.target as HTMLElement).closest<HTMLElement>('[data-action]')
    if (!btn || btn.dataset['prefix'] !== prefix) return
    const action = btn.dataset['action']

    if (action === 'preset-days') {
      const days   = parseInt(btn.dataset['days'] ?? '7', 10)
      const p      = presetFromNow(days)
      const dateEl = document.getElementById(`${prefix}-exp-date`) as HTMLInputElement | null
      const timeEl = document.getElementById(`${prefix}-exp-time`) as HTMLInputElement | null
      if (dateEl) dateEl.value = p.date
      if (timeEl) timeEl.value = p.time
      // Ensure "set" radio is checked
      const setRadio = document.querySelector<HTMLInputElement>(`input[name="${prefix}-expiry-mode"][value="set"]`)
      if (setRadio && !setRadio.checked) {
        setRadio.checked = true
        const inputsDiv = document.getElementById(`${prefix}-schedule-inputs`)
        if (inputsDiv) inputsDiv.hidden = false
      }
      onChange(readScheduleEditor(prefix))
    } else if (action === 'preset-none') {
      const noneRadio = document.querySelector<HTMLInputElement>(`input[name="${prefix}-expiry-mode"][value="none"]`)
      if (noneRadio) {
        noneRadio.checked = true
        const inputsDiv = document.getElementById(`${prefix}-schedule-inputs`)
        if (inputsDiv) inputsDiv.hidden = true
      }
      const dateEl = document.getElementById(`${prefix}-exp-date`) as HTMLInputElement | null
      const timeEl = document.getElementById(`${prefix}-exp-time`) as HTMLInputElement | null
      if (dateEl) dateEl.value = ''
      if (timeEl) timeEl.value = ''
      onChange(readScheduleEditor(prefix))
    }
  })

  // Input change events
  const dateEl = document.getElementById(`${prefix}-exp-date`)
  const timeEl = document.getElementById(`${prefix}-exp-time`)
  dateEl?.addEventListener('change', () => onChange(readScheduleEditor(prefix)))
  timeEl?.addEventListener('change', () => onChange(readScheduleEditor(prefix)))
}
