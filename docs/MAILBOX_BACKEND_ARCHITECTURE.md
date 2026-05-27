# Mailbox Backend Architecture

## 1. Scope

This document defines the server-side mailbox system for Backpack Adventures, implemented as Unity Cloud Code functions backed by Cloud Save. It covers global broadcast mails, user-specific mails, attachment rewards, read/unread state, claim tracking, expiration, deduplication, pagination, and security.

---

## 2. Design Decisions

### 2.1 Storage Strategy: Flat Keys over Nested Documents

Cloud Save has a per-key size limit (~1 MB) and no server-side query capability. The design partitions data into multiple fixed-purpose keys rather than one large document. This keeps individual key payloads small, allows targeted updates, and avoids loading the full mailbox when only metadata is needed.

### 2.2 Global vs User-Specific Mail

**Global mails** are authored and pushed by administrators (via a Cloud Code admin function) and stored in a **Public Access Layer** key that all players can read. Each global mail has a globally unique ID and a server-side `expiresAt` ISO-8601 timestamp.

**User mails** are generated server-side (event completions, rewards, support) and written directly into the **player's private Cloud Save namespace**.

This avoids per-player duplication of global mail content. The per-player state for global mails is limited to a small `claimedGlobalMails` set and an implicit "read if seen" timestamp.

### 2.3 Read State

Read state is tracked per-player. A mail is considered read once the client calls `MailboxMarkRead`. The player's Cloud Save key `mailbox_meta` stores the `lastReadAt` ISO timestamp and a sparse `readIds` array containing IDs of mails that have been individually marked read. The `lastReadAt` shortcut allows fast "read everything before this time" bulk marking without enumerating every ID.

### 2.4 Attachment Claim Tracking

Attachments on global mails: claimed status is stored in the player's `mailbox_global_state` key as a set of claimed global mail IDs. Granting happens inside the same Cloud Code function as claiming, ensuring atomicity via a read-modify-write on Cloud Save (last-write-wins; idempotent if the reward grant is also idempotent on the economy service side).

Attachments on user mails: the mail record itself carries a `claimed` boolean. Claiming writes `claimed: true` back to the player's `mailbox_user_items` key before issuing the reward. Idempotency is enforced by checking `claimed` before granting.

### 2.5 Expiration

Expiration is **lazy**: no background sweep job. Each read or list call filters out items where `expiresAt < now`. Expired user mails are also **pruned** during list operations (the list function rewrites the key without expired items, keeping storage clean).

### 2.6 Deduplication

- Global mails use a server-assigned `globalMailId` (GUID). Clients cannot inject duplicates.
- User mails use a `dedupKey` field (optional, caller-supplied). Before inserting a user mail, the write function checks for an existing mail with the same `dedupKey` and skips insertion if found. If no `dedupKey` is provided, a GUID is generated and dedup is skipped.
- Admin sends of global mails include a `dedupKey` checked against the `global_mail_index` before inserting.

### 2.7 Pagination

`MailboxGetUserMails` and `MailboxGetGlobalMails` accept `page` (0-based) and `pageSize` (default 20, max 50). Pagination is cursor-free (offset-based) because Cloud Save does not support server-side sorting; items are sorted descending by `sentAt` client-side within Cloud Code before slicing. This is acceptable for mailbox sizes in the hundreds; if scale exceeds ~500 items the design should migrate to a dedicated database.

### 2.8 Security

- `IExecutionContext.PlayerId` is the authoritative caller identity — never trusted from client payload.
- Admin functions (`MailboxSendGlobal`, `MailboxSendUser`) validate the caller against a server-configured admin player ID list stored in `global_mail_index` access-controlled key. In production, replace with a service-account token check.
- Attachment grants call economy/inventory services server-side; the client receives confirmation only, never the reward directly.
- Input length limits enforced on all string fields (title ≤ 128, body ≤ 1024).

---

## 3. Cloud Save Data Schemas

All keys are in the **Default** Cloud Save collection unless noted.

### 3.1 Global Mail Index (Public Access)

**Key:** `global_mail_index`  
**Access:** Public (read by all players, write by Cloud Code only)

```json
{
  "version": 1,
  "mails": [
    {
      "globalMailId": "gm_a1b2c3d4",
      "title": "Server Maintenance Reward",
      "body": "Thank you for your patience.",
      "sentAt": "2026-05-27T10:00:00Z",
      "expiresAt": "2026-06-27T10:00:00Z",
      "attachment": {
        "type": "currency",
        "itemId": "gold",
        "quantity": 100
      },
      "dedupKey": "maintenance-2026-05-27"
    }
  ]
}
```

Field notes:
- `attachment` is nullable; omit for mails with no reward.
- `type` values: `"currency"`, `"item"`, `"none"`.
- `expiresAt` is nullable; null means no expiry.

### 3.2 Player Global Mail State (Private per player)

**Key:** `mailbox_global_state`  
**Access:** Player-private

```json
{
  "version": 1,
  "claimedIds": ["gm_a1b2c3d4"],
  "readIds": ["gm_a1b2c3d4"]
}
```

### 3.3 Player User Mail Items (Private per player)

**Key:** `mailbox_user_items`  
**Access:** Player-private

```json
{
  "version": 1,
  "mails": [
    {
      "mailId": "um_e5f6g7h8",
      "title": "Daily Login Bonus",
      "body": "Here is your day-7 reward.",
      "sentAt": "2026-05-27T08:00:00Z",
      "expiresAt": "2026-06-03T08:00:00Z",
      "read": false,
      "claimed": false,
      "attachment": {
        "type": "item",
        "itemId": "chest_rare",
        "quantity": 1
      },
      "dedupKey": "login-bonus-day7-2026-05-27"
    }
  ]
}
```

### 3.4 Player Mailbox Metadata (Private per player)

**Key:** `mailbox_meta`  
**Access:** Player-private

```json
{
  "version": 1,
  "lastReadAt": "2026-05-27T09:00:00Z",
  "totalUserMails": 3,
  "totalGlobalMails": 1
}
```

`lastReadAt` is used for bulk mark-as-read: any mail with `sentAt <= lastReadAt` is implicitly read.

---

## 4. Mail Lifecycle

```
GLOBAL MAIL:
  Admin calls MailboxSendGlobal
       |
       v
  Dedup check on global_mail_index.dedupKey
       |-- duplicate --> return existing mailId, no-op
       |
       v
  Append to global_mail_index (Public key)
       |
       v
  Player calls MailboxGetGlobalMails
       |
       v
  Cloud Code reads global_mail_index, filters expired,
  reads player mailbox_global_state,
  annotates each mail with read/claimed flags
       |
       v
  Player calls MailboxMarkRead(mailId)
       |
       v
  Append mailId to mailbox_global_state.readIds
       |
       v
  Player calls MailboxClaimAttachment(mailId, "global")
       |
       v
  Idempotency check: mailId in claimedIds? --> error AlreadyClaimed
       |
       v
  Call economy service server-side to grant reward
       |
       v
  Append mailId to mailbox_global_state.claimedIds
       |
       v
  Return ClaimResult to client

USER MAIL:
  Server event calls MailboxSendUser(playerId, mail)
       |
       v
  Dedup check on mailbox_user_items.dedupKey (if present)
       |-- duplicate --> no-op
       |
       v
  Append to mailbox_user_items.mails
       |
       v
  Player calls MailboxGetUserMails
       |
       v
  Cloud Code reads mailbox_user_items,
  prunes expired mails (rewrites key),
  returns paginated + annotated results
       |
       v
  Player calls MailboxClaimAttachment(mailId, "user")
       |
       v
  Idempotency check: mail.claimed == true? --> error AlreadyClaimed
       |
       v
  Grant reward server-side
       |
       v
  Set mail.claimed = true, write mailbox_user_items
       |
       v
  Return ClaimResult to client
```

---

## 5. API Contract

### 5.1 MailboxGetUserMails

**Function name:** `MailboxGetUserMails`  
**Caller:** Player client  
**Auth:** IExecutionContext.PlayerId (auto)

**Input:**
```json
{ "page": 0, "pageSize": 20 }
```

**Output:**
```json
{
  "mails": [ /* MailItemDto[] */ ],
  "totalCount": 5,
  "page": 0,
  "pageSize": 20
}
```

**Errors:** `InvalidInput` (pageSize > 50)

---

### 5.2 MailboxGetGlobalMails

**Function name:** `MailboxGetGlobalMails`  
**Caller:** Player client

**Input:**
```json
{ "page": 0, "pageSize": 20 }
```

**Output:**
```json
{
  "mails": [ /* GlobalMailDto[] (includes read/claimed flags per player) */ ],
  "totalCount": 3,
  "page": 0,
  "pageSize": 20
}
```

---

### 5.3 MailboxMarkRead

**Function name:** `MailboxMarkRead`  
**Caller:** Player client

**Input:**
```json
{ "mailId": "um_e5f6g7h8", "mailType": "user" }
```
`mailType`: `"user"` | `"global"`

**Output:**
```json
{ "success": true }
```

---

### 5.4 MailboxMarkAllRead

**Function name:** `MailboxMarkAllRead`  
**Caller:** Player client

**Input:** none

**Output:**
```json
{ "success": true, "lastReadAt": "2026-05-27T10:00:00Z" }
```

Writes `lastReadAt = UtcNow` to `mailbox_meta`.

---

### 5.5 MailboxClaimAttachment

**Function name:** `MailboxClaimAttachment`  
**Caller:** Player client

**Input:**
```json
{ "mailId": "gm_a1b2c3d4", "mailType": "global" }
```

**Output:**
```json
{
  "success": true,
  "grantedAttachment": {
    "type": "currency",
    "itemId": "gold",
    "quantity": 100
  }
}
```

**Errors:** `AlreadyClaimed`, `MailNotFound`, `MailExpired`, `NoAttachment`

---

### 5.6 MailboxSendUser (server-to-player)

**Function name:** `MailboxSendUser`  
**Caller:** Trusted Cloud Code internal call or admin  
**Auth:** Caller validated against admin player ID list

**Input:**
```json
{
  "targetPlayerId": "player_xyz",
  "title": "Reward",
  "body": "Enjoy!",
  "expiresInDays": 30,
  "attachment": { "type": "item", "itemId": "chest_rare", "quantity": 1 },
  "dedupKey": "event-reward-spring-2026"
}
```

**Output:**
```json
{ "success": true, "mailId": "um_e5f6g7h8" }
```

**Errors:** `Unauthorized`, `TargetNotFound`, `DuplicateMail`, `InvalidInput`

---

### 5.7 MailboxSendGlobal (admin broadcast)

**Function name:** `MailboxSendGlobal`  
**Caller:** Admin  
**Auth:** Caller validated against admin player ID list

**Input:**
```json
{
  "title": "Server Maintenance Reward",
  "body": "Thank you for your patience.",
  "expiresInDays": 30,
  "attachment": { "type": "currency", "itemId": "gold", "quantity": 100 },
  "dedupKey": "maintenance-2026-05-27"
}
```

**Output:**
```json
{ "success": true, "globalMailId": "gm_a1b2c3d4" }
```

**Errors:** `Unauthorized`, `DuplicateMail`, `InvalidInput`

---

### 5.8 MailboxDeleteMail

**Function name:** `MailboxDeleteMail`  
**Caller:** Player client (user mails only)

**Input:**
```json
{ "mailId": "um_e5f6g7h8" }
```

**Output:**
```json
{ "success": true }
```

**Errors:** `MailNotFound`, `CannotDeleteGlobal`

---

## 6. Attachment Reward Model

The `attachment` field is a lightweight descriptor. Cloud Code functions that grant rewards call out to the UGS Economy service (or a custom economy endpoint) server-side, using the `IGameApiClient` or direct HTTP with the service account token from `IExecutionContext`. The client never receives raw economy tokens; it only gets the confirmation that the grant succeeded.

Supported `type` values:
- `"currency"` — grant via Economy Currency Grant API
- `"item"` — grant via Economy Inventory API
- `"none"` / omit — no reward

---

## 7. Performance Constraints

- Cloud Save key reads are ~50–100 ms each. `MailboxGetGlobalMails` does 2 reads (index + player state); `MailboxGetUserMails` does 1 read. Keep total Cloud Save calls per function <= 3.
- Prune expired user mails in-band during list to avoid unbounded key growth.
- Do not store attachment binary data in Cloud Save. Store only descriptors.
- Page size capped at 50 to limit serialization cost.
- `global_mail_index` should be pruned of expired entries by `MailboxSendGlobal` on each write (cheap, amortized cleanup).

---

## 8. Security Considerations

1. Never trust `playerId` from client payload. Always use `IExecutionContext.PlayerId`.
2. Admin functions must validate caller identity before any write.
3. Validate all string inputs for length. Reject titles > 128 chars, bodies > 1024 chars.
4. `pageSize` capped at 50; `page` must be >= 0.
5. Economy grants happen server-side only; never pass grant tokens to client.
6. `mailbox_user_items` is player-private in Cloud Save — one player cannot read another's mails.
7. `global_mail_index` is Public-readable but Cloud Code write-only; no client write path exists.

---

## 9. Open Risks

| Risk | Likelihood | Mitigation |
|------|-----------|------------|
| `mailbox_user_items` key exceeds 1 MB (many mails) | Low-Medium | Prune expired mails on list; cap stored mails at 200 per player |
| `global_mail_index` grows unbounded | Low | Prune expired entries on each `MailboxSendGlobal` write |
| Double-claim due to concurrent requests | Low | Cloud Save last-write-wins; claimed flag checked before grant — grant must be idempotent |
| Admin auth relies on player ID allowlist | Medium | Replace with UGS service account token validation in production |
| Economy service unavailable at claim time | Low | Return `EconomyUnavailable` error; let client retry; claimed flag not set on failure |

---

## 10. Implementation Handoff for Teammate 2

Teammate 2 (Unity Developer) should implement these Cloud Code function files under `CloudCodeModule/BackpackAdventuresModule/Mailbox/`:

| File | Function |
|------|----------|
| `MailboxGetUserMailsModule.cs` | `MailboxGetUserMails` |
| `MailboxGetGlobalMailsModule.cs` | `MailboxGetGlobalMails` |
| `MailboxMarkReadModule.cs` | `MailboxMarkRead`, `MailboxMarkAllRead` |
| `MailboxClaimAttachmentModule.cs` | `MailboxClaimAttachment` |
| `MailboxSendUserModule.cs` | `MailboxSendUser` |
| `MailboxSendGlobalModule.cs` | `MailboxSendGlobal` |
| `MailboxDeleteMailModule.cs` | `MailboxDeleteMail` |

Shared models are in `MailboxModels.cs` (same folder).

Each module follows the same pattern as `HealthCheckModule.cs`:
- Constructor injects `IExecutionContext`, `IGameApiClient`, `ILogger<T>`
- One public `[CloudCodeFunction("...")]` method per file
- Request/response DTOs defined at the bottom of the same file
- Namespace: `BackpackAdventures.CloudCode`

Cloud Save keys to use (from `MailboxConstants` in `MailboxModels.cs`):
- `global_mail_index` — global mail list (public)
- `mailbox_global_state` — per-player global mail read/claim state
- `mailbox_user_items` — per-player user mail list
- `mailbox_meta` — per-player metadata and lastReadAt

The `IGameApiClient` (from `Com.Unity.Services.CloudCode.Apis`) provides `CloudSaveDataApi` for reads and writes. Use `SetItemAsync` for writes and `GetItemAsync`/`GetItemsAsync` for reads.
