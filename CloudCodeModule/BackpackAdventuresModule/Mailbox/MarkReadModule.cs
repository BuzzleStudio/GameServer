using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

public class MarkReadModule
{
    private readonly IExecutionContext _context;
    private readonly IGameApiClient _gameApiClient;
    private readonly ILogger<MarkReadModule> _logger;

    public MarkReadModule(IExecutionContext context, IGameApiClient gameApiClient, ILogger<MarkReadModule> logger)
    {
        _context = context;
        _gameApiClient = gameApiClient;
        _logger = logger;
    }

    /// <summary>Marks one or more mails as read for the calling player. Idempotent — re-marking is a no-op.</summary>
    [CloudCodeFunction("MarkMailRead")]
    public async Task<MarkReadResponse> MarkMailReadAsync(MarkReadRequest request)
    {
        var playerId = _context.PlayerId ?? string.Empty;
        _logger.LogInformation("MarkMailRead called for {PlayerId}, {Count} mail(s)", playerId, request.MailIds?.Count ?? 0);
        try
        {
            if (request.MailIds == null || request.MailIds.Count == 0)
                throw new ArgumentException(MailboxError.InvalidInput + ": MailIds must not be empty");

            // Separate by type so we update the right store for each
            var globalIds = new HashSet<string>();
            var userIds = new HashSet<string>();

            foreach (var entry in request.MailIds)
            {
                if (entry.MailType == "global") globalIds.Add(entry.MailId);
                else userIds.Add(entry.MailId);
            }

            var tasks = new List<Task>();

            if (globalIds.Count > 0)
                tasks.Add(MarkGlobalReadAsync(playerId, globalIds));

            if (userIds.Count > 0)
                tasks.Add(MarkUserReadAsync(playerId, userIds));

            await Task.WhenAll(tasks);

            _logger.LogInformation("Marked {Count} mail(s) read for {PlayerId}", request.MailIds.Count, playerId);
            return new MarkReadResponse { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MarkMailRead failed for {PlayerId}", playerId);
            throw;
        }
    }

    private async Task MarkGlobalReadAsync(string playerId, HashSet<string> ids)
    {
        var state = await CloudSaveHelper.GetPlayerDataAsync<PlayerGlobalMailState>(
            _gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState) ?? new PlayerGlobalMailState();

        var readSet = new HashSet<string>(state.ReadIds);
        foreach (var id in ids) readSet.Add(id);
        state.ReadIds = new List<string>(readSet);

        await CloudSaveHelper.SetPlayerDataAsync(
            _gameApiClient, _context, playerId, MailboxConstants.KeyGlobalState, state);
    }

    private async Task MarkUserReadAsync(string playerId, HashSet<string> ids)
    {
        var mailbox = await CloudSaveHelper.GetPlayerDataAsync<PlayerUserMailbox>(
            _gameApiClient, _context, playerId, MailboxConstants.KeyUserItems) ?? new PlayerUserMailbox();

        var changed = false;
        foreach (var mail in mailbox.Mails)
        {
            if (ids.Contains(mail.MailId) && !mail.Read)
            {
                mail.Read = true;
                changed = true;
            }
        }

        if (changed)
        {
            await CloudSaveHelper.SetPlayerDataAsync(
                _gameApiClient, _context, playerId, MailboxConstants.KeyUserItems, mailbox);
        }
    }
}

public class MarkReadEntry
{
    public string MailId { get; set; } = string.Empty;
    public string MailType { get; set; } = "user"; // "user" | "global"
}

public class MarkReadRequest
{
    public List<MarkReadEntry> MailIds { get; set; } = new();
}

public class MarkReadResponse
{
    public bool Success { get; set; }
}
