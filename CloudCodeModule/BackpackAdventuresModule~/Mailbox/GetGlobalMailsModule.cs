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
    public async Task<PagedMailResponse> GetGlobalMailsAsync(GetMailsRequest request)
    {
        var playerId = _context.PlayerId ?? string.Empty;
        if (request.Page < 0 || request.PageSize > MailboxConstants.MaxPageSize)
            throw new ArgumentException(MailboxError.InvalidInput);

        var pageSize = request.PageSize <= 0 ? MailboxConstants.DefaultPageSize : request.PageSize;
        var stateTask = CloudSaveHelper.GetPlayerDataAsync<PlayerGlobalMailState>(_gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState);
        var indexTask = CloudSaveHelper.GetCustomDataAsync<GlobalMailIndexV2>(_gameApiClient, _context, MailboxConstants.KeyGlobalMailIndexV2);
        await Task.WhenAll(stateTask, indexTask);

        var state = stateTask.Result ?? new PlayerGlobalMailState();
        MailSchemaHelper.MigrateLegacyMetadata(state);
        var index = indexTask.Result;

        List<MailItemDto> allMails;
        if (index != null && index.Refs.Count > 0)
            allMails = await BuildDtosFromV2Async(index, state, playerId);
        else
            allMails = await BuildDtosFromV1LegacyAsync(state);

        allMails.Sort((a, b) => string.Compare(b.MailInfo.StartTime, a.MailInfo.StartTime, StringComparison.Ordinal));

        var totalCount = allMails.Count;
        var startIdx = request.Page * pageSize;
        var slice = new List<MailItemDto>();
        for (var i = startIdx; i < Math.Min(startIdx + pageSize, totalCount); i++)
            slice.Add(allMails[i]);

        return new PagedMailResponse
        {
            Mails = slice,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = pageSize,
            HasMore = (startIdx + pageSize) < totalCount
        };
    }

    private async Task<List<MailItemDto>> BuildDtosFromV2Async(GlobalMailIndexV2 index, PlayerGlobalMailState state, string playerId)
    {
        var dtos = new List<MailItemDto>();
        var payloadTasks = new List<Task<GlobalMailPayload?>>();
        var validRefs = new List<GlobalMailRef>();

        foreach (var reference in index.Refs)
        {
            if (reference.IsExpired()) continue;
            validRefs.Add(reference);
            payloadTasks.Add(CloudSaveHelper.GetCustomDataAsync<GlobalMailPayload>(
                _gameApiClient,
                _context,
                string.Format(MailboxConstants.KeyGlobalMailPayloadFmt, reference.MessageId)));
        }

        await Task.WhenAll(payloadTasks);

        for (var i = 0; i < validRefs.Count; i++)
        {
            var payload = payloadTasks[i].Result;
            if (payload?.Mail == null) continue;
            if (!payload.Mail.IsAvailable) continue;
            if (!MailSchemaHelper.IsVisibleToPlayer(payload.Mail, playerId)) continue;
            var metadata = MailSchemaHelper.FindMetadata(state, validRefs[i].MessageId);
            if (metadata?.IsDelete == true) continue;
            dtos.Add(MailSchemaHelper.ToMailItemDto(payload.Mail, metadata));
        }

        return dtos;
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
