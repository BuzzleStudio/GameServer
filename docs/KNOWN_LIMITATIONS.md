# Known Limitations — BackpackAdventures Mailbox System

This document honestly describes what the current mailbox system cannot do, where the implementation is incomplete, and what risks and constraints apply. Do not document things here as limitations if they are actually implemented.

---

## 1. Server-Side Handlers Implemented but Not Yet Staged in CI

All five mailbox Cloud Code functions have server-side handlers implemented. However, the Mailbox source files (`GetMailboxModule.cs`, `MarkReadModule.cs`, `ClaimAttachmentModule.cs`, `SendMailModule.cs`, `MailModels.cs`, `CloudSaveHelper.cs`) are currently untracked in git on the `feature/mailbox-cloudsave-system` branch. They will not be deployed until they are committed and the branch merges to `staging` to trigger the CI pipeline.

| Function | Server Handler File | Client DTOs | Deployment Status |
|----------|---------------------|-------------|-------------------|
| `SendGlobalMail` | `SendMailModule.cs` | Yes | Not committed yet |
| `SendUserMail` | `SendMailModule.cs` | Yes | Not committed yet |
| `GetMailbox` | `GetMailboxModule.cs` | Yes | Not committed yet |
| `MarkMailRead` | `MarkReadModule.cs` | Yes | Not committed yet |
| `ClaimAttachment` | `ClaimAttachmentModule.cs` | Yes | Not committed yet |

**Impact:** All mailbox functions will return 404 until the feature branch is committed and merged to staging.

---

## 2. No Admin-Only Authorization for `SendGlobalMail` and `SendUserMail`

Any authenticated player can call `SendGlobalMail` and `SendUserMail`. There is no role-based access control, API key, or admin token enforcement at the Cloud Code function level.

**Consequence:** A player who knows the function name can broadcast arbitrary mail to all players or spam another player's mailbox with any content they choose.

**Workaround (not implemented):** An admin-token pattern could be enforced by checking `IExecutionContext.PlayerId` against an allowlist stored in Cloud Save custom data, or by requiring a signed payload. Neither is implemented.

---

## 3. No Pagination on `GetMailbox` (Planned Feature)

When `GetMailbox` is implemented, the planned design returns the full list of global mails and user mails in a single response. There is no cursor-based or offset-based pagination.

**Impact:** As the number of global mails grows, the response payload grows with it. Combined with Cloud Save's size limit (see below), this will eventually cause failures.

---

## 4. Cloud Save Size Limits

Cloud Save limits individual key values to approximately 5 MB of JSON. The `global_mails` key holds the entire broadcast mail list in one blob.

**Consequence:** A `global_mails` list with hundreds of entries, especially if each entry includes attachment data, can approach or exceed the 5 MB limit. No pruning, archiving, or overflow strategy is implemented.

**Impact when limit is hit:** `SetCustomItemAsync` will return an error from the Cloud Save API. `SendGlobalMail` will throw, and no mail will be stored.

**No expiration-based cleanup is implemented.** `ExpiresAt` is stored in the mail record but no server-side cleanup job removes expired mails from Cloud Save. The list only grows.

---

## 5. Economy Integration Not Implemented

The `ClaimAttachment` server handler (`ClaimAttachmentModule.cs`) returns attachment data from the mail record but does not call any Economy service to grant items or currency to the player.

**Current state:** `ClaimAttachment` writes the claim ID to `mailbox_state.ClaimedIds` and returns `ClaimedItems` from the mail record. No `IEconomyApiClient` call is made. The player receives no in-game reward from the claim operation.

**Consequence:** Claiming a mail attachment with gems, items, or other economy rewards has no effect on the player's actual inventory or wallet. The claim is recorded (preventing re-claim), but the reward is never granted.

---

## 6. Client-Server DTO Contract Mismatches

Multiple field name and shape mismatches exist between server-side response models and client-side DTOs. JSON deserialization silently drops unmatched fields, so these mismatches do not throw — they produce null or zero values on the client without any visible error.

**`MailAttachment` field names:**

| Field | Server model | Client model |
|-------|-------------|--------------|
| Item identifier | `ItemId` | `id` |
| Quantity | `Quantity` | `amount` |

**`ClaimAttachment` response fields:**

| Field | Server model | Client model |
|-------|-------------|--------------|
| Claimed items | `ClaimedItems` | `claimedAttachments` |
| Already claimed | Not returned (throws instead) | `alreadyClaimed` bool |

**`MarkMailRead` request/response:**

| Field | Server model | Client model |
|-------|-------------|--------------|
| Mail IDs input | `MailIds` (List) | `mailId` (single string) |
| Response read flag | Not returned | `isRead` bool |

**This must be reconciled across all affected files before any attachment data or mark-read results are usable on the client.**

---

## 7. No Push Notification on Mail Receipt

Sending a global or user mail does not trigger any push notification to the recipient. Players will only see new mail when they explicitly call `GetMailbox`. There is no webhook, Firebase Cloud Messaging integration, or in-game socket event implemented.

---

## 8. No Expiration Enforcement at Write Time

`ExpiresAt` is stored in the mail record but is not validated at write time. A caller can provide an expiration date in the past, a malformed date string, or omit it entirely. No date parsing or range check is performed by `SendGlobalMail` or `SendUserMail`.

**Impact at read time:** `GetMailbox` (when implemented) is expected to filter out expired mails client- or server-side. If the filter is not implemented, expired mails will be returned to the player indefinitely.

---

## 9. No Rate Limiting

There is no per-player or global rate limit on any mailbox function. A caller can send thousands of mails in rapid succession. Combined with the lack of admin-only authorization, this creates a denial-of-service vector against the `global_mails` Cloud Save key.

---

## 10. `mailbox_state` Grows Indefinitely

`mailbox_state.ReadIds` and `mailbox_state.ClaimedIds` are append-only lists. They are never pruned, even when the corresponding mail has expired or been deleted. Over time, a player's `mailbox_state` record will accumulate IDs that refer to mails that no longer exist in `global_mails` or `user_mails`.

**Impact:** The `mailbox_state` JSON will grow without bound. This counts toward the player's Cloud Save storage quota.

---

## 11. No Test Coverage for Mailbox Functions

No unit tests or integration tests exist for `SendGlobalMail`, `SendUserMail`, or the planned handlers. The `CloudCodeIntegrationTest` MonoBehaviour in `UnityClient/Tests/` only covers the original three functions (HealthCheck, PlayerEcho, ServerConfig).
