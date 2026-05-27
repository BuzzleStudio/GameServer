# UnityCloudCode

Production-quality proof-of-concept for Unity Cloud Code C# Module deployment with GitHub Actions CI/CD.

## Architecture

```
Unity Client
  └─► Cloud Code C# Module (BackpackAdventures)
        ├─► HealthCheck        — server connectivity
        ├─► PlayerEchoTest     — authenticated request roundtrip
        └─► ServerConfigTest   — server-side runtime config
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
    ModuleConfig.cs               ← DI registration

UnityClient/                      ← Unity-side integration (copy into your project)
  Scripts/
    CloudCodeModuleService.cs     ← static service wrapper
    CloudCodeDebugTest.cs         ← MonoBehaviour for Play Mode validation
    Models/                       ← response DTOs

.github/workflows/
  staging-deploy.yml              ← auto-deploys on push to staging

docs/
  DEPLOYMENT.md                   ← full setup and troubleshooting guide
```

## Quick Start

### 1. Configure GitHub Secrets

| Secret | Description |
|--------|-------------|
| `UNITY_PROJECT_ID` | Unity Dashboard → Project Settings → Project ID |
| `UNITY_ENVIRONMENT` | UGS environment name (e.g. `staging`) |
| `UNITY_KEY_ID` | UGS Dashboard → Service Accounts → key ID |
| `UNITY_SECRET_KEY` | Generated with the key above |

### 2. Deploy

Push to `staging` — the pipeline runs automatically.

For manual local deploy:
```bash
npm install -g ugs
ugs auth login --service-account-key-id <KEY_ID> --secret-key <SECRET>
ugs config set project-id <PROJECT_ID>
ugs config set environment-name staging
ugs deploy CloudCodeModule/
```

### 3. Call from Unity

```csharp
// Requires Unity Services initialized + authenticated
var health = await CloudCodeModuleService.HealthCheckAsync();
var echo   = await CloudCodeModuleService.PlayerEchoTestAsync(playerId);
var config = await CloudCodeModuleService.ServerConfigTestAsync();
```

Add `CloudCodeDebugTest` MonoBehaviour to any GameObject to run all 3 APIs in Play Mode.

## API Contracts

| Function | Input | Output |
|----------|-------|--------|
| `HealthCheck` | — | `{ success, message, timestamp }` |
| `PlayerEchoTest` | `playerId: string` | `{ success, playerId, serverTime }` |
| `ServerConfigTest` | — | `{ environment, version, deploymentTime }` |

See [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) for full documentation.
