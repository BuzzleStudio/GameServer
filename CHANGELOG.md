# CHANGELOG — BackpackAdventures Cloud Code Module

All notable changes to this project are documented here.

Format: each entry names the exact function, file, or class changed, the business reason, the design reasoning, and known risks.

---

## [Unreleased] — feature/mailbox-cloudsave-system

### Changed: ClaimAttachment accepts raw mail id requests

**What changed:** `ClaimAttachment` now normalizes both object requests (`{ mailId, mailType?, requestId? }`) and raw string requests (`"gm_..."`) into `ClaimAttachmentRequest`.

**Client impact:** Existing `BackpackCloudCodeService.CallClaimAttachmentAsync` calls still use the object request. Direct `CloudCodeService.Instance.CallModuleEndpointAsync` callers may pass `["request"] = messageId` when they only need to send a mail id.

**Test update:** Fake backend dispatch supports the raw string request and `P09A_ClaimAttachment_StringRequest_Global_GrantsReward` covers the compact direct-call form.

### Changed: Global mail player metadata JSON names

**What changed:** Per-player global mail state now writes Cloud Save key `mail_meta_state` as `{ "MailMetadata": [...] }`. Metadata items serialize claim/delete flags as `IsClaimed` and `IsDeleted` instead of the older `IsClaim` and `IsDelete` names.

**Backward compatible:** Existing records using root `Mails`, `IsClaim`, or `IsDelete` still deserialize. The next metadata write normalizes them to `MailMetadata`, `IsClaimed`, and `IsDeleted`.

**Test update:** Mail sent through `CallAdminSendUserMail` is targeted admin mail stored in the global admin-mail store, so tests now assert visibility and claim state through `GetGlobalMails`. The old user-mail reward eviction test is explicit-only because admin targeted mail no longer fills `mailbox_user_items`.

### Added: ClaimAllAttachments mailbox API

**What changed:** Added Cloud Code function `ClaimAllAttachments` in `ClaimAttachmentModule`. It claims all visible, unexpired reward mails for the current player across `all`, `global`, or `user` scope, reusing the existing single-mail claim logic for reward grants and claim/read state updates.

**Client update:** Added `ClaimAllAttachmentsRequest`, `ClaimAllAttachmentResult`, `ClaimAllAttachmentsResponse`, and `BackpackCloudCodeService.CallClaimAllAttachmentsAsync`. The Mailbox editor window now has a `Claim All` action for the selected scope. The fake EditMode backend supports the endpoint and a positive test covers user + global reward claims.

**Known risk:** The placeholder Cloud Save wallet grant still does not provide true external Economy idempotency. The bulk endpoint derives stable per-mail request IDs, but the underlying grant service is still the same Cloud Save-backed implementation used by `ClaimAttachment`.

### Changed: mails_all wraps mails under a "Mails" property

**What changed:** The `mails_all` Cloud Save value is now an object `{ "Mails": [ … ] }` whose array elements are bare mail objects (`{ "MessageId": …, "Title": …, … }`). Introduced `GlobalMailCollection` (with `GlobalMailCollectionConverter`) as the stored type; the 8 mailbox modules read/write it and operate on `.Mails`. Inner elements stay bare via `GlobalMailPayloadConverter`.

**Backward compatible (in-place migration):** The converter reads ALL prior shapes — a legacy raw array `[ … ]` is wrapped under `Mails` in order, the new `{ "Mails": [ … ] }` object reads directly, and per-element `{ "Mail": { … } }` wrappers still deserialize. Mail fields are preserved exactly; nothing is dropped, merged, or mapped to `IsRead`/`IsClaim`/`IsDelete` state. The next write normalizes the key to `{ "Mails": [ … ] }`. A foreign/malformed value reads as empty and self-heals on the next write. Verified by round-trip test (legacy-array → wrapped, multi-mail order preserved, wrapped round-trip, foreign→empty, legacy per-element wrapper).

### Fixed: Malformed Cloud Save value no longer 422s every mailbox endpoint

**What changed:** `CloudSaveHelper.DeserializeValue<T>` now shape-checks the stored JSON before deserializing and returns `default` (instead of throwing `JsonException`) when the value cannot map to `T` — for example an object `{…}` stored under a key the code reads as `List<GlobalMailPayload>` (array `[…]`).

**Why:** A foreign/stale value under the shared, project-wide `mails_all` key caused `JsonException` (`Path: $`, byte 1) on read, which Cloud Code surfaced as **HTTP 422**. Because every admin/mailbox endpoint (`SendGlobalMail`, `SendUserMail`, `GetGlobalMails`, `PurgeExpired`, `ExpireMail`, `DeleteMail`, `ClaimAttachment`, `MarkMailRead`) reads that one key, a single bad value bricked the entire mailbox.

**Behavior update:** A malformed shared-key value is now treated as "no data yet." The next write (with the still-valid `writeLock`) overwrites and self-heals the key with the correct shape. Field-level data on a matching shape is preserved unchanged. No client change required.

### Changed: Admin mail storage uses mails_all

**What changed:** Admin global and targeted mail no longer writes split custom-data keys (`global_mail_index` + `mail_global_{mailId}`). New sends write all admin mail payloads into the custom-data key `mails_all` as an array of `{ "Mail": { ... } }` objects.

**Behavior update:** `GetGlobalMails`, `ClaimAttachment`, `MarkMailRead`, `DeleteMail`, `ExpireMail`, `SetMailEndTime`, `DeleteGlobalMail`, and `PurgeExpired` now read/update the matching object inside `mails_all`. Per-player state is still stored in `mail_meta_state`, and user-to-user `GiftMail` still uses `mailbox_user_items`.

**Dedup update:** `DedupKey` remains supported without adding a field to `mails_all`; when a dedup key is provided, the server derives a stable `MessageId` from that key and deduplicates by `MessageId`.

### Changed: Admin Manage Mail supports EndTime update and hard delete

**What changed:** `AdminMailWindow` Manage Mail now supports project-scoped REST admin actions without Play Mode: `Set EndTime`, `Expire Global`, and `Delete Global`.

**Backend update:** Added `SetMailEndTime` to update `Mail.EndTime` for a global mail ID. Added `DeleteGlobalMail` to remove the matching `{ Mail }` object from Cloud Save. `ExpireMail` remains a soft expire operation that sets the end time to now.

**Cloud Save JSON update:** Removed mailbox storage `Version` properties from the server models, so new Cloud Save writes no longer include `"Version"` in mailbox payload/index/state JSON.

### Changed: Admin mail EndTime can be null

**What changed:** `Mail.EndTime` is now nullable for admin-authored global and targeted mail. `SendGlobalMail` and the compatibility `SendUserMail` wrapper no longer default a blank `expiresAt` to seven days; blank/null `expiresAt` stores `EndTime = null`.

**Editor update:** `AdminMailWindow` now exposes two explicit options for `MailInfo.EndTime`: `Null / no expiration` and `Use UTC time`. When UTC time is selected, the editor provides separate date/time fields plus +1d/+7d/+30d presets and validates the input before sending.

**Docs updated:** `README.md`, `docs/API_CONTRACTS.md`, `docs/MAILBOX_API_USAGE.md`, and `docs/KNOWN_LIMITATIONS.md` now describe nullable `EndTime` behavior.

### Fixed: Admin mailbox global storage deploy compile issue

**What changed:** Fixed the Cloud Code module compile error in `ClaimAttachmentModule` by using the `Mail.IsExpired` property correctly after the admin mail payload schema changed from `MailItemDto` to `Mail`.

**Storage contract update:** Superseded by the later `mails_all` storage change. Admin-authored mail now lives in `mails_all`, while per-player state remains in `mail_meta_state`. `TargetUserIds = null` means broadcast; a non-empty `TargetUserIds` list means targeted admin mail. User-to-user `GiftMail` remains in `mailbox_user_items`.

**Docs updated:** `README.md` and `docs/API_CONTRACTS.md` now describe the current Cloud Save keys and targeted admin mail behavior.

### Added: Mailbox system — Cloud Save-backed in-game mail

---

#### `SendGlobalMail` [CloudCodeFunction] — `CloudCodeModule/BackpackAdventuresModule/Mailbox/SendMailModule.cs`

**What changed:** New Cloud Code function that appends a `GlobalMail` record to the Cloud Save custom-data key `global_mails` (project-scoped, not player-scoped). Any authenticated player can trigger the call; the sender's `IExecutionContext.PlayerId` is recorded as `SenderId`.

**Business reason:** Game needs a broadcast channel for server-side events — maintenance rewards, seasonal promotions, patch notes with attachment rewards. These must be authored from a backend script (or admin tool) rather than from the client.

**Design reasoning:** Custom-data keys in Cloud Save are project-scoped rather than tied to a single player record, which makes them the natural store for global/broadcast content. The function appends to an existing list rather than overwriting it so that concurrent sends during a deployment window do not race. A `Guid.NewGuid()` mail ID is assigned server-side to prevent client-spoofed IDs.

**Files modified:** `CloudCodeModule/BackpackAdventuresModule/Mailbox/SendMailModule.cs` (new), `CloudCodeModule/BackpackAdventuresModule/Mailbox/MailModels.cs` (new)

**API added:**
- Function name: `SendGlobalMail`
- Input: `MailContent { Subject, Body, ExpiresAt?, Attachments? }`
- Output: `SendMailResponse { Success, MailId }`

**Data structure introduced:**
- `GlobalMail` — stored in Cloud Save custom key `global_mails` as a JSON array
- Fields: `MailId` (GUID string), `Subject`, `Body`, `CreatedAt` (ISO 8601 UTC), `ExpiresAt` (nullable ISO 8601 UTC), `SenderId`, `Attachments` (nullable list)
- `MailAttachment` — sub-object: `Type`, `ItemId`, `Quantity`

**Validation added:**
- `Subject` must be non-null and non-whitespace — throws `ArgumentException("Subject is required")`
- `Body` must be non-null and non-whitespace — throws `ArgumentException("Body is required")`
- `MailContent` itself must be non-null — throws `ArgumentNullException`

**Edge cases handled:**
- If `global_mails` key does not yet exist in Cloud Save, `GetCustomDataAsync` returns `null` and the function starts a fresh list. No bootstrap step needed.
- `ExpiresAt` and `Attachments` are optional and may be null.

**Known risks:**
- No authorization check: any authenticated player can call `SendGlobalMail`. There is no admin-only enforcement at the Cloud Code layer. A malicious player can broadcast spam mail. See `docs/KNOWN_LIMITATIONS.md`.
- Cloud Save custom-data item size limit: Cloud Save limits individual keys to approximately 5 MB of JSON. A global mail list with many mails and large attachment payloads can approach this limit. No pruning or pagination is implemented yet.
- No push notification is triggered after send. Players only see new global mail when they call `GetMailbox`.

**Migration concerns:** None — key is created on first write.

---

#### `SendUserMail` [CloudCodeFunction] — `CloudCodeModule/BackpackAdventuresModule/Mailbox/SendMailModule.cs`

**What changed:** New Cloud Code function that appends a `UserMail` record to the Cloud Save player-data key `user_mails` of the specified target player. The caller provides `TargetUserId` and a `MailContent` payload.

**Business reason:** Supports player-to-player or system-to-player targeted mail — e.g., expedition results delivered to a specific player, or support compensation targeted by player ID.

**Design reasoning:** Player-data keys in Cloud Save are scoped to a single player's record. Writing to another player's player-data key requires the server-side access token from `IExecutionContext`, which is only available inside a Cloud Code function — this is the correct and only viable pattern for cross-player writes without exposing admin credentials to clients.

**Files modified:** `CloudCodeModule/BackpackAdventuresModule/Mailbox/SendMailModule.cs`

**API added:**
- Function name: `SendUserMail`
- Input: `SendUserMailRequest { TargetUserId, Content: MailContent }`
- Output: `SendMailResponse { Success, MailId }`

**Data structure introduced:**
- `UserMail` — stored in Cloud Save player key `user_mails` as a JSON array
- Same fields as `GlobalMail`
- `SendUserMailRequest` — wrapper binding `TargetUserId` (string) and `Content` (MailContent)

**Validation added:**
- `TargetUserId` must be non-null and non-whitespace — throws `ArgumentException("TargetUserId is required")`
- Full `ValidateContent` check applied to `Content` (same as `SendGlobalMail`)

**Edge cases handled:**
- If target player has no existing `user_mails` key, `GetPlayerDataAsync` returns null and the function initializes a fresh list.
- Self-send (caller sends to their own player ID) is not blocked — no business rule prohibits it.

**Known risks:**
- No authorization check: any authenticated player can send a mail to any other player by ID. A caller who knows a victim's player ID can fill their mailbox. Rate limiting is not implemented.
- Same Cloud Save size limit risk as `SendGlobalMail`.

**Migration concerns:** None.

---

#### `GetMailbox` [CloudCodeFunction] — `CloudCodeModule/BackpackAdventuresModule/Mailbox/GetMailboxModule.cs`

**What changed:** New Cloud Code function that reads `global_mails`, `user_mails`, and `mailbox_state` concurrently via `Task.WhenAll`, filters expired mails at read time, merges both lists, decorates each item with `IsRead` and `IsClaimed` from player state, and returns items sorted descending by `CreatedAt`.

**Business reason:** Players need a single API call to retrieve their full mailbox so the client can render read/unread state, attachment availability, and expiry without requiring multiple calls.

**Design reasoning:** `Task.WhenAll` is used to parallelize the three Cloud Save reads to minimize latency. Expiry is enforced server-side by parsing `ExpiresAt` as a `DateTimeOffset` and comparing to `DateTime.UtcNow` — this prevents clients from bypassing expiry by avoiding the filter. String comparison on ISO 8601 dates is used for sort order to avoid a second parse pass.

**Files modified:** `CloudCodeModule/BackpackAdventuresModule/Mailbox/GetMailboxModule.cs` (new), `UnityClient/Runtime/CloudCodeModels.cs` (DTOs added), `UnityClient/Runtime/BackpackCloudCodeService.cs` (client method added)

**API added:**
- Function name: `GetMailbox`
- Input: None
- Output: `GetMailboxResponse { Success, Mails: List<MailboxItem> }`

**Validation:** No input validation required — all inputs are derived from player context.

**Edge cases handled:**
- Missing Cloud Save keys (no mails yet, no state yet) return empty lists rather than errors.
- Mails where `ExpiresAt` cannot be parsed as a date are treated as non-expired (safe default).

**Known risks:**
- No pagination: the full mail list is returned in one response. Large lists combined with the Cloud Save 5 MB limit can cause failures.
- Sort is lexicographic on ISO 8601 strings; only correct if all timestamps use the same format (ISO 8601 with `o` format specifier, which the server enforces on write).

---

#### `MarkMailRead` [CloudCodeFunction] — `CloudCodeModule/BackpackAdventuresModule/Mailbox/MarkReadModule.cs`

**What changed:** New Cloud Code function that accepts a list of mail IDs (`MailIds: List<string>`) and adds them to `mailbox_state.ReadIds` in Cloud Save player data. Uses a `HashSet` to deduplicate before writing, making repeated calls for the same IDs idempotent.

**Business reason:** Track which mails the player has opened to power unread-badge counts and suppress re-alerting on subsequent `GetMailbox` calls.

**Design reasoning:** Accepting a list (batch) rather than a single ID reduces Cloud Save write operations per session. Using a `HashSet` on both the existing state and incoming IDs prevents the `ReadIds` list from accumulating duplicate entries on client retry.

**Files modified:** `CloudCodeModule/BackpackAdventuresModule/Mailbox/MarkReadModule.cs` (new), `UnityClient/Runtime/CloudCodeModels.cs`, `UnityClient/Runtime/BackpackCloudCodeService.cs`

**API added:**
- Function name: `MarkMailRead`
- Input: `MarkReadRequest { MailIds: List<string> }`
- Output: `MarkReadResponse { Success }`

**Validation:** `MailIds` must be non-null and non-empty — throws `ArgumentException("MailIds must not be empty")`.

**Known risks:**
- Client-server contract mismatch: the client-side `MarkMailReadRequest` sends a single `mailId` field, but the server expects `MailIds` (a list). The response also differs — client DTO has `isRead` field but server returns only `{ success: true }`. This mismatch may cause serialization issues. Alignment needed before release.
- `mailbox_state.ReadIds` grows indefinitely. No cleanup on mail expiry or deletion.

---

#### `ClaimAttachment` [CloudCodeFunction] — `CloudCodeModule/BackpackAdventuresModule/Mailbox/ClaimAttachmentModule.cs`

**What changed:** New Cloud Code function that checks `mailbox_state.ClaimedIds` before granting an attachment. If the mail ID is already in `ClaimedIds`, throws `InvalidOperationException` (resulting in a 500 error to the client). If not claimed, finds the attachment data by searching user mails then global mails, writes the updated state, and returns the attachment list. Also marks the mail as read as a side effect.

**Business reason:** Prevent duplicate reward grants when a player's claim request succeeds server-side but the response is lost to a network timeout, causing the client to retry.

**Design reasoning:** The check-before-grant pattern (read `ClaimedIds` → check → find attachment → write state → return) is the simplest correct approach given that Economy integration is not implemented. The write is done before returning so the state is persisted even if the response is dropped. Side-effect read-marking avoids a second API call when a player claims from an unread mail.

**Files modified:** `CloudCodeModule/BackpackAdventuresModule/Mailbox/ClaimAttachmentModule.cs` (new), `UnityClient/Runtime/CloudCodeModels.cs`, `UnityClient/Runtime/BackpackCloudCodeService.cs`

**API added:**
- Function name: `ClaimAttachment`
- Input: `ClaimAttachmentRequest { MailId: string }`
- Output: `ClaimAttachmentResponse { Success, ClaimedItems: List<MailAttachment> }`

**Validation:**
- `MailId` must be non-null and non-whitespace — throws `ArgumentException("MailId is required")`
- If mail has no attachments or mail not found — throws `InvalidOperationException("No attachments found for mail {id}")`
- If already claimed — throws `InvalidOperationException("Attachment for mail {id} has already been claimed")`

**Edge cases handled:**
- Attachment lookup searches user mails before global mails. If the same `mailId` appears in both (should not happen normally), user mail takes priority.
- Already-claimed path throws rather than returning `alreadyClaimed: true` — this differs from the client DTO contract which expects an `alreadyClaimed` boolean. Clients must catch exceptions to detect this case.

**Known risks:**
- Grant-before-state-write risk is partially mitigated: state is written before returning. However, if the Cloud Save write fails (transient error), the client receives a 500 and has no way to distinguish "write failed, retry safe" from "already claimed". Retrying after a failed write will re-trigger the claim and potentially re-grant if Economy is added later.
- Economy integration is not implemented. Attachment data is returned but no actual currency or item grant is performed against any Economy service.
- Client-server DTO mismatch: server response field is `ClaimedItems`, client model field is `claimedAttachments`. Attachment sub-fields also differ (`ItemId`/`Quantity` server vs `id`/`amount` client).

---

#### `CloudSaveHelper` [Internal utility] — `CloudCodeModule/BackpackAdventuresModule/Mailbox/CloudSaveHelper.cs`

**What changed:** New internal static class centralizing all typed Cloud Save read/write operations used by the mailbox modules. Defines the three key name constants (`GlobalMailsKey`, `UserMailsKey`, `MailboxStateKey`) and four generic methods: `GetPlayerDataAsync<T>`, `SetPlayerDataAsync<T>`, `GetCustomDataAsync<T>`, `SetCustomDataAsync<T>`.

**Design reasoning:** Extracting Cloud Save I/O into a shared helper removes code duplication between `GetMailboxModule`, `MarkReadModule`, and `ClaimAttachmentModule`. All Cloud Save deserialization errors in reads silently return `default` rather than propagating — this matches the convention in `SendMailModule` and avoids failures when a key has never been written.

---

#### `MailModels.cs` — `CloudCodeModule/BackpackAdventuresModule/Mailbox/MailModels.cs`

**What changed:** New file defining all server-side domain types for the mailbox system.

**Data structures introduced:**
- `MailAttachment { Type, ItemId, Quantity }` — server model (note: client model uses `id` and `amount` — the field names diverge and must be reconciled before deployment)
- `GlobalMail { MailId, Subject, Body, CreatedAt, ExpiresAt?, SenderId, Attachments? }`
- `UserMail` — identical shape to `GlobalMail` (separate type to allow future divergence)
- `MailboxState { ReadIds: List<string>, ClaimedIds: List<string> }` — persisted in Cloud Save player key `mailbox_state`
- `MailContent { Subject, Body, ExpiresAt?, Attachments? }` — input shape for send operations
- `MailboxItem { MailId, Subject, Body, CreatedAt, ExpiresAt?, SenderId, IsRead, IsClaimed, Attachments? }` — unified output shape for `GetMailbox`

**Known risks:** `MailAttachment` field names differ between server (`ItemId`, `Quantity`) and client (`id`, `amount`). JSON deserialization will silently drop mismatched fields. This must be aligned before `GetMailbox` or `ClaimAttachment` returns attachment data to the client.

---

#### `BackpackCloudCodeService.cs` — `UnityClient/Runtime/BackpackCloudCodeService.cs`

**What changed:** Added 5 public static async methods for the mailbox API: `SendGlobalMailAsync`, `SendUserMailAsync`, `GetMailboxAsync`, `MarkMailReadAsync`, `ClaimAttachmentAsync`. All methods wrap Cloud Code calls in `WithTimeout` (10-second deadline) and log via `Debug.Log`/`Debug.LogError`.

**Design reasoning:** Centralizing all Cloud Code calls in a single static service keeps the call site simple and ensures the module name (`BackpackAdventuresModule`) is not scattered across the codebase.

**Files modified:** `UnityClient/Runtime/BackpackCloudCodeService.cs`

---

#### `CloudCodeModels.cs` — `UnityClient/Runtime/CloudCodeModels.cs`

**What changed:** Added `[Serializable]` DTOs for all mailbox request/response types. Client-side `MailAttachment` uses field names `type`, `id`, `amount` (camelCase public fields). `MailItem` uses `attachmentClaimed` for the claim flag.

**Files modified:** `UnityClient/Runtime/CloudCodeModels.cs`

---

## Earlier Changes (pre-mailbox, merged to develop/staging)

### Fixed: `PlayerEcho` argument wrapping — `UnityClient/Runtime/BackpackCloudCodeService.cs`

Arguments must be wrapped under a `"request"` key: `{ "request": { "playerId": "..." } }`. The Cloud Code C# module expects the parameter named `request` at the top level. Direct key injection (`{ "playerId": "..." }`) does not match the module's parameter binding.

### Fixed: `MODULE_NAME` constant — `UnityClient/Runtime/BackpackCloudCodeService.cs`

Corrected from `"BackpackAdventures"` to `"BackpackAdventuresModule"` to match the deployed assembly name. The UGS function lookup is case-sensitive against the module's registered name.

### Fixed: `LimitRequestBodySize` removal — `CloudCodeModule/BackpackAdventuresModule/`

Removed call to `LimitRequestBodySize` which is not available in `Com.Unity.Services.CloudCode.Core` version 0.0.4. Attempting to call it caused a build failure.

### Fixed: UGS CLI login flags — `.github/workflows/staging-deploy.yml`

Corrected `--service-account-key-id` to `--service-key-id` to match the current UGS CLI flag names. Secret piped via stdin with `--secret-key-stdin` because the non-interactive CI shell does not support interactive prompts.

### Fixed: CCMR path — `.github/workflows/staging-deploy.yml`

Corrected deploy artifact path to `CloudCodeModule/BackpackAdventures.ccmr`. Earlier runs referenced an incorrect subdirectory path that did not exist in the repository.
