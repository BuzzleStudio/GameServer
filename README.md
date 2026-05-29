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

Configure these as GitHub repository secrets or GitHub Environment secrets for whichever environment runs the workflow:

| Secret | Where to find it |
|--------|-----------------|
| `UNITY_PROJECT_ID` | Unity Dashboard → your project → Settings → General → **Project ID** |
| `UNITY_ENVIRONMENT` | Unity Dashboard → your project → LiveOps → Environments → environment name |
| `UNITY_PROJECT_SERVICE_ACCOUNT_KEY` | Unity Dashboard > Organization > Settings > Service Accounts > project-scoped account > **Key ID** |
| `UNITY_PROJECT_SERVICE_ACCOUNT_SECRET` | Same page - **Secret Key** (shown only once at key creation, store immediately) |
| `ADMIN_SERVICE_TOKEN` | Unity Secret Manager > target environment > `ADMIN_SERVICE_TOKEN` |

`SendUserMail` smoke test uses the optional `workflow_dispatch` input `admin_test_player_id`; no extra secret is required.

**To create a service account:** Unity Dashboard > Organization > Settings > Service Accounts > Create service account > assign **Cloud Code Editor** only on this project > Add key.

### 2. Deploy

Push to `staging` — the pipeline runs automatically.

For manual local deploy:
```bash
npm install -g ugs
ugs login --service-key-id <UNITY_PROJECT_SERVICE_ACCOUNT_KEY> --secret-key-stdin <<< "<UNITY_PROJECT_SERVICE_ACCOUNT_SECRET>"
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

---

## Mailbox System

Cloud Save-backed in-game mail with global broadcast and targeted player delivery.

### Architecture

```
Unity Client
  └─► Cloud Code C# Module (BackpackAdventuresModule)
        ├─► SendGlobalMail  — broadcast to all players via Cloud Save custom key
        ├─► SendUserMail    — targeted mail to a specific player
        ├─► GetMailbox      — fetch merged mailbox with read/claim status [not yet deployed]
        ├─► MarkMailRead    — mark a mail as read [not yet deployed]
        └─► ClaimAttachment — claim attachment with idempotency guard [not yet deployed]

Cloud Save Keys:
  global_mails   (custom, project-wide)  — broadcast mail list
  user_mails     (player)                — targeted mail list per player
  mailbox_state  (player)                — ReadIds[], ClaimedIds[] per player
```

### Mailbox API Quick Reference

| Function | Status | Input | Output |
|----------|--------|-------|--------|
| `SendGlobalMail` | Implemented, not yet committed | `{ subject, body, expiresAt?, attachments? }` | `{ success, mailId }` |
| `SendUserMail` | Implemented, not yet committed | `{ userId, subject, body, expiresAt?, attachments? }` | `{ success, mailId }` |
| `GetMailbox` | Implemented, not yet committed | — | `{ success, mails[] }` |
| `MarkMailRead` | Implemented, not yet committed | `{ mailIds[] }` | `{ success }` |
| `ClaimAttachment` | Implemented, not yet committed | `{ mailId }` | `{ success, claimedItems[] }` |

### Quick Usage Example

```csharp
// Initialize once
await BackpackCloudCodeService.InitializeAsync();

// Send a global broadcast mail with an attachment
var response = await BackpackCloudCodeService.SendGlobalMailAsync(
    subject: "Maintenance Reward",
    body: "Thanks for your patience!",
    expiresAt: "2026-06-30T00:00:00Z",
    attachments: new List<MailAttachment>
    {
        new MailAttachment { type = "currency", id = "gem", amount = 50 }
    }
);
Debug.Log($"Mail broadcast: {response.mailId}");

// Send a targeted mail to a player
await BackpackCloudCodeService.SendUserMailAsync(
    userId: "player-uuid",
    subject: "Expedition Complete",
    body: "Your team returned safely.",
    expiresAt: null,
    attachments: null
);
```

See [docs/MAILBOX_API_USAGE.md](docs/MAILBOX_API_USAGE.md) for full API documentation, parameter details, and integration patterns.

See [docs/KNOWN_LIMITATIONS.md](docs/KNOWN_LIMITATIONS.md) for current limitations, unimplemented features, and known risks.

See [CHANGELOG.md](CHANGELOG.md) for detailed change history including design decisions and risk notes.
