// Tests for best-effort global_mail_change_log bump AFTER a successful mails_all commit.
//
//   1. mails_all POST fails        → endpoint fails, no bump.
//   2. mails_all + bump succeed     → mails_all cache stamped with the new version.
//   3. mails_all OK, bump non-409   → endpoint SUCCEEDS, warning logged, local cache evicted.
//   4. bump 409 once then retry OK  → endpoint succeeds, mails_all written once (no double bump).
//   5. bump 409 exhausted           → endpoint succeeds (commit stands), local cache evicted.
//
// mails_all is the source of truth; the change-log bump is a best-effort cache-invalidation signal.

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BackpackAdventures.CloudCode.Tests;

public class BumpFailureTests : IDisposable
{
    private readonly HttpClient _originalHttp;
    private readonly ProgrammableHttpMessageHandler _handler;
    private const string ProjectId = "proj-bumpfail";
    private const string ChangeLogFrag = "global_mail_change_log";

    public BumpFailureTests()
    {
        _handler = new ProgrammableHttpMessageHandler();
        var http = new HttpClient(_handler) { Timeout = TimeSpan.FromSeconds(10) };
        _originalHttp = HttpSeam.Inject(http);
    }

    public void Dispose()
    {
        HttpSeam.Inject(_originalHttp);
        MailboxCache.Enabled = true;
    }

    // ── builders ──────────────────────────────────────────────────────────────

    private static string GlobalMail(string id, double startOffsetHours)
        => $@"{{""MessageId"":""{id}"",""Title"":""t"",""Content"":""c"",""StartTime"":""{DateTime.UtcNow.AddHours(startOffsetHours):o}"",""Attachments"":[]}}";

    private static string Collection(params string[] mails)
        => $@"{{""Mails"":[{string.Join(",", mails)}]}}";

    private static string ChangeLog(long v)
        => $@"{{""Version"":{v},""LastChangedAt"":""2026-01-01T00:00:00Z""}}";

    private static FakeExecutionContext AdminCtx()
        => new FakeExecutionContext(ProjectId, playerId: "", serviceToken: "svc");

    private static HttpResponseMessage Resp(HttpStatusCode status, string json = "{}")
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string MailsAllCacheKey()
        => $"{ProjectId}:custom:{CloudSaveHelper.GlobalCustomId}:{MailboxConstants.KeyMailsAll}";

    // ════════════════════════════════════════════════════════════════════════════
    // 1. mails_all POST fails → endpoint fails, no bump
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MailsAllPostFails_EndpointFails_NoBump()
    {
        MailboxCache.Enabled = false;
        var ctx = AdminCtx();
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyMailsAll, Collection(GlobalMail("gm_x", -1)), writeLock: "lk");
        _handler.AddPost(MailboxConstants.KeyMailsAll, Resp(HttpStatusCode.InternalServerError)); // mails_all write 500

        var module = new ExpireMailModule(ctx, null!, NullLogger<ExpireMailModule>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            module.ExpireMailAsync(new ExpireMailRequest { MailId = "gm_x", OperatorId = "op", AdminToken = "t" }));

        Assert.Equal(0, _handler.PostCount(ChangeLogFrag)); // bump never reached
        Console.WriteLine("[1] mails_all POST 500 → endpoint throws, change_log POST=0 (no bump).");
    }

    // ════════════════════════════════════════════════════════════════════════════
    // 2. mails_all + bump succeed → cache stamped with the new version
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task BumpSucceeds_MailsAllCacheStampedWithNewVersion()
    {
        MailboxCache.Enabled = true;
        var ctx = AdminCtx();
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyGlobalMailChangeLog, ChangeLog(9), writeLock: "lk-cl");
        _handler.AddPostOk(MailboxConstants.KeyGlobalMailChangeLog);

        var collection = new GlobalMailCollection();
        collection.Mails.Add(new GlobalMailPayload { Mail = new Mail { MessageId = "gm_1" } });

        _handler.AddPostOk(MailboxConstants.KeyMailsAll);

        await CloudSaveHelper.SetCustomDataWithLockAsync(
            null!, ctx, MailboxConstants.KeyMailsAll, collection, "lk", NullLogger<BumpFailureTests>.Instance);

        var key = MailsAllCacheKey();
        Assert.True(MailboxCache.TryGetVersioned<GlobalMailCollection>(key, 10, out var stamped),
            "mails_all must be cached at the bumped version (9 → 10).");
        Assert.Single(stamped!.Mails);
        Assert.False(MailboxCache.TryGetVersioned<GlobalMailCollection>(key, 9, out _),
            "Old version must NOT hit.");

        Console.WriteLine("[2] bump 9→10: mails_all cache stamped @10 (hit at 10, miss at 9).");
    }

    // ════════════════════════════════════════════════════════════════════════════
    // 3. mails_all OK, bump non-409 transient → endpoint succeeds, warning, cache evicted
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task BumpNon409Fails_EndpointSucceeds_Warns_EvictsCache()
    {
        MailboxCache.Enabled = true;
        var ctx = AdminCtx();
        var log = new CapturingLogger<ExpireMailModule>();

        // Pre-seed a stale versioned mails_all entry to prove eviction.
        var stale = new GlobalMailCollection();
        MailboxCache.SetVersioned(MailsAllCacheKey(), stale, 1);

        _handler.AddGetCloudSaveResult(MailboxConstants.KeyMailsAll, Collection(GlobalMail("gm_x", -1)), writeLock: "lk");
        _handler.AddPostOk(MailboxConstants.KeyMailsAll);
        _handler.AddPost(MailboxConstants.KeyGlobalMailChangeLog, Resp(HttpStatusCode.InternalServerError)); // bump 500

        var module = new ExpireMailModule(ctx, null!, log);
        var resp = await module.ExpireMailAsync(new ExpireMailRequest { MailId = "gm_x", OperatorId = "op", AdminToken = "t" });

        Assert.Equal("gm_x", resp.Data!.MailId);                         // committed mutation succeeds
        Assert.Equal(1, _handler.PostCount(MailboxConstants.KeyMailsAll)); // mails_all written
        Assert.True(log.HasWarningContaining("bump failed"), "Bump failure must be logged.");
        Assert.False(MailboxCache.TryGetVersioned<GlobalMailCollection>(MailsAllCacheKey(), 1, out _),
            "Local mails_all cache must be evicted on bump failure (no wrong-version stamp).");

        Console.WriteLine("[3] mails_all OK + bump 500 → endpoint success, warning logged, cache evicted.");
    }

    // ════════════════════════════════════════════════════════════════════════════
    // 4. bump 409 once then retry OK → endpoint succeeds, no double bump on mails_all
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Bump409ThenRetryOK_EndpointSucceeds_NoDoubleMailsAllWrite()
    {
        MailboxCache.Enabled = false;
        var ctx = AdminCtx();
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyMailsAll, Collection(GlobalMail("gm_x", -1)), writeLock: "lk");
        _handler.AddPostOk(MailboxConstants.KeyMailsAll);
        _handler.AddPostConflict(MailboxConstants.KeyGlobalMailChangeLog); // bump attempt 1 → 409
        _handler.AddPostOk(MailboxConstants.KeyGlobalMailChangeLog);       // bump attempt 2 → OK

        var module = new ExpireMailModule(ctx, null!, NullLogger<ExpireMailModule>.Instance);
        var resp = await module.ExpireMailAsync(new ExpireMailRequest { MailId = "gm_x", OperatorId = "op", AdminToken = "t" });

        Assert.Equal("gm_x", resp.Data!.MailId);
        Assert.Equal(1, _handler.PostCount(MailboxConstants.KeyMailsAll)); // single mutation, NOT double
        Assert.Equal(2, _handler.PostCount(ChangeLogFrag));               // 409 + OK = same bump retried

        Console.WriteLine("[4] bump 409→OK: endpoint success, mails_all POST=1 (no double bump), change_log POST=2 (retry).");
    }

    // ════════════════════════════════════════════════════════════════════════════
    // 5. bump 409 exhausted → endpoint succeeds (commit stands), cache evicted
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Bump409Exhausted_EndpointSucceeds_EvictsCache()
    {
        MailboxCache.Enabled = true;
        var ctx = AdminCtx();
        var log = new CapturingLogger<ExpireMailModule>();
        MailboxCache.SetVersioned(MailsAllCacheKey(), new GlobalMailCollection(), 1); // stale entry

        _handler.AddGetCloudSaveResult(MailboxConstants.KeyMailsAll, Collection(GlobalMail("gm_x", -1)), writeLock: "lk");
        _handler.AddPostOk(MailboxConstants.KeyMailsAll);
        _handler.AddPostConflict(MailboxConstants.KeyGlobalMailChangeLog); // sticky 409 → all 3 attempts conflict

        var module = new ExpireMailModule(ctx, null!, log);
        var resp = await module.ExpireMailAsync(new ExpireMailRequest { MailId = "gm_x", OperatorId = "op", AdminToken = "t" });

        Assert.Equal("gm_x", resp.Data!.MailId);                          // commit stands
        Assert.Equal(1, _handler.PostCount(MailboxConstants.KeyMailsAll));
        Assert.Equal(3, _handler.PostCount(ChangeLogFrag));               // ChangeLogBumpAttempts = 3
        Assert.True(log.HasWarningContaining("bump failed"));
        Assert.False(MailboxCache.TryGetVersioned<GlobalMailCollection>(MailsAllCacheKey(), 1, out _),
            "Cache must be evicted after exhausted bump retries.");

        Console.WriteLine("[5] bump 409 x3 exhausted → endpoint success, change_log POST=3, cache evicted.");
    }
}
