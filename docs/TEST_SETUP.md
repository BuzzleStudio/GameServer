# TEST_SETUP.md - Mailbox Test Environment Setup

Before running any mailbox test that calls an admin-gated endpoint
(`SendGlobalMail`, `SendUserMail`, `PurgeExpired`), call through a Unity
service-account REST request. Player-authenticated calls are rejected.

---

## Step 1 - Configure the Project-Scoped Service Account

The admin gate is service-account based, not allowlist-based and not
`ADMIN_SERVICE_TOKEN` based. There is no Cloud Save setup required.

### Via UGS Dashboard

1. Open the Unity Gaming Services Dashboard.
2. Create or select a service account scoped to this project.
3. Assign the **Cloud Code Editor** role on the target project only.
4. Create a key pair and store it in the caller environment as:
   - `UNITY_PROJECT_SERVICE_ACCOUNT_KEY`
   - `UNITY_PROJECT_SERVICE_ACCOUNT_SECRET`

These values are the service account key pair. They are not arbitrary Unity
Secret Manager project secrets.

> Fail-closed behaviour: if a call is not made with a service-account token,
> all admin calls return `Unauthorized`. This is intentional per 5.3.

### How the gate works

`AdminAuth.RequireAdminToolAsync` checks the Cloud Code execution context. Calls
pass only when the context has a service token or non-player access token and no
player ID. Token values are never logged.

---

## Step 2 - Verify the Target Player

`TestConstants.TargetPlayerId = "player_target_test_003"` is used in
`GiftMail` and `SendUserMail` tests as the recipient.

For gift tests to verify the target's mailbox, you need a second UGS test
account. In the current test suite, `P14` and `P03` only validate the
sender-side response, not the target's mailbox state.

To extend coverage, sign in a second player or pass a real target Player ID to
the `workflow_dispatch` input `admin_test_player_id` and verify the recipient's
`GetUserMails` contains the expected mail.

---

## Step 3 - Run Order

Run the suites in this order to avoid state interference:

1. `MailboxApiPositiveTests` - seeds and validates happy paths
2. `MailboxApiNegativeTests` - validates error handling
3. `MailboxApiConcurrencyTests` - fires concurrent calls
4. `MailboxApiReliabilityTests` - shape/boundary tests

Do not run `[Explicit]` tests (`R04`, `R05`, `R07`) against production or shared
staging environments. They seed 200-250 mails into the test player's mailbox.

---

## Step 4 - Test Player Account Cleanup

After running tests, the test player's `mailbox_user_items` may contain stale
mails from the run. To reset:

1. Open UGS Dashboard > Cloud Save > Player Data.
2. Search for the test player by PlayerId.
3. Delete the `mailbox_user_items` key to reset the mailbox to empty.

Alternatively, call `PurgeExpired` from the Editor window with a project-scoped
service account configured, then manually delete user mails via the `DeleteMail`
endpoint for any remaining notifications.

---

## Step 5 - GiftMail Quota Reset

The `GiftMail` daily quota resets at UTC midnight. If `N14_GiftMail_QuotaExceeded`
fails on re-run within the same UTC day because the quota was already exhausted,
wait for UTC midnight or reset `mailbox_meta.giftsSentToday` via UGS Dashboard.

---

## Known Test Limitations

| Test | Limitation | Workaround |
|------|-----------|------------|
| R04, R05 | Seeding 200-250 mails is destructive | Run in isolated test-data environment only |
| R06 | Requires manual v1 index seeding | UGS Dashboard manual setup |
| R03 | Requires backend fault injection | Modified Cloud Code deployment |
| C01-C05 | True concurrency depends on network timing | Flakiness possible on high-latency connections |
| N14 | Gift quota resets at UTC midnight | May need to wait or reset manually |
