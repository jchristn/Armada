namespace Armada.Core.Settings
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Settings for bulk vessel import (discovery and import of many repositories at once). All values apply live.
    /// </summary>
    public class VesselImportSettings
    {
        #region Public-Members

        /// <summary>
        /// Root directories that discovery and browsing are limited to. Default empty, which limits browsing to the
        /// user profile directory and the drives or mount points under it. Never null.
        /// </summary>
        public List<string> AllowedRoots
        {
            get => _AllowedRoots;
            set => _AllowedRoots = value ?? new List<string>();
        }

        /// <summary>
        /// Maximum directory depth discovery descends below a scan root. Default 6, minimum 1, maximum 16;
        /// out-of-range values are clamped.
        /// </summary>
        public int MaxDepth
        {
            get => _MaxDepth;
            set => _MaxDepth = value < 1 ? 1 : (value > 16 ? 16 : value);
        }

        /// <summary>
        /// Directory names discovery never descends into (directories starting with a dot are also skipped).
        /// Default: bin, obj, node_modules, dist, .git, .vs, packages, TestResults, .armada, target, venv, .venv,
        /// __pycache__. Setting null yields an empty list.
        /// </summary>
        public List<string> ExcludedDirectoryNames
        {
            get => _ExcludedDirectoryNames;
            set => _ExcludedDirectoryNames = value ?? new List<string>();
        }

        /// <summary>
        /// Largest import that runs inline in the request; larger imports run as a background job. Default 25,
        /// minimum 1, maximum 500; out-of-range values are clamped.
        /// </summary>
        public int InlineBatchLimit
        {
            get => _InlineBatchLimit;
            set => _InlineBatchLimit = value < 1 ? 1 : (value > 500 ? 500 : value);
        }

        /// <summary>
        /// Longest a captain may spend recommending fleets for an import before its process is stopped and the
        /// categorization fails. Default 20 minutes, minimum 1, maximum 240; out-of-range values are clamped.
        /// </summary>
        public int CategorizationTimeoutMinutes
        {
            get => _CategorizationTimeoutMinutes;
            set => _CategorizationTimeoutMinutes = value < 1 ? 1 : (value > 240 ? 240 : value);
        }

        #endregion

        #region Private-Members

        private List<string> _AllowedRoots = new List<string>();
        private int _MaxDepth = 6;
        private List<string> _ExcludedDirectoryNames = new List<string>
        {
            "bin", "obj", "node_modules", "dist", ".git", ".vs", "packages", "TestResults",
            ".armada", "target", "venv", ".venv", "__pycache__"
        };
        private int _InlineBatchLimit = 25;
        private int _CategorizationTimeoutMinutes = 20;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public VesselImportSettings()
        {
        }

        #endregion
    }
}
