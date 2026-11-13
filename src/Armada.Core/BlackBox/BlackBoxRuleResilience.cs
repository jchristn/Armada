namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// A rule's resilience template: whether and how Armada could have healed the failure by itself, why Armada did not
    /// catch it earlier and the invariant that should hold, the Armada source locations involved, the proposed Armada
    /// changes (mission briefs) and the regression test that would have caught it. The advisor renders it into
    /// <c>analysis.resilience</c>. Text fields are rule templates (<see cref="BlackBoxRuleTemplate"/>); use fallbacks
    /// (<c>{name|fallback}</c>) so they always render. Not thread-safe while being built; read-only afterwards.
    /// </summary>
    public class BlackBoxRuleResilience
    {
        #region Public-Members

        /// <summary>Whether Armada could have recovered automatically.</summary>
        public BlackBoxSelfHealableEnum SelfHealable { get; set; } = BlackBoxSelfHealableEnum.Unknown;

        /// <summary>What Armada could have done automatically (template), phrased to follow "Armada could have".</summary>
        public string? SelfHealing { get; set; } = null;

        /// <summary>Kind of detection gap.</summary>
        public BlackBoxDetectionGapEnum GapKind { get; set; } = BlackBoxDetectionGapEnum.Unknown;

        /// <summary>Why Armada did not catch or prevent it earlier (template).</summary>
        public string? DetectionGap { get; set; } = null;

        /// <summary>The invariant that should hold (template).</summary>
        public string? Invariant { get; set; } = null;

        /// <summary>Armada source locations most likely involved (file:method).</summary>
        public List<string> CodePointers
        {
            get => _CodePointers;
            set => _CodePointers = value ?? new List<string>();
        }

        /// <summary>Proposed Armada changes, most impactful first.</summary>
        public List<BlackBoxRuleChange> Changes
        {
            get => _Changes;
            set => _Changes = value ?? new List<BlackBoxRuleChange>();
        }

        /// <summary>Test suite of the regression test (for example Services.LandingService).</summary>
        public string? RegressionSuite { get; set; } = null;

        /// <summary>The regression test case (template).</summary>
        public string? RegressionTest { get; set; } = null;

        #endregion

        #region Private-Members

        private List<string> _CodePointers = new List<string>();
        private List<BlackBoxRuleChange> _Changes = new List<BlackBoxRuleChange>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxRuleResilience()
        {
        }

        #endregion
    }
}
