namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.BlackBox;

    /// <summary>
    /// State a dispatch advisor keeps across the missions of one voyage creation (RS-10.4): the vessel context is read
    /// once per vessel and each advised mission is added to it, instead of reading active missions, hotspots and tracked
    /// files again for every mission. Create one per voyage creation and discard it afterwards. Not thread-safe.
    /// </summary>
    public sealed class MissionDispatchBatch
    {
        /// <summary>
        /// Number of vessel contexts the batch has loaded.
        /// </summary>
        public int LoadedContexts => Contexts.Count;

        internal Dictionary<string, VesselOverlapContext> Contexts { get; } = new Dictionary<string, VesselOverlapContext>(StringComparer.Ordinal);

        internal HashSet<string> AddedMissionIds { get; } = new HashSet<string>(StringComparer.Ordinal);
    }
}
