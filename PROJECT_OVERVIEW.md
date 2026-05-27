# UnityCloudCode — Project Overview

Production-ready proof-of-concept for Unity Gaming Services (UGS) Cloud Code C# Module deployment with automated CI/CD.

## Architecture

```
Unity Client (MonoBehaviour / service wrapper)
  └─► UGS Cloud Code (BackpackAdventures C# Module)
        ├─► HealthCheck        — liveness probe
        ├─► PlayerEchoTest     — authenticated round-trip
        └─► ServerConfigTest   — runtime environment config
```

The module is a .NET 7.0 C# project compiled and deployed to UGS via the `ugs` CLI. GitHub Actions triggers deployment automatically on every push to `staging`.

## Repository Structure

```
CloudCodeModule/                        ← .NET 7.0 C# module (UGS backend)
  BackpackAdventures.ccmr               ← Cloud Code Module Reference (CLI entry point)
  BackpackAdventuresModule.sln
  BackpackAdventuresModule/
    BackpackAdventuresModule.csproj
    HealthCheckModule.cs                ← HealthCheck endpoint
    PlayerEchoModule.cs                 ← PlayerEchoTest endpoint
    ServerConfigModule.cs               ← ServerConfigTest endpoint
    ModuleConfig.cs                     ← DI registration

UnityClient/                            ← Unity C# integration scripts
  Scripts/
    CloudCodeModuleService.cs           ← static service wrapper
    CloudCodeDebugTest.cs               ← Play Mode validation MonoBehaviour
    Models/                             ← typed response DTOs

.github/workflows/
  staging-deploy.yml                    ← auto-deploy on push to staging

docs/
  DEPLOYMENT.md                         ← full setup and troubleshooting guide

PROJECT_OVERVIEW.md                     ← this file
API_CONTRACTS.md                        ← machine-readable API contracts
```

## Branch Strategy

| Branch | Purpose | Deployment |
|--------|---------|-----------|
| `main` | Stable, production-ready | Manual |
| `staging` | Integration testing | Auto on push |
| `feature/coordination` | Repo setup, docs, task coordination | None |
| `feature/cloudcode-module` | C# module implementation | None |
| `feature/cicd-deployment` | GitHub Actions CI/CD pipeline | None |
| `feature/client-integration` | Unity client scripts and validation | None |

## Team Roles

| Role | Branch | Responsibility |
|------|--------|---------------|
| Tech Lead | `feature/coordination` | Repo structure, docs, API contracts, coordination |
| Backend Engineer | `feature/cloudcode-module` | C# module endpoints, DI, build validation |
| DevOps Engineer | `feature/cicd-deployment` | GitHub Actions workflow, secrets documentation |
| Client/QA Engineer | `feature/client-integration` | Unity client scripts, Play Mode validation, sign-off |

## Quick Start

### Prerequisites

- Unity project with Cloud Code package (`com.unity.services.cloudcode`)
- UGS account with service account credentials (Cloud Code Editor role)
- .NET 7 SDK, Node.js 18, `ugs` CLI

### GitHub Secrets Required

| Secret | Source |
|--------|--------|
| `UNITY_PROJECT_ID` | Unity Dashboard > Project Settings |
| `UNITY_ENVIRONMENT` | UGS Dashboard > Environments (e.g. `staging`) |
| `UNITY_KEY_ID` | UGS Dashboard > Service Accounts > Keys |
| `UNITY_SECRET_KEY` | Generated alongside `UNITY_KEY_ID` |

### Deploy

Push to `staging` — CI deploys automatically. See docs/DEPLOYMENT.md for local deployment and troubleshooting.

## Definition of Done

- [ ] Cloud Code module deploys successfully to UGS
- [ ] Unity client calls all 3 APIs successfully
- [ ] APIs execute on UGS backend (verified via logs)
- [ ] GitHub Actions auto-deploy triggers on push to `staging`
- [ ] No manual deployment steps required post-setup
- [ ] End-to-end validation completed via CloudCodeDebugTest
- [ ] Logs prove successful deployment and API responses
- [ ] Project reproducible from a clean clone
