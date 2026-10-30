namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// A rule's recovery path template. Its id is stable: the rule id in lower case, a dot, and <see cref="Key"/>
    /// (for example <c>bbx-land-001.stash-retry</c>), so a confirm token and an agent's plan survive re-analysis. Not
    /// thread-safe while being built; read-only afterwards.
    /// </summary>
    public class BlackBoxRulePath
    {
        #region Public-Members

        /// <summary>Key, unique within the rule (lower case, digits and dashes).</summary>
        public string Key { get; set; } = "";

        /// <summary>Title (template).</summary>
        public string Title { get; set; } = "";

        /// <summary>Order within the rule (lower ranks first).</summary>
        public int Priority { get; set; } = 0;

        /// <summary>Preconditions (templates).</summary>
        public List<string> Preconditions
        {
            get => _Preconditions;
            set => _Preconditions = value ?? new List<string>();
        }

        /// <summary>Steps, in order.</summary>
        public List<BlackBoxRuleStep> Steps
        {
            get => _Steps;
            set => _Steps = value ?? new List<BlackBoxRuleStep>();
        }

        /// <summary>Risk.</summary>
        public BlackBoxSeverityEnum Risk { get; set; } = BlackBoxSeverityEnum.Low;

        /// <summary>Expected outcome (template).</summary>
        public string? ExpectedOutcome { get; set; } = null;

        /// <summary>Optional (default): when its placeholders are missing the path is left out (and logged) instead of
        /// skipping the whole rule. Set false for a path the rule must not be reported without.</summary>
        public bool Optional { get; set; } = true;

        /// <summary>Extra condition for offering this path (null always offers it).</summary>
        public Func<BlackBoxRecord, bool>? When { get; set; } = null;

        #endregion

        #region Private-Members

        private List<string> _Preconditions = new List<string>();
        private List<BlackBoxRuleStep> _Steps = new List<BlackBoxRuleStep>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxRulePath()
        {
        }

        #endregion
    }
}
