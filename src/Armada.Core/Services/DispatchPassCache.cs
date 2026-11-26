namespace Armada.Core.Services
{
    using Armada.Core.Models;

    /// <summary>
    /// What one dispatch pass read from the database, shared by every pending mission the pass evaluates (RS-4.7).
    /// State a claim can change (captains, running missions, slot counts, voyages) is dropped after each claim, so a
    /// later mission in the same pass never admits against counts from before it. Reference data (vessels, personas,
    /// endpoints, profiles) is kept for the whole pass.
    /// </summary>
    internal sealed class DispatchPassCache
    {
        internal List<Captain>? Captains;
        internal int? ExecutionSlots;
        internal readonly Dictionary<string, List<Mission>> RunningByVessel = new Dictionary<string, List<Mission>>(StringComparer.Ordinal);
        internal readonly Dictionary<string, List<MissionAdmissionSnapshot>> AdmissionByVessel = new Dictionary<string, List<MissionAdmissionSnapshot>>(StringComparer.Ordinal);
        internal readonly Dictionary<string, Voyage?> Voyages = new Dictionary<string, Voyage?>(StringComparer.Ordinal);
        internal readonly Dictionary<string, Vessel?> Vessels = new Dictionary<string, Vessel?>(StringComparer.Ordinal);
        internal readonly Dictionary<string, Persona?> Personas = new Dictionary<string, Persona?>(StringComparer.Ordinal);
        internal readonly Dictionary<string, ModelEndpoint?> Endpoints = new Dictionary<string, ModelEndpoint?>(StringComparer.Ordinal);
        internal readonly Dictionary<string, ModelProfile?> Profiles = new Dictionary<string, ModelProfile?>(StringComparer.Ordinal);

        /// <summary>The capacity model built from Captains, ExecutionSlots and Endpoints (RS-4.8).</summary>
        internal CapacityModel? Capacity;

        /// <summary>The service's claim generation this cache reflects; a newer one means another caller claimed.</summary>
        internal long Generation;

        /// <summary>Database reads this pass made through the cache, for tests and the dispatch log.</summary>
        internal int Reads;

        /// <summary>Drop everything a claim, a rollback or a mission leaving Pending can change.</summary>
        internal void AfterStateChange()
        {
            Captains = null;
            ExecutionSlots = null;
            Capacity = null;
            RunningByVessel.Clear();
            AdmissionByVessel.Clear();
            Voyages.Clear();
        }
    }
}
