namespace Test.Shared.Infrastructure.ModelOutputs
{
    using System.Collections.Generic;

    /// <summary>
    /// Canned Architect, Judge, and Worker outputs covering the shapes weaker models produce (fences of another kind,
    /// bare JSON, typed values as strings, decorated or prose verdicts, two verdicts, verdicts inside a truncated
    /// fence, decorated completion markers), each with the result Armada should reach under the contract decisions
    /// recorded in RELIABILITY_SIMPLICITY_PLAN.md (R5: ambiguity goes to a format repair, never "pick the last";
    /// R6: plan validation stays strict, only locating one block gets more tolerant).
    /// </summary>
    public static class ModelOutputFixtures
    {
        private const string Fence = "```";

        /// <summary>The catalog.</summary>
        public static IReadOnlyList<ModelOutputFixture> All { get; } = new List<ModelOutputFixture>
        {
            // Architect
            Make("architect_fenced", "Architect", "a canonical fenced plan", Fence + "armada-plan\n{\"missions\":[{\"title\":\"A\",\"description\":\"do a\"},{\"title\":\"B\",\"description\":\"do b\"}]}\n" + Fence + "\n", "missions:2", null),
            Make("architect_other_fence", "Architect", "the plan in a json fence", "Here is the plan.\n" + Fence + "json\n{\"missions\":[{\"title\":\"A\",\"description\":\"do a\"}]}\n" + Fence + "\n", "missions:1", null),
            Make("architect_tilde_fence", "Architect", "the plan in a ~~~ fence", "~~~armada-plan\n{\"missions\":[{\"title\":\"A\",\"description\":\"do a\"}]}\n~~~\n", "missions:1", "RS-6.5"),
            Make("architect_bare_json", "Architect", "the plan as bare JSON with no fence", "Plan:\n{\"missions\":[{\"title\":\"A\",\"description\":\"do a\"}]}\nDone.", "missions:1", "RS-6.5"),
            Make("architect_unclosed_fence", "Architect", "a fence cut off before it closes", Fence + "armada-plan\n{\"missions\":[{\"title\":\"A\",\"description\":\"do a\"}]}\n", "missions:1", null),
            Make("architect_parent_as_string", "Architect", "a v4 parent index written as a string", Fence + "armada-plan\n{\"contractVersion\":4,\"missions\":[{\"title\":\"A\",\"description\":\"do a\"},{\"title\":\"B\",\"description\":\"do b\",\"parent\":\"1\"}]}\n" + Fence + "\n", "rejected", null),
            Make("architect_kind_case", "Architect", "a v4 kind in the wrong case", Fence + "armada-plan\n{\"contractVersion\":4,\"missions\":[{\"title\":\"A\",\"description\":\"do a\",\"kind\":\"Implementation\"}]}\n" + Fence + "\n", "rejected", null),
            Make("architect_prose_only", "Architect", "a plan described in prose with no block", "I will split this into two missions: first the parser, then the tests.", "rejected", null),

            // Judge
            Make("judge_canonical_pass", "Judge", "a canonical PASS line", "## Review\nChecked the diff and ran the tests.\n\n[ARMADA:VERDICT] PASS", "Pass", null),
            Make("judge_bold_line", "Judge", "the verdict line in bold", "## Review\nThe change is wrong.\n\n**[ARMADA:VERDICT] FAIL**", "Fail", null),
            Make("judge_colon_spaced", "Judge", "the verdict line with a colon and spaced words", "Findings below.\n\nARMADA VERDICT: NEEDS REVISION.", "NeedsRevision", null),
            Make("judge_prose_heading", "Judge", "a prose verdict under a heading, no marker (goes to format repair, not guessed)", "## Verdict\n**Verdict: NEEDS_REVISION**\nThe retry loop never stops.", "None", null),
            Make("judge_two_verdicts", "Judge", "a draft verdict and a final verdict that disagree", "[ARMADA:VERDICT] FAIL\n\nOn reflection the tests pass.\n\n[ARMADA:VERDICT] PASS", "None", null),
            Make("judge_echoed_in_fence", "Judge", "the contract example echoed inside a fence, then a real verdict", "The contract says:\n" + Fence + "\n[ARMADA:VERDICT] PASS\n" + Fence + "\nI found a defect.\n\n[ARMADA:VERDICT] FAIL", "Fail", null),
            Make("judge_after_truncated_fence", "Judge", "a verdict after output truncation cut a fence open", "Log excerpt:\n" + Fence + "\nline 1\n[...output truncated...]\n\n[ARMADA:VERDICT] PASS", "Pass", "RS-6.6"),
            Make("judge_spec_conflict_bare", "Judge", "a near-miss SPEC_CONFLICT with no quoted criterion", "This cannot be satisfied.\n\n**ARMADA VERDICT: SPEC CONFLICT**", "None", null),
            Make("judge_spec_conflict_section", "Judge", "a near-miss SPEC_CONFLICT with its quoted criterion", "## Spec Conflict\n> AC-2: keep escapes\nThis contradicts AC-1.\n\n**ARMADA VERDICT: SPEC CONFLICT**", "SpecConflict", null),
            Make("judge_missing", "Judge", "a review with no verdict at all", "## Review\nLooks reasonable overall.", "None", null),

            // Worker
            Make("worker_canonical", "Worker", "the canonical completion line", "Implemented the change.\n\n[ARMADA:RESULT] COMPLETE", "None", null),
            Make("worker_backticked", "Worker", "the completion line copied with backticks from the prompt", "Implemented the change.\n\n`[ARMADA:RESULT] COMPLETE`", "None", null),
            Make("worker_bold", "Worker", "the completion line in bold", "Implemented the change.\n\n**[ARMADA:RESULT] COMPLETE**", "None", null),
            Make("worker_spaced", "Worker", "the marker written as words", "Implemented the change.\n\nARMADA RESULT: COMPLETE", "CompletionLineMalformed", null),
            Make("worker_missing", "Worker", "no completion line", "Implemented the change and committed it.", "CompletionLineMissing", null)
        };

        private static ModelOutputFixture Make(string id, string persona, string description, string text, string expected, string? pendingItem)
        {
            return new ModelOutputFixture { Id = id, Persona = persona, Description = description, Text = text, Expected = expected, PendingItem = pendingItem };
        }
    }
}
