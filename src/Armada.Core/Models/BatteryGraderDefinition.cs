namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// How a battery task is graded. Which fields apply depends on Type; see battery/README.md.
    /// </summary>
    public class BatteryGraderDefinition
    {
        #region Public-Members

        /// <summary>
        /// Grader type.
        /// </summary>
        public BatteryGraderTypeEnum? Type { get; set; } = null;

        /// <summary>
        /// Command and Mutation: directory, relative to the pack, copied into the worktree before the commands run
        /// (hidden acceptance checks).
        /// </summary>
        public string? Overlay { get; set; } = null;

        /// <summary>
        /// Command and Mutation: commands run in the worktree, in order.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<BatteryCommand> Commands
        {
            get => _Commands;
            set => _Commands = value ?? new List<BatteryCommand>();
        }

        /// <summary>
        /// ByteComparison: files that must match their expected bytes.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<BatteryExpectedFile> Files
        {
            get => _Files;
            set => _Files = value ?? new List<BatteryExpectedFile>();
        }

        /// <summary>
        /// SeededSet: the seeded defects or violations.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<BatterySeededItem> Seeded
        {
            get => _Seeded;
            set => _Seeded = value ?? new List<BatterySeededItem>();
        }

        /// <summary>
        /// SeededSet: accepted Judge verdicts (PASS, FAIL, NEEDS_REVISION); empty accepts any.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string> ExpectedVerdicts
        {
            get => _ExpectedVerdicts;
            set => _ExpectedVerdicts = value ?? new List<string>();
        }

        /// <summary>
        /// SeededSet: headings whose content is the blocking findings (each list item, or the whole section).
        /// Empty means every list item outside non-blocking sections is a finding.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string> BlockingSections
        {
            get => _BlockingSections;
            set => _BlockingSections = value ?? new List<string>();
        }

        /// <summary>
        /// SeededSet: a seeded item is found when any line of the output (outside code) mentions it, not only a
        /// blocking finding.
        /// </summary>
        public bool MatchAnywhere { get; set; } = false;

        /// <summary>
        /// SeededSet: most findings that match no seeded item before the task fails.
        /// </summary>
        public int MaxFalsePositives { get; set; } = 0;

        /// <summary>
        /// SeededSet: fewest seeded items that must be found, or null for all of them.
        /// </summary>
        public int? MinMatched { get; set; } = null;

        /// <summary>
        /// Mutation: source changes the tests must detect.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<BatteryMutant> Mutants
        {
            get => _Mutants;
            set => _Mutants = value ?? new List<BatteryMutant>();
        }

        /// <summary>
        /// Rubric: a schema the output must pass first (armada-plan), or null.
        /// </summary>
        public string? Schema { get; set; } = null;

        /// <summary>
        /// Rubric: rubric file, relative to the pack, for the evaluator.
        /// </summary>
        public string? Rubric { get; set; } = null;

        #endregion

        #region Private-Members

        private List<BatteryCommand> _Commands = new List<BatteryCommand>();
        private List<BatteryExpectedFile> _Files = new List<BatteryExpectedFile>();
        private List<BatterySeededItem> _Seeded = new List<BatterySeededItem>();
        private List<string> _ExpectedVerdicts = new List<string>();
        private List<string> _BlockingSections = new List<string>();
        private List<BatteryMutant> _Mutants = new List<BatteryMutant>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BatteryGraderDefinition()
        {
        }

        #endregion
    }
}
