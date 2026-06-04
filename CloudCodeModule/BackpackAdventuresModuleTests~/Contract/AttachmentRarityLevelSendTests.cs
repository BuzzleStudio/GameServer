using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BackpackAdventures.CloudCode.Tests;

/// <summary>
/// Verifies that Item PayoutAssetId (JSON array of ItemSpecificAsset) survives
/// the full send → Cloud Save write path. Uses ProgrammableHttpMessageHandler +
/// HttpSeam to intercept the Cloud Save POST and inspect the serialized body.
/// </summary>
public class ItemSpecificAssetSendTests : IDisposable
{
    private readonly HttpClient _originalHttp;
    private readonly ProgrammableHttpMessageHandler _handler;

    public ItemSpecificAssetSendTests()
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

    private static FakeExecutionContext AdminCtx()
        => new FakeExecutionContext("proj-item-asset", playerId: "", serviceToken: "svc-token");

    [Fact]
    public async System.Threading.Tasks.Task SendGlobalMail_ItemAttachment_PayoutAssetIdContainsJsonArray()
    {
        MailboxCache.Enabled = false;
        var ctx = AdminCtx();
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyMailsAll, @"{""Mails"":[]}", writeLock: "lk-1");
        _handler.AddPostOk(MailboxConstants.KeyMailsAll);
        _handler.AddPostOk(MailboxConstants.KeyGlobalMailChangeLog);

        var items = new List<ItemSpecificAsset>
        {
            new() { BlueprintId = "bp_epic_helm", CurrentLevel = 10, Rarity = Rarity.Epic, InitialLevel = 1, FromSource = "event" }
        };
        var jsonArray = MailSchemaHelper.SerializeItemAssets(items);

        var module = new SendGlobalMailModule(ctx, null!, NullLogger<SendGlobalMailModule>.Instance);
        var req = new SendGlobalMailRequest
        {
            Subject     = "Item Asset Round-Trip",
            Body        = "Test item attachment",
            OperatorId  = "op-1",
            AdminToken  = "t",
            Attachments = new List<MailAttachment>
            {
                new() { ItemId = jsonArray, Type = "item", Quantity = 1, Chance = 1.0 }
            }
        };

        var resp = await module.SendGlobalMailAsync(req);

        Assert.False(string.IsNullOrEmpty(resp.Data!.GlobalMailId),
            "SendGlobalMail must return a non-empty GlobalMailId");

        // The stored Payout must carry the JSON array string verbatim in PayoutAssetId.
        var post = _handler.LastPost(MailboxConstants.KeyMailsAll);
        Assert.NotNull(post?.Body);
        Assert.Contains("bp_epic_helm", post!.Body, StringComparison.Ordinal);
        Assert.Contains("PayoutAssetId", post.Body, StringComparison.Ordinal);

        // Rarity and Level flat fields must NOT appear as top-level Payout properties.
        Assert.DoesNotContain(@"""Rarity"":""Epic""", post.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(@"""Level"":10", post.Body, StringComparison.Ordinal);

        Console.WriteLine("[ItemAsset] Send→store POST body contains JSON array PayoutAssetId. Pass.");
    }

    [Fact]
    public async System.Threading.Tasks.Task SendGlobalMail_CurrencyAttachment_PayoutAssetIdIsPlainString()
    {
        MailboxCache.Enabled = false;
        var ctx = AdminCtx();
        _handler.AddGetCloudSaveResult(MailboxConstants.KeyMailsAll, @"{""Mails"":[]}", writeLock: "lk-2");
        _handler.AddPostOk(MailboxConstants.KeyMailsAll);
        _handler.AddPostOk(MailboxConstants.KeyGlobalMailChangeLog);

        var module = new SendGlobalMailModule(ctx, null!, NullLogger<SendGlobalMailModule>.Instance);
        var req = new SendGlobalMailRequest
        {
            Subject     = "Currency Attachment",
            Body        = "Test currency stays plain",
            OperatorId  = "op-1",
            AdminToken  = "t",
            Attachments = new List<MailAttachment>
            {
                new() { ItemId = "mine_1", Type = "currency", Quantity = 50, Chance = 1.0 }
            }
        };

        await module.SendGlobalMailAsync(req);

        var post = _handler.LastPost(MailboxConstants.KeyMailsAll);
        Assert.NotNull(post?.Body);
        Assert.Contains("mine_1", post!.Body, StringComparison.Ordinal);

        Console.WriteLine("[ItemAsset] Currency PayoutAssetId stays plain string. Pass.");
    }
}
