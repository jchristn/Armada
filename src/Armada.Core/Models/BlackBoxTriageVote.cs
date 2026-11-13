namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// One rule's triage vote.
    /// </summary>
    public class BlackBoxTriageVote
    {
        /// <summary>
        /// Rule.
        /// </summary>
        public string RuleId { get; set; } = "";

        /// <summary>
        /// Vote.
        /// </summary>
        public BlackBoxTriageEnum Verdict { get; set; } = BlackBoxTriageEnum.Unknown;

        /// <summary>
        /// Weight.
        /// </summary>
        public int Weight { get; set; } = 1;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxTriageVote()
        {
        }
    }
}
