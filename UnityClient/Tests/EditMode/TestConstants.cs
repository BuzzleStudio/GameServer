// TestConstants.cs
// Test player IDs and shared constants for mailbox test suites.
// Admin-gated tests pass TestConstants.AdminToken directly in the request body.
// See Assets/UnityCloudCode/docs/TEST_SETUP.md.

namespace BackpackAdventures.CloudCode.Client.Tests
{
    public static class TestConstants
    {
        // -------------------------------------------------------------------
        // Player IDs
        // -------------------------------------------------------------------

        /// <summary>
        /// A regular (non-admin) player used for negative/permission tests.
        /// Must NOT have a valid admin token for admin tests to be meaningful.
        /// </summary>
        public const string RegularPlayerId = "player_regular_test_002";

        /// <summary>
        /// A second regular player used as a gift/mail target in send tests.
        /// </summary>
        public const string TargetPlayerId = "player_target_test_003";

        // -------------------------------------------------------------------
        // Admin credentials
        // -------------------------------------------------------------------

        /// <summary>
        /// Admin service token matching ADMIN_SERVICE_TOKEN env var on UGS staging.
        /// Must match what is configured in the UGS Dashboard Cloud Code module secrets.
        /// </summary>
        public const string AdminToken = "test-admin-token-staging";

        /// <summary>Operator ID used for admin audit logging in tests.</summary>
        public const string OperatorId = "tester@backpackadventures.test";

        // -------------------------------------------------------------------
        // Mail content fixtures
        // -------------------------------------------------------------------

        public const string DefaultSubject = "Test subject";
        public const string DefaultBody = "Test body";

        /// <summary>Subject exactly at the 128-character limit (valid).</summary>
        public const string SubjectAtLimit =
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAA";  // 128 chars total

        /// <summary>Subject one character over the 128-character limit (invalid).</summary>
        public const string SubjectOverLimit =
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAAAAAA";  // 129 chars total

        /// <summary>Body exactly at the 1024-character limit (valid).</summary>
        public static string BodyAtLimit => new string('B', 1024);

        /// <summary>Body one character over the 1024-character limit (invalid).</summary>
        public static string BodyOverLimit => new string('B', 1025);

        // -------------------------------------------------------------------
        // Item / currency identifiers
        // -------------------------------------------------------------------

        public const string CurrencyItemId = "gold";
        public const string ItemId = "chest_rare";
        public const string CurrencyType = "currency";
        public const string ItemType = "item";

        // -------------------------------------------------------------------
        // Idempotency / dedup keys
        // -------------------------------------------------------------------

        public const string DedupKeyTest = "test-dedup-key-001";

        // -------------------------------------------------------------------
        // Limits (mirror server constants — update if server values change)
        // -------------------------------------------------------------------

        public const int PageSizeDefault = 20;
        public const int PageSizeMax = 50;
        public const int PageSizeOverLimit = 51;
        public const int GiftDailyQuota = 5;
        public const int UserMailSoftCap = 200;
        public const int UserMailHardCap = 250;
        public const int GlobalMailIndexCap = 500;

        // -------------------------------------------------------------------
        // Cloud Save key names (read-only reference for test harness)
        // -------------------------------------------------------------------

        public const string KeyUserItems = "mailbox_user_items";
        public const string KeyGlobalState = "mailbox_global_state";
        public const string KeyGlobalIndexV2 = "global_mail_index_v2";
        public const string KeyMailboxMeta = "mailbox_meta";
        public const string KeyIdempotencyCache = "mailbox_idem_cache";
    }
}
