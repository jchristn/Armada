namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The one prompt composer (RS-6.11): the whole-file budget trims sections in their declared order and never trims the
    /// role, description or contract; the persona template's declared slots, not its rendered text, decide where the
    /// handoff goes; and an edited template without the slot still gets the handoff once.
    /// </summary>
    public sealed class InstructionsComposerSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.InstructionsComposer";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("budget_trims_in_rank_order", "Over budget, the highest-ranked section is trimmed first and trimming stops once the file fits", TestTags.Positive, () =>
            {
                List<InstructionSection> sections = Sections();
                long untrimmed = InstructionsComposer.Compose(sections, null).EstimatedTokens;
                // Room for everything except roughly the papercuts and the skills.
                long budget = untrimmed - 900;
                InstructionsComposition composed = InstructionsComposer.Compose(sections, budget);
                AssertEqual("papercuts", composed.TrimmedSections[0], "papercuts go first");
                AssertEqual("skills", composed.TrimmedSections[1], "skills go next");
                AssertFalse(composed.TrimmedSections.Contains("playbooks"), "playbooks are trimmed last and were not needed");
                AssertTrue(composed.EstimatedTokens <= budget, "fits: " + composed.EstimatedTokens);
                AssertContains("The papercut directive was left out", composed.Text, "a trimmed section leaves its note");
                AssertContains("AC-1: reject empty input", composed.Text, "acceptance criteria kept");
                AssertTrue(composed.Text.EndsWith("[ARMADA:RESULT] COMPLETE\n", StringComparison.Ordinal), "the contract still closes the file");
                AssertFalse(composed.OverBudget);
                return Task.CompletedTask;
            }));

            cases.Add(Case("budget_never_trims_fixed_sections", "A budget smaller than the fixed sections trims every trimmable one, keeps the description and contract, and reports over budget", TestTags.Negative, () =>
            {
                InstructionsComposition composed = InstructionsComposer.Compose(Sections(), 10);
                AssertEqual(4, composed.TrimmedSections.Count, "every trimmable section trimmed");
                AssertEqual("playbooks", composed.TrimmedSections[3], "playbooks last");
                AssertContains("AC-1: reject empty input", composed.Text);
                AssertContains("## Output Contract", composed.Text);
                AssertTrue(composed.OverBudget, "still over budget");

                InstructionsComposition unbounded = InstructionsComposer.Compose(Sections(), null, 5000);
                AssertEqual(0, unbounded.TrimmedSections.Count, "an unknown context trims nothing");
                AssertNull(unbounded.BudgetTokens);
                return Task.CompletedTask;
            }));

            cases.Add(Case("budget_from_context_less_tool_schemas", "The budget is a quarter of the served context less the tool schemas, with a floor; unknown context has no budget", TestTags.Positive, () =>
            {
                AssertEqual(32768L / 4 - 4000, InstructionsComposer.ResolveBudget(new HandoffReader { ContextTokens = 32768 }, 4000));
                AssertEqual(InstructionsComposer.MinimumBudgetTokens, InstructionsComposer.ResolveBudget(new HandoffReader { ContextTokens = 8192 }, 4000), "floor");
                AssertNull(InstructionsComposer.ResolveBudget(new HandoffReader(), 4000), "unknown context");
                AssertNull(InstructionsComposer.ResolveBudget(null, 0), "no reader");
                long judge = PersonaToolScope.EstimateSchemaTokens(PersonaCatalog.Judge, true, true);
                AssertEqual(PersonaToolScope.BuiltInToolSchemaTokens + (PersonaToolScope.ForPersona(PersonaCatalog.Judge).Count + 1) * PersonaToolScope.ArmadaToolSchemaTokens, judge, "built-in plus scoped plus discovery");
                AssertEqual(0L, PersonaToolScope.EstimateSchemaTokens(PersonaCatalog.Judge, false, true), "no tools, no schemas");
                return Task.CompletedTask;
            }));

            cases.Add(Case("declared_slot_carries_handoff", "A template that declares {PreviousStageOutput} carries the evidence; the description gets a note, not a second copy", TestTags.Positive, () =>
            {
                HandoffRendering handoff = Rendering();
                Dictionary<string, string> templateParams = Params("Review it." + Placeholder());
                string? appended = MissionService.DeliverHandoff(templateParams, handoff, "Review.\n{PreviousStageOutput}\n{Diff}\n");
                AssertNull(appended, "nothing appended");
                AssertContains(MissionService.HandoffInRoleNote, templateParams["MissionDescription"]);
                AssertFalse(templateParams["MissionDescription"].Contains("EVIDENCE-TEXT", StringComparison.Ordinal), "evidence not repeated in the description");
                return Task.CompletedTask;
            }));

            cases.Add(Case("edited_template_without_slot_gets_handoff_once", "An operator-edited template without the slot gets the handoff once: in the description placeholder, or appended when there is none", TestTags.Reliability, () =>
            {
                HandoffRendering handoff = Rendering();
                Dictionary<string, string> withPlaceholder = Params("Review it." + Placeholder());
                AssertNull(MissionService.DeliverHandoff(withPlaceholder, handoff, "My own judge brief, no slots."), "delivered in the description");
                AssertEqual(1, Count(withPlaceholder["MissionDescription"], "EVIDENCE-TEXT"), "evidence once");
                AssertEqual(1, Count(withPlaceholder["MissionDescription"], "DIFF-TEXT"), "diff once");

                Dictionary<string, string> noPlaceholder = Params("Review it.");
                string? appended = MissionService.DeliverHandoff(noPlaceholder, handoff, "My own judge brief, no slots.");
                AssertNotNull(appended, "appended by the composer");
                AssertTrue(appended!.StartsWith("## Prior Stage Output\n", StringComparison.Ordinal));
                AssertEqual(1, Count(appended, "EVIDENCE-TEXT"));
                AssertEqual("Review it.", noPlaceholder["MissionDescription"], "description unchanged");

                Dictionary<string, string> diffOnly = Params("Review it." + Placeholder());
                MissionService.DeliverHandoff(diffOnly, handoff, "Diff here: {Diff}");
                AssertEqual(1, Count(diffOnly["MissionDescription"], "EVIDENCE-TEXT"), "evidence in the description");
                AssertEqual(0, Count(diffOnly["MissionDescription"], "DIFF-TEXT"), "the diff is the template's");
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "One prompt composer and instructions budget",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static List<InstructionSection> Sections()
        {
            string filler = new string('x', 2000);
            return new List<InstructionSection>
            {
                new InstructionSection("playbooks", "## Playbooks\n" + filler + "\n", InstructionsComposer.PlaybooksRank, InstructionsComposer.TrimNote("The selected playbooks")),
                new InstructionSection("skills", "## Skills\n" + filler + "\n", InstructionsComposer.SkillsRank, InstructionsComposer.TrimNote("The Skills section")),
                new InstructionSection("mission", "# Mission Instructions\nFix the parser.\n## Acceptance criteria\n- AC-1: reject empty input\n"),
                new InstructionSection("git anchors", "## Git anchors\n" + filler + "\n", InstructionsComposer.GitAnchorsRank, InstructionsComposer.TrimNote("The git anchors section")),
                new InstructionSection("papercuts", "## Papercuts\n" + filler + "\n", InstructionsComposer.PapercutsRank, InstructionsComposer.TrimNote("The papercut directive")),
                new InstructionSection("output contract", "\n\n" + StageOutputContract.For(PersonaCatalog.Worker))
            };
        }

        private static HandoffRendering Rendering()
        {
            HandoffRendering rendering = new HandoffRendering();
            rendering.Evidence = "EVIDENCE-TEXT\n";
            rendering.Diff = "DIFF-TEXT\n";
            return rendering;
        }

        private static string Placeholder()
        {
            return "\n\n### Prior evidence\n" + PipelineHandoffContext.ArtifactPlaceholder + "\n(rendered at launch)\n";
        }

        private static Dictionary<string, string> Params(string description)
        {
            return new Dictionary<string, string> { ["MissionDescription"] = description };
        }

        private static int Count(string text, string value)
        {
            int count = 0;
            for (int at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal)) count++;
            return count;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
