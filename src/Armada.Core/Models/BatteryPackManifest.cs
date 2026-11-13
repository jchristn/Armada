namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// A battery pack manifest (battery-pack.json): the fixture, the tasks, the persona each task tests, how each
    /// task is graded and the toolchain the Harbor needs. Packs are data; see battery/README.md for the schema.
    /// </summary>
    public class BatteryPackManifest
    {
        #region Public-Members

        /// <summary>
        /// Manifest schema version; 1 is the only version defined.
        /// </summary>
        public int SchemaVersion { get; set; } = 0;

        /// <summary>
        /// Pack identifier: lowercase letters, digits and dashes (for example csharp).
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Display name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Battery version of this pack. Raise it whenever the fixture, a task or a grader changes, so results
        /// recorded under different versions are never compared as equal.
        /// </summary>
        public int Version { get; set; } = 0;

        /// <summary>
        /// Domain, for example code or documentation.
        /// </summary>
        public string? Domain { get; set; } = null;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// The fixture repository content.
        /// </summary>
        public BatteryFixtureDefinition? Fixture { get; set; } = null;

        /// <summary>
        /// Tools the Harbor needs for the pack's command-graded tasks.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<BatteryToolchainRequirement> Toolchain
        {
            get => _Toolchain;
            set => _Toolchain = value ?? new List<BatteryToolchainRequirement>();
        }

        /// <summary>
        /// Build and test commands recorded with the fixture vessel, or null.
        /// </summary>
        public BatteryStageChecks? StageChecks { get; set; } = null;

        /// <summary>
        /// Tasks, in run order.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<BatteryTaskDefinition> Tasks
        {
            get => _Tasks;
            set => _Tasks = value ?? new List<BatteryTaskDefinition>();
        }

        #endregion

        #region Private-Members

        private List<BatteryToolchainRequirement> _Toolchain = new List<BatteryToolchainRequirement>();
        private List<BatteryTaskDefinition> _Tasks = new List<BatteryTaskDefinition>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BatteryPackManifest()
        {
        }

        #endregion
    }
}
