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

## Notes

- All timestamps are UTC ISO 8601 format (`DateTime.UtcNow.ToString("o")`).
- `Environment` in `ServerConfig` is sourced from `IExecutionContext.EnvironmentId` at runtime; falls back to `"production"` if null.
- `PlayerEcho.PlayerId` field name is PascalCase in JSON (matches C# property name).
- All function names are case-sensitive on the UGS side.
