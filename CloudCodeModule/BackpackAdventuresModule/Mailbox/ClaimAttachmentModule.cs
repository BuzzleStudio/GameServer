using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

public class ClaimAttachmentModule
{
    private readonly IExecutionContext _context;
    private readonly IGameApiClient _gameApiClient;
    private readonly ILogger<ClaimAttachmentModule> _logger;

    public ClaimAttachmentModule(IExecutionContext context, IGameApiClient gameApiClient, ILogger<ClaimAttachmentModule> logger)
    {
        _context = context;
        _gameApiClient = gameApiClient;
        _logger = logger;
    }

    /// <summary>Claims the attachment from a mail. Idempotent guard: duplicate claims throw AlreadyClaimed. Returns attachment data; Economy grant is caller's responsibility.</summary>
    [CloudCodeFunction("ClaimAttachment")]
    public async Task<ClaimAttachmentResponse> ClaimAttachmentAsync(ClaimAttachmentRequest request)
    {
        var playerId = _context.PlayerId ?? string.Empty;
        _logger.LogInformation("ClaimAttachment called for {PlayerId}, mailId={MailId}, mailType={MailType}",
            playerId, request.MailId, request.MailType);
        try
        {
            if (string.IsNullOrWhiteSpace(request.MailId))
                throw new ArgumentException(MailboxError.InvalidInput + ": MailId is required");

            if (request.MailType == "global")
                return await ClaimGlobalAttachmentAsync(playerId, request.MailId);
            else
                return await ClaimUserAttachmentAsync(playerId, request.MailId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClaimAttachment failed for {PlayerId}, mailId={MailId}", playerId, request.MailId);
            throw;
        }
    }

    private async Task<ClaimAttachmentResponse> ClaimGlobalAttachmentAsync(string playerId, string mailId)
    {
        var globalState = await CloudSaveHelper.GetPlayerDataAsync<PlayerGlobalMailState>(
            _gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState) ?? new PlayerGlobalMailState();

        if (globalState.ClaimedIds.Contains(mailId))
            throw new InvalidOperationException(MailboxError.AlreadyClaimed);

        var globalIndex = await CloudSaveHelper.GetCustomDataAsync<GlobalMailIndex>(
            _gameApiClient, _context, MailboxConstants.KeyGlobalMailIndex) ?? new GlobalMailIndex();

        var mail = globalIndex.Mails.Find(m => m.GlobalMailId == mailId)
            ?? throw new InvalidOperationException(MailboxError.MailNotFound);

        if (mail.IsExpired())
            throw new InvalidOperationException(MailboxError.MailExpired);

        if (mail.Attachment == null)
            throw new InvalidOperationException(MailboxError.NoAttachment);

        globalState.ClaimedIds.Add(mailId);
        if (!globalState.ReadIds.Contains(mailId))
            globalState.ReadIds.Add(mailId);

        await CloudSaveHelper.SetPlayerDataAsync(
            _gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState, globalState);

        _logger.LogInformation("Global attachment claimed for mailId={MailId} by {PlayerId}", mailId, playerId);
        return new ClaimAttachmentResponse
        {
            Success = true,
            GrantedAttachment = mail.Attachment
        };
    }

    private async Task<ClaimAttachmentResponse> ClaimUserAttachmentAsync(string playerId, string mailId)
    {
        var mailbox = await CloudSaveHelper.GetPlayerDataAsync<PlayerUserMailbox>(
            _gameApiClient, _context, playerId, MailboxConstants.KeyUserItems) ?? new PlayerUserMailbox();

        var mail = mailbox.Mails.Find(m => m.MailId == mailId)
            ?? throw new InvalidOperationException(MailboxError.MailNotFound);

        if (mail.Claimed)
            throw new InvalidOperationException(MailboxError.AlreadyClaimed);

        if (mail.IsExpired())
            throw new InvalidOperationException(MailboxError.MailExpired);

        if (mail.Attachment == null)
            throw new InvalidOperationException(MailboxError.NoAttachment);

        mail.Claimed = true;
        mail.Read = true;

        await CloudSaveHelper.SetPlayerDataAsync(
            _gameApiClient, _context, playerId, MailboxConstants.KeyUserItems, mailbox);

        _logger.LogInformation("User attachment claimed for mailId={MailId} by {PlayerId}", mailId, playerId);
        return new ClaimAttachmentResponse
        {
            Success = true,
            GrantedAttachment = mail.Attachment
        };
    }
}

public class ClaimAttachmentRequest
{
    public string MailId { get; set; } = string.Empty;
    public string MailType { get; set; } = "user"; // "user" | "global"
}

public class ClaimAttachmentResponse
{
    public bool Success { get; set; }
    public MailAttachment? GrantedAttachment { get; set; }
}
