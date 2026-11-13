namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Scope section: the mission's scoped files and what fell outside them.
    /// </summary>
    public class BlackBoxScope
    {
        /// <summary>
        /// Scoped files from the mission brief.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxScopeFiles, Reason = "Capped by BlackBoxRecordBounder")]
        public List<string> ScopedFiles
        {
            get => _ScopedFiles;
            set => _ScopedFiles = value ?? new List<string>();
        }

        /// <summary>
        /// Changed files outside the scope.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxScopeFiles, Reason = "Capped by BlackBoxRecordBounder")]
        public List<string> OutOfScopeFiles
        {
            get => _OutOfScopeFiles;
            set => _OutOfScopeFiles = value ?? new List<string>();
        }

        /// <summary>
        /// Total out-of-scope files before capping.
        /// </summary>
        public int OutOfScopeFileTotal { get; set; } = 0;

        /// <summary>
        /// Protected-path findings.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxScopeFiles, Reason = "Capped by BlackBoxRecordBounder")]
        public List<string> BoundaryFindings
        {
            get => _BoundaryFindings;
            set => _BoundaryFindings = value ?? new List<string>();
        }

        private List<string> _ScopedFiles = new List<string>();
        private List<string> _OutOfScopeFiles = new List<string>();
        private List<string> _BoundaryFindings = new List<string>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxScope()
        {
        }
    }
}
