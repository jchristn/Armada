namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Success rate of one recovery path over the caller's tracked runs.
    /// </summary>
    public class BlackBoxRecoveryStat
    {
        #region Public-Members

        /// <summary>
        /// Rule that offers the path.
        /// </summary>
        public string? RuleId { get; set; } = null;

        /// <summary>
        /// Recovery path.
        /// </summary>
        public string PathId { get; set; } = "";

        /// <summary>
        /// Confirmed runs.
        /// </summary>
        public long Runs { get; set; } = 0;

        /// <summary>
        /// Runs after which the mission completed.
        /// </summary>
        public long Succeeded { get; set; } = 0;

        /// <summary>
        /// Runs after which the mission failed again (or a step failed).
        /// </summary>
        public long Failed { get; set; } = 0;

        /// <summary>
        /// Runs still being watched.
        /// </summary>
        public long Pending { get; set; } = 0;

        /// <summary>
        /// Runs with no terminal status within the watch window.
        /// </summary>
        public long Expired { get; set; } = 0;

        /// <summary>
        /// Succeeded divided by settled runs (Succeeded plus Failed), or null when none settled.
        /// </summary>
        public double? SuccessRate { get; set; } = null;

        /// <summary>
        /// Newest run.
        /// </summary>
        public DateTime? LastRunUtc { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxRecoveryStat()
        {
        }

        #endregion
    }
}
