// Tests for version-aware mails_all caching via global_mail_change_log.
//
//   1. global_mail_change_log is never RAM-cached (read fresh every time).
//   2. mails_all cache HIT when version unchanged.
//   3. mails_all REFETCH when version changes (cross-instance invalidation).
//   4-8. Global mail mutation endpoints bump the version after a real change; no bump on no-ops.
//        Bump is centralized in the mails_all write helper, so it covers SendGlobalMail,
//        SendUserMail, DeleteGlobalMail, ExpireMail, SetMailEndTime, PurgeExpired.
//
// Harness mirrors the other server tests (ProgrammableHttpMessageHandler + HttpSeam).

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BackpackAdventures.CloudCode.Tests;

public class GlobalMailChangeLogTests : IDisposable
{
    private readonly HttpClient _originalHttp;
    private readonly ProgrammableHttpMessageHandler _handler;
    private const string ProjectId = "proj-changelog";

    private const string ChangeLogFrag = "global_mail_change_log";
    private const string MailsAllFrag   = "keys=mails_all";

    public GlobalMailChangeLogTests()
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

    // ── JSON builders ───────────────────────────────────────────────────────────

    private static string ChangeLog(long version)
        => $@"{{""Version"":{version},""LastChangedAt"":""2026-01-01T00:00:00.0000000Z""}}";

    private static string GlobalMail(string id, double startOffsetHours, double? endOffsetHours = null)
    {
        var end = endOffsetHours == null ? ""
            : $@",""EndTime"":""{DateTime.UtcNow.AddHours(endOffsetHours.Value):o}""";
        return $@"{{""MessageId"":""{id}"",""Title"":""t"",""Content"":""c"",""StartTime"":""{DateTime.UtcNow.AddHours(startOffsetHours):o}""{end},""Attachments"":[]}}";
    }

    private static string Collection(params string[] mails)
        => $@"{{""Mails"":[{string.Join(",", mails)}]}}";

    // Service-account context (empty PlayerId + ServiceToken) so AdminAuth passes.
    private static FakeExecutionContext AdminCtx()
        => new FakeExecutionContext(ProjectId, playerId: "", serviceToken: "svc-token");

    private static FakeExecutionContext PlayerCtx()
        => new FakeExecutionContext(ProjectId, playerId: "player-1", serviceToken: "svc-token");

    // ════════════════════════════════════════════════════════════════════════════
    // 1. global_mail_change_log is not cached
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GlobalMailChangeLog_IsNeverCached_EveryReadHitsRest()
    {
        MailboxCache.Enabled = true;
        var ctx = PlayerCtx();
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyGlobalMailChangeLog, ChangeLog(7));

        var v1 = await CloudSaveHelper.GetCurrentGlobalMailVersionAsync(null!, ctx);
        var v2 = await CloudSaveHelper.GetCurrentGlobalMailVersionAsync(null!, ctx);

        Assert.Equal(7, v1);
        Assert.Equal(7, v2);
        Assert.Equal(2, _handler.GetCount(ChangeLogFrag)); // not cached → two REST reads

        Console.WriteLine("[1] global_mail_change_log read twice = 2 REST GETs (no RAM cache).");
    }

    // ════════════════════════════════════════════════════════════════════════════
    // 2. mails_all cache HIT when version unchanged
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MailsAll_CacheHit_WhenVersionUnchanged()
    {
        MailboxCache.Enabled = true;
        var ctx = PlayerCtx();
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyGlobalMailChangeLog, ChangeLog(5));
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyMailsAll, Collection(GlobalMail("gm_1", -1)));

        var a = await CloudSaveHelper.GetCustomDataAsync<GlobalMailCollection>(null!, ctx, MailboxConstants.KeyMailsAll);
        var b = await CloudSaveHelper.GetCustomDataAsync<GlobalMailCollection>(null!, ctx, MailboxConstants.KeyMailsAll);

        Assert.Single(a!.Mails);
        Assert.Single(b!.Mails);
        Assert.Equal(1, _handler.GetCount(MailsAllFrag));   // mails_all fetched ONCE (2nd is cache hit)
        Assert.Equal(2, _handler.GetCount(ChangeLogFrag));  // version validated on EACH read

        Console.WriteLine("[2] version unchanged (5): mails_all GET=1 (cache hit), change_log GET=2 (validated).");
    }

    // ════════════════════════════════════════════════════════════════════════════
    // 3. mails_all REFETCH when version changes
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MailsAll_Refetched_WhenVersionChanges()
    {
        MailboxCache.Enabled = true;
        var ctx = PlayerCtx();
        // First read sees version 1, second read sees version 2 (another instance bumped).
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyGlobalMailChangeLog, ChangeLog(1));
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyGlobalMailChangeLog, ChangeLog(2));
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyMailsAll, Collection(GlobalMail("gm_1", -1)));

        await CloudSaveHelper.GetCustomDataAsync<GlobalMailCollection>(null!, ctx, MailboxConstants.KeyMailsAll); // v1 → fetch, cache@1
        await CloudSaveHelper.GetCustomDataAsync<GlobalMailCollection>(null!, ctx, MailboxConstants.KeyMailsAll); // v2 → mismatch → refetch

        Assert.Equal(2, _handler.GetCount(MailsAllFrag)); // refetched because version changed

        Console.WriteLine("[3] version 1→2: mails_all GET=2 (cache invalidated by version change).");
    }

    // ════════════════════════════════════════════════════════════════════════════
    // 4. SendGlobalMail bumps version
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SendGlobalMail_BumpsVersion()
    {
        MailboxCache.Enabled = false;
        var ctx = AdminCtx();
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyMailsAll, Collection(), writeLock: "lk-mails");
        _handler.AddPostOk(MailboxConstants.KeyMailsAll);
        _handler.AddPostOk(MailboxConstants.KeyGlobalMailChangeLog);

        var module = new SendGlobalMailModule(ctx, null!, NullLogger<SendGlobalMailModule>.Instance);
        var resp = await module.SendGlobalMailAsync(new SendGlobalMailRequest
        {
            Subject = "Hi", Body = "Body", OperatorId = "op-1", AdminToken = "t"
        });

        Assert.False(string.IsNullOrEmpty(resp.Data!.GlobalMailId));
        Assert.Equal(1, _handler.PostCount(MailboxConstants.KeyMailsAll));
        Assert.Equal(1, _handler.PostCount(ChangeLogFrag)); // version bumped

        Console.WriteLine("[4] SendGlobalMail → change_log POST=1 (bumped).");
    }

    // ════════════════════════════════════════════════════════════════════════════
    // 5. DeleteGlobalMail bumps version
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeleteGlobalMail_BumpsVersion()
    {
        MailboxCache.Enabled = false;
        var ctx = AdminCtx();
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyMailsAll, Collection(GlobalMail("gm_del", -1)), writeLock: "lk");
        _handler.AddPostOk(MailboxConstants.KeyMailsAll);
        _handler.AddPostOk(MailboxConstants.KeyGlobalMailChangeLog);

        var module = new ExpireMailModule(ctx, null!, NullLogger<ExpireMailModule>.Instance);
        var resp = await module.DeleteGlobalMailAsync(new AdminDeleteMailRequest
        {
            MailId = "gm_del", OperatorId = "op-1", AdminToken = "t"
        });

        Assert.Equal("gm_del", resp.Data!.MailId);
        Assert.Equal(1, _handler.PostCount(ChangeLogFrag));

        Console.WriteLine("[5] DeleteGlobalMail → change_log POST=1 (bumped).");
    }

    // ════════════════════════════════════════════════════════════════════════════
    // 6. ExpireMail bumps only when it actually changes global mail data
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExpireMail_Found_BumpsVersion()
    {
        MailboxCache.Enabled = false;
        var ctx = AdminCtx();
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyMailsAll, Collection(GlobalMail("gm_x", -1)), writeLock: "lk");
        _handler.AddPostOk(MailboxConstants.KeyMailsAll);
        _handler.AddPostOk(MailboxConstants.KeyGlobalMailChangeLog);

        var module = new ExpireMailModule(ctx, null!, NullLogger<ExpireMailModule>.Instance);
        await module.ExpireMailAsync(new ExpireMailRequest { MailId = "gm_x", OperatorId = "op-1", AdminToken = "t" });

        Assert.Equal(1, _handler.PostCount(ChangeLogFrag));
        Console.WriteLine("[6a] ExpireMail (found) → change_log POST=1.");
    }

    [Fact]
    public async Task ExpireMail_NotFound_NoBump()
    {
        MailboxCache.Enabled = false;
        var ctx = AdminCtx();
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyMailsAll, Collection(), writeLock: "lk"); // empty → not found

        var module = new ExpireMailModule(ctx, null!, NullLogger<ExpireMailModule>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            module.ExpireMailAsync(new ExpireMailRequest { MailId = "gm_missing", OperatorId = "op-1", AdminToken = "t" }));

        Assert.Equal(0, _handler.PostCount(MailboxConstants.KeyMailsAll)); // no write
        Assert.Equal(0, _handler.PostCount(ChangeLogFrag));                // no bump
        Console.WriteLine("[6b] ExpireMail (not found) → no write, change_log POST=0.");
    }

    // ════════════════════════════════════════════════════════════════════════════
    // 7. SetMailEndTime bumps only when it actually changes global mail data
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SetMailEndTime_Found_BumpsVersion()
    {
        MailboxCache.Enabled = false;
        var ctx = AdminCtx();
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyMailsAll, Collection(GlobalMail("gm_y", -1)), writeLock: "lk");
        _handler.AddPostOk(MailboxConstants.KeyMailsAll);
        _handler.AddPostOk(MailboxConstants.KeyGlobalMailChangeLog);

        var module = new ExpireMailModule(ctx, null!, NullLogger<ExpireMailModule>.Instance);
        await module.SetMailEndTimeAsync(new SetMailEndTimeRequest
        {
            MailId = "gm_y", EndTime = DateTime.UtcNow.AddDays(1).ToString("o"), OperatorId = "op-1", AdminToken = "t"
        });

        Assert.Equal(1, _handler.PostCount(ChangeLogFrag));
        Console.WriteLine("[7a] SetMailEndTime (found) → change_log POST=1.");
    }

    [Fact]
    public async Task SetMailEndTime_NotFound_NoBump()
    {
        MailboxCache.Enabled = false;
        var ctx = AdminCtx();
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyMailsAll, Collection(), writeLock: "lk");

        var module = new ExpireMailModule(ctx, null!, NullLogger<ExpireMailModule>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            module.SetMailEndTimeAsync(new SetMailEndTimeRequest
            {
                MailId = "gm_missing", EndTime = DateTime.UtcNow.AddDays(1).ToString("o"), OperatorId = "op-1", AdminToken = "t"
            }));

        Assert.Equal(0, _handler.PostCount(ChangeLogFrag));
        Console.WriteLine("[7b] SetMailEndTime (not found) → change_log POST=0.");
    }

    // ════════════════════════════════════════════════════════════════════════════
    // 8. PurgeExpired bumps only when it actually removes mails
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PurgeExpired_RemovesSomething_BumpsVersion()
    {
        MailboxCache.Enabled = false;
        var ctx = AdminCtx();
        // gm_old has EndTime 5h in the past → expired → purged.
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyMailsAll,
            Collection(GlobalMail("gm_old", -10, endOffsetHours: -5)), writeLock: "lk");
        _handler.AddPostOk(MailboxConstants.KeyMailsAll);
        _handler.AddPostOk(MailboxConstants.KeyGlobalMailChangeLog);

        var module = new PurgeExpiredModule(ctx, null!, NullLogger<PurgeExpiredModule>.Instance);
        var resp = await module.PurgeExpiredAsync(new PurgeExpiredRequest { OperatorId = "op-1", AdminToken = "t" });

        Assert.True(resp.Data!.PurgedCount > 0);
        Assert.Equal(1, _handler.PostCount(ChangeLogFrag));
        Console.WriteLine($"[8a] PurgeExpired removed {resp.Data.PurgedCount} → change_log POST=1.");
    }

    [Fact]
    public async Task PurgeExpired_NothingExpired_NoBump()
    {
        MailboxCache.Enabled = false;
        var ctx = AdminCtx();
        // gm_live has no EndTime → never expires → nothing purged.
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyMailsAll,
            Collection(GlobalMail("gm_live", -1)), writeLock: "lk");

        var module = new PurgeExpiredModule(ctx, null!, NullLogger<PurgeExpiredModule>.Instance);
        var resp = await module.PurgeExpiredAsync(new PurgeExpiredRequest { OperatorId = "op-1", AdminToken = "t" });

        Assert.Equal(0, resp.Data!.PurgedCount);
        Assert.Equal(0, _handler.PostCount(MailboxConstants.KeyMailsAll)); // no write
        Assert.Equal(0, _handler.PostCount(ChangeLogFrag));                // no bump
        Console.WriteLine("[8b] PurgeExpired (nothing expired) → no write, change_log POST=0.");
    }
}
