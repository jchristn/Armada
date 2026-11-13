namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
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
                AssertNull(plan.Missions[0].Parent);
                AssertEqual(1, plan.Missions[1].Parent, "a legacy dependsOn is read as the parent");
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
                AssertEqual(1, plan.Missions[1].Parent, "original mission 2 (Core) is now mission 1");
            }));

            cases.Add(Case("invalid_dependencies_are_cleared", "Self, forward, and out-of-range dependencies are cleared", TestTags.Negative, () =>
            {
                string output =
                    "```armada-plan\n" +
                    "{\"missions\":[{\"title\":\"A\",\"dependsOn\":1},{\"title\":\"B\",\"dependsOn\":3},{\"title\":\"C\",\"dependsOn\":0},{\"title\":\"A\",\"dependsOn\":1}]}\n" +
                    "```";
                AssertTrue(ArchitectPlanParser.TryParse(output, out ArchitectPlan? plan));
                AssertEqual(3, plan!.Missions.Count, "a repeated title is dropped");
                AssertNull(plan.Missions[0].Parent, "self dependency");
                AssertNull(plan.Missions[1].Parent, "forward dependency");
                AssertNull(plan.Missions[2].Parent, "zero index");
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
                AssertContains("contractVersion:4", text);
                AssertContains("parent:earlier 1-based index", text);
                AssertContains("ownedPaths", text);
                AssertContains("waitForOtherMissions", text);
            }));

            cases.Add(Case("descriptive_requirement_ids_are_rejected_actionably", "An ID plus description cannot match its bare reference; errors name the mismatch once without normalizing the plan", TestTags.Negative, () =>
            {
                ArchitectPlan input = ValidContract();
                input.Requirements[0] = "REQ-READ: handle HTTP errors";
                input.Missions[1].AcceptanceCriteria.Add(input.Missions[0].AcceptanceCriteria[0]);
                input.Missions[1].AcceptanceCriteria[^1] = new ArchitectAcceptanceCriterion
                {
                    Id = "AC-READ-AGAIN", RequirementIds = new List<string> { "REQ-READ" }, Scenario = "Repeat coverage",
                    Expected = "HTTP errors rejected", Verification = "HTTP fixture", NegativeCases = new List<string> { "HTTP 401 fails" }
                };
                AssertFalse(ArchitectPlanParser.TryParse(Fence(input), out ArchitectPlan? parsed, out string? error));
                AssertNull(parsed);
                AssertContains("unknown objective requirement ID \"REQ-READ\"", error!);
                AssertContains("requirements contains IDs only", error!);
                AssertEqual(1, error!.Split("unknown objective requirement ID \"REQ-READ\"").Length - 1, "Repeated references need one actionable ID diagnostic.");
                AssertContains("requirements:[\"REQ-1\"]", ArchitectPlanParser.FormatInstructions());
                AssertContains("match exactly", ArchitectPlanParser.FormatInstructions());
                AssertEqual("REQ-READ: handle HTTP errors", input.Requirements[0], "Never silently rewrite a rejected plan.");
            }));

            cases.Add(Case("versioned_contract_preserves_evidence", "Versioned plans retain objective traceability and evidence in every slice", TestTags.Positive, () =>
            {
                ArchitectPlan input = ValidContract();
                AssertTrue(ArchitectPlanParser.TryParse(Fence(input), out ArchitectPlan? plan, out string? error));
                AssertNull(error);
                string description = ArchitectPlanParser.DescribeMission(plan!, plan!.Missions[0]);
                AssertContains("AC-READ", description);
                AssertContains("REQ-READ", description);
                AssertContains("HTTP 401 must report disconnected", description);
                AssertContains("Capture before request", description);
                AssertContains("dotnet test", description);
                AssertContains("explicit false is retained", description);
            }));

            cases.Add(Case("slice_decisions_are_scoped", "Explicit decision indexes omit unrelated context without dropping acceptance evidence", TestTags.Positive, () =>
            {
                ArchitectPlan input = ValidContract();
                input.Decisions.Add("Unrelated dashboard design decision");
                input.Missions[0].DecisionIndexes = new List<int> { 1 };
                AssertTrue(ArchitectPlanParser.TryParse(Fence(input), out ArchitectPlan? plan, out _));
                string description = ArchitectPlanParser.DescribeMission(plan!, plan!.Missions[0]);
                AssertContains("explicit false is retained", description);
                AssertFalse(description.Contains("Unrelated dashboard"));
                AssertContains("AC-READ", description);
                AssertContains("HTTP 401 must report disconnected", description);
                AssertContains("Capture before request", description);
                AssertContains("dotnet test", description);
                AssertContains("Unrelated dashboard", ArchitectPlanParser.DescribeMission(plan, plan.Missions[1]), "Omitted indexes preserve older plans");
                plan.Missions[0].DecisionIndexes!.Clear();
                description = ArchitectPlanParser.DescribeMission(plan, plan.Missions[0]);
                AssertFalse(description.Contains("explicit false is retained"));
                AssertContains("AC-READ", description);
            }));

            cases.Add(Case("v4_rejects_parallel_ownership_overlap", "A v4 plan whose parallel siblings own the same file is rejected and the message names both missions", TestTags.Negative, () =>
            {
                ArchitectPlan plan = OwnershipPlan();
                plan.Missions[1].OwnedPaths = new List<string> { "src/Foo.cs" };
                plan.Missions[2].OwnedPaths = new List<string> { "./src/Foo.cs" };
                AssertFalse(ArchitectPlanParser.TryParse(Fence(plan), out ArchitectPlan? parsed, out string? error));
                AssertNull(parsed);
                AssertContains("Missions 2 and 3 can run in parallel but both own src/Foo.cs", error!);
                AssertContains("make one the parent of the other", error!);

                ArchitectPlan prefix = OwnershipPlan();
                prefix.Missions[1].OwnedPaths = new List<string> { "src/" };
                prefix.Missions[2].OwnedPaths = new List<string> { "src/Bar/*.cs" };
                AssertFalse(ArchitectPlanParser.TryParse(Fence(prefix), out _, out error), "a directory prefix overlaps a pattern below it");
                AssertContains("src/ and src/Bar/*.cs", error!);
            }));

            cases.Add(Case("v4_parent_child_overlap_and_disjoint_siblings_accepted", "A parent and its child may own the same file; disjoint siblings and the final integration mission are accepted", TestTags.Positive, () =>
            {
                ArchitectPlan plan = OwnershipPlan();
                plan.Missions[1].Parent = 1;
                plan.Missions[1].BranchStrategy = "fork";
                plan.Missions[1].OwnedPaths = new List<string> { "src/Core/" };
                plan.Missions[3].OwnedPaths = new List<string> { "src/Core/Shared.cs", "CHANGELOG.md" };
                AssertTrue(ArchitectPlanParser.TryParse(Fence(plan), out ArchitectPlan? parsed, out string? error), error ?? "");
                AssertEqual(1, parsed!.Missions[1].Parent);
                AssertEqual("fork", parsed.Missions[1].BranchStrategy);
                AssertContains("## Owned paths (change only these)\n- src/Core/", ArchitectPlanParser.DescribeMission(parsed, parsed.Missions[1]));
            }));

            cases.Add(Case("v4_parent_rules", "Self, forward and too-deep parents and an unknown or parentless branchStrategy are rejected", TestTags.Negative, () =>
            {
                List<Action<ArchitectPlan>> defects = new List<Action<ArchitectPlan>>
                {
                    p => p.Missions[1].Parent = 2,
                    p => p.Missions[1].Parent = 3,
                    p => p.Missions[1].Parent = 0,
                    p => { p.Missions[1].Parent = 1; p.Missions[1].BranchStrategy = "sideways"; },
                    p => p.Missions[2].BranchStrategy = "continue",
                    p => { p.Missions[1].Parent = 1; p.Missions[1].DependsOn = 2; }
                };
                foreach (Action<ArchitectPlan> defect in defects)
                {
                    ArchitectPlan plan = OwnershipPlan();
                    defect(plan);
                    AssertFalse(ArchitectPlanParser.TryParse(Fence(plan), out ArchitectPlan? parsed, out string? error), "defect accepted");
                    AssertNull(parsed);
                }
                ArchitectPlan deep = OwnershipPlan();
                deep.Missions[1].Parent = 1;
                deep.Missions[2].Parent = 2;
                AssertTrue(ArchitectPlanParser.TryParse(Fence(deep), out _, out _, 2), "depth 2 within a maximum of 2");
                AssertFalse(ArchitectPlanParser.TryParse(Fence(deep), out _, out string? depthError, 1), "depth 2 over a maximum of 1");
                AssertContains("the maximum is 1", depthError!);
            }));

            cases.Add(Case("v3_dependsOn_maps_to_parent_with_warning", "A v3 dependsOn is read as the parent with a warning, and a v3 ownership overlap only warns", TestTags.Positive, () =>
            {
                ArchitectPlan plan = OwnershipPlan();
                plan.ContractVersion = 3;
                plan.Missions[1].DependsOn = 1;
                plan.Missions[2].OwnedPaths = new List<string> { "src/Foo.cs" };
                plan.Missions[0].OwnedPaths = new List<string> { "src/Foo.cs" };
                AssertTrue(ArchitectPlanParser.TryParse(Fence(plan), out ArchitectPlan? parsed, out string? error), error ?? "");
                AssertEqual(1, parsed!.Missions[1].Parent);
                AssertNull(parsed.Missions[1].DependsOn);
                AssertTrue(parsed.Warnings.Any(w => w.Contains("dependsOn 1 was read as parent 1")), "mapping warning");
                AssertTrue(parsed.Warnings.Any(w => w.Contains("Missions 1 and 3 can run in parallel")), "v3 overlap is a warning");
            }));

            cases.Add(Case("owned_path_matching", "Owned paths overlap on equal files, directory prefixes and patterns, and not otherwise", TestTags.Positive, () =>
            {
                AssertTrue(OwnedPathMatcher.Overlaps("src/a.cs", "src/a.cs"));
                AssertTrue(OwnedPathMatcher.Overlaps("src/", "src/a.cs"));
                AssertTrue(OwnedPathMatcher.Overlaps("src", "src/a.cs"), "a bare directory name covers its files");
                AssertTrue(OwnedPathMatcher.Overlaps("src\\a.cs", "./src/a.cs"), "slashes and ./ are normalized");
                AssertTrue(OwnedPathMatcher.Overlaps("src/*.cs", "src/a.cs"));
                AssertFalse(OwnedPathMatcher.Overlaps("src/*.cs", "src/sub/a.cs"), "* stays within one segment");
                AssertTrue(OwnedPathMatcher.Overlaps("src/**", "src/sub/a.cs"));
                AssertTrue(OwnedPathMatcher.Overlaps("src/*.cs", "src/*.ts"), "two patterns with a shared literal prefix overlap conservatively");
                AssertFalse(OwnedPathMatcher.Overlaps("src/a.cs", "src/ab.cs"));
                AssertFalse(OwnedPathMatcher.Overlaps("docs/", "src/docs/a.md"));
                AssertFalse(OwnedPathMatcher.Overlaps("", "src/a.cs"));
            }));

            cases.Add(Case("versioned_contract_rejects_unsafe_plans", "Incomplete coverage, vacuous checks, invalid dependencies and missing integration never dispatch", TestTags.Negative, () =>
            {
                List<Action<ArchitectPlan>> defects = new List<Action<ArchitectPlan>>
                {
                    p => p.ContractVersion = 99,
                    p => p.Requirements.Add("UNOWNED"),
                    p => p.Decisions.Clear(),
                    p => p.Missions[0].DecisionIndexes = new List<int> { 0 },
                    p => p.Missions[0].DecisionIndexes = new List<int> { 2 },
                    p => p.Missions[0].DecisionIndexes = new List<int> { 1, 1 },
                    p => p.Missions[0].DependsOn = 2,
                    p => p.Missions[0].WaitForOtherMissions = true,
                    p => p.Missions[0].ValidationCommands.Clear(),
                    p => p.Missions[0].AcceptanceCriteria[0].Verification = "",
                    p => p.Missions[0].AcceptanceCriteria[0].NegativeCases.Clear(),
                    p => p.Missions[0].AcceptanceCriteria[0].RequirementIds = new List<string> { "invented" },
                    p => p.Missions[1].AcceptanceCriteria[0].Id = "AC-READ",
                    p => p.Missions[1].WaitForOtherMissions = false,
                    p => p.Missions[1].Kind = "implementation",
                    p => p.Missions.Add(null!),
                    p => p.Missions[0].AcceptanceCriteria.Add(null!),
                    p => p.Missions[0].AcceptanceCriteria[0].NegativeCases = null!,
                    p => p.Missions[0].Title = "<title>",
                    p => p.Missions[1].Title = p.Missions[0].Title
                };
                foreach (Action<ArchitectPlan> defect in defects)
                {
                    ArchitectPlan plan = ValidContract();
                    defect(plan);
                    AssertFalse(ArchitectPlanParser.TryParse(Fence(plan), out ArchitectPlan? parsed, out string? error));
                    AssertNull(parsed);
                    AssertContains("Remediation", error!);
                }
            }));

            cases.Add(Case("invalid_revision_cannot_use_old_plan", "An invalid final plan cannot silently dispatch an earlier valid block", TestTags.Negative, () =>
            {
                string valid = Fence(ValidContract());
                AssertFalse(ArchitectPlanParser.TryParse(valid + "\n```armada-plan\ninvalid\n```", out _, out string? error));
                AssertContains("JSON is invalid", error!);
                ArchitectPlan broken = ValidContract();
                broken.Missions[1].WaitForOtherMissions = false;
                AssertFalse(ArchitectPlanParser.TryParse(valid + "\n" + Fence(broken), out _, out error));
                AssertContains("last mission", error!);
            }));

            cases.Add(Case("decision_checks_are_traceable", "Version 3 links named checks to numbered slice decisions and permits explicit general checks", TestTags.Positive, () =>
            {
                ArchitectPlan input = ValidDecisionContract();
                input.Decisions.Add("Optional transport injection must be available.");
                input.Missions[0].DecisionIndexes = new List<int> { 2 };
                input.Missions[0].AcceptanceCriteria[0].DecisionIndexes = new List<int> { 2 };
                AssertTrue(ArchitectPlanParser.TryParse(Fence(input), out ArchitectPlan? plan, out string? error), error);
                string description = ArchitectPlanParser.DescribeMission(plan!, plan!.Missions[0]);
                AssertContains("- D2: Optional transport injection", description);
                AssertContains("Decisions checked: D2", description);
                AssertFalse(description.Contains("- D1:"), "unrelated decisions stay outside this slice");
                plan.Missions[0].AcceptanceCriteria[0].DecisionIndexes!.Clear();
                AssertContains("none (general check)", ArchitectPlanParser.DescribeMission(plan, plan.Missions[0]));
            }));

            cases.Add(Case("orphaned_or_mislinked_decisions_reject_plan", "Missing, duplicate, invalid, out-of-scope or orphaned decision links reject version 3 with remediation", TestTags.Negative, () =>
            {
                List<Action<ArchitectPlan>> defects = new List<Action<ArchitectPlan>>
                {
                    p => p.Missions[0].AcceptanceCriteria[0].DecisionIndexes = null,
                    p => p.Missions[0].AcceptanceCriteria[0].DecisionIndexes = new List<int> { 0 },
                    p => p.Missions[0].AcceptanceCriteria[0].DecisionIndexes = new List<int> { 2 },
                    p => p.Missions[0].AcceptanceCriteria[0].DecisionIndexes = new List<int> { 1, 1 },
                    p => p.Missions[0].DecisionIndexes = new List<int>(),
                    p => p.Decisions.Add("Missing injection interface"),
                    p => { foreach (ArchitectPlanMission m in p.Missions) m.AcceptanceCriteria[0].DecisionIndexes!.Clear(); }
                };
                foreach (Action<ArchitectPlan> defect in defects)
                {
                    ArchitectPlan input = ValidDecisionContract();
                    defect(input);
                    AssertFalse(ArchitectPlanParser.TryParse(Fence(input), out ArchitectPlan? plan, out string? error));
                    AssertNull(plan);
                    AssertContains("decision", error!);
                    AssertContains("Remediation", error!);
                }
                ArchitectPlan orphaned = ValidDecisionContract();
                orphaned.Decisions.Add("Missing injection interface");
                AssertFalse(ArchitectPlanParser.TryParse(Fence(orphaned), out _, out string? diagnostic));
                AssertContains("decision D2 has no acceptance check", diagnostic!);
                AssertFalse(ArchitectPlanParser.TryParse(Fence(ValidDecisionContract()) + "\n" + Fence(orphaned), out _), "invalid final revision never reuses an older plan");
            }));

            cases.Add(Case("negative_cases_can_be_inapplicable", "A specific non-applicability reason is accepted instead of fabricated failure cases", TestTags.Positive, () =>
            {
                ArchitectPlan input = ValidContract();
                input.Missions[0].AcceptanceCriteria[0].NegativeCases.Clear();
                input.Missions[0].AcceptanceCriteria[0].NegativeCasesNotApplicableReason = "Static manifest-only check; no execution path changes.";
                AssertTrue(ArchitectPlanParser.TryParse(Fence(input), out _, out _));
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.ArchitectPlanParser",
                displayName: "Architect Plan Parser",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static string Fence(ArchitectPlan plan) => "```armada-plan\n" + JsonSerializer.Serialize(plan) + "\n```";

        // v4: an implementation root, two parallel siblings and the final integration mission, all with disjoint paths.
        private static ArchitectPlan OwnershipPlan()
        {
            ArchitectPlan plan = ValidDecisionContract();
            plan.ContractVersion = 4;
            ArchitectPlanMission integration = plan.Missions[1];
            integration.DependsOn = null;
            ArchitectPlanMission template = plan.Missions[0];
            plan.Missions.Clear();
            string[] titles = { "Core", "Sibling A", "Sibling B" };
            for (int i = 0; i < titles.Length; i++)
            {
                ArchitectPlanMission mission = JsonSerializer.Deserialize<ArchitectPlanMission>(JsonSerializer.Serialize(template))!;
                mission.Title = titles[i];
                mission.DependsOn = null;
                mission.OwnedPaths = new List<string> { "src/" + titles[i].Replace(" ", "") + ".cs" };
                mission.AcceptanceCriteria[0].Id = "AC-" + i;
                plan.Missions.Add(mission);
            }
            integration.OwnedPaths = new List<string> { "CHANGELOG.md" };
            plan.Missions.Add(integration);
            return plan;
        }

        private static ArchitectPlan ValidDecisionContract()
        {
            ArchitectPlan plan = ValidContract();
            plan.ContractVersion = 3;
            foreach (ArchitectPlanMission mission in plan.Missions)
                mission.AcceptanceCriteria[0].DecisionIndexes = new List<int> { 1 };
            return plan;
        }

        private static ArchitectPlan ValidContract()
        {
            ArchitectPlan plan = new ArchitectPlan
            {
                ContractVersion = 2,
                Requirements = new List<string> { "REQ-READ" },
                Decisions = new List<string> { "Defaults only apply when absent; explicit false is retained." }
            };
            for (int i = 0; i < 2; i++) plan.Missions.Add(new ArchitectPlanMission
            {
                Title = i == 0 ? "Discovery" : "Integration",
                Description = "Verify inventory and connection status.",
                Kind = i == 0 ? "implementation" : "integration",
                DependsOn = i == 0 ? null : 1,
                WaitForOtherMissions = i == 1,
                ValidationCommands = new List<string> { "dotnet test" },
                AcceptanceCriteria = new List<ArchitectAcceptanceCriterion> { new ArchitectAcceptanceCriterion
                {
                    Id = i == 0 ? "AC-READ" : "AC-INTEGRATE",
                    RequirementIds = new List<string> { "REQ-READ" },
                    Scenario = "List inventory through local HTTP",
                    Expected = "Authorized inventory is returned",
                    Verification = "Capture before request; assert request and response",
                    NegativeCases = new List<string> { "HTTP 401 must report disconnected" }
                } }
            });
            return plan;
        }

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
