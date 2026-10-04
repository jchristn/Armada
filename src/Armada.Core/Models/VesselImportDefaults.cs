namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>
    /// Optional settings applied to every vessel an import creates.
    /// </summary>
    public class VesselImportDefaults
    {
        #region Public-Members

        /// <summary>
        /// Default pipeline identifier (ppl_ prefix), or null for none.
        /// </summary>
        public string? DefaultPipelineId { get; set; } = null;

        /// <summary>
        /// Landing mode, or null to inherit the global default.
        /// </summary>
        public LandingModeEnum? LandingMode { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselImportDefaults()
        {
        }

        #endregion
    }
}
