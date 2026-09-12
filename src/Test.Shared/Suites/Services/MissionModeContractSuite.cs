namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="MissionModeContract"/>. Positive cases confirm write mode is not read-only
    /// and emits no extra brief; negative cases confirm the read-only modes are detected and produce a
    /// report-shaped contract that forbids repository changes.
    /// </summary>
    public sealed class MissionModeContractSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the mission-mode-contract suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("implementation_is_write_mode", "Implementation is not read-only", TestTags.Positive, () =>
            {
                AssertFalse(MissionModeContract.IsReadOnly(MissionModeEnum.Implementation), "Implementation must be a write mode");
            }));

            cases.Add(Case("implementation_emits_no_section", "Implementation adds no mode brief", TestTags.Positive, () =>
            {
                string section = MissionModeContract.BuildBriefSection(MissionModeEnum.Implementation);
                AssertTrue(String.IsNullOrEmpty(section), "write mode must not inject a mode section");
            }));

            cases.Add(Case("audit_is_read_only", "Audit is read-only", TestTags.Negative, () =>
            {
                AssertTrue(MissionModeContract.IsReadOnly(MissionModeEnum.Audit), "Audit must be read-only");
            }));

            cases.Add(Case("research_is_read_only", "Research is read-only", TestTags.Negative, () =>
            {
                AssertTrue(MissionModeContract.IsReadOnly(MissionModeEnum.Research), "Research must be read-only");
            }));

            cases.Add(Case("audit_brief_forbids_changes", "Audit brief forbids repository changes and asks for a report", TestTags.Negative, () =>
            {
                string section = MissionModeContract.BuildBriefSection(MissionModeEnum.Audit);
                AssertFalse(String.IsNullOrEmpty(section), "audit must inject a mode section");
                AssertContains("READ-ONLY", section, "audit brief must flag read-only");
                AssertContains("Do NOT modify", section, "audit brief must forbid modifications");
                AssertContains("empty diff is a successful outcome", section, "audit brief must state empty diff is success");
                AssertContains("[ARMADA:RESULT] COMPLETE", section, "audit brief must require the completion marker");
            }));

            cases.Add(Case("research_brief_forbids_changes", "Research brief forbids repository changes and asks for a report", TestTags.Negative, () =>
            {
                string section = MissionModeContract.BuildBriefSection(MissionModeEnum.Research);
                AssertFalse(String.IsNullOrEmpty(section), "research must inject a mode section");
                AssertContains("READ-ONLY", section, "research brief must flag read-only");
                AssertContains("Do NOT modify", section, "research brief must forbid modifications");
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.MissionModeContract",
                displayName: "Mission Mode Contract",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.MissionModeContract",
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
