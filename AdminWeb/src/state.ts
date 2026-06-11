// src/state.ts — Typed sub-state definitions and factory functions.
// Step 1: Introduced in state rationalization. Old AppState flat fields in main.ts
// continue to work via compatibility shims (removed in Step 4).

import type { AttachmentDraft, MailRecord } from './types'

// ─── MailStatus (canonical source — re-exported by modules/status.ts in Step 2) ──

export type MailStatus =
  | 'Active'
  | 'Expiring soon'
  | 'Expired'
  | 'Scheduled'
  | 'No expiry'

// ─── Sub-state interfaces ─────────────────────────────────────────────────────────

export interface ConnectionState {
  projectId:   string
  environment: string
  moduleName:  string
  operatorId:  string
  proxyToken:  string   // memory only — NEVER persisted
}

/** Edit buffer for the mail editor drawer. Independent copy until Save commits. */
export interface DrawerState {
  open:        boolean
  mailId:      string | null
  mode:        'view' | 'edit'
  // Edit buffer fields
  subject:     string
  body:        string
  expiryMode:  'none' | 'set'
  expiryDate:  string
  expiryTime:  string
  targetMode:  'all' | 'specific'
  targetText:  string           // raw textarea, one ID per line
  attachments: AttachmentDraft[]
  senderName:  string
  dedupKey:    string
  // Drawer meta
  dirty:       boolean
  saving:      boolean
  error:       string | null
}

export interface ManageTabState {
  mailList:     MailRecord[] | null
  loading:      boolean
  error:        string | null
  searchQuery:  string
  scopeFilter:  '' | 'Global' | 'Global-targeted' | 'User'
  statusFilter: '' | MailStatus
  page:         number
  pageSize:     number   // target: 10 (increase to 20 in v2)
  drawer:       DrawerState
}

export interface SendFormState {
  recipientMode:  'global' | 'targeted'
  subject:        string
  body:           string
  expiryMode:     'none' | 'set'
  expiryDate:     string
  expiryTime:     string
  category:       number
  senderName:     string
  dedupKey:       string
  targetText:     string           // raw textarea, one ID per line
  attachments:    AttachmentDraft[]
  importText:     string
  importErrors:   string[]
  importWarnings: string[]
  importExpanded: boolean
  busy:           boolean
  lastMailId:     string | null
  lastError:      string | null
}

/** Root application state — new shape (Step 1+). */
export interface AppState {
  connection:  ConnectionState
  sendForm:    SendFormState
  manageTab:   ManageTabState
  activeTab:   'send' | 'manage'
  statusMsg:   string
  statusType:  'success' | 'error' | 'warning' | 'info' | ''
  rawJson:     string
  busy:        boolean
}

// ─── Factory functions ────────────────────────────────────────────────────────────

export function defaultConnectionState(): ConnectionState {
  return {
    projectId:   '',
    environment: '',
    moduleName:  'BackpackAdventuresModule',
    operatorId:  '',
    proxyToken:  '',
  }
}

export function defaultDrawerState(): DrawerState {
  return {
    open:        false,
    mailId:      null,
    mode:        'view',
    subject:     '',
    body:        '',
    expiryMode:  'none',
    expiryDate:  '',
    expiryTime:  '',
    targetMode:  'all',
    targetText:  '',
    attachments: [],
    senderName:  '',
    dedupKey:    '',
    dirty:       false,
    saving:      false,
    error:       null,
  }
}

export function defaultManageTabState(): ManageTabState {
  return {
    mailList:     null,
    loading:      false,
    error:        null,
    searchQuery:  '',
    scopeFilter:  '',
    statusFilter: '',
    page:         0,
    pageSize:     10,
    drawer:       defaultDrawerState(),
  }
}

export function defaultSendFormState(): SendFormState {
  return {
    recipientMode:  'global',
    subject:        '',
    body:           '',
    expiryMode:     'none',
    expiryDate:     '',
    expiryTime:     '',
    category:       0,
    senderName:     '',
    dedupKey:       '',
    targetText:     '',
    attachments:    [],
    importText:     '',
    importErrors:   [],
    importWarnings: [],
    importExpanded: false,
    busy:           false,
    lastMailId:     null,
    lastError:      null,
  }
}

export function defaultAppState(): AppState {
  return {
    connection:  defaultConnectionState(),
    sendForm:    defaultSendFormState(),
    manageTab:   defaultManageTabState(),
    activeTab:   'send',
    statusMsg:   '',
    statusType:  '',
    rawJson:     '',
    busy:        false,
  }
}
