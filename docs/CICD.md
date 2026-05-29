# CI/CD Pipeline — BackpackAdventures Cloud Code Module

This document covers the automated deployment pipeline that pushes the BackpackAdventures Cloud Code C# Module to Unity Gaming Services (UGS) whenever code is merged to the `staging` branch.

---

## Table of Contents

1. [How the Deployment Flow Works](#1-how-the-deployment-flow-works)
2. [Required GitHub Secrets](#2-required-github-secrets)
3. [Setting Up GitHub Secrets](#3-setting-up-github-secrets)
4. [Triggering a Deployment Manually](#4-triggering-a-deployment-manually)
5. [Troubleshooting Common Issues](#5-troubleshooting-common-issues)

---

## 1. How the Deployment Flow Works

The pipeline is defined in `.github/workflows/staging-deploy.yml` and executes the following steps in order whenever a push lands on the `staging` branch:

```
push to staging
       |
       v
1. Checkout repository
       |
       v
2. Setup .NET 7 runtime
       |
       v
3. Setup Node.js 18 (required for UGS CLI)
       |
       v
4. Install UGS CLI  (npm install -g ugs)
       |
       v
5. Verify UGS CLI is installed  (ugs --version)
       |
       v
6. Authenticate with UGS service account
       |
       v
7. Validate deployment artifacts exist
       |  (.ccmr file + .csproj file)
       |
       v
8. Build .NET module  (dotnet publish -c Release)
       |
       v
9. Deploy via UGS CLI  (ugs deploy *.ccmr)
       |
       v
10. Verify deployment  (ugs cloud-code modules list)
       |
       v
11. Print deployment summary
```

Any step that exits non-zero causes the entire job to fail immediately (`set -e` is used in all shell steps). The pipeline does not silently continue on error.

Concurrency control (`group: staging-deploy`, `cancel-in-progress: false`) serialises deployments: if two pushes arrive simultaneously the second run waits for the first to finish.

---

## 2. Required GitHub Secrets

| Secret Name | Description |
|---|---|
| `UNITY_PROJECT_ID` | The Unity project ID from the Unity Dashboard (Settings > Project Settings > Project ID) |
| `UNITY_ENVIRONMENT` | The UGS target environment name, e.g. `production` or `staging` (UGS Dashboard > Environments) |
| `UNITY_PROJECT_SERVICE_ACCOUNT_KEY` | The project-scoped service account key ID from UGS Dashboard (UGS Dashboard > Service Accounts > Keys) |
| `UNITY_PROJECT_SERVICE_ACCOUNT_SECRET` | The secret paired with `UNITY_PROJECT_SERVICE_ACCOUNT_KEY` - shown once at key creation time |
`SendUserMail` smoke test uses the optional `workflow_dispatch` input `admin_test_player_id`; no extra secret is required.

The service account must have the **Cloud Code Editor** role assigned on the target Unity project. These values are the service account key pair, not arbitrary Unity Secret Manager project secrets.

---

## 3. Setting Up GitHub Secrets

1. Open the repository on GitHub.
2. Go to **Settings > Secrets and variables > Actions**, or to the GitHub Environment that runs this workflow.
3. Click **New repository secret** for each secret listed above.
4. Paste the value and click **Add secret**.

Secrets are masked in all log output. Never commit secret values to the repository.

If `UNITY_PROJECT_SERVICE_ACCOUNT_SECRET` was not saved at creation time, delete the key in the UGS Dashboard, create a new key pair, and update both `UNITY_PROJECT_SERVICE_ACCOUNT_KEY` and `UNITY_PROJECT_SERVICE_ACCOUNT_SECRET` in GitHub.

---

## 4. Triggering a Deployment Manually

### Option A — Merge to staging (standard flow)

```bash
git checkout staging
git merge feature/my-feature
git push origin staging
```

The push automatically triggers the workflow.

### Option B — Workflow dispatch (one-click re-deploy)

To enable manual dispatch, ensure the workflow has this trigger:

```yaml
on:
  push:
    branches: [staging]
  workflow_dispatch:
```

Then go to **GitHub > Actions > Deploy Cloud Code Modules to Staging > Run workflow**.

### Option C — Force push to staging

```bash
git push origin staging --force-with-lease
```

Use only when re-deploying without new code changes. Prefer Option A or B.

---

## 5. Troubleshooting Common Issues

### UGS CLI installation fails

**Symptom:** `npm install -g ugs` exits non-zero.

**Fix:** Verify the `Setup Node.js 18` step ran before the install. Retry the workflow run if the npm registry is rate-limiting.

---

### Authentication fails — unrecognized argument

**Symptom:** `Unrecognized command or argument '--service-account-key-id'`

**Cause:** The UGS CLI changed its flag names in a recent version.

**Fix:** Pipe the secret via stdin — env vars alone don't work in CI's non-interactive shell:
```yaml
run: |
echo "${{ secrets.UNITY_PROJECT_SERVICE_ACCOUNT_SECRET }}" | ugs login \
--service-key-id "${{ secrets.UNITY_PROJECT_SERVICE_ACCOUNT_KEY }}" \
    --secret-key-stdin
```
For local use: `echo "<SECRET>" | ugs login --service-key-id <KEY_ID> --secret-key-stdin`

---

### Authentication fails (401 Unauthorized)

**Symptom:** "Authenticate with UGS" step fails with `401` or `invalid credentials`.

**Fixes:**
- Verify `UNITY_PROJECT_SERVICE_ACCOUNT_KEY` and `UNITY_PROJECT_SERVICE_ACCOUNT_SECRET` are set correctly in GitHub Secrets.
- Confirm the key pair was not deleted or rotated in the UGS Dashboard.
- Check the service account has the **Cloud Code Editor** role on the correct project.

---

### Artifact validation fails

**Symptom:** "Validate deployment artifacts" exits with `ERROR: ... not found`.

**Fixes:**
- Confirm `CloudCodeModule/BackpackAdventures.ccmr` is committed and pushed to the `staging` branch.
- Confirm `CloudCodeModule/BackpackAdventuresModule~/BackpackAdventuresModule.csproj` exists.
- Run `git status` locally to confirm the files are tracked.

---

### Build fails

**Symptom:** `dotnet publish` exits non-zero.

**Fixes:**
- Run `dotnet restore && dotnet publish -c Release` locally to reproduce the error.
- Check for missing NuGet packages or incompatible target framework (`net7.0` required).

---

### Deploy fails

**Symptom:** `ugs deploy` exits non-zero.

**Fixes:**
- Confirm `UNITY_PROJECT_ID` exactly matches the project ID in the UGS Dashboard (case-sensitive).
- Confirm `UNITY_ENVIRONMENT` names an existing environment for that project.
- Review UGS CLI output for API error codes — `400 Bad Request` usually indicates a malformed `.ccmr`.
- Check https://status.unity.com/ for active incidents.

---

### Verification step shows module not listed

**Symptom:** Deployment succeeds but `ugs cloud-code modules list` does not show the module.

**Fix:** UGS may take a few seconds to propagate. Wait 30 seconds and re-check manually.
