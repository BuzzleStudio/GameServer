# UnityCloudCode

Production-quality proof-of-concept for Unity Cloud Code C# Module deployment with GitHub Actions CI/CD.

## Architecture

```
Unity Client
  └─► Cloud Code C# Module (BackpackAdventures)
        ├─► HealthCheck    — server connectivity
        ├─► PlayerEcho     — authenticated request roundtrip
        └─► ServerConfig   — server-side runtime config
```

Auto-deploy triggers on every push to `staging` via GitHub Actions.

## Repository Structure

```
CloudCodeModule/                  ← C# .NET 7.0 module (deployed to UGS)
  BackpackAdventures.ccmr         ← Cloud Code Module Reference (UGS CLI entry point)
  BackpackAdventuresModule.sln
  BackpackAdventuresModule/
    HealthCheckModule.cs
    PlayerEchoModule.cs
    ServerConfigModule.cs

UnityClient/                      ← Unity-side integration (copy into your Unity project)
  Runtime/
    BackpackCloudCodeService.cs   ← static service wrapper (HealthCheck, PlayerEcho, ServerConfig)
    CloudCodeModels.cs            ← response/request DTOs
    CloudCodeValidator.cs         ← per-field response validation
  Tests/
    CloudCodeIntegrationTest.cs   ← MonoBehaviour for Play Mode validation
  UnityClient.asmdef

.github/workflows/
  staging-deploy.yml              ← auto-deploys on push to staging

docs/
  DEPLOYMENT.md                   ← full setup and troubleshooting guide
```

## Quick Start

### 1. Configure GitHub Secrets

Go to **Settings → Secrets and variables → Actions** in the repository and add:

| Secret | Where to find it |
|--------|-----------------|
| `UNITY_PROJECT_ID` | Unity Dashboard → your project → Settings → General → **Project ID** |
| `UNITY_ENVIRONMENT` | Unity Dashboard → your project → LiveOps → Environments → environment name |
| `UNITY_SERVICE_ACCOUNT_KEY` | Unity Dashboard → Organization → Settings → Service Accounts → your account → **Key ID** |
| `UNITY_SERVICE_ACCOUNT_SECRET` | Same page — **Secret Key** (shown only once at key creation, store immediately) |

**To create a service account:** Unity Dashboard → Organization → Settings → Service Accounts → Create service account → assign role **Cloud Code Editor** → Add key.

### 2. Deploy

Push to `staging` — the pipeline runs automatically.

For manual local deploy:
```bash
npm install -g ugs
ugs login --service-account-key-id <UNITY_SERVICE_ACCOUNT_KEY> --secret <UNITY_SERVICE_ACCOUNT_SECRET>
ugs config set project-id <UNITY_PROJECT_ID>
ugs config set environment-name <UNITY_ENVIRONMENT>
ugs deploy CloudCodeModule/BackpackAdventures.ccmr
```

### 3. Call from Unity

```csharp
// Requires Unity Services initialized + authenticated
await BackpackCloudCodeService.InitializeAsync();
var health = await BackpackCloudCodeService.CallHealthCheckAsync();
var echo   = await BackpackCloudCodeService.CallPlayerEchoAsync(playerId);
var config = await BackpackCloudCodeService.CallServerConfigAsync();
```

Add `CloudCodeIntegrationTest` MonoBehaviour to any GameObject to run all 3 APIs in Play Mode.

## API Contracts

| Function | Input | Output |
|----------|-------|--------|
| `HealthCheck` | — | `{ success, message, timestamp }` |
| `PlayerEcho` | `playerId: string` | `{ success, playerId, serverTime }` |
| `ServerConfig` | — | `{ environment, version, deploymentTime }` |

See [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) for full documentation.
