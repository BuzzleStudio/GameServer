using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace BackpackAdventures.CloudCode;

/// <summary>
/// Implements the §5.7 eviction policy for <see cref="PlayerUserMailbox"/>.
/// All rules are applied in priority order before a new mail is inserted.
/// NEVER evicts a non-expired, unclaimed reward mail unless the hard cap is hit.
/// </summary>
internal static class MailboxEviction
{
    /// <summary>
    /// Runs the §5.7 eviction rules against <paramref name="mailbox"/> to make room for one new mail.
    /// Throws <see cref="InvalidOperationException"/> with <see cref="MailboxError.MailboxFull"/>
    /// when the hard cap is reached and no safe eviction is possible.
    /// </summary>
    internal static void Evict(
        PlayerUserMailbox mailbox,
        string targetPlayerId,
        ILogger logger)
    {
        var now = DateTime.UtcNow;

        // Priority 1: expired AND (claimed OR notification-only)
        mailbox.Mails.RemoveAll(m =>
            m.IsExpired() && (m.AttachmentClaimed || m.Attachments == null || m.Attachments.Count == 0));

        // Priority 2: expired AND read (already handled above if claimed, but isRead without attachment also safe)
        mailbox.Mails.RemoveAll(m => m.IsExpired() && m.IsRead);

        // Priority 3: remaining expired (any — attachment unclaimed but expired by admin intent)
        foreach (var expired in FindAll(mailbox.Mails, m => m.IsExpired()))
        {
            if (expired.Attachments != null && expired.Attachments.Count > 0 && !expired.AttachmentClaimed)
            {
                logger.LogWarning(
                    "Evicting unclaimed expired reward mail {MailId} for {PlayerId}",
                    expired.MailId, targetPlayerId);
            }
        }
        mailbox.Mails.RemoveAll(m => m.IsExpired());

        // If within soft cap after expired cleanup, no further action needed.
        if (mailbox.Mails.Count < MailboxConstants.MaxUserMailsStored)
            return;

        // Priority 5: oldest non-expired with claimed attachment
        var oldestClaimed = OldestWhere(mailbox.Mails, m => m.AttachmentClaimed);
        if (oldestClaimed != null)
        {
            mailbox.Mails.Remove(oldestClaimed);
            return;
        }

        // Priority 6: oldest non-expired notification-only mail
        var oldestNotification = OldestWhere(mailbox.Mails, m => m.Attachments == null || m.Attachments.Count == 0);
        if (oldestNotification != null)
        {
            mailbox.Mails.Remove(oldestNotification);
            return;
        }

        // Priority 7: hard cap check — all remaining mails are unclaimed rewards
        if (mailbox.Mails.Count >= MailboxConstants.HardCapUserMailsStored)
        {
            logger.LogError(
                "Mailbox hard cap ({Cap}) reached for player {PlayerId} — all mails are unclaimed rewards. Rejecting insert.",
                MailboxConstants.HardCapUserMailsStored, targetPlayerId);
            throw new InvalidOperationException(MailboxError.MailboxFull);
        }
    }

    private static List<UserMailItem> FindAll(List<UserMailItem> mails, Predicate<UserMailItem> match)
    {
        var result = new List<UserMailItem>();
        foreach (var m in mails)
        {
            if (match(m)) result.Add(m);
        }
        return result;
    }

    private static UserMailItem? OldestWhere(List<UserMailItem> mails, Func<UserMailItem, bool> predicate)
    {
        UserMailItem? oldest = null;
        foreach (var m in mails)
        {
            if (!predicate(m)) continue;
            if (oldest == null ||
                string.Compare(m.SentAt, oldest.SentAt, StringComparison.Ordinal) < 0)
            {
                oldest = m;
            }
        }
        return oldest;
    }
}
