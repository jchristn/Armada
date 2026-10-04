namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Response to a discover request: the persisted batch (status Discovered) and its candidate items, or, for a
    /// background discovery, the batch in status Discovering and the job identifier.
    /// </summary>
    public class VesselImportDiscoverResponse
    {
        #region Public-Members

        /// <summary>
        /// Identifier of the persisted batch (vib_ prefix). Never null.
        /// </summary>
        public string BatchId
        {
            get => _BatchId;
            set => _BatchId = value ?? String.Empty;
        }

        /// <summary>
        /// Background discovery job identifier when discovery runs in the background, otherwise null.
        /// </summary>
        public string? JobId { get; set; } = null;

        /// <summary>
        /// True when discovery runs as a background job (HTTP 202) and candidates are not available yet.
        /// </summary>
        public bool RunsInBackground { get; set; } = false;

        /// <summary>
        /// The persisted batch, or null.
        /// </summary>
        public VesselImportBatch? Batch { get; set; } = null;

        /// <summary>
        /// Persisted candidate items in discovery order. Never null.
        /// </summary>
        public List<VesselImportItem> Candidates
        {
            get => _Candidates;
            set => _Candidates = value ?? new List<VesselImportItem>();
        }

        /// <summary>
        /// True when discovery stopped at the candidate cap and more candidates may exist.
        /// </summary>
        public bool Truncated { get; set; } = false;

        /// <summary>
        /// Advisory hints. Never null.
        /// </summary>
        public List<VesselImportHint> Hints
        {
            get => _Hints;
            set => _Hints = value ?? new List<VesselImportHint>();
        }

        #endregion

        #region Private-Members

        private string _BatchId = String.Empty;
        private List<VesselImportItem> _Candidates = new List<VesselImportItem>();
        private List<VesselImportHint> _Hints = new List<VesselImportHint>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselImportDiscoverResponse()
        {
        }

        #endregion
    }
}
