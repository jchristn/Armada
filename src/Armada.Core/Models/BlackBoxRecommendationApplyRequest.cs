namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Apply one configuration recommendation's applyAction. Dry run first; the real run needs the confirm token.
    /// </summary>
    public class BlackBoxRecommendationApplyRequest
    {
        /// <summary>
        /// When true (default), return what would change and a confirm token.
        /// </summary>
        public bool DryRun { get; set; } = true;

        /// <summary>
        /// Token from the dry run.
        /// </summary>
        public string? ConfirmToken { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxRecommendationApplyRequest()
        {
        }
    }
}
