/**
 * AdminWeb Proxy — Cloudflare Worker (module syntax)
 *
 * Holds UGS service-account credentials as server-side secrets.
 * The SPA presents only ADMIN_PROXY_TOKEN (session-only, entered by the operator).
 * Key/Secret are NEVER sent to the browser.
 *
 * Routes
 * ──────
 *   OPTIONS *              CORS preflight (no auth required)
 *   GET  /api/health       Liveness probe (no auth required)
 *   POST /api/cloudcode    Proxy Cloud Code call (Bearer ADMIN_PROXY_TOKEN required)
 *
 * POST /api/cloudcode body
 * ────────────────────────
 *   {
 *     projectId:   string   — UGS project ID
 *     environment: string   — env name ("production" / "testing") or UUID passthrough
 *     moduleName:  string   — Cloud Code module name (e.g. "BackpackAdventuresModule")
 *     endpoint:    string   — module endpoint name (e.g. "SendGlobalMail")
 *     request:     unknown  — payload forwarded as { params: { request } }
 *   }
 *
 * Response: upstream Cloud Code JSON + status code, plus CORS headers.
 */

// ---------------------------------------------------------------------------
// Environment bindings (set as Worker secrets via wrangler or CI)
// ---------------------------------------------------------------------------
export interface Env {
  /** UGS project-scoped service-account key ID */
  UNITY_PROJECT_SERVICE_ACCOUNT_KEY: string;
  /** UGS project-scoped service-account secret */
  UNITY_PROJECT_SERVICE_ACCOUNT_SECRET: string;
  /** Gate token the SPA sends as `Authorization: Bearer <token>` */
  ADMIN_PROXY_TOKEN: string;
  /** Exact origin allowed in CORS headers, e.g. https://adminweb.pages.dev */
  ALLOWED_ORIGIN: string;
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------
const UGS_AUTH_BASE = 'https://services.api.unity.com';
const UGS_CC_BASE = 'https://cloud-code.services.api.unity.com';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Build CORS headers for every response. */
function corsHeaders(allowedOrigin: string): Record<string, string> {
  return {
    'Access-Control-Allow-Origin': allowedOrigin || '*',
    'Access-Control-Allow-Methods': 'POST, GET, OPTIONS',
    'Access-Control-Allow-Headers': 'Authorization, Content-Type',
    'Access-Control-Max-Age': '86400',
  };
}

/** Return a JSON response with CORS headers attached. */
function jsonResponse(
  body: unknown,
  status: number,
  cors: Record<string, string>,
): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', ...cors },
  });
}

/** Returns true when `s` looks like a UGS environment UUID. */
function looksLikeUuid(s: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(s);
}

/**
 * Constant-time string equality using SHA-256 digest comparison.
 *
 * A naïve `===` check leaks timing information: it short-circuits on the first
 * differing byte, allowing an attacker to reconstruct the token one character at
 * a time via repeated requests. This implementation instead:
 *   1. Hashes both strings with SHA-256 (fixed 32-byte output regardless of input length).
 *   2. XOR-accumulates all 32 byte pairs — the loop ALWAYS runs to completion.
 *   3. Returns true only when the accumulator is zero (all bytes matched).
 *
 * `crypto.subtle` is available natively in the Cloudflare Workers runtime (and all
 * other Web-standard runtimes this Worker targets: Deno, Vercel, Netlify Edge).
 */
async function timingSafeEqual(a: string, b: string): Promise<boolean> {
  const enc = new TextEncoder();
  const [ha, hb] = await Promise.all([
    crypto.subtle.digest('SHA-256', enc.encode(a)),
    crypto.subtle.digest('SHA-256', enc.encode(b)),
  ]);
  const va = new Uint8Array(ha);
  const vb = new Uint8Array(hb);
  let diff = 0;
  for (let i = 0; i < va.length; i++) diff |= (va[i] as number) ^ (vb[i] as number);
  return diff === 0;
}

/**
 * Resolve an environment name to its UUID.
 * If `environment` is already a UUID it is returned as-is (zero network calls).
 * Otherwise calls the UGS Environments API with Basic auth — no CORS constraint
 * server-side, so this works reliably even though browsers cannot call it directly.
 */
async function resolveEnvironmentId(
  projectId: string,
  environment: string,
  keyId: string,
  secret: string,
): Promise<string> {
  if (looksLikeUuid(environment)) return environment;

  const basic = btoa(`${keyId}:${secret}`);
  const res = await fetch(
    `${UGS_AUTH_BASE}/unity/v1/projects/${projectId}/environments`,
    { headers: { Authorization: `Basic ${basic}` } },
  );

  if (!res.ok) {
    throw new Error(`Environment resolution failed (HTTP ${res.status})`);
  }

  const data = await res.json() as { results?: Array<{ id: string; name: string }>; environments?: Array<{ id: string; name: string }> };
  const list = data.results ?? data.environments ?? [];
  const match = list.find(e => e.name?.toLowerCase() === environment.toLowerCase());

  if (!match?.id) {
    throw new Error(`Environment '${environment}' not found in project ${projectId}`);
  }
  return match.id;
}

/**
 * Exchange service-account credentials for a short-lived UGS access token.
 * Credentials are used only server-side and never forwarded to the browser.
 */
async function tokenExchange(
  projectId: string,
  environmentId: string,
  keyId: string,
  secret: string,
): Promise<string> {
  const basic = btoa(`${keyId}:${secret}`);
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
  );

  if (!res.ok) {
    throw new Error(`Token exchange failed (HTTP ${res.status})`);
  }

  const data = await res.json() as { accessToken?: string };
  if (!data.accessToken) {
    throw new Error('Token exchange returned no accessToken');
  }
  return data.accessToken;
}

// ---------------------------------------------------------------------------
// Request body shape for POST /api/cloudcode
// ---------------------------------------------------------------------------
interface CloudCodeBody {
  projectId: string;
  environment: string;
  moduleName: string;
  endpoint: string;
  request?: unknown;
}

// ---------------------------------------------------------------------------
// Main fetch handler
// ---------------------------------------------------------------------------
export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const cors = corsHeaders(env.ALLOWED_ORIGIN);

    // ------------------------------------------------------------------
    // CORS preflight — respond to all OPTIONS requests unconditionally
    // ------------------------------------------------------------------
    if (request.method === 'OPTIONS') {
      return new Response(null, { status: 204, headers: cors });
    }

    const { pathname } = new URL(request.url);

    // ------------------------------------------------------------------
    // GET /api/health — liveness probe, no auth
    // ------------------------------------------------------------------
    if (request.method === 'GET' && pathname === '/api/health') {
      return jsonResponse({ ok: true }, 200, cors);
    }

    // ------------------------------------------------------------------
    // POST /api/cloudcode — authenticated Cloud Code proxy
    // ------------------------------------------------------------------
    if (request.method === 'POST' && pathname === '/api/cloudcode') {
      // 1. Validate gate token (constant-time comparison to resist timing attacks)
      const authHeader = request.headers.get('Authorization') ?? '';
      const bearerToken = authHeader.startsWith('Bearer ')
        ? authHeader.slice(7).trim()
        : '';

      if (!bearerToken || !(await timingSafeEqual(bearerToken, env.ADMIN_PROXY_TOKEN))) {
        return jsonResponse({ error: 'Unauthorized' }, 401, cors);
      }

      // 2. Parse and validate body
      let body: CloudCodeBody;
      try {
        body = await request.json() as CloudCodeBody;
      } catch {
        return jsonResponse({ error: 'Invalid JSON body' }, 400, cors);
      }

      const { projectId, environment, moduleName, endpoint, request: ccRequest } = body;
      if (!projectId || !environment || !moduleName || !endpoint) {
        return jsonResponse(
          { error: 'Missing required fields: projectId, environment, moduleName, endpoint' },
          400,
          cors,
        );
      }

      // 3. Execute the three-step UGS flow (all server-side)
      try {
        const environmentId = await resolveEnvironmentId(
          projectId,
          environment,
          env.UNITY_PROJECT_SERVICE_ACCOUNT_KEY,
          env.UNITY_PROJECT_SERVICE_ACCOUNT_SECRET,
        );

        const accessToken = await tokenExchange(
          projectId,
          environmentId,
          env.UNITY_PROJECT_SERVICE_ACCOUNT_KEY,
          env.UNITY_PROJECT_SERVICE_ACCOUNT_SECRET,
        );

        const ccUrl = `${UGS_CC_BASE}/v1/projects/${projectId}/modules/${moduleName}/${endpoint}`;
        const ccRes = await fetch(ccUrl, {
          method: 'POST',
          headers: {
            Authorization: `Bearer ${accessToken}`,
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({ params: { request: ccRequest ?? {} } }),
        });

        // Return upstream response body + status verbatim; add CORS headers.
        const ccBody = await ccRes.text();
        return new Response(ccBody, {
          status: ccRes.status,
          headers: {
            'Content-Type': ccRes.headers.get('Content-Type') ?? 'application/json',
            ...cors,
          },
        });
      } catch (err: unknown) {
        // Sanitise error message — never surface secrets in the response.
        let message = err instanceof Error ? err.message : 'Proxy error';
        // Belt-and-suspenders redaction: strip any accidental secret leakage.
        message = message
          .replaceAll(env.UNITY_PROJECT_SERVICE_ACCOUNT_KEY ?? '', '[REDACTED]')
          .replaceAll(env.UNITY_PROJECT_SERVICE_ACCOUNT_SECRET ?? '', '[REDACTED]')
          .replaceAll(env.ADMIN_PROXY_TOKEN ?? '', '[REDACTED]');

        return jsonResponse({ error: message }, 502, cors);
      }
    }

    // ------------------------------------------------------------------
    // Fallthrough — 404
    // ------------------------------------------------------------------
    return jsonResponse({ error: 'Not found' }, 404, cors);
  },
} satisfies ExportedHandler<Env>;
