# API Contracts — BackpackAdventures Cloud Code Module

Module name: `BackpackAdventures`
Namespace: `BackpackAdventures.CloudCode`
SDK: `Com.Unity.Services.CloudCode.Core` 0.0.4

---

## HealthCheck

**Function name:** `HealthCheck`
**Input:** None

**Response:**
```json
{
  "Success": true,
  "Message": "Cloud Code module online",
  "Timestamp": "2024-01-01T00:00:00.0000000Z"
}
```

**C# response class:**
```csharp
public class HealthCheckResponse
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public string Timestamp { get; set; }
}
```

**Unity client call:**
```csharp
var result = await CloudCodeService.Instance
    .CallModuleEndpointAsync<HealthCheckResponse>("BackpackAdventures", "HealthCheck");
```

---

## PlayerEcho

**Function name:** `PlayerEcho`
**Input:**
```json
{ "PlayerId": "<string>" }
```

**Response:**
```json
{
  "Success": true,
  "PlayerId": "<echoed input>",
  "ServerTime": "2024-01-01T00:00:00.0000000Z"
}
```

**C# request/response classes:**
```csharp
public class PlayerEchoRequest
{
    public string PlayerId { get; set; }
}

public class PlayerEchoResponse
{
    public bool Success { get; set; }
    public string PlayerId { get; set; }
    public string ServerTime { get; set; }
}
```

**Unity client call:**
```csharp
var args = new Dictionary<string, object> { { "PlayerId", playerId } };
var result = await CloudCodeService.Instance
    .CallModuleEndpointAsync<PlayerEchoResponse>("BackpackAdventures", "PlayerEcho", args);
```

---

## ServerConfig

**Function name:** `ServerConfig`
**Input:** None

**Response:**
```json
{
  "Environment": "production",
  "Version": "1.0.0",
  "DeploymentTime": "2024-01-01T00:00:00.0000000Z"
}
```

**C# response class:**
```csharp
public class ServerConfigResponse
{
    public string Environment { get; set; }
    public string Version { get; set; }
    public string DeploymentTime { get; set; }
}
```

**Unity client call:**
```csharp
var result = await CloudCodeService.Instance
    .CallModuleEndpointAsync<ServerConfigResponse>("BackpackAdventures", "ServerConfig");
```

---

## Mailbox Storage Contract

Admin-authored mail uses Cloud Save custom data ID `global_mail`.

| Key | Scope | Contents |
|-----|-------|----------|
| `global_mail_index` | Custom data | `GlobalMailIndexV2` lightweight refs for active admin mail |
| `mail_global_{mailId}` | Custom data | Full `Mail` payload (`TargetUserIds`, title/content, availability, attachments) |
| `global_mail_index_legacy` | Custom data | Read-only fallback for legacy v1 global mail, if present |
| `mailbox_global_state` | Player data | Per-player `MailMetadata` only: `MessageId`, `IsClaim`, `IsRead`, `IsDelete` |
| `mailbox_user_items` | Player data | Full user-to-user `GiftMail` payloads |

`TargetUserIds = null` or an empty list means broadcast to all players. A non-empty
`TargetUserIds` list means targeted admin mail; the mail still lives in
`mail_global_{mailId}`, and each player only writes state to `mailbox_global_state`
when they read, claim, or delete it.

`Mail.EndTime` is nullable. `EndTime = null` means no expiration and the mail stays
available until it is manually expired or purged by admin tooling. The Admin Mail
editor exposes two modes: `Null / no expiration` sends `expiresAt = null`, while
`Use UTC time` sends an ISO 8601 UTC timestamp that is stored as `EndTime`.

Admin management behavior:
- `SetMailEndTime` updates both `mail_global_{mailId}.Mail.EndTime` and
  `global_mail_index.Refs[].ExpireTime`.
- `ExpireMail` is a soft expire operation; it sets EndTime/ExpireTime to the
  current UTC time so list endpoints filter the mail out.
- `DeleteGlobalMail` is the hard delete operation; it removes the index ref and
  deletes the `mail_global_{mailId}` custom-data key.

New mailbox Cloud Save writes omit `"Version"` fields. Existing stored records with
`Version` still deserialize normally, but rewritten records drop that field.

---

## Notes

- All timestamps are UTC ISO 8601 format (`DateTime.UtcNow.ToString("o")`).
- `Environment` in `ServerConfig` is sourced from `IExecutionContext.EnvironmentId` at runtime; falls back to `"production"` if null.
- `PlayerEcho.PlayerId` field name is PascalCase in JSON (matches C# property name).
- All function names are case-sensitive on the UGS side.
