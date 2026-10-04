namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// The values a fleet action template is rendered against for one target vessel. Every value is plain text and
    /// is substituted verbatim (no escaping, no re-expansion).
    /// </summary>
    public class FleetActionTemplateContext
    {
        #region Public-Members

        /// <summary>
        /// Value of {{vessel.name}}. Never null.
        /// </summary>
        public string VesselName
        {
            get => _VesselName;
            set => _VesselName = value ?? String.Empty;
        }

        /// <summary>
        /// Value of {{vessel.id}}. Never null.
        /// </summary>
        public string VesselId
        {
            get => _VesselId;
            set => _VesselId = value ?? String.Empty;
        }

        /// <summary>
        /// Value of {{vessel.defaultBranch}}. Never null.
        /// </summary>
        public string DefaultBranch
        {
            get => _DefaultBranch;
            set => _DefaultBranch = value ?? String.Empty;
        }

        /// <summary>
        /// Value of {{vessel.workingDirectory}}. Never null; empty when the vessel has no working directory.
        /// </summary>
        public string WorkingDirectory
        {
            get => _WorkingDirectory;
            set => _WorkingDirectory = value ?? String.Empty;
        }

        /// <summary>
        /// Value of {{vessel.buildCommand}} (the vessel's definition-of-done build command). Never null; empty when unset.
        /// </summary>
        public string BuildCommand
        {
            get => _BuildCommand;
            set => _BuildCommand = value ?? String.Empty;
        }

        /// <summary>
        /// Value of {{health.summary}}. Never null.
        /// </summary>
        public string HealthSummary
        {
            get => _HealthSummary;
            set => _HealthSummary = value ?? String.Empty;
        }

        #endregion

        #region Private-Members

        private string _VesselName = String.Empty;
        private string _VesselId = String.Empty;
        private string _DefaultBranch = String.Empty;
        private string _WorkingDirectory = String.Empty;
        private string _BuildCommand = String.Empty;
        private string _HealthSummary = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetActionTemplateContext()
        {
        }

        /// <summary>
        /// Build a context from a vessel. <see cref="HealthSummary"/> is left empty; callers fill it when the
        /// template references {{health.summary}}.
        /// </summary>
        /// <param name="vessel">Vessel.</param>
        /// <returns>Context.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="vessel"/> is null.</exception>
        public static FleetActionTemplateContext FromVessel(Vessel vessel)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            return new FleetActionTemplateContext
            {
                VesselName = vessel.Name,
                VesselId = vessel.Id,
                DefaultBranch = vessel.DefaultBranch,
                WorkingDirectory = vessel.WorkingDirectory ?? String.Empty,
                BuildCommand = vessel.DefinitionOfDoneBuildCommand ?? String.Empty
            };
        }

        #endregion
    }
}
