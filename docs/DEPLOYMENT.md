# Deployment Guide — BackpackAdventures Cloud Code Module

This guide covers everything needed to deploy the BackpackAdventures Cloud Code Module to Unity Gaming Services (UGS), both through the automated CI/CD pipeline and locally from a developer machine.

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Required GitHub Secrets](#2-required-github-secrets)
3. [Local Development Deployment](#3-local-development-deployment)
4. [Staging Auto-Deploy](#4-staging-auto-deploy)
5. [Available APIs](#5-available-apis)
6. [Unity Client Usage](#6-unity-client-usage)
7. [Rollback Strategy](#7-rollback-strategy)
8. [Troubleshooting](#8-troubleshooting)

---

## 1. Prerequisites

### Unity Account and Project

- A Unity account with **Cloud Code** enabled for the project.
- The Unity project linked to Unity Gaming Services (UGS) in the Unity Dashboard.
- A **service account** created in the UGS Dashboard with the **Cloud Code Editor** role assigned.
  - Navigate to: UGS Dashboard > Service Accounts > Create Service Account > assign role "Cloud Code Editor".

### Local Tooling

| Tool | Version | Install |
|---|---|---|
| .NET SDK | 7.0.x | https://dotnet.microsoft.com/download/dotnet/7.0 |
| Node.js | 18.x | https://nodejs.org/ |
| UGS CLI | latest | `npm install -g ugs` |

Verify installations:

```bash
dotnet --version   # Should show 7.x.x
node --version     # Should show v18.x.x
ugs --version      # Should show a version number
```

### Repository Access

- Read/write access to the `UnityCloudCode` GitHub repository.
- Permission to configure GitHub Actions secrets (repository admin or environment admin role).

---

## 2. Required GitHub Secrets

Configure these secrets in the repository at:
**GitHub > Repository Settings > Secrets and variables > Actions > New repository secret**

| Secret Name | Where to Find | Description |
|---|---|---|
| `UNITY_PROJECT_ID` | Unity Dashboard > Project Settings > Project ID | Unique identifier for the Unity project in UGS |
| `UNITY_ENVIRONMENT` | UGS Dashboard > Environments | The target environment name (e.g. `staging`, `production`) |
| `UNITY_KEY_ID` | UGS Dashboard > Service Accounts > (your account) > Keys | The key ID portion of a service account credential pair |
| `UNITY_SECRET_KEY` | Shown once at key creation time in UGS Dashboard | The secret key paired with `UNITY_KEY_ID`; store it securely immediately |

> **Security note:** Secret keys are shown only once in the UGS Dashboard at creation time. If lost, delete the key and generate a new pair, then update the GitHub secrets.

---

## 3. Local Development Deployment

Use these steps to deploy directly from your development machine without triggering CI. This is useful for rapid iteration and testing before merging to `staging`.

### Step 1 — Authenticate

```bash
ugs auth login \
  --service-account-key-id <KEY_ID> \
  --secret-key <SECRET_KEY>
```

Replace `<KEY_ID>` and `<SECRET_KEY>` with your personal service account credentials from the UGS Dashboard.

### Step 2 — Configure project and environment

```bash
ugs config set project-id <PROJECT_ID>
ugs config set environment-name staging
```

Replace `<PROJECT_ID>` with the Unity project ID from the Unity Dashboard.

### Step 3 — Validate the .NET build

```bash
dotnet build \
  CloudCodeModule/BackpackAdventuresModule/BackpackAdventuresModule.csproj \
  -c Release
```

Fix any build errors before proceeding. Deploying a module that does not compile will fail at the UGS validation stage.

### Step 4 — Deploy

```bash
ugs deploy CloudCodeModule/
```

### Step 5 — Verify

```bash
ugs cloud-code modules list \
  --project-id <PROJECT_ID> \
  --environment-name staging
```

The `BackpackAdventures` module should appear in the list with an updated version or timestamp.

---

## 4. Staging Auto-Deploy

### Branch Strategy

| Branch | Purpose | Deployment |
|---|---|---|
| `main` | Stable production-ready code | Manual / separate workflow |
| `staging` | Integration and QA testing | Automatic on every push |
| Feature branches | Individual feature work | No deployment |

### How It Works

1. A developer merges a feature branch into `staging` (via pull request or direct push).
2. GitHub Actions detects the push to the `staging` branch and triggers the workflow defined in `.github/workflows/staging-deploy.yml`.
3. The pipeline runs in order:
   - Checks out the code.
   - Sets up .NET 7 and Node.js 18.
   - Installs and verifies the UGS CLI.
   - Configures the UGS project and environment from repository secrets.
   - Authenticates using the service account credentials stored as secrets.
   - Validates the .NET build in Release configuration.
   - Deploys the `CloudCodeModule/` directory to the configured UGS environment.
   - Lists deployed modules to confirm the deployment succeeded.
   - Prints a deployment summary.
4. Concurrency control (`group: staging-deploy`, `cancel-in-progress: false`) ensures that if multiple pushes arrive quickly, they deploy in sequence rather than racing.

### Monitoring a Deployment

Go to **GitHub > Actions** and select the most recent run of "Deploy Cloud Code Modules to Staging" to view step-by-step logs.

---

## 5. Available APIs

These endpoints are exposed by the `BackpackAdventures` Cloud Code Module.

| API Name | Function Name | Input Parameters | Output Shape | Purpose |
|---|---|---|---|---|
| Health Check | `HealthCheck` | none | `{ success: bool, message: string, timestamp: string }` | Verify the module is online and responding |
| Player Echo Test | `PlayerEchoTest` | `playerId: string` | `{ success: bool, playerId: string, serverTime: string }` | Validate player authentication and round-trip serialization |
| Server Config Test | `ServerConfigTest` | none | `{ environment: string, version: string, deploymentTime: string }` | Validate that server-side configuration is loaded correctly |

---

## 6. Unity Client Usage

Add `com.unity.services.cloudcode` to your project via the Package Manager, then call module endpoints as shown below.

### Response types

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

```csharp
var result = await CloudCodeService.Instance
    .CallModuleEndpointAsync<HealthCheckResponse>(
        "BackpackAdventures",
        "HealthCheck");

Debug.Log($"Server online: {result.success} — {result.message} at {result.timestamp}");
```

### PlayerEchoTest

```csharp
var args = new Dictionary<string, object>
{
    { "playerId", AuthenticationService.Instance.PlayerId }
};

var result = await CloudCodeService.Instance
    .CallModuleEndpointAsync<PlayerEchoResponse>(
        "BackpackAdventures",
        "PlayerEchoTest",
        args);

Debug.Log($"Echo OK: {result.success}, player: {result.playerId}, server time: {result.serverTime}");
```

### ServerConfigTest

```csharp
var result = await CloudCodeService.Instance
    .CallModuleEndpointAsync<ServerConfigResponse>(
        "BackpackAdventures",
        "ServerConfigTest");

Debug.Log($"Env: {result.environment}, version: {result.version}, deployed: {result.deploymentTime}");
```

> All calls require the player to be signed in via `AuthenticationService.Instance.SignInAnonymouslyAsync()` or a full sign-in flow before invoking Cloud Code endpoints.

---

## 7. Rollback Strategy

Cloud Code Modules in UGS are versioned. To roll back to a previous deployment:

### Option A — Redeploy from a previous Git commit (recommended)

```bash
# Check out the commit you want to revert to
git checkout <previous-commit-sha>

# Re-authenticate if needed
ugs auth login \
  --service-account-key-id <KEY_ID> \
  --secret-key <SECRET_KEY>

# Configure target environment
ugs config set project-id <PROJECT_ID>
ugs config set environment-name staging

# Deploy the older version
ugs deploy CloudCodeModule/
```

This redeploys the code from that commit and creates a new module version in UGS with the older logic.

### Option B — Revert the staging branch and let CI redeploy

```bash
git revert <bad-commit-sha>
git push origin staging
```

The push triggers the CI pipeline automatically, which deploys the reverted code.

### Verifying a rollback

After either option, run:

```bash
ugs cloud-code modules list \
  --project-id <PROJECT_ID> \
  --environment-name staging
```

Confirm the module version matches the expected rollback state. Run the HealthCheck and ServerConfigTest APIs from the client or via `ugs` to validate behavior.

---

## 8. Troubleshooting

### Authentication Failures

**Symptom:** `ugs auth login` returns `401 Unauthorized` or `Invalid credentials`.

**Fixes:**
- Confirm `UNITY_KEY_ID` and `UNITY_SECRET_KEY` match the same key pair in the UGS Dashboard.
- Check that the service account is not disabled or deleted.
- Verify the service account has the **Cloud Code Editor** role on the correct project.
- If the secret key was never saved, delete the key in UGS Dashboard, create a new pair, and update the GitHub secrets.

---

### Build Failures

**Symptom:** `dotnet build` exits non-zero; CI step "Validate .NET build" fails.

**Fixes:**
- Run `dotnet build` locally to reproduce the error message.
- Check for missing NuGet package references: run `dotnet restore` first.
- Ensure `BackpackAdventuresModule.csproj` targets `net7.0`.
- Look for C# compiler errors (missing types, wrong namespaces) in the step output.

---

### Deploy Failures

**Symptom:** `ugs deploy` exits non-zero or returns a UGS API error.

**Fixes:**
- Confirm `UNITY_PROJECT_ID` matches the project in the UGS Dashboard exactly (case-sensitive).
- Confirm `UNITY_ENVIRONMENT` is a valid existing environment for that project.
- Check that the module `.cs` files compile without errors (build step above must pass first).
- Review UGS service status at https://status.unity.com/ for any active incidents.
- Check UGS CLI output for `400 Bad Request` — this often means a malformed `ugs-module.yaml` or missing entry point declaration.

---

### API Call Errors from the Unity Client

**Symptom:** `CloudCodeException` thrown when calling a module endpoint.

| Error Code | Likely Cause | Fix |
|---|---|---|
| `Unauthorized` (401) | Player not signed in | Call `AuthenticationService.Instance.SignInAnonymouslyAsync()` before invoking Cloud Code |
| `NotFound` (404) | Wrong module name or function name | Verify the module is named `BackpackAdventures` and function names match exactly (case-sensitive) |
| `InternalServerError` (500) | Runtime exception in Cloud Code script | Check server-side logs in the UGS Dashboard > Cloud Code > Logs |
| `Timeout` | Network or cold-start delay | Retry with exponential back-off; report persistent timeouts to Unity Support |

---

*Last updated: see git log for this file.*
