namespace Armada.Core.BlackBox
{
    using System;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>
    /// The rows <see cref="BlackBoxConfigurationResolver"/> resolves effective configuration from. Every row is optional;
    /// a missing row's settings fall through to the next source. Not thread-safe.
    /// </summary>
    public class BlackBoxConfigurationInputs
    {
        #region Public-Members

        /// <summary>Global settings (required).</summary>
        public ArmadaSettings Settings
        {
            get => _Settings;
            set => _Settings = value ?? throw new ArgumentNullException(nameof(Settings));
        }

        /// <summary>Mission, or null.</summary>
        public Mission? Mission { get; set; } = null;

        /// <summary>Voyage, or null.</summary>
        public Voyage? Voyage { get; set; } = null;

        /// <summary>Vessel, or null.</summary>
        public Vessel? Vessel { get; set; } = null;

        /// <summary>Captain, or null.</summary>
        public Captain? Captain { get; set; } = null;

        /// <summary>Persona the mission ran as, or null.</summary>
        public Persona? Persona { get; set; } = null;

        /// <summary>Workflow profile resolved for the vessel, or null.</summary>
        public WorkflowProfile? WorkflowProfile { get; set; } = null;

        #endregion

        #region Private-Members

        private ArmadaSettings _Settings = new ArmadaSettings();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxConfigurationInputs()
        {
        }

        #endregion
    }
}
