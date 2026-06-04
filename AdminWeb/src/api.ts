// ─── API abstraction layer (proxy model) ─────────────────────────────────────────
//
// All Cloud Code calls are routed through a serverless proxy.
// The SPA sends ONE request per operation; the proxy handles:
//   1. Env name → UUID resolution (server-side, no CORS issue)
//   2. UGS service-account token exchange (secret never reaches the browser)
//   3. Cloud Code REST call
//   4. Returns the same Cloud Code response envelope: { output: { StatusCode, Message, Data } }
//
// SPA ─── POST /api/cloudcode ──────────────────────────────────────────────────►  Proxy
//          Authorization: Bearer <proxyToken>                                      │
//          { projectId, environment, moduleName, endpoint, request }               │
//                                                                                  ▼
//                                                     services.api.unity.com + cloud-code
//
// Security: NO UGS service-account Key or Secret ever enters the browser bundle
// or sessionStorage. The proxyToken is the only secret — kept in memory only.
//
// Proxy request shape:
//   POST /api/cloudcode
//   Authorization: Bearer <proxyToken>
//   Content-Type: application/json
//   { "projectId": "...", "environment": "production", "moduleName": "...",
//     "endpoint": "SendGlobalMail", "request": { ... } }

// ── Same-origin proxy base URL ───────────────────────────────────────────────────
// Cloudflare Pages serves the SPA and Pages Function on the same origin.
// Empty base means fetch('/api/cloudcode'), so no build-time proxy URL is needed.
export const apiConfig = {
  proxyBase: '',
}

// ── Session-storage credential keys ──────────────────────────────────────────────
// NOTE: proxyToken is NEVER stored in sessionStorage — memory only.
const SS = {
  projectId:   'adminmail.projectId',
  environment: 'adminmail.environment',
  moduleName:  'adminmail.moduleName',
  operatorId:  'adminmail.operatorId',
} as const

export interface StoredCredentials {
  projectId:   string
  environment: string  // name ("production" / "testing") or UUID
  moduleName:  string  // default "BackpackAdventuresModule"
  operatorId:  string
}

export function saveCredentials(c: StoredCredentials): void {
  sessionStorage.setItem(SS.projectId,   c.projectId)
  sessionStorage.setItem(SS.environment, c.environment)
  sessionStorage.setItem(SS.moduleName,  c.moduleName)
  sessionStorage.setItem(SS.operatorId,  c.operatorId)
}

export function loadCredentials(): StoredCredentials {
  return {
    projectId:   sessionStorage.getItem(SS.projectId)   ?? '',
    environment: sessionStorage.getItem(SS.environment) ?? '',
    moduleName:  sessionStorage.getItem(SS.moduleName)  ?? 'BackpackAdventuresModule',
    operatorId:  sessionStorage.getItem(SS.operatorId)  ?? '',
  }
}

export function clearAllCredentials(): void {
  Object.values(SS).forEach((k) => sessionStorage.removeItem(k))
}

// ── Resolve effective proxy base URL ─────────────────────────────────────────────
export function effectiveProxyBase(): string {
  return apiConfig.proxyBase
}

// ── Core proxy call ───────────────────────────────────────────────────────────────
export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly body?: string,
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

export interface ProxyCallArgs {
  proxyBase:   string   // same-origin base, normally ''
  proxyToken:  string   // operator's proxy access token — MEMORY ONLY, never stored
  projectId:   string
  environment: string   // name or UUID — proxy resolves
  moduleName:  string
}

/**
 * Call one Cloud Code endpoint through the proxy.
 *
 * Request body: { projectId, environment, moduleName, endpoint, request }
 * Response envelope (from proxy, forwarded from Cloud Code):
 *   { output: { StatusCode, Message, Data } }
 * Returns unwrapped Data and the raw response text.
 */
export async function callCloudCode<T>(
  args: ProxyCallArgs,
  endpoint: string,
  request: unknown,
): Promise<{ data: T; rawResponse: string }> {
  const url = `${args.proxyBase}/api/cloudcode`

  const resp = await fetch(url, {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${args.proxyToken}`,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({
      projectId:   args.projectId,
      environment: args.environment,
      moduleName:  args.moduleName,
      endpoint,
      request,
    }),
  })

  const rawResponse = await resp.text()

  if (!resp.ok) {
    throw new ApiError(
      `${endpoint} failed: HTTP ${resp.status} ${resp.statusText}`,
      resp.status,
      rawResponse,
    )
  }

  let parsed: Record<string, unknown>
  try {
    parsed = JSON.parse(rawResponse)
  } catch {
    throw new ApiError(`${endpoint}: invalid JSON response`, 0, rawResponse)
  }

  // Unwrap Cloud Code envelope: { output: { StatusCode, Message, Data } }
  const output = (parsed['output'] as Record<string, unknown> | undefined) ?? parsed
  const data =
    (output['Data'] as T | undefined) ??
    (output['data'] as T | undefined) ??
    (output as unknown as T)

  return { data, rawResponse }
}

// ── DTO types (re-exported for convenience) ───────────────────────────────────────
export type {
  SendGlobalMailRequest,
  SendGlobalMailResponse,
  GetGlobalMailsRequest,
  GetGlobalMailsResponse,
  SetMailEndTimeRequest,
  SetMailEndTimeResponse,
  UpdateGlobalMailRequest,
  UpdateGlobalMailResponse,
  ExpireMailRequest,
  ExpireMailResponse,
  DeleteGlobalMailRequest,
  DeleteMailResponse,
  PurgeExpiredRequest,
  PurgeExpiredResponse,
} from './types'

import type {
  SendGlobalMailRequest,
  SendGlobalMailResponse,
  GetGlobalMailsRequest,
  GetGlobalMailsResponse,
  SetMailEndTimeRequest,
  SetMailEndTimeResponse,
  UpdateGlobalMailRequest,
  UpdateGlobalMailResponse,
  ExpireMailRequest,
  ExpireMailResponse,
  DeleteGlobalMailRequest,
  DeleteMailResponse,
  PurgeExpiredRequest,
  PurgeExpiredResponse,
} from './types'

// ── Endpoint wrappers (moduleName is a runtime parameter, not a constant) ─────────
export async function apiSendGlobalMail(
  args: ProxyCallArgs, req: SendGlobalMailRequest,
) {
  return callCloudCode<SendGlobalMailResponse>(args, 'SendGlobalMail', req)
}

export async function apiGetGlobalMails(
  args: ProxyCallArgs, req: GetGlobalMailsRequest,
) {
  return callCloudCode<GetGlobalMailsResponse>(args, 'GetGlobalMails', req)
}

export async function apiSetMailEndTime(
  args: ProxyCallArgs, req: SetMailEndTimeRequest,
) {
  return callCloudCode<SetMailEndTimeResponse>(args, 'SetMailEndTime', req)
}

export async function apiUpdateGlobalMail(
  args: ProxyCallArgs, req: UpdateGlobalMailRequest,
) {
  return callCloudCode<UpdateGlobalMailResponse>(args, 'UpdateGlobalMail', req)
}

export async function apiExpireMail(
  args: ProxyCallArgs, req: ExpireMailRequest,
) {
  return callCloudCode<ExpireMailResponse>(args, 'ExpireMail', req)
}

export async function apiDeleteGlobalMail(
  args: ProxyCallArgs, req: DeleteGlobalMailRequest,
) {
  return callCloudCode<DeleteMailResponse>(args, 'DeleteGlobalMail', req)
}

export async function apiPurgeExpired(
  args: ProxyCallArgs, req: PurgeExpiredRequest,
) {
  return callCloudCode<PurgeExpiredResponse>(args, 'PurgeExpired', req)
}
