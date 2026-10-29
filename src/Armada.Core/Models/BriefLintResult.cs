namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Brief lint result.
    /// </summary>
    public class BriefLintResult
    {
        #region Public-Members

        /// <summary>
        /// Findings in rule order, at most 50.
        /// </summary>
        [CollectionBound(BlackBoxInsightLimits.MaxLintFindings, Reason = "Lint stops at 50 findings; FindingCount has the total")]
        public List<BriefLintFinding> Findings
        {
            get => _Findings;
            set => _Findings = value ?? new List<BriefLintFinding>();
        }

        /// <summary>
        /// How many findings.
        /// </summary>
        public int FindingCount { get; set; } = 0;

        /// <summary>
        /// True when there are no findings.
        /// </summary>
        public bool Clean { get; set; } = true;

        /// <summary>
        /// True when paths were checked against the vessel's tracked files.
        /// </summary>
        public bool PathsChecked { get; set; } = false;

        #endregion

        #region Private-Members

        private List<BriefLintFinding> _Findings = new List<BriefLintFinding>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BriefLintResult()
        {
        }

        #endregion
    }
}
