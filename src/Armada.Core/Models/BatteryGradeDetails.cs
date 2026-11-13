namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Grader evidence for one battery task attempt (stored as JSON).
    /// </summary>
    public class BatteryGradeDetails
    {
        #region Public-Members

        /// <summary>
        /// Commands run.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems, Reason = "One per grader command in the pack manifest (validated to MaxConfigItems)")]
        public List<BatteryCommandOutcome> Commands
        {
            get => _Commands;
            set => _Commands = value ?? new List<BatteryCommandOutcome>();
        }

        /// <summary>
        /// Byte comparisons.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems, Reason = "One per expected file in the pack manifest")]
        public List<BatteryFileComparison> Files
        {
            get => _Files;
            set => _Files = value ?? new List<BatteryFileComparison>();
        }

        /// <summary>
        /// Mutants.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems, Reason = "One per mutant in the pack manifest")]
        public List<BatteryMutantOutcome> Mutants
        {
            get => _Mutants;
            set => _Mutants = value ?? new List<BatteryMutantOutcome>();
        }

        /// <summary>
        /// Seeded items found.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems, Reason = "At most one per seeded item in the pack manifest")]
        public List<BatterySeededMatch> Matched
        {
            get => _Matched;
            set => _Matched = value ?? new List<BatterySeededMatch>();
        }

        /// <summary>
        /// Seeded items not found.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems, Reason = "At most one per seeded item in the pack manifest")]
        public List<string> Missed
        {
            get => _Missed;
            set => _Missed = value ?? new List<string>();
        }

        /// <summary>
        /// Findings that match no seeded item: the first <see cref="CollectionLimits.MaxConfigItems"/> of them.
        /// <see cref="FalsePositiveCount"/> is the full count (agent output is not truncated silently).
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems, Reason = "First MaxConfigItems false positives; FalsePositiveCount is the total")]
        public List<string> FalsePositives
        {
            get => _FalsePositives;
            set => _FalsePositives = value ?? new List<string>();
        }

        /// <summary>
        /// Every finding that matched no seeded item, including any beyond the stored <see cref="FalsePositives"/>.
        /// </summary>
        public int FalsePositiveCount { get; set; } = 0;

        /// <summary>
        /// Findings counted.
        /// </summary>
        public int FindingCount { get; set; } = 0;

        /// <summary>
        /// Judge verdict parsed from the output, or null.
        /// </summary>
        public string? Verdict { get; set; } = null;

        /// <summary>
        /// Why the output failed its schema check, or null.
        /// </summary>
        public string? SchemaError { get; set; } = null;

        /// <summary>
        /// Rubric text for the evaluator, or null.
        /// </summary>
        public string? Rubric { get; set; } = null;

        #endregion

        #region Private-Members

        private List<BatteryCommandOutcome> _Commands = new List<BatteryCommandOutcome>();
        private List<BatteryFileComparison> _Files = new List<BatteryFileComparison>();
        private List<BatteryMutantOutcome> _Mutants = new List<BatteryMutantOutcome>();
        private List<BatterySeededMatch> _Matched = new List<BatterySeededMatch>();
        private List<string> _Missed = new List<string>();
        private List<string> _FalsePositives = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BatteryGradeDetails()
        {
        }

        #endregion
    }
}
