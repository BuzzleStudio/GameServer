using System;
using System.Collections.Generic;

namespace BackpackAdventures.CloudCode;

public static class MailboxConstants
{
    public const string KeyGlobalMailIndex = "global_mail_index";
    public const string KeyGlobalState = "mailbox_global_state";
    public const string KeyUserItems = "mailbox_user_items";

    public const int MaxSubjectLength = 128;
    public const int MaxBodyLength = 1024;
    public const int MaxUserMailsStored = 200;
}

// Attachment descriptor — field names match the Unity client MailAttachment model.
public class MailAttachment
{
    public string Type { get; set; } = "none"; // "currency" | "item" | "none"
    public string Id { get; set; } = string.Empty;
    public int Amount { get; set; }
}

// Cloud Save storage models

public class GlobalMailItem
{
    public string GlobalMailId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string SentAt { get; set; } = string.Empty;
    public string? ExpiresAt { get; set; }
    public List<MailAttachment>? Attachments { get; set; }

    public bool IsExpired() =>
        ExpiresAt != null &&
        DateTime.TryParse(ExpiresAt, out var exp) &&
        exp < DateTime.UtcNow;
}

public class GlobalMailIndex
{
    public int Version { get; set; } = 1;
    public List<GlobalMailItem> Mails { get; set; } = new();
}

public class PlayerGlobalMailState
{
    public int Version { get; set; } = 1;
    public List<string> ClaimedIds { get; set; } = new();
    public List<string> ReadIds { get; set; } = new();
}

public class UserMailItem
{
    public string MailId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string SentAt { get; set; } = string.Empty;
    public string? ExpiresAt { get; set; }
    public bool IsRead { get; set; }
    public bool AttachmentClaimed { get; set; }
    public List<MailAttachment>? Attachments { get; set; }

    public bool IsExpired() =>
        ExpiresAt != null &&
        DateTime.TryParse(ExpiresAt, out var exp) &&
        exp < DateTime.UtcNow;
}

public class PlayerUserMailbox
{
    public int Version { get; set; } = 1;
    public List<UserMailItem> Mails { get; set; } = new();
}

// API DTOs — PascalCase properties serialize to camelCase by the UGS SDK,
// matching the Unity client model field names exactly.

public class MailItemDto
{
    public string MailId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public bool AttachmentClaimed { get; set; }
    public string SentAt { get; set; } = string.Empty;
    public string? ExpiresAt { get; set; }
    public List<MailAttachment>? Attachments { get; set; }
}

// API Request types

public class SendGlobalMailRequest
{
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? ExpiresAt { get; set; }
    public List<MailAttachment>? Attachments { get; set; }
}

public class SendUserMailRequest
{
    public string UserId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? ExpiresAt { get; set; }
    public List<MailAttachment>? Attachments { get; set; }
}

public class MarkMailReadRequest
{
    public string MailId { get; set; } = string.Empty;
}

public class ClaimAttachmentRequest
{
    public string MailId { get; set; } = string.Empty;
}

// API Response types

public class SendGlobalMailResponse
{
    public bool Success { get; set; }
    public string MailId { get; set; } = string.Empty;
    public string SentAt { get; set; } = string.Empty;
}

public class SendUserMailResponse
{
    public bool Success { get; set; }
    public string MailId { get; set; } = string.Empty;
    public string SentAt { get; set; } = string.Empty;
}

public class GetMailboxResponse
{
    public bool Success { get; set; }
    public List<MailItemDto> Mails { get; set; } = new();
}

public class MarkMailReadResponse
{
    public bool Success { get; set; }
    public string MailId { get; set; } = string.Empty;
    public bool IsRead { get; set; }
}

public class ClaimAttachmentResponse
{
    public bool Success { get; set; }
    public string MailId { get; set; } = string.Empty;
    public bool AlreadyClaimed { get; set; }
    public List<MailAttachment>? ClaimedAttachments { get; set; }
}

// Error code strings — clients map these to localized UI strings.
public static class MailboxError
{
    public const string InvalidInput = "InvalidInput";
    public const string MailNotFound = "MailNotFound";
    public const string MailExpired = "MailExpired";
    public const string AlreadyClaimed = "AlreadyClaimed";
    public const string NoAttachment = "NoAttachment";
}
