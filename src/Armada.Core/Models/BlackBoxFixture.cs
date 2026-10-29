namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A black box regression fixture (src/Test.Shared/Fixtures/BlackBox/*.json): a sanitized record and the findings the
    /// advisor must keep producing for it. Written by IBlackBoxService.ExportFixtureAsync (armada blackbox export-fixture).
    /// </summary>
    public class BlackBoxFixture
    {
        /// <summary>
        /// Fixture name (file name without .json).
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// What the fixture reproduces.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Where it came from (entry or mission id, or "reconstructed").
        /// </summary>
        public string? Source { get; set; } = null;

        /// <summary>
        /// Rules version the expectations were taken with.
        /// </summary>
        public int RulesVersion { get; set; } = 0;

        /// <summary>
        /// Rule ids the advisor must produce findings for.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxFindings, Reason = "One per expected finding")]
        public List<string> ExpectedRuleIds
        {
            get => _ExpectedRuleIds;
            set => _ExpectedRuleIds = value ?? new List<string>();
        }

        /// <summary>
        /// Triage verdict the advisor must reach.
        /// </summary>
        public BlackBoxTriageEnum ExpectedTriage { get; set; } = BlackBoxTriageEnum.Unknown;

        /// <summary>
        /// The sanitized record.
        /// </summary>
        public BlackBoxRecord Record { get; set; } = new BlackBoxRecord();

        private List<string> _ExpectedRuleIds = new List<string>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxFixture()
        {
        }
    }
}
