// @vitest-environment happy-dom
/**
 * json-import-dialog.test.ts — mountImportPanel DOM behavior
 *
 * Design ref: §10 (JSON import / paste panel), §10.1 (toggle collapsed by default),
 *             §10.2 (invalid JSON shows error), §10.3 (size gate: 256KB max)
 * Module:     src/modules/json-import-dialog.ts
 *
 * Tests: collapsed by default, toggle expand/collapse, invalid JSON error,
 *        oversized input error, valid JSON calls onApply, panel isolation.
 *
 * Note: validateAndImport integration tested in mail-import.test.ts.
 * This file tests the panel DOM behavior only.
 */
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import {
  mountImportPanel,
  type ImportPanelDeps,
  type ImportPanelHandle,
} from '../modules/json-import-dialog'

// ─── Fixtures ─────────────────────────────────────────────────────────────────

function makeDeps(overrides: Partial<ImportPanelDeps> = {}): ImportPanelDeps {
  return {
    prefix:      'test',
    isConnected: () => true,
    currencyIds: ['gem', 'gold'],
    itemIds:     ['W_Dagger'],
    ticketIds:   ['tkt_a1'],
    onApply:     vi.fn(),
    ...overrides,
  }
}

// Minimal valid export envelope (schemaVersion 1)
const VALID_JSON = JSON.stringify({
  schemaVersion: 1,
  scope: 'Global',
  sourceEnv: 'production',
  sourceMailId: 'msg-src-001',
  exportedAt: '2025-01-01T00:00:00.000Z',
  mail: {
    title: 'Imported Mail',
    content: 'Body text here.',
    endTime: null,
    targetUserIds: [],
    attachments: [],
  },
})

// ─── Setup / teardown ─────────────────────────────────────────────────────────

let container: HTMLDivElement
let handle: ImportPanelHandle

beforeEach(() => {
  container = document.createElement('div')
  document.body.appendChild(container)
})

afterEach(() => {
  if (handle) { try { handle.destroy() } catch { /* */ } }
  document.body.removeChild(container)
})

// ─── Initial state (collapsed) ────────────────────────────────────────────────

describe('mountImportPanel — initial state', () => {
  it('renders without throwing', () => {
    expect(() => { handle = mountImportPanel(container, makeDeps()) }).not.toThrow()
  })

  it('renders a toggle element', () => {
    handle = mountImportPanel(container, makeDeps())
    const toggle = document.getElementById('test-import-toggle')
    expect(toggle).not.toBeNull()
  })

  it('collapsed by default — no textarea visible', () => {
    handle = mountImportPanel(container, makeDeps())
    const ta = document.getElementById('test-import-textarea')
    expect(ta).toBeNull()
  })

  it('toggle label contains "Paste from JSON"', () => {
    handle = mountImportPanel(container, makeDeps())
    const toggle = document.getElementById('test-import-toggle')!
    expect(toggle.textContent).toContain('Paste from JSON')
  })

  it('collapsed indicator shows ►', () => {
    handle = mountImportPanel(container, makeDeps())
    const toggle = document.getElementById('test-import-toggle')!
    expect(toggle.textContent).toContain('►')
  })
})

// ─── Toggle expand / collapse ─────────────────────────────────────────────────

describe('mountImportPanel — toggle expand/collapse', () => {
  it('clicking toggle once expands the panel (textarea appears)', () => {
    handle = mountImportPanel(container, makeDeps())
    document.getElementById('test-import-toggle')!.click()
    const ta = document.getElementById('test-import-textarea')
    expect(ta).not.toBeNull()
  })

  it('expanded indicator shows ▼', () => {
    handle = mountImportPanel(container, makeDeps())
    document.getElementById('test-import-toggle')!.click()
    const toggle = document.getElementById('test-import-toggle')!
    expect(toggle.textContent).toContain('▼')
  })

  it('clicking toggle twice collapses the panel again', () => {
    handle = mountImportPanel(container, makeDeps())
    document.getElementById('test-import-toggle')!.click()
    document.getElementById('test-import-toggle')!.click()
    const ta = document.getElementById('test-import-textarea')
    expect(ta).toBeNull()
  })

  it('Apply as Draft button visible when expanded', () => {
    handle = mountImportPanel(container, makeDeps())
    document.getElementById('test-import-toggle')!.click()
    const applyBtn = document.getElementById('test-import-apply')
    expect(applyBtn).not.toBeNull()
  })
})

// ─── Invalid JSON — error display ─────────────────────────────────────────────

describe('mountImportPanel — invalid JSON error', () => {
  it('pasting invalid JSON and clicking Apply shows error', () => {
    handle = mountImportPanel(container, makeDeps())
    document.getElementById('test-import-toggle')!.click()
    ;(document.getElementById('test-import-textarea') as HTMLTextAreaElement).value = 'not valid json'
    document.getElementById('test-import-apply')!.click()
    const errorDiv = container.querySelector('.alert-error')
    expect(errorDiv).not.toBeNull()
    expect(errorDiv!.textContent).toContain('Invalid JSON')
  })

  it('pasting invalid JSON does NOT call onApply', () => {
    const onApply = vi.fn()
    handle = mountImportPanel(container, makeDeps({ onApply }))
    document.getElementById('test-import-toggle')!.click()
    ;(document.getElementById('test-import-textarea') as HTMLTextAreaElement).value = '{bad json'
    document.getElementById('test-import-apply')!.click()
    expect(onApply).not.toHaveBeenCalled()
  })
})

// ─── Oversized input — size gate ─────────────────────────────────────────────

describe('mountImportPanel — size gate (256KB)', () => {
  it('input >256KB shows "Import too large" error', () => {
    handle = mountImportPanel(container, makeDeps())
    document.getElementById('test-import-toggle')!.click()
    // 256KB + 1 byte
    const huge = 'x'.repeat(256 * 1024 + 1)
    ;(document.getElementById('test-import-textarea') as HTMLTextAreaElement).value = huge
    document.getElementById('test-import-apply')!.click()
    const errorDiv = container.querySelector('.alert-error')
    expect(errorDiv).not.toBeNull()
    expect(errorDiv!.textContent).toContain('too large')
  })

  it('size error does NOT call onApply', () => {
    const onApply = vi.fn()
    handle = mountImportPanel(container, makeDeps({ onApply }))
    document.getElementById('test-import-toggle')!.click()
    const huge = 'x'.repeat(256 * 1024 + 1)
    ;(document.getElementById('test-import-textarea') as HTMLTextAreaElement).value = huge
    document.getElementById('test-import-apply')!.click()
    expect(onApply).not.toHaveBeenCalled()
  })
})

// ─── Valid JSON → onApply ─────────────────────────────────────────────────────

describe('mountImportPanel — valid JSON calls onApply', () => {
  it('valid envelope JSON calls onApply once', () => {
    const onApply = vi.fn()
    handle = mountImportPanel(container, makeDeps({ onApply }))
    document.getElementById('test-import-toggle')!.click()
    ;(document.getElementById('test-import-textarea') as HTMLTextAreaElement).value = VALID_JSON
    document.getElementById('test-import-apply')!.click()
    expect(onApply).toHaveBeenCalledOnce()
  })

  it('after successful import, panel collapses', () => {
    handle = mountImportPanel(container, makeDeps())
    document.getElementById('test-import-toggle')!.click()
    ;(document.getElementById('test-import-textarea') as HTMLTextAreaElement).value = VALID_JSON
    document.getElementById('test-import-apply')!.click()
    // Panel should be collapsed — textarea gone
    const ta = document.getElementById('test-import-textarea')
    expect(ta).toBeNull()
  })

  it('onApply receives draft object with title from JSON', () => {
    const onApply = vi.fn()
    handle = mountImportPanel(container, makeDeps({ onApply }))
    document.getElementById('test-import-toggle')!.click()
    ;(document.getElementById('test-import-textarea') as HTMLTextAreaElement).value = VALID_JSON
    document.getElementById('test-import-apply')!.click()
    const [draft] = onApply.mock.calls[0]
    expect(draft.title).toBe('Imported Mail')
  })
})

// ─── Connected state ──────────────────────────────────────────────────────────

describe('mountImportPanel — disconnected state', () => {
  it('when disconnected, Apply button is disabled', () => {
    handle = mountImportPanel(container, makeDeps({ isConnected: () => false }))
    document.getElementById('test-import-toggle')!.click()
    const applyBtn = document.getElementById('test-import-apply') as HTMLButtonElement
    expect(applyBtn.disabled).toBe(true)
  })
})

// ─── destroy() ────────────────────────────────────────────────────────────────

describe('mountImportPanel — destroy()', () => {
  it('destroy() clears container', () => {
    handle = mountImportPanel(container, makeDeps())
    handle.destroy()
    expect(container.innerHTML).toBe('')
  })
})
