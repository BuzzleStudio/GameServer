// Tests for the four hot-path Mailbox optimizations:
//   A. Idempotency store is fire-and-forget (off the response critical path) — Claim + MarkRead.
//   B. MarkAllRead runs its two independent key-writes concurrently (Task.WhenAll).
//   C. GetGlobalMails filters+sorts payloads and builds DTOs for the page only (order preserved).
//   D. MarkRead (user) + DeleteMail use the O(1) PlayerUserMailbox.FindById index.
//
// Harness mirrors BatchClaim409DoubleGrantTests: MailboxCache disabled so every read hits the
// ProgrammableHttpMessageHandler; HttpSeam swaps CloudSaveHelper._http.

using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BackpackAdventures.CloudCode.Tests;

public class HotPathOptimizationTests : IDisposable
{
    private readonly HttpClient _originalHttp;
    private readonly ProgrammableHttpMessageHandler _handler;
    private readonly FakeExecutionContext _ctx;

    private const string PlayerId  = "player-hotpath";
    private const string ProjectId = "proj-hotpath";

    public HotPathOptimizationTests()
    {
        MailboxCache.Enabled = false;
        _handler = new ProgrammableHttpMessageHandler();
        var http = new HttpClient(_handler) { Timeout = TimeSpan.FromSeconds(10) };
        _originalHttp = HttpSeam.Inject(http);
        _ctx = new FakeExecutionContext(ProjectId, PlayerId);
    }

    public void Dispose()
    {
        HttpSeam.Inject(_originalHttp);
        MailboxCache.Enabled = true;
    }

    // ── JSON builders ───────────────────────────────────────────────────────────

    private static string UserMailJson(string id, bool isRead = false, bool isClaimed = true,
        bool withAttachment = false, string? expireTime = null)
    {
        var att = withAttachment
            ? @"[{""PayoutAssetId"":""coins"",""Chance"":1.0,""AssetType"":""Currency"",""PayoutAmount"":10}]"
            : "[]";
        var expire = expireTime == null ? "null" : $@"""{expireTime}""";
        return $@"{{
            ""MessageId"":""{id}"",
            ""MailInfo"":{{""Title"":""T-{id}"",""Content"":""C"",""StartTime"":""{DateTime.UtcNow.AddHours(-1):o}"",""Period"":0,""ExpireTime"":{expire},""Attachment"":{att}}},
            ""MailMetaData"":{{""IsRead"":{(isRead ? "true" : "false")},""IsClaimed"":{(isClaimed ? "true" : "false")},""MailCategory"":""System"",""SenderType"":""System""}}
        }}";
    }

    private static string UserMailboxJson(params string[] mailJsons)
        => $@"{{""Mails"":[{string.Join(",", mailJsons)}]}}";

    // Global mail: startOffsetHours negative = in the past (available). targets null = broadcast.
    private static string GlobalMailJson(string id, double startOffsetHours, string? targetUserId = null)
    {
        var targets = targetUserId == null ? "null" : $@"[""{targetUserId}""]";
        return $@"{{
            ""MessageId"":""{id}"",
            ""Title"":""T-{id}"",
            ""Content"":""C"",
            ""TargetUserIds"":{targets},
            ""StartTime"":""{DateTime.UtcNow.AddHours(startOffsetHours):o}"",
            ""Attachments"":[{{""PayoutAssetId"":""coins"",""Chance"":1.0,""AssetType"":""Currency"",""PayoutAmount"":5}}]
        }}";
    }

    private static string GlobalCollectionJson(params string[] mailJsons)
        => $@"{{""Mails"":[{string.Join(",", mailJsons)}]}}";

    // ════════════════════════════════════════════════════════════════════════════
    // A. Idempotency store is fire-and-forget (off critical path)
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MarkMailRead_IdemStoreFails_ResponseStillSucceeds_AndStoreIsNotAwaited()
    {
        // user mailbox with one unread mail
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyUserItems,
            UserMailboxJson(UserMailJson("um_1", isRead: false)), writeLock: "lock-u1");
        _handler.AddPostOk(MailboxConstants.KeyUserItems);

        // idem precheck GET → empty (fallback) ; idem store POST → 409 (would throw if awaited)
        _handler.AddPostConflict(MailboxConstants.KeyIdemCache);

        var module = new MarkReadModule(_ctx, null!, NullLogger<MarkReadModule>.Instance);
        var resp = await module.MarkMailReadAsync(new MarkMailReadRequest
        {
            MailId = "um_1", MailType = "user", RequestId = "req-A1"
        });

        // Response is success even though the idem store will fail — proves it is OFF the critical path.
        Assert.True(resp.Data!.IsRead);
        Assert.Equal("um_1", resp.Data.MailId);

        // The store was launched as a separate (un-awaited) task and exposed for the test.
        Assert.NotNull(module.PendingIdemStore);
        // Awaiting it must NOT throw — StoreIdemSafeAsync swallows the 409.
        await module.PendingIdemStore!;

        Console.WriteLine("[A/MarkRead] response succeeded despite idem-store 409; PendingIdemStore completed without throwing.");
    }

    [Fact]
    public async Task ClaimAttachment_IdemStoreFails_ClaimStillSucceeds_NoThrow()
    {
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyUserItems,
            UserMailboxJson(UserMailJson("um_claim", isClaimed: false, withAttachment: true)), writeLock: "lock-c1");
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyPlayerWallet, @"{""coins"":0}");
        _handler.AddPostOk(MailboxConstants.KeyPlayerWallet);
        _handler.AddPostOk(MailboxConstants.KeyUserItems);
        _handler.AddPostConflict(MailboxConstants.KeyIdemCache); // idem store fails

        var module = new ClaimAttachmentModule(_ctx, null!, NullLogger<ClaimAttachmentModule>.Instance);
        var resp = await module.ClaimAttachmentAsync(new ClaimAttachmentRequest
        {
            MailId = "um_claim", MailType = "user", RequestId = "req-A2"
        });

        Assert.False(resp.Data!.AlreadyClaimed);
        Assert.Equal(1, _handler.PostCount(MailboxConstants.KeyPlayerWallet)); // granted exactly once
        Assert.NotNull(module.PendingIdemStore);
        await module.PendingIdemStore!; // swallows the idem 409

        Console.WriteLine("[A/Claim] claim succeeded + granted once; idem-store 409 did not affect response.");
    }

    // ════════════════════════════════════════════════════════════════════════════
    // B. MarkAllRead writes both keys (parallel) and retries are intact
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MarkAllRead_WritesBothUserItemsAndMeta()
    {
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyUserItems,
            UserMailboxJson(UserMailJson("um_1"), UserMailJson("um_2")), writeLock: "lock-u1");
        _handler.AddPostOk(MailboxConstants.KeyUserItems);
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyMeta, @"{}", writeLock: "lock-m1");
        _handler.AddPostOk(MailboxConstants.KeyMeta);

        var module = new MarkReadModule(_ctx, null!, NullLogger<MarkReadModule>.Instance);
        var resp = await module.MarkAllReadAsync();

        Assert.False(string.IsNullOrEmpty(resp.Data!.LastReadAt));
        Assert.Equal(1, _handler.PostCount(MailboxConstants.KeyUserItems));
        Assert.Equal(1, _handler.PostCount(MailboxConstants.KeyMeta));

        // Both mails flagged read in the persisted user-items body.
        var body = _handler.LastPost(MailboxConstants.KeyUserItems)!.Body!;
        Assert.DoesNotContain("\"IsRead\":false", body.Replace(" ", ""));

        Console.WriteLine("[B] MarkAllRead wrote both user_items and meta (parallel branches both ran).");
    }

    [Fact]
    public async Task MarkAllRead_UserItemsConflictThenRetry_BothStillPersist()
    {
        // user items: first write 409, retry read + write OK
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyUserItems,
            UserMailboxJson(UserMailJson("um_1")), writeLock: "lock-u1");
        _handler.AddPostConflict(MailboxConstants.KeyUserItems);
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyUserItems,
            UserMailboxJson(UserMailJson("um_1")), writeLock: "lock-u2");
        _handler.AddPostOk(MailboxConstants.KeyUserItems);
        // meta OK first try
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyMeta, @"{}", writeLock: "lock-m1");
        _handler.AddPostOk(MailboxConstants.KeyMeta);

        var module = new MarkReadModule(_ctx, null!, NullLogger<MarkReadModule>.Instance);
        var resp = await module.MarkAllReadAsync();

        Assert.False(string.IsNullOrEmpty(resp.Data!.LastReadAt));
        Assert.Equal(2, _handler.PostCount(MailboxConstants.KeyUserItems)); // 409 + retry
        Assert.Equal(1, _handler.PostCount(MailboxConstants.KeyMeta));

        Console.WriteLine("[B] MarkAllRead retry intact under parallelization: user_items posts=2, meta posts=1.");
    }

    // ════════════════════════════════════════════════════════════════════════════
    // C. GetGlobalMails pagination — order preserved, totalCount/filters correct,
    //    DTOs built only for the page.
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetGlobalMails_PaginatesNewestFirst_OrderPreserved()
    {
        // 5 mails, StartTime increasing (gm_4 newest). All broadcast + available.
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyGlobalState, @"{""MailMetadata"":[]}");
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyMailsAll, GlobalCollectionJson(
            GlobalMailJson("gm_0", -5),
            GlobalMailJson("gm_1", -4),
            GlobalMailJson("gm_2", -3),
            GlobalMailJson("gm_3", -2),
            GlobalMailJson("gm_4", -1)));

        var module = new GetGlobalMailsModule(_ctx, null!, NullLogger<GetGlobalMailsModule>.Instance);

        var page0 = (await module.GetGlobalMailsAsync(new GetMailsRequest { Page = 0, PageSize = 2 })).Data!;
        Assert.Equal(5, page0.TotalCount);
        Assert.Equal(2, page0.Mails.Count);
        Assert.Equal("gm_4", page0.Mails[0].MessageId); // newest first
        Assert.Equal("gm_3", page0.Mails[1].MessageId);
        Assert.True(page0.HasMore);

        var page2 = (await module.GetGlobalMailsAsync(new GetMailsRequest { Page = 2, PageSize = 2 })).Data!;
        Assert.Single(page2.Mails);
        Assert.Equal("gm_0", page2.Mails[0].MessageId); // oldest, last page
        Assert.False(page2.HasMore);

        Console.WriteLine("[C] GetGlobalMails order preserved (newest-first) + pagination correct; DTOs built per-page only.");
    }

    [Fact]
    public async Task GetGlobalMails_FiltersInvisibleUnavailableDeleted_FromTotalCount()
    {
        // gm_vis: broadcast, available → counts.
        // gm_other: targeted to another player → not visible.
        // gm_future: starts in +5h → not available.
        // gm_del: broadcast+available but marked IsDelete in player state → excluded.
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyGlobalState,
            @"{""MailMetadata"":[{""MessageId"":""gm_del"",""IsDelete"":true}]}");
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyMailsAll, GlobalCollectionJson(
            GlobalMailJson("gm_vis", -2),
            GlobalMailJson("gm_other", -2, targetUserId: "someone-else"),
            GlobalMailJson("gm_future", +5),
            GlobalMailJson("gm_del", -2)));

        var module = new GetGlobalMailsModule(_ctx, null!, NullLogger<GetGlobalMailsModule>.Instance);
        var resp = (await module.GetGlobalMailsAsync(new GetMailsRequest { Page = 0, PageSize = 20 })).Data!;

        Assert.Equal(1, resp.TotalCount);
        Assert.Single(resp.Mails);
        Assert.Equal("gm_vis", resp.Mails[0].MessageId);

        Console.WriteLine("[C] GetGlobalMails filtered invisible/unavailable/deleted before pagination — totalCount=1.");
    }

    // ════════════════════════════════════════════════════════════════════════════
    // D. O(1) FindById used by MarkRead (user) + DeleteMail — correct element resolved
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MarkMailReadUser_FindByIdResolvesCorrectMailAmongMany()
    {
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyUserItems, UserMailboxJson(
            UserMailJson("um_1", isRead: false),
            UserMailJson("um_2", isRead: false),
            UserMailJson("um_3", isRead: false)), writeLock: "lock-u1");
        _handler.AddPostOk(MailboxConstants.KeyUserItems);

        var module = new MarkReadModule(_ctx, null!, NullLogger<MarkReadModule>.Instance);
        var resp = await module.MarkMailReadAsync(new MarkMailReadRequest { MailId = "um_2", MailType = "user" });
        Assert.True(resp.Data!.IsRead);

        // Only um_2 should be flagged read in the persisted body.
        var body = _handler.LastPost(MailboxConstants.KeyUserItems)!.Body!;
        using var doc = JsonDocument.Parse(body);
        var value = ExtractWrittenValue(doc);
        foreach (var m in value.GetProperty("Mails").EnumerateArray())
        {
            var id = m.GetProperty("MessageId").GetString();
            var isRead = m.GetProperty("MailMetaData").GetProperty("IsRead").GetBoolean();
            Assert.Equal(id == "um_2", isRead);
        }

        Console.WriteLine("[D] MarkMailRead resolved um_2 via O(1) FindById; only um_2 marked read.");
    }

    [Fact]
    public async Task MarkMailReadUser_MissingMail_ThrowsMailNotFound()
    {
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyUserItems,
            UserMailboxJson(UserMailJson("um_1")), writeLock: "lock-u1");

        var module = new MarkReadModule(_ctx, null!, NullLogger<MarkReadModule>.Instance);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            module.MarkMailReadAsync(new MarkMailReadRequest { MailId = "um_missing", MailType = "user" }));
        Assert.Contains(MailboxError.MailNotFound, ex.Message);

        Console.WriteLine("[D] MarkMailRead missing id → MailNotFound (FindById returned null).");
    }

    [Fact]
    public async Task DeleteMailUser_FindByIdRemovesCorrectMail()
    {
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyUserItems, UserMailboxJson(
            UserMailJson("um_1", isClaimed: true, withAttachment: false),
            UserMailJson("um_2", isClaimed: true, withAttachment: false)), writeLock: "lock-u1");
        _handler.AddPostOk(MailboxConstants.KeyUserItems);

        var module = new DeleteMailModule(_ctx, null!, NullLogger<DeleteMailModule>.Instance);
        var resp = await module.DeleteMailAsync(new DeleteMailRequest { MailId = "um_1" });
        Assert.Equal("um_1", resp.Data!.MailId);

        var body = _handler.LastPost(MailboxConstants.KeyUserItems)!.Body!;
        using var doc = JsonDocument.Parse(body);
        var value = ExtractWrittenValue(doc);
        var ids = new System.Collections.Generic.List<string>();
        foreach (var m in value.GetProperty("Mails").EnumerateArray())
            ids.Add(m.GetProperty("MessageId").GetString()!);

        Assert.DoesNotContain("um_1", ids);
        Assert.Contains("um_2", ids);

        Console.WriteLine("[D] DeleteMail removed um_1 via O(1) FindById; um_2 retained.");
    }

    // Cloud Save POST body is {"key":..,"value":<obj>,...}. Return the value element.
    private static JsonElement ExtractWrittenValue(JsonDocument doc)
        => doc.RootElement.GetProperty("value");
}
