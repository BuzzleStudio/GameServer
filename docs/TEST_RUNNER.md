# TEST_RUNNER.md — How to Run the Mailbox Test Suite

## Test Files

| File | Suite class | Category | Tests |
|------|------------|----------|-------|
| `Tests/EditMode/MailboxApiPositiveTests.cs` | `MailboxApiPositiveTests` | Positive | P01–P16 (16) |
| `Tests/EditMode/MailboxApiNegativeTests.cs` | `MailboxApiNegativeTests` | Negative | N01–N16 (16) |
| `Tests/EditMode/MailboxApiConcurrencyTests.cs` | `MailboxApiConcurrencyTests` | Concurrency | C01–C05 (5) |
| `Tests/EditMode/MailboxApiReliabilityTests.cs` | `MailboxApiReliabilityTests` | Reliability | R01–R10 (10, 5 explicit) |
| `Tests/EditMode/MailboxTestRunner.cs` | `MailboxTestRunner` (static) | — | Runner entry points |
| `Tests/EditMode/MailboxTestHarness.cs` | `MailboxTestHarness` (static) | — | Shared setup/cleanup |
| `Tests/EditMode/TestConstants.cs` | `TestConstants` (static) | — | Constants |

Total automated tests: **42** (37 auto-run + 5 `[Explicit]` that require dedicated environments).

---

## Method 1 — Unity Test Runner (Window > General > Test Runner)

1. Open Unity Editor with the project loaded.
2. Open the Test Runner: **Window > General > Test Runner**.
3. Select the **EditMode** tab.
4. Expand `BackpackAdventures.CloudCode.Client.Tests`.
5. Run individual suites or all at once:
   - Right-click `MailboxApiPositiveTests` > **Run**.
   - Right-click `MailboxApiNegativeTests` > **Run**.
   - Right-click `MailboxApiConcurrencyTests` > **Run**.
   - Right-click `MailboxApiReliabilityTests` > **Run** (explicit tests skipped unless individually triggered).
   - Or click **Run All** to run everything.
6. Check the results panel for PASS / FAIL / SKIP.

> `[Explicit]` tests (`R04`, `R05`, `R07`) will not run from "Run All".
> To run them, right-click individually in the Test Runner.

---

## Method 2 — QA Editor Window (MailboxAdminToolWindow)

The `MailboxAdminToolWindow` (created by the Client Tooling agent) exposes:

1. Open: **BackpackAdventures > Mailbox Admin Tool**.
2. In the **QA** panel, click one of:
   - **Run All Tests** → calls `await MailboxTestRunner.RunAllAsync()`
   - **Run Positive** → calls `await MailboxTestRunner.RunPositiveAsync()`
   - **Run Negative** → calls `await MailboxTestRunner.RunNegativeAsync()`
   - **Run Concurrency** → calls `await MailboxTestRunner.RunConcurrencyAsync()`
   - **Run Reliability** → calls `await MailboxTestRunner.RunReliabilityAsync()`
3. Results are displayed in the scrollable log panel and also written to the Unity Console.

---

## Method 3 — DevOps Post-Deploy Hook (Headless CI)

### Full Unity Test Runner CLI (requires Unity license on CI)

The post-deploy CI step in `.github/workflows/staging-deploy.yml` must:

```yaml
- name: Run Mailbox EditMode Tests
  run: |
    unity-editor-path/Unity \
      -runTests \
      -testPlatform EditMode \
      -testFilter "BackpackAdventures.CloudCode.Client.Tests" \
      -testResults TestResults/mailbox-results.xml \
      -batchmode \
      -nographics \
      -quit
  env:
    UNITY_LICENSE: ${{ secrets.UNITY_LICENSE }}
```

Test results are written to `TestResults/mailbox-results.xml` (NUnit XML format).
Parse with a JUnit/NUnit reporter step:

```yaml
- name: Publish Test Results
  uses: mikepenz/action-junit-report@v4
  if: always()
  with:
    report_paths: 'TestResults/mailbox-results.xml'
```

### Alternative: UGS CLI Smoke Tests (no Unity license required)

For headless CI without a Unity license, DevOps invokes a subset of API smoke tests
using the UGS CLI against the deployed Cloud Code module.

**Prerequisite:** UGS CLI installed and configured with `UNITY_PROJECT_SERVICE_ACCOUNT_KEY`
and `UNITY_PROJECT_SERVICE_ACCOUNT_SECRET` environment variables from a project-scoped service account.

#### Exact CLI commands (smoke suite)

```bash
# 1. Health check — confirms module deployed and reachable
if ! ugs cloud-code run BackpackAdventuresModule HealthCheck \
    --project-id "$UNITY_PROJECT_ID" \
    --environment-name "$UNITY_ENVIRONMENT" | grep -q '"success":true'; then
  echo "FAIL: HealthCheck did not return success=true"
  exit 1
fi
echo "PASS: HealthCheck"

# 2. GetUserMails smoke — confirms mailbox endpoint is reachable
if ! ugs cloud-code run BackpackAdventuresModule GetUserMails \
    --args '{"request":{"page":0,"pageSize":1}}' \
    --project-id "$UNITY_PROJECT_ID" \
    --environment-name "$UNITY_ENVIRONMENT" | grep -q '"success":true'; then
  echo "FAIL: GetUserMails did not return success=true"
  exit 1
fi
echo "PASS: GetUserMails smoke"

# 3. GetGlobalMails smoke — confirms index endpoint is reachable
if ! ugs cloud-code run BackpackAdventuresModule GetGlobalMails \
    --args '{"request":{"page":0,"pageSize":1}}' \
    --project-id "$UNITY_PROJECT_ID" \
    --environment-name "$UNITY_ENVIRONMENT" | grep -q '"success":true'; then
  echo "FAIL: GetGlobalMails did not return success=true"
  exit 1
fi
echo "PASS: GetGlobalMails smoke"

# 4. SendGlobalMail admin gate check — non-admin caller must get Unauthorized
RESULT=$(ugs cloud-code run BackpackAdventuresModule SendGlobalMail \
    --args '{"request":{"subject":"Smoke","body":"Smoke body"}}' \
    --project-id "$UNITY_PROJECT_ID" \
    --environment-name "$UNITY_ENVIRONMENT" 2>&1 || true)
if echo "$RESULT" | grep -qi "notadmin\|unauthorized\|401"; then
  echo "PASS: SendGlobalMail admin gate (non-admin rejected as expected)"
else
  echo "FAIL: SendGlobalMail admin gate not enforced — check allowlist or module deploy"
  echo "Response: $RESULT"
  exit 1
fi
```

> The UGS CLI smoke tests verify that:
> (a) the module is deployed and callable,
> (b) basic mailbox endpoints respond with success,
> (c) the admin gate rejects unauthenticated / non-admin callers.
>
> They do NOT replace the full Unity Test Runner suite. Run the full suite
> (Method 1 or 2) in a staging environment before promoting to production.

---

## Test Suite Entry Points (for DevOps reference)

| Entry point | How to invoke |
|-------------|---------------|
| Full EditMode suite | Unity Test Runner CLI with `-testFilter "BackpackAdventures.CloudCode.Client.Tests"` |
| Positive only | `-testFilter "BackpackAdventures.CloudCode.Client.Tests.MailboxApiPositiveTests"` |
| Negative only | `-testFilter "BackpackAdventures.CloudCode.Client.Tests.MailboxApiNegativeTests"` |
| Concurrency only | `-testFilter "BackpackAdventures.CloudCode.Client.Tests.MailboxApiConcurrencyTests"` |
| Reliability only | `-testFilter "BackpackAdventures.CloudCode.Client.Tests.MailboxApiReliabilityTests"` |
| Editor window | `await MailboxTestRunner.RunAllAsync()` from `MailboxAdminToolWindow` |
| UGS CLI smoke | See "Exact CLI commands" section above |
