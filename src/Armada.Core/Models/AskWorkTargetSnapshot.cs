namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// One target row of a fleet action run work card.
    /// </summary>
    public class AskWorkTargetSnapshot
    {
        #region Public-Members

        /// <summary>
        /// Target identifier (fat_ prefix).
        /// </summary>
        public string Id
        {
            get => _Id;
            set => _Id = value ?? String.Empty;
        }

        /// <summary>
        /// Target vessel.
        /// </summary>
        public string VesselId
        {
            get => _VesselId;
            set => _VesselId = value ?? String.Empty;
        }

        /// <summary>
        /// Target vessel name.
        /// </summary>
        public string VesselName
        {
            get => _VesselName;
            set => _VesselName = value ?? String.Empty;
        }

        /// <summary>
        /// Target status.
        /// </summary>
        public string Status
        {
            get => _Status;
            set => _Status = value ?? String.Empty;
        }

        /// <summary>
        /// Skip or failure reason, or null.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Voyage created for a Mission-kind target, or null.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// First mission of the target's voyage, or null.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Command exit code, or null.
        /// </summary>
        public int? ExitCode { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Id = String.Empty;
        private string _VesselId = String.Empty;
        private string _VesselName = String.Empty;
        private string _Status = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskWorkTargetSnapshot()
        {
        }

        #endregion
    }
}
