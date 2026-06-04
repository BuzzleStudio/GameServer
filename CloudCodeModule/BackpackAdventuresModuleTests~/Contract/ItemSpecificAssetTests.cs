using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace BackpackAdventures.CloudCode.Tests;

public class ItemSpecificAssetTests
{
    // ── Serialize / Parse round-trip ──────────────────────────────────────────

    [Fact]
    public void ItemSpecificAsset_SerializeParseRoundTrip_AllFieldsPreserved()
    {
        var original = new ItemSpecificAsset
        {
            BlueprintId   = "bp_sword_01",
            CurrentLevel  = 7,
            Rarity        = Rarity.Epic,
            InitialLevel  = 3,
            FromSource    = "dungeon_boss"
        };

        var json   = MailSchemaHelper.SerializeItemAssets(new List<ItemSpecificAsset> { original });
        var parsed = MailSchemaHelper.ParseItemAssets(json);

        Assert.Single(parsed);
        var item = parsed[0];
        Assert.Equal("bp_sword_01", item.BlueprintId);
        Assert.Equal(7,             item.CurrentLevel);
        Assert.Equal(Rarity.Epic,   item.Rarity);
        Assert.Equal(3,             item.InitialLevel);
        Assert.Equal("dungeon_boss", item.FromSource);
    }

    // ── Rarity enum emits int in JSON ─────────────────────────────────────────

    [Theory]
    [InlineData(Rarity.None,      0)]
    [InlineData(Rarity.Common,    1)]
    [InlineData(Rarity.Rare,      2)]
    [InlineData(Rarity.Epic,      3)]
    [InlineData(Rarity.Legendary, 4)]
    [InlineData(Rarity.Mythic,    5)]
    public void Rarity_SerializesAsInt(Rarity rarity, int expectedInt)
    {
        var asset = new ItemSpecificAsset { Rarity = rarity };
        var json  = JsonSerializer.Serialize(asset);
        Assert.Contains($"\"Rarity\":{expectedInt}", json);
    }

    // ── Item PayoutAssetId survives full mapper chain ─────────────────────────

    [Fact]
    public void ItemPayoutAssetId_SurvivesMapPayouts_MapAttachmentDtos_ToAttachments()
    {
        var items   = new List<ItemSpecificAsset>
        {
            new() { BlueprintId = "bp_helm", CurrentLevel = 5, Rarity = Rarity.Legendary, InitialLevel = 1, FromSource = "pvp" }
        };
        var jsonArray = MailSchemaHelper.SerializeItemAssets(items);

        // Hop 1: MailAttachment -> Payout
        var payouts = MailSchemaHelper.MapPayouts(new List<MailAttachment>
        {
            new() { ItemId = jsonArray, Type = "item", Quantity = 1, Chance = 1.0 }
        });
        Assert.Single(payouts);
        Assert.Equal(jsonArray, payouts[0].PayoutAssetId);

        // Hop 2: Payout -> MailAttachmentDto
        var dtos = MailSchemaHelper.MapAttachmentDtos(payouts);
        Assert.NotNull(dtos);
        Assert.Single(dtos!);
        Assert.Equal(jsonArray, dtos[0].PayoutAssetId);

        // Hop 3: MailAttachmentDto -> MailAttachment
        var result = MailSchemaHelper.ToAttachments(dtos);
        Assert.NotNull(result);
        Assert.Single(result!);
        Assert.Equal(jsonArray, result[0].ItemId);

        // Parsed at end still yields the original item
        var reparsed = MailSchemaHelper.ParseItemAssets(result[0].ItemId);
        Assert.Single(reparsed);
        Assert.Equal("bp_helm",        reparsed[0].BlueprintId);
        Assert.Equal(Rarity.Legendary, reparsed[0].Rarity);
    }

    // ── Currency PayoutAssetId stays plain string ─────────────────────────────

    [Fact]
    public void CurrencyPayoutAssetId_StaysPlainString_ThroughMappers()
    {
        const string plainId = "mine_1";

        var payouts = MailSchemaHelper.MapPayouts(new List<MailAttachment>
        {
            new() { ItemId = plainId, Type = "currency", Quantity = 25, Chance = 1.0 }
        });
        Assert.Equal(plainId, payouts[0].PayoutAssetId);

        var dtos = MailSchemaHelper.MapAttachmentDtos(payouts);
        Assert.Equal(plainId, dtos![0].PayoutAssetId);

        var result = MailSchemaHelper.ToAttachments(dtos);
        Assert.Equal(plainId, result![0].ItemId);
    }

    // ── Defaults when fields omitted ──────────────────────────────────────────

    [Fact]
    public void ItemSpecificAsset_Defaults_WhenFieldsOmitted()
    {
        var asset = new ItemSpecificAsset();

        Assert.Equal("",          asset.BlueprintId);
        Assert.Equal(1,           asset.CurrentLevel);
        Assert.Equal(Rarity.None, asset.Rarity);
        Assert.Equal(1,           asset.InitialLevel);
        Assert.Equal("",          asset.FromSource);
    }

    [Fact]
    public void ParseItemAssets_SingleItem_Defaults_WhenFieldsOmitted()
    {
        // JSON missing all optional fields
        const string json = "[{}]";
        var items = MailSchemaHelper.ParseItemAssets(json);

        Assert.Single(items);
        Assert.Equal("",          items[0].BlueprintId);
        Assert.Equal(1,           items[0].CurrentLevel);
        Assert.Equal(Rarity.None, items[0].Rarity);
        Assert.Equal(1,           items[0].InitialLevel);
        Assert.Equal("",          items[0].FromSource);
    }

    // ── Legacy tolerance ──────────────────────────────────────────────────────

    [Fact]
    public void ParseItemAssets_LegacyPlainString_ReturnsEmptyList_NoThrow()
    {
        // Old item PayoutAssetId was a plain id string — must not throw
        var result = MailSchemaHelper.ParseItemAssets("epic_sword");
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseItemAssets_NullOrEmpty_ReturnsEmptyList(string? input)
    {
        var result = MailSchemaHelper.ParseItemAssets(input);
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void SerializeItemAssets_EmptyList_ReturnsEmptyJsonArray()
    {
        var result = MailSchemaHelper.SerializeItemAssets(new List<ItemSpecificAsset>());
        Assert.Equal("[]", result);
    }
}
