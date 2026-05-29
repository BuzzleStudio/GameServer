// FakeClock.cs
// Deterministic, controllable clock for hermetic mailbox tests.
// Replaces DateTime.UtcNow so expiry filtering and gift-quota-day logic are reproducible.

using System;

namespace BackpackAdventures.CloudCode.Client.Tests
{
    public interface IMailboxClock
    {
        DateTimeOffset UtcNow { get; }
    }

    public class FakeClock : IMailboxClock
    {
        private DateTimeOffset _offset;

        public FakeClock(DateTimeOffset startTime)
        {
            this._offset = startTime;
        }

        public DateTimeOffset UtcNow => this._offset;

        public void Advance(TimeSpan delta) => this._offset = this._offset.Add(delta);

        public void AdvanceDays(int days) => this._offset = this._offset.Add(TimeSpan.FromDays(days));
    }
}
