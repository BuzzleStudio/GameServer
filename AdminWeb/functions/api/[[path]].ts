/**
 * Cloudflare Pages Function for AdminWeb API.
 *
 * Runs under the same origin as the static SPA:
 *   GET  /api/health
 *   POST /api/cloudcode
 *
 * UGS service-account credentials are Pages secrets, available only on
 * context.env at runtime. They are never written into the browser bundle.
 */

interface Env {
  UNITY_PROJECT_SERVICE_ACCOUNT_KEY: string
  UNITY_PROJECT_SERVICE_ACCOUNT_SECRET: string
  ADMIN_PROXY_TOKEN: string
}

const UGS_AUTH_BASE = 'https://services.api.unity.com'
const UGS_CC_BASE = 'https://cloud-code.services.api.unity.com'

function corsHeaders(request: Request): Record<string, string> {
  return {
    'Access-Control-Allow-Origin': request.headers.get('Origin') ?? '*',
    'Access-Control-Allow-Methods': 'POST, GET, OPTIONS',
    'Access-Control-Allow-Headers': 'Authorization, Content-Type',
    'Access-Control-Max-Age': '86400',
  }
}

function jsonResponse(body: unknown, status: number, headers: Record<string, string>): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', ...headers },
  })
}

function looksLikeUuid(s: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(s)
}

async function timingSafeEqual(a: string, b: string): Promise<boolean> {
  const enc = new TextEncoder()
  const [ha, hb] = await Promise.all([
    crypto.subtle.digest('SHA-256', enc.encode(a)),
    crypto.subtle.digest('SHA-256', enc.encode(b)),
  ])
  const va = new Uint8Array(ha)
  const vb = new Uint8Array(hb)
  let diff = 0
  for (let i = 0; i < va.length; i++) diff |= (va[i] as number) ^ (vb[i] as number)
  return diff === 0
}

async function resolveEnvironmentId(
  projectId: string,
  environment: string,
  keyId: string,
  secret: string,
): Promise<string> {
  if (looksLikeUuid(environment)) return environment

  const basic = btoa(`${keyId}:${secret}`)
  const res = await fetch(`${UGS_AUTH_BASE}/unity/v1/projects/${projectId}/environments`, {
    headers: { Authorization: `Basic ${basic}` },
  })

  if (!res.ok) throw new Error(`Environment resolution failed (HTTP ${res.status})`)

  const data = (await res.json()) as {
    results?: Array<{ id: string; name: string }>
    environments?: Array<{ id: string; name: string }>
  }
  const list = data.results ?? data.environments ?? []
  const match = list.find((e) => e.name?.toLowerCase() === environment.toLowerCase())

  if (!match?.id) throw new Error(`Environment '${environment}' not found in project ${projectId}`)
  return match.id
}

async function tokenExchange(
  projectId: string,
  environmentId: string,
  keyId: string,
  secret: string,
): Promise<string> {
  const basic = btoa(`${keyId}:${secret}`)
  const res = await fetch(
    `${UGS_AUTH_BASE}/auth/v1/token-exchange?projectId=${projectId}&environmentId=${environmentId}`,
    {
      method: 'POST',
      headers: {
        Authorization: `Basic ${basic}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ scopes: [] }),
    },
  )

  if (!res.ok) throw new Error(`Token exchange failed (HTTP ${res.status})`)

  const data = (await res.json()) as { accessToken?: string }
  if (!data.accessToken) throw new Error('Token exchange returned no accessToken')
  return data.accessToken
}

interface CloudCodeBody {
  projectId: string
  environment: string
  moduleName: string
  endpoint: string
  request?: unknown
}

export const onRequest = async ({ request, env }: { request: Request; env: Env }) => {
  const cors = corsHeaders(request)

  if (request.method === 'OPTIONS') {
    return new Response(null, { status: 204, headers: cors })
  }

  const { pathname } = new URL(request.url)

  if (request.method === 'GET' && pathname === '/api/health') {
    return jsonResponse({ ok: true }, 200, cors)
  }

  if (request.method !== 'POST' || pathname !== '/api/cloudcode') {
    return jsonResponse({ error: 'Not found' }, 404, cors)
  }

  const authHeader = request.headers.get('Authorization') ?? ''
  const bearerToken = authHeader.startsWith('Bearer ') ? authHeader.slice(7).trim() : ''

  if (!bearerToken || !(await timingSafeEqual(bearerToken, env.ADMIN_PROXY_TOKEN))) {
    return jsonResponse({ error: 'Unauthorized' }, 401, cors)
  }

  let body: CloudCodeBody
  try {
    body = (await request.json()) as CloudCodeBody
  } catch {
    return jsonResponse({ error: 'Invalid JSON body' }, 400, cors)
  }

  const { projectId, environment, moduleName, endpoint, request: ccRequest } = body
  if (!projectId || !environment || !moduleName || !endpoint) {
    return jsonResponse(
      { error: 'Missing required fields: projectId, environment, moduleName, endpoint' },
      400,
      cors,
    )
  }

  try {
    const environmentId = await resolveEnvironmentId(
      projectId,
      environment,
      env.UNITY_PROJECT_SERVICE_ACCOUNT_KEY,
      env.UNITY_PROJECT_SERVICE_ACCOUNT_SECRET,
    )

    const accessToken = await tokenExchange(
      projectId,
      environmentId,
      env.UNITY_PROJECT_SERVICE_ACCOUNT_KEY,
      env.UNITY_PROJECT_SERVICE_ACCOUNT_SECRET,
    )

    const ccUrl = `${UGS_CC_BASE}/v1/projects/${projectId}/modules/${moduleName}/${endpoint}`
    const ccRes = await fetch(ccUrl, {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${accessToken}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ params: { request: ccRequest ?? {} } }),
    })

    const ccBody = await ccRes.text()
    return new Response(ccBody, {
      status: ccRes.status,
      headers: {
        'Content-Type': ccRes.headers.get('Content-Type') ?? 'application/json',
        ...cors,
      },
    })
  } catch (err: unknown) {
    let message = err instanceof Error ? err.message : 'Proxy error'
    message = message
      .replaceAll(env.UNITY_PROJECT_SERVICE_ACCOUNT_KEY ?? '', '[REDACTED]')
      .replaceAll(env.UNITY_PROJECT_SERVICE_ACCOUNT_SECRET ?? '', '[REDACTED]')
      .replaceAll(env.ADMIN_PROXY_TOKEN ?? '', '[REDACTED]')

    return jsonResponse({ error: message }, 502, cors)
  }
}
