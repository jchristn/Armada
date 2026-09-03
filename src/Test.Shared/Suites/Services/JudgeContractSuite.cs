namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
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

            cases.Add(Case("output_contract_names_lenses_and_case", "The output contract names the three lenses and the affected-case rule", TestTags.Positive, () =>
            {
                string contract = JudgeContract.OutputContract();
                AssertContains("Correctness", contract);
                AssertContains("Blast Radius", contract);
                AssertContains("Source Fidelity", contract);
                AssertContains("Affected Case", contract);
            }));

            cases.Add(Case("block_with_affected_case_file_ref", "A block that exhibits a file reference is a valid block", TestTags.Positive, () =>
            {
                string output =
                    "## Correctness\nThe update drops omitted fields.\n\n" +
                    "## Affected Case\n" +
                    "In src/Core/Settings.cs the partial update overwrites unspecified keys, wiping stored values.\n\n" +
                    "[ARMADA:VERDICT] NEEDS_REVISION\n";
                AssertTrue(JudgeContract.ExhibitsAffectedCase(output), "a file-referenced affected case should count");
            }));

            cases.Add(Case("block_with_scenario_case", "A block that describes a triggering scenario is a valid block", TestTags.Positive, () =>
            {
                string output =
                    "## Affected Case\n" +
                    "When the caller passes an empty list, the loop throws instead of returning early.\n\n" +
                    "[ARMADA:VERDICT] FAIL\n";
                AssertTrue(JudgeContract.ExhibitsAffectedCase(output), "a scenario-described affected case should count");
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
