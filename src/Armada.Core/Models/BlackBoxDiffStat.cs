namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Diff stat of the mission's change.
    /// </summary>
    public class BlackBoxDiffStat
    {
        /// <summary>
        /// Files changed.
        /// </summary>
        public int Files { get; set; } = 0;

        /// <summary>
        /// Lines inserted.
        /// </summary>
        public int Insertions { get; set; } = 0;

        /// <summary>
        /// Lines deleted.
        /// </summary>
        public int Deletions { get; set; } = 0;

        /// <summary>
        /// Top paths by change size.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxDiffTopPaths, Reason = "Capped by BlackBoxRecordBounder")]
        public List<string> TopPaths
        {
            get => _TopPaths;
            set => _TopPaths = value ?? new List<string>();
        }

        private List<string> _TopPaths = new List<string>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxDiffStat()
        {
        }
    }
}
