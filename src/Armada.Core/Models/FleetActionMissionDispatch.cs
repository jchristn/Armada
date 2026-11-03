namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// One Mission fleet action target to dispatch: the rendered prompt and how the voyage runs (pipeline, persona,
    /// approval snapshot, mission mode, and landing mode).
    /// </summary>
    public class FleetActionMissionDispatch
    {
        #region Public-Members

        /// <summary>
        /// Target vessel.
        /// </summary>
        public Vessel Vessel
        {
            get => _Vessel;
            set => _Vessel = value ?? throw new ArgumentNullException(nameof(Vessel));
        }

        /// <summary>
        /// Voyage and mission title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Rendered prompt, used as the mission description.
        /// </summary>
        public string Prompt { get; set; } = String.Empty;

        /// <summary>
        /// Optional pipeline identifier.
        /// </summary>
        public string? PipelineId { get; set; } = null;

        /// <summary>
        /// Optional persona name.
        /// </summary>
        public string? Persona { get; set; } = null;

        /// <summary>
        /// The run's approval snapshot, or null.
        /// </summary>
        public RunApprovalOptions? ApprovalOptions { get; set; } = null;

        /// <summary>
        /// Mission mode; Audit and Research are read-only report missions.
        /// </summary>
        public MissionModeEnum MissionMode { get; set; } = MissionModeEnum.Implementation;

        /// <summary>
        /// Voyage landing mode, or null for the vessel's own.
        /// </summary>
        public LandingModeEnum? LandingMode { get; set; } = null;

        #endregion

        #region Private-Members

        private Vessel _Vessel;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="vessel">Target vessel.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="vessel"/> is null.</exception>
        public FleetActionMissionDispatch(Vessel vessel)
        {
            _Vessel = vessel ?? throw new ArgumentNullException(nameof(vessel));
        }

        #endregion
    }
}
