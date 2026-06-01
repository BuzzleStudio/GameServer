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
// Proxy request shape (coordinate with devops/task #6):
//   POST <proxyBase>/api/cloudcode
//   Authorization: Bearer <proxyToken>
//   Content-Type: application/json
//   { "projectId": "...", "environment": "production", "moduleName": "...",
//     "endpoint": "SendGlobalMail", "request": { ... } }

// ── Configurable proxy base URL ───────────────────────────────────────────────────
// Set VITE_PROXY_URL at build time (non-secret — it's just a URL).
// The operator can also override at runtime via the connection form (saved to sessionStorage).
// FILL: set VITE_PROXY_URL=https://your-worker.workers.dev at build time,
//       or override the "Proxy URL" field in the connection form at runtime.
export const apiConfig = {
  proxyBase: (import.meta.env['VITE_PROXY_URL'] as string | undefined) || '<PROXY_URL_NOT_SET>',
}

// ── Session-storage credential keys ──────────────────────────────────────────────
// NOTE: proxyToken is NEVER stored in sessionStorage — memory only.
// proxyBase is non-secret (just a URL) and safe to persist.
const SS = {
  projectId:   'adminmail.projectId',
  environment: 'adminmail.environment',
  moduleName:  'adminmail.moduleName',
  operatorId:  'adminmail.operatorId',
  proxyBase:   'adminmail.proxyBase',   // runtime override for proxy URL (non-secret)
} as const

export interface StoredCredentials {
  projectId:   string
  environment: string  // name ("production" / "testing") or UUID
  moduleName:  string  // default "BackpackAdventuresModule"
  operatorId:  string
  proxyBase:   string  // runtime proxy URL override; '' means use build-time default
}

export function saveCredentials(c: StoredCredentials): void {
  sessionStorage.setItem(SS.projectId,   c.projectId)
  sessionStorage.setItem(SS.environment, c.environment)
  sessionStorage.setItem(SS.moduleName,  c.moduleName)
  sessionStorage.setItem(SS.operatorId,  c.operatorId)
  sessionStorage.setItem(SS.proxyBase,   c.proxyBase)
}

export function loadCredentials(): StoredCredentials {
  return {
    projectId:   sessionStorage.getItem(SS.projectId)   ?? '',
    environment: sessionStorage.getItem(SS.environment) ?? '',
    moduleName:  sessionStorage.getItem(SS.moduleName)  ?? 'BackpackAdventuresModule',
    operatorId:  sessionStorage.getItem(SS.operatorId)  ?? '',
    proxyBase:   sessionStorage.getItem(SS.proxyBase)   ?? '',
  }
}

export function clearAllCredentials(): void {
  Object.values(SS).forEach((k) => sessionStorage.removeItem(k))
}

// ── Resolve effective proxy base URL ─────────────────────────────────────────────
// Runtime override (sessionStorage) wins over build-time VITE_PROXY_URL default.
export function effectiveProxyBase(runtimeOverride: string): string {
  return runtimeOverride.trim() || apiConfig.proxyBase
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
  proxyBase:   string   // effective proxy URL (runtime override or build-time default)
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
