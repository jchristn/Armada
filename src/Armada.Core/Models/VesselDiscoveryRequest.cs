namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Request to discover vessel import candidates. At least one entry in <see cref="Directories"/> or
    /// <see cref="Roots"/> is required.
    /// </summary>
    public class VesselDiscoveryRequest
    {
        #region Public-Members

        /// <summary>
        /// Explicit directories. A directory that is a git repository becomes one candidate; a directory that is not
        /// is scanned like a root. Never null.
        /// </summary>
        public List<string> Directories
        {
            get => _Directories;
            set => _Directories = value ?? new List<string>();
        }

        /// <summary>
        /// Roots to scan breadth-first for git repositories. Never null.
        /// </summary>
        public List<string> Roots
        {
            get => _Roots;
            set => _Roots = value ?? new List<string>();
        }

        /// <summary>
        /// Maximum scan depth below each root, or null to use the configured Import.MaxDepth. Minimum 1, maximum 16;
        /// out-of-range values are clamped.
        /// </summary>
        public int? MaxDepth
        {
            get => _MaxDepth;
            set => _MaxDepth = value.HasValue ? Math.Clamp(value.Value, 1, 16) : null;
        }

        /// <summary>
        /// Harbor to run discovery on, or null for the Admiral host. Harbor discovery is not supported yet; a non-null
        /// value is rejected.
        /// </summary>
        public string? HarborId { get; set; } = null;

        #endregion

        #region Private-Members

        private List<string> _Directories = new List<string>();
        private List<string> _Roots = new List<string>();
        private int? _MaxDepth = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselDiscoveryRequest()
        {
        }

        #endregion
    }
}
