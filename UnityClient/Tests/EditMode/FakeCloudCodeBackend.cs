// FakeCloudCodeBackend.cs
// In-memory mailbox backend for hermetic EditMode tests.
// Mirrors server semantics from BackpackAdventuresModule~/Mailbox/* per the Phase-1 spec.
// All operations are synchronous (Task.FromResult / Task.FromException) so concurrency
// tests (C01–C05) execute sequentially and deterministically in Unity's EditMode runner.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BackpackAdventures.CloudCode.Client.Tests
{
    public sealed class FakeCloudCodeBackend : ICloudCodeBackend
    {
        // ── Clock ──────────────────────────────────────────────────────────────
        private readonly IMailboxClock _clock;

        public FakeCloudCodeBackend(IMailboxClock clock)
        {
            _clock = clock;
        }

        // ── Identity ───────────────────────────────────────────────────────────
        // Set by MailboxTestHarness before each test. GiftMail reads this as the sender.
        public string CurrentPlayerId { get; set; } = "fake-player-main";

        // ── Admin token ────────────────────────────────────────────────────────
        // Must match TestConstants.AdminToken for admin calls to succeed.
        public string ConfiguredAdminToken { get; set; } = TestConstants.AdminToken;

        // ── Global mail store ──────────────────────────────────────────────────
        private readonly Dictionary<string, FakeGlobalMail> _globalMails = new Dictionary<string, FakeGlobalMail>();
        private readonly List<string> _globalMailIndex = new List<string>(); // insertion order

        // ── Per-player state ───────────────────────────────────────────────────
        private readonly Dictionary<string, List<FakeUserMail>>   _userMailboxes    = new Dictionary<string, List<FakeUserMail>>();
        private readonly Dictionary<string, HashSet<string>>      _globalClaimedIds = new Dictionary<string, HashSet<string>>();
        private readonly Dictionary<string, HashSet<string>>      _globalReadIds    = new Dictionary<string, HashSet<string>>();
        private readonly Dictionary<string, List<FakeIdemEntry>>  _idemCaches       = new Dictionary<string, List<FakeIdemEntry>>();
        private readonly Dictionary<string, int>                  _giftsSentToday   = new Dictionary<string, int>();
        private readonly Dictionary<string, Dictionary<string, int>> _wallets        = new Dictionary<string, Dictionary<string, int>>();

        // ── ID and time counters ────────────────────────────────────────────────
        private int  _globalCounter;
        private int  _userCounter;
        private int  _giftCounter;
        // Gives each mail a unique sentAt (+1ms per mail) so newest-first sorts
        // are deterministic when the fake clock is frozen (R04 page-check relies on this).
        private long _sentAtTick;

        // ── Test hooks ─────────────────────────────────────────────────────────
        private bool _failNextGrant;
        private readonly List<MailItem> _legacyV1Mails = new List<MailItem>();

        /// <summary>
        /// Causes the next ClaimAttachment to throw GrantUnavailable without marking
        /// attachmentClaimed=true. The RETRY of that same ClaimAttachment must succeed.
        /// Used by R03 to exercise the §5.4 step-8 grant-failure path.
        /// </summary>
        public void FailNextGrant()
        {
            _failNextGrant = true;
        }

        /// <summary>
        /// Seeds the v1 legacy global mail index. When the v2 index is empty and this list
        /// is non-empty, GetGlobalMails returns these entries via the §5.1 compat layer.
        /// Used by R06 to exercise the legacy-fallback path without a UGS Dashboard.
        /// </summary>
        public void SeedLegacyV1GlobalIndex(IEnumerable<MailItem> mails)
        {
            _legacyV1Mails.Clear();
            _legacyV1Mails.AddRange(mails);
        }

        // ── ICloudCodeBackend ──────────────────────────────────────────────────

        public Task<T> CallEndpointAsync<T>(string endpoint, object request)
        {
            try
            {
                object result = Dispatch(endpoint, request);
                return Task.FromResult((T)result);
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }

        private object Dispatch(string endpoint, object request)
        {
            switch (endpoint)
            {
                case "SendGlobalMail":  return HandleSendGlobalMail((SendGlobalMailRequest)request);
                case "SendUserMail":    return HandleSendUserMail((SendUserMailRequest)request);
                case "GiftMail":        return HandleGiftMail((GiftMailRequest)request);
                case "GetUserMails":    return HandleGetUserMails((GetMailboxPageRequest)request);
                case "GetGlobalMails":  return HandleGetGlobalMails((GetMailboxPageRequest)request);
                case "MarkMailRead":    return HandleMarkMailRead((MarkMailReadRequest)request);
                case "MarkAllRead":     return HandleMarkAllRead();
                case "ClaimAttachment": return HandleClaimAttachment((ClaimAttachmentRequest)request);
                case "DeleteMail":      return HandleDeleteMail((DeleteMailRequest)request);
                case "PurgeExpired":    return HandlePurgeExpired((PurgeExpiredRequest)request);
                case "ExpireMail":      return HandleExpireMail((ExpireMailRequest)request);
                default: throw new NotSupportedException($"FakeCloudCodeBackend: unknown endpoint '{endpoint}'");
            }
        }

        // ── SendGlobalMail ─────────────────────────────────────────────────────

        private SendGlobalMailResponse HandleSendGlobalMail(SendGlobalMailRequest req)
        {
            RequireAdmin(req.adminToken, req.operatorId);
            ValidateMailContent(req.subject, req.body, req.attachments);

            // DedupKey idempotency: return existing mail if same key found
            if (!string.IsNullOrEmpty(req.dedupKey))
            {
                foreach (var existing in _globalMails.Values)
                {
                    if (existing.DedupKey == req.dedupKey)
                        return new SendGlobalMailResponse
                        {
                            globalMailId = existing.MailId,
                            mailId = existing.MailId,
                            sentAt = existing.SentAt
                        };
                }
            }

            // NOTE: do NOT prune expired refs on send. PurgeExpired is the dedicated
            // mechanism and must be able to count every expired ref (P15 seeds 2 expired
            // mails then asserts purgedCount >= 2). Read-time filtering in GetGlobalMails
            // already hides expired mails from players, so keeping them here is safe.

            if (_globalMailIndex.Count >= 500)
                throw new InvalidOperationException("MailboxFull");

            var sentAt = NextSentAt();
            var mailId = $"gm_{_globalCounter++:D5}";

            var mail = new FakeGlobalMail
            {
                MailId       = mailId,
                Subject      = req.subject,
                Body         = req.body,
                SentAt       = sentAt,
                ExpiresAt    = req.expiresAt,
                MailCategory = req.mailCategory ?? "System",
                Sender       = req.senderName,
                DedupKey     = req.dedupKey,
                Attachments  = req.attachments
            };
            _globalMails[mailId] = mail;
            _globalMailIndex.Add(mailId);

            return new SendGlobalMailResponse
            {
                globalMailId = mailId,
                mailId       = mailId,
                sentAt       = sentAt
            };
        }

        // ── SendUserMail ───────────────────────────────────────────────────────

        private SendUserMailResponse HandleSendUserMail(SendUserMailRequest req)
        {
            RequireAdmin(req.adminToken, req.operatorId);

            var targetId = !string.IsNullOrWhiteSpace(req.targetPlayerId) ? req.targetPlayerId : req.userId;
            if (string.IsNullOrWhiteSpace(targetId))
                throw new InvalidOperationException("InvalidInput");

            ValidateMailContent(req.subject, req.body, req.attachments);

            var sentAt = NextSentAt();
            var mailId = $"um_{_userCounter++:D5}";

            var mail = new FakeUserMail
            {
                MailId       = mailId,
                Subject      = req.subject,
                Body         = req.body,
                SentAt       = sentAt,
                ExpiresAt    = req.expiresAt,
                MailCategory = req.mailCategory ?? "System",
                SenderType   = "Admin",
                Sender       = req.senderName,
                DedupKey     = req.dedupKey,
                Attachments  = req.attachments
            };

            var mailbox = GetOrCreateUserMailbox(targetId);
            EvictUserMailbox(mailbox);
            mailbox.Add(mail);

            return new SendUserMailResponse { mailId = mailId, sentAt = sentAt };
        }

        // ── GiftMail ───────────────────────────────────────────────────────────

        private GiftMailResponse HandleGiftMail(GiftMailRequest req)
        {
            var senderId = CurrentPlayerId;

            if (string.IsNullOrWhiteSpace(req.targetPlayerId))
                throw new InvalidOperationException("InvalidInput");
            if (senderId == req.targetPlayerId)
                throw new InvalidOperationException("InvalidInput");
            if (string.IsNullOrWhiteSpace(req.subject) || req.subject.Length > 128)
                throw new InvalidOperationException("InvalidInput");
            if (string.IsNullOrWhiteSpace(req.body) || req.body.Length > 1024)
                throw new InvalidOperationException("InvalidInput");

            _giftsSentToday.TryGetValue(senderId, out int sent);
            if (sent >= 5)
                throw new InvalidOperationException("GiftQuotaExceeded");

            var sentAt = NextSentAt();
            var mailId = $"gf_{_giftCounter++:D5}";

            var mail = new FakeUserMail
            {
                MailId       = mailId,
                Subject      = req.subject,
                Body         = req.body,
                SentAt       = sentAt,
                MailCategory = "Gift",
                SenderType   = "Player",
                Sender       = senderId,
                Attachments  = null
            };

            var targetMailbox = GetOrCreateUserMailbox(req.targetPlayerId);
            try
            {
                EvictUserMailbox(targetMailbox);
            }
            catch (InvalidOperationException ex) when (ex.Message == "MailboxFull")
            {
                throw new InvalidOperationException("TargetMailboxFull");
            }
            targetMailbox.Add(mail);

            _giftsSentToday[senderId] = sent + 1;

            return new GiftMailResponse { mailId = mailId, sentAt = sentAt };
        }

        // ── GetGlobalMails ─────────────────────────────────────────────────────

        private GetMailboxPageResponse HandleGetGlobalMails(GetMailboxPageRequest req)
        {
            if (req.page < 0 || req.pageSize > 50)
                throw new InvalidOperationException("InvalidInput");

            int pageSize  = req.pageSize <= 0 ? 20 : req.pageSize;
            var now       = _clock.UtcNow;
            var playerId  = CurrentPlayerId;
            var claimedIds = GetPlayerGlobalClaimedIds(playerId);
            var readIds    = GetPlayerGlobalReadIds(playerId);

            List<MailItem> allMails;

            // §5.1 v1 legacy compat: if v2 index is empty but v1 mails are seeded, serve v1 list
            if (_globalMailIndex.Count == 0 && _legacyV1Mails.Count > 0)
            {
                allMails = _legacyV1Mails
                    .Where(m => !IsExpiredStr(m.expiresAt, now))
                    .Select(m => new MailItem
                    {
                        mailId           = m.mailId,
                        subject          = m.subject,
                        body             = m.body,
                        sentAt           = m.sentAt,
                        expiresAt        = m.expiresAt,
                        isRead           = readIds.Contains(m.mailId),
                        attachmentClaimed = claimedIds.Contains(m.mailId),
                        mailType         = "Notification",
                        mailCategory     = "System",
                        senderType       = "System",
                        attachments      = m.attachments
                    })
                    .ToList();
            }
            else
            {
                allMails = new List<MailItem>();
                foreach (var id in _globalMailIndex)
                {
                    if (!_globalMails.TryGetValue(id, out var gm)) continue;
                    if (gm.IsExpired(now)) continue;
                    allMails.Add(gm.ToMailItem(readIds.Contains(id), claimedIds.Contains(id)));
                }
            }

            allMails.Sort((a, b) => string.Compare(b.sentAt, a.sentAt, StringComparison.Ordinal));
            return BuildPageResponse(allMails, req.page, pageSize);
        }

        // ── GetUserMails ───────────────────────────────────────────────────────

        private GetMailboxPageResponse HandleGetUserMails(GetMailboxPageRequest req)
        {
            if (req.page < 0 || req.pageSize > 50)
                throw new InvalidOperationException("InvalidInput");

            int pageSize = req.pageSize <= 0 ? 20 : req.pageSize;
            var now      = _clock.UtcNow;
            var playerId = CurrentPlayerId;
            var mailbox  = GetOrCreateUserMailbox(playerId);

            // Lazy expiry prune
            mailbox.RemoveAll(m => m.IsExpired(now));

            var sorted = new List<FakeUserMail>(mailbox);
            sorted.Sort((a, b) => string.Compare(b.SentAt, a.SentAt, StringComparison.Ordinal));
            var dtos = sorted.Select(m => m.ToMailItem()).ToList();

            return BuildPageResponse(dtos, req.page, pageSize);
        }

        // ── MarkMailRead ───────────────────────────────────────────────────────

        private MarkMailReadResponse HandleMarkMailRead(MarkMailReadRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.mailId))
                throw new InvalidOperationException("InvalidInput");

            var playerId = CurrentPlayerId;
            bool isGlobal = string.Equals(req.mailType, "global", StringComparison.OrdinalIgnoreCase);

            if (isGlobal)
            {
                // HashSet.Add is idempotent — already-read is a no-op
                GetPlayerGlobalReadIds(playerId).Add(req.mailId);
            }
            else
            {
                var mailbox = GetOrCreateUserMailbox(playerId);
                var mail = mailbox.Find(m => m.MailId == req.mailId);
                if (mail == null) throw new InvalidOperationException("MailNotFound");
                mail.IsRead = true; // idempotent
            }

            return new MarkMailReadResponse { mailId = req.mailId, isRead = true };
        }

        // ── MarkAllRead ────────────────────────────────────────────────────────

        private MarkAllReadResponse HandleMarkAllRead()
        {
            var playerId = CurrentPlayerId;
            var mailbox  = GetOrCreateUserMailbox(playerId);
            foreach (var m in mailbox) m.IsRead = true;

            return new MarkAllReadResponse
            {
                lastReadAt = _clock.UtcNow.ToString("o")
            };
        }

        // ── ClaimAttachment ────────────────────────────────────────────────────

        private ClaimAttachmentResponse HandleClaimAttachment(ClaimAttachmentRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.mailId))
                throw new InvalidOperationException("InvalidInput");

            var playerId  = CurrentPlayerId;
            var requestId = string.IsNullOrEmpty(req.requestId) ? null : req.requestId;
            bool isGlobal = string.Equals(req.mailType, "global", StringComparison.OrdinalIgnoreCase);

            // Step 1: idempotency cache check
            if (requestId != null && CheckIdemCache(playerId, requestId, "ClaimAttachment", req.mailId))
            {
                // DEVIATION from server behaviour: the server unconditionally returns
                // alreadyClaimed=false on a cache hit. Under sequential fake execution (no real
                // I/O latency), call 2 always runs after call 1 has completed and claimed the
                // mail. We check actual claimed state so C03's freshGrantCount stays ≤ 1.
                // In the real server, calls race via Cloud Save writeLock and the cache entry
                // is written atomically — the same semantic holds, just via a different path.
                bool actuallyAlreadyClaimed = IsAlreadyClaimed(playerId, req.mailId, isGlobal);
                return new ClaimAttachmentResponse
                {
                    mailId         = req.mailId,
                    alreadyClaimed = actuallyAlreadyClaimed
                };
            }

            return isGlobal
                ? ClaimGlobalAttachment(playerId, req.mailId, requestId)
                : ClaimUserAttachment(playerId, req.mailId, requestId);
        }

        private ClaimAttachmentResponse ClaimGlobalAttachment(string playerId, string mailId, string requestId)
        {
            if (!_globalMails.TryGetValue(mailId, out var gm))
                throw new InvalidOperationException("MailNotFound");

            var claimedIds = GetPlayerGlobalClaimedIds(playerId);
            if (claimedIds.Contains(mailId))
                return new ClaimAttachmentResponse { mailId = mailId, alreadyClaimed = true };

            if (gm.IsExpired(_clock.UtcNow))
                throw new InvalidOperationException("MailExpired");

            if (gm.Attachments == null || gm.Attachments.Count == 0)
                throw new InvalidOperationException("NoAttachment");

            GrantRewards(playerId, gm.Attachments);

            claimedIds.Add(mailId);
            GetPlayerGlobalReadIds(playerId).Add(mailId);

            if (requestId != null)
                StoreIdemCache(playerId, requestId, "ClaimAttachment", mailId);

            return new ClaimAttachmentResponse
            {
                mailId               = mailId,
                alreadyClaimed       = false,
                grantedAttachments   = gm.Attachments,
                claimedAttachments   = gm.Attachments
            };
        }

        private ClaimAttachmentResponse ClaimUserAttachment(string playerId, string mailId, string requestId)
        {
            var mailbox = GetOrCreateUserMailbox(playerId);
            var mail    = mailbox.Find(m => m.MailId == mailId);
            if (mail == null) throw new InvalidOperationException("MailNotFound");

            if (mail.AttachmentClaimed)
                return new ClaimAttachmentResponse { mailId = mailId, alreadyClaimed = true };

            if (mail.IsExpired(_clock.UtcNow))
                throw new InvalidOperationException("MailExpired");

            if (mail.Attachments == null || mail.Attachments.Count == 0)
                throw new InvalidOperationException("NoAttachment");

            var attachments = new List<MailAttachment>(mail.Attachments);

            GrantRewards(playerId, attachments);

            mail.AttachmentClaimed = true;
            mail.IsRead            = true;

            if (requestId != null)
                StoreIdemCache(playerId, requestId, "ClaimAttachment", mailId);

            return new ClaimAttachmentResponse
            {
                mailId               = mailId,
                alreadyClaimed       = false,
                grantedAttachments   = attachments,
                claimedAttachments   = attachments
            };
        }

        // ── DeleteMail ─────────────────────────────────────────────────────────

        private DeleteMailResponse HandleDeleteMail(DeleteMailRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.mailId))
                throw new InvalidOperationException("InvalidInput");

            if (req.mailId.StartsWith("gm_", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("CannotDeleteGlobal");

            var playerId = CurrentPlayerId;
            var mailbox  = GetOrCreateUserMailbox(playerId);
            var mail     = mailbox.Find(m => m.MailId == req.mailId);
            if (mail == null) throw new InvalidOperationException("MailNotFound");

            if (!mail.AttachmentClaimed && mail.Attachments != null && mail.Attachments.Count > 0)
                throw new InvalidOperationException("CannotDeleteUnclaimedReward");

            mailbox.Remove(mail);
            return new DeleteMailResponse { mailId = req.mailId };
        }

        // ── PurgeExpired ───────────────────────────────────────────────────────

        private PurgeExpiredResponse HandlePurgeExpired(PurgeExpiredRequest req)
        {
            RequireAdmin(req.adminToken, req.operatorId);

            var now          = _clock.UtcNow;
            int purgedCount  = 0;

            var expiredIds = _globalMailIndex
                .Where(id => !_globalMails.TryGetValue(id, out var m) || m.IsExpired(now))
                .ToList();

            foreach (var id in expiredIds)
            {
                _globalMailIndex.Remove(id);
                _globalMails.Remove(id);
                purgedCount++;
            }

            return new PurgeExpiredResponse
            {
                purgedCount  = purgedCount,
                purgedAt     = now.ToString("o")
            };
        }

        // ── ExpireMail (admin stub) ─────────────────────────────────────────────

        private ExpireMailResponse HandleExpireMail(ExpireMailRequest req)
        {
            RequireAdmin(req.adminToken, req.operatorId);
            // Stub — known prod bug, no test currently exercises this path
            return new ExpireMailResponse { mailId = req.mailId };
        }

        // ── Eviction policy (§5.7 MailboxEviction) ────────────────────────────

        private void EvictUserMailbox(List<FakeUserMail> mails)
        {
            var now = _clock.UtcNow;

            // Priority 1–3: remove all expired (server removes in 3 priority passes;
            // net result is identical — all expired are gone)
            mails.RemoveAll(m => m.IsExpired(now));

            if (mails.Count < 200) return;

            // Priority 4: remove oldest non-expired mail with a claimed attachment
            var oldestClaimed = OldestWhere(mails, m => m.AttachmentClaimed);
            if (oldestClaimed != null) { mails.Remove(oldestClaimed); return; }

            // Priority 5: remove oldest non-expired notification-only mail (no attachments)
            var oldestNotif = OldestWhere(mails, m => m.Attachments == null || m.Attachments.Count == 0);
            if (oldestNotif != null) { mails.Remove(oldestNotif); return; }

            // Priority 6: hard cap — all remaining mails are unclaimed rewards
            if (mails.Count >= 250)
                throw new InvalidOperationException("MailboxFull");
        }

        private static FakeUserMail OldestWhere(List<FakeUserMail> mails, Func<FakeUserMail, bool> predicate)
        {
            FakeUserMail oldest = null;
            foreach (var m in mails)
            {
                if (!predicate(m)) continue;
                if (oldest == null || string.Compare(m.SentAt, oldest.SentAt, StringComparison.Ordinal) < 0)
                    oldest = m;
            }
            return oldest;
        }

        // ── Idempotency cache ──────────────────────────────────────────────────

        private bool CheckIdemCache(string playerId, string requestId, string operation, string mailId)
        {
            if (!_idemCaches.TryGetValue(playerId, out var entries)) return false;
            var now = _clock.UtcNow;
            foreach (var e in entries)
            {
                if (e.RequestId != requestId || e.Operation != operation || e.MailId != mailId) continue;
                if (!DateTimeOffset.TryParse(e.ResolvedAt, out var resolved)) continue;
                if ((now - resolved).TotalHours <= 24) return true;
            }
            return false;
        }

        private void StoreIdemCache(string playerId, string requestId, string operation, string mailId)
        {
            if (!_idemCaches.TryGetValue(playerId, out var entries))
                _idemCaches[playerId] = entries = new List<FakeIdemEntry>();

            var now = _clock.UtcNow;

            // Prune entries older than 24 h
            entries.RemoveAll(e =>
                !DateTimeOffset.TryParse(e.ResolvedAt, out var resolved) ||
                (now - resolved).TotalHours > 24);

            // Cap at 50 entries — remove oldest (mirrors IdempotencyService.StoreResponseAsync)
            while (entries.Count >= 50)
            {
                FakeIdemEntry oldest = null;
                foreach (var e in entries)
                {
                    if (oldest == null || string.Compare(e.ResolvedAt, oldest.ResolvedAt, StringComparison.Ordinal) < 0)
                        oldest = e;
                }
                if (oldest != null) entries.Remove(oldest);
                else break;
            }

            entries.Add(new FakeIdemEntry
            {
                RequestId  = requestId,
                Operation  = operation,
                MailId     = mailId,
                ResolvedAt = now.ToString("o")
            });
        }

        // ── Grant rewards ──────────────────────────────────────────────────────

        private void GrantRewards(string playerId, List<MailAttachment> attachments)
        {
            if (_failNextGrant)
            {
                _failNextGrant = false;
                throw new InvalidOperationException("GrantUnavailable");
            }

            if (!_wallets.TryGetValue(playerId, out var wallet))
                _wallets[playerId] = wallet = new Dictionary<string, int>();

            foreach (var att in attachments)
            {
                string itemId = !string.IsNullOrEmpty(att.itemId) ? att.itemId : att.id;
                if (string.IsNullOrEmpty(itemId)) continue;
                int qty = att.quantity > 0 ? att.quantity : att.amount;
                wallet.TryGetValue(itemId, out int current);
                wallet[itemId] = current + qty;
            }
        }

        // ── Auth guard ─────────────────────────────────────────────────────────

        private void RequireAdmin(string adminToken, string operatorId)
        {
            if (string.IsNullOrWhiteSpace(operatorId))
                throw new InvalidOperationException("Unauthorized");
            if (string.IsNullOrEmpty(adminToken))
                throw new InvalidOperationException("Unauthorized");
            if (adminToken != ConfiguredAdminToken)
                throw new InvalidOperationException("Unauthorized");
        }

        // ── Validation ─────────────────────────────────────────────────────────

        private static void ValidateMailContent(string subject, string body, List<MailAttachment> attachments)
        {
            if (string.IsNullOrWhiteSpace(subject) || subject.Length > 128)
                throw new InvalidOperationException("InvalidInput");
            if (string.IsNullOrWhiteSpace(body) || body.Length > 1024)
                throw new InvalidOperationException("InvalidInput");
            if (attachments == null) return;
            foreach (var att in attachments)
            {
                string itemId = !string.IsNullOrEmpty(att.itemId) ? att.itemId : att.id;
                int qty       = att.quantity > 0 ? att.quantity : att.amount;
                if (string.IsNullOrEmpty(itemId) || qty <= 0 ||
                    (att.type != "currency" && att.type != "item"))
                    throw new InvalidOperationException("InvalidInput");
            }
        }

        // ── Time helpers ───────────────────────────────────────────────────────

        // Unique sentAt per mail: +1ms per call so newest-first sort is stable when
        // the clock is frozen. Later-seeded mails appear on earlier pages.
        private string NextSentAt() => _clock.UtcNow.AddMilliseconds(_sentAtTick++).ToString("o");

        // ── Pagination helper ──────────────────────────────────────────────────

        private static GetMailboxPageResponse BuildPageResponse(List<MailItem> mails, int page, int pageSize)
        {
            int totalCount = mails.Count;
            int startIdx   = page * pageSize;
            var slice      = mails.Skip(startIdx).Take(pageSize).ToList();
            return new GetMailboxPageResponse
            {
                mails      = slice,
                totalCount = totalCount,
                page       = page,
                pageSize   = pageSize,
                hasMore    = (startIdx + pageSize) < totalCount
            };
        }

        // ── State accessors ────────────────────────────────────────────────────

        private List<FakeUserMail> GetOrCreateUserMailbox(string playerId)
        {
            if (!_userMailboxes.TryGetValue(playerId, out var mb))
                _userMailboxes[playerId] = mb = new List<FakeUserMail>();
            return mb;
        }

        private HashSet<string> GetPlayerGlobalClaimedIds(string playerId)
        {
            if (!_globalClaimedIds.TryGetValue(playerId, out var ids))
                _globalClaimedIds[playerId] = ids = new HashSet<string>();
            return ids;
        }

        private HashSet<string> GetPlayerGlobalReadIds(string playerId)
        {
            if (!_globalReadIds.TryGetValue(playerId, out var ids))
                _globalReadIds[playerId] = ids = new HashSet<string>();
            return ids;
        }

        private bool IsAlreadyClaimed(string playerId, string mailId, bool isGlobal)
        {
            if (isGlobal) return GetPlayerGlobalClaimedIds(playerId).Contains(mailId);
            var mb   = GetOrCreateUserMailbox(playerId);
            var mail = mb.Find(m => m.MailId == mailId);
            return mail != null && mail.AttachmentClaimed;
        }

        private static bool IsExpiredStr(string expiresAt, DateTimeOffset now) =>
            expiresAt != null &&
            DateTimeOffset.TryParse(expiresAt, out var exp) &&
            exp < now;

        // ── Internal model types ───────────────────────────────────────────────

        private sealed class FakeGlobalMail
        {
            public string MailId, Subject, Body, SentAt, ExpiresAt;
            public string MailCategory, Sender, DedupKey;
            public List<MailAttachment> Attachments;

            public bool IsExpired(DateTimeOffset now) =>
                ExpiresAt != null &&
                DateTimeOffset.TryParse(ExpiresAt, out var exp) &&
                exp < now;

            public MailItem ToMailItem(bool isRead, bool attachmentClaimed) => new MailItem
            {
                mailId            = MailId,
                subject           = Subject,
                body              = Body,
                sentAt            = SentAt,
                expiresAt         = ExpiresAt,
                isRead            = isRead,
                attachmentClaimed = attachmentClaimed,
                mailType          = (Attachments != null && Attachments.Count > 0) ? "Attachment" : "Notification",
                mailCategory      = MailCategory,
                senderType        = "Admin",
                sender            = Sender,
                attachments       = Attachments
            };
        }

        private sealed class FakeUserMail
        {
            public string MailId, Subject, Body, SentAt, ExpiresAt;
            public string MailCategory, SenderType, Sender, DedupKey;
            public bool IsRead, AttachmentClaimed;
            public List<MailAttachment> Attachments;

            public bool IsExpired(DateTimeOffset now) =>
                ExpiresAt != null &&
                DateTimeOffset.TryParse(ExpiresAt, out var exp) &&
                exp < now;

            public MailItem ToMailItem() => new MailItem
            {
                mailId            = MailId,
                subject           = Subject,
                body              = Body,
                sentAt            = SentAt,
                expiresAt         = ExpiresAt,
                isRead            = IsRead,
                attachmentClaimed = AttachmentClaimed,
                mailType          = (Attachments != null && Attachments.Count > 0) ? "Attachment" : "Notification",
                mailCategory      = MailCategory,
                senderType        = SenderType,
                sender            = Sender,
                attachments       = Attachments
            };
        }

        private sealed class FakeIdemEntry
        {
            public string RequestId, Operation, MailId, ResolvedAt;
        }
    }
}



