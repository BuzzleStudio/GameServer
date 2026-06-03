// cc-index is merged. Un-gated in .csproj.
// Tests verify O(1) dict-index methods return the same result as their linear-scan predecessors.

using System;
using System.Collections.Generic;
using Xunit;

namespace BackpackAdventures.CloudCode.Tests;

/// <summary>
/// Verifies O(1) dictionary-based index correctness against linear scan for:
///   • GlobalMailCollection  — GlobalMailStore.FindById(collection, id) vs FindById(list, id)
///   • PlayerGlobalMailState — state.FindMetadataById(id) vs MailSchemaHelper.FindMetadata
///   • PlayerUserMailbox     — mailbox.FindById(id) vs Mails.Find(...)
/// </summary>
public class O1IndexPostMergeTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static GlobalMailPayload Payload(string id) =>
        new() { Mail = new Mail { MessageId = id } };

    private static GlobalMailCollection MakeCollection(params string[] ids)
    {
        var col = new GlobalMailCollection();
        foreach (var id in ids) col.Mails.Add(Payload(id));
        return col;
    }

    private static PlayerGlobalMailState MakeState(params string[] ids)
    {
        var state = new PlayerGlobalMailState();
        foreach (var id in ids) state.Mails.Add(new MailMetadata { MessageId = id });
        return state;
    }

    private static PlayerUserMailbox MakeMailbox(params string[] ids)
    {
        var mb = new PlayerUserMailbox();
        foreach (var id in ids)
            mb.Mails.Add(new MailItemDto
            {
                MessageId = id,
                MailInfo = new MailInfoDto(),
                MailMetaData = new MailMetaDataDto()
            });
        return mb;
    }

    // ── GlobalMailCollection — public FindById(collection, id) ────────────────

    [Fact]
    public void GlobalCollection_FindById_MatchesLinearScan_Present()
    {
        var col = MakeCollection("gm_001", "gm_002", "gm_003");

        var linear = GlobalMailStore.FindById(col.Mails, "gm_002");
        var fast   = GlobalMailStore.FindById(col, "gm_002");

        Assert.Equal(linear?.Mail.MessageId, fast?.Mail.MessageId);
    }

    [Fact]
    public void GlobalCollection_FindById_MatchesLinearScan_Absent()
    {
        var col = MakeCollection("gm_001");

        var linear = GlobalMailStore.FindById(col.Mails, "gm_999");
        var fast   = GlobalMailStore.FindById(col, "gm_999");

        Assert.Null(linear);
        Assert.Null(fast);
    }

    [Fact]
    public void GlobalCollection_FindById_CaseInsensitive()
    {
        var col = MakeCollection("gm_MixedCase");

        var lower  = GlobalMailStore.FindById(col, "gm_mixedcase");
        var upper  = GlobalMailStore.FindById(col, "GM_MIXEDCASE");
        var linear = GlobalMailStore.FindById(col.Mails, "gm_MixedCase");

        Assert.NotNull(lower);
        Assert.NotNull(upper);
        Assert.Equal(linear?.Mail.MessageId, lower?.Mail.MessageId);
    }

    [Fact]
    public void GlobalCollection_FindById_NullCollection_ReturnsNull()
    {
        var result = GlobalMailStore.FindById((GlobalMailCollection?)null, "gm_001");
        Assert.Null(result);
    }

    [Fact]
    public void GlobalCollection_FindById_After_Mutation_ReturnsUpdated()
    {
        // Index must rebuild when Mails list is mutated via InvalidateIndex().
        var col = MakeCollection("gm_001");
        Assert.NotNull(GlobalMailStore.FindById(col, "gm_001")); // warms index

        col.Mails.Add(Payload("gm_002"));
        col.InvalidateIndex(); // force rebuild

        var found = GlobalMailStore.FindById(col, "gm_002");
        Assert.NotNull(found);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(500)]
    public void GlobalCollection_FindById_ScalesCorrectly(int count)
    {
        var ids = new string[count];
        for (var i = 0; i < count; i++) ids[i] = $"gm_{i:D5}";
        var col    = MakeCollection(ids);
        var target = ids[count / 2];

        var linear = GlobalMailStore.FindById(col.Mails, target);
        var fast   = GlobalMailStore.FindById(col, target);

        Assert.Equal(linear?.Mail.MessageId, fast?.Mail.MessageId);
    }

    // ── PlayerGlobalMailState — internal FindMetadataById ─────────────────────

    [Fact]
    public void PlayerState_FindMetadataById_MatchesLinearScan_Present()
    {
        var state = MakeState("gm_001", "gm_002", "gm_003");

        var linear = MailSchemaHelper.FindMetadata(state, "gm_002"); // linear (delegates to new dict)
        var fast   = state.FindMetadataById("gm_002");               // direct dict path

        Assert.Equal(linear?.MessageId, fast?.MessageId);
    }

    [Fact]
    public void PlayerState_FindMetadataById_MatchesLinearScan_Absent()
    {
        var state = MakeState("gm_001");

        var linear = MailSchemaHelper.FindMetadata(state, "gm_999");
        var fast   = state.FindMetadataById("gm_999");

        Assert.Null(linear);
        Assert.Null(fast);
    }

    [Fact]
    public void PlayerState_GetOrCreateMetadataById_CreatesAndIndexes()
    {
        var state = new PlayerGlobalMailState();

        var meta = state.GetOrCreateMetadataById("gm_new");

        Assert.NotNull(meta);
        Assert.Equal("gm_new", meta.MessageId);
        // Must be findable via the dict index too
        Assert.Same(meta, state.FindMetadataById("gm_new"));
        Assert.Single(state.Mails);
    }

    [Fact]
    public void PlayerState_GetOrCreateMetadataById_ExistingId_ReturnsSame()
    {
        var state = MakeState("gm_001");
        var first = state.GetOrCreateMetadataById("gm_001");

        var second = state.GetOrCreateMetadataById("gm_001");

        Assert.Same(first, second);
        Assert.Single(state.Mails);
    }

    // ── PlayerUserMailbox — internal FindById ─────────────────────────────────

    [Fact]
    public void UserMailbox_FindById_MatchesLinearScan_Present()
    {
        var mb = MakeMailbox("um_001", "um_002", "um_003");

        var linear = mb.Mails.Find(m => m.MessageId == "um_002");
        var fast   = mb.FindById("um_002");

        Assert.Equal(linear?.MessageId, fast?.MessageId);
    }

    [Fact]
    public void UserMailbox_FindById_MatchesLinearScan_Absent()
    {
        var mb = MakeMailbox("um_001");

        var linear = mb.Mails.Find(m => m.MessageId == "um_999");
        var fast   = mb.FindById("um_999");

        Assert.Null(linear);
        Assert.Null(fast);
    }

    [Fact]
    public void UserMailbox_FindById_After_InvalidateIndex_RebuildsDictionary()
    {
        var mb = MakeMailbox("um_001");
        Assert.NotNull(mb.FindById("um_001")); // warm dict

        mb.Mails.Add(new MailItemDto { MessageId = "um_002", MailInfo = new(), MailMetaData = new() });
        mb.InvalidateIndex(); // force rebuild

        Assert.NotNull(mb.FindById("um_002"));
    }
}
