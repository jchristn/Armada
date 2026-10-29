namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// What one matched rule produced while the advisor renders it (internal to <see cref="BlackBoxAdvisor"/>).
    /// </summary>
    internal sealed class BlackBoxRuleOutput
    {
        public BlackBoxRule Rule { get; }

        public BlackBoxFinding Finding { get; set; } = new BlackBoxFinding();

        public List<BlackBoxConfigRecommendation> Recommendations { get; } = new List<BlackBoxConfigRecommendation>();

        public List<BlackBoxImprovement> Improvements { get; } = new List<BlackBoxImprovement>();

        public List<BlackBoxRecoveryPath> Paths { get; } = new List<BlackBoxRecoveryPath>();

        public BlackBoxRuleOutput(BlackBoxRule rule)
        {
            Rule = rule ?? throw new ArgumentNullException(nameof(rule));
        }
    }
}
