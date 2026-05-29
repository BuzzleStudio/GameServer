using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

/// <summary>
/// Returns the global mail list for the calling player with pagination.
/// Reads v2 index first; falls back to legacy v1 global_mail_index if v2 is absent (§5.1 compat layer).
/// </summary>
public class GetGlobalMailsModule
{
    private readonly IExecutionContext _context;
    private readonly IGameApiClient _gameApiClient;
    private readonly ILogger<GetGlobalMailsModule> _logger;

    public GetGlobalMailsModule(
        IExecutionContext context,
        IGameApiClient gameApiClient,
        ILogger<GetGlobalMailsModule> logger)
    {
        _context = context;
        _gameApiClient = gameApiClient;
        _logger = logger;
    }

    [CloudCodeFunction("GetGlobalMails")]
    public async Task<PagedMailResponse> GetGlobalMailsAsync(GetMailsRequest request)
    {
        var playerId = _context.PlayerId ?? string.Empty;
        _logger.LogInformation("GetGlobalMails called for {PlayerId}, page={Page}, pageSize={PageSize}",
            playerId, request.Page, request.PageSize);

        if (request.Page < 0 || request.PageSize > MailboxConstants.MaxPageSize)
            throw new ArgumentException(MailboxError.InvalidInput);

        var pageSize = request.PageSize <= 0 ? MailboxConstants.DefaultPageSize : request.PageSize;

        // Load player global state (claimed / read IDs) in parallel with index read
        var stateTask = CloudSaveHelper.GetPlayerDataAsync<PlayerGlobalMailState>(
            _gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState);
        var v2IndexTask = CloudSaveHelper.GetCustomDataAsync<GlobalMailIndexV2>(
            _gameApiClient, _context, MailboxConstants.KeyGlobalMailIndexV2);

        await Task.WhenAll(stateTask, v2IndexTask);

        var state = stateTask.Result ?? new PlayerGlobalMailState();
        var claimedIds = new HashSet<string>(state.ClaimedIds);
        var readIds    = new HashSet<string>(state.ReadIds);

        var v2Index = v2IndexTask.Result;

        List<MailItemDto> allMails;

        if (v2Index != null && v2Index.Refs.Count > 0)
        {
            allMails = await BuildDtosFromV2Async(v2Index, claimedIds, readIds);
        }
        else
        {
            // §5.1 Legacy compat read — v2 index absent, fall back to old global_mail_index (read-only)
            _logger.LogInformation("GetGlobalMails: v2 index absent — using v1 legacy fallback for {PlayerId}", playerId);
            allMails = await BuildDtosFromV1LegacyAsync(claimedIds, readIds);
        }

        // Sort newest first
        allMails.Sort((a, b) => string.Compare(b.SentAt, a.SentAt, StringComparison.Ordinal));

        var totalCount = allMails.Count;
        var startIdx   = request.Page * pageSize;
        var slice      = new List<MailItemDto>();

        for (var i = startIdx; i < Math.Min(startIdx + pageSize, totalCount); i++)
            slice.Add(allMails[i]);

        _logger.LogInformation("GetGlobalMails returning {Count}/{Total} for {PlayerId}", slice.Count, totalCount, playerId);
        return new PagedMailResponse
        {
            Mails      = slice,
            TotalCount = totalCount,
            Page       = request.Page,
            PageSize   = pageSize,
            HasMore    = (startIdx + pageSize) < totalCount
        };
    }

    private async Task<List<MailItemDto>> BuildDtosFromV2Async(
        GlobalMailIndexV2 v2Index,
        HashSet<string> claimedIds,
        HashSet<string> readIds)
    {
        var dtos = new List<MailItemDto>();
        var payloadTasks = new List<Task<GlobalMailPayload?>>();
        var validRefs    = new List<GlobalMailRef>();

        foreach (var r in v2Index.Refs)
        {
            if (r.IsExpired()) continue;
            validRefs.Add(r);
            payloadTasks.Add(CloudSaveHelper.GetCustomDataAsync<GlobalMailPayload>(
                _gameApiClient, _context,
                string.Format(MailboxConstants.KeyGlobalMailPayloadFmt, r.MailId)));
        }

        await Task.WhenAll(payloadTasks);

        for (var i = 0; i < validRefs.Count; i++)
        {
            var r       = validRefs[i];
            var payload = payloadTasks[i].Result;
            if (payload == null) continue; // payload key missing — orphaned ref, skip

            dtos.Add(new MailItemDto
            {
                MailId            = r.MailId,
                Subject           = payload.Subject,
                Body              = payload.Body,
                SentAt            = payload.SentAt,
                ExpiresAt         = payload.ExpiresAt,
                IsRead            = readIds.Contains(r.MailId),
                AttachmentClaimed = claimedIds.Contains(r.MailId),
                MailType          = payload.MailType,
                MailCategory      = payload.MailCategory,
                SenderType        = payload.SenderType,
                Sender            = payload.Sender,
                Attachments       = payload.Attachments
            });
        }

        return dtos;
    }

    private async Task<List<MailItemDto>> BuildDtosFromV1LegacyAsync(
        HashSet<string> claimedIds,
        HashSet<string> readIds)
    {
        var v1Index = await CloudSaveHelper.GetCustomDataAsync<GlobalMailIndex>(
            _gameApiClient, _context, MailboxConstants.KeyGlobalMailIndex);
        if (v1Index == null) return new List<MailItemDto>();

        var dtos = new List<MailItemDto>();
        foreach (var m in v1Index.Mails)
        {
            if (m.IsExpired()) continue;
            dtos.Add(new MailItemDto
            {
                MailId            = m.GlobalMailId,
                Subject           = m.Subject,
                Body              = m.Body,
                SentAt            = m.SentAt,
                ExpiresAt         = m.ExpiresAt,
                IsRead            = readIds.Contains(m.GlobalMailId),
                AttachmentClaimed = claimedIds.Contains(m.GlobalMailId),
                MailType          = MailType.Notification,
                MailCategory      = MailCategory.System,
                SenderType        = SenderType.System,
                Attachments       = m.Attachments
            });
        }
        return dtos;
    }
}

