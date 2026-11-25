namespace Test.Shared.Suites.Models
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Gate;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The acceptance gate's scoring (RELIABILITY_SIMPLICITY_PLAN.md RS-1.5): G1 from graded battery tasks, G4 and G5
    /// from repair dispositions, G6 from runtime failure kinds; measures checked elsewhere are NotMeasured, never Pass.
    /// </summary>
    public sealed class GateScoringSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Models.GateScoring";

        /// <summary>
        /// Build the descriptor for the gate scoring suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("thresholds_pass_and_fail", "G1 passes at 99% and fails below; G4 fails on any full rerun; G5 and G6 use their thresholds", () =>
                {
                    GateReport good = new GateReport { Pipelines = new List<GatePipelineResult> { new GatePipelineResult { Pipeline = "Gate-A", Passed = 99, Failed = 1, CouldNotRun = 5 } } };
                    List<GateMeasure> measures = GateRunner.Score(good, new List<StageOutcome>
                    {
                        new StageOutcome { Disposition = StageDispositionEnum.Completed, ContractRepairKind = ContractRepairKindEnum.VerdictNonCanonical, RuntimeFailureKind = RuntimeFailureKindEnum.Clean }
                    });
                    AssertEqual(GateMeasureStatusEnum.Pass, Measure(measures, "G1").Status, "99 of 100 is 99%; could-not-run tasks are excluded");
                    AssertEqual(GateMeasureStatusEnum.Pass, Measure(measures, "G4").Status);
                    AssertEqual(GateMeasureStatusEnum.Pass, Measure(measures, "G5").Status);
                    AssertEqual(GateMeasureStatusEnum.Pass, Measure(measures, "G6").Status, "a clean exit is not a runtime failure");

                    GateReport bad = new GateReport { Pipelines = new List<GatePipelineResult> { new GatePipelineResult { Pipeline = "Gate-D", Passed = 97, Failed = 3 } } };
                    List<StageOutcome> outcomes = new List<StageOutcome>
                    {
                        new StageOutcome { Disposition = StageDispositionEnum.OutputRepair },
                        new StageOutcome { Disposition = StageDispositionEnum.Failed, RuntimeFailureKind = RuntimeFailureKindEnum.Crash },
                        new StageOutcome { Disposition = StageDispositionEnum.Relaunched, RuntimeFailureKind = RuntimeFailureKindEnum.Transient }
                    };
                    List<GateMeasure> failing = GateRunner.Score(bad, outcomes);
                    AssertEqual(GateMeasureStatusEnum.Fail, Measure(failing, "G1").Status);
                    AssertEqual(GateMeasureStatusEnum.Fail, Measure(failing, "G4").Status, "one full rerun fails G4");
                    AssertEqual(GateMeasureStatusEnum.Fail, Measure(failing, "G5").Status, "no format failure was repaired in place");
                    AssertEqual(GateMeasureStatusEnum.Fail, Measure(failing, "G6").Status, "one of two runtime failures is unclassified");
                }),
                Case("unmeasured_are_never_pass", "Measures checked by other suites are NotMeasured and name where they are checked", () =>
                {
                    List<GateMeasure> measures = GateRunner.Score(new GateReport(), new List<StageOutcome>());
                    AssertEqual(12, measures.Count, "G1 to G12");
                    AssertEqual(GateMeasureStatusEnum.NotMeasured, Measure(measures, "G1").Status, "no graded task, no G1");
                    foreach (string id in new[] { "G3", "G7", "G8", "G9", "G10", "G11", "G12" })
                    {
                        GateMeasure measure = Measure(measures, id);
                        AssertEqual(GateMeasureStatusEnum.NotMeasured, measure.Status, id);
                        AssertContains("Measured elsewhere", measure.Detail, id);
                    }
                })
            };

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Gate scoring", cases: cases);
        }

        private static GateMeasure Measure(List<GateMeasure> measures, string id)
        {
            return measures.Single(m => m.Id == id);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; },
                tags: new List<string> { TestTags.Positive });
        }
    }
}
