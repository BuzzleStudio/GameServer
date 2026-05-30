using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace BackpackAdventures.CloudCode;

internal static class MailboxEviction
{
    internal static void Evict(PlayerUserMailbox mailbox, string targetPlayerId, ILogger logger)
    {
        mailbox.Mails.RemoveAll(m =>
            m.IsExpired() &&
            (m.MailMetaData.IsClaimed || !MailSchemaHelper.HasAttachments(m)));

        mailbox.Mails.RemoveAll(m => m.IsExpired() && m.MailMetaData.IsRead);

        foreach (var expired in FindAll(mailbox.Mails, m => m.IsExpired()))
        {
            if (MailSchemaHelper.HasAttachments(expired) && !expired.MailMetaData.IsClaimed)
            {
                logger.LogWarning("Evicting unclaimed expired reward mail {MailId} for {PlayerId}", expired.MessageId, targetPlayerId);
            }
        }
        mailbox.Mails.RemoveAll(m => m.IsExpired());

        if (mailbox.Mails.Count < MailboxConstants.MaxUserMailsStored)
            return;

        var oldestClaimed = OldestWhere(mailbox.Mails, m => m.MailMetaData.IsClaimed);
        if (oldestClaimed != null)
        {
            mailbox.Mails.Remove(oldestClaimed);
            return;
        }

        var oldestNotification = OldestWhere(mailbox.Mails, m => !MailSchemaHelper.HasAttachments(m));
        if (oldestNotification != null)
        {
            mailbox.Mails.Remove(oldestNotification);
            return;
        }

        if (mailbox.Mails.Count >= MailboxConstants.HardCapUserMailsStored)
            throw new InvalidOperationException(MailboxError.MailboxFull);
    }

    private static List<MailItemDto> FindAll(List<MailItemDto> mails, Predicate<MailItemDto> match)
    {
        var result = new List<MailItemDto>();
        foreach (var mail in mails)
            if (match(mail)) result.Add(mail);
        return result;
    }

    private static MailItemDto? OldestWhere(List<MailItemDto> mails, Func<MailItemDto, bool> predicate)
    {
        MailItemDto? oldest = null;
        foreach (var mail in mails)
        {
            if (!predicate(mail)) continue;
            if (oldest == null || string.Compare(mail.MailInfo.StartTime, oldest.MailInfo.StartTime, StringComparison.Ordinal) < 0)
                oldest = mail;
        }
        return oldest;
    }
}
