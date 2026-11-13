namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Redaction report: rule names and counts, never values.
    /// </summary>
    public class BlackBoxRedaction
    {
        /// <summary>
        /// Rules that fired.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxRedactionRules, Reason = "One per BlackBoxRedactor rule")]
        public List<BlackBoxRedactionRuleCount> RulesApplied
        {
            get => _RulesApplied;
            set => _RulesApplied = value ?? new List<BlackBoxRedactionRuleCount>();
        }

        /// <summary>
        /// Total redactions.
        /// </summary>
        public int RedactedCount { get; set; } = 0;

        private List<BlackBoxRedactionRuleCount> _RulesApplied = new List<BlackBoxRedactionRuleCount>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxRedaction()
        {
        }
    }
}
