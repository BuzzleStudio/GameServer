// src/modules/asset-selector.ts — Searchable combobox component (P0).
// Pure logic exported for testing; DOM mount in full impl (Step 3).

// ─── Types ────────────────────────────────────────────────────────────────────

export interface ComboboxOption {
  id:     string      // stored value (e.g. "gem", "W_Dagger")
  label?: string      // display name if different (e.g. localization key or human name)
}

export interface ComboboxConfig {
  containerId:   string            // ID of the wrapper <div> to mount into
  inputId:       string            // ID to assign the <input>
  options:       ComboboxOption[]  // full list of valid choices
  initialValue:  string            // pre-populated value
  placeholder?:  string
  allowUnknown:  boolean           // true = free text entry allowed; shows ⚠ badge
  onChange:      (value: string) => void
}

export interface ComboboxHandle {
  getValue(): string
  setValue(v: string): void
  destroy(): void    // removes DOM and all listeners
}

// ─── Pure filter logic (exported for testing) ─────────────────────────────────

/**
 * Filter combobox options by query string.
 * Matches against both `id` and `label` (case-insensitive substring).
 * Empty query returns all options unchanged.
 */
export function filterOptions(
  options: ComboboxOption[],
  query: string,
): ComboboxOption[] {
  const q = query.trim().toLowerCase()
  if (!q) return options
  return options.filter(
    o =>
      o.id.toLowerCase().includes(q) ||
      (o.label?.toLowerCase().includes(q) ?? false),
  )
}

// ─── DOM component (full implementation in Step 3) ───────────────────────────

/**
 * Mount a searchable combobox into a container element.
 * Full keyboard-nav + ARIA implementation — see §4 of design doc.
 */
export function mountCombobox(config: ComboboxConfig): ComboboxHandle {
  const container = document.getElementById(config.containerId)
  if (!container) throw new Error(`mountCombobox: container #${config.containerId} not found`)

  let currentValue = config.initialValue

  // Build DOM
  container.innerHTML = _comboboxHtml(config)
  const input = document.getElementById(config.inputId) as HTMLInputElement
  const listbox = document.getElementById(`${config.inputId}-listbox`) as HTMLUListElement
  const badgeUnknown = container.querySelector<HTMLElement>('.combobox-badge-unknown')!
  const clearBtn = container.querySelector<HTMLButtonElement>('.combobox-clear')!
  const toggleBtn = container.querySelector<HTMLButtonElement>('.combobox-toggle')!

  let isOpen = false
  let activeIndex = -1
  let filteredOptions: ComboboxOption[] = config.options

  function updateBadge(): void {
    const known = config.options.some(o => o.id === currentValue)
    badgeUnknown.hidden = !config.allowUnknown || !currentValue || known
    clearBtn.hidden = !currentValue
  }

  function renderOptions(opts: ComboboxOption[]): void {
    filteredOptions = opts
    activeIndex = -1
    listbox.innerHTML = ''
    if (opts.length === 0) {
      const li = document.createElement('li')
      li.className = 'combobox-noresults'
      li.setAttribute('role', 'option')
      li.setAttribute('aria-disabled', 'true')
      li.textContent = 'No matches'
      listbox.appendChild(li)
    } else {
      opts.forEach((opt, i) => {
        const li = document.createElement('li')
        li.className = 'combobox-option'
        li.setAttribute('role', 'option')
        li.setAttribute('data-value', opt.id)
        li.id = `${config.inputId}-opt-${i}`
        if (opt.label) {
          li.innerHTML = `<span class="combobox-option-label">${_esc(opt.label)}</span> <span class="combobox-option-id">${_esc(opt.id)}</span>`
        } else {
          li.innerHTML = `<span class="combobox-option-id">${_esc(opt.id)}</span>`
        }
        if (opt.id === currentValue) li.setAttribute('aria-selected', 'true')
        li.addEventListener('mousedown', (e) => {
          e.preventDefault()
          selectOption(opt.id)
        })
        listbox.appendChild(li)
      })
    }
  }

  function openList(filterQuery?: string): void {
    const q = filterQuery ?? input.value
    renderOptions(filterOptions(config.options, q))
    listbox.hidden = false
    input.setAttribute('aria-expanded', 'true')
    isOpen = true
  }

  function closeList(commit = true): void {
    listbox.hidden = true
    input.setAttribute('aria-expanded', 'false')
    input.removeAttribute('aria-activedescendant')
    isOpen = false
    activeIndex = -1
    if (commit) {
      currentValue = input.value
      updateBadge()
      config.onChange(currentValue)
    }
  }

  function selectOption(id: string): void {
    currentValue = id
    input.value = id
    closeList(false)
    updateBadge()
    config.onChange(id)
    input.focus()
  }

  function setActive(idx: number): void {
    const items = listbox.querySelectorAll<HTMLElement>('.combobox-option')
    items.forEach((li, i) => li.classList.toggle('combobox-active', i === idx))
    if (idx >= 0 && items[idx]) {
      input.setAttribute('aria-activedescendant', items[idx].id)
      items[idx].scrollIntoView({ block: 'nearest' })
    } else {
      input.removeAttribute('aria-activedescendant')
    }
    activeIndex = idx
  }

  // Event listeners
  input.addEventListener('focus', () => { if (!isOpen) openList('') })

  input.addEventListener('input', () => {
    openList(input.value)
  })

  input.addEventListener('keydown', (e) => {
    const items = listbox.querySelectorAll<HTMLElement>('.combobox-option')
    const count = items.length
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      if (!isOpen) { openList(); return }
      setActive(activeIndex < count - 1 ? activeIndex + 1 : 0)
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      if (!isOpen) { openList(); return }
      setActive(activeIndex > 0 ? activeIndex - 1 : count - 1)
    } else if (e.key === 'Enter') {
      if (isOpen && activeIndex >= 0 && items[activeIndex]) {
        e.preventDefault()
        const val = items[activeIndex].getAttribute('data-value')
        if (val) selectOption(val)
      }
    } else if (e.key === 'Escape') {
      if (isOpen) { e.preventDefault(); input.value = currentValue; closeList(false) }
    } else if (e.key === 'Tab') {
      if (isOpen) closeList(true)
    }
  })

  toggleBtn.addEventListener('mousedown', (e) => {
    e.preventDefault()
    if (isOpen) { closeList(true) } else { input.focus(); openList() }
  })

  clearBtn.addEventListener('mousedown', (e) => {
    e.preventDefault()
    currentValue = ''
    input.value = ''
    updateBadge()
    config.onChange('')
    input.focus()
    openList('')
  })

  document.addEventListener('mousedown', (e) => {
    if (isOpen && !container.contains(e.target as Node)) closeList(true)
  })

  // Initial state
  updateBadge()

  return {
    getValue: () => currentValue,
    setValue: (v) => {
      currentValue = v
      input.value = v
      updateBadge()
    },
    destroy: () => { container.innerHTML = '' },
  }
}

// ─── Internal helpers ─────────────────────────────────────────────────────────

function _esc(s: string): string {
  return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
}

function _comboboxHtml(config: ComboboxConfig): string {
  const val = _esc(config.initialValue)
  const ph  = _esc(config.placeholder ?? '')
  return `<div class="combobox-input-wrap">
  <input
    type="text"
    id="${config.inputId}"
    class="combobox-input"
    autocomplete="off"
    role="combobox"
    aria-expanded="false"
    aria-haspopup="listbox"
    aria-autocomplete="list"
    aria-controls="${config.inputId}-listbox"
    value="${val}"
    placeholder="${ph}"
  />
  <span class="combobox-badge-unknown" hidden title="Unknown ID — not in known list">⚠</span>
  <button class="combobox-clear btn-icon" aria-label="Clear value" hidden>✕</button>
  <button class="combobox-toggle btn-icon" aria-label="Show all options" tabindex="-1">▾</button>
</div>
<ul
  class="combobox-listbox"
  id="${config.inputId}-listbox"
  role="listbox"
  hidden
  aria-label="Options"
></ul>`
}
