using System;
using System.Collections.Generic;

namespace BackpackAdventures.CloudCode;

// Cloud Save key constants — single source of truth for all mailbox functions.
public static class MailboxConstants
{
    public const string KeyGlobalMailIndex = "global_mail_index";
    public const string KeyGlobalState = "mailbox_global_state";
    public const string KeyUserItems = "mailbox_user_items";
    public const string KeyMeta = "mailbox_meta";

    public const int MaxPageSize = 50;
    public const int DefaultPageSize = 20;
    public const int MaxTitleLength = 128;
    public const int MaxBodyLength = 1024;
    public const int MaxUserMailsStored = 200;
}

// Attachment descriptor — stored in Cloud Save, never contains economy tokens.
public class MailAttachment
{
    public string Type { get; set; } = "none"; // "currency" | "item" | "none"
    public string ItemId { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

// A single global broadcast mail stored in global_mail_index.
public class GlobalMailItem
{
    public string GlobalMailId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string SentAt { get; set; } = string.Empty;       // ISO-8601 UTC
    public string? ExpiresAt { get; set; }                   // ISO-8601 UTC; null = no expiry
    public MailAttachment? Attachment { get; set; }
    public string? DedupKey { get; set; }

    public bool IsExpired() =>
        ExpiresAt != null &&
        DateTime.TryParse(ExpiresAt, out var exp) &&
        exp < DateTime.UtcNow;
}

// Root document stored under global_mail_index Cloud Save key.
public class GlobalMailIndex
{
    public int Version { get; set; } = 1;
    public List<GlobalMailItem> Mails { get; set; } = new();
}

// Per-player read/claim state for global mails, stored under mailbox_global_state.
public class PlayerGlobalMailState
{
    public int Version { get; set; } = 1;
    public List<string> ClaimedIds { get; set; } = new();
    public List<string> ReadIds { get; set; } = new();
}

// A single user-specific mail stored in mailbox_user_items.
public class UserMailItem
{
    public string MailId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string SentAt { get; set; } = string.Empty;       // ISO-8601 UTC
    public string? ExpiresAt { get; set; }                   // ISO-8601 UTC; null = no expiry
    public bool Read { get; set; }
    public bool Claimed { get; set; }
    public MailAttachment? Attachment { get; set; }
    public string? DedupKey { get; set; }

    public bool IsExpired() =>
        ExpiresAt != null &&
        DateTime.TryParse(ExpiresAt, out var exp) &&
        exp < DateTime.UtcNow;
}

// Root document stored under mailbox_user_items Cloud Save key.
public class PlayerUserMailbox
{
    public int Version { get; set; } = 1;
    public List<UserMailItem> Mails { get; set; } = new();
}

// Lightweight metadata stored under mailbox_meta Cloud Save key.
public class PlayerMailboxMeta
{
    public int Version { get; set; } = 1;
    public string? LastReadAt { get; set; }   // ISO-8601 UTC; marks all mails sent before this as read
    public int TotalUserMails { get; set; }
    public int TotalGlobalMails { get; set; }
}

// DTOs returned to the client — separate from storage models to allow independent evolution.

public class MailItemDto
{
    public string MailId { get; set; } = string.Empty;
    public string MailType { get; set; } = string.Empty;     // "user" | "global"
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string SentAt { get; set; } = string.Empty;
    public string? ExpiresAt { get; set; }
    public bool Read { get; set; }
    public bool Claimed { get; set; }
    public MailAttachment? Attachment { get; set; }
}

public class PagedMailResult
{
    public List<MailItemDto> Mails { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class ClaimResult
{
    public bool Success { get; set; }
    public MailAttachment? GrantedAttachment { get; set; }
}

// Error codes used as exception messages — clients map these to localized UI strings.
public static class MailboxError
{
    public const string InvalidInput = "InvalidInput";
    public const string Unauthorized = "Unauthorized";
    public const string MailNotFound = "MailNotFound";
    public const string MailExpired = "MailExpired";
    public const string AlreadyClaimed = "AlreadyClaimed";
    public const string NoAttachment = "NoAttachment";
    public const string DuplicateMail = "DuplicateMail";
    public const string CannotDeleteGlobal = "CannotDeleteGlobal";
    public const string EconomyUnavailable = "EconomyUnavailable";
}
