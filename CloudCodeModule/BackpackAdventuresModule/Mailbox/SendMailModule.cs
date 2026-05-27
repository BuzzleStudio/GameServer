using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

public class SendMailModule
{
    private readonly IExecutionContext _context;
    private readonly IGameApiClient _gameApiClient;
    private readonly ILogger<SendMailModule> _logger;

    public SendMailModule(IExecutionContext context, IGameApiClient gameApiClient, ILogger<SendMailModule> logger)
    {
        _context = context;
        _gameApiClient = gameApiClient;
        _logger = logger;
    }

    [CloudCodeFunction("SendGlobalMail")]
    public async Task<SendGlobalMailResponse> SendGlobalMailAsync(SendGlobalMailRequest request)
    {
        _logger.LogInformation("SendGlobalMail called by {PlayerId}", _context.PlayerId);
        ValidateRequest(request.Subject, request.Body);

        var index = await GetCustomDataAsync<GlobalMailIndex>(MailboxConstants.KeyGlobalMailIndex)
                    ?? new GlobalMailIndex();

        var sentAt = DateTime.UtcNow.ToString("o");
        var mail = new GlobalMailItem
        {
            GlobalMailId = Guid.NewGuid().ToString(),
            Subject = request.Subject,
            Body = request.Body,
            SentAt = sentAt,
            ExpiresAt = request.ExpiresAt,
            Attachments = request.Attachments
        };

        index.Mails.Add(mail);
        await SetCustomDataAsync(MailboxConstants.KeyGlobalMailIndex, index);

        _logger.LogInformation("Global mail {MailId} stored", mail.GlobalMailId);
        return new SendGlobalMailResponse { Success = true, MailId = mail.GlobalMailId, SentAt = sentAt };
    }

    [CloudCodeFunction("SendUserMail")]
    public async Task<SendUserMailResponse> SendUserMailAsync(SendUserMailRequest request)
    {
        _logger.LogInformation("SendUserMail to {UserId} by {PlayerId}", request.UserId, _context.PlayerId);

        if (string.IsNullOrWhiteSpace(request.UserId))
            throw new ArgumentException(MailboxError.InvalidInput);
        ValidateRequest(request.Subject, request.Body);

        var mailbox = await GetPlayerDataAsync<PlayerUserMailbox>(request.UserId, MailboxConstants.KeyUserItems)
                      ?? new PlayerUserMailbox();

        var sentAt = DateTime.UtcNow.ToString("o");
        var mail = new UserMailItem
        {
            MailId = Guid.NewGuid().ToString(),
            Subject = request.Subject,
            Body = request.Body,
            SentAt = sentAt,
            ExpiresAt = request.ExpiresAt,
            Attachments = request.Attachments
        };

        if (mailbox.Mails.Count >= MailboxConstants.MaxUserMailsStored)
            mailbox.Mails.RemoveAt(0);

        mailbox.Mails.Add(mail);
        await SetPlayerDataAsync(request.UserId, MailboxConstants.KeyUserItems, mailbox);

        _logger.LogInformation("User mail {MailId} stored for {UserId}", mail.MailId, request.UserId);
        return new SendUserMailResponse { Success = true, MailId = mail.MailId, SentAt = sentAt };
    }

    private static void ValidateRequest(string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > MailboxConstants.MaxSubjectLength)
            throw new ArgumentException(MailboxError.InvalidInput);
        if (string.IsNullOrWhiteSpace(body) || body.Length > MailboxConstants.MaxBodyLength)
            throw new ArgumentException(MailboxError.InvalidInput);
    }

    private async Task<T?> GetCustomDataAsync<T>(string key)
    {
        try
        {
            var response = await _gameApiClient.CloudSaveData.GetCustomItemsAsync(
                _context, _context.AccessToken, _context.ProjectId, new List<string> { key });
            if (response.Data.Results.Count == 0) return default;
            var raw = response.Data.Results[0].Value?.ToString();
            return string.IsNullOrEmpty(raw) ? default : JsonSerializer.Deserialize<T>(raw);
        }
        catch { return default; }
    }

    private async Task SetCustomDataAsync<T>(string key, T value)
    {
        var json = JsonSerializer.Serialize(value);
        var item = new Unity.Services.CloudCode.Apis.CloudSaveData.Model.SetItemBody(key, json);
        await _gameApiClient.CloudSaveData.SetCustomItemAsync(
            _context, _context.AccessToken, _context.ProjectId, item);
    }

    private async Task<T?> GetPlayerDataAsync<T>(string playerId, string key)
    {
        try
        {
            var response = await _gameApiClient.CloudSaveData.GetItemsAsync(
                _context, _context.AccessToken, _context.ProjectId, playerId, new List<string> { key });
            if (response.Data.Results.Count == 0) return default;
            var raw = response.Data.Results[0].Value?.ToString();
            return string.IsNullOrEmpty(raw) ? default : JsonSerializer.Deserialize<T>(raw);
        }
        catch { return default; }
    }

    private async Task SetPlayerDataAsync<T>(string playerId, string key, T value)
    {
        var json = JsonSerializer.Serialize(value);
        var item = new Unity.Services.CloudCode.Apis.CloudSaveData.Model.SetItemBody(key, json);
        await _gameApiClient.CloudSaveData.SetItemAsync(
            _context, _context.AccessToken, _context.ProjectId, playerId, item);
    }
}
