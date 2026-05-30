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
        var mailsTask = CloudSaveHelper.GetCustomDataAsync<GlobalMailCollection>(_gameApiClient, _context, MailboxConstants.KeyMailsAll);
        await Task.WhenAll(stateTask, mailsTask);

        var state = stateTask.Result ?? new PlayerGlobalMailState();
        MailSchemaHelper.MigrateLegacyMetadata(state);
        var mails = mailsTask.Result?.Mails ?? new List<GlobalMailPayload>();

        var allMails = mails.Count > 0
            ? BuildDtosFromAllMails(mails, state, playerId)
            : await BuildDtosFromV1LegacyAsync(state);

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

    private static List<MailItemDto> BuildDtosFromAllMails(List<GlobalMailPayload> mails, PlayerGlobalMailState state, string playerId)
    {
        var dtos = new List<MailItemDto>();
        foreach (var payload in mails)
        {
            if (payload?.Mail == null) continue;
            if (!payload.Mail.IsAvailable) continue;
            if (!MailSchemaHelper.IsVisibleToPlayer(payload.Mail, playerId)) continue;
            var metadata = MailSchemaHelper.FindMetadata(state, payload.Mail.MessageId);
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
