namespace Armada.Core.Services
{
    using System.Collections.Generic;

    /// <summary>Capacity totals the invariant monitor refreshes each tick for the observable gauges.</summary>
    internal sealed class ConcurrencyGaugeSnapshot
    {
        /// <summary>Assigned or running missions by runtime name.</summary>
        internal Dictionary<string, int> ActiveByRuntime { get; set; } = new Dictionary<string, int>();

        /// <summary>Occupied captain mission slots by runtime name.</summary>
        internal Dictionary<string, int> SlotsInUseByRuntime { get; set; } = new Dictionary<string, int>();

        /// <summary>Configured captain mission slots by runtime name.</summary>
        internal Dictionary<string, int> SlotsTotalByRuntime { get; set; } = new Dictionary<string, int>();

        /// <summary>Voyage slots held on vessels with a voyage limit.</summary>
        internal int VoyageSlotsInUse { get; set; }

        /// <summary>Sum of voyage limits on vessels with a limit.</summary>
        internal int VoyageSlotsTotal { get; set; }

        /// <summary>Vessels with no voyage limit.</summary>
        internal int UnlimitedVessels { get; set; }
    }
}
