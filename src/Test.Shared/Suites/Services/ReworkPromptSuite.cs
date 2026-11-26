namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The rework prompt built from the reviewer's handoff artifact (docs/MODEL_HETEROGENEITY.md, Phase 3 item 2): numbered
    /// findings with file, line, severity and required fix; the fallback when the review has no structured findings or
    /// there is no artifact; the renderer budget and did-not-fit list; every demand of the former paragraph, one per line;
    /// and no output contract of its own: the instructions file states it once, at the end (RS-6.11).
    /// </summary>
    public sealed class ReworkPromptSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.ReworkPrompt";

        private const string TaskSpec = "Fix the parser.\n\n## Acceptance criteria\n- AC-1: reject empty input";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("several_findings_numbered", "An artifact with several findings gives numbered findings with severity, file, line and required fix, once, before the demands", TestTags.Positive, () =>
            {
                Mission judge = Judge(JudgeOutput(3));
                HandoffArtifact artifact = HandoffArtifactBuilder.Build(judge);
                AssertEqual(3, artifact.Findings.Count, "precondition: three parsed findings");
                HandoffRendering rendering = HandoffRenderer.RenderWithBudget(artifact, null, 4000);
                string prompt = ReworkPromptBuilder.Compose(ReworkDescription(judge), rendering);

                AssertContains("1. [Blocking] src/File1.cs:11 -- Required fix: guard case 1.", prompt, "finding 1 with file, line, severity and fix");
                AssertContains("2. [Blocking] src/File2.cs:12 -- Required fix: guard case 2.", prompt, "finding 2");
                AssertContains("3. [Blocking] src/File3.cs:13 -- Required fix: guard case 3.", prompt, "finding 3");
                AssertEqual(1, Count(prompt, "src/File1.cs:11 -- Required fix"), "the finding is delivered once, not again in the evidence");
                AssertFalse(prompt.Contains("#### Unresolved findings", StringComparison.Ordinal), "the evidence block drops its own findings section");
                AssertFalse(prompt.Contains(ReworkPromptBuilder.FindingsPlaceholder, StringComparison.Ordinal), "findings placeholder filled");
                AssertFalse(prompt.Contains(PipelineHandoffContext.ArtifactPlaceholder, StringComparison.Ordinal), "artifact placeholder filled");
                AssertTrue(prompt.IndexOf("### Findings to fix", StringComparison.Ordinal) < prompt.IndexOf("1. [Blocking] src/File1.cs:11", StringComparison.Ordinal), "findings under their heading");
                AssertTrue(prompt.IndexOf("3. [Blocking] src/File3.cs:13", StringComparison.Ordinal) < prompt.IndexOf("### Rework demands", StringComparison.Ordinal), "findings before the demands");
                AssertContains("get_handoff_artifact with artifactId " + artifact.Id, prompt, "pointer to the complete artifact kept");
                AssertContains("#### Did not fit\nNothing was left out.", prompt, "did-not-fit list kept");
                AssertContains("diff --git a/src/File1.cs", prompt, "diff section delivered");
                AssertTrue(prompt.StartsWith(TaskSpec + "\n\n## Rework round 1 requested by the Judge\n", StringComparison.Ordinal), "task first, then the rework heading");
                AssertNoContract(prompt);
                return Task.CompletedTask;
            }));

            cases.Add(Case("no_findings_falls_back_to_rendered_block", "An artifact without structured findings keeps the whole rendered handoff block and says to treat each reported problem as a finding", TestTags.Negative, () =>
            {
                Mission judge = Judge("## Correctness\nThe empty-input path is wrong in the parser.\n[ARMADA:VERDICT] NEEDS_REVISION\n");
                HandoffArtifact artifact = HandoffArtifactBuilder.Build(judge);
                AssertEqual(0, artifact.Findings.Count, "precondition: no structured findings");
                HandoffRendering rendering = HandoffRenderer.RenderWithBudget(artifact, null, 4000);
                string prompt = ReworkPromptBuilder.Compose(ReworkDescription(judge), rendering);

                AssertContains("The review has no structured findings. Read it in the prior stage evidence below. Treat each problem it reports as one finding.", prompt);
                AssertContains(rendering.Evidence.TrimEnd(), prompt, "the rendered handoff block is delivered whole");
                AssertContains("The empty-input path is wrong in the parser.", prompt, "the review text is not lost");
                AssertFalse(prompt.Contains(ReworkPromptBuilder.FindingsPlaceholder, StringComparison.Ordinal), "placeholder removed");
                AssertNoContract(prompt);
                return Task.CompletedTask;
            }));

            cases.Add(Case("missing_artifact_keeps_current_behavior", "Without an artifact the description keeps the fallback findings line and all demands, drops the placeholder and does not throw", TestTags.Negative, () =>
            {
                string persisted = TaskSpec + ReworkPromptBuilder.BuildSection(2, null);
                string prompt = ReworkPromptBuilder.Compose(persisted, null);
                AssertContains("## Rework round 2 requested by the Judge", prompt, "a missing reviewer persona reads as the Judge");
                AssertContains("The reviewer's findings are in the prior stage evidence below.", prompt, "fallback findings line kept");
                AssertFalse(prompt.Contains(ReworkPromptBuilder.FindingsPlaceholder, StringComparison.Ordinal), "placeholder removed");
                foreach (string demand in ReworkPromptBuilder.Demands) AssertContains(demand, prompt, "demand kept");
                AssertEqual(0, Count(prompt, ReworkPromptBuilder.OutputContract), "the description carries no contract; the instructions file states it once");
                AssertNoContract(prompt);

                // A handoff that was appended but cannot be rendered stays as it was persisted.
                Mission judge = Judge(JudgeOutput(1));
                string withHandoff = ReworkPromptBuilder.Compose(ReworkDescription(judge), null);
                AssertContains(PipelineHandoffContext.ArtifactPlaceholder, withHandoff, "the unrendered handoff placeholder is left for the existing path");
                AssertNoContract(withHandoff);

                AssertEqual("", ReworkPromptBuilder.Compose(null, null), "null description");
                AssertEqual(TaskSpec, ReworkPromptBuilder.Compose(TaskSpec, null), "a non-rework description is unchanged");
                return Task.CompletedTask;
            }));

            cases.Add(Case("budget_overflow_lists_findings_that_did_not_fit", "A budget too small for every finding delivers those that fit, lists the rest under Did not fit and keeps the rendering within budget", TestTags.Positive, () =>
            {
                Mission judge = Judge(JudgeOutput(12));
                HandoffArtifact artifact = HandoffArtifactBuilder.Build(judge);
                AssertEqual(12, artifact.Findings.Count, "precondition: twelve findings");
                int budget = 700;
                HandoffRendering rendering = HandoffRenderer.RenderWithBudget(artifact, null, budget);
                AssertTrue(rendering.EstimatedTokens <= budget, "rendering within budget: " + rendering.EstimatedTokens);
                AssertTrue(rendering.Findings.Count > 0 && rendering.Findings.Count < 12, "some findings fit: " + rendering.Findings.Count);
                string prompt = ReworkPromptBuilder.Compose(ReworkDescription(judge), rendering);

                AssertContains("1. [Blocking] src/File1.cs:11", prompt, "first finding delivered");
                AssertFalse(prompt.Contains("12. [Blocking] src/File12.cs:22", StringComparison.Ordinal), "last finding not inlined past the budget");
                AssertContains("More findings did not fit the handoff budget.", prompt, "note on the findings left out");
                AssertContains("#### Did not fit\n", prompt, "did-not-fit list present");
                AssertContains("- finding 12 (src/File12.cs)\n", prompt, "the left-out finding is listed");

                // The findings move out of the evidence; the delivered content is the same as the rendering's.
                AssertEqual(rendering.Evidence.Length, rendering.EvidenceWithoutFindings.Length + rendering.FindingsText.Length + "#### Unresolved findings\n".Length, "same content, findings moved");

                HandoffRendering none = HandoffRenderer.RenderWithBudget(artifact, null, 0);
                AssertEqual(0, none.Findings.Count, "precondition: nothing fits a zero budget");
                string empty = ReworkPromptBuilder.Compose(ReworkDescription(judge), none);
                AssertContains("No finding fit the handoff budget.", empty, "says no finding fit");
                AssertContains("- finding 1 (src/File1.cs)\n", empty, "every finding listed under Did not fit");
                AssertNoContract(empty);
                return Task.CompletedTask;
            }));

            cases.Add(Case("every_former_demand_kept_one_per_line", "Every demand of the former rework paragraph is still present, numbered, one per line, without compound sentences", TestTags.Positive, () =>
            {
                string section = ReworkPromptBuilder.BuildSection(1, "Judge");
                string[] former =
                {
                    "Resolve every reported finding.",
                    "Check adjacent paths of the same feature against the original mission acceptance requirements.",
                    "Reconcile findings from all preceding verifiers, including those labelled nonblocking.",
                    "reproducible violation of an explicit acceptance criterion or invariant as blocking",
                    "regression test, or explain with evidence why it is outside this slice contract.",
                    "required corrections, not as a replacement for the original scope.",
                    "Do not treat the findings as proof that unreported paths work.",
                    "Keep everything that is already right.",
                    "Add regression coverage that exercises the affected behavior.",
                    "Run the relevant tests.",
                    "Commit your changes.",
                    "Report every original requirement that remains unverified, including publication and delivery.",
                    "Do not start over.",
                    "Do not expand the mission scope.",
                    "End with the completion line in the Output Contract section at the end of these instructions."
                };
                foreach (string demand in former) AssertContains(demand, section, "former demand present");

                List<string> lines = section.Split('\n').SkipWhile(l => l != "### Rework demands").Skip(1).TakeWhile(l => l.Length > 0).ToList();
                AssertEqual(ReworkPromptBuilder.Demands.Count, lines.Count, "one line per demand");
                for (int i = 0; i < lines.Count; i++)
                {
                    AssertTrue(lines[i].StartsWith((i + 1) + ". ", StringComparison.Ordinal), "numbered: " + lines[i]);
                    string body = lines[i].Substring(lines[i].IndexOf(". ", StringComparison.Ordinal) + 2);
                    AssertEqual(1, body.Count(c => c == '.'), "one sentence per demand: " + body);
                    AssertFalse(body.Contains(", and ", StringComparison.Ordinal) || body.Contains(';', StringComparison.Ordinal), "no compound demand: " + body);
                }

                AssertFalse(section.Contains(ReworkPromptBuilder.OutputContract, StringComparison.Ordinal), "the section states no contract of its own");
                AssertContains("[ARMADA:RESULT] COMPLETE", ReworkPromptBuilder.OutputContract, "the Worker completion line");
                AssertContains("## Rework round 1 requested by the Verifier", ReworkPromptBuilder.BuildSection(1, "Verifier"), "the requesting stage is named");
                return Task.CompletedTask;
            }));

            cases.Add(Case("legacy_section_contract_removed", "RS-6.11: a rework section persisted before 1.5.0 loses its own contract block at launch, so the file states the contract once", TestTags.Reliability, () =>
            {
                Mission judge = Judge(JudgeOutput(1));
                string legacy = TaskSpec + ReworkPromptBuilder.BuildSection(1, judge.Persona)
                    + "\n### Output contract\n" + ReworkPromptBuilder.OutputContract + "\n" + PipelineHandoffContext.Build(judge);
                string prompt = ReworkPromptBuilder.Compose(legacy, HandoffRenderer.RenderWithBudget(HandoffArtifactBuilder.Build(judge), null, 4000));
                AssertNoContract(prompt);
                AssertContains("### Rework demands", prompt, "the demands stay");
                AssertContains("1. [Blocking] src/File1.cs:11", prompt, "the findings stay");
                return Task.CompletedTask;
            }));

            cases.Add(Case("later_round_replaces_earlier_section", "A later round starts from the task: the earlier round's rework section and handoff are stripped, so one rework section remains", TestTags.Positive, () =>
            {
                Mission judge = Judge(JudgeOutput(1));
                string round1 = ReworkDescription(judge);
                AssertEqual(TaskSpec, ReworkPromptBuilder.StripSection(round1), "rework section and later handoff removed");
                AssertEqual(TaskSpec, ReworkPromptBuilder.StripSection(TaskSpec + "\n\n## Rework round 1 requested by the Judge\nA reviewer checked this branch. Resolve every reported finding."), "a former-paragraph section is removed too");
                AssertEqual(TaskSpec, ReworkPromptBuilder.StripSection(TaskSpec), "no section: unchanged");
                AssertEqual("", ReworkPromptBuilder.StripSection(null), "null");
                string round2 = ReworkPromptBuilder.StripSection(round1) + ReworkPromptBuilder.BuildSection(2, "Judge");
                AssertEqual(1, Count(round2, ReworkPromptBuilder.FindingsPlaceholder), "one findings placeholder");
                AssertEqual(1, Count(round2, ReworkPromptBuilder.SectionPrefix), "one rework section");
                return Task.CompletedTask;
            }));

            cases.Add(Case("renderer_evidence_unchanged", "Exposing the delivered findings leaves the evidence block byte for byte as before: findings section, then the rest", TestTags.Reliability, () =>
            {
                HandoffArtifact artifact = HandoffArtifactBuilder.Build(Judge(JudgeOutput(2)));
                HandoffRendering rendering = HandoffRenderer.RenderWithBudget(artifact, null, 4000);
                AssertEqual(2, rendering.Findings.Count, "both findings delivered");
                AssertEqual(1, Count(rendering.Evidence, "#### Unresolved findings\n"), "findings section present");
                AssertEqual(1, Count(rendering.Evidence, rendering.FindingsText), "the findings text is the evidence's own, contiguous");
                string removed = rendering.Evidence.Replace("#### Unresolved findings\n", "", StringComparison.Ordinal).Replace(rendering.FindingsText, "", StringComparison.Ordinal);
                AssertEqual(rendering.EvidenceWithoutFindings, removed, "evidence without findings = evidence minus the findings section");

                HandoffRendering noFindings = HandoffRenderer.RenderWithBudget(HandoffArtifactBuilder.Build(Judge("[ARMADA:VERDICT] NEEDS_REVISION\n")), null, 4000);
                AssertEqual(noFindings.Evidence, noFindings.EvidenceWithoutFindings, "no findings: identical");
                AssertEqual("", noFindings.FindingsText, "no findings text");
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Rework prompt built from the handoff artifact",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static string JudgeOutput(int findings)
        {
            StringBuilder text = new StringBuilder("## Correctness\nMostly right.\n\n## Affected Case\n");
            for (int i = 1; i <= findings; i++)
            {
                text.Append("File: src/File").Append(i).Append(".cs:").Append(10 + i).Append('\n');
                text.Append("Scenario: input ").Append(i).Append(" is accepted although the contract rejects it, and the caller sees success.\n");
                text.Append("Required fix: guard case ").Append(i).Append(".\n");
            }

            return text.Append("\n## Verdict\nNeeds revision.\n[ARMADA:VERDICT] NEEDS_REVISION\n").ToString();
        }

        private static Mission Judge(string output)
        {
            Mission judge = new Mission("[Judge] Review parser", "Review the parser change.");
            judge.Persona = "Judge";
            judge.AgentOutput = output;
            judge.BranchName = "armada/parser";
            judge.CommitHash = "abc1234";
            judge.DiffSnapshot = "diff --git a/src/File1.cs b/src/File1.cs\n--- a/src/File1.cs\n+++ b/src/File1.cs\n@@ -1 +1,2 @@\n+guard\n";
            return judge;
        }

        private static string ReworkDescription(Mission judge)
        {
            // As persisted: the task, the rework section at scheduling, then the prior-stage section at handoff.
            return TaskSpec + ReworkPromptBuilder.BuildSection(1, judge.Persona) + PipelineHandoffContext.Build(judge);
        }

        private static void AssertNoContract(string prompt)
        {
            AssertFalse(prompt.Contains(ReworkPromptBuilder.OutputContract, StringComparison.Ordinal), "the description states no contract; the instructions file closes with it");
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
