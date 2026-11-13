namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A commit referenced by a black box record.
    /// </summary>
    public class BlackBoxCommitRef
    {
        /// <summary>
        /// Commit hash.
        /// </summary>
        public string CommitHash { get; set; } = "";

        /// <summary>
        /// Commit subject, or null.
        /// </summary>
        public string? Subject { get; set; } = null;

        /// <summary>
        /// Author, or null.
        /// </summary>
        public string? Author { get; set; } = null;

        /// <summary>
        /// Commit time (UTC), or null.
        /// </summary>
        public DateTime? CommittedUtc { get; set; } = null;

        /// <summary>
        /// Armada-Mission-Id trailer value, or null.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxCommitRef()
        {
        }
    }
}
