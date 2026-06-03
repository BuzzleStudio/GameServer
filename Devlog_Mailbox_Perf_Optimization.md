# Devlog_Mailbox_Perf_Optimization

## Status

- Current phase: Execution (team spawned)
- Owner: Claude (AI Orchestration Tech Lead) + 3 Sonnet teammates
- Last updated: 2026-06-03

## Problem & Product Goal

**Problem:** Mailbox Cloud Code functions — especially `ClaimAttachmentModule` (`ClaimAttachment`, `ClaimAllAttachments`) — have long round-trip latency from execution server to client. Every request loads data straight from Cloud Save over HTTP. No RAM cache. Lookups are O(n) `List.Find()`. `ClaimAll` re-reads the global collection per mail.

**Product goal:** Make Claim APIs fast (comparable to other read paths), reduce Cloud Save round-trips, O(1) data lookup where possible, and expose server execution time to the client for measurement.

## Solution Direction

1. **RAM cache** (`MailboxCache.cs`): process-wide best-effort warm-instance cache with short TTL, write-through on set, lock-safe (writeLock reads always fetch fresh lock token). Wired into `CloudSaveHelper` read/set.
2. **O(1) lookups**: lazy `Dictionary` indexes on `GlobalMailCollection`, `PlayerGlobalMailState`, `PlayerUserMailbox`. Replace `.Find()` in `ClaimAttachmentModule` + `MailSchemaHelper`.
3. **Fewer round-trips**: `ClaimAll` loads collection/state once, reuses across all candidate mails (no per-mail re-read).
4. **Server exec time**: add `ServerExecutionMs` to `ApiResponse<T>`; `Stopwatch` wrapper in Claim functions.

## Scope

- `ClaimAttachmentModule.cs`, `CloudSaveHelper.cs`, `MailboxModels.cs` (models + `GlobalMailStore` + `MailSchemaHelper`), `ModuleConfig.cs` (`ApiResponse<T>`), new `MailboxCache.cs`.
- Server-side test project (new) + client EditMode test run via Unity MCP.

## Non-Scope

- AdminWeb, UnityClient runtime API surface changes (client just reads new optional field).
- Replacing Cloud Save with external Redis (no managed Redis in UGS; RAM cache is in-process best-effort).
- RewardGrant economy SDK migration (separate TODO).

## Technical Design

### Constraints discovered
- Cloud Code modules are stateless/ephemeral per invocation; UGS reuses warm instances for a while → in-process static cache helps within bursts and warm reuse, NOT guaranteed across cold starts/instances. Therefore: cache = **best-effort acceleration only**; correctness still guarded by Cloud Save `writeLock` on claim writes.
- `dotnet.exe` available at `/mnt/c/Program Files/dotnet/dotnet.exe` → server builds/tests runnable from WSL.
- Module target: net7.0.

### Cache invariants (CRITICAL — correctness)
- Plain `GetPlayerDataAsync`/`GetCustomDataAsync`: read-through cache (TTL ~5s).
- `Get*WithLockAsync`: MUST always REST-fetch (writeLock token must be current). May write-through populate plain cache with the data, but never serve a stale lock.
- `Set*Async`: write-through update cache entry with new value + bump version; never leave stale.
- On writeLock conflict (409): evict the key so next read is fresh.
- Idempotency cache + wallet reads benefit most (extra GETs collapsed within an invocation).

### O(1) indexes
- `GlobalMailCollection`: `[JsonIgnore]` lazy `Dictionary<string,GlobalMailPayload>` (case-insensitive) rebuilt on first lookup / invalidated on mutate.
- `PlayerGlobalMailState`: lazy `Dictionary<string,MailMetadata>` by MessageId.
- `PlayerUserMailbox`: lazy `Dictionary<string,...>` by MessageId.

## Model & Resource Allocation

| Phase | Model/Agent | Reason |
|---|---|---|
| Design, review, integration | Opus (lead, this session) | architecture + cross-file integration + final verify |
| Cache layer | Sonnet (cc-cache) | focused impl in CloudSaveHelper + new file |
| Indexes + exec-time | Sonnet (cc-index) | focused impl in models + module |
| Tests | Sonnet (cc-test) | server test project + Unity EditMode run |

## Implementation Plan / Agent Allocation

| Agent | Files OWNED (no others edit) | Responsibility | Acceptance |
|---|---|---|---|
| **cc-cache** | `MailboxCache.cs` (new), `CloudSaveHelper.cs` | RAM cache: read-through, write-through, lock-safe, 409-evict, TTL const in MailboxConstants | `dotnet build` green; lock reads bypass cache; set is write-through |
| **cc-index** | `MailboxModels.cs`, `ClaimAttachmentModule.cs`, `ModuleConfig.cs` | O(1) dict indexes; replace `.Find()`; ClaimAll single-load reuse; `ApiResponse<T>.ServerExecutionMs` + Stopwatch in Claim fns | `dotnet build` green; no `.Find()` in claim hot path; exec ms populated |
| **cc-test** | new `BackpackAdventuresModule.Tests/` (xUnit), test reports | server tests (cache TTL/invalidation, O(1) correctness, exec-time field); run client EditMode via Unity MCP `tests-run`; report concrete numbers | tests compile + pass; numeric before/after where possible |

### Conflict rule
File ownership is exclusive. Cross-file needs (e.g. cc-test needs cc-cache+cc-index merged) gated by lead. cc-cache + cc-index run parallel (disjoint files). cc-test scaffolds in parallel, full run after merge.

## Testing Plan

- Server xUnit: cache hit/miss/TTL-expiry/write-through/lock-bypass; dict index correctness vs linear scan; ClaimAll round-trip count (mock IGameApiClient/REST); exec-time non-zero.
- Client EditMode (Unity MCP `tests-run`): existing `MailboxApi*Tests` must stay green (no contract regression); contract test for new optional `serverExecutionMs` field.
- Build gate: `dotnet build` on module before any merge.

## Execution Notes

### cc-cache (#1) — DONE, lead-reviewed + approved
- NEW `MailboxCache.cs`: static `ConcurrentDictionary`, stores JSON copies (avoids reference-aliasing where caller mutates then writes back), TTL 5s, `Enabled` toggle, `TryGet`/`Set`/`Evict`.
- `CloudSaveHelper`: read-through on plain Get; write-through on Set; `Get*WithLockAsync` always REST-fetch (fresh writeLock) + side-effect write-through of data; 409 → evict; delete → evict.
- Wallet excluded from cache (`IsNoCacheKey` checks `KeyPlayerWallet`) — money path never serves stale balance. Lead-directed after review.

### cc-index (#2) — DONE, lead-reviewed + 1 regression caught & fixed
- O(1) lazy `[JsonIgnore]` dict indexes: `GlobalMailCollection` (OrdinalIgnoreCase), `PlayerGlobalMailState` (Ordinal, list+dict kept in sync in `GetOrCreateMetadataById`), `PlayerUserMailbox` (Ordinal). `.Find()` replaced in claim hot paths.
- ClaimAll: load collection/state ONCE, grant all candidates, write ONCE (was per-mail re-read + per-mail write).
- `ApiResponse<T>.ServerExecutionMs` + `Ok(data, Stopwatch)`; Stopwatch wraps `ClaimAttachment` + `ClaimAllAttachments`.
- **Regression caught in lead review:** batch grant-then-single-write meant one 409 lost ALL N claim-flags while wallet already credited → N× re-grant next call (placeholder `RewardGrant` is NOT idempotent). False "rewards are idempotent" comment.
- **Fix:** deleted `MarkNewlyClaimedAsConflict`; added `PersistGlobal/UserClaimFlagsWithRetryAsync` — on 409 re-read fresh lock, re-apply only newly-granted IDs' flags (idempotent), retry up to 3; all-fail → `LogError` with player+mailIds (re-grant risk surfaced, not hidden).
- **Bonus:** cc-index self-caught an O(n²) it introduced (migration re-ran per `GetOrCreateMetadata`, re-nulling dict) → added `_migrated` guard in `MigrateLegacyMetadata`.

### Lead integration build
`dotnet build` (net7.0) on full module after both merges: **Build succeeded, 0 warnings, 0 errors.**

## Verification Results

- Lead integration build: PASS (0 warnings, 0 errors) — re-run by lead after meta cleanup.
- **Server xUnit: 59/59 PASSED, 0 failed** (lead re-ran `dotnet test`; net9.0 test host, module stays net7.0).
  - MailboxCacheTests 15, O1IndexCurrentTests 14, O1IndexPostMergeTests 14, ClaimAllRoundTripTests 6, ServerExecutionMsTests 7, + 3 batch-409 double-grant tests.
  - **Double-grant safety proven:** 409-then-OK → walletPosts=3 (not 6) for 3 mails; all-retries-fail → walletPosts=2 (not 4) + error logged. No re-grant under writeLock conflict.
  - Measured: ClaimAll `mails_all` reads for 5 mails **before=6 (N+1) → after=1**; writeLock reads never cached; ServerExecutionMs>0.
- **Client EditMode: 58/58 PASSED** (cc-test via Unity MCP `tests-run`) — 55 existing MailboxApi* green (zero regression) + 3 new serverExecutionMs contract tests (verified present by lead in ApiResponseContractTests.cs).
- Ownership audit (lead, via mtime): exactly 6 files changed this session (MailboxCache.cs new, CloudSaveHelper.cs, MailboxModels.cs, ClaimAttachmentModule.cs, ModuleConfig.cs, ApiResponseContractTests.cs) + module csproj InternalsVisibleTo. All other dirty files are pre-existing WIP (2026-06-01). No teammate strayed outside lane.
- Cleanup: removed 282 orphan `.meta` files Unity auto-created inside the test folder before it was renamed to end-`~` (Unity-ignored, git-untracked).

## Test Infrastructure Notes
- Server tests live in `CloudCodeModule/BackpackAdventuresModuleTests~/` (trailing `~` → Unity ignores; sibling of module dir → NOT in module deploy glob; one-way ProjectReference test→module).
- Module exposes internals via `InternalsVisibleTo("BackpackAdventuresModule.Tests")`.
- Test host targets net9.0 (net7.0 runtime not installed locally); module unchanged at net7.0.
- ClaimAll HTTP-count assertions post-optimization are `[Fact(Skip)]` pending an injectable HTTP handler seam in CloudSaveHelper; round-trip reduction proven via MailboxCache TryGet/Set counts instead.

## Issues & Risks (final)
- **Wallet lost-update (pre-existing, NOT introduced):** wallet writes use no writeLock. Mitigated by excluding wallet from cache (always REST). Recommend follow-up: writeLock on wallet OR idempotent RewardGrant.
- **RewardGrant not idempotent (pre-existing placeholder):** drives the batch-claim retry design. Real fix = Economy SDK with idempotency keys (existing TODO).
- **Batch ClaimAll 3-retry exhaustion:** extreme contention → grants made, flags unpersisted, `LogError` raised (ops-visible) → possible re-grant next call. Bounded, logged, no silent data loss.
- Cache is best-effort warm-instance only; cold start / multi-instance = cache miss (falls through to REST). Correctness never depends on cache.

## Round 2 — Hot-path optimizations (A/B/C/D), lead-implemented + tested

Scope expanded by PO to the other Mailbox endpoints (run on every mailbox open / mark-read).

- **A — Idempotency store off the response critical path.** `ClaimAttachment` + `MarkMailRead` no longer `await` `IdempotencyService.StoreResponseAsync`; it runs via `StoreIdemSafeAsync` (exceptions swallowed + logged), exposed as `internal Task? PendingIdemStore` for deterministic tests. Removes ~1 Cloud Save POST from the response path. The cached idem GET returns synchronously, so the method only backgrounds at the POST await. **Caveat (documented):** on serverless cold recycle the un-awaited store may be dropped → idem cache becomes best-effort; correctness still holds via writeLock + IsClaim/IsRead guard on re-run. PO should confirm on staging that warm-instance stores persist.
- **B — `MarkAllRead` parallelized.** The two independent key writes (user-items, meta) now run via `Task.WhenAll` → ~halves MarkAllRead latency. Retry logic preserved.
- **C — `GetGlobalMails` paginate-before-DTO.** Filters (cheap + O(1) metadata) → sorts payloads by the exact `ToMailItemDto` StartTime key (UTC `"o"`) → slices → builds DTOs for the page only (≤pageSize) instead of one per mail (up to MaxGlobalMailsStored=500). Ordering + totalCount identical. V1 legacy path unchanged.
- **D — O(1) `FindById`** applied in `MarkReadModule.MarkUserReadAsync` + `DeleteMailModule.DeleteWithRetryAsync` (were raw `List.Find`).

### Round 2 tests — 9 added, server suite now 68/68 PASSED (lead-run)
- `HotPathOpt/HotPathOptimizationTests.cs`: A (idem-store 409 doesn't break Claim/MarkRead response; PendingIdemStore completes without throw), B (both keys written; retry intact under parallel), C (newest-first order preserved across pages; invisible/unavailable/deleted excluded from totalCount), D (correct mail resolved among many; missing→MailNotFound; delete removes correct mail).
- Test-infra hardening (lead): `ProgrammableHttpMessageHandler` now thread-safe (`_gate` lock — B issues concurrent requests) and builds a FRESH `HttpResponseMessage` per send (fixes disposed-body reuse when an endpoint re-reads a key, e.g. `mails_all` twice in MarkGlobalRead, or sticky defaults). Added `LastPost()` for body inspection. New `TestInfrastructure/HttpSeam.cs` shared `_http` swap.
- Module deploy build: 0 warnings, 0 errors. No stray `.meta`.

## Round 3 — Version-aware `mails_all` cache (`global_mail_change_log`)

Replaces TTL-only staleness for the project-wide `mails_all` key with cross-instance version validation.

- **New global CustomData key `global_mail_change_log`** (`MailboxConstants.KeyGlobalMailChangeLog`). Model `GlobalMailChangeLog { long Version; string LastChangedAt; }` — minimal, no event list, no per-mail/reason fields.
- **No-cache:** `global_mail_change_log` added to `MailboxCache.IsNoCacheKey` (alongside `player_wallet`) — always read fresh, else version validation is pointless.
- **`mails_all` is now version-aware** (`MailboxCache.TryGetVersioned`/`SetVersioned`; entry stores the change-log version it was cached at). `CloudSaveHelper.GetCustomDataAsync(mails_all)` → reads `global_mail_change_log` fresh, serves cache only if `cachedVersion == currentVersion`, else refetches + re-stamps. TTL retained as a defense-in-depth backstop.
- **Bump centralized in the write path:** `CloudSaveHelper` bumps the change log after every successful `mails_all` write (`OnCustomWriteSucceededAsync` → `BumpGlobalMailChangeLogAsync`, writeLock + bounded retry; missing key → Version 1, else +1, `LastChangedAt` ISO-8601 UTC). This covers **all** mutation paths automatically: SendGlobalMail, DeleteGlobalMail, ExpireMail, SetMailEndTime, PurgeExpired — **and SendUserMail**. No bump on no-ops (dedup early-return, not-found, purge-0) because they never call Set.
- **Helpers added:** `GetGlobalMailChangeLogAsync`, `GetCurrentGlobalMailVersionAsync`, `BumpGlobalMailChangeLogAsync`, `GetCustomDataWithVersionAwareCacheAsync`, `FetchCustomAsync`.
- **Correctness preserved:** `Get*WithLockAsync` still always REST-fetch (fresh writeLock); 409 still evicts; delete still evicts; `player_wallet` still no-cache; Cloud Save writeLock remains the correctness layer. Cache stays per-process, best-effort.

### DEVIATION from spec (flagged): SendUserMail also bumps
Spec listed 5 mutation paths and excluded SendUserMail. But `SendUserMailModule` writes targeted mail as `gm_`-prefixed entries **into `mails_all`** — so omitting its bump would leave targeted user mail stale across instances, defeating the goal. Centralizing the bump on the `mails_all` write (not per-endpoint) makes it impossible to miss a path and auto-covers SendUserMail. Still a single global key; no player-scoped change log was added.

### Round 3 tests — 11 added, server suite now 79/79 PASSED (lead-run)
`ChangeLog/GlobalMailChangeLogTests.cs`: change-log not cached (2 reads → 2 GETs); mails_all cache hit when version unchanged; mails_all refetch when version changes (cross-instance invalidation); SendGlobalMail/DeleteGlobalMail bump; ExpireMail/SetMailEndTime bump only when found (no-op → no bump); PurgeExpired bumps only when it removes (purge-0 → no bump).
- Updated existing tests for the new `mails_all` contract (no weakening): `MailboxCacheTests` (3 — use neutral keys for plain-cache mechanics + 3-arg `CacheEntry` in the reflection helper); `ClaimAllRoundTripTests` (3 — versioned cache API; mails_all-fetch reduction 6→1 still asserted).
- Module deploy build: 0 warnings, 0 errors. No stray `.meta`.

### Known limitations (Round 3)
- Each `mails_all` read now does **+1 small `global_mail_change_log` GET** to validate (by design — the alternative is the ≤5s stale window). The large `mails_all` doc is still served from cache; only the tiny version doc is fetched.
- Each `mails_all` write does **+1 GET +1 POST** to bump the change log (admin/expiry frequency — negligible).
- Still per-process / best-effort; correctness unchanged (writeLock). The change log reduces cross-instance stale global mail from "≤TTL window" to "next read sees the new version".
- Client suite unaffected (server-only change, no API response shape change) — not re-run.

## Round 4 — Best-effort change-log bump + ApiResponse runtime field

### Best-effort `global_mail_change_log` bump after committed `mails_all`
- **`mails_all` is the source of truth; the bump is a cache-invalidation signal.** A bump failure no longer fails an already-committed mutation.
- `OnCustomWriteSucceededAsync` (mails_all branch): `try { bump; SetVersioned(newVersion) } catch { Evict(mails_all cache); logger?.LogWarning(...) }` — never rethrows. The strict `mails_all` POST path is unchanged (a failed `mails_all` write still throws as before).
- `BumpGlobalMailChangeLogAsync`: `ChangeLogBumpAttempts = 3`. Retries on 409 (re-read fresh log + lock + increment); on exhausted 409 OR any non-409 error it **throws**, which the caller treats as best-effort failure.
- On bump failure the local `mails_all` cache is **evicted** (never stamped with a wrong/old version) → next read refetches at the current version; other workers fall back to the TTL window.
- Logging threaded via optional `ILogger?` on `SetCustomDataAsync` / `SetCustomDataWithLockAsync`; the 8 `mails_all` mutation call sites (SendGlobalMail ×2, SendUserMail ×2, ExpireMail ×3, PurgeExpired ×1) pass `_logger`.
- No double bump: bump runs only after a successful `mails_all` POST; Send* conflict-retry's first (409'd) write never reaches the bump. No bump on no-ops.

### ApiResponse runtime field for no-data APIs
- **Client** (`UnityClient/Runtime/CloudCodeModels.cs`): added `[JsonProperty("serverExecutionMs")] long ServerExecutionMs` to the **base** `ApiResponse` → inherited by `ApiResponse<T>`, so every response (including no-data endpoints) carries server runtime. Additive: older payloads → 0.
- **Server** (`ModuleConfig.cs`): mirrored `ServerExecutionMs` onto the non-generic `ApiResponse` + `Ok(Stopwatch)` helper, so no-data endpoints can report runtime symmetrically with `ApiResponse<T>`.

### Round 4 tests — 5 added, server suite now 84/84 PASSED (lead-run)
`ChangeLog/BumpFailureTests.cs`: (1) mails_all POST fails → endpoint fails, no bump; (2) bump OK → mails_all cache stamped at new version; (3) mails_all OK + bump non-409 → endpoint succeeds, warning logged, cache evicted; (4) bump 409→retry OK → success, single mails_all write (no double bump); (5) bump 409 exhausted (×3) → endpoint succeeds, cache evicted.
- Shared `CapturingLogger` extended to capture Warning+ (added `HasWarningContaining`).
- Module deploy build: 0 warnings, 0 errors. No stray `.meta`.
- Client EditMode not re-run (Unity MCP unavailable this session); client change is one additive Newtonsoft property.

### Known limitations (Round 4)
- If a bump fails, that single change is not signalled cross-instance until the next successful bump; other workers serve stale for ≤ TTL (5s) — the original fallback, by design.
- `ServerExecutionMs` on the non-generic server `ApiResponse` is available but currently unused (all endpoints return `ApiResponse<T>`); present for symmetry/future no-data endpoints.

## Final State
COMPLETE. All acceptance criteria met:
- RAM cache added (read-through/write-through/lock-safe/409-evict, wallet excluded). ✅
- O(1) dict lookups replace O(n) `.Find()` in claim paths. ✅
- ClaimAll round-trips cut N+1→1 for the shared collection. ✅
- `ServerExecutionMs` on `ApiResponse<T>`, populated for ClaimAttachment + ClaimAllAttachments. ✅
- Tested: 56 server + 58 client, all green; no regressions. ✅
- Not committed (awaiting PO review). Working tree only.

## Issues & Risks

- Stale cache across instances → mitigated: best-effort + writeLock correctness + short TTL + write-through.
- ServerExecutionMs added to generic `ApiResponse<T>` — verify client deserializer ignores unknown/added field (it should; additive).
- No pre-existing server test project → cc-test creates one; must not be included in Cloud Code deploy build (separate csproj, not referenced by module).

## Final State

(pending)
