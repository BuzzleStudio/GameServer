using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

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
    public async Task<ApiResponse<PagedMailResponse>> GetGlobalMailsAsync(GetMailsRequest request)
    {
        var playerId = _context.PlayerId ?? string.Empty;
        if (request.Page < 0 || request.PageSize > MailboxConstants.MaxPageSize)
            throw new ArgumentException(MailboxError.InvalidInput);

        var pageSize = request.PageSize <= 0 ? MailboxConstants.DefaultPageSize : request.PageSize;
        var stateTask = CloudSaveHelper.GetPlayerDataAsync<PlayerGlobalMailState>(_gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState);
        var mailsTask = CloudSaveHelper.GetCustomDataAsync<GlobalMailCollection>(_gameApiClient, _context, MailboxConstants.KeyMailsAll);
        await Task.WhenAll(stateTask, mailsTask);

        var state = stateTask.Result ?? new PlayerGlobalMailState();
        MailSchemaHelper.MigrateLegacyMetadata(state);
        var mails = mailsTask.Result?.Mails ?? new List<GlobalMailPayload>();

        var startIdx = request.Page * pageSize;

        if (mails.Count == 0)
        {
            // V1 legacy fallback (rare/deprecated): build all, sort, then paginate.
            var legacy = await BuildDtosFromV1LegacyAsync(state);
            legacy.Sort((a, b) => string.Compare(b.MailInfo.StartTime, a.MailInfo.StartTime, StringComparison.Ordinal));
            return Paginate(legacy, request.Page, pageSize, startIdx);
        }

        // V2: filter (cheap checks + O(1) metadata) → sort payloads → slice → build DTOs for the
        // page ONLY. Sort key is identical to ToMailItemDto's MailInfo.StartTime (UTC round-trip
        // "o"), so ordering matches building+sorting full DTOs — but we allocate ≤pageSize DTOs
        // instead of one per mail in the whole collection (up to MaxGlobalMailsStored).
        var visible = new List<(GlobalMailPayload Payload, MailMetadata? Meta, string SortKey)>();
        foreach (var payload in mails)
        {
            if (payload?.Mail == null) continue;
            if (!payload.Mail.IsAvailable) continue;
            if (!MailSchemaHelper.IsVisibleToPlayer(payload.Mail, playerId)) continue;
            var metadata = MailSchemaHelper.FindMetadata(state, payload.Mail.MessageId);
            if (metadata?.IsDelete == true) continue;
            visible.Add((payload, metadata, payload.Mail.StartTime.ToUniversalTime().ToString("o")));
        }

        visible.Sort((a, b) => string.Compare(b.SortKey, a.SortKey, StringComparison.Ordinal));

        var totalCount = visible.Count;
        var slice = new List<MailItemDto>();
        for (var i = startIdx; i < Math.Min(startIdx + pageSize, totalCount); i++)
            slice.Add(MailSchemaHelper.ToMailItemDto(visible[i].Payload.Mail, visible[i].Meta));

        return ApiResponse<PagedMailResponse>.Ok(new PagedMailResponse
        {
            Mails = slice,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = pageSize,
            HasMore = (startIdx + pageSize) < totalCount
        });
    }

    private static ApiResponse<PagedMailResponse> Paginate(List<MailItemDto> all, int page, int pageSize, int startIdx)
    {
        var totalCount = all.Count;
        var slice = new List<MailItemDto>();
        for (var i = startIdx; i < Math.Min(startIdx + pageSize, totalCount); i++)
            slice.Add(all[i]);

        return ApiResponse<PagedMailResponse>.Ok(new PagedMailResponse
        {
            Mails = slice,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            HasMore = (startIdx + pageSize) < totalCount
        });
    }

    private async Task<List<MailItemDto>> BuildDtosFromV1LegacyAsync(PlayerGlobalMailState state)
    {
        var v1Index = await CloudSaveHelper.GetCustomDataAsync<GlobalMailIndex>(_gameApiClient, _context, MailboxConstants.KeyGlobalMailIndex);
        if (v1Index == null) return new List<MailItemDto>();

        var dtos = new List<MailItemDto>();
        foreach (var mail in v1Index.Mails)
        {
            var metadata = MailSchemaHelper.FindMetadata(state, mail.GlobalMailId);
            if (metadata?.IsDelete == true) continue;
            if (mail.IsExpired()) continue;
            dtos.Add(MailSchemaHelper.FromLegacyGlobalMail(mail, metadata?.IsRead ?? false, metadata?.IsClaim ?? false));
        }
        return dtos;
    }
}
