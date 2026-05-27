using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace BackpackAdventures.CloudCode.Client
{
    public class MailboxIntegrationTest : MonoBehaviour
    {
        [SerializeField] private string targetUserId = "player_target_456";

        // -----------------------------------------------------------------------
        // Entry points
        // -----------------------------------------------------------------------

        [ContextMenu("Run All Mailbox Tests")]
        public void RunAllTests() => RunAllTestsAsync();

        [ContextMenu("Run Positive Tests")]
        public void RunPositiveTests() => RunPositiveTestsAsync();

        [ContextMenu("Run Negative Tests")]
        public void RunNegativeTests() => RunNegativeTestsAsync();

        [ContextMenu("Run Edge Case Tests")]
        public void RunEdgeCaseTests() => RunEdgeCaseTestsAsync();

        // -----------------------------------------------------------------------
        // Orchestrators
        // -----------------------------------------------------------------------

        private async void RunAllTestsAsync()
        {
            Debug.Log("[MailboxTest] === Starting all mailbox tests ===");
            if (!await EnsureInitialized()) return;

            await RunPositiveTestsCore();
            await RunNegativeTestsCore();
            await RunEdgeCaseTestsCore();

            Debug.Log("[MailboxTest] === All mailbox tests complete ===");
        }

        private async void RunPositiveTestsAsync()
        {
            Debug.Log("[MailboxTest] === Starting positive tests ===");
            if (!await EnsureInitialized()) return;
            await RunPositiveTestsCore();
            Debug.Log("[MailboxTest] === Positive tests complete ===");
        }

        private async void RunNegativeTestsAsync()
        {
            Debug.Log("[MailboxTest] === Starting negative tests ===");
            if (!await EnsureInitialized()) return;
            await RunNegativeTestsCore();
            Debug.Log("[MailboxTest] === Negative tests complete ===");
        }

        private async void RunEdgeCaseTestsAsync()
        {
            Debug.Log("[MailboxTest] === Starting edge case tests ===");
            if (!await EnsureInitialized()) return;
            await RunEdgeCaseTestsCore();
            Debug.Log("[MailboxTest] === Edge case tests complete ===");
        }

        private async Task RunPositiveTestsCore()
        {
            await Test_P01_SendGlobalMail_NoAttachment();
            await Test_P02_SendGlobalMail_WithAttachment();
            await Test_P03_SendUserMail_NoAttachment();
            await Test_P04_SendUserMail_WithAttachment();
            await Test_P05_GetMailbox_ContainsGlobalAndUserMails();
            await Test_P06_GetMailbox_ExpiredMailsFiltered();
            await Test_P07_MarkMailRead_UpdatesState();
            await Test_P08_ClaimAttachment_ReturnsReward();
            await Test_P09_ClaimAttachment_Idempotent();
        }

        private async Task RunNegativeTestsCore()
        {
            await Test_N01_SendUserMail_EmptyUserId();
            await Test_N02_SendGlobalMail_MissingSubject();
            await Test_N03_ClaimAttachment_InvalidMailId();
            await Test_N04_ClaimAttachment_NoAttachment();
            await Test_N05_ClaimAttachment_ExpiredMail();
            await Test_N06_GetMailbox_NoMails_ReturnsEmptyList();
            await Test_N07_MarkMailRead_InvalidMailId();
        }

        private async Task RunEdgeCaseTestsCore()
        {
            await Test_E01_SendGlobalMail_VeryLongBody();
            await Test_E02_SendGlobalMail_MultipleAttachments();
            await Test_E03_ClaimAttachment_RaceCondition();
            await Test_E04_GetMailbox_ManyMails();
            await Test_E05_Mail_AtExpiryBoundary();
            await Test_E06_SendUserMail_ToSelf();
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private async Task<bool> EnsureInitialized()
        {
            try
            {
                await BackpackCloudCodeService.InitializeAsync();
                Debug.Log("[MailboxTest] Signed in as: " + AuthenticationService.Instance.PlayerId);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[MailboxTest] FAIL — InitializeAsync: " + ex.Message);
                return false;
            }
        }

        private static List<MailAttachment> MakeCurrencyAttachment(int amount = 100) =>
            new List<MailAttachment>
            {
                new MailAttachment { type = "currency", id = "gold", amount = amount }
            };

        private static string FutureExpiry(int secondsFromNow = 3600) =>
            DateTime.UtcNow.AddSeconds(secondsFromNow).ToString("o");

        private static string PastExpiry(int secondsAgo = 60) =>
            DateTime.UtcNow.AddSeconds(-secondsAgo).ToString("o");

        // Sends a global mail and returns its mailId, or null on failure.
        private static async Task<string> SendTestGlobalMailAndGetId(
            string subject = "Test subject",
            string body = "Test body",
            List<MailAttachment> attachments = null,
            string expiresAt = null)
        {
            try
            {
                var resp = await BackpackCloudCodeService.SendGlobalMailAsync(subject, body, expiresAt, attachments);
                if (resp == null || !resp.success || string.IsNullOrEmpty(resp.mailId))
                    return null;
                return resp.mailId;
            }
            catch
            {
                return null;
            }
        }

        // -----------------------------------------------------------------------
        // Positive tests
        // -----------------------------------------------------------------------

        private async Task Test_P01_SendGlobalMail_NoAttachment()
        {
            const string id = "P01";
            Debug.Log($"[MailboxTest] --- {id} SendGlobalMail (notification only) ---");
            try
            {
                var resp = await BackpackCloudCodeService.SendGlobalMailAsync(
                    "Server maintenance", "Maintenance at midnight UTC.");

                bool pass = resp != null
                    && resp.success
                    && !string.IsNullOrEmpty(resp.mailId)
                    && !string.IsNullOrEmpty(resp.sentAt);

                Log(id, pass, pass ? null : $"Unexpected response: success={resp?.success} mailId={resp?.mailId}");
            }
            catch (Exception ex) { LogException(id, ex); }
        }

        private async Task Test_P02_SendGlobalMail_WithAttachment()
        {
            const string id = "P02";
            Debug.Log($"[MailboxTest] --- {id} SendGlobalMail (with currency attachment) ---");
            try
            {
                var resp = await BackpackCloudCodeService.SendGlobalMailAsync(
                    "Login reward", "Here is your reward!",
                    FutureExpiry(), MakeCurrencyAttachment(500));

                bool pass = resp != null && resp.success && !string.IsNullOrEmpty(resp.mailId);
                Log(id, pass, pass ? null : $"Unexpected response: success={resp?.success}");
            }
            catch (Exception ex) { LogException(id, ex); }
        }

        private async Task Test_P03_SendUserMail_NoAttachment()
        {
            const string id = "P03";
            Debug.Log($"[MailboxTest] --- {id} SendUserMail (notification only) ---");
            try
            {
                var resp = await BackpackCloudCodeService.SendUserMailAsync(
                    targetUserId, "Friend request", "Player X wants to be your friend.");

                bool pass = resp != null && resp.success && !string.IsNullOrEmpty(resp.mailId);
                Log(id, pass, pass ? null : $"Unexpected response: success={resp?.success}");
            }
            catch (Exception ex) { LogException(id, ex); }
        }

        private async Task Test_P04_SendUserMail_WithAttachment()
        {
            const string id = "P04";
            Debug.Log($"[MailboxTest] --- {id} SendUserMail (with item attachment) ---");
            try
            {
                var attachments = new List<MailAttachment>
                {
                    new MailAttachment { type = "item", id = "rare_sword", amount = 1 }
                };
                var resp = await BackpackCloudCodeService.SendUserMailAsync(
                    targetUserId, "Gift from GM", "Enjoy this rare item!", FutureExpiry(), attachments);

                bool pass = resp != null && resp.success && !string.IsNullOrEmpty(resp.mailId);
                Log(id, pass, pass ? null : $"Unexpected response: success={resp?.success}");
            }
            catch (Exception ex) { LogException(id, ex); }
        }

        private async Task Test_P05_GetMailbox_ContainsGlobalAndUserMails()
        {
            const string id = "P05";
            Debug.Log($"[MailboxTest] --- {id} GetMailbox — global + user mails present ---");
            try
            {
                // Pre-seed one of each so the mailbox has known content.
                await BackpackCloudCodeService.SendGlobalMailAsync("Global seed P05", "body");
                var selfId = AuthenticationService.Instance.PlayerId;
                await BackpackCloudCodeService.SendUserMailAsync(selfId, "User seed P05", "body");

                var resp = await BackpackCloudCodeService.GetMailboxAsync();

                bool hasGlobal = resp?.mails?.Any(m => m.subject == "Global seed P05") ?? false;
                bool hasUser   = resp?.mails?.Any(m => m.subject == "User seed P05") ?? false;
                bool pass = resp != null && resp.success && hasGlobal && hasUser;
                Log(id, pass, pass ? null
                    : $"success={resp?.success} hasGlobal={hasGlobal} hasUser={hasUser} count={resp?.mails?.Count}");
            }
            catch (Exception ex) { LogException(id, ex); }
        }

        private async Task Test_P06_GetMailbox_ExpiredMailsFiltered()
        {
            const string id = "P06";
            Debug.Log($"[MailboxTest] --- {id} GetMailbox — expired mails excluded ---");
            try
            {
                // Send a mail that is already expired.
                var expiredResp = await BackpackCloudCodeService.SendGlobalMailAsync(
                    "Expired P06", "should not appear", PastExpiry());

                var mailboxResp = await BackpackCloudCodeService.GetMailboxAsync();

                bool expiredPresent = mailboxResp?.mails?.Any(m => m.subject == "Expired P06") ?? false;
                bool pass = mailboxResp != null && mailboxResp.success && !expiredPresent;
                Log(id, pass, pass ? null
                    : $"Expired mail found in mailbox (expiredMailId={expiredResp?.mailId})");
            }
            catch (Exception ex) { LogException(id, ex); }
        }

        private async Task Test_P07_MarkMailRead_UpdatesState()
        {
            const string id = "P07";
            Debug.Log($"[MailboxTest] --- {id} MarkMailRead — read flag updates ---");
            try
            {
                var selfId = AuthenticationService.Instance.PlayerId;
                var sendResp = await BackpackCloudCodeService.SendUserMailAsync(
                    selfId, "Read-me P07", "body");
                if (sendResp == null || !sendResp.success)
                {
                    Log(id, false, "Pre-condition failed: could not send mail");
                    return;
                }

                var markResp = await BackpackCloudCodeService.MarkMailReadAsync(sendResp.mailId);
                bool markOk = markResp != null && markResp.success && markResp.isRead;

                // Verify via GetMailbox that the flag persisted.
                var mailboxResp = await BackpackCloudCodeService.GetMailboxAsync();
                var mail = mailboxResp?.mails?.FirstOrDefault(m => m.mailId == sendResp.mailId);
                bool persistOk = mail != null && mail.isRead;

                bool pass = markOk && persistOk;
                Log(id, pass, pass ? null
                    : $"markOk={markOk} persistOk={persistOk} isRead={mail?.isRead}");
            }
            catch (Exception ex) { LogException(id, ex); }
        }

        private async Task Test_P08_ClaimAttachment_ReturnsReward()
        {
            const string id = "P08";
            Debug.Log($"[MailboxTest] --- {id} ClaimAttachment — reward returned ---");
            try
            {
                var selfId = AuthenticationService.Instance.PlayerId;
                var sendResp = await BackpackCloudCodeService.SendUserMailAsync(
                    selfId, "Claim me P08", "body", null, MakeCurrencyAttachment(200));
                if (sendResp == null || !sendResp.success)
                {
                    Log(id, false, "Pre-condition failed: could not send mail");
                    return;
                }

                var claimResp = await BackpackCloudCodeService.ClaimAttachmentAsync(sendResp.mailId);
                bool pass = claimResp != null
                    && claimResp.success
                    && (claimResp.claimedAttachments?.Count ?? 0) > 0
                    && !claimResp.alreadyClaimed;

                Log(id, pass, pass ? null
                    : $"success={claimResp?.success} alreadyClaimed={claimResp?.alreadyClaimed} itemCount={claimResp?.claimedAttachments?.Count}");
            }
            catch (Exception ex) { LogException(id, ex); }
        }

        private async Task Test_P09_ClaimAttachment_Idempotent()
        {
            const string id = "P09";
            Debug.Log($"[MailboxTest] --- {id} ClaimAttachment — second claim idempotent ---");
            try
            {
                var selfId = AuthenticationService.Instance.PlayerId;
                var sendResp = await BackpackCloudCodeService.SendUserMailAsync(
                    selfId, "Idempotent P09", "body", null, MakeCurrencyAttachment(50));
                if (sendResp == null || !sendResp.success)
                {
                    Log(id, false, "Pre-condition failed: could not send mail");
                    return;
                }

                await BackpackCloudCodeService.ClaimAttachmentAsync(sendResp.mailId);
                var secondResp = await BackpackCloudCodeService.ClaimAttachmentAsync(sendResp.mailId);

                // Acceptable outcomes: alreadyClaimed=true (success) or a specific error — not a double grant.
                bool pass = secondResp != null && secondResp.alreadyClaimed;
                Log(id, pass, pass ? null
                    : $"Second claim did not return alreadyClaimed=true. success={secondResp?.success} alreadyClaimed={secondResp?.alreadyClaimed}");
            }
            catch (Exception ex)
            {
                // A server-side error (not double-reward) is also acceptable — log but not a hard fail.
                Debug.LogWarning($"[MailboxTest] {id} — second claim threw (acceptable if server rejects re-claim): {ex.Message}");
            }
        }

        // -----------------------------------------------------------------------
        // Negative tests
        // -----------------------------------------------------------------------

        private async Task Test_N01_SendUserMail_EmptyUserId()
        {
            const string id = "N01";
            Debug.Log($"[MailboxTest] --- {id} SendUserMail empty userId → expect error ---");
            try
            {
                var resp = await BackpackCloudCodeService.SendUserMailAsync(
                    "", "Subject", "Body");
                // If the call succeeds it is a backend validation gap.
                Log(id, false, $"Expected error but got success={resp?.success} mailId={resp?.mailId}");
            }
            catch (Exception ex)
            {
                bool isExpected = IsValidationOrBadRequest(ex);
                Log(id, isExpected, isExpected ? null : $"Unexpected exception type: {ex.GetType().Name} — {ex.Message}");
            }
        }

        private async Task Test_N02_SendGlobalMail_MissingSubject()
        {
            const string id = "N02";
            Debug.Log($"[MailboxTest] --- {id} SendGlobalMail missing subject → expect validation error ---");
            try
            {
                var resp = await BackpackCloudCodeService.SendGlobalMailAsync("", "Body only");
                Log(id, false, $"Expected validation error but got success={resp?.success} mailId={resp?.mailId}");
            }
            catch (Exception ex)
            {
                bool isExpected = IsValidationOrBadRequest(ex);
                Log(id, isExpected, isExpected ? null : $"Unexpected exception: {ex.GetType().Name} — {ex.Message}");
            }
        }

        private async Task Test_N03_ClaimAttachment_InvalidMailId()
        {
            const string id = "N03";
            Debug.Log($"[MailboxTest] --- {id} ClaimAttachment invalid mailId → expect not-found error ---");
            try
            {
                var resp = await BackpackCloudCodeService.ClaimAttachmentAsync("nonexistent-mail-id-000");
                Log(id, false, $"Expected error but got success={resp?.success}");
            }
            catch (Exception ex)
            {
                bool isExpected = IsNotFoundOrBadRequest(ex);
                Log(id, isExpected, isExpected ? null : $"Unexpected exception: {ex.GetType().Name} — {ex.Message}");
            }
        }

        private async Task Test_N04_ClaimAttachment_NoAttachment()
        {
            const string id = "N04";
            Debug.Log($"[MailboxTest] --- {id} ClaimAttachment on mail with no attachment → expect error ---");
            try
            {
                var selfId = AuthenticationService.Instance.PlayerId;
                var sendResp = await BackpackCloudCodeService.SendUserMailAsync(
                    selfId, "No attachment N04", "notification only");
                if (sendResp == null || !sendResp.success)
                {
                    Log(id, false, "Pre-condition failed: could not send mail");
                    return;
                }

                var claimResp = await BackpackCloudCodeService.ClaimAttachmentAsync(sendResp.mailId);
                Log(id, false, $"Expected error but call succeeded: success={claimResp?.success}");
            }
            catch (Exception ex)
            {
                bool isExpected = IsValidationOrBadRequest(ex);
                Log(id, isExpected, isExpected ? null : $"Unexpected exception: {ex.GetType().Name} — {ex.Message}");
            }
        }

        private async Task Test_N05_ClaimAttachment_ExpiredMail()
        {
            const string id = "N05";
            Debug.Log($"[MailboxTest] --- {id} ClaimAttachment on expired mail → expect error ---");
            try
            {
                var expiredSend = await BackpackCloudCodeService.SendGlobalMailAsync(
                    "Expired N05", "body", PastExpiry(), MakeCurrencyAttachment(10));
                if (expiredSend == null || !expiredSend.success)
                {
                    Debug.LogWarning($"[MailboxTest] {id} — could not create expired mail; skipping");
                    return;
                }

                var claimResp = await BackpackCloudCodeService.ClaimAttachmentAsync(expiredSend.mailId);
                Log(id, false, $"Expected error but got success={claimResp?.success}");
            }
            catch (Exception ex)
            {
                bool isExpected = IsNotFoundOrBadRequest(ex);
                Log(id, isExpected, isExpected ? null : $"Unexpected exception: {ex.GetType().Name} — {ex.Message}");
            }
        }

        private async Task Test_N06_GetMailbox_NoMails_ReturnsEmptyList()
        {
            const string id = "N06";
            Debug.Log($"[MailboxTest] --- {id} GetMailbox no mails → empty list, not error ---");
            try
            {
                // This test is best-effort; other tests may have seeded the mailbox already.
                var resp = await BackpackCloudCodeService.GetMailboxAsync();
                bool pass = resp != null && resp.success && resp.mails != null;
                Log(id, pass, pass ? null : $"success={resp?.success} mails={resp?.mails}");
            }
            catch (Exception ex) { LogException(id, ex); }
        }

        private async Task Test_N07_MarkMailRead_InvalidMailId()
        {
            const string id = "N07";
            Debug.Log($"[MailboxTest] --- {id} MarkMailRead invalid mailId → graceful handling ---");
            try
            {
                var resp = await BackpackCloudCodeService.MarkMailReadAsync("nonexistent-id-999");
                // Either a false-success with isRead=false OR an exception — both are acceptable.
                bool pass = resp == null || !resp.success || !resp.isRead;
                Log(id, pass, pass ? null : $"Unexpected success: isRead={resp?.isRead}");
            }
            catch (Exception ex)
            {
                // A 404/bad-request is the expected graceful path.
                bool isExpected = IsNotFoundOrBadRequest(ex);
                Log(id, isExpected, isExpected ? null : $"Unexpected exception: {ex.GetType().Name} — {ex.Message}");
            }
        }

        // -----------------------------------------------------------------------
        // Edge case tests
        // -----------------------------------------------------------------------

        private async Task Test_E01_SendGlobalMail_VeryLongBody()
        {
            const string id = "E01";
            Debug.Log($"[MailboxTest] --- {id} SendGlobalMail 1000-char body ---");
            try
            {
                string longBody = new string('A', 1000);
                var resp = await BackpackCloudCodeService.SendGlobalMailAsync(
                    "Long body test E01", longBody);
                bool pass = resp != null && resp.success && !string.IsNullOrEmpty(resp.mailId);
                Log(id, pass, pass ? null : $"success={resp?.success}");
            }
            catch (Exception ex) { LogException(id, ex); }
        }

        private async Task Test_E02_SendGlobalMail_MultipleAttachments()
        {
            const string id = "E02";
            Debug.Log($"[MailboxTest] --- {id} SendGlobalMail with 3 attachments ---");
            try
            {
                var attachments = new List<MailAttachment>
                {
                    new MailAttachment { type = "currency", id = "gold",     amount = 100 },
                    new MailAttachment { type = "currency", id = "gems",     amount = 10  },
                    new MailAttachment { type = "item",     id = "potion",   amount = 5   }
                };
                var resp = await BackpackCloudCodeService.SendGlobalMailAsync(
                    "Multi-reward E02", "You get everything!", null, attachments);
                bool pass = resp != null && resp.success && !string.IsNullOrEmpty(resp.mailId);
                Log(id, pass, pass ? null : $"success={resp?.success}");
            }
            catch (Exception ex) { LogException(id, ex); }
        }

        private async Task Test_E03_ClaimAttachment_RaceCondition()
        {
            const string id = "E03";
            Debug.Log($"[MailboxTest] --- {id} ClaimAttachment double-fire concurrency ---");
            try
            {
                var selfId = AuthenticationService.Instance.PlayerId;
                var sendResp = await BackpackCloudCodeService.SendUserMailAsync(
                    selfId, "Race E03", "body", null, MakeCurrencyAttachment(999));
                if (sendResp == null || !sendResp.success)
                {
                    Log(id, false, "Pre-condition failed: could not send mail");
                    return;
                }

                // Fire both claims simultaneously.
                var t1 = BackpackCloudCodeService.ClaimAttachmentAsync(sendResp.mailId);
                var t2 = BackpackCloudCodeService.ClaimAttachmentAsync(sendResp.mailId);

                ClaimAttachmentResponse r1 = null, r2 = null;
                Exception ex1 = null, ex2 = null;
                try { r1 = await t1; } catch (Exception e) { ex1 = e; }
                try { r2 = await t2; } catch (Exception e) { ex2 = e; }

                int successCount = (r1 != null && r1.success && !r1.alreadyClaimed ? 1 : 0)
                                 + (r2 != null && r2.success && !r2.alreadyClaimed ? 1 : 0);

                // Exactly one claim must succeed; the other must be alreadyClaimed or error.
                bool pass = successCount == 1;
                Log(id, pass, pass ? null
                    : $"Double-claim risk: successCount={successCount} r1.alreadyClaimed={r1?.alreadyClaimed} r2.alreadyClaimed={r2?.alreadyClaimed} ex1={ex1?.Message} ex2={ex2?.Message}");
            }
            catch (Exception ex) { LogException(id, ex); }
        }

        private async Task Test_E04_GetMailbox_ManyMails()
        {
            const string id = "E04";
            Debug.Log($"[MailboxTest] --- {id} GetMailbox with 10+ mails ---");
            try
            {
                // Seed 10 mails.
                for (int i = 0; i < 10; i++)
                    await BackpackCloudCodeService.SendGlobalMailAsync($"Bulk mail E04-{i}", "body");

                var resp = await BackpackCloudCodeService.GetMailboxAsync();
                bool pass = resp != null && resp.success && (resp.mails?.Count ?? 0) >= 10;
                Log(id, pass, pass ? null : $"success={resp?.success} count={resp?.mails?.Count}");
            }
            catch (Exception ex) { LogException(id, ex); }
        }

        private async Task Test_E05_Mail_AtExpiryBoundary()
        {
            const string id = "E05";
            Debug.Log($"[MailboxTest] --- {id} Mail expiry boundary (expires in 2 seconds) ---");
            try
            {
                string nearExpiry = DateTime.UtcNow.AddSeconds(2).ToString("o");
                var selfId = AuthenticationService.Instance.PlayerId;
                var sendResp = await BackpackCloudCodeService.SendUserMailAsync(
                    selfId, "Boundary E05", "body", nearExpiry, MakeCurrencyAttachment(1));
                if (sendResp == null || !sendResp.success)
                {
                    Log(id, false, "Pre-condition failed: could not send mail");
                    return;
                }

                // Claim immediately — should succeed before expiry.
                var claimBeforeResp = await BackpackCloudCodeService.ClaimAttachmentAsync(sendResp.mailId);
                bool pass = claimBeforeResp != null && claimBeforeResp.success;
                Log(id, pass, pass ? null : $"Claim before expiry failed: success={claimBeforeResp?.success}");

                if (!pass)
                    Debug.LogWarning($"[MailboxTest] {id} — RISK: server-side expiry check may use a different clock resolution than the client.");
            }
            catch (Exception ex) { LogException(id, ex); }
        }

        private async Task Test_E06_SendUserMail_ToSelf()
        {
            const string id = "E06";
            Debug.Log($"[MailboxTest] --- {id} SendUserMail to self ---");
            try
            {
                var selfId = AuthenticationService.Instance.PlayerId;
                var sendResp = await BackpackCloudCodeService.SendUserMailAsync(
                    selfId, "Self-mail E06", "You sent this to yourself.", null, MakeCurrencyAttachment(25));
                bool sent = sendResp != null && sendResp.success && !string.IsNullOrEmpty(sendResp.mailId);
                if (!sent)
                {
                    Log(id, false, $"Send failed: success={sendResp?.success}");
                    return;
                }

                var mailboxResp = await BackpackCloudCodeService.GetMailboxAsync();
                bool receivedSelf = mailboxResp?.mails?.Any(m => m.mailId == sendResp.mailId) ?? false;
                bool pass = receivedSelf;
                Log(id, pass, pass ? null : $"Self-mail not found in mailbox. mailId={sendResp.mailId}");
            }
            catch (Exception ex) { LogException(id, ex); }
        }

        // -----------------------------------------------------------------------
        // Logging helpers
        // -----------------------------------------------------------------------

        private static void Log(string testId, bool pass, string failReason)
        {
            if (pass)
                Debug.Log($"[MailboxTest] PASS — {testId}");
            else
                Debug.LogError($"[MailboxTest] FAIL — {testId}: {failReason}");
        }

        private static void LogException(string testId, Exception ex) =>
            Debug.LogError($"[MailboxTest] FAIL — {testId} threw: {ex.Message}");

        private static bool IsValidationOrBadRequest(Exception ex)
        {
            string msg = ex.Message ?? "";
            return msg.Contains("400") || msg.Contains("validation") || msg.Contains("invalid")
                || msg.Contains("bad request") || msg.Contains("required");
        }

        private static bool IsNotFoundOrBadRequest(Exception ex)
        {
            string msg = ex.Message ?? "";
            return msg.Contains("404") || msg.Contains("not found") || msg.Contains("400")
                || msg.Contains("invalid") || msg.Contains("expired");
        }
    }
}
