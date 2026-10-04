namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Request to import the candidates an operator kept from a discovered batch.
    /// </summary>
    public class VesselImportRequest
    {
        #region Public-Members

        /// <summary>
        /// Batch identifier (vib_ prefix) returned by discover. Never null.
        /// </summary>
        public string BatchId
        {
            get => _BatchId;
            set => _BatchId = value ?? String.Empty;
        }

        /// <summary>
        /// Candidate paths to import, exactly as returned in the batch items. Candidates not listed are recorded as
        /// skipped. Never null.
        /// </summary>
        public List<string> Paths
        {
            get => _Paths;
            set => _Paths = value ?? new List<string>();
        }

        /// <summary>
        /// Fleet to assign the created vessels to, or null for none.
        /// </summary>
        public string? FleetId { get; set; } = null;

        /// <summary>
        /// Optional settings applied to every created vessel.
        /// </summary>
        public VesselImportDefaults? Defaults { get; set; } = null;

        /// <summary>
        /// Optional fleet categorization to run after the vessels are created, or null for none.
        /// </summary>
        public VesselImportCategorizationRequest? Categorization { get; set; } = null;

        #endregion

        #region Private-Members

        private string _BatchId = String.Empty;
        private List<string> _Paths = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselImportRequest()
        {
        }

        #endregion
    }
}
