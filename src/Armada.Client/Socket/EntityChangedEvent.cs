namespace Armada.Client.Socket
{
    using System;
    using System.Text.Json.Serialization;
    using Armada.Core;
    using Armada.Core.Enums;

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

        /// <summary>
        /// Target environment name (deployments), or null when the deployment has none.
        /// </summary>
        public string? EnvironmentName { get; set; } = null;

        /// <summary>
        /// <see cref="Status"/> as a mission status (mission.changed), or null when absent or not a defined name.
        /// </summary>
        [JsonIgnore]
        public MissionStatusEnum? MissionStatus
        {
            get { return EnumNames.ParseOrNull<MissionStatusEnum>(Status); }
        }

        /// <summary>
        /// <see cref="Status"/> as a voyage status (voyage.changed), or null when absent or not a defined name.
        /// </summary>
        [JsonIgnore]
        public VoyageStatusEnum? VoyageStatus
        {
            get { return EnumNames.ParseOrNull<VoyageStatusEnum>(Status); }
        }

        /// <summary>
        /// <see cref="State"/> (else <see cref="Status"/>) as a captain state (captain.changed), or null when absent or
        /// not a defined name.
        /// </summary>
        [JsonIgnore]
        public CaptainStateEnum? CaptainState
        {
            get { return EnumNames.ParseOrNull<CaptainStateEnum>(!String.IsNullOrEmpty(State) ? State : Status); }
        }

        /// <summary>
        /// <see cref="Status"/> as a deployment status (deployment.changed), or null when absent or not a defined name.
        /// </summary>
        [JsonIgnore]
        public DeploymentStatusEnum? DeploymentStatus
        {
            get { return EnumNames.ParseOrNull<DeploymentStatusEnum>(Status); }
        }

        /// <summary>
        /// <see cref="VerificationStatus"/> as a deployment verification status, or null when absent or not a defined name.
        /// </summary>
        [JsonIgnore]
        public DeploymentVerificationStatusEnum? DeploymentVerificationStatus
        {
            get { return EnumNames.ParseOrNull<DeploymentVerificationStatusEnum>(VerificationStatus); }
        }

        /// <summary>
        /// <see cref="Status"/> as an objective status (objective.changed), or null when absent or not a defined name.
        /// </summary>
        [JsonIgnore]
        public ObjectiveStatusEnum? ObjectiveStatus
        {
            get { return EnumNames.ParseOrNull<ObjectiveStatusEnum>(Status); }
        }

        /// <summary>
        /// <see cref="Status"/> as an incident status (incident.changed), or null when absent or not a defined name.
        /// </summary>
        [JsonIgnore]
        public IncidentStatusEnum? IncidentStatus
        {
            get { return EnumNames.ParseOrNull<IncidentStatusEnum>(Status); }
        }

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
