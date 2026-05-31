# Mailbox API — Developer Usage Guide

Module: `BackpackAdventuresModule`
Runtime: .NET 7.0 Cloud Code C# Module
Persistence: Unity Cloud Save (custom data + player data)

---

## Overview

The mailbox system provides two mail channels:

- **Global mail** — broadcast to all players; stored in a project-scoped Cloud Save custom-data key (`global_mails`).
- **User mail** — targeted to a specific player; stored in that player's Cloud Save player-data key (`user_mails`).

Each player maintains a `mailbox_state` record (player-data key) that tracks which mail IDs have been read and which attachments have been claimed.

---

## Prerequisites

All five mailbox endpoints require the player to be authenticated. Initialize UGS once at application start:

```csharp
await UnityServices.InitializeAsync();
await AuthenticationService.Instance.SignInAnonymouslyAsync();
```

Or use the provided service wrapper:

```csharp
await BackpackCloudCodeService.InitializeAsync();
```

---

## Function Reference

### 1. `SendGlobalMail`

Creates an admin mail payload in custom data key `mails_all`. The stored value is
an array of `{ "Mail": { ... } }` objects. `targetUserIds = null` broadcasts to all
players; a non-empty `targetUserIds` list limits visibility to those players.

**Status:** Implemented and deployed.

**Input parameters:**

| Field | Type | Required | Validation |
|-------|------|----------|------------|
| `subject` | `string` | Yes | Non-null, non-whitespace |
| `body` | `string` | Yes | Non-null, non-whitespace |
| `expiresAt` | `string` (ISO 8601 UTC) | No | Nullable; null stores `Mail.EndTime = null` and means no expiration |
| `attachments` | `MailAttachment[]` | No | Nullable list |

**`MailAttachment` object:**

| Field | Type | Description |
|-------|------|-------------|
| `type` | `string` | Attachment category, e.g. `"currency"`, `"item"` |
| `itemId` | `string` | Item or currency identifier |
| `quantity` | `int` | Amount to grant |

**Response shape:**

```json
{
  "success": true,
  "mailId": "3f6a1b2c-4d5e-6f7a-8b9c-0d1e2f3a4b5c"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `success` | `bool` | `true` on success |
| `mailId` | `string` | Server-assigned GUID for the new mail |

**Error responses:**

| Condition | Error |
|-----------|-------|
| `subject` is null or whitespace | `ArgumentException: Subject is required` (500) |
| `body` is null or whitespace | `ArgumentException: Body is required` (500) |
| Player not authenticated | 401 Unauthorized |

**C# client call:**

```csharp
var attachments = new List<MailAttachment>
{
    new MailAttachment { type = "currency", id = "gold", amount = 100 }
};

var response = await BackpackCloudCodeService.SendGlobalMailAsync(
    subject: "Maintenance Reward",
    body: "Thank you for your patience during scheduled maintenance.",
    expiresAt: null,
    attachments: attachments
);

Debug.Log($"Global mail sent: {response.mailId}");
```

**Note:** The server-side `MailAttachment` model uses `ItemId` and `Quantity` while the client model uses `id` and `amount`. This field name mismatch must be resolved before attachment data round-trips correctly through `GetMailbox` or `ClaimAttachment`. See `docs/KNOWN_LIMITATIONS.md`.

---

### Admin manage endpoints

The Admin Mail editor calls these through project-scoped REST, so Play Mode is not
required:

| Function | Purpose |
|----------|---------|
| `SetMailEndTime` | Sets `Mail.EndTime` on the matching `{ Mail }` object in `mails_all`; null clears expiration |
| `ExpireMail` | Soft expires a global mail by setting end time to current UTC |
| `DeleteGlobalMail` | Hard deletes the matching `{ Mail }` object from `mails_all` |

---

### 2. `SendUserMail`

Sends a mail item to a specific player by appending it to that player's `user_mails` Cloud Save key.

**Status:** Implemented and deployed.

**Input parameters:**

| Field | Type | Required | Validation |
|-------|------|----------|------------|
| `userId` | `string` | Yes | Non-null, non-whitespace — the target player's UGS player ID |
| `subject` | `string` | Yes | Non-null, non-whitespace |
| `body` | `string` | Yes | Non-null, non-whitespace |
| `expiresAt` | `string` (ISO 8601 UTC) | No | Nullable |
| `attachments` | `MailAttachment[]` | No | Nullable list |

**Note on request shape:** The client service wrapper packs `userId`, `subject`, `body`, `expiresAt`, and `attachments` into a flat `SendUserMailRequest`. The Cloud Code module receives this as `SendUserMailRequest { TargetUserId, Content: MailContent }`. The client-side `SendUserMailRequest` DTO does not exactly mirror the server-side shape — the service wrapper builds the correct nested structure.

**Response shape:**

```json
{
  "success": true,
  "mailId": "7a8b9c0d-1e2f-3a4b-5c6d-7e8f9a0b1c2d"
}
```

**Error responses:**

| Condition | Error |
|-----------|-------|
| `userId` is null or whitespace | `ArgumentException: TargetUserId is required` (500) |
| `subject` is null or whitespace | `ArgumentException: Subject is required` (500) |
| `body` is null or whitespace | `ArgumentException: Body is required` (500) |
| Player not authenticated | 401 Unauthorized |

**C# client call:**

```csharp
var response = await BackpackCloudCodeService.SendUserMailAsync(
    userId: "player-uuid-here",
    subject: "Expedition Complete",
    body: "Your expedition returned successfully.",
    expiresAt: null,
    attachments: null
);

Debug.Log($"User mail sent: {response.mailId}");
```

---

### 3. `GetMailbox`

Retrieves the calling player's combined mailbox: global mails and user mails merged into one list, with read and claim status applied per player.

**Status:** Implemented server-side (`GetMailboxModule.cs`). Reads `global_mails`, `user_mails`, and `mailbox_state` in parallel using `Task.WhenAll`. Filters expired mails at read time. Returns items sorted descending by `CreatedAt`.

**Input:** None.

**Expected response shape (when implemented):**

```json
{
  "success": true,
  "mails": [
    {
      "mailId": "3f6a1b2c-...",
      "subject": "Maintenance Reward",
      "body": "Thank you for your patience.",
      "isRead": false,
      "attachmentClaimed": false,
      "sentAt": "2026-05-27T10:00:00Z",
      "expiresAt": "2026-06-30T00:00:00Z",
      "attachments": [
        { "type": "currency", "id": "gold", "amount": 100 }
      ]
    }
  ]
}
```

**`MailItem` fields:**

| Field | Type | Description |
|-------|------|-------------|
| `mailId` | `string` | Unique identifier |
| `subject` | `string` | Mail subject line |
| `body` | `string` | Mail body text |
| `isRead` | `bool` | Whether calling player has read this mail |
| `attachmentClaimed` | `bool` | Whether calling player has claimed the attachment |
| `sentAt` | `string` | ISO 8601 UTC creation timestamp |
| `expiresAt` | `string` | ISO 8601 UTC expiration (nullable — may be null) |
| `attachments` | `MailAttachment[]` | Attachment list (nullable — may be null for notification-only mails) |

**C# client call:**

```csharp
var mailbox = await BackpackCloudCodeService.GetMailboxAsync();
foreach (var mail in mailbox.mails)
{
    Debug.Log($"[{(mail.isRead ? "READ" : "NEW")}] {mail.subject}");
}
```

---

### 4. `MarkMailRead`

Marks one or more mails as read for the calling player by adding their IDs to `mailbox_state.ReadIds` in Cloud Save. Idempotent — marking an already-read ID is a no-op.

**Status:** Implemented server-side (`MarkReadModule.cs`).

**Important note on server vs client contract mismatch:** The server-side `MarkMailRead` function accepts a `MarkReadRequest { MailIds: List<string> }` (a list of IDs). The client-side DTO `MarkMailReadRequest` sends a single `mailId`. The client service wrapper `MarkMailReadAsync(string mailId)` passes a single-element structure. This works for single-mail reads but the client cannot batch-mark multiple mails in one call via the current wrapper.

**Input parameters (server-side `MarkReadRequest`):**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `mailIds` | `List<string>` | Yes | One or more mail IDs to mark as read; must be non-empty |

**Response shape:**

```json
{
  "success": true
}
```

**Error responses:**

| Condition | Error |
|-----------|-------|
| `mailIds` is null or empty | `ArgumentException: MailIds must not be empty` (500) |
| Player not authenticated | 401 Unauthorized |

**C# client call:**

```csharp
var result = await BackpackCloudCodeService.MarkMailReadAsync(mail.mailId);
Debug.Log($"Mail marked read: {result.isRead}");
```

---

### 5. `ClaimAttachment`

Claims the attachment for a specific mail for the calling player. Checks `mailbox_state.ClaimedIds` for duplicate claims. Also marks the mail as read as a side effect.

**Status:** Implemented server-side (`ClaimAttachmentModule.cs`).

**Important behavior difference vs client contract:** The server-side handler throws `InvalidOperationException` when the mail is already claimed (resulting in a 500 error). The client-side `ClaimAttachmentResponse` DTO has an `alreadyClaimed` field, but the server does not return that field — it throws instead. Clients should catch `CloudCodeException` and inspect the error message to detect duplicate claims. This is a known mismatch.

**Input parameters:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `mailId` | `string` | Yes | The mail ID whose attachment to claim |
| `mailType` | `string` | No | `global` or `user`; omitted/empty infers global for `gm_` IDs |
| `requestId` | `string` | No | Client-generated idempotency key |

Direct Cloud Code calls can pass either the normal object request:

```csharp
var args = new Dictionary<string, object>
{
    ["request"] = new { mailId = messageId, mailType = "global" }
};
```

or a compact string request when only the mail id is needed:

```csharp
var args = new Dictionary<string, object>
{
    ["request"] = messageId
};
```

The compact string form is equivalent to `{ mailId = messageId }`.

All mailbox endpoints return `ApiResponse<TData>` when called directly through
Unity's official Cloud Code API. For example:

```csharp
var response = await CloudCodeService.Instance
    .CallModuleEndpointAsync<ApiResponse<ClaimAttachmentData>>(
        "BackpackAdventuresModule",
        "ClaimAttachment",
        args);

if (response.StatusCode == 200)
{
    var data = response.Data;
}
```

If the caller only needs status and message:

```csharp
var response = await CloudCodeService.Instance
    .CallModuleEndpointAsync<ApiResponse>(
        "BackpackAdventuresModule",
        "ClaimAttachment",
        args);
```

**Response shape (success path only):**

```json
{
  "success": true,
  "claimedItems": [
    { "type": "currency", "itemId": "gold", "quantity": 100 }
  ]
}
```

| Field | Type | Description |
|-------|------|-------------|
| `success` | `bool` | `true` on successful first claim |
| `claimedItems` | `MailAttachment[]` | Items granted (server field names: `type`, `itemId`, `quantity`) |

**Error responses:**

| Condition | Error |
|-----------|-------|
| `mailId` is null or whitespace | `ArgumentException: MailId is required` (500) |
| Already claimed | `InvalidOperationException: Attachment for mail {id} has already been claimed` (500) |
| Mail not found or has no attachments | `InvalidOperationException: No attachments found for mail {id}` (500) |
| Player not authenticated | 401 Unauthorized |

**Attachment search order:** The handler searches `user_mails` first, then `global_mails`. If the `mailId` exists in both, user mail takes priority.

**C# client call:**

```csharp
try
{
    var result = await BackpackCloudCodeService.ClaimAttachmentAsync(mail.mailId);
    foreach (var item in result.claimedAttachments)
        Debug.Log($"Claimed: {item.amount}x {item.id} ({item.type})");
}
catch (CloudCodeException e) when (e.Message.Contains("already been claimed"))
{
    Debug.Log("Already claimed — no additional reward granted.");
    UpdateMailUI(mail.mailId, claimed: true);
}
catch (CloudCodeException e)
{
    Debug.LogError($"Claim failed [{e.ErrorCode}]: {e.Message}");
}
```

---

## Common Integration Patterns

### How to send a global reward mail (step by step)

This pattern is used for sending a maintenance compensation reward to all players.

1. Ensure you have a server-side script or admin tool that can call `SendGlobalMail` — it must run inside a Cloud Code function because the caller needs authenticated UGS access. A client cannot call `SendGlobalMail` as a privileged admin without an authorization check (which is not yet implemented).

2. Prepare the mail content:

```csharp
var attachments = new List<MailAttachment>
{
    new MailAttachment { type = "currency", id = "gem", amount = 50 }
};

var response = await BackpackCloudCodeService.SendGlobalMailAsync(
    subject: "Maintenance Compensation",
    body: "We are sorry for the interruption. Here is a small thank-you.",
    expiresAt: "2026-06-30T00:00:00Z",
    attachments: attachments
);
```

3. Log and store the returned `mailId` for audit purposes.

4. Players will see the mail on their next `GetMailbox` call (once that function is implemented).

---

### How to claim an attachment (step by step)

This pattern should be used on the client side after a player taps "Claim" on a mail item.

1. Call `GetMailbox` to retrieve the list (once implemented). Check `mail.attachmentClaimed` before showing the claim button.

2. When the player taps Claim:

```csharp
try
{
    var result = await BackpackCloudCodeService.ClaimAttachmentAsync(mail.mailId);

    if (result.alreadyClaimed)
    {
        // Server says already claimed — update local UI state without showing reward
        UpdateMailUI(mail.mailId, claimed: true);
        return;
    }

    // Grant succeeded — show reward popup and update UI
    ShowRewardPopup(result.claimedAttachments);
    UpdateMailUI(mail.mailId, claimed: true);
}
catch (TimeoutException)
{
    // Safe to retry — ClaimAttachment is idempotent once implemented
    ShowRetryPrompt();
}
catch (CloudCodeException e)
{
    Debug.LogError($"Claim failed [{e.ErrorCode}]: {e.Message}");
}
```

3. Do not grant rewards locally before receiving a server confirmation. Always treat the server response as the source of truth.

---

### How to claim all available attachments

Use `ClaimAllAttachments` when the player taps a "Claim All" button. The endpoint
claims all visible, unexpired reward mails for the selected scope and marks each
claimed mail as read.

```csharp
var result = await BackpackCloudCodeService.CallClaimAllAttachmentsAsync(
    mailType: "all",
    requestId: Guid.NewGuid().ToString());

Debug.Log($"Claimed={result.claimedCount}, already={result.alreadyClaimedCount}, skipped={result.skippedCount}");
ShowRewardPopup(result.grantedAttachments);
```

`mailType` can be `all`, `global`, or `user`. Empty/null behaves as `all`. A retry
with the same `requestId` derives the same per-mail idempotency keys.

---

## Error Codes Reference

| HTTP Code | `CloudCodeException` Cause | Resolution |
|-----------|---------------------------|------------|
| 401 | Player not authenticated | Call `InitializeAsync()` before any Cloud Code call |
| 404 | Function not found or not yet deployed | Check function name spelling (case-sensitive); confirm server-side handler exists |
| 500 | Unhandled server exception (e.g., validation failure) | Check UGS Dashboard > Cloud Code > Logs for the stack trace |
| Timeout | No response within 10 seconds | Retry with back-off; `ClaimAttachment` is safe to retry |

---

## Cloud Save Keys Reference

| Key | Scope | Type | Contents |
|-----|-------|------|----------|
| `global_mails` | Custom (project-wide) | `List<GlobalMail>` JSON | All broadcast mails |
| `user_mails` | Player | `List<UserMail>` JSON | Mails targeted to the player |
| `mailbox_state` | Player | `MailboxState` JSON | `ReadIds[]` and `ClaimedIds[]` arrays |
