using System;
using System.Collections.Generic;
using Xunit;

namespace BackpackAdventures.CloudCode.Tests;

/// <summary>
/// Verifies that O(1) dict-index lookups return identical results to the existing
/// linear-scan path (GlobalMailStore.FindById).
///
/// These tests compile and pass TODAY against the current code.  After cc-index
/// is merged, O1IndexPostMergeTests.cs will also run and verify the new fast paths.
/// </summary>
public class O1IndexCurrentTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static GlobalMailPayload Payload(string id)
    {
        return new GlobalMailPayload
        {
            Mail = new Mail { MessageId = id, Title = $"Mail {id}" }
        };
    }

    private static GlobalMailCollection MakeCollection(params string[] ids)
    {
        var col = new GlobalMailCollection();
        foreach (var id in ids)
            col.Mails.Add(Payload(id));
        return col;
    }

    private static PlayerGlobalMailState MakePlayerState(params string[] ids)
    {
        var state = new PlayerGlobalMailState();
        foreach (var id in ids)
            state.Mails.Add(new MailMetadata { MessageId = id });
        return state;
    }

    private static PlayerUserMailbox MakeUserMailbox(params string[] ids)
    {
        var mb = new PlayerUserMailbox();
        foreach (var id in ids)
            mb.Mails.Add(new MailItemDto
            {
                MessageId = id,
                MailInfo = new MailInfoDto { Title = $"UserMail {id}" },
                MailMetaData = new MailMetaDataDto()
            });
        return mb;
    }

    // ── GlobalMailStore.FindById ──────────────────────────────────────────────

    [Fact]
    public void FindById_PresentKey_ReturnsMatchingPayload()
    {
        var col = MakeCollection("gm_001", "gm_002", "gm_003");

        var result = GlobalMailStore.FindById(col.Mails, "gm_002");

        Assert.NotNull(result);
        Assert.Equal("gm_002", result!.Mail.MessageId);
    }

    [Fact]
    public void FindById_AbsentKey_ReturnsNull()
    {
        var col = MakeCollection("gm_001", "gm_002");

        var result = GlobalMailStore.FindById(col.Mails, "gm_999");

        Assert.Null(result);
    }

    [Fact]
    public void FindById_CaseInsensitive_ReturnsMatch()
    {
        var col = MakeCollection("gm_UPPER");

        var lower = GlobalMailStore.FindById(col.Mails, "gm_upper");
        var mixed = GlobalMailStore.FindById(col.Mails, "GM_Upper");

        Assert.NotNull(lower);
        Assert.NotNull(mixed);
    }

    [Fact]
    public void FindById_EmptyList_ReturnsNull()
    {
        var result = GlobalMailStore.FindById(new List<GlobalMailPayload>(), "gm_001");
        Assert.Null(result);
    }

    [Fact]
    public void FindById_NullList_ReturnsNull()
    {
        var result = GlobalMailStore.FindById((List<GlobalMailPayload>?)null, "gm_001");
        Assert.Null(result);
    }

    [Fact]
    public void FindById_ReturnsFirstMatchOnly()
    {
        // Duplicate IDs are pathological but must not throw
        var mails = new List<GlobalMailPayload> { Payload("gm_dup"), Payload("gm_dup") };
        var result = GlobalMailStore.FindById(mails, "gm_dup");
        Assert.NotNull(result);
    }

    // ── MailSchemaHelper.FindMetadata ─────────────────────────────────────────

    [Fact]
    public void FindMetadata_PresentId_ReturnsMetadata()
    {
        var state = MakePlayerState("gm_001", "gm_002", "gm_003");

        var meta = MailSchemaHelper.FindMetadata(state, "gm_002");

        Assert.NotNull(meta);
        Assert.Equal("gm_002", meta!.MessageId);
    }

    [Fact]
    public void FindMetadata_AbsentId_ReturnsNull()
    {
        var state = MakePlayerState("gm_001");

        var meta = MailSchemaHelper.FindMetadata(state, "gm_999");

        Assert.Null(meta);
    }

    [Fact]
    public void GetOrCreateMetadata_NewId_CreatesAndAdds()
    {
        var state = new PlayerGlobalMailState();

        var meta = MailSchemaHelper.GetOrCreateMetadata(state, "gm_new");

        Assert.NotNull(meta);
        Assert.Equal("gm_new", meta.MessageId);
        Assert.Contains(state.Mails, m => m.MessageId == "gm_new");
    }

    [Fact]
    public void GetOrCreateMetadata_ExistingId_ReturnsSameInstance()
    {
        var state = MakePlayerState("gm_001");
        var first = MailSchemaHelper.GetOrCreateMetadata(state, "gm_001");

        var second = MailSchemaHelper.GetOrCreateMetadata(state, "gm_001");

        Assert.Same(first, second);
        Assert.Single(state.Mails); // not duplicated
    }

    // ── UserMailbox linear scan (baseline before cc-index O(1) path) ──────────

    [Fact]
    public void UserMailbox_LinearFind_PresentId_ReturnsMatch()
    {
        var mb = MakeUserMailbox("um_001", "um_002", "um_003");

        var mail = mb.Mails.Find(m => m.MessageId == "um_002");

        Assert.NotNull(mail);
        Assert.Equal("um_002", mail!.MessageId);
    }

    [Fact]
    public void UserMailbox_LinearFind_AbsentId_ReturnsNull()
    {
        var mb = MakeUserMailbox("um_001");

        var mail = mb.Mails.Find(m => m.MessageId == "um_999");

        Assert.Null(mail);
    }

    // ── LiveIds helper ────────────────────────────────────────────────────────

    [Fact]
    public void LiveIds_CollectsNonEmptyMessageIds()
    {
        var mails = new List<GlobalMailPayload>
        {
            Payload("gm_001"),
            Payload("gm_002"),
            new() { Mail = new Mail { MessageId = string.Empty } }  // should be skipped
        };

        var ids = GlobalMailStore.LiveIds(mails);

        Assert.Equal(2, ids.Count);
        Assert.Contains("gm_001", ids);
        Assert.Contains("gm_002", ids);
    }
}
