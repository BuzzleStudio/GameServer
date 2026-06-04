// Disable parallel test execution across classes.
// MailboxCache and ClaimAllRoundTrip tests share a process-wide static (MailboxCache.Enabled).
// Running in parallel causes races where one class sets Enabled=false while another is reading.
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
