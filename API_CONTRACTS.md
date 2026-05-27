# API Contracts — BackpackAdventures Cloud Code Module

Module Name (UGS): `BackpackAdventures`
Runtime: .NET 7.0 C# Module
Transport: HTTPS via Unity Cloud Code SDK

---

## API 1 — HealthCheck

**Function Name:** `HealthCheck`
**Input:** none

**Response:**
```json
{
  "success": true,
  "message": "Cloud Code module online",
  "timestamp": "2024-01-15T10:30:00.0000000Z"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `success` | `bool` | Always `true` when module responds |
| `message` | `string` | Fixed: `"Cloud Code module online"` |
| `timestamp` | `string` | UTC ISO 8601 |

**Unity Client Call:**
```csharp
var result = await CloudCodeService.Instance
    .CallModuleEndpointAsync<HealthCheckResponse>("BackpackAdventures", "HealthCheck");
```

---

## API 2 — PlayerEchoTest

**Function Name:** `PlayerEchoTest`
**Input:** `{ "playerId": string }`

**Response:**
```json
{
  "success": true,
  "playerId": "player-abc-123",
  "serverTime": "2024-01-15T10:30:00.0000000Z"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `success` | `bool` | Always `true` on success |
| `playerId` | `string` | Echoed from request |
| `serverTime` | `string` | UTC ISO 8601 server timestamp |

**Unity Client Call:**
```csharp
var args = new Dictionary<string, object> { { "playerId", playerId } };
var result = await CloudCodeService.Instance
    .CallModuleEndpointAsync<PlayerEchoResponse>("BackpackAdventures", "PlayerEchoTest", args);
```

---

## API 3 — ServerConfigTest

**Function Name:** `ServerConfigTest`
**Input:** none

**Response:**
```json
{
  "environment": "staging",
  "version": "1.0.0",
  "deploymentTime": "2024-01-15T10:30:00.0000000Z"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `environment` | `string` | UGS environment ID from `IExecutionContext.EnvironmentId` |
| `version` | `string` | Module version string |
| `deploymentTime` | `string` | UTC ISO 8601 response timestamp |

**Unity Client Call:**
```csharp
var result = await CloudCodeService.Instance
    .CallModuleEndpointAsync<ServerConfigResponse>("BackpackAdventures", "ServerConfigTest");
```

---

## Error Handling

| HTTP Status | Cause | Client Handling |
|-------------|-------|-----------------|
| 401 | Player not authenticated | Call `AuthenticationService.SignInAnonymouslyAsync()` first |
| 404 | Wrong module or function name | Verify exact casing |
| 500 | Unhandled server exception | Check UGS Dashboard > Cloud Code > Logs |

## Authentication Requirements

1. `await UnityServices.InitializeAsync();`
2. `await AuthenticationService.Instance.SignInAnonymouslyAsync();`

## Naming (case-sensitive)

- Module: `BackpackAdventures`
- Functions: `HealthCheck`, `PlayerEchoTest`, `ServerConfigTest`
- Namespace: `BackpackAdventures`
