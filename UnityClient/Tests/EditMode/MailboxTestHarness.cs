// MailboxTestHarness.cs
// Hermetic test harness — installs an in-memory FakeCloudCodeBackend with a frozen clock.
// No UGS network calls. No real authentication. Deterministic per-test state.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BackpackAdventures.CloudCode.Client.Tests
{
    /// <summary>
    /// Shared test infrastructure. Installs a fresh FakeCloudCodeBackend at every [SetUp]
    /// so each test runs against isolated in-memory state with a deterministic clock.
    /// </summary>
    public static class MailboxTestHarness
    {
        // -----------------------------------------------------------------------
        // Deterministic identity + clock
        // -----------------------------------------------------------------------

        /// <summary>Stable player id used by all tests instead of AuthenticationService.</summary>
        public const string CurrentPlayerId = "test-player-self";

        private static readonly DateTimeOffset _baseTime =
            new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);

        /// <summary>Frozen clock for the current test (set in EnsureSignedInAsync).</summary>
        public static FakeClock Clock { get; private set; }

        /// <summary>The fake backend installed for the current test (hook access).</summary>
        internal static FakeCloudCodeBackend CurrentFake { get; private set; }

        // -----------------------------------------------------------------------
        // Setup helpers (replace the old UGS sign-in)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Installs a fresh FakeCloudCodeBackend (zero state) into BackpackCloudCodeService.
        /// Call from [SetUp]. No network, no auth.
        /// </summary>
        public static async Task EnsureSignedInAsync()
        {
            Clock = new FakeClock(_baseTime);
            CurrentFake = new FakeCloudCodeBackend(Clock);
            CurrentFake.CurrentPlayerId = CurrentPlayerId;
            BackpackCloudCodeService.Backend = CurrentFake;
            await Task.CompletedTask;
        }

        /// <summary>Admin setup — same as sign-in for the fake (token passed per-call).</summary>
        public static async Task EnsureAdminAsync()
        {
            await EnsureSignedInAsync();
        }

        /// <summary>
        /// [TearDown] cleanup: restore the production backend. No network — state is
        /// discarded with the per-test fake instance.
        /// </summary>
        public static async Task CleanupAsync()
        {
            BackpackCloudCodeService.ResetToDefault();
            CurrentFake = null;
            Clock = null;
            await Task.CompletedTask;
        }

        // -----------------------------------------------------------------------
        // Fixture builders
        // -----------------------------------------------------------------------

        /// <summary>Currency attachment list for send requests.</summary>
        public static List<MailAttachment> MakeCurrencyAttachment(int quantity = 100)
        {
            return new List<MailAttachment>
            {
                new MailAttachment
                {
                    itemId = TestConstants.CurrencyItemId,
                    type = TestConstants.CurrencyType,
                    quantity = quantity,
                    id = TestConstants.CurrencyItemId,
                    amount = quantity
                }
            };
        }

        /// <summary>Item attachment list for send requests.</summary>
        public static List<MailAttachment> MakeItemAttachment(string itemId = null, int quantity = 1)
        {
            string resolvedId = itemId ?? TestConstants.ItemId;
            return new List<MailAttachment>
            {
                new MailAttachment
                {
                    itemId = resolvedId,
                    type = TestConstants.ItemType,
                    quantity = quantity,
                    id = resolvedId,
                    amount = quantity
                }
            };
        }

        /// <summary>ISO-8601 UTC timestamp offset from the frozen clock.</summary>
        public static string FutureExpiry(int seconds = 3600) =>
            Clock.UtcNow.AddSeconds(seconds).ToString("o");

        /// <summary>ISO-8601 UTC timestamp already in the past relative to the frozen clock.</summary>
        public static string PastExpiry(int secondsAgo = 120) =>
            Clock.UtcNow.AddSeconds(-secondsAgo).ToString("o");

        // -----------------------------------------------------------------------
        // Error classification helpers (match FakeCloudCodeBackend exception messages)
        // -----------------------------------------------------------------------

        private static string Msg(Exception ex) => (ex?.Message ?? string.Empty).ToLowerInvariant();

        public static bool IsUnauthorizedError(Exception ex)
        {
            string m = Msg(ex);
            return m.Contains("401") || m.Contains("unauthorized") || m.Contains("notadmin");
        }

        public static bool IsInvalidInputError(Exception ex)
        {
            string m = Msg(ex);
            return m.Contains("400") || m.Contains("invalid") || m.Contains("invalidinput")
                   || m.Contains("bad request") || m.Contains("validation");
        }

        public static bool IsNotFoundError(Exception ex)
        {
            string m = Msg(ex);
            return m.Contains("404") || m.Contains("notfound") || m.Contains("mailnotfound");
        }

        public static bool IsMailExpiredError(Exception ex)
        {
            string m = Msg(ex);
            return m.Contains("mailexpired") || m.Contains("expired");
        }

        public static bool IsAlreadyClaimedError(Exception ex)
        {
            return Msg(ex).Contains("alreadyclaimed");
        }

        public static bool IsMailboxFullError(Exception ex)
        {
            return Msg(ex).Contains("mailboxfull");
        }

        public static bool IsGiftRateLimitedError(Exception ex)
        {
            string m = Msg(ex);
            return m.Contains("giftquotaexceeded") || m.Contains("ratelimited") || m.Contains("quota");
        }

        public static bool IsCannotDeleteError(Exception ex)
        {
            string m = Msg(ex);
            return m.Contains("cannotdelete") || m.Contains("cannotdeleteunclaimedreward")
                   || m.Contains("cannotdeleteglobal");
        }

        public static bool IsNoAttachmentError(Exception ex)
        {
            string m = Msg(ex);
            return m.Contains("noattachment") || m.Contains("no attachment");
        }
    }
}
