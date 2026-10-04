namespace Armada.Client.Socket
{
    using System;

    /// <summary>
    /// Payload of mission.changed, voyage.changed, captain.changed, deployment.changed, objective.changed, and incident.changed.
    /// </summary>
    public class EntityChangedEvent
    {
        #region Public-Members

        /// <summary>
        /// Entity id.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Title (missions, voyages, deployments, objectives, incidents).
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Name (captains).
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Status name.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// State name (captains).
        /// </summary>
        public string? State { get; set; } = null;

        /// <summary>
        /// Voyage id (missions).
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Verification status (deployments).
        /// </summary>
        public string? VerificationStatus { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public EntityChangedEvent()
        {
        }

        #endregion
    }
}
