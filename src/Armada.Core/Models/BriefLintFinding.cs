namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// One brief lint finding. Findings are warnings: dispatch never refuses a brief because of them.
    /// </summary>
    public class BriefLintFinding
    {
        #region Public-Members

        /// <summary>
        /// Stable rule id: BRIEF-DEP-001 (dependency language without DependsOnMissionId), BRIEF-NOOP-001 (verification-only brief), BRIEF-PATH-001 (path the vessel does not have), BRIEF-PATH-002 (absolute or parent-relative path).
        /// </summary>
        public string RuleId { get; set; } = "";

        /// <summary>
        /// Severity.
        /// </summary>
        public BriefLintSeverityEnum Severity { get; set; } = BriefLintSeverityEnum.Warning;

        /// <summary>
        /// What is wrong, in one sentence.
        /// </summary>
        public string Message { get; set; } = "";

        /// <summary>
        /// The brief text that triggered it (at most 200 characters), or null.
        /// </summary>
        public string? Excerpt { get; set; } = null;

        /// <summary>
        /// The path concerned (BRIEF-PATH rules), or null.
        /// </summary>
        public string? Path { get; set; } = null;

        /// <summary>
        /// How to fix it, or null.
        /// </summary>
        public string? Suggestion { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BriefLintFinding()
        {
        }

        #endregion
    }
}
