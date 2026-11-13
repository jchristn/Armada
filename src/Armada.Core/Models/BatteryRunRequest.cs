namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Body of POST /api/v1/battery/runs (run_battery).
    /// </summary>
    public class BatteryRunRequest
    {
        #region Public-Members

        /// <summary>
        /// Packs to run (identifiers).
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string> Packs
        {
            get => _Packs;
            set => _Packs = value ?? new List<string>();
        }

        /// <summary>
        /// Captain to run the tasks (cpt_ prefix).
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Runs of each task (1 to 10; default 3).
        /// </summary>
        public int? RunCount { get; set; } = null;

        /// <summary>
        /// Seed to record with the run, or null to pick one.
        /// </summary>
        public int? Seed { get; set; } = null;

        /// <summary>
        /// Create the pack's fixture vessel when it does not exist yet.
        /// </summary>
        public bool CreateFixtureVessel { get; set; } = false;

        /// <summary>
        /// Persona prompt variant every task of the run renders with, or null to use the captain's own setting
        /// (default Current). Runs that differ only in this setting compare the variants.
        /// </summary>
        public PromptVariantEnum? PromptVariant { get; set; } = null;

        #endregion

        #region Private-Members

        private List<string> _Packs = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BatteryRunRequest()
        {
        }

        #endregion
    }
}
