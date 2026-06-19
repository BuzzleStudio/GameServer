// xUnit tests for adminMode (GetGlobalMails) and GetUserMailsAdmin.
//
// Key scenarios:
//   1. GetGlobalMails adminMode=true (service account) returns targeted mails that would
//      otherwise be hidden by IsVisibleToPlayer.
//   2. GetGlobalMails adminMode=true with a non-empty PlayerId (player context) throws
//      UnauthorizedAccessException.
//   3. GetGlobalMails adminMode=false (normal player) hides targeted mails — unchanged behavior.
//   4. GetUserMailsAdmin (service account) reads another player's mailbox.
//   5. GetUserMailsAdmin with a non-empty PlayerId throws UnauthorizedAccessException.
//   6. GetUserMailsAdmin with blank TargetPlayerId throws ArgumentException.

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BackpackAdventures.CloudCode.Tests;

public class AdminModeTests : IDisposable
{
    private readonly HttpClient _originalHttp;
    private readonly ProgrammableHttpMessageHandler _handler;

    // Service-account context: empty PlayerId, non-empty ServiceToken.
    private readonly FakeExecutionContext _svcCtx;
    // Player context: non-empty PlayerId.
    private readonly FakeExecutionContext _playerCtx;

    private const string OperatorId  = "operator-test";
    private const string AdminToken  = "admin-token-test";
    private const string TargetPlayer = "target-player-123";
    private const string CallerPlayer = "caller-player-456";
    private const string ProjectId    = "proj-admin-test";

    public AdminModeTests()
    {
        MailboxCache.Enabled = false;
        _handler = new ProgrammableHttpMessageHandler();
        var http = new HttpClient(_handler) { Timeout = TimeSpan.FromSeconds(10) };
        _originalHttp = HttpSeam.Inject(http);

        // Service account: PlayerId empty, ServiceToken set.
        _svcCtx = new FakeExecutionContext(projectId: ProjectId, playerId: "", serviceToken: "svc-token");
        // Player: PlayerId non-empty (FakeExecutionContext.AccessToken always returns empty, so
        // provide a serviceToken so CloudSaveHelper.AddAuth doesn't throw "both tokens empty").
        // IsServiceAccountCall returns false because PlayerId is non-empty.
        _playerCtx = new FakeExecutionContext(projectId: ProjectId, playerId: CallerPlayer, serviceToken: "player-token");
    }

    public void Dispose()
    {
        HttpSeam.Inject(_originalHttp);
        MailboxCache.Enabled = true;
    }

    // ── JSON helpers ────────────────────────────────────────────────────────────

    // Targeted global mail: TargetUserIds = [targetUserId]. Not visible to other players.
    private static string TargetedGlobalMailJson(string id, string targetUserId)
        => $@"{{
            ""MessageId"":""{id}"",
            ""Title"":""T-{id}"",
            ""Content"":""C"",
            ""TargetUserIds"":[""{targetUserId}""],
            ""StartTime"":""{DateTime.UtcNow.AddHours(-1):o}"",
            ""Attachments"":[]
        }}";

    // Broadcast global mail (no TargetUserIds).
    private static string BroadcastGlobalMailJson(string id)
        => $@"{{
            ""MessageId"":""{id}"",
            ""Title"":""T-{id}"",
            ""Content"":""C"",
            ""TargetUserIds"":null,
            ""StartTime"":""{DateTime.UtcNow.AddHours(-1):o}"",
            ""Attachments"":[]
        }}";

    private static string GlobalCollectionJson(params string[] mails)
        => $@"{{""Mails"":[{string.Join(",", mails)}]}}";

    private static string UserMailJson(string id)
        => $@"{{
            ""MessageId"":""{id}"",
            ""MailInfo"":{{
                ""Title"":""UM-{id}"",
                ""Content"":""C"",
                ""StartTime"":""{DateTime.UtcNow.AddHours(-1):o}"",
                ""Period"":0,
                ""ExpireTime"":null,
                ""Attachment"":[]
            }},
            ""MailMetaData"":{{
                ""IsRead"":false,
                ""IsClaimed"":false,
                ""MailCategory"":""Gift"",
                ""SenderType"":""Player""
            }}
        }}";

    private static string UserMailboxJson(params string[] mails)
        => $@"{{""Mails"":[{string.Join(",", mails)}]}}";

    private GetGlobalMailsModule BuildGlobalModule(FakeExecutionContext ctx)
        => new(ctx, null!, NullLogger<GetGlobalMailsModule>.Instance);

    private GetUserMailsAdminModule BuildAdminModule(FakeExecutionContext ctx)
        => new(ctx, null!, NullLogger<GetUserMailsAdminModule>.Instance);

    // ── Test 1: adminMode=true (service account) returns targeted mail ───────────

    [Fact]
    public async Task GetGlobalMails_AdminMode_ShowsTargetedMail()
    {
        // Arrange: one targeted mail for "other-player", one broadcast.
        var collection = GlobalCollectionJson(
            TargetedGlobalMailJson("mail-targeted", "other-player"),
            BroadcastGlobalMailJson("mail-broadcast"));

        // state key (mail_meta_state) — empty state
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyGlobalState, @"{""ClaimedIds"":[],""Metadata"":[]}");
        // mails_all
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyMailsAll, collection);

        var module = BuildGlobalModule(_svcCtx);
        var request = new GetMailsRequest
        {
            Page = 0, PageSize = 20,
            AdminMode = true,
            OperatorId = OperatorId,
            AdminToken = AdminToken
        };

        // Act
        var result = await module.GetGlobalMailsAsync(request);

        // Assert: both mails returned (targeted not filtered out)
        Assert.NotNull(result);
        var body = result.Data!;
        Assert.Equal(2, body.TotalCount);
        Assert.Equal(2, body.Mails.Count);
    }

    // ── Test 2: adminMode=true with player context → rejected ───────────────────

    [Fact]
    public async Task GetGlobalMails_AdminMode_PlayerContext_Throws()
    {
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyGlobalState, @"{""ClaimedIds"":[],""Metadata"":[]}");
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyMailsAll, GlobalCollectionJson());

        var module = BuildGlobalModule(_playerCtx);
        var request = new GetMailsRequest
        {
            Page = 0, PageSize = 20,
            AdminMode = true,
            OperatorId = OperatorId,
            AdminToken = AdminToken
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => module.GetGlobalMailsAsync(request));
    }

    // ── Test 3: adminMode=false hides targeted mail — unchanged behavior ─────────

    [Fact]
    public async Task GetGlobalMails_NormalMode_HidesTargetedMail()
    {
        var collection = GlobalCollectionJson(
            TargetedGlobalMailJson("mail-targeted", "other-player"),
            BroadcastGlobalMailJson("mail-broadcast"));

        // Normal player context, using _playerCtx.
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyGlobalState, @"{""ClaimedIds"":[],""Metadata"":[]}");
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyMailsAll, collection);

        var module = BuildGlobalModule(_playerCtx);
        var request = new GetMailsRequest { Page = 0, PageSize = 20 }; // adminMode defaults false

        var result = await module.GetGlobalMailsAsync(request);

        // Only the broadcast mail is visible to CallerPlayer.
        Assert.Equal(1, result.Data!.TotalCount);
        Assert.Equal("T-mail-broadcast", result.Data.Mails[0].MailInfo.Title);
    }

    // ── Test 4: GetUserMailsAdmin reads target player mailbox ────────────────────

    [Fact]
    public async Task GetUserMailsAdmin_ServiceAccount_ReturnsTargetPlayerMails()
    {
        var mailbox = UserMailboxJson(UserMailJson("um-1"), UserMailJson("um-2"));
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyUserItems, mailbox);

        var module = BuildAdminModule(_svcCtx);
        var request = new GetUserMailsAdminRequest
        {
            TargetPlayerId = TargetPlayer,
            Page = 0, PageSize = 20,
            OperatorId = OperatorId,
            AdminToken = AdminToken
        };

        var result = await module.GetUserMailsAdminAsync(request);

        Assert.Equal(2, result.Data!.TotalCount);
        Assert.Equal(2, result.Data.Mails.Count);
    }

    // ── Test 5: GetUserMailsAdmin with player context → rejected ─────────────────

    [Fact]
    public async Task GetUserMailsAdmin_PlayerContext_Throws()
    {
        var module = BuildAdminModule(_playerCtx);
        var request = new GetUserMailsAdminRequest
        {
            TargetPlayerId = TargetPlayer,
            Page = 0, PageSize = 20,
            OperatorId = OperatorId,
            AdminToken = AdminToken
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => module.GetUserMailsAdminAsync(request));
    }

    // ── Test 6: GetUserMailsAdmin with blank TargetPlayerId → ArgumentException ──

    [Fact]
    public async Task GetUserMailsAdmin_BlankTargetPlayerId_Throws()
    {
        var module = BuildAdminModule(_svcCtx);
        var request = new GetUserMailsAdminRequest
        {
            TargetPlayerId = "   ",
            Page = 0, PageSize = 20,
            OperatorId = OperatorId,
            AdminToken = AdminToken
        };

        await Assert.ThrowsAsync<ArgumentException>(() => module.GetUserMailsAdminAsync(request));
    }
}
