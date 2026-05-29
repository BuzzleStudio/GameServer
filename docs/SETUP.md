# Setup Guide — BackpackAdventures Cloud Code Module

This document covers prerequisites, local setup, Unity project integration, and how to call the deployed Cloud Code APIs from a Unity client.

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Clone and Run Locally](#2-clone-and-run-locally)
3. [Connect a Unity Project to These Cloud Code Modules](#3-connect-a-unity-project-to-these-cloud-code-modules)
4. [Call the APIs from the Unity Client](#4-call-the-apis-from-the-unity-client)

---

## 1. Prerequisites

### Unity Gaming Services Account

- A Unity account at unity.com.
- A Unity project linked to Unity Gaming Services in the Unity Dashboard.
- Cloud Code enabled for the project (Dashboard > Your Project > Products > Cloud Code).

### Service Account

The CI/CD pipeline and local deployments both require a UGS service account with the **Cloud Code Editor** role.

To create one:
1. Open the UGS Dashboard and select your project.
2. Go to **Settings > Service Accounts > Create Service Account**.
3. Assign the **Cloud Code Editor** role.
4. Generate a key pair. Copy and store the secret immediately — it is shown only once.

### Local Tooling

| Tool | Minimum Version | Install |
|---|---|---|
| .NET SDK | 7.0.x | https://dotnet.microsoft.com/download/dotnet/7.0 |
| Node.js | 18.x | https://nodejs.org/ |
| UGS CLI | latest | `npm install -g ugs` |

Verify installations:

```bash
dotnet --version   # 7.x.x
node --version     # v18.x.x
ugs --version      # any version string
```

### Repository Access

- Read access to `git@github.com:dyCuong03/UnityCloudCode.git`.
- Write access to `staging` branch to trigger auto-deploys.

---

## 2. Clone and Run Locally

### Clone the repository

```bash
git clone git@github.com:dyCuong03/UnityCloudCode.git
cd UnityCloudCode
```

### Restore .NET dependencies

```bash
dotnet restore CloudCodeModule/BackpackAdventuresModule~/BackpackAdventuresModule.csproj
```

### Build the module

```bash
dotnet publish CloudCodeModule/BackpackAdventuresModule~/BackpackAdventuresModule.csproj \
  -c Release
```

### Deploy to UGS from your local machine

```bash
# Authenticate
ugs login \
  --service-key-id <YOUR_KEY_ID> \
  --secret-key-stdin <<< "<YOUR_SECRET_KEY>"

# Set project and environment
ugs config set project-id <YOUR_PROJECT_ID>
ugs config set environment-name staging

# Deploy
ugs deploy CloudCodeModule/BackpackAdventures.ccmr

# Verify
ugs cloud-code modules list \
  --project-id <YOUR_PROJECT_ID> \
  --environment-name staging
```

Replace `<YOUR_KEY_ID>`, `<YOUR_SECRET_KEY>`, and `<YOUR_PROJECT_ID>` with your actual service account credentials and Unity project ID from the UGS Dashboard.

---

## 3. Connect a Unity Project to These Cloud Code Modules

### Add required Unity packages

In the Unity Editor, open **Window > Package Manager** and add:

| Package | Name |
|---|---|
| Authentication | `com.unity.services.authentication` |
| Cloud Code | `com.unity.services.cloudcode` |
| Core | `com.unity.services.core` |

Or add to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.unity.services.authentication": "3.3.0",
    "com.unity.services.cloudcode": "2.7.1",
    "com.unity.services.core": "1.13.0"
  }
}
```

### Link the Unity project to UGS

1. Open **Edit > Project Settings > Services**.
2. Click **Link Unity project** and select your UGS project.
3. Confirm the Project ID matches the one used during deployment.

### Initialize UGS at runtime

Call this once at application startup before invoking any Cloud Code endpoint:

```csharp
await UnityServices.InitializeAsync();
await AuthenticationService.Instance.SignInAnonymouslyAsync();
```

---

## 4. Call the APIs from the Unity Client

All three APIs are exposed by the `BackpackAdventures` Cloud Code Module.

### Response models

```csharp
[Serializable]
public class HealthCheckResponse
{
    public bool success;
    public string message;
    public string timestamp;
}

[Serializable]
public class PlayerEchoResponse
{
    public bool success;
    public string playerId;
    public string serverTime;
}

[Serializable]
public class ServerConfigResponse
{
    public string environment;
    public string version;
    public string deploymentTime;
}
```

### HealthCheck

Verifies the module is deployed and reachable. Takes no parameters.

```csharp
var result = await CloudCodeService.Instance
    .CallModuleEndpointAsync<HealthCheckResponse>(
        "BackpackAdventures",
        "HealthCheck");

Debug.Log($"Server online: {result.success} — {result.message} at {result.timestamp}");
```

### PlayerEcho

Validates player authentication and round-trip serialisation.

```csharp
var args = new Dictionary<string, object>
{
    { "playerId", AuthenticationService.Instance.PlayerId }
};

var result = await CloudCodeService.Instance
    .CallModuleEndpointAsync<PlayerEchoResponse>(
        "BackpackAdventures",
        "PlayerEcho",
        args);

Debug.Log($"Echo OK: {result.success}, player: {result.playerId}, time: {result.serverTime}");
```

### ServerConfig

Returns server-side configuration values.

```csharp
var result = await CloudCodeService.Instance
    .CallModuleEndpointAsync<ServerConfigResponse>(
        "BackpackAdventures",
        "ServerConfig");

Debug.Log($"Env: {result.environment}, version: {result.version}, deployed: {result.deploymentTime}");
```

### Error handling

```csharp
try
{
    var result = await CloudCodeService.Instance
        .CallModuleEndpointAsync<HealthCheckResponse>("BackpackAdventures", "HealthCheck");
    Debug.Log(result.message);
}
catch (CloudCodeException e)
{
    Debug.LogError($"Cloud Code error [{e.ErrorCode}]: {e.Message}");
}
```

Common error codes:

| Code | Cause | Fix |
|---|---|---|
| 401 Unauthorized | Player not signed in | Call `SignInAnonymouslyAsync` before invoking Cloud Code |
| 404 Not Found | Wrong module or function name | Names are case-sensitive — verify against the deployed module |
| 500 Internal Server Error | Runtime exception server-side | Check UGS Dashboard > Cloud Code > Logs |
