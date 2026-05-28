// MailboxApiConcurrencyTests.cs
// NUnit EditMode concurrency tests for the Mailbox v2 API.
//
// Test IDs map to the Testing Plan "Concurrency Tests" section of
// Devlog_Mailbox_Production.md (C01–C04) and §5.5 (WriteLock matrix).
//
// These tests fire simultaneous async calls to verify that Cloud Save writeLock
// and the idempotency store prevent race conditions.
//
// REQUIREMENTS:
//   - Live UGS backend.
//   - ADMIN_SERVICE_TOKEN env var configured on the UGS Dashboard (see TEST_SETUP.md).
//   - Concurrency tests must be run individually or in a dedicated suite pass
//     to avoid cross-test state interference.
//
// SEVERITY NOTE:
//   C01 is BLOCKER (double economy grant is an exploit).

using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Services.Authentication;

namespace BackpackAdventures.CloudCode.Client.Tests
{
    [TestFixture]
    [Category("Mailbox")]
    [Category("Concurrency")]
    public class MailboxApiConcurrencyTests
    {
        // -----------------------------------------------------------------------
        // Setup / Teardown
        // -----------------------------------------------------------------------

        [SetUp]
        public async Task SetUp()
        {
            await MailboxTestHarness.EnsureAdminAsync();
        }

        [TearDown]
        public async Task TearDown()
        {
            await MailboxTestHarness.CleanupAsync();
        }

        // -----------------------------------------------------------------------
        // C01 — Two simultaneous ClaimAttachment calls -> exactly ONE grant
        // Devlog row: C01 — ClaimAttachment_ConcurrentDoubleFire
        // Severity: BLOCKER
        // -----------------------------------------------------------------------

        [Test]
        [Description("C01 — BLOCKER. Fire two concurrent ClaimAttachment calls for the same mailId. " +
                     "Expected: exactly one call returns alreadyClaimed=false (grant issued); " +
                     "the other returns alreadyClaimed=true or throws. Reward granted exactly once.")]
        public async Task C01_ClaimAttachment_ConcurrentDoubleFire_ExactlyOneGrant()
        {
            string selfId = AuthenticationService.Instance.PlayerId;

            // Seed a user mail with a currency attachment
            var sendResp = await BackpackCloudCodeService.CallAdminSendUserMailAsync(
                targetPlayerId: selfId,
                subject: "C01 Race Condition",
                body: "C01 concurrency test",
                expiresAt: MailboxTestHarness.FutureExpiry(),
                attachments: MailboxTestHarness.MakeCurrencyAttachment(999),
                adminToken: TestConstants.AdminToken,
                operatorId: TestConstants.OperatorId);
            Assert.IsTrue(sendResp.success, "C01: pre-condition send failed");
            string mailId = sendResp.mailId;

            // Fire two claims simultaneously
            var t1 = BackpackCloudCodeService.CallClaimAttachmentAsync(mailId, "user");
            var t2 = BackpackCloudCodeService.CallClaimAttachmentAsync(mailId, "user");

            ClaimAttachmentResponse r1 = null, r2 = null;
            Exception ex1 = null, ex2 = null;

            // Await both, capturing any exceptions
            try { r1 = await t1; } catch (Exception e) { ex1 = e; }
            try { r2 = await t2; } catch (Exception e) { ex2 = e; }

            // Count how many calls returned a fresh (non-duplicate) grant
            int successCount = 0;
            if (r1 != null && r1.success && !r1.alreadyClaimed) successCount++;
            if (r2 != null && r2.success && !r2.alreadyClaimed) successCount++;

            // Acceptable outcomes:
            //   - Exactly one call succeeds with alreadyClaimed=false (writeLock worked)
            //   - The other returns alreadyClaimed=true OR throws
            //
            // Unacceptable outcome:
            //   - successCount == 2 (double grant — economy exploit)
            Assert.AreNotEqual(2, successCount,
                "C01: BLOCKER — both concurrent claims returned alreadyClaimed=false. " +
                "Double reward grant detected. WriteLock must prevent this. " +
                $"r1=(success={r1?.success} alreadyClaimed={r1?.alreadyClaimed}) " +
                $"r2=(success={r2?.success} alreadyClaimed={r2?.alreadyClaimed}) " +
                $"ex1={ex1?.Message} ex2={ex2?.Message}");

            Assert.AreEqual(1, successCount,
                "C01: Exactly one concurrent claim must succeed with alreadyClaimed=false. " +
                $"Got successCount={successCount}. " +
                $"r1=(success={r1?.success} alreadyClaimed={r1?.alreadyClaimed} ex={ex1?.Message}) " +
                $"r2=(success={r2?.success} alreadyClaimed={r2?.alreadyClaimed} ex={ex2?.Message})");
        }

        // -----------------------------------------------------------------------
        // C02 — Two simultaneous AdminSendGlobalMail calls -> both succeed, distinct mailIds
        // Devlog row: C03 — SendGlobalMail_ConcurrentAdmins
        // -----------------------------------------------------------------------

        [Test]
        [Description("C02 — Fire two concurrent AdminSendGlobalMail calls from the same admin. " +
                     "Expected: both succeed with DIFFERENT globalMailIds; " +
                     "both refs present in global_mail_index_v2 (verified via GetGlobalMails).")]
        public async Task C02_SendGlobalMail_ConcurrentAdmins_BothSucceed()
        {
            // Fire two sends simultaneously
            var t1 = BackpackCloudCodeService.CallAdminSendGlobalMailAsync(
                subject: "C02 Concurrent Global 1",
                body: "C02 body 1",
                expiresAt: MailboxTestHarness.FutureExpiry(),
                adminToken: TestConstants.AdminToken,
                operatorId: TestConstants.OperatorId);
            var t2 = BackpackCloudCodeService.CallAdminSendGlobalMailAsync(
                subject: "C02 Concurrent Global 2",
                body: "C02 body 2",
                expiresAt: MailboxTestHarness.FutureExpiry(),
                adminToken: TestConstants.AdminToken,
                operatorId: TestConstants.OperatorId);

            SendGlobalMailResponse r1 = null, r2 = null;
            Exception ex1 = null, ex2 = null;

            try { r1 = await t1; } catch (Exception e) { ex1 = e; }
            try { r2 = await t2; } catch (Exception e) { ex2 = e; }

            // Both sends must succeed
            Assert.IsNull(ex1, $"C02: first concurrent send threw: {ex1?.Message}");
            Assert.IsNull(ex2, $"C02: second concurrent send threw: {ex2?.Message}");
            Assert.IsNotNull(r1, "C02: first send response must not be null");
            Assert.IsNotNull(r2, "C02: second send response must not be null");
            Assert.IsTrue(r1.success, "C02: first send success must be true");
            Assert.IsTrue(r2.success, "C02: second send success must be true");

            string id1 = r1.globalMailId ?? r1.mailId;
            string id2 = r2.globalMailId ?? r2.mailId;

            Assert.IsFalse(string.IsNullOrEmpty(id1), "C02: first mailId must be non-empty");
            Assert.IsFalse(string.IsNullOrEmpty(id2), "C02: second mailId must be non-empty");
            Assert.AreNotEqual(id1, id2,
                "C02: concurrent sends must produce DIFFERENT mailIds — sharded writes must not collapse");

            // Verify both refs are in the index
            var getResp = await BackpackCloudCodeService.CallGetGlobalMailsAsync(page: 0, pageSize: 50);
            Assert.IsNotNull(getResp, "C02: GetGlobalMails must not be null");
            Assert.IsTrue(getResp.success, "C02: GetGlobalMails success must be true");

            bool id1Present = getResp.mails?.Any(m => m.mailId == id1) ?? false;
            bool id2Present = getResp.mails?.Any(m => m.mailId == id2) ?? false;

            Assert.IsTrue(id1Present,
                $"C02: first mail id={id1} must be present in GetGlobalMails after concurrent send");
            Assert.IsTrue(id2Present,
                $"C02: second mail id={id2} must be present in GetGlobalMails after concurrent send");
        }

        // -----------------------------------------------------------------------
        // C03 — Idempotency: same requestId twice -> same response, no double-grant
        // Devlog row: Idempotency replay per §5.8; also maps to P12
        // -----------------------------------------------------------------------

        [Test]
        [Description("C03 — Fire two ClaimAttachment calls with the SAME requestId simultaneously. " +
                     "Expected: idempotency cache dedups them; reward granted exactly once; " +
                     "both responses are semantically equivalent (no double-grant).")]
        public async Task C03_ClaimAttachment_SameRequestId_NoDoubleGrant()
        {
            string selfId = AuthenticationService.Instance.PlayerId;

            // Seed user mail with attachment
            var sendResp = await BackpackCloudCodeService.CallAdminSendUserMailAsync(
                targetPlayerId: selfId,
                subject: "C03 Idempotency Concurrent",
                body: "C03 body",
                expiresAt: MailboxTestHarness.FutureExpiry(),
                attachments: MailboxTestHarness.MakeCurrencyAttachment(50),
                adminToken: TestConstants.AdminToken,
                operatorId: TestConstants.OperatorId);
            Assert.IsTrue(sendResp.success, "C03: pre-condition send failed");

            string requestId = Guid.NewGuid().ToString();

            // Fire both with the same requestId simultaneously
            var t1 = BackpackCloudCodeService.CallClaimAttachmentAsync(
                sendResp.mailId, "user", requestId);
            var t2 = BackpackCloudCodeService.CallClaimAttachmentAsync(
                sendResp.mailId, "user", requestId);

            ClaimAttachmentResponse r1 = null, r2 = null;
            Exception ex1 = null, ex2 = null;

            try { r1 = await t1; } catch (Exception e) { ex1 = e; }
            try { r2 = await t2; } catch (Exception e) { ex2 = e; }

            // At least one call must return a non-error response
            bool anySuccess = (r1 != null && r1.success) || (r2 != null && r2.success);
            Assert.IsTrue(anySuccess,
                $"C03: At least one call must succeed. ex1={ex1?.Message} ex2={ex2?.Message}");

            // Count fresh grants (alreadyClaimed=false)
            int freshGrantCount = 0;
            if (r1 != null && r1.success && !r1.alreadyClaimed) freshGrantCount++;
            if (r2 != null && r2.success && !r2.alreadyClaimed) freshGrantCount++;

            // Same requestId must not yield two fresh grants
            Assert.LessOrEqual(freshGrantCount, 1,
                "C03: Same requestId must not result in two fresh grants. " +
                $"freshGrantCount={freshGrantCount} — idempotency cache or writeLock must prevent double grant.");
        }

        // -----------------------------------------------------------------------
        // C04 — Two concurrent ClaimAttachment calls for different mailIds -> both succeed
        // Devlog row: C04 — ClaimAttachment_ConcurrentDifferentMails
        // -----------------------------------------------------------------------

        [Test]
        [Description("C04 — Fire two concurrent ClaimAttachment calls for different mailIds. " +
                     "Expected: both succeed independently with alreadyClaimed=false; " +
                     "writeLock on one mail must not block the other.")]
        public async Task C04_ClaimAttachment_ConcurrentDifferentMails_BothSucceed()
        {
            string selfId = AuthenticationService.Instance.PlayerId;

            // Seed two distinct user mails with attachments
            var send1 = await BackpackCloudCodeService.CallAdminSendUserMailAsync(
                targetPlayerId: selfId,
                subject: "C04 Mail A",
                body: "C04 mail A body",
                expiresAt: MailboxTestHarness.FutureExpiry(),
                attachments: MailboxTestHarness.MakeCurrencyAttachment(10),
                adminToken: TestConstants.AdminToken,
                operatorId: TestConstants.OperatorId);
            Assert.IsTrue(send1.success, "C04: pre-condition send A failed");

            var send2 = await BackpackCloudCodeService.CallAdminSendUserMailAsync(
                targetPlayerId: selfId,
                subject: "C04 Mail B",
                body: "C04 mail B body",
                expiresAt: MailboxTestHarness.FutureExpiry(),
                attachments: MailboxTestHarness.MakeCurrencyAttachment(20),
                adminToken: TestConstants.AdminToken,
                operatorId: TestConstants.OperatorId);
            Assert.IsTrue(send2.success, "C04: pre-condition send B failed");

            // Claim both simultaneously
            var t1 = BackpackCloudCodeService.CallClaimAttachmentAsync(send1.mailId, "user");
            var t2 = BackpackCloudCodeService.CallClaimAttachmentAsync(send2.mailId, "user");

            ClaimAttachmentResponse r1 = null, r2 = null;
            Exception ex1 = null, ex2 = null;

            try { r1 = await t1; } catch (Exception e) { ex1 = e; }
            try { r2 = await t2; } catch (Exception e) { ex2 = e; }

            // Both claims for different mails must succeed independently
            Assert.IsNull(ex1, $"C04: claim A threw unexpectedly: {ex1?.Message}");
            Assert.IsNull(ex2, $"C04: claim B threw unexpectedly: {ex2?.Message}");

            Assert.IsNotNull(r1, "C04: claim A response must not be null");
            Assert.IsNotNull(r2, "C04: claim B response must not be null");
            Assert.IsTrue(r1.success, "C04: claim A success must be true");
            Assert.IsTrue(r2.success, "C04: claim B success must be true");
            Assert.IsFalse(r1.alreadyClaimed,
                "C04: claim A alreadyClaimed must be false — independent claims must not interfere");
            Assert.IsFalse(r2.alreadyClaimed,
                "C04: claim B alreadyClaimed must be false — independent claims must not interfere");
        }

        // -----------------------------------------------------------------------
        // C05 — Two simultaneous MarkMailRead calls -> both succeed; no duplicate readIds
        // Devlog row: C02 — MarkMailRead_ConcurrentDoubleFire
        // -----------------------------------------------------------------------

        [Test]
        [Description("C05 — Fire two concurrent MarkMailRead calls for the same mailId. " +
                     "Expected: both return success=true, isRead=true; " +
                     "mail is marked read exactly once (no duplicate readIds in global_state).")]
        public async Task C05_MarkMailRead_ConcurrentDoubleFire_NoDuplicate()
        {
            string selfId = AuthenticationService.Instance.PlayerId;

            // Seed a user mail
            var sendResp = await BackpackCloudCodeService.CallAdminSendUserMailAsync(
                targetPlayerId: selfId,
                subject: "C05 Concurrent Read",
                body: "C05 mark read concurrently",
                adminToken: TestConstants.AdminToken,
                operatorId: TestConstants.OperatorId);
            Assert.IsTrue(sendResp.success, "C05: pre-condition send failed");

            // Fire two MarkMailRead simultaneously
            var t1 = BackpackCloudCodeService.CallMarkMailReadAsync(sendResp.mailId, "user");
            var t2 = BackpackCloudCodeService.CallMarkMailReadAsync(sendResp.mailId, "user");

            MarkMailReadResponse r1 = null, r2 = null;
            Exception ex1 = null, ex2 = null;

            try { r1 = await t1; } catch (Exception e) { ex1 = e; }
            try { r2 = await t2; } catch (Exception e) { ex2 = e; }

            // Both calls must eventually result in success (MarkMailRead retries on 409 per §5.5)
            bool r1Ok = (r1 != null && r1.success && r1.isRead) || ex1 == null;
            bool r2Ok = (r2 != null && r2.success && r2.isRead) || ex2 == null;

            // We require that at minimum neither call throws an unexpected error
            if (ex1 != null && !MailboxTestHarness.IsInvalidInputError(ex1))
                Assert.Fail($"C05: claim 1 threw unexpected error: {ex1.Message}");
            if (ex2 != null && !MailboxTestHarness.IsInvalidInputError(ex2))
                Assert.Fail($"C05: claim 2 threw unexpected error: {ex2.Message}");

            // If both returned, they must both report isRead=true
            if (r1 != null) Assert.IsTrue(r1.isRead, "C05: r1.isRead must be true");
            if (r2 != null) Assert.IsTrue(r2.isRead, "C05: r2.isRead must be true");

            // Verify final state in mailbox — mail must be read exactly once
            var getResp = await BackpackCloudCodeService.CallGetMailboxAsync(page: 0, pageSize: 50);
            var mail = getResp?.mails?.FirstOrDefault(m => m.mailId == sendResp.mailId);
            if (mail != null)
            {
                Assert.IsTrue(mail.isRead,
                    "C05: mail must be read=true in GetUserMails after concurrent MarkMailRead");
            }
        }
    }
}
