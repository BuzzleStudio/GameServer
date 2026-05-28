// MailboxTestHarness.cs
// Shared setup, sign-in helpers, and cleanup utilities for mailbox test classes.
// All methods are static async Tasks — no MonoBehaviour, no coroutines.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace BackpackAdventures.CloudCode.Client.Tests
{
    /// <summary>
    /// Shared test infrastructure for all mailbox EditMode tests.
    /// Does not extend any MonoBehaviour. Intended for use inside NUnit [SetUp] methods.
    /// </summary>
    public static class MailboxTestHarness
    {
        // -----------------------------------------------------------------------
        // Authentication helpers
        // -----------------------------------------------------------------------

        /// <summary>
        /// Initialises Unity Services and signs in anonymously if not already signed in.
        /// Call from [SetUp] or at the start of each integration test.
        /// </summary>
        public static async Task EnsureSignedInAsync()
        {
            try
            {
                if (UnityServices.State == ServicesInitializationState.Uninitialized)
                    await UnityServices.InitializeAsync();

                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
            catch (Exception ex)
            {
                Assert.Fail($"[MailboxTestHarness] EnsureSignedInAsync failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Ensures Unity Services is initialised and the test player is signed in.
        /// Admin-gated tests no longer require a specific playerId in an allowlist —
        /// they pass TestConstants.AdminToken directly in the request body.
        /// </summary>
        public static async Task EnsureAdminAsync()
        {
            await EnsureSignedInAsync();
        }

        // -----------------------------------------------------------------------
        // Fixture builders
        // -----------------------------------------------------------------------

        /// <summary>Creates a single currency attachment list for use in send requests.</summary>
        public static List<MailAttachment> MakeCurrencyAttachment(int quantity = 100)
        {
            return new List<MailAttachment>
            {
                new MailAttachment
                {
                    itemId = TestConstants.CurrencyItemId,
                    type = TestConstants.CurrencyType,
                    quantity = quantity,
                    // Legacy field aliases kept for backward compatibility
                    id = TestConstants.CurrencyItemId,
                    amount = quantity
                }
            };
        }

        /// <summary>Creates a single item attachment list for use in send requests.</summary>
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

        /// <summary>Returns an ISO-8601 UTC timestamp offset from now by <paramref name="seconds"/>.</summary>
        public static string FutureExpiry(int seconds = 3600) =>
            DateTime.UtcNow.AddSeconds(seconds).ToString("o");

        /// <summary>Returns an ISO-8601 UTC timestamp that is already in the past.</summary>
        public static string PastExpiry(int secondsAgo = 120) =>
            DateTime.UtcNow.AddSeconds(-secondsAgo).ToString("o");

        // -----------------------------------------------------------------------
        // Error classification helpers
        // -----------------------------------------------------------------------

        public static bool IsUnauthorizedError(Exception ex)
        {
            string msg = (ex?.Message ?? "").ToLowerInvariant();
            return msg.Contains("401") || msg.Contains("unauthorized") || msg.Contains("notadmin");
        }

        public static bool IsInvalidInputError(Exception ex)
        {
            string msg = (ex?.Message ?? "").ToLowerInvariant();
            return msg.Contains("400") || msg.Contains("invalid") || msg.Contains("invalidinput")
                   || msg.Contains("bad request") || msg.Contains("validation");
        }

        public static bool IsNotFoundError(Exception ex)
        {
            string msg = (ex?.Message ?? "").ToLowerInvariant();
            return msg.Contains("404") || msg.Contains("notfound") || msg.Contains("mailnotfound");
        }

        public static bool IsMailExpiredError(Exception ex)
        {
            string msg = (ex?.Message ?? "").ToLowerInvariant();
            return msg.Contains("mailexpired") || msg.Contains("expired");
        }

        public static bool IsAlreadyClaimedError(Exception ex)
        {
            string msg = (ex?.Message ?? "").ToLowerInvariant();
            return msg.Contains("alreadyclaimed");
        }

        public static bool IsMailboxFullError(Exception ex)
        {
            string msg = (ex?.Message ?? "").ToLowerInvariant();
            return msg.Contains("mailboxfull");
        }

        public static bool IsGiftRateLimitedError(Exception ex)
        {
            string msg = (ex?.Message ?? "").ToLowerInvariant();
            return msg.Contains("giftquotaexceeded") || msg.Contains("ratelimited") || msg.Contains("quota");
        }

        public static bool IsCannotDeleteError(Exception ex)
        {
            string msg = (ex?.Message ?? "").ToLowerInvariant();
            return msg.Contains("cannotdelete") || msg.Contains("cannotdeleteunclaimedreward")
                   || msg.Contains("cannotdeleteglobal");
        }

        public static bool IsNoAttachmentError(Exception ex)
        {
            string msg = (ex?.Message ?? "").ToLowerInvariant();
            return msg.Contains("noattachment") || msg.Contains("no attachment");
        }

        // -----------------------------------------------------------------------
        // Cleanup
        // -----------------------------------------------------------------------

        /// <summary>
        /// Best-effort cleanup: purges expired global mail refs via admin PurgeExpired.
        ///
        /// Not guaranteed to clean all state because:
        ///   - PurgeExpired requires admin privileges.
        ///   - User-mail state (mailbox_user_items) persists per player across tests unless
        ///     explicitly deleted or the test player account is reset via UGS Dashboard.
        ///
        /// Call from [TearDown] after integration tests.
        /// </summary>
        public static async Task CleanupAsync()
        {
            try
            {
                await BackpackCloudCodeService.CallPurgeExpiredAsync(TestConstants.AdminToken, TestConstants.OperatorId);
            }
            catch (Exception ex)
            {
                // Cleanup failure is non-fatal; log but do not fail the test.
                Debug.LogWarning($"[MailboxTestHarness] CleanupAsync — PurgeExpired failed (non-fatal): {ex.Message}");
            }
        }
    }
}
