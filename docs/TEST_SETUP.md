# TEST_SETUP.md — Mailbox Test Environment Setup

Before running any mailbox test that calls an admin-gated endpoint
(`SendGlobalMail`, `SendUserMail`, `PurgeExpired`), the admin player
must be seeded into the Cloud Save allowlist on the UGS Dashboard.

---

## Step 1 — Seed the Admin Allowlist

The `mailbox_admin_allowlist` is a **Custom Data** key (project-wide scope).
It is NOT a player-private key — it lives in the default Cloud Save custom collection.

### Via UGS Dashboard (required before first run)

1. Open the [Unity Gaming Services Dashboard](https://dashboard.unity3d.com).
2. Navigate to your project > **Cloud Save** > **Custom Data**.
3. Find or create the key: `mailbox_admin_allowlist`.
4. Set the value to the following JSON (replace `<admin-player-id>` with the actual UGS PlayerId):

```json
{
  "version": 1,
  "playerIds": [
    "player_admin_test_001"
  ]
}
```

The value `player_admin_test_001` matches `TestConstants.AdminPlayerId` in
`Assets/UnityCloudCode/UnityClient/Tests/EditMode/TestConstants.cs`.

If the test suite is run with a different UGS PlayerId (e.g., the
PlayerId assigned by Unity's anonymous sign-in for a real device),
update both `TestConstants.AdminPlayerId` and the Dashboard value accordingly.

> **Fail-closed behaviour:** If the key is absent or the test PlayerId is
> not in the list, all admin calls return `Unauthorized (NotAdmin)`.
> This is intentional per §5.3. Admin positive tests will fail until
> the allowlist is seeded.

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
2. `MailboxApiNegativeTests` — validates error handling
3. `MailboxApiConcurrencyTests` — fires concurrent calls (admin + regular player)
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

Alternatively, call `PurgeExpired` from the Editor window, then manually
delete user mails via the `DeleteMail` endpoint for any remaining notifications.

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
