namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>Receipt for synthetic activity writes. Existing records were skipped without modification.</summary>
    public class SyntheticActivityResult
    {
        /// <summary>Generated batch identifier.</summary>
        public string BatchId { get; set; } = "";
        /// <summary>Disabled screenshot Harbor with its own jobs, slots, latency and token series.</summary>
        public string HarborId { get; set; } = "";
        /// <summary>Screenshot vessel used by mission filters.</summary>
        public string VesselId { get; set; } = "";
        /// <summary>Successful inserts per record type.</summary>
        [CollectionBound(8, Reason = "One entry per record kind SyntheticActivityGenerator writes (fleets, vessels, harbors, missions, events, requests, signals, tokens)")]
        public Dictionary<string, int> Created { get; set; } = new Dictionary<string, int>();
        /// <summary>Existing synthetic records skipped during replay.</summary>
        public int Existing { get; set; }
    }
}
