// Tests: ClaimAll batch-409 double-grant prevention.
//
// Scenario: ClaimAllAttachmentsAsync (global scope) grants rewards for N mails,
// then the single batch state write returns HTTP 409. The module catches this and
// calls PersistGlobalClaimFlagsWithRetryAsync which re-reads fresh state+lock and
// retries the write up to ClaimWriteRetries (3) times.
//
// Invariant: wallet incremented EXACTLY ONCE per mail regardless of how many times
// the state write 409s. No second grant occurs within the same call or after a
// successful retry persist.
//
// HTTP injection: CloudSaveHelper._http is a private static readonly field.
// In .NET 9, FieldInfo.SetValue throws FieldAccessException for initonly statics
// after class initialization. We use a DynamicMethod with skipVisibility=true
// to emit a stsfld instruction directly — the JIT does not enforce initonly for
// unverifiable IL, making this the standard approach for mutable-static test seams.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BackpackAdventures.CloudCode.Tests;

public class BatchClaim409DoubleGrantTests : IDisposable
{
    private readonly HttpClient  _originalHttp;
    private readonly ProgrammableHttpMessageHandler _handler;
    private readonly FakeExecutionContext _ctx;

    private const string PlayerId  = "player-409-test";
    private const string ProjectId = "proj-409-test";

    public BatchClaim409DoubleGrantTests()
    {
        MailboxCache.Enabled = false; // all reads bypass cache → go through our handler
        _handler  = new ProgrammableHttpMessageHandler();
        var http  = new HttpClient(_handler) { Timeout = TimeSpan.FromSeconds(10) };
        _originalHttp = InjectHttpClient(http);
        _ctx = new FakeExecutionContext(ProjectId, PlayerId);
    }

    public void Dispose()
    {
        InjectHttpClient(_originalHttp);
        MailboxCache.Enabled = true;
    }

    // ── JSON helpers ──────────────────────────────────────────────────────────────

    private static string CollectionJson(int count, int coinQty = 10)
    {
        var mails = string.Join(",", System.Linq.Enumerable.Range(0, count).Select(i =>
            $@"{{
                ""MessageId"":""gm_{i:D5}"",
                ""Title"":""Mail {i}"",
                ""StartTime"":""{DateTime.UtcNow.AddHours(-1):o}"",
                ""Attachments"":[{{""PayoutAssetId"":""coins"",""PayoutAmount"":{coinQty},""AssetType"":""Currency"",""Chance"":1.0}}]
            }}"));
        return $@"{{""Mails"":[{mails}]}}";
    }

    private static string EmptyStateJson()  => @"{""MailMetadata"":[]}";
    private static string WalletJson(int coins) => $@"{{""coins"":{coins}}}";

    private ClaimAttachmentModule MakeModule() =>
        new ClaimAttachmentModule(_ctx, null!, NullLogger<ClaimAttachmentModule>.Instance);

    private ClaimAttachmentModule MakeModule(CapturingLogger<ClaimAttachmentModule> log) =>
        new ClaimAttachmentModule(_ctx, null!, log);

    // ── Test 1: batch write 409 once, retry succeeds ──────────────────────────────

    [Fact]
    public async Task ClaimAll_BatchStateWrite409ThenOK_WalletIncrementedExactlyOncePerMail()
    {
        const int mailCount = 3;
        const int coinQty   = 10;

        // mails_all — single GET
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyMailsAll,
            CollectionJson(mailCount, coinQty), writeLock: "lock-mails-v1");

        // player global state — initial GET-with-lock
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyGlobalState,
            EmptyStateJson(), writeLock: "lock-state-v1");

        // wallet: one GET + one POST per mail grant (player_wallet is a no-cache key)
        for (int i = 0; i < mailCount; i++)
        {
            _handler.AddGetCloudSaveResult(MailboxConstants.KeyPlayerWallet, WalletJson(i * coinQty));
            _handler.AddPostOk(MailboxConstants.KeyPlayerWallet);
        }

        // batch state write → 409 (the single write-all fails)
        _handler.AddPostConflict(MailboxConstants.KeyGlobalState);

        // retry: re-read fresh state with new lock
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyGlobalState,
            EmptyStateJson(), writeLock: "lock-state-v2");

        // retry write succeeds
        _handler.AddPostOk(MailboxConstants.KeyGlobalState);

        // ── Run ────────────────────────────────────────────────────────────────────
        var apiResp  = await MakeModule().ClaimAllAttachmentsAsync(
            new ClaimAllAttachmentsRequest { MailType = "global" });
        var response = apiResp.Data!;

        // ── Assert: all 3 mails claimed, none skipped ──────────────────────────────
        Assert.Equal(mailCount, response.ClaimedCount);
        Assert.Equal(0, response.AlreadyClaimedCount);
        Assert.Equal(0, response.SkippedCount);

        // ── Assert: wallet incremented exactly once per mail (no double-grant) ──────
        int walletPosts = _handler.PostCount(MailboxConstants.KeyPlayerWallet);
        Assert.Equal(mailCount, walletPosts);

        // ── Assert: 2 state writes total (1 failed 409 + 1 retry success) ──────────
        int stateWrites = _handler.PostCount(MailboxConstants.KeyGlobalState);
        Assert.Equal(2, stateWrites);

        // ── Assert: timing field present ──────────────────────────────────────────
        Assert.True(apiResp.ServerExecutionMs >= 0);

        Console.WriteLine(
            $"[409-ThenOK] mailCount={mailCount}, walletPosts={walletPosts} (expected {mailCount}), " +
            $"stateWrites={stateWrites} (expected 2). No double-grant.");
    }

    // ── Test 2: all 3 retries fail — wallet still incremented once ───────────────

    [Fact]
    public async Task ClaimAll_AllRetriesFail_WalletIncrementedOnceNoDoubleGrant()
    {
        const int mailCount  = 2;
        const int coinQty    = 5;
        const int maxRetries = 3; // ClaimWriteRetries constant in ClaimAttachmentModule

        _handler.AddGetCloudSaveResult(MailboxConstants.KeyMailsAll,
            CollectionJson(mailCount, coinQty), writeLock: "lock-mails-v1");

        _handler.AddGetCloudSaveResult(MailboxConstants.KeyGlobalState,
            EmptyStateJson(), writeLock: "lock-state-v1");

        for (int i = 0; i < mailCount; i++)
        {
            _handler.AddGetCloudSaveResult(MailboxConstants.KeyPlayerWallet, WalletJson(i * coinQty));
            _handler.AddPostOk(MailboxConstants.KeyPlayerWallet);
        }

        // initial batch write → 409
        _handler.AddPostConflict(MailboxConstants.KeyGlobalState);

        // all 3 retry iterations: read fresh state + 409 on write
        for (int retry = 0; retry < maxRetries; retry++)
        {
            _handler.AddGetCloudSaveResult(MailboxConstants.KeyGlobalState,
                EmptyStateJson(), writeLock: $"lock-state-v{retry + 2}");
            _handler.AddPostConflict(MailboxConstants.KeyGlobalState);
        }

        var log    = new CapturingLogger<ClaimAttachmentModule>();
        var apiResp = await MakeModule(log).ClaimAllAttachmentsAsync(
            new ClaimAllAttachmentsRequest { MailType = "global" });
        var response = apiResp.Data!;

        // ── Assert: grants WERE made (rewards delivered) ──────────────────────────
        Assert.Equal(mailCount, response.ClaimedCount);
        Assert.Equal(0, response.AlreadyClaimedCount);

        // ── Assert: wallet incremented exactly once per mail ──────────────────────
        int walletPosts = _handler.PostCount(MailboxConstants.KeyPlayerWallet);
        Assert.Equal(mailCount, walletPosts);

        // ── Assert: 1 + 3 = 4 total state write attempts, all 409 ────────────────
        int stateWrites = _handler.PostCount(MailboxConstants.KeyGlobalState);
        Assert.Equal(1 + maxRetries, stateWrites);

        // ── Assert: error logged about failed flag persistence ────────────────────
        Assert.True(log.HasErrorContaining("failed to persist claim flags"),
            "Module must log an error when all retries are exhausted.");

        Console.WriteLine(
            $"[AllFail] mailCount={mailCount}, walletPosts={walletPosts} (no double-grant), " +
            $"stateWrites={stateWrites} (all 409). Error logged correctly.");
    }

    // ── Test 3: structural — same in-memory state, second ClaimCore is AlreadyClaimed ─

    [Fact]
    public async Task ClaimCore_SameInMemoryState_SecondCallIsAlreadyClaimed_NoDoubleGrant()
    {
        // Within one ClaimAll batch, metadata.IsClaim is set to true BEFORE the HTTP
        // write. Any second attempt on the same state object (e.g. if two concurrent
        // requests share state in-memory) returns AlreadyClaimed=true — no double grant.

        var col = new GlobalMailCollection();
        col.Mails.Add(new GlobalMailPayload
        {
            Mail = new Mail
            {
                MessageId  = "gm_test",
                Attachments = new List<Payout>
                {
                    new() { PayoutAssetId = "coins", PayoutAmount = 10, AssetType = "Currency", Chance = 1.0 }
                }
            }
        });
        var state = new PlayerGlobalMailState();

        var method = typeof(ClaimAttachmentModule)
            .GetMethod("ClaimGlobalAttachmentCoreAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("ClaimGlobalAttachmentCoreAsync not found via reflection");

        // wallet: 1 GET + 1 POST for the first call only
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyPlayerWallet, WalletJson(0));
        _handler.AddPostOk(MailboxConstants.KeyPlayerWallet);

        var module = MakeModule();

        // First call: grants reward, sets metadata.IsClaim = true in-memory
        var result1 = await (Task<ClaimAttachmentResponse>)method
            .Invoke(module, new object?[] { PlayerId, "gm_test", col, state, null })!;

        Assert.False(result1.AlreadyClaimed, "First call must grant the attachment.");
        Assert.True(state.FindMetadataById("gm_test")?.IsClaim,
            "metadata.IsClaim must be true in-memory immediately after first grant.");

        // Second call on SAME state object — metadata.IsClaim is already true
        var result2 = await (Task<ClaimAttachmentResponse>)method
            .Invoke(module, new object?[] { PlayerId, "gm_test", col, state, null })!;

        Assert.True(result2.AlreadyClaimed,
            "Second call on same state must return AlreadyClaimed=true — in-memory guard prevents double-grant.");

        // wallet granted exactly once
        int walletPosts = _handler.PostCount(MailboxConstants.KeyPlayerWallet);
        Assert.Equal(1, walletPosts);

        Console.WriteLine(
            $"[Structural] walletPosts={walletPosts} — second call is AlreadyClaimed, " +
            "no double-grant from in-memory state.");
    }

    // ── IL helper: bypass initonly on CloudSaveHelper._http in .NET 9+ ───────────

    private static HttpClient InjectHttpClient(HttpClient newClient)
    {
        var field = typeof(CloudSaveHelper)
            .GetField("_http", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("CloudSaveHelper._http field not found");

        var previous = (HttpClient)field.GetValue(null)!;

        // FieldInfo.SetValue throws FieldAccessException for initonly static fields in .NET 9.
        // DynamicMethod with skipVisibility=true emits unverifiable IL; the JIT does NOT
        // enforce initonly at the stsfld instruction for unverifiable code paths.
        var dm = new DynamicMethod(
            "__test_set_CloudSaveHelper_http",
            null,
            new[] { typeof(HttpClient) },
            typeof(CloudSaveHelper).Module,
            skipVisibility: true);
        var il = dm.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stsfld, field);
        il.Emit(OpCodes.Ret);
        ((Action<HttpClient>)dm.CreateDelegate(typeof(Action<HttpClient>)))(newClient);

        return previous;
    }
}

// ── Logger that captures error messages ───────────────────────────────────────

internal sealed class CapturingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
{
    // Captures Warning and above so both LogError and LogWarning are inspectable.
    private readonly List<string> _messages = new();

    public bool HasErrorContaining(string fragment) =>
        _messages.Exists(e => e.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    public bool HasWarningContaining(string fragment) =>
        _messages.Exists(e => e.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel level) => true;

    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel level,
        Microsoft.Extensions.Logging.EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (level >= Microsoft.Extensions.Logging.LogLevel.Warning)
            _messages.Add(formatter(state, exception));
    }
}
