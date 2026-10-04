namespace Armada.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Output of the discovery engine: classified candidates, whether the candidate cap truncated the scan, and
    /// advisory hints.
    /// </summary>
    public class VesselDiscoveryResult
    {
        #region Public-Members

        /// <summary>
        /// Candidates in discovery order. Never null.
        /// </summary>
        public List<VesselImportCandidate> Candidates
        {
            get => _Candidates;
            set => _Candidates = value ?? new List<VesselImportCandidate>();
        }

        /// <summary>
        /// True when discovery stopped at the candidate cap and more candidates may exist.
        /// </summary>
        public bool Truncated { get; set; } = false;

        /// <summary>
        /// Advisory hints, for example PathNotVisibleToAdmiral. Never null.
        /// </summary>
        public List<VesselImportHint> Hints
        {
            get => _Hints;
            set => _Hints = value ?? new List<VesselImportHint>();
        }

        #endregion

        #region Private-Members

        private List<VesselImportCandidate> _Candidates = new List<VesselImportCandidate>();
        private List<VesselImportHint> _Hints = new List<VesselImportHint>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselDiscoveryResult()
        {
        }

        #endregion
    }
}
