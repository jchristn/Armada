namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="ArchitectPlanParser"/>. Positive cases confirm a fenced armada-plan block
    /// deserializes into a typed plan with dependency indexes and the wait flag; negative cases confirm malformed
    /// JSON, unfenced JSON, echoed placeholder examples, and invalid dependency indexes do not produce a plan or a
    /// dependency.
    /// </summary>
    public sealed class ArchitectPlanParserSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("fenced_plan_is_read", "A fenced armada-plan block deserializes into a typed plan", TestTags.Positive, () =>
            {
                string output =
                    "Plan follows.\n" +
                    "```armada-plan\n" +
                    "{\"missions\":[{\"title\":\"A\",\"description\":\"do a\"},{\"title\":\"B\",\"description\":\"do b\",\"dependsOn\":1},{\"title\":\"C\",\"waitForOtherMissions\":true}]}\n" +
                    "```\n";
                AssertTrue(ArchitectPlanParser.TryParse(output, out ArchitectPlan? plan), "plan found");
                AssertEqual(3, plan!.Missions.Count);
                AssertEqual("A", plan.Missions[0].Title);
                AssertNull(plan.Missions[0].DependsOn);
                AssertEqual(1, plan.Missions[1].DependsOn);
                AssertTrue(plan.Missions[2].WaitForOtherMissions, "wait flag");
                AssertEqual("C", plan.Missions[2].Description, "a missing description falls back to the title");
            }));

            cases.Add(Case("last_plan_wins", "When the plan is revised, the last valid block wins", TestTags.Positive, () =>
            {
                string output =
                    "```armada-plan\n{\"missions\":[{\"title\":\"Draft\"}]}\n```\n" +
                    "Revised:\n" +
                    "```ARMADA-PLAN\n{\"missions\":[{\"title\":\"Final\"}]}\n```\n";
                AssertTrue(ArchitectPlanParser.TryParse(output, out ArchitectPlan? plan));
                AssertEqual("Final", plan!.Missions[0].Title);
            }));

            cases.Add(Case("dependency_indexes_survive_dropped_entries", "dependsOn keeps naming the intended mission after untitled or placeholder entries are dropped", TestTags.Positive, () =>
            {
                string output =
                    "```armada-plan\n" +
                    "{\"missions\":[{\"title\":\"<title>\"},{\"title\":\"Core\"},{\"title\":\"\"},{\"title\":\"Backend\",\"dependsOn\":2}]}\n" +
                    "```";
                AssertTrue(ArchitectPlanParser.TryParse(output, out ArchitectPlan? plan));
                AssertEqual(2, plan!.Missions.Count);
                AssertEqual("Core", plan.Missions[0].Title);
                AssertEqual(1, plan.Missions[1].DependsOn, "original mission 2 (Core) is now mission 1");
            }));

            cases.Add(Case("invalid_dependencies_are_cleared", "Self, forward, and out-of-range dependencies are cleared", TestTags.Negative, () =>
            {
                string output =
                    "```armada-plan\n" +
                    "{\"missions\":[{\"title\":\"A\",\"dependsOn\":1},{\"title\":\"B\",\"dependsOn\":3},{\"title\":\"C\",\"dependsOn\":0},{\"title\":\"A\",\"dependsOn\":1}]}\n" +
                    "```";
                AssertTrue(ArchitectPlanParser.TryParse(output, out ArchitectPlan? plan));
                AssertEqual(3, plan!.Missions.Count, "a repeated title is dropped");
                AssertNull(plan.Missions[0].DependsOn, "self dependency");
                AssertNull(plan.Missions[1].DependsOn, "forward dependency");
                AssertNull(plan.Missions[2].DependsOn, "zero index");
            }));

            cases.Add(Case("no_plan_without_valid_block", "Malformed JSON, unfenced JSON, other fences, and placeholder-only examples yield no plan", TestTags.Negative, () =>
            {
                AssertFalse(ArchitectPlanParser.TryParse(null, out _));
                AssertFalse(ArchitectPlanParser.TryParse("{\"missions\":[{\"title\":\"A\"}]}", out _), "unfenced JSON");
                AssertFalse(ArchitectPlanParser.TryParse("```json\n{\"missions\":[{\"title\":\"A\"}]}\n```", out _), "other info string");
                AssertFalse(ArchitectPlanParser.TryParse("```armada-plan\n{\"missions\":[{\"title\":\"A\",\"dependsOn\":\"Mission 1\"}]}\n```", out _), "dependsOn must be a number");
                AssertFalse(ArchitectPlanParser.TryParse("```armada-plan\nnot json\n```", out _), "malformed");
                AssertFalse(ArchitectPlanParser.TryParse("```armada-plan\n{\"missions\":[{\"title\":\"<title>\",\"description\":\"<what to do>\"}]}\n```", out _), "echoed prompt example");
                AssertFalse(ArchitectPlanParser.TryParse("```armada-plan\n{\"missions\":[{\"title\":\"A\"}]}", out _), "unterminated block");
            }));

            cases.Add(Case("format_instructions_name_the_fence", "The prompt contract names the fence and the fields", TestTags.Positive, () =>
            {
                string text = ArchitectPlanParser.FormatInstructions();
                AssertContains("armada-plan", text);
                AssertContains("dependsOn", text);
                AssertContains("waitForOtherMissions", text);
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.ArchitectPlanParser",
                displayName: "Architect Plan Parser",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.ArchitectPlanParser",
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
