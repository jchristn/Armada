namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="JudgeContract"/>, the bounded three-lens Judge contract. Positive cases
    /// confirm a well-formed PASS and a substantiated block are accepted; negative cases confirm a missing
    /// lens, a too-short PASS, and a block without a concrete affected case are each rejected.
    /// </summary>
    public sealed class JudgeContractSuite : IArmadaTestSuite
    {
        #region Public-Members

        private const string ThreeLensPass =
            "## Correctness\n" +
            "The change validates the input before use and handles the null branch, so it is logically sound for the inputs it can receive.\n\n" +
            "## Blast Radius\n" +
            "Only the settings-update path is touched and its two callers were reviewed; no shared state or existing behavior regresses.\n\n" +
            "## Source Fidelity\n" +
            "The diff implements exactly the requested field and stays within the named files, inventing no new API surface.\n\n" +
            "## Verdict\n" +
            "[ARMADA:VERDICT] PASS\n";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the judge-contract suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(Case("affected_case_key_ignores_line_moves", "The affected-case key ignores line numbers, case and spacing, and is null without a concrete case", TestTags.Positive, () =>
            {
                string? first = JudgeContract.AffectedCaseKey("## Affected Case\nFile: src/A.py:17\nStill throws.");
                string? moved = JudgeContract.AffectedCaseKey("## Affected Case\nFile:   src/a.py:42-44\nStill throws after the edit.");
                AssertNotNull(first);
                AssertEqual(first, moved, "the same file at a new line is the same case");
                AssertEqual("scenario:a null start time with end set returns a negative elapsed",
                    JudgeContract.AffectedCaseKey("## Affected Case\nScenario: A null start time   with End set returns a negative Elapsed"));
                AssertNull(JudgeContract.AffectedCaseKey("No affected case section here.\n[ARMADA:VERDICT] NEEDS_REVISION"));
                AssertNull(JudgeContract.AffectedCaseKey("## Affected Case\nScenario: short"));
                AssertFalse(String.Equals(first, JudgeContract.AffectedCaseKey("## Affected Case\nFile: src/b.py:17\nDifferent file."), StringComparison.Ordinal));
            }));


            cases.Add(Case("well_formed_pass_is_accepted", "A three-lens PASS with real narrative is accepted", TestTags.Positive, () =>
            {
                bool ok = JudgeContract.ValidatePass(ThreeLensPass, StripHeadings(ThreeLensPass), out string? reason);
                AssertTrue(ok, "expected a well-formed PASS to validate; reason: " + (reason ?? "<none>"));
                AssertNull(reason);
            }));

            cases.Add(Case("all_three_lenses_detected", "All three required lenses are detected", TestTags.Positive, () =>
            {
                AssertTrue(JudgeContract.HasAllLenses(ThreeLensPass, out List<string> missing), "expected all lenses present");
                AssertEqual(0, missing.Count, "no lenses should be missing");
            }));

            cases.Add(Case("decorated_lens_headings_validate_pass_and_keep_bodies", "Decorated lens headings validate a PASS and stop section bodies at the next heading", TestTags.Positive, () =>
            {
                string output =
                    "**## Correctness**\n" +
                    "The change validates each input before use and handles the empty case without changing stored state.\n\n" +
                    "*## Blast Radius:*\n" +
                    "Only the intended request path changes and its callers retain their existing authorization and response behavior.\n\n" +
                    "## **Source Fidelity**\n" +
                    "The implementation follows the requested contract and uses the existing owner without adding unrelated behavior.\n\n" +
                    "**## Affected Case**\n" +
                    "File: src/Core/RequestHandler.cs\n" +
                    "The request with an empty value currently reaches the invalid branch.\n\n" +
                    "**## Other Findings**\n" +
                    "This text must not enter the affected-case body.\n\n" +
                    "[ARMADA:VERDICT] PASS\n";

                AssertTrue(JudgeContract.HasAllLenses(output, out List<string> missing),
                    "wrapped heading marks must still identify all three required lenses; missing: " + String.Join(", ", missing));
                AssertTrue(JudgeContract.ValidatePass(output, StripHeadings(output), out string? reason),
                    "a sufficiently detailed PASS with decorated lens headings should validate; reason: " + (reason ?? "<none>"));
                AssertTrue(JudgeContract.ExhibitsAffectedCase(output), "the decorated affected-case heading must expose its file field");

                string conflictOutput =
                    "**## Spec Conflict**\n" +
                    "The stated requirement conflicts with the verified dependency behavior.\n\n" +
                    "**## Other Findings**\n" +
                    "This text must not enter the conflict body.\n\n" +
                    "[ARMADA:VERDICT] SPEC_CONFLICT\n";
                AssertEqual("The stated requirement conflicts with the verified dependency behavior.",
                    JudgeContract.ExtractSpecConflict(conflictOutput),
                    "the decorated conflict heading must retain its section body and stop at the next decorated heading");
            }));

            cases.Add(Case("section_heading_match_requires_complete_line", "A lens name in prose or an unrelated heading does not count, and a verdict line does not end a section", TestTags.Negative, () =>
            {
                string output =
                    "The review covers Correctness and its edge cases.\n" +
                    "**## Unrelated section**\n" +
                    "This section does not name a required lens.\n\n" +
                    "## Blast Radius\nThe change is narrow.\n\n" +
                    "## Source Fidelity\nThe change follows the request.\n";

                AssertFalse(JudgeContract.ContainsSection(output, JudgeContract.LensCorrectness),
                    "a lens name inside prose must not count as a heading");
                AssertFalse(JudgeContract.HasAllLenses(output, out List<string> missing),
                    "an unrelated heading and prose must not replace the missing lens");
                AssertEqual(1, missing.Count, "only the actual missing lens should be reported");
                AssertEqual(JudgeContract.LensCorrectness, missing[0]);

                string plainConflict =
                    "## Spec Conflict\n" +
                    "The stated requirement conflicts with the verified dependency behavior.\n\n" +
                    "[ARMADA:VERDICT] SPEC_CONFLICT\n";
                AssertEqual(
                    "The stated requirement conflicts with the verified dependency behavior.\n\n[ARMADA:VERDICT] SPEC_CONFLICT",
                    JudgeContract.ExtractSpecConflict(plainConflict),
                    "a verdict line is not a heading and stays in the section body");
            }));

            cases.Add(Case("section_headings_accepted_before_stay_accepted", "Every heading form accepted before wrapped decoration support is still accepted", TestTags.Positive, () =>
            {
                string[] accepted = new[]
                {
                    "## Correctness",
                    "Correctness",
                    "Correctness:",
                    "Correctness - notes follow",
                    "Correctness looks fine",
                    "**Correctness**: looks fine",
                    "**Correctness**",
                    "__Correctness__",
                    "`Correctness`",
                    "1. Correctness",
                    "- **Correctness**",
                    "* Correctness",
                    "## **Correctness**",
                    "### 2. Correctness:",
                    "  #### correctness  "
                };
                List<string> lost = new List<string>();
                foreach (string line in accepted)
                {
                    if (!JudgeContract.ContainsSection("Intro text.\n" + line + "\nBody text.", JudgeContract.LensCorrectness)) lost.Add(line);
                }

                string[] bodies = new[] { "## Affected Case", "**Affected Case**:", "Affected Case", "- Affected Case:", "## **Affected Case**" };
                foreach (string heading in bodies)
                {
                    string output = heading + "\nFile: src/Core/RequestHandler.cs\n## Verdict\n[ARMADA:VERDICT] FAIL";
                    if (!JudgeContract.ExhibitsAffectedCase(output)) lost.Add("affected case under " + heading);
                }

                AssertEqual(0, lost.Count, "forms no longer accepted: " + String.Join(" | ", lost));
            }));

            cases.Add(Case("wrapped_section_headings_are_accepted", "Headings with emphasis around the heading marks or the whole name are accepted, and prose is not", TestTags.Positive, () =>
            {
                string[] wrapped = new[]
                {
                    "**## Correctness**",
                    "*## Correctness*",
                    "__## Correctness__",
                    "`## Correctness`",
                    "**## Correctness:**",
                    "**## Correctness**:",
                    "*Correctness*",
                    "_Correctness_",
                    "***Correctness***",
                    "**1. Correctness**",
                    "- *Correctness*",
                    "** ## Correctness **",
                    "**Correctness:** looks fine",
                    "__Correctness:__ looks fine"
                };
                foreach (string line in wrapped)
                {
                    AssertTrue(JudgeContract.ContainsSection("Intro text.\n" + line + "\nBody text.", JudgeContract.LensCorrectness), "wrapped heading accepted: " + line);
                }

                string[] rejected = new[]
                {
                    "The review covers Correctness and its edge cases.",
                    "**## Correctness review of edge cases**",
                    "**## Incorrectness**",
                    "No correctness section here."
                };
                foreach (string line in rejected)
                {
                    AssertFalse(JudgeContract.ContainsSection("Intro text.\n" + line + "\nBody text.", JudgeContract.LensCorrectness), "not a heading: " + line);
                }

                foreach (string heading in new[] { "**## Affected Case**", "*Affected Case*", "**Affected Case:**", "_## Affected Case_" })
                {
                    string output = heading + "\nScenario: an empty request reaches the invalid branch and returns 500\n**## Other Findings**\nFile: src/Unrelated.cs";
                    AssertTrue(JudgeContract.ExhibitsAffectedCase(output), "affected case read under wrapped heading: " + heading);
                    AssertEqual("scenario:an empty request reaches the invalid branch and returns 500", JudgeContract.AffectedCaseKey(output), "the body stops at the next wrapped heading: " + heading);
                }

                string noise = new string('*', 5000) + "x";
                DateTime started = DateTime.UtcNow;
                AssertFalse(JudgeContract.ContainsSection(noise + "\n" + noise, JudgeContract.LensCorrectness), "a long run of asterisks is not a heading");
                AssertTrue(DateTime.UtcNow - started < TimeSpan.FromSeconds(2), "a long run of asterisks does not backtrack");
            }));

            cases.Add(Case("output_contract_names_lenses_and_case", "The output contract names the three lenses and the affected-case rule", TestTags.Positive, () =>
            {
                string contract = StageOutputContract.For(PersonaCatalog.Judge);
                AssertContains("Correctness", contract);
                AssertContains("Blast Radius", contract);
                AssertContains("Source Fidelity", contract);
                AssertContains("Affected Case", contract);
                AssertFalse(StageOutputContract.RoleSummary(PersonaCatalog.Judge).Contains("--no-restore"), "toolchain advice lives in the .NET workflow profile");
                AssertContains("--no-restore", WorkflowProfileTemplates.DotNetValidationGuidance);
            }));

            cases.Add(Case("block_with_affected_case_file_ref", "A block that exhibits a file reference is a valid block", TestTags.Positive, () =>
            {
                string output =
                    "## Correctness\nThe update drops omitted fields.\n\n" +
                    "## Affected Case\n" +
                    "- **File:** src/Core/Settings.cs:118\n" +
                    "The partial update overwrites unspecified keys, wiping stored values.\n\n" +
                    "[ARMADA:VERDICT] NEEDS_REVISION\n";
                AssertTrue(JudgeContract.ExhibitsAffectedCase(output), "a File: field naming a path should count");
            }));

            cases.Add(Case("block_with_scenario_case", "A block that describes a triggering scenario is a valid block", TestTags.Positive, () =>
            {
                string output =
                    "## Affected Case\n" +
                    "Scenario: the caller passes an empty list and the loop throws instead of returning early.\n\n" +
                    "[ARMADA:VERDICT] FAIL\n";
                AssertTrue(JudgeContract.ExhibitsAffectedCase(output), "a Scenario: field should count");
            }));

            cases.Add(Case("missing_lens_is_rejected", "A PASS missing a lens is rejected and names it", TestTags.Negative, () =>
            {
                string output =
                    "## Correctness\nLooks correct for the reviewed inputs and edge cases considered here.\n\n" +
                    "## Blast Radius\nThe change is narrow and its callers were reviewed for regressions.\n\n" +
                    "[ARMADA:VERDICT] PASS\n";
                bool ok = JudgeContract.ValidatePass(output, StripHeadings(output), out string? reason);
                AssertFalse(ok, "a PASS missing Source Fidelity should be rejected");
                AssertContains("Source Fidelity", reason ?? String.Empty, "the reason should name the missing lens");
            }));

            cases.Add(Case("too_short_pass_is_rejected", "A PASS with a trivial narrative is rejected", TestTags.Negative, () =>
            {
                string output =
                    "## Correctness\nok\n\n## Blast Radius\nok\n\n## Source Fidelity\nok\n\n[ARMADA:VERDICT] PASS\n";
                bool ok = JudgeContract.ValidatePass(output, StripHeadings(output), out string? reason);
                AssertFalse(ok, "a shallow PASS should be rejected");
                AssertContains("too short", reason ?? String.Empty, "the reason should explain the shallow review");
            }));

            cases.Add(Case("block_without_case_is_not_valid", "A block without an affected case is not a valid block", TestTags.Negative, () =>
            {
                string output =
                    "## Correctness\nI have some concerns about this change.\n\n" +
                    "## Blast Radius\nIt might break something somewhere.\n\n" +
                    "## Source Fidelity\nThe intent seems off.\n\n" +
                    "[ARMADA:VERDICT] NEEDS_REVISION\n";
                AssertFalse(JudgeContract.ExhibitsAffectedCase(output), "a vague block with no affected-case section must not count");
            }));

            cases.Add(Case("affected_case_section_but_vague_is_not_valid", "An affected-case section with no concrete reference does not count", TestTags.Negative, () =>
            {
                string output =
                    "## Affected Case\n" +
                    "Something feels wrong about the overall approach here.\n\n" +
                    "[ARMADA:VERDICT] FAIL\n";
                AssertFalse(JudgeContract.ExhibitsAffectedCase(output), "an affected-case section must cite a real reference or scenario");
            }));

            cases.Add(Case("affected_case_prose_keywords_do_not_count", "Prose that merely contains scenario words or dotted tokens is not an affected case", TestTags.Negative, () =>
            {
                // The old check accepted any body containing "when", "if the", "returns", "input", or a dotted token
                // such as "e.g." as a concrete case.
                string output =
                    "## Affected Case\n" +
                    "When I look at this, e.g. the overall design, it returns a vague feeling that the input is off.\n\n" +
                    "[ARMADA:VERDICT] FAIL\n";
                AssertFalse(JudgeContract.ExhibitsAffectedCase(output), "keywords in prose are not a structured affected case");
            }));

            cases.Add(Case("affected_case_fields_must_have_content", "Empty or trivial File and Scenario fields do not count", TestTags.Negative, () =>
            {
                string output =
                    "## Affected Case\n" +
                    "File: none\n" +
                    "Scenario: bad\n\n" +
                    "[ARMADA:VERDICT] FAIL\n";
                AssertFalse(JudgeContract.ExhibitsAffectedCase(output), "a File: value must be a path and a Scenario: must describe the trigger");
            }));

            cases.Add(Case("affected_case_field_outside_section_does_not_count", "A File field outside the Affected Case section does not count", TestTags.Negative, () =>
            {
                string output =
                    "## Correctness\n" +
                    "File: src/Core/Settings.cs:118\n\n" +
                    "## Affected Case\n" +
                    "Something is wrong.\n\n" +
                    "[ARMADA:VERDICT] FAIL\n";
                AssertFalse(JudgeContract.ExhibitsAffectedCase(output), "the field must be inside the Affected Case section");
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.JudgeContract",
                displayName: "Judge Contract",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static string StripHeadings(string output)
        {
            // A crude narrative extraction sufficient for tests: drop heading and verdict lines.
            List<string> lines = new List<string>();
            foreach (string raw in output.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (String.IsNullOrEmpty(line)) continue;
                if (line.StartsWith("#", StringComparison.Ordinal)) continue;
                if (line.StartsWith("[ARMADA:VERDICT]", StringComparison.Ordinal)) continue;
                lines.Add(line);
            }
            return String.Join(" ", lines);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.JudgeContract",
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
