# TEST_SETUP.md — Mailbox Test Environment Setup

Before running any mailbox test that calls an admin-gated endpoint
(`SendGlobalMail`, `SendUserMail`, `PurgeExpired`), the `ADMIN_SERVICE_TOKEN`
must be configured on the UGS Dashboard and `TestConstants.AdminToken` must
match that value.

---

## Step 1 — Configure the Admin Service Token

The admin gate is now **token-based**, not allowlist-based. There is no Cloud Save
setup required. Instead, configure an environment variable in the UGS Dashboard
for the Cloud Code module.

### Via UGS Dashboard (required before first run)

1. Open the [Unity Gaming Services Dashboard](https://dashboard.unity3d.com).
2. Navigate to your project > **Cloud Code** > **Modules** > **BackpackAdventuresModule**.
3. Under **Environment Variables** (or **Secrets**), add:

```
Name:  ADMIN_SERVICE_TOKEN
Value: test-admin-token-staging
```

The value `test-admin-token-staging` matches `TestConstants.AdminToken` in
`Assets/UnityCloudCode/UnityClient/Tests/EditMode/TestConstants.cs`.

For production deployments, replace this with a strong random secret and update
`TestConstants.AdminToken` (or set it via a CI/CD environment variable) accordingly.

> **Fail-closed behaviour:** If `ADMIN_SERVICE_TOKEN` is absent or the token is
> empty/wrong, all admin calls return `Unauthorized`. This is intentional per §5.3.
> Admin positive tests will fail until the env var is configured.

### How the gate works

`AdminAuth.RequireAdminToolAsync` reads `ADMIN_SERVICE_TOKEN` from the server
environment at call time. It compares the value to the `adminToken` field in
the request body using `CryptographicOperations.FixedTimeEquals` (constant-time
comparison) to prevent timing attacks. The token is never logged.

---

## Step 2 — Verify the Target Player

`TestConstants.TargetPlayerId = "player_target_test_003"` is used in
`GiftMail` and `SendUserMail` tests as the recipient.

For gift tests to verify the target's mailbox, you need a second UGS test
account. In the current test suite, `P14` and `P03` only validate the
sender-side response, not the target's mailbox state.

To extend coverage, sign in a second player (e.g., via a second device or
test harness) and verify the recipient's `GetUserMails` contains the expected mail.

---

## Step 3 — Run Order

Run the suites in this order to avoid state interference:

1. `MailboxApiPositiveTests` — seeds and validates happy paths
2. `MailboxApiNegativeTests` — validates error handling (N01/N02/N15 use invalid tokens)
3. `MailboxApiConcurrencyTests` — fires concurrent calls
4. `MailboxApiReliabilityTests` — shape/boundary tests (explicit eviction tests skipped)

Do NOT run `[Explicit]` tests (`R04`, `R05`, `R07`) against production or shared
staging environments. They seed 200–250 mails into the test player's mailbox.

---

## Step 4 — Test Player Account Cleanup

After running tests, the test player's `mailbox_user_items` may contain
stale mails from the run. To reset:

1. Open UGS Dashboard > **Cloud Save** > **Player Data**.
2. Search for the test player by PlayerId.
3. Delete the `mailbox_user_items` key to reset the mailbox to empty.

Alternatively, call `PurgeExpired` from the Editor window (with a valid Admin Token
entered in the Admin Credentials section), then manually delete user mails via the
`DeleteMail` endpoint for any remaining notifications.

---

## Step 5 — GiftMail Quota Reset

The `GiftMail` daily quota resets at UTC midnight. If `N14_GiftMail_QuotaExceeded`
fails on re-run within the same UTC day because the quota was already exhausted,
wait for UTC midnight or reset `mailbox_meta.giftsSentToday` via UGS Dashboard.

---

## Known Test Limitations

| Test | Limitation | Workaround |
|------|-----------|------------|
| R04, R05 | Seeding 200–250 mails is destructive | Run in isolated test-data environment only |
| R06 | Requires manual v1 index seeding | UGS Dashboard manual setup |
| R03 | Requires backend fault injection | Modified Cloud Code deployment |
| C01–C05 | True concurrency depends on network timing | Flakiness possible on high-latency connections |
| N14 | Gift quota resets at UTC midnight | May need to wait or reset manually |
