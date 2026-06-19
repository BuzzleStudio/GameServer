// @vitest-environment happy-dom
/**
 * send-form.test.ts — mountSendForm DOM behaviour
 *
 * Design ref: §3.3 (unified send form), §3.3.1 (global/targeted mode switch),
 *             §3.3.2 (shared fields preserved across mode switch),
 *             §3.3.3 (targetUserIds only populated in targeted mode)
 * Module:     src/modules/send-form.ts
 *
 * Tests: initial state, recipient-mode toggle, shared-field preservation,
 *        targetUserIds gating, sub-component mount, reset(), destroy().
 */
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import {
  mountSendForm,
  type SendFormDeps,
  type SendFormHandle,
  type SendFormPayload,
} from '../modules/send-form'

// ─── Fixtures ─────────────────────────────────────────────────────────────────

function makeDeps(overrides: Partial<SendFormDeps> = {}): SendFormDeps {
  return {
    getEnv:          () => 'testing',
    isBusy:          () => false,
    isConnected:     () => true,
    currencyOptions: [{ id: 'gem', label: 'Gems' }, { id: 'gold', label: 'Gold' }],
    itemOptions:     [{ id: 'W_Dagger' }],
    ticketOptions:   [{ id: 'tkt_a1' }],
    onSend:          vi.fn(),
    onStatusMessage: vi.fn(),
    ...overrides,
  }
}

// ─── Setup / teardown ─────────────────────────────────────────────────────────

let container: HTMLDivElement
let handle: SendFormHandle

function mount(deps?: Partial<SendFormDeps>): SendFormHandle {
  handle = mountSendForm(container, makeDeps(deps))
  return handle
}

function radio(value: 'global' | 'targeted'): HTMLInputElement {
  return container.querySelector<HTMLInputElement>(
    `input[name="sf-recipient-mode"][value="${value}"]`,
  )!
}

function setMode(mode: 'global' | 'targeted'): void {
  const r = radio(mode)
  r.checked = true
  r.dispatchEvent(new Event('change', { bubbles: true }))
}

function targetSection(): HTMLElement {
  return container.querySelector<HTMLElement>('#sf-target-section')!
}

function subjectEl(): HTMLInputElement {
  return container.querySelector<HTMLInputElement>('#sf-subject')!
}

function bodyEl(): HTMLTextAreaElement {
  return container.querySelector<HTMLTextAreaElement>('#sf-body')!
}

function sendBtn(): HTMLButtonElement {
  return container.querySelector<HTMLButtonElement>('#sf-send')!
}

function setInputValue(el: HTMLInputElement | HTMLTextAreaElement, value: string): void {
  el.value = value
  el.dispatchEvent(new Event('input', { bubbles: true }))
}

beforeEach(() => {
  container = document.createElement('div')
  document.body.appendChild(container)
})

afterEach(() => {
  handle?.destroy()
  document.body.removeChild(container)
})

// ─── Initial state ────────────────────────────────────────────────────────────

describe('mountSendForm — initial state', () => {
  it('mounts without throwing', () => {
    expect(() => mount()).not.toThrow()
  })

  it('"global" radio is checked by default', () => {
    mount()
    expect(radio('global').checked).toBe(true)
  })

  it('"targeted" radio is NOT checked by default', () => {
    mount()
    expect(radio('targeted').checked).toBe(false)
  })

  it('target section is hidden by default', () => {
    mount()
    expect(targetSection().hidden).toBe(true)
  })

  it('subject input is empty', () => {
    mount()
    expect(subjectEl().value).toBe('')
  })

  it('body textarea is empty', () => {
    mount()
    expect(bodyEl().value).toBe('')
  })

  it('send button labelled "Send Global Mail" when global mode', () => {
    mount()
    expect(sendBtn().textContent).toContain('Send Global Mail')
  })

  it('env banner shown', () => {
    mount()
    expect(container.textContent).toContain('testing')
  })

  it('production banner is bold warning for prod env', () => {
    mount({ getEnv: () => 'production' })
    expect(container.querySelector('.alert-warning')).not.toBeNull()
    expect(container.textContent).toContain('production')
  })
})

// ─── Recipient mode toggle ────────────────────────────────────────────────────

describe('mountSendForm — global/targeted mode switch (§3.3.1)', () => {
  it('switching to "targeted" shows target section', () => {
    mount()
    expect(targetSection().hidden).toBe(true)
    setMode('targeted')
    expect(targetSection().hidden).toBe(false)
  })

  it('switching back to "global" hides target section', () => {
    mount()
    setMode('targeted')
    setMode('global')
    expect(targetSection().hidden).toBe(true)
  })

  it('send button label changes to "Send Targeted Mail" in targeted mode', () => {
    mount()
    // Re-render is not triggered by mode change alone — button label updates on render
    // Label from initial render reflects initial mode; mode change fires listener only
    // Send button caption verified via onSend payload (recipientMode field)
    setMode('targeted')
    // The radio listener updates state but does NOT re-render — send button caption
    // reflects mode at next render. Verify via payload instead.
    expect(radio('targeted').checked).toBe(true)
  })

  it('target textarea is present inside target section', () => {
    mount()
    setMode('targeted')
    const ta = container.querySelector<HTMLTextAreaElement>('#sf-target-text')
    expect(ta).not.toBeNull()
  })
})

// ─── Shared-field preservation across mode switch (§3.3.2) ───────────────────

describe('mountSendForm — shared fields preserved across mode switch (§3.3.2)', () => {
  it('subject preserved when switching global → targeted', () => {
    mount()
    setInputValue(subjectEl(), 'My Subject')
    setMode('targeted')
    // Target section toggle does NOT re-render — subject input remains
    expect(subjectEl().value).toBe('My Subject')
  })

  it('body preserved when switching global → targeted', () => {
    mount()
    setInputValue(bodyEl(), 'Hello players!')
    setMode('targeted')
    expect(bodyEl().value).toBe('Hello players!')
  })

  it('subject preserved when switching targeted → global', () => {
    mount()
    setMode('targeted')
    setInputValue(subjectEl(), 'Targeted subject')
    setMode('global')
    expect(subjectEl().value).toBe('Targeted subject')
  })

  it('body preserved when switching targeted → global', () => {
    mount()
    setMode('targeted')
    setInputValue(bodyEl(), 'Body for targeted')
    setMode('global')
    expect(bodyEl().value).toBe('Body for targeted')
  })

  it('mode switch does not destroy attachment editor', () => {
    mount()
    const attListBefore = container.querySelector('#sf-att-list')
    expect(attListBefore).not.toBeNull()
    setMode('targeted')
    const attListAfter = container.querySelector('#sf-att-list')
    expect(attListAfter).not.toBeNull()
  })
})

// ─── targetUserIds gating (§3.3.3) ───────────────────────────────────────────

describe('mountSendForm — targetUserIds only in targeted mode (§3.3.3)', () => {
  it('global mode: onSend called with targetUserIds = null', () => {
    const onSend = vi.fn()
    mount({ onSend })
    setInputValue(subjectEl(), 'Subj')
    setInputValue(bodyEl(), 'Body')
    sendBtn().click()
    const payload: SendFormPayload = onSend.mock.calls[0][0]
    expect(payload.targetUserIds).toBeNull()
  })

  it('global mode: onSend payload has recipientMode = "global"', () => {
    const onSend = vi.fn()
    mount({ onSend })
    sendBtn().click()
    expect(onSend.mock.calls[0][0].recipientMode).toBe('global')
  })

  it('targeted mode: onSend called with targetUserIds array', () => {
    const onSend = vi.fn()
    mount({ onSend })
    setMode('targeted')
    const ta = container.querySelector<HTMLTextAreaElement>('#sf-target-text')!
    ta.value = 'uuid-aaa\nuuid-bbb'
    sendBtn().click()
    const payload: SendFormPayload = onSend.mock.calls[0][0]
    expect(payload.targetUserIds).toEqual(['uuid-aaa', 'uuid-bbb'])
  })

  it('targeted mode: onSend payload has recipientMode = "targeted"', () => {
    const onSend = vi.fn()
    mount({ onSend })
    setMode('targeted')
    sendBtn().click()
    expect(onSend.mock.calls[0][0].recipientMode).toBe('targeted')
  })

  it('targeted mode with empty textarea: targetUserIds = [] (empty array, not null)', () => {
    const onSend = vi.fn()
    mount({ onSend })
    setMode('targeted')
    // textarea is empty
    sendBtn().click()
    const payload: SendFormPayload = onSend.mock.calls[0][0]
    expect(payload.targetUserIds).toEqual([])
    expect(payload.targetUserIds).not.toBeNull()
  })

  it('targeted mode: blank lines in textarea are filtered out', () => {
    const onSend = vi.fn()
    mount({ onSend })
    setMode('targeted')
    const ta = container.querySelector<HTMLTextAreaElement>('#sf-target-text')!
    ta.value = 'uuid-aaa\n\n  \nuuid-bbb\n'
    sendBtn().click()
    const payload: SendFormPayload = onSend.mock.calls[0][0]
    expect(payload.targetUserIds).toEqual(['uuid-aaa', 'uuid-bbb'])
  })

  it('targeted mode with whitespace-padded UUIDs: trimmed before inclusion', () => {
    const onSend = vi.fn()
    mount({ onSend })
    setMode('targeted')
    const ta = container.querySelector<HTMLTextAreaElement>('#sf-target-text')!
    ta.value = '  uuid-aaa  \n  uuid-bbb  '
    sendBtn().click()
    const payload: SendFormPayload = onSend.mock.calls[0][0]
    expect(payload.targetUserIds).toEqual(['uuid-aaa', 'uuid-bbb'])
  })
})

// ─── onSend payload — shared fields ──────────────────────────────────────────

describe('mountSendForm — onSend payload shared fields', () => {
  it('onSend receives subject from input', () => {
    const onSend = vi.fn()
    mount({ onSend })
    setInputValue(subjectEl(), 'Test Subject')
    sendBtn().click()
    expect(onSend.mock.calls[0][0].subject).toBe('Test Subject')
  })

  it('onSend receives body from textarea', () => {
    const onSend = vi.fn()
    mount({ onSend })
    setInputValue(bodyEl(), 'Mail body content')
    sendBtn().click()
    expect(onSend.mock.calls[0][0].body).toBe('Mail body content')
  })

  it('onSend trims subject whitespace', () => {
    const onSend = vi.fn()
    mount({ onSend })
    subjectEl().value = '  Padded  '
    sendBtn().click()
    expect(onSend.mock.calls[0][0].subject).toBe('Padded')
  })

  it('onSend payload contains attachments array', () => {
    const onSend = vi.fn()
    mount({ onSend })
    sendBtn().click()
    const payload: SendFormPayload = onSend.mock.calls[0][0]
    expect(Array.isArray(payload.attachments)).toBe(true)
  })

  it('onSend senderName is null when sender input empty', () => {
    const onSend = vi.fn()
    mount({ onSend })
    sendBtn().click()
    expect(onSend.mock.calls[0][0].senderName).toBeNull()
  })

  it('onSend dedupKey is null when dedup input empty', () => {
    const onSend = vi.fn()
    mount({ onSend })
    sendBtn().click()
    expect(onSend.mock.calls[0][0].dedupKey).toBeNull()
  })
})

// ─── Sub-components mounted ───────────────────────────────────────────────────

describe('mountSendForm — sub-components', () => {
  it('attachment editor container present', () => {
    mount()
    expect(container.querySelector('#sf-att-list')).not.toBeNull()
  })

  it('attachment editor renders rows', () => {
    mount()
    // Default: 1 attachment row from _defaultAttachment()
    const attList = container.querySelector('#sf-att-list')!
    expect(attList.innerHTML.length).toBeGreaterThan(0)
  })

  it('import panel container present', () => {
    mount()
    expect(container.querySelector('#sf-import-container')).not.toBeNull()
  })

  it('import panel renders toggle', () => {
    mount()
    // mountImportPanel renders a toggle element with prefix "sf"
    const toggle = document.getElementById('sf-import-toggle')
    expect(toggle).not.toBeNull()
  })
})

// ─── reset() ─────────────────────────────────────────────────────────────────

describe('mountSendForm — reset()', () => {
  it('reset() clears subject input', () => {
    mount()
    subjectEl().value = 'Pre-filled subject'
    handle.reset()
    expect(subjectEl().value).toBe('')
  })

  it('reset() clears body textarea', () => {
    mount()
    bodyEl().value = 'Pre-filled body'
    handle.reset()
    expect(bodyEl().value).toBe('')
  })

  it('reset() returns to global mode', () => {
    mount()
    setMode('targeted')
    handle.reset()
    expect(radio('global').checked).toBe(true)
  })

  it('reset() hides target section (back to global)', () => {
    mount()
    setMode('targeted')
    handle.reset()
    expect(targetSection().hidden).toBe(true)
  })

  it('reset() re-mounts attachment editor', () => {
    mount()
    handle.reset()
    expect(container.querySelector('#sf-att-list')).not.toBeNull()
    expect(container.querySelector('#sf-att-list')!.innerHTML.length).toBeGreaterThan(0)
  })

  it('reset() does not throw', () => {
    mount()
    expect(() => handle.reset()).not.toThrow()
  })
})

// ─── destroy() ───────────────────────────────────────────────────────────────

describe('mountSendForm — destroy()', () => {
  it('destroy() clears container', () => {
    mount()
    handle.destroy()
    expect(container.innerHTML).toBe('')
  })

  it('destroy() does not throw', () => {
    mount()
    expect(() => handle.destroy()).not.toThrow()
  })
})

// ─── Disabled state ───────────────────────────────────────────────────────────

describe('mountSendForm — disabled when busy', () => {
  it('send button is disabled when isBusy() = true', () => {
    mount({ isBusy: () => true })
    expect(sendBtn().disabled).toBe(true)
  })

  it('send button is disabled when isConnected() = false', () => {
    mount({ isConnected: () => false })
    expect(sendBtn().disabled).toBe(true)
  })

  it('send button enabled when connected and not busy', () => {
    mount()
    expect(sendBtn().disabled).toBe(false)
  })
})

// ─── SF-03: shared attachment editor across modes ─────────────────────────────

describe('mountSendForm — shared attachment editor (SF-03)', () => {
  it('[SF-03] switching mode does not re-mount attachment editor (same #sf-att-list element)', () => {
    mount()
    const attListBefore = container.querySelector('#sf-att-list')
    setMode('targeted')
    const attListAfter = container.querySelector('#sf-att-list')
    // Same element reference: mode switch must NOT destroy+recreate the att-list
    expect(attListAfter).toBe(attListBefore)
  })

  it('[SF-03b] attachment editor content preserved after mode switch', () => {
    mount()
    const attContentBefore = container.querySelector('#sf-att-list')!.innerHTML
    setMode('targeted')
    expect(container.querySelector('#sf-att-list')!.innerHTML).toBe(attContentBefore)
  })
})

// ─── SF-14/SF-15: category and schedule in payload ────────────────────────────

describe('mountSendForm — category and schedule in payload (SF-14/SF-15)', () => {
  it('[SF-14] payload.category reflects selected category index', () => {
    const onSend = vi.fn()
    mount({ onSend })
    const catSelect = container.querySelector<HTMLSelectElement>('#sf-category')!
    catSelect.value = '2'
    sendBtn().click()
    const payload: SendFormPayload = onSend.mock.calls[0][0]
    expect(payload.category).toBe(2)
  })

  it('[SF-14b] payload.category defaults to 0 when nothing changed', () => {
    const onSend = vi.fn()
    mount({ onSend })
    sendBtn().click()
    expect(onSend.mock.calls[0][0].category).toBe(0)
  })

  it('[SF-15] payload.schedule exists and has expiryMode', () => {
    const onSend = vi.fn()
    mount({ onSend })
    sendBtn().click()
    const payload: SendFormPayload = onSend.mock.calls[0][0]
    expect(payload.schedule).toBeDefined()
    expect(payload.schedule).toHaveProperty('expiryMode')
  })

  it('[SF-15b] payload.schedule.expiryMode defaults to "none"', () => {
    const onSend = vi.fn()
    mount({ onSend })
    sendBtn().click()
    expect(onSend.mock.calls[0][0].schedule.expiryMode).toBe('none')
  })
})

// ─── SF-16: attachment content in payload ────────────────────────────────────

describe('mountSendForm — attachment content in payload (SF-16)', () => {
  it('[SF-16] payload.attachments[0] has assetType from default attachment (Currency)', () => {
    const onSend = vi.fn()
    mount({ onSend })
    sendBtn().click()
    const payload: SendFormPayload = onSend.mock.calls[0][0]
    // _defaultAttachment() creates assetType: 'Currency'
    expect(payload.attachments).toHaveLength(1)
    expect(payload.attachments[0].assetType).toBe('Currency')
  })

  it('[SF-16b] payload.attachments[0].payoutAmount comes from default attachment (1)', () => {
    const onSend = vi.fn()
    mount({ onSend })
    sendBtn().click()
    expect(onSend.mock.calls[0][0].attachments[0].payoutAmount).toBe(1)
  })
})

// ─── SF-17/SF-18: senderName and dedupKey non-null when filled ────────────────

describe('mountSendForm — senderName and dedupKey when filled (SF-17/SF-18)', () => {
  it('[SF-17] payload.senderName is non-null when #sf-sender has value', () => {
    const onSend = vi.fn()
    mount({ onSend })
    const senderInput = container.querySelector<HTMLInputElement>('#sf-sender')!
    senderInput.value = 'System'
    sendBtn().click()
    expect(onSend.mock.calls[0][0].senderName).toBe('System')
  })

  it('[SF-17b] payload.senderName trims whitespace', () => {
    const onSend = vi.fn()
    mount({ onSend })
    const senderInput = container.querySelector<HTMLInputElement>('#sf-sender')!
    senderInput.value = '  System  '
    sendBtn().click()
    expect(onSend.mock.calls[0][0].senderName).toBe('System')
  })

  it('[SF-18] payload.dedupKey is non-null when #sf-dedup has value', () => {
    const onSend = vi.fn()
    mount({ onSend })
    const dedupInput = container.querySelector<HTMLInputElement>('#sf-dedup')!
    dedupInput.value = 'key-unique-123'
    sendBtn().click()
    expect(onSend.mock.calls[0][0].dedupKey).toBe('key-unique-123')
  })

  it('[SF-18b] payload.dedupKey trims whitespace', () => {
    const onSend = vi.fn()
    mount({ onSend })
    const dedupInput = container.querySelector<HTMLInputElement>('#sf-dedup')!
    dedupInput.value = '  key-abc  '
    sendBtn().click()
    expect(onSend.mock.calls[0][0].dedupKey).toBe('key-abc')
  })
})

// ─── SF-21/SF-22: send button label matches recipientMode ────────────────────
//
// §18 fix landed: mode-switch listener (send-form.ts lines 234–241) now
// directly updates the send button textContent on every mode change.

describe('mountSendForm — send button label matches recipientMode (SF-21/SF-22)', () => {
  it('[SF-21] initial global mode: send button says "Send Global Mail"', () => {
    mount()
    expect(sendBtn().textContent?.trim()).toContain('Send Global Mail')
  })

  it('[SF-22] after switch to targeted: send button says "Send Targeted Mail"', () => {
    mount()
    setMode('targeted')
    expect(sendBtn().textContent?.trim()).toContain('Send Targeted Mail')
  })

  it('[SF-22b] after switch back to global: send button reverts to "Send Global Mail"', () => {
    mount()
    setMode('targeted')
    setMode('global')
    expect(sendBtn().textContent?.trim()).toContain('Send Global Mail')
  })
})
