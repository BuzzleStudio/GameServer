# Known Limitations — BackpackAdventures Mailbox System

Current state as of `feature/mailbox-cloudsave-system` (all five functions committed and building cleanly).

---

## 1. No Admin-Only Authorization for SendGlobalMail and SendUserMail

Any authenticated player can call `SendGlobalMail` and `SendUserMail`. There is no role-based access control, API key, or admin token enforcement at the Cloud Code function level.

**Consequence:** A player who knows the function name can broadcast mail to all players or spam another player's mailbox.

**Workaround (not implemented):** Check `IExecutionContext.PlayerId` against an allowlist stored in Cloud Save custom data, or require a signed payload. Call these functions only from trusted admin panels, never from game client UI, until authorization is added.

---

## 2. ClaimAttachment Has No Atomic Guard Against Double-Claim

The claim operation uses a read-then-write pattern: read `mailbox_global_state` or `mailbox_user_items`, check the `AttachmentClaimed` flag, then write the updated state. Cloud Save is last-write-wins with no native transactions.

**Race condition:** Two simultaneous claim requests from the same player may both read `AttachmentClaimed = false` before either write completes, causing both to proceed and potentially granting the reward twice.

**Mitigation in place:** The idempotent check reduces the risk to the narrow window between the two reads. For a mobile game with single active sessions per player this window is extremely narrow in practice.

**Not acceptable for high-value rewards.** For gem or premium-currency attachments, an Economy service idempotency key or a dedicated claim-lock Cloud Save key is required.

---

## 3. Economy Integration Not Implemented

`ClaimAttachment` records the claim in Cloud Save and returns the attachment data (`ClaimedAttachments[]`) but does not call any Economy service to grant items or currency.

**Current behavior:** The player's inventory and wallet are unaffected by claiming. The game client must read `ClaimedAttachments` from the response and call the Economy API separately.

---

## 4. Cloud Save Size Limits on global_mail_index

Cloud Save limits individual key values to approximately 5 MB of JSON. The `global_mail_index` key holds the entire broadcast mail list in one blob.

**Consequence:** A large number of global mails (hundreds with attachments) can approach or exceed the limit. `SendGlobalMail` will throw when the limit is hit.

**No cleanup is implemented.** Expired mails are filtered at read time (`GetMailbox`) but are never deleted from Cloud Save. The key only grows.

---

## 5. mailbox_global_state Grows Indefinitely

`PlayerGlobalMailState.ReadIds` and `PlayerGlobalMailState.ClaimedIds` are append-only lists that are never pruned, even when the referenced global mail has expired. This counts toward the player's Cloud Save storage quota.

---

## 6. No Pagination on GetMailbox

`GetMailbox` returns all non-expired mails in a single response. With many global mails this increases payload size and Cloud Code execution time.

**Acceptable for MVP.** Add cursor-based pagination if global mail list exceeds ~50 entries.

---

## 7. ExpiresAt Not Validated at Write Time

`SendGlobalMail` and `SendUserMail` accept any `expiresAt` string without parsing or range validation. A past date, malformed string, or null value is stored as-is. Filtering is applied at read time in `GetMailbox`.

---

## 8. No Rate Limiting

No per-player or global rate limit exists on any mailbox function. Combined with the lack of admin authorization (#1), this is a denial-of-service vector on the `global_mail_index` Cloud Save key.

---

## 9. No Push Notification on Mail Receipt

Sending a mail does not trigger any push notification. Players see new mail only when they call `GetMailbox`.
