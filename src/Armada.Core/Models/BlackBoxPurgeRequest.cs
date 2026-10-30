namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Purge black box entries by filter (Admin). Dry run by default. Paging fields are ignored; up to MaxEntries matches are processed.
    /// </summary>
    public class BlackBoxPurgeRequest : BlackBoxEnumerateRequest
    {
        /// <summary>
        /// When true (default), report what would be deleted and delete nothing.
        /// </summary>
        public bool DryRun { get; set; } = true;

        /// <summary>
        /// Whether pinned entries are purged too. Default false.
        /// </summary>
        public bool IncludePinned { get; set; } = false;

        /// <summary>
        /// Most entries one purge deletes (1 to 100000).
        /// </summary>
        public int MaxEntries { get; set; } = 10000;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxPurgeRequest()
        {
        }
    }
}
