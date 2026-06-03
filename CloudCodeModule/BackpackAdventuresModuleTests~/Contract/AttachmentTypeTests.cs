using System.Collections.Generic;
using Xunit;

namespace BackpackAdventures.CloudCode.Tests;

public class AttachmentTypeTests
{
    [Fact]
    public void MapPayouts_PreservesCustomAssetTypeAndChance()
    {
        var payouts = MailSchemaHelper.MapPayouts(new List<MailAttachment>
        {
            new() { ItemId = "bp_ticket", Type = "BattlePass", Quantity = 3, Chance = 0.25 }
        });

        Assert.Single(payouts);
        Assert.Equal("BattlePass", payouts[0].AssetType);
        Assert.Equal(0.25, payouts[0].Chance);
        Assert.Equal(3, payouts[0].PayoutAmount);
    }
}
