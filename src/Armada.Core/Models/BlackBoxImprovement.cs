namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// An Armada behavior change that could address the failure.
    /// </summary>
    public class BlackBoxImprovement
    {
        /// <summary>
        /// Area of Armada.
        /// </summary>
        public string? Area { get; set; } = null;

        /// <summary>
        /// Hypothesis.
        /// </summary>
        public string Hypothesis { get; set; } = "";

        /// <summary>
        /// Evidence.
        /// </summary>
        public string? Evidence { get; set; } = null;

        /// <summary>
        /// Source pointers (file, method).
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxSourcePointers, Reason = "Capped by BlackBoxRecordBounder")]
        public List<string> SourcePointers
        {
            get => _SourcePointers;
            set => _SourcePointers = value ?? new List<string>();
        }

        /// <summary>
        /// Severity.
        /// </summary>
        public BlackBoxSeverityEnum Severity { get; set; } = BlackBoxSeverityEnum.Medium;

        /// <summary>
        /// Rule that produced it.
        /// </summary>
        public string? RuleId { get; set; } = null;

        /// <summary>
        /// Known issue link, or null.
        /// </summary>
        public string? KnownIssue { get; set; } = null;

        private List<string> _SourcePointers = new List<string>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxImprovement()
        {
        }
    }
}
