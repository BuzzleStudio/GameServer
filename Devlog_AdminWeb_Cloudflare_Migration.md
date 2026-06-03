# Devlog_AdminWeb_Cloudflare_Migration

## Status
- Phase: Execution (team spawned)
- Owner: Claude (tech lead) + 2 Sonnet teammates
- Last updated: 2026-06-03

## Problem & Goal
AdminWeb SPA currently deploys to **GitHub Pages** (`deploy-adminweb.yml`) at subpath
`https://dycuong03.github.io/UnityCloudCode/`. Move it to **pure Cloudflare** (Cloudflare Pages),
no GitHub Pages. The proxy is already on Cloudflare (Worker, `deploy-proxy.yml`) and the repo
already has `CLOUDFLARE_API_TOKEN` + `CLOUDFLARE_ACCOUNT_ID` secrets — reuse them.

## Current state (verified)
- `AdminWeb/` = Vite SPA, `npm run build` → `AdminWeb/dist`. Single page (`#app`, `src/main.ts`).
- `vite.config.ts`: `base = process.env.VITE_BASE ?? './'`.
- GH Pages build used `VITE_BASE=/UnityCloudCode/` (subpath) + `VITE_PROXY_URL` (repo var).
- Proxy Worker (`deploy-proxy.yml`) reads CORS `ALLOWED_ORIGIN` from repo var
  `ADMIN_PROXY_ALLOWED_ORIGIN` = `https://dycuong03.github.io` (the old Pages origin).
- No `public/` dir. node 22 / npm 11 available locally.

## Design

### deploy-adminweb.yml → Cloudflare Pages
- Build job: checkout → setup-node 20 (npm cache) → `npm ci` → `npm run build` with
  **`VITE_BASE=/`** (root serving on `*.pages.dev`, NOT the `/UnityCloudCode/` subpath) and
  `VITE_PROXY_URL: ${{ vars.VITE_PROXY_URL }}`.
- Deploy: `cloudflare/wrangler-action@v3`, `workingDirectory: AdminWeb`,
  `command: pages deploy dist --project-name=adminweb --branch=${{ github.ref_name }}`,
  `apiToken: CLOUDFLARE_API_TOKEN`, `accountId: CLOUDFLARE_ACCOUNT_ID`.
- `permissions: { contents: read }` (drop `pages: write` / `id-token: write`).
- `concurrency: group: adminweb-pages-${{ github.ref_name }}, cancel-in-progress: true`.
- Triggers: keep `staging` + `release/*` + `workflow_dispatch`; paths `AdminWeb/**` &
  `.github/workflows/deploy-adminweb.yml`, **exclude `AdminWeb/proxy/**`** (proxy has its own workflow).
- Remove `upload-pages-artifact` / `deploy-pages`. Job summary prints the `*.pages.dev` URL.

### SPA fallback
- Add `AdminWeb/public/_redirects` containing `/*    /index.html   200` (Vite copies `public/` → `dist`).

### Build base path
- Pass `VITE_BASE=/` in the workflow. `vite.config.ts` default stays `./` for local dev; update its comment (no longer GitHub-Pages-specific).

### CORS coupling (cross-file)
- The AdminWeb origin becomes `https://adminweb.pages.dev` (or `https://<project>.pages.dev`).
- `deploy-proxy.yml`: update header docs + `ADMIN_PROXY_ALLOWED_ORIGIN` example to the pages.dev origin.
- **Ops action (documented, not automated):** set repo var `ADMIN_PROXY_ALLOWED_ORIGIN` to the new
  pages.dev origin and re-run deploy-proxy so the Worker allows the new origin. Until then CORS blocks.

### One-time Cloudflare setup (documented in workflow header)
- `wrangler pages project create adminweb` (or create in dashboard); set production branch = `staging`.
- Existing `CLOUDFLARE_API_TOKEN` needs **Pages:Edit** in addition to Workers:Edit.

## Scope / Non-scope
- IN: `deploy-adminweb.yml` rewrite, `AdminWeb/public/_redirects`, `vite.config.ts` comment,
  `deploy-proxy.yml` doc/origin update, doc references (README/Devlog) github.io → pages.dev.
- OUT: changing proxy Worker logic, Cloud Code `deploy.yml`, AdminWeb app code/features.
- Secrets unchanged (reuse Cloudflare ones). No GitHub Pages settings needed anymore.

## Agent Allocation
| Agent | Model | Responsibility | Acceptance |
|---|---|---|---|
| cf-deploy | sonnet | Rewrite deploy-adminweb.yml → CF Pages; add public/_redirects; VITE_BASE=/; update vite.config comment; update deploy-proxy.yml CORS docs/origin; grep+fix github.io / GitHub-Pages / /UnityCloudCode/ doc refs | files updated, no GH Pages remnants, headers document one-time setup + ops origin change |
| cf-verify | sonnet | `npm ci && npm run build` in AdminWeb with VITE_BASE=/ → confirm dist/index.html asset paths are root-relative (`/assets/...`), _redirects present in dist; YAML sanity (valid, no pages perms, wrangler pages deploy correct); cross-check proxy origin consistency; confirm zero github.io/Pages remnants; report concrete results | build green, asset paths root-relative, _redirects in dist, YAML valid, no remnants |

## Testing Plan
- Local: AdminWeb build with `VITE_BASE=/` → inspect `dist/index.html` (script/asset hrefs start with `/`), `dist/_redirects` exists.
- YAML: parse all 3 workflows; confirm deploy-adminweb has no `pages:`/`deploy-pages`, uses wrangler-action `pages deploy`.
- Consistency: deploy-proxy CORS origin == AdminWeb pages.dev origin (docs).
- Cannot run real Cloudflare deploy here (needs live token/account) — verified by build + workflow correctness; document that first live deploy needs the one-time Pages project + token Pages:Edit + origin var.

## Execution Notes
- cf-deploy: rewrote `deploy-adminweb.yml` → Cloudflare Pages (wrangler-action `pages deploy dist --project-name=adminweb`), `VITE_BASE=/`, perms `contents:read`, paths exclude `AdminWeb/proxy/**`, removed all GH Pages steps; created `AdminWeb/public/_redirects`; updated `vite.config.ts` comment; updated `deploy-proxy.yml` CORS docs + `README.md`, `docs/CICD.md`, `Devlog_AdminWeb_Mailbox.md` github.io→pages.dev.
- Lead: reviewed final `deploy-adminweb.yml` (correct); fixed stale `worker.ts:37` CORS example comment github.io→pages.dev; removed agent-created `AdminWeb/public.meta` + `public/_redirects.meta` (no-meta rule; would otherwise ship to CDN via vite public copy).

## Verification Results (cf-verify, re-checked by lead)
- Build: `npm ci && VITE_BASE=/ npm run build` PASS. dist asset hrefs ROOT-RELATIVE (`/assets/index-*.js`, `/assets/index-*.css`) — no `./`, no `/UnityCloudCode/`. `dist/_redirects` present (`/*    /index.html   200`).
- YAML: all 3 workflows valid. deploy-adminweb has NO `pages:`/`id-token:`, NO upload-pages-artifact/deploy-pages/github-pages env; uses `cloudflare/wrangler-action@v3` `pages deploy dist`; CF secrets; `VITE_BASE: /`.
- Consistency: deploy-adminweb live URL + deploy-proxy `ADMIN_PROXY_ALLOWED_ORIGIN` example both `https://adminweb.pages.dev`.
- Remnants: none in active workflows/config/source (only historical Devlog entries). `deploy.yml` (Cloud Code) untouched; AdminWeb app code untouched.

## Final State
COMPLETE (code). AdminWeb deploys to pure Cloudflare Pages; GitHub Pages fully removed.

### Required ops actions before first live deploy (cannot be automated here)
1. `wrangler pages project create adminweb` (or via dashboard); set production branch = `staging`.
2. `CLOUDFLARE_API_TOKEN` must include **Pages:Edit** (already has Workers:Edit).
3. Set repo variable `ADMIN_PROXY_ALLOWED_ORIGIN` = `https://adminweb.pages.dev` and re-run `deploy-proxy.yml` so the Worker allows the new origin (else CORS blocks SPA→proxy).
4. Confirm `VITE_PROXY_URL` repo variable still set (unchanged).

### Limitations
- Real Cloudflare deploy not executed here (no live account/token) — validated via build + workflow correctness.
- `--branch=${{ github.ref_name }}`: `release/*` names contain a slash; Cloudflare sanitizes branch aliases — production mapping is via the project's production-branch setting (= staging). Acceptable; documented.
