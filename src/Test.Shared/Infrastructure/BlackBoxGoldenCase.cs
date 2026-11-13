namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// One golden case of the black box advisor suite: a record builder and what the advisor must produce for it.
    /// </summary>
    public sealed class BlackBoxGoldenCase
    {
        /// <summary>Rule that must fire.</summary>
        public string RuleId { get; }

        /// <summary>Expected triage verdict.</summary>
        public BlackBoxTriageEnum Verdict { get; }

        /// <summary>Record builder.</summary>
        public Func<BlackBoxRecord> Build { get; }

        /// <summary>Exact why statement, or null to skip the check.</summary>
        public string? Statement { get; set; } = null;

        /// <summary>Id of the top-ranked recovery path, or null to skip the check.</summary>
        public string? FirstPathId { get; set; } = null;

        /// <summary>Rules that must also fire.</summary>
        public List<string> AlsoFires { get; } = new List<string>();

        /// <summary>Rules that must not fire (they step aside).</summary>
        public List<string> DoesNotFire { get; } = new List<string>();

        /// <summary>Instantiate.</summary>
        /// <param name="ruleId">Rule id.</param>
        /// <param name="verdict">Expected verdict.</param>
        /// <param name="build">Record builder.</param>
        public BlackBoxGoldenCase(string ruleId, BlackBoxTriageEnum verdict, Func<BlackBoxRecord> build)
        {
            RuleId = ruleId ?? throw new ArgumentNullException(nameof(ruleId));
            Verdict = verdict;
            Build = build ?? throw new ArgumentNullException(nameof(build));
        }
    }
}
