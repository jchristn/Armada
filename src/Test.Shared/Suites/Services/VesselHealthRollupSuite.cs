namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Health;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors pinning the vessel health rollup: worst-of over Pass, Warn, and Fail; Unknown and NotApplicable are
    /// neutral; all-Unknown is Unknown (never Pass); all-NotApplicable is NotApplicable; criteria outside ScoredCriteria
    /// are ignored; criterion overrides change effective columns and the rollup; an Overall override wins.
    /// </summary>
    public sealed class VesselHealthRollupSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.VesselHealthRollup";

        private static readonly List<VesselHealthCriterionEnum> _Scored = new RepositoryHealthSettings().ScoredCriteria;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("worst_of_pass_warn_fail", "Fail beats Warn beats Pass", TestTags.Positive, () =>
            {
                AssertEqual(VesselHealthStatusEnum.Fail, Overall(Pass(VesselHealthCriterionEnum.GitDivergence), Warn(VesselHealthCriterionEnum.Branches), Fail(VesselHealthCriterionEnum.Dependencies)));
                AssertEqual(VesselHealthStatusEnum.Warn, Overall(Pass(VesselHealthCriterionEnum.GitDivergence), Warn(VesselHealthCriterionEnum.Branches)));
                AssertEqual(VesselHealthStatusEnum.Pass, Overall(Pass(VesselHealthCriterionEnum.GitDivergence), Pass(VesselHealthCriterionEnum.Branches)));
            }));

            cases.Add(Case("unknown_and_not_applicable_are_neutral", "Unknown and NotApplicable neither pass nor fail a vessel", TestTags.Positive, () =>
            {
                AssertEqual(VesselHealthStatusEnum.Pass, Overall(Pass(VesselHealthCriterionEnum.GitDivergence), F(VesselHealthCriterionEnum.Dependencies, VesselHealthStatusEnum.Unknown), F(VesselHealthCriterionEnum.WorkingTree, VesselHealthStatusEnum.NotApplicable)));
                AssertEqual(VesselHealthStatusEnum.Warn, Overall(Warn(VesselHealthCriterionEnum.GitDivergence), F(VesselHealthCriterionEnum.Dependencies, VesselHealthStatusEnum.Unknown)));
            }));

            cases.Add(Case("all_unknown_is_unknown", "A vessel whose every scored criterion is Unknown is Unknown, not Pass", TestTags.Negative, () =>
            {
                List<VesselHealthFinding> findings = new List<VesselHealthFinding>();
                foreach (VesselHealthCriterionEnum criterion in _Scored) findings.Add(F(criterion, VesselHealthStatusEnum.Unknown));
                AssertEqual(VesselHealthStatusEnum.Unknown, Overall(findings.ToArray()));
                AssertEqual(VesselHealthStatusEnum.Unknown, Overall(), "no findings at all is Unknown");
                AssertEqual(VesselHealthStatusEnum.Unknown, Overall(F(VesselHealthCriterionEnum.GitDivergence, VesselHealthStatusEnum.NotApplicable)), "NotApplicable plus missing (Unknown) is Unknown");
            }));

            cases.Add(Case("all_not_applicable_is_not_applicable", "Every scored criterion NotApplicable rolls up to NotApplicable", TestTags.Positive, () =>
            {
                List<VesselHealthFinding> findings = new List<VesselHealthFinding>();
                foreach (VesselHealthCriterionEnum criterion in _Scored) findings.Add(F(criterion, VesselHealthStatusEnum.NotApplicable));
                AssertEqual(VesselHealthStatusEnum.NotApplicable, Overall(findings.ToArray()));
            }));

            cases.Add(Case("scored_criteria_exclusion", "Criteria outside ScoredCriteria (CI, CommitRecency by default) never affect the rollup", TestTags.Positive, () =>
            {
                AssertEqual(VesselHealthStatusEnum.Pass, Overall(Pass(VesselHealthCriterionEnum.GitDivergence), Fail(VesselHealthCriterionEnum.ContinuousIntegration), Fail(VesselHealthCriterionEnum.CommitRecency)));
                Dictionary<VesselHealthCriterionEnum, VesselHealthStatusEnum> effective = new Dictionary<VesselHealthCriterionEnum, VesselHealthStatusEnum>
                {
                    { VesselHealthCriterionEnum.ContinuousIntegration, VesselHealthStatusEnum.Fail },
                    { VesselHealthCriterionEnum.GitDivergence, VesselHealthStatusEnum.Pass }
                };
                AssertEqual(VesselHealthStatusEnum.Fail, VesselHealthRollup.ComputeOverall(effective, new List<VesselHealthCriterionEnum> { VesselHealthCriterionEnum.ContinuousIntegration }), "opting CI in makes it count");
                AssertEqual(VesselHealthStatusEnum.Unknown, VesselHealthRollup.ComputeOverall(effective, new List<VesselHealthCriterionEnum>()), "empty ScoredCriteria is Unknown");
            }));

            cases.Add(Case("criterion_override_changes_effective", "A criterion override replaces the raw status in its column and in the rollup", TestTags.Positive, () =>
            {
                VesselHealth health = new VesselHealth();
                List<VesselHealthFinding> findings = new List<VesselHealthFinding> { Pass(VesselHealthCriterionEnum.GitDivergence), Fail(VesselHealthCriterionEnum.Dependencies) };
                List<VesselHealthOverride> overrides = new List<VesselHealthOverride> { O(VesselHealthCriterionEnum.Dependencies, VesselHealthStatusEnum.Pass) };
                VesselHealthRollup.ApplyEffectiveStatuses(health, findings, overrides, _Scored);
                AssertEqual(VesselHealthStatusEnum.Pass, health.DependencyStatus);
                AssertEqual(VesselHealthStatusEnum.Pass, health.DivergenceStatus);
                AssertEqual(VesselHealthStatusEnum.Pass, health.OverallStatus);
                AssertEqual(VesselHealthStatusEnum.Unknown, health.TestInfraStatus, "criteria without findings are Unknown");
            }));

            cases.Add(Case("overall_override_wins", "An Overall override replaces the computed overall status", TestTags.Positive, () =>
            {
                VesselHealth health = new VesselHealth();
                List<VesselHealthFinding> findings = new List<VesselHealthFinding> { Fail(VesselHealthCriterionEnum.GitDivergence) };
                List<VesselHealthOverride> overrides = new List<VesselHealthOverride> { O(VesselHealthCriterionEnum.Overall, VesselHealthStatusEnum.Warn) };
                VesselHealthRollup.ApplyEffectiveStatuses(health, findings, overrides, _Scored);
                AssertEqual(VesselHealthStatusEnum.Warn, health.OverallStatus);
                AssertEqual(VesselHealthStatusEnum.Fail, health.DivergenceStatus, "criterion columns keep their effective values");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Vessel Health Rollup",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static VesselHealthStatusEnum Overall(params VesselHealthFinding[] findings)
        {
            VesselHealth health = new VesselHealth();
            VesselHealthRollup.ApplyEffectiveStatuses(health, findings, null, _Scored);
            return health.OverallStatus;
        }

        private static VesselHealthFinding F(VesselHealthCriterionEnum criterion, VesselHealthStatusEnum status)
        {
            return new VesselHealthFinding { Criterion = criterion, Status = status };
        }

        private static VesselHealthFinding Pass(VesselHealthCriterionEnum criterion) => F(criterion, VesselHealthStatusEnum.Pass);

        private static VesselHealthFinding Warn(VesselHealthCriterionEnum criterion) => F(criterion, VesselHealthStatusEnum.Warn);

        private static VesselHealthFinding Fail(VesselHealthCriterionEnum criterion) => F(criterion, VesselHealthStatusEnum.Fail);

        private static VesselHealthOverride O(VesselHealthCriterionEnum criterion, VesselHealthStatusEnum status)
        {
            return new VesselHealthOverride { Criterion = criterion, Status = status };
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
