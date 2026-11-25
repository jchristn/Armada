namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// One battery task to dispatch to a captain on a fixture vessel.
    /// </summary>
    public class BatteryDispatchRequest
    {
        /// <summary>
        /// Fixture vessel.
        /// </summary>
        public Vessel Vessel { get; }

        /// <summary>
        /// Captain that must run the mission.
        /// </summary>
        public Captain Captain { get; }

        /// <summary>
        /// Mission title.
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// Mission description (the task prompt with its attachments).
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Persona the mission runs as.
        /// </summary>
        public string Persona { get; }

        /// <summary>
        /// How long to wait for the mission to finish, in milliseconds.
        /// </summary>
        public int TimeoutMs { get; }

        /// <summary>
        /// Persona prompt variant the mission renders with, or null to use the captain's own setting. Dispatch records
        /// it on the voyage's captain override.
        /// </summary>
        public PromptVariantEnum? PromptVariant { get; set; } = null;

        /// <summary>
        /// Pipeline to dispatch under, or null for the built-in ModelEvaluation pipeline with <see cref="Captain"/>.
        /// </summary>
        public string? Pipeline { get; set; } = null;

        /// <summary>
        /// Per-persona captains used with <see cref="Pipeline"/>; ignored without it.
        /// </summary>
        public List<CaptainAssignmentOverride>? CaptainOverrides { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="vessel">Fixture vessel.</param>
        /// <param name="captain">Captain.</param>
        /// <param name="title">Mission title.</param>
        /// <param name="description">Mission description.</param>
        /// <param name="persona">Persona.</param>
        /// <param name="timeoutMs">Timeout in milliseconds.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BatteryDispatchRequest(Vessel vessel, Captain captain, string title, string description, string persona, int timeoutMs)
        {
            Vessel = vessel ?? throw new ArgumentNullException(nameof(vessel));
            Captain = captain ?? throw new ArgumentNullException(nameof(captain));
            Title = title ?? throw new ArgumentNullException(nameof(title));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            Persona = persona ?? throw new ArgumentNullException(nameof(persona));
            TimeoutMs = timeoutMs;
        }
    }
}
