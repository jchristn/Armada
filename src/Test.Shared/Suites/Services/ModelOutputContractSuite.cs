namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Test.Shared.Infrastructure.ModelOutputs;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Reads every canned output in <see cref="ModelOutputFixtures"/> the way the pipeline does (the canonical parser,
    /// then the deterministic contract repair) and checks the result (RELIABILITY_SIMPLICITY_PLAN.md RS-1.2). A fixture
    /// whose intended result needs later work is skipped with that plan item, which turns it on.
    /// </summary>
    public sealed class ModelOutputContractSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.ModelOutputContract";

        /// <summary>
        /// Build the descriptor for the model output contract suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            bool runPending = Environment.GetEnvironmentVariable("ARMADA_TEST_PENDING_CONTRACT_CASES") == "1";
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            foreach (ModelOutputFixture fixture in ModelOutputFixtures.All)
            {
                ModelOutputFixture captured = fixture;
                bool skip = captured.PendingItem != null && !runPending;
                cases.Add(new TestCaseDescriptor(
                    suiteId: SuiteId,
                    caseId: captured.Id,
                    displayName: captured.Persona + ": " + captured.Description + " reads as " + captured.Expected,
                    executeAsync: (CancellationToken ct) =>
                    {
                        AssertEqual(captured.Expected, Read(captured), captured.Id);
                        return Task.CompletedTask;
                    },
                    tags: new List<string> { TestTags.Reliability },
                    skip: skip,
                    skipReason: skip ? "Pending " + captured.PendingItem + ". Set ARMADA_TEST_PENDING_CONTRACT_CASES=1 to run it." : null));
            }

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Model output contracts", cases: cases);
        }

        private static string Read(ModelOutputFixture fixture)
        {
            switch (fixture.Persona)
            {
                case "Judge":
                    JudgeVerdictEnum verdict = JudgeVerdictParser.Parse(fixture.Text);
                    if (verdict == JudgeVerdictEnum.None && ContractRepair.TryRecoverVerdict(fixture.Text, out JudgeVerdictEnum recovered, out string? _)) verdict = recovered;
                    return verdict.ToString();
                case "Worker":
                    return ContractRepair.ClassifyCompletionLine(fixture.Text).ToString();
                case "Architect":
                    if (ArchitectPlanParser.TryParse(fixture.Text, out ArchitectPlan? plan, out string? error) && plan != null) return "missions:" + plan.Missions.Count;
                    if (error == null && ContractRepair.TryRecoverArchitectPlan(fixture.Text, out string? json) && json != null
                        && ArchitectPlanParser.TryParse("```" + ArchitectPlanParser.FenceInfo + "\n" + json + "\n```\n", out ArchitectPlan? repaired, out string? _) && repaired != null)
                        return "missions:" + repaired.Missions.Count;
                    return "rejected";
                default:
                    throw new ArgumentOutOfRangeException(nameof(fixture), "Unknown persona " + fixture.Persona + ".");
            }
        }
    }
}
