namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// A battery pack as listed by list_battery_packs, with its validation errors when it is malformed.
    /// </summary>
    public class BatteryPackInfo
    {
        #region Public-Members

        /// <summary>
        /// Pack identifier (the directory name when the manifest has none).
        /// </summary>
        public string Id
        {
            get => _Id;
            set => _Id = value ?? String.Empty;
        }

        /// <summary>
        /// Display name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Battery version.
        /// </summary>
        public int Version { get; set; } = 0;

        /// <summary>
        /// Manifest schema version.
        /// </summary>
        public int SchemaVersion { get; set; } = 0;

        /// <summary>
        /// Domain.
        /// </summary>
        public string? Domain { get; set; } = null;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Whether the manifest passed validation; only valid packs can run.
        /// </summary>
        public bool Valid { get; set; } = false;

        /// <summary>
        /// Validation errors.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems, Reason = "Validator errors, capped at MaxConfigItems with a final summary entry")]
        public List<string> Errors
        {
            get => _Errors;
            set => _Errors = value ?? new List<string>();
        }

        /// <summary>
        /// Toolchain names.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems, Reason = "The pack toolchain; ToolchainCount is the manifest total")]
        public List<string> Toolchain
        {
            get => _Toolchain;
            set => _Toolchain = value ?? new List<string>();
        }

        /// <summary>
        /// Tasks.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems, Reason = "The pack tasks; TaskCount is the manifest total")]
        public List<BatteryTaskInfo> Tasks
        {
            get => _Tasks;
            set => _Tasks = value ?? new List<BatteryTaskInfo>();
        }

        /// <summary>
        /// Tasks in the manifest. <see cref="Tasks"/> lists at most <see cref="CollectionLimits.MaxConfigItems"/>; a
        /// manifest with more is invalid and says so in <see cref="Errors"/>.
        /// </summary>
        public int TaskCount { get; set; } = 0;

        /// <summary>
        /// Toolchain entries in the manifest. <see cref="Toolchain"/> lists at most
        /// <see cref="CollectionLimits.MaxConfigItems"/>.
        /// </summary>
        public int ToolchainCount { get; set; } = 0;

        /// <summary>
        /// SHA-256 of the manifest bytes (part of a run's configuration revision).
        /// </summary>
        public string? ManifestHash { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Id = String.Empty;
        private List<string> _Errors = new List<string>();
        private List<string> _Toolchain = new List<string>();
        private List<BatteryTaskInfo> _Tasks = new List<BatteryTaskInfo>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BatteryPackInfo()
        {
        }

        #endregion
    }
}
