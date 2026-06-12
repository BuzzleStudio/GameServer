// @vitest-environment happy-dom
/**
 * drawer.test.ts — createMailEditorDrawer DOM behavior
 *
 * Design ref: §3 (drawer), §3.2 (width: min(640px,100vw)), §3.3 (ARIA dialog)
 * Module:     src/modules/mail-editor-drawer.ts
 *
 * Tests: open/close state, ARIA attributes, body class, isOpen(), destroy(),
 * edit buffer independence, drawer element structure.
 */
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import {
  createMailEditorDrawer,
  type DrawerDeps,
  type MailEditorDrawerHandle,
} from '../modules/mail-editor-drawer'
import type { MailRecord } from '../types'

// ─── Fixtures ─────────────────────────────────────────────────────────────────

const MAIL_A: MailRecord = {
  MessageId: 'msg-aaa-001',
  MailInfo: {
    Title: 'Hello World',
    Content: 'Mail body here.',
    Attachment: [],
  },
}

const MAIL_B: MailRecord = {
  MessageId: 'msg-bbb-002',
  MailInfo: {
    Title: 'Second Mail',
    Content: 'Another body.',
    Attachment: [],
  },
}

const MAIL_WITH_EXPIRY: MailRecord = {
  MessageId: 'msg-exp-003',
  MailInfo: {
    Title: 'Expiring Mail',
    Content: 'Expires soon.',
    ExpireTime: '2099-12-31T23:59:00.000Z',
    Attachment: [],
  },
}

function makeDeps(overrides: Partial<DrawerDeps> = {}): DrawerDeps {
  return {
    getEnv:          () => 'production',
    isBusy:          () => false,
    isConnected:     () => true,
    currencyOptions: [{ id: 'gem', label: 'Gems' }, { id: 'gold', label: 'Gold' }],
    itemOptions:     [{ id: 'W_Dagger' }],
    ticketOptions:   [{ id: 'tkt_a1' }],
    onSave:          vi.fn(),
    onExpire:        vi.fn(),
    onDelete:        vi.fn(),
    onCopyJson:      vi.fn(),
    ...overrides,
  }
}

// ─── Setup / teardown ─────────────────────────────────────────────────────────

let drawer: MailEditorDrawerHandle

beforeEach(() => {
  // Each test gets a fresh drawer
  drawer = createMailEditorDrawer(makeDeps())
})

afterEach(() => {
  drawer.destroy()
  // Confirm body is clean after destroy
  document.body.innerHTML = ''
})

// ─── Initial state ────────────────────────────────────────────────────────────

describe('createMailEditorDrawer — initial state', () => {
  it('isOpen() returns false before open() is called', () => {
    expect(drawer.isOpen()).toBe(false)
  })

  it('drawer element is appended to document.body', () => {
    const el = document.getElementById('mail-drawer')
    expect(el).not.toBeNull()
  })

  it('drawer element is hidden initially', () => {
    const el = document.getElementById('mail-drawer') as HTMLElement
    expect(el.hidden).toBe(true)
  })

  it('backdrop is hidden initially', () => {
    const backdrop = document.querySelector('.drawer-backdrop') as HTMLElement
    expect(backdrop).not.toBeNull()
    expect(backdrop.hidden).toBe(true)
  })
})

// ─── open() ───────────────────────────────────────────────────────────────────

describe('createMailEditorDrawer — open()', () => {
  it('isOpen() returns true after open()', () => {
    drawer.open(MAIL_A)
    expect(drawer.isOpen()).toBe(true)
  })

  it('drawer element becomes visible after open()', () => {
    drawer.open(MAIL_A)
    const el = document.getElementById('mail-drawer') as HTMLElement
    expect(el.hidden).toBe(false)
  })

  it('backdrop becomes visible after open()', () => {
    drawer.open(MAIL_A)
    const backdrop = document.querySelector('.drawer-backdrop') as HTMLElement
    expect(backdrop.hidden).toBe(false)
  })

  it('document.body gets drawer-open class after open()', () => {
    drawer.open(MAIL_A)
    expect(document.body.classList.contains('drawer-open')).toBe(true)
  })

  it('drawer-title contains the mail ID', () => {
    drawer.open(MAIL_A)
    const title = document.querySelector('.drawer-title')!
    expect(title.textContent).toContain('msg-aaa-001')
  })

  it('subject input pre-populated with mail title', () => {
    drawer.open(MAIL_A)
    const subj = document.getElementById('drawer-subject') as HTMLInputElement
    expect(subj).not.toBeNull()
    expect(subj.value).toBe('Hello World')
  })

  it('body textarea pre-populated with mail content', () => {
    drawer.open(MAIL_A)
    const body = document.getElementById('drawer-body-text') as HTMLTextAreaElement
    expect(body).not.toBeNull()
    expect(body.value).toBe('Mail body here.')
  })

  it('opening second mail replaces first mail content (edit buffer independence)', () => {
    drawer.open(MAIL_A)
    expect((document.getElementById('drawer-subject') as HTMLInputElement).value).toBe('Hello World')
    drawer.open(MAIL_B)
    expect((document.getElementById('drawer-subject') as HTMLInputElement).value).toBe('Second Mail')
  })
})

// ─── ARIA attributes (§3.3) ───────────────────────────────────────────────────

describe('createMailEditorDrawer — ARIA (§3.3)', () => {
  it('drawer aside has role="dialog"', () => {
    const el = document.getElementById('mail-drawer')!
    expect(el.getAttribute('role')).toBe('dialog')
  })

  it('drawer aside has aria-modal="true"', () => {
    const el = document.getElementById('mail-drawer')!
    expect(el.getAttribute('aria-modal')).toBe('true')
  })

  it('drawer aside has aria-label="Mail editor"', () => {
    const el = document.getElementById('mail-drawer')!
    expect(el.getAttribute('aria-label')).toBe('Mail editor')
  })

  it('close button has aria-label="Close drawer"', () => {
    drawer.open(MAIL_A)
    const closeBtn = document.getElementById('drawer-close-btn')!
    expect(closeBtn.getAttribute('aria-label')).toBe('Close drawer')
  })
})

// ─── close() ─────────────────────────────────────────────────────────────────

describe('createMailEditorDrawer — close()', () => {
  it('isOpen() returns false after close()', () => {
    drawer.open(MAIL_A)
    drawer.close()
    expect(drawer.isOpen()).toBe(false)
  })

  it('drawer element is hidden after close()', () => {
    drawer.open(MAIL_A)
    drawer.close()
    const el = document.getElementById('mail-drawer') as HTMLElement
    expect(el.hidden).toBe(true)
  })

  it('backdrop is hidden after close()', () => {
    drawer.open(MAIL_A)
    drawer.close()
    const backdrop = document.querySelector('.drawer-backdrop') as HTMLElement
    expect(backdrop.hidden).toBe(true)
  })

  it('drawer-open class removed from body after close()', () => {
    drawer.open(MAIL_A)
    drawer.close()
    expect(document.body.classList.contains('drawer-open')).toBe(false)
  })

  it('close via close-btn click hides drawer', () => {
    drawer.open(MAIL_A)
    const closeBtn = document.getElementById('drawer-close-btn') as HTMLButtonElement
    closeBtn.click()
    expect(drawer.isOpen()).toBe(false)
  })
})

// ─── destroy() ───────────────────────────────────────────────────────────────

describe('createMailEditorDrawer — destroy()', () => {
  it('destroy() removes the drawer element from DOM', () => {
    drawer.destroy()
    expect(document.getElementById('mail-drawer')).toBeNull()
    // prevent afterEach from double-destroying
    drawer = createMailEditorDrawer(makeDeps())
  })

  it('destroy() removes backdrop from DOM', () => {
    drawer.destroy()
    expect(document.querySelector('.drawer-backdrop')).toBeNull()
    drawer = createMailEditorDrawer(makeDeps())
  })

  it('destroy() sets isOpen() to false', () => {
    drawer.open(MAIL_A)
    drawer.destroy()
    expect(drawer.isOpen()).toBe(false)
    drawer = createMailEditorDrawer(makeDeps())
  })
})

// ─── Callbacks ────────────────────────────────────────────────────────────────

describe('createMailEditorDrawer — action callbacks', () => {
  it('Copy JSON button triggers onCopyJson with mail ID', () => {
    const onCopyJson = vi.fn()
    const d = createMailEditorDrawer(makeDeps({ onCopyJson }))
    d.open(MAIL_A)
    const btn = document.getElementById('drawer-copy-json') as HTMLButtonElement
    btn.click()
    expect(onCopyJson).toHaveBeenCalledWith('msg-aaa-001')
    d.destroy()
    document.body.innerHTML = ''
  })

  it('Save button with valid subject/body calls onSave', () => {
    const onSave = vi.fn()
    const d = createMailEditorDrawer(makeDeps({ onSave }))
    d.open(MAIL_A)
    ;(document.getElementById('drawer-subject') as HTMLInputElement).value = 'Valid Subject'
    ;(document.getElementById('drawer-body-text') as HTMLTextAreaElement).value = 'Valid body text.'
    document.getElementById('drawer-save')!.click()
    expect(onSave).toHaveBeenCalledOnce()
    const [mId, subj, body] = (onSave as ReturnType<typeof vi.fn>).mock.calls[0]
    expect(mId).toBe('msg-aaa-001')
    expect(subj).toBe('Valid Subject')
    expect(body).toBe('Valid body text.')
    d.destroy()
    document.body.innerHTML = ''
  })

  it('Save button with empty subject shows status error, does NOT call onSave', () => {
    const onSave = vi.fn()
    const d = createMailEditorDrawer(makeDeps({ onSave }))
    d.open(MAIL_A)
    ;(document.getElementById('drawer-subject') as HTMLInputElement).value = ''
    document.getElementById('drawer-save')!.click()
    expect(onSave).not.toHaveBeenCalled()
    const status = document.getElementById('drawer-status')!
    expect(status.textContent).toContain('Subject')
    d.destroy()
    document.body.innerHTML = ''
  })
})

// ─── Expiry (byte-compat) ─────────────────────────────────────────────────────

describe('createMailEditorDrawer — expiry display', () => {
  it('drawer with ExpireTime shows date/time inputs with correct values', () => {
    drawer.open(MAIL_WITH_EXPIRY)
    const dateEl = document.getElementById('drawer-exp-date') as HTMLInputElement
    const timeEl = document.getElementById('drawer-exp-time') as HTMLInputElement
    expect(dateEl).not.toBeNull()
    // isoToEditInputs('2099-12-31T23:59:00.000Z') → date='2099-12-31', time='23:59'
    expect(dateEl.value).toBe('2099-12-31')
    expect(timeEl.value).toBe('23:59')
  })

  it('drawer with no ExpireTime shows "none" expiry mode', () => {
    drawer.open(MAIL_A)
    const noneRadio = document.querySelector<HTMLInputElement>('input[name="drawer-expiry-mode"][value="none"]')
    expect(noneRadio?.checked).toBe(true)
  })
})

// ─── Status badge ─────────────────────────────────────────────────────────────

describe('createMailEditorDrawer — status badge', () => {
  it('drawer renders a status badge element', () => {
    drawer.open(MAIL_A)
    const badge = document.querySelector('.status-badge')
    expect(badge).not.toBeNull()
  })

  it('mail with no expiry shows "No expiry" badge', () => {
    drawer.open(MAIL_A)
    const badge = document.querySelector('.status-badge')!
    expect(badge.textContent).toContain('No expiry')
  })
})

// ─── SR-50/SR-51: _legacyWarning via _attInfoToDraft in drawer path ───────────
//
// _legacyWarning is set by _attInfoToDraft (mail-editor-drawer.ts:350–358) when
// JSON.parse(payoutAssetId) fails for a Ticket/ISA type (plain-string legacy format).
// mail-import.ts does NOT and SHOULD NOT set this flag (FROZEN by design).
// Flag surfaces as <span class="att-legacy-warn"> inside the attachment-editor.
//
// Design ref: §6.3 (Ticket/ISA as JSON object; plain-string = legacy)

describe('createMailEditorDrawer — _legacyWarning via drawer path (SR-50/SR-51)', () => {
  it('[SR-50] Ticket with plain-string PayoutAssetId: .att-legacy-warn span rendered', () => {
    const mail: MailRecord = {
      MessageId: 'msg-legacy-ticket',
      MailInfo: {
        Title: 'Legacy Ticket Mail',
        Content: 'Body',
        Attachment: [{ AssetType: 'Ticket', PayoutAssetId: 'expedition_map_ticket_grass', PayoutAmount: 1, Chance: 1 }],
      },
    }
    drawer.open(mail)
    expect(document.body.querySelector('.att-legacy-warn')).not.toBeNull()
  })

  it('[SR-50b] Ticket plain-string: legacy warn span has non-empty text', () => {
    const mail: MailRecord = {
      MessageId: 'msg-legacy-ticket-b',
      MailInfo: {
        Title: 'Legacy Ticket Mail B',
        Content: 'Body',
        Attachment: [{ AssetType: 'Ticket', PayoutAssetId: 'expedition_map_ticket_forest', PayoutAmount: 1, Chance: 1 }],
      },
    }
    drawer.open(mail)
    const warn = document.body.querySelector('.att-legacy-warn')
    expect(warn?.textContent?.trim().length).toBeGreaterThan(0)
  })

  it('[SR-51] ISA with plain-string PayoutAssetId: .att-legacy-warn span rendered', () => {
    const mail: MailRecord = {
      MessageId: 'msg-legacy-isa',
      MailInfo: {
        Title: 'Legacy ISA Mail',
        Content: 'Body',
        Attachment: [{ AssetType: 'ItemSpecificAsset', PayoutAssetId: 'some_legacy_asset_id', PayoutAmount: 1, Chance: 1 }],
      },
    }
    drawer.open(mail)
    expect(document.body.querySelector('.att-legacy-warn')).not.toBeNull()
  })

  it('[SR-50/51 negative] Ticket with JSON-object PayoutAssetId: NO .att-legacy-warn span', () => {
    const ticketJson = JSON.stringify({ BlueprintId: 'expedition_map_ticket_grass', CurrentLevel: 1, Rarity: 0, InitialLevel: 1, FromSource: '' })
    const mail: MailRecord = {
      MessageId: 'msg-modern-ticket',
      MailInfo: {
        Title: 'Modern Ticket Mail',
        Content: 'Body',
        Attachment: [{ AssetType: 'Ticket', PayoutAssetId: ticketJson, PayoutAmount: 1, Chance: 1 }],
      },
    }
    drawer.open(mail)
    expect(document.body.querySelector('.att-legacy-warn')).toBeNull()
  })
})
