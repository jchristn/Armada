namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Expected resilience output of a black box regression fixture.
    /// </summary>
    public class BlackBoxFixtureResilience
    {
        /// <summary>
        /// Expected InScope.
        /// </summary>
        public bool InScope { get; set; } = false;

        /// <summary>
        /// Expected SelfHealable.
        /// </summary>
        public BlackBoxSelfHealableEnum SelfHealable { get; set; } = BlackBoxSelfHealableEnum.Unknown;

        /// <summary>
        /// Text the top proposed change's title or change must contain (case-insensitive), or null.
        /// </summary>
        public string? ProposalContains { get; set; } = null;

        /// <summary>
        /// Text the self-healing proposal must contain (case-insensitive), or null.
        /// </summary>
        public string? SelfHealingContains { get; set; } = null;

        /// <summary>
        /// A configuration recommendation that must still be offered (the user-side fix), or null.
        /// </summary>
        public string? RecommendationId { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxFixtureResilience()
        {
        }
    }
}
