namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// A rule's Armada improvement hypothesis template, with source pointers (repository-relative file and method, for
    /// example <c>src/Armada.Core/Services/MissionService.cs:FindOutOfScopeFiles</c>) for the maintainer. Not
    /// thread-safe while being built.
    /// </summary>
    public class BlackBoxRuleImprovement
    {
        #region Public-Members

        /// <summary>Area of Armada.</summary>
        public string? Area { get; set; } = null;

        /// <summary>Hypothesis (template).</summary>
        public string Hypothesis { get; set; } = "";

        /// <summary>Evidence (template), or null.</summary>
        public string? Evidence { get; set; } = null;

        /// <summary>Source pointers (file:method).</summary>
        public List<string> SourcePointers
        {
            get => _SourcePointers;
            set => _SourcePointers = value ?? new List<string>();
        }

        /// <summary>Severity.</summary>
        public BlackBoxSeverityEnum Severity { get; set; } = BlackBoxSeverityEnum.Medium;

        /// <summary>Known issue reference, or null.</summary>
        public string? KnownIssue { get; set; } = null;

        /// <summary>Optional: when its placeholders are missing it is left out instead of skipping the rule.</summary>
        public bool Optional { get; set; } = true;

        #endregion

        #region Private-Members

        private List<string> _SourcePointers = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxRuleImprovement()
        {
        }

        #endregion
    }
}
