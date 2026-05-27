# Mailbox System — Test Matrix

Module: `BackpackAdventuresModule`
Branch: `feature/mailbox-cloudsave-system`
Date: 2026-05-27
Author: QA / Tester agent

---

## Legend

| Column | Meaning |
|--------|---------|
| ID | Unique test identifier (P=positive, N=negative, E=edge) |
| Severity | BLOCKER / HIGH / MEDIUM / LOW |
| Status | PENDING (not yet run against live backend) |

---

## Positive Tests

### P01 — SendGlobalMail (notification only)

**Input:**
```json
{ "subject": "Server maintenance", "body": "Maintenance at midnight UTC." }
```
**Expected output:**
```json
{ "success": true, "mailId": "<non-empty UUID>", "sentAt": "<ISO-8601 UTC>" }
```
**Pass criteria:** `success=true`, `mailId` non-empty string, `sentAt` is valid UTC timestamp.
**Notes:** No `expiresAt` or `attachments` fields — both must be optional on the server.

---

### P02 — SendGlobalMail (with currency attachment)

**Input:**
```json
{
  "subject": "Login reward",
  "body": "Here is your reward!",
  "expiresAt": "<UTC + 1 hour>",
  "attachments": [{ "type": "currency", "id": "gold", "amount": 500 }]
}
```
**Expected output:** `success=true`, non-empty `mailId`.
**Pass criteria:** As above. Attachment stored server-side and visible via GetMailbox.

---

### P03 — SendUserMail (notification only)

**Input:**
```json
{ "userId": "<targetPlayerId>", "subject": "Friend request", "body": "Player X wants to be your friend." }
```
**Expected output:** `success=true`, non-empty `mailId`.
**Pass criteria:** Mail appears in target player's mailbox under GetMailbox.

---

### P04 — SendUserMail (with item attachment)

**Input:**
```json
{
  "userId": "<targetPlayerId>",
  "subject": "Gift from GM",
  "body": "Enjoy this rare item!",
  "expiresAt": "<UTC + 1 hour>",
  "attachments": [{ "type": "item", "id": "rare_sword", "amount": 1 }]
}
```
**Expected output:** `success=true`, non-empty `mailId`.
**Pass criteria:** Attachment claimable by recipient.

---

### P05 — GetMailbox (global + user mails present)

**Pre-condition:** One global mail and one user-targeted mail sent to the caller.
**Input:** No parameters (caller identity from auth token).
**Expected output:**
```json
{ "success": true, "mails": [ { "mailId": "...", "subject": "...", ... }, ... ] }
```
**Pass criteria:** Both seeded mails present in `mails` list.

---

### P06 — GetMailbox (expired mails filtered)

**Pre-condition:** A global mail with `expiresAt` in the past is sent.
**Expected output:** `mails` list does NOT contain the expired mail.
**Pass criteria:** Expired mail absent from response.
**Edge case note:** Server clock vs. client clock skew may cause flakiness at exact boundary.

---

### P07 — MarkMailRead (read flag updates)

**Pre-condition:** A user mail exists that has `isRead=false`.
**Input:** `{ "mailId": "<existingId>" }`
**Expected output:**
```json
{ "success": true, "mailId": "<id>", "isRead": true }
```
**Pass criteria:** `isRead=true` in response AND verified via subsequent GetMailbox call.

---

### P08 — ClaimAttachment (reward returned)

**Pre-condition:** A user mail with currency attachment exists and is unclaimed.
**Input:** `{ "mailId": "<existingId>" }`
**Expected output:**
```json
{
  "success": true,
  "mailId": "<id>",
  "alreadyClaimed": false,
  "claimedAttachments": [{ "type": "currency", "id": "gold", "amount": 200 }]
}
```
**Pass criteria:** `success=true`, `alreadyClaimed=false`, `claimedAttachments` non-empty with correct values.

---

### P09 — ClaimAttachment (idempotent second claim)

**Pre-condition:** Attachment already claimed once (P08 ran first or same flow).
**Input:** Same `mailId` as P08.
**Expected output:** `alreadyClaimed=true` (no double reward granted).
**Pass criteria:** Second call returns `alreadyClaimed=true` OR server-side error (no reward duplication).
**Severity:** BLOCKER — double reward is an economy exploit.

---

## Negative Tests

### N01 — SendUserMail empty userId

**Input:** `{ "userId": "", "subject": "Subject", "body": "Body" }`
**Expected:** HTTP 400 / validation exception.
**Pass criteria:** Call throws with a 400/validation error; no mail created.
**Severity:** HIGH

---

### N02 — SendGlobalMail missing subject

**Input:** `{ "subject": "", "body": "Body only" }`
**Expected:** HTTP 400 / validation exception.
**Pass criteria:** Call throws with validation error.
**Severity:** HIGH

---

### N03 — ClaimAttachment invalid mailId

**Input:** `{ "mailId": "nonexistent-mail-id-000" }`
**Expected:** HTTP 404 or equivalent.
**Pass criteria:** Exception thrown; no state mutated.
**Severity:** HIGH

---

### N04 — ClaimAttachment on mail with no attachment

**Pre-condition:** Mail sent with no attachment fields.
**Input:** `{ "mailId": "<id of notification-only mail>" }`
**Expected:** Error response — cannot claim from mail that has no attachment.
**Pass criteria:** Exception thrown or `success=false`.
**Severity:** MEDIUM

---

### N05 — ClaimAttachment on expired mail

**Pre-condition:** Mail with `expiresAt` in the past.
**Input:** `{ "mailId": "<expired mailId>" }`
**Expected:** Error — expired mail not claimable.
**Pass criteria:** Exception or `success=false`.
**Severity:** HIGH — prevents farming expired content.

---

### N06 — GetMailbox when no mails exist

**Pre-condition:** Fresh player with no sent mails.
**Input:** None.
**Expected output:** `{ "success": true, "mails": [] }`
**Pass criteria:** `success=true`, `mails` is empty array (not null, not error).
**Severity:** MEDIUM

---

### N07 — MarkMailRead with invalid mailId

**Input:** `{ "mailId": "nonexistent-id-999" }`
**Expected:** 404 or `success=false` — graceful handling, no crash.
**Pass criteria:** Exception or `success=false`; no server 500.
**Severity:** MEDIUM

---

## Edge Case Tests

### E01 — SendGlobalMail with 1000-character body

**Input:** `{ "subject": "Long body test", "body": "A" * 1000 }`
**Expected:** `success=true` if within server limit; validation error if limit exceeded.
**Pass criteria:** Either clean success or explicit validation error — no server 500.
**Notes:** Server must document and enforce a body length limit.

---

### E02 — SendGlobalMail with multiple attachments

**Input:**
```json
{
  "subject": "Multi-reward",
  "body": "You get everything!",
  "attachments": [
    { "type": "currency", "id": "gold",   "amount": 100 },
    { "type": "currency", "id": "gems",   "amount": 10  },
    { "type": "item",     "id": "potion", "amount": 5   }
  ]
}
```
**Expected:** `success=true`, all 3 attachments claimable in one ClaimAttachment call.
**Pass criteria:** `claimedAttachments` has 3 items matching input.

---

### E03 — ClaimAttachment race condition (concurrent double-fire)

**Setup:** Two simultaneous ClaimAttachment calls for the same mailId.
**Expected:** Exactly one succeeds with `alreadyClaimed=false`; the other returns `alreadyClaimed=true` or an error.
**Pass criteria:** `successCount == 1` (no double grant).
**Severity:** BLOCKER — backend must use atomic check-and-set (Cloud Save conditional write or equivalent).
**Risk for Architect:** If Cloud Save does not support conditional writes at the mailbox level, a lock mechanism is required.

---

### E04 — GetMailbox with 10+ mails

**Setup:** 10 global mails sent before the call.
**Expected:** All 10+ mails returned; no truncation without pagination indicator.
**Pass criteria:** `mails.Count >= 10`.
**Notes:** If server paginates, API contract must expose page/cursor. Currently undocumented — GAP.

---

### E05 — Mail at expiry boundary (expires in 2 seconds)

**Setup:** Mail with `expiresAt = UtcNow + 2s`.
**Step 1:** ClaimAttachment immediately — expect success.
**Pass criteria:** Claim succeeds before server-side TTL triggers.
**Risk:** Client/server clock skew can cause false failures. Sub-second resolution in `expiresAt` must be consistent.

---

### E06 — SendUserMail to self

**Setup:** Caller sends a user mail to their own playerId.
**Expected:** Mail appears in their own GetMailbox.
**Pass criteria:** `success=true` on send and mail present in mailbox.
**Notes:** Self-mail is a valid support/GM use case; must not be blocked server-side.

---

## API Design Gaps Identified

| # | Gap | Severity | Owner |
|---|-----|----------|-------|
| G1 | No pagination contract for GetMailbox — large mailboxes may be silently truncated or cause timeout. | HIGH | Architect |
| G2 | `expiresAt` precision and clock-skew tolerance not documented. | MEDIUM | Architect |
| G3 | No explicit body/subject character limit defined in API contract. | MEDIUM | Architect |
| G4 | Race condition on ClaimAttachment requires atomic server-side guard (Cloud Save conditional write). Not yet specified. | BLOCKER | Architect |
| G5 | Behavior of ClaimAttachment on a mail with no attachment undefined (error type not specified). | MEDIUM | Architect |
| G6 | MarkMailRead on an already-read mail — expected to be idempotent but not documented. | LOW | Architect |
| G7 | SendUserMail — whether userId is a UGS PlayerId or a game-layer userId is ambiguous. Must be specified. | HIGH | Architect |

---

## Risk Summary for Architect

1. **Double-claim / economy exploit (BLOCKER):** E03 and P09 both probe whether the backend atomically prevents double reward. Cloud Save does not natively support transactions — the ClaimAttachment implementation must use a conditional write or a separate claim-lock key.

2. **Pagination (HIGH):** GetMailbox with no limit/cursor can fail at scale. Define a page size ceiling or add cursor pagination before launch.

3. **userId ambiguity (HIGH):** If `userId` in SendUserMail is a raw UGS PlayerId, callers can target arbitrary players. If it is a game-layer ID, a lookup is required server-side. The distinction must be locked down before client integration.

4. **Expiry boundary behavior (MEDIUM):** E05 shows that near-expiry claims are sensitive to clock skew. Recommend server-side tolerance window (e.g., ±1s) and documentation of the enforcement clock source.
