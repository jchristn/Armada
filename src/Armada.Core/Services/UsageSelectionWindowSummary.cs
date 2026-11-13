namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>Accumulated per-candidate window evaluation used by <see cref="UsageSelectionPolicy"/>.</summary>
    internal sealed class UsageSelectionWindowSummary
    {
        internal double? Available { get; set; }
        internal bool Exhausted { get; set; }
        internal bool Unknown { get; set; }
        internal string UnknownReason { get; set; } = UsageSelectionReasonCodes.NoApplicableWindows;
        internal DateTime? NextReset { get; private set; }
        internal bool UnknownReset { get; private set; }
        internal List<string> ComparableKeys { get; } = new List<string>();

        internal void MarkUnknown(string reason)
        {
            Unknown = true;
            UnknownReason = reason;
        }

        // Latest known reset across contributing windows; any window without a reset makes the time unreliable.
        internal void RecordReset(DateTime? resetUtc)
        {
            if (!resetUtc.HasValue) UnknownReset = true;
            else if (!NextReset.HasValue || resetUtc > NextReset) NextReset = resetUtc;
        }
    }
}
