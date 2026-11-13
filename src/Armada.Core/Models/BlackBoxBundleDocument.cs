namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.BlackBox;
    using Armada.Core.Services;

    /// <summary>
    /// The JSON maintainer bundle (BlackBoxBundleResult.Content when Format is Json).
    /// </summary>
    public class BlackBoxBundleDocument
    {
        /// <summary>
        /// Instructions for the agent that reads the bundle.
        /// </summary>
        public string Preamble { get; set; } = "";

        /// <summary>
        /// Entries in the bundle.
        /// </summary>
        public int EntryCount { get; set; } = 0;

        /// <summary>
        /// Failure modes in the bundle.
        /// </summary>
        public int FingerprintCount { get; set; } = 0;

        /// <summary>
        /// Groups, most frequent first.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxBundleEntries, Reason = "At most one group per bundled entry")]
        public List<BlackBoxBundleGroup> Groups
        {
            get => _Groups;
            set => _Groups = value ?? new List<BlackBoxBundleGroup>();
        }

        private List<BlackBoxBundleGroup> _Groups = new List<BlackBoxBundleGroup>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxBundleDocument()
        {
        }
    }
}
