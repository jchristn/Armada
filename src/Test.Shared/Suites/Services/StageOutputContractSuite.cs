namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The one output contract source (RS-6.1): every generated contract matches the parser that reads it. The Architect
    /// example parses as a valid v4 plan, every Judge verdict line parses to its verdict, every result contract ends with
    /// the line the completion check reads, and no contract shows a marker in backticks.
    /// </summary>
    public sealed class StageOutputContractSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.StageOutputContract";

        private static readonly string[] _ResultPersonas = new[]
        {
            PersonaCatalog.Worker, PersonaCatalog.TestEngineer, PersonaCatalog.Linter, PersonaCatalog.ProductManager, PersonaCatalog.UsabilityEngineer
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("architect_example_parses_as_a_valid_v4_plan", "The Architect contract's example is a complete plan the real parser accepts", TestTags.Positive, () =>
            {
                string contract = StageOutputContract.For(PersonaCatalog.Architect);
                AssertEqual(1, StageOutputContract.CountContracts(contract), "one heading");
                AssertTrue(ArchitectPlanParser.TryParse(contract, out ArchitectPlan? plan, out string? error), "the contract parses: " + error);
                AssertEqual(StageOutputContract.PlanContractVersion, plan!.ContractVersion, "contract version");
                AssertEqual(2, plan.Missions.Count, "two slices");
                AssertEqual("integration", plan.Missions.Last().Kind, "the last slice is integration");
                AssertTrue(plan.Missions.Last().WaitForOtherMissions, "the integration slice waits");
                AssertEqual(0, plan.Warnings.Count, "no warnings: " + String.Join("; ", plan.Warnings));
            }));

            cases.Add(Case("architect_field_table_covers_the_model", "The field table lists every prompted model field, and the example writes every one except the optional branch strategy", TestTags.Positive, () =>
            {
                string contract = StageOutputContract.For(PersonaCatalog.Architect);
                string example = StageOutputContract.ArchitectPlanExample();
                IReadOnlyList<string> paths = StageOutputContract.PlanFieldPaths();
                AssertTrue(paths.Contains("missions[].ownedPaths"), "nested fields are listed");
                AssertTrue(paths.Contains("missions[].acceptanceCriteria[].negativeCases"), "criterion fields are listed");
                foreach (string hidden in StageOutputContract.PlanFieldsNotPrompted)
                {
                    AssertFalse(paths.Contains(hidden), hidden + " is not prompted");
                    string name = hidden.Substring(hidden.LastIndexOf('.') + 1);
                    AssertFalse(example.Contains("\"" + name + "\"", StringComparison.Ordinal), hidden + " is not in the example");
                }
                foreach (string path in paths)
                {
                    AssertContains("| " + path + " |", contract, path + " has a table row");
                    string name = path.Substring(path.LastIndexOf('.') + 1).Replace("[]", String.Empty);
                    if (name == "branchStrategy") continue;
                    AssertContains("\"" + name + "\"", example, path + " appears in the example");
                }
            }));

            cases.Add(Case("judge_verdict_lines_parse_to_their_verdicts", "Every verdict line the Judge contract lists parses to that verdict, and every verdict except None is listed", TestTags.Positive, () =>
            {
                string contract = StageOutputContract.For(PersonaCatalog.Judge);
                List<JudgeVerdictEnum> all = Enum.GetValues(typeof(JudgeVerdictEnum)).Cast<JudgeVerdictEnum>().Where(v => v != JudgeVerdictEnum.None).ToList();
                AssertEqual(String.Join(",", all), String.Join(",", StageOutputContract.AcceptedVerdicts), "accepted verdicts follow the enum");
                foreach (JudgeVerdictEnum verdict in all)
                {
                    string line = StageOutputContract.VerdictLine(verdict);
                    AssertContains("\n" + line + "\n", contract, verdict + " is a standalone line");
                    AssertEqual(verdict, JudgeVerdictParser.ParseLine(line), verdict + " parses");
                    AssertEqual(line, ContractRepair.CanonicalVerdictLine(verdict), verdict + " repair writes the same line");
                    AssertEqual(verdict, JudgeVerdictParser.Parse("## Verdict\nok\n" + line), verdict + " parses as the last line of an output");
                }
                AssertEqual("NEEDS_REVISION", StageOutputContract.VerdictToken(JudgeVerdictEnum.NeedsRevision), "underscored token");
                AssertEqual("SPEC_CONFLICT", StageOutputContract.VerdictToken(JudgeVerdictEnum.SpecConflict), "underscored token");
                foreach (string section in StageOutputContract.RequiredSections(PersonaCatalog.Judge))
                    AssertContains("- ## " + section + "\n", contract, section + " section");
                AssertContains(JudgeContract.AffectedCaseSection, contract, "affected case rule");
                AssertContains(JudgeContract.SpecConflictSection, contract, "spec conflict rule");
                AssertFalse(contract.Contains(StageOutputContract.ResultMarker, StringComparison.Ordinal), "a Judge never reports a result line");
            }));

            cases.Add(Case("none_verdict_has_no_line", "None and undefined verdicts have no canonical line", TestTags.Negative, () =>
            {
                AssertThrows<ArgumentOutOfRangeException>(() => StageOutputContract.VerdictLine(JudgeVerdictEnum.None), "None");
                AssertThrows<ArgumentOutOfRangeException>(() => ContractRepair.CanonicalVerdictLine(JudgeVerdictEnum.None), "repair None");
                AssertThrows<ArgumentOutOfRangeException>(() => StageOutputContract.VerdictToken((JudgeVerdictEnum)99), "undefined");
            }));

            cases.Add(Case("result_contracts_end_with_the_completion_line", "Every result contract ends with the standalone completion line the completion check reads, after its sections", TestTags.Positive, () =>
            {
                foreach (string persona in _ResultPersonas)
                {
                    string contract = StageOutputContract.For(persona);
                    AssertEqual(1, StageOutputContract.CountContracts(contract), persona + " one heading");
                    AssertTrue(contract.EndsWith("\n" + StageOutputContract.ResultCompleteLine + "\n", StringComparison.Ordinal), persona + " ends with the completion line");
                    AssertTrue(CaptainBlockedContract.HasResultLine(contract), persona + " completion line is read by the completion check");
                    AssertFalse(contract.Contains(StageOutputContract.VerdictMarker, StringComparison.Ordinal), persona + " has no verdict line");
                    foreach (string section in StageOutputContract.RequiredSections(persona))
                        AssertContains("- ## " + section + "\n", contract, persona + " " + section);
                }
                AssertContains(VerifierContract.BlockedMarker, StageOutputContract.For(PersonaCatalog.TestEngineer), "verifiers may block");
                AssertFalse(StageOutputContract.For(PersonaCatalog.Worker).Contains(VerifierContract.BlockedMarker, StringComparison.Ordinal), "the Worker is not a verifier");
                AssertEqual(String.Empty, StageOutputContract.For(PersonaCatalog.Recorder), "the Recorder has no contract");
                AssertEqual(String.Empty, StageOutputContract.For("Custom Persona"), "an unknown persona has no contract");
            }));

            cases.Add(Case("contracts_show_markers_plainly", "No contract or role summary shows a marker in backticks or uses an em dash", TestTags.Negative, () =>
            {
                foreach (string persona in _ResultPersonas.Concat(new[] { PersonaCatalog.Judge, PersonaCatalog.Architect }))
                {
                    foreach (string text in new[] { StageOutputContract.For(persona), StageOutputContract.RoleSummary(persona) })
                    {
                        AssertFalse(text.Contains("`[ARMADA:", StringComparison.Ordinal), persona + " marker in backticks");
                        AssertFalse(text.Contains("\u2014", StringComparison.Ordinal), persona + " em dash");
                    }
                }
            }));

            cases.Add(Case("append_states_the_contract_once", "Appending adds the contract after the prompt once, and leaves a persona without one unchanged", TestTags.Positive, () =>
            {
                string prompt = StageOutputContract.AppendTo(PersonaCatalog.Worker, "You are a worker.\n\n");
                AssertEqual("You are a worker.\n\n" + StageOutputContract.For(PersonaCatalog.Worker), prompt, "contract after one blank line");
                AssertEqual(StageOutputContract.For(PersonaCatalog.Judge), StageOutputContract.AppendTo(PersonaCatalog.Judge, null), "an empty prompt is the contract");
                AssertEqual("notes", StageOutputContract.AppendTo(PersonaCatalog.Recorder, "notes"), "the Recorder prompt is unchanged");
                AssertEqual(0, StageOutputContract.CountContracts(null), "null text");
            }));

            cases.Add(Case("dotnet_validation_advice_comes_from_the_workflow_profile", "The .NET restore advice comes from an active .NET workflow profile, not from any persona contract", TestTags.Positive, () =>
            {
                WorkflowProfile dotnet = new WorkflowProfile { SourceTemplate = WorkflowProfileTemplates.DotNet, Active = true };
                WorkflowProfile hinted = new WorkflowProfile { SourceTemplate = "custom", LanguageHints = new List<string> { ".NET" }, Active = true };
                WorkflowProfile inactive = new WorkflowProfile { SourceTemplate = WorkflowProfileTemplates.DotNet, Active = false };
                WorkflowProfile node = new WorkflowProfile { SourceTemplate = WorkflowProfileTemplates.Node, Active = true };
                AssertEqual(WorkflowProfileTemplates.DotNetValidationGuidance, WorkflowProfileTemplates.ValidationGuidance(new[] { dotnet }), "template key");
                AssertEqual(WorkflowProfileTemplates.DotNetValidationGuidance, WorkflowProfileTemplates.ValidationGuidance(new[] { hinted }), "language hint");
                AssertEqual(String.Empty, WorkflowProfileTemplates.ValidationGuidance(new[] { inactive, node }), "inactive or other toolchains");
                AssertEqual(String.Empty, WorkflowProfileTemplates.ValidationGuidance(null), "no profiles");
                foreach (string persona in _ResultPersonas.Concat(new[] { PersonaCatalog.Judge, PersonaCatalog.Architect }))
                    AssertFalse(StageOutputContract.For(persona).Contains("--no-restore", StringComparison.Ordinal), persona + " contract is toolchain neutral");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Stage Output Contract",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
