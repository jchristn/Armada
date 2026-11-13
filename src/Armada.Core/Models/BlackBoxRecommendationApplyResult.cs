namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Result of applying (or dry running) a configuration recommendation.
    /// </summary>
    public class BlackBoxRecommendationApplyResult
    {
        /// <summary>
        /// Entry.
        /// </summary>
        public string EntryId { get; set; } = "";

        /// <summary>
        /// Recommendation.
        /// </summary>
        public string RecommendationId { get; set; } = "";

        /// <summary>
        /// Whether this was a dry run.
        /// </summary>
        public bool DryRun { get; set; } = false;

        /// <summary>
        /// Confirm token (dry run only).
        /// </summary>
        public string? ConfirmToken { get; set; } = null;

        /// <summary>
        /// Expiry (UTC).
        /// </summary>
        public DateTime? ConfirmTokenExpiresUtc { get; set; } = null;

        /// <summary>
        /// Action that runs (or would run).
        /// </summary>
        public BlackBoxAction? Action { get; set; } = null;

        /// <summary>
        /// Whether it succeeded.
        /// </summary>
        public bool Succeeded { get; set; } = false;

        /// <summary>
        /// Result message.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxRecommendationApplyResult()
        {
        }
    }
}
