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
    /// The format-only repair turn's acceptance rules (RS-6.4) and the PASS evidence rule (RS-6.8): a restatement is
    /// accepted only when it parses and adds no verdict, evidence, finding or mission the original lacked.
    /// </summary>
    public sealed class FormatRepairSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.FormatRepair";

        private const string Lenses =
            "## Correctness\nThe validator rejects empty names before saving, and the save path is unchanged for valid names.\n"
            + "## Blast Radius\nOnly the widget create route calls the validator; no other caller changes.\n"
            + "## Source Fidelity\nThe change matches the requested 400 response and adds nothing else.\n";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("pass_evidence_file_or_check", "A PASS cites evidence through an inspected file or a check it ran; prose alone does not", TestTags.Positive, () =>
            {
                AssertTrue(JudgeEvidence.HasPassEvidence("I read src/widgets/validator.ts and it is fine."), "a file path");
                AssertTrue(JudgeEvidence.HasPassEvidence("Checked REST_API.md for the new field."), "a bare file name with a known extension");
                AssertTrue(JudgeEvidence.HasPassEvidence("I ran dotnet test and everything passed."), "a check command");
                AssertFalse(JudgeEvidence.HasPassEvidence("The change looks correct, e.g. it handles v1.5.0 inputs."), "no file and no check");
                CollectionAssertEqual(new List<string> { "src/a.cs", "README.md" }, JudgeEvidence.FileReferences("See src/a.cs:12, README.md and src/A.cs again."));
            }));

            cases.Add(Case("pass_without_evidence_is_rejected", "ValidatePass rejects a three-lens PASS that names no check and no file", TestTags.Negative, () =>
            {
                string output = Lenses + "## Verdict\n[ARMADA:VERDICT] PASS\n";
                AssertFalse(JudgeContract.ValidatePass(output, output, out string? reason));
                AssertEqual(JudgeEvidence.MissingEvidenceReason, reason);
                string cited = Lenses + "I inspected src/widgets/validator.ts.\n## Verdict\n[ARMADA:VERDICT] PASS\n";
                AssertTrue(JudgeContract.ValidatePass(cited, cited, out reason), "citing one inspected file is enough: " + reason);
            }));

            cases.Add(Case("judge_restatement_of_stated_pass_is_accepted", "A restated PASS is accepted when the original stated PASS and cited a file", TestTags.Positive, () =>
            {
                string original = Lenses + "I inspected src/widgets/validator.ts. Verdict: **PASS**";
                string restated = Lenses + "Inspected: src/widgets/validator.ts\n## Verdict\n[ARMADA:VERDICT] PASS";
                AssertTrue(FormatRepair.TryAcceptJudge(original, restated, out JudgeVerdictEnum verdict, out string? rejection), rejection ?? "");
                AssertEqual(JudgeVerdictEnum.Pass, verdict);
            }));

            cases.Add(Case("judge_restatement_never_invents", "A restatement cannot create a PASS, evidence, a finding or a SPEC_CONFLICT the original lacked", TestTags.Negative, () =>
            {
                string noEvidence = Lenses + "Verdict: PASS";
                AssertFalse(FormatRepair.TryAcceptJudge(noEvidence, Lenses + "Inspected: src/widgets/validator.ts\n[ARMADA:VERDICT] PASS", out _, out string? rejection), "no evidence to restate");
                AssertContains("evidence", rejection ?? "");

                string blocking = Lenses + "I inspected src/widgets/validator.ts. This FAILS: whitespace names still save. Verdict: FAIL";
                AssertFalse(FormatRepair.TryAcceptJudge(blocking, Lenses + "src/widgets/validator.ts\n[ARMADA:VERDICT] PASS", out _, out rejection), "a PASS when the original blocks");

                string noVerdict = Lenses + "I inspected src/widgets/validator.ts.";
                AssertFalse(FormatRepair.TryAcceptJudge(noVerdict, Lenses + "src/widgets/validator.ts\n[ARMADA:VERDICT] PASS", out _, out rejection), "a verdict the original never stated");

                string fail = Lenses + "Verdict: FAIL. Whitespace-only names still save through the create route.";
                AssertFalse(FormatRepair.TryAcceptJudge(fail, Lenses + "## Affected Case\nFile: src/widgets/other.ts:4\n[ARMADA:VERDICT] FAIL", out _, out rejection), "an affected file the original never named");
                AssertFalse(FormatRepair.TryAcceptJudge(fail, Lenses + "## Affected Case\nScenario: deleting a widget twice corrupts the database index\n[ARMADA:VERDICT] FAIL", out _, out rejection), "an invented scenario");
                AssertTrue(FormatRepair.TryAcceptJudge(fail, Lenses + "## Affected Case\nScenario: whitespace-only names still save through the create route\n[ARMADA:VERDICT] FAIL", out JudgeVerdictEnum verdict, out rejection), rejection ?? "");
                AssertEqual(JudgeVerdictEnum.Fail, verdict);

                AssertFalse(FormatRepair.TryAcceptJudge(Lenses + "SPEC_CONFLICT", Lenses + "## Spec Conflict\n> AC-1\n[ARMADA:VERDICT] SPEC_CONFLICT", out _, out rejection), "never SPEC_CONFLICT");
            }));

            cases.Add(Case("verifier_block_restatement", "A restated verifier block keeps the block marker and takes its affected case from the original", TestTags.Positive, () =>
            {
                string original = "The importer drops rows with a trailing comma in src/import/csv.py. Cannot fix product code.\n" + VerifierContract.BlockedMarker;
                string restated = "## Affected Case\nFile: src/import/csv.py\n" + VerifierContract.BlockedMarker;
                AssertTrue(FormatRepair.TryAcceptVerifierBlock(original, restated, out string? rejection), rejection ?? "");
                AssertFalse(FormatRepair.TryAcceptVerifierBlock(original, "## Affected Case\nFile: src/import/csv.py\n[ARMADA:RESULT] COMPLETE", out rejection), "dropping the block is not a restatement");
            }));

            cases.Add(Case("architect_restatement_keeps_planned_missions", "A restated plan must parse and plan only missions the original planned", TestTags.Negative, () =>
            {
                string example = StageOutputContract.ArchitectPlanExample();
                string original = "```armada-plan\n" + example.Replace("\"parent\": null", "\"parent\": \"root\"") + "\n```";
                string restated = "```armada-plan\n" + example + "\n```";
                AssertTrue(FormatRepair.TryAcceptArchitectPlan(original, restated, 4, out string? rejection), rejection ?? "");

                string extra = restated.Replace("Integrate and document name validation", "Rewrite the storage layer");
                AssertFalse(FormatRepair.TryAcceptArchitectPlan(original, extra, 4, out rejection), "an invented mission");
                AssertContains("Rewrite the storage layer", rejection ?? "");
                AssertFalse(FormatRepair.TryAcceptArchitectPlan(original, original, 4, out rejection), "a restatement that still does not parse");
            }));

            cases.Add(Case("prompt_quotes_output_errors_and_contract", "The format prompt quotes the stored output, the parser's errors in order, and the contract, and forbids new work", TestTags.Positive, () =>
            {
                string prompt = FormatRepair.BuildPrompt("Judge", "my review", "CONTRACT TEXT", new List<string> { "first error", "second error" });
                AssertContains("Do not do any new work", prompt);
                AssertContains("1. first error\n2. second error", prompt);
                AssertContains("CONTRACT TEXT", prompt);
                AssertContains("<<<REPORT\nmy review\nREPORT", prompt);
                string longOutput = new string('x', FormatRepair.MaxQuotedOutputChars + 100);
                AssertContains("[...earlier output omitted...]", FormatRepair.BuildPrompt("Judge", longOutput, "C", new List<string>()));
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Format-only repair", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static void CollectionAssertEqual(List<string> expected, List<string> actual)
        {
            AssertEqual(String.Join("|", expected), String.Join("|", actual));
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
