// TestConstants.cs
// Test player IDs and shared constants for mailbox test suites.
// AdminPlayerId must be present in the mailbox_admin_allowlist Cloud Save custom key
// before admin-gated tests can pass. See Assets/UnityCloudCode/docs/TEST_SETUP.md.

namespace BackpackAdventures.CloudCode.Client.Tests
{
    public static class TestConstants
    {
        // -------------------------------------------------------------------
        // Player IDs
        // -------------------------------------------------------------------

        /// <summary>
        /// A player that has been added to the mailbox_admin_allowlist Cloud Save key.
        /// Admin-gated tests (SendGlobalMail, SendUserMail, PurgeExpired) sign in
        /// as this player. Must be seeded manually — see TEST_SETUP.md.
        /// </summary>
        public const string AdminPlayerId = "player_admin_test_001";

        /// <summary>
        /// A regular (non-admin) player used for negative/permission tests.
        /// Must NOT be in the mailbox_admin_allowlist.
        /// </summary>
        public const string RegularPlayerId = "player_regular_test_002";

        /// <summary>
        /// A second regular player used as a gift/mail target in send tests.
        /// </summary>
        public const string TargetPlayerId = "player_target_test_003";

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

        public const string KeyAdminAllowlist = "mailbox_admin_allowlist";
        public const string KeyUserItems = "mailbox_user_items";
        public const string KeyGlobalState = "mailbox_global_state";
        public const string KeyGlobalIndexV2 = "global_mail_index_v2";
        public const string KeyMailboxMeta = "mailbox_meta";
        public const string KeyIdempotencyCache = "mailbox_idem_cache";
    }
}
