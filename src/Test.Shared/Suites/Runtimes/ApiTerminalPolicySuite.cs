namespace Test.Shared.Suites.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Runtimes;
    using PolyPrompt.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The API runtime's terminal policy completes, continues twice, forces the submit tool twice, or fails with a typed
    /// error, and judges plain replies (RS-7.11).
    /// </summary>
    public sealed class ApiTerminalPolicySuite : IArmadaTestSuite
    {
        private const string SuiteId = "Runtimes.ApiTerminalPolicy";
        private const string Done = "Implemented and tested.\n[ARMADA:RESULT] COMPLETE";

        /// <summary>Builds the terminal policy cases.</summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(Case("complete_with_completion_line", "Text with the completion line and a summary completes the run", TestTags.Positive, () =>
            {
                ApiTerminalDecision decision = new ApiTerminalPolicy(true, null, null).Decide(Reply(Done, "stop"), 0, 10);
                AssertEqual(ApiTerminalActionEnum.Complete, decision.Action);
                AssertEqual(Done, decision.FinalText);
            }));
            cases.Add(Case("think_blocks_are_stripped", "Complete think blocks are removed from the final text; an unmatched tag stays", TestTags.Positive, () =>
            {
                ApiTerminalDecision decision = new ApiTerminalPolicy(false, null, null).Decide(Reply("<think>plan</think>Answer", "stop"), 0, 10);
                AssertEqual("Answer", decision.FinalText);
                AssertEqual("a <think> b", ApiTerminalPolicy.StripThinkBlocks("a <think> b"));
                AssertEqual("ab", ApiTerminalPolicy.StripThinkBlocks("a<think type=\"x\">hidden</think >b"));
                AssertEqual(String.Empty, ApiTerminalPolicy.StripThinkBlocks(null));
            }));
            cases.Add(Case("bare_close_think_is_stripped", "With reasoning hygiene, text before a bare closing think tag and an unclosed leading block are removed from the final text; without it, 1.4.0 stripping applies (RS-7.6)", TestTags.Positive, () =>
            {
                ApiTerminalPolicy hygienic = new ApiTerminalPolicy(false, null, null);
                ApiTerminalDecision decision = hygienic.Decide(Reply("Let me check the plan.\n</think>\nAnswer", "stop"), 0, 10);
                AssertEqual("Answer", decision.FinalText);
                AssertEqual(String.Empty, hygienic.VisibleText("<think>still reasoning when the stream ended"));
                AssertEqual("Use `</think>` to close a block.", hygienic.VisibleText("Use `</think>` to close a block."), "a quoted tag is not reasoning");
                ApiTerminalPolicy legacy = new ApiTerminalPolicy(false, null, null, reasoningHygiene: false);
                AssertContains("</think>", legacy.VisibleText("Let me check.\n</think>\nAnswer"));
                AssertEqual("Answer", legacy.VisibleText("<think>plan</think>Answer"));
            }));
            cases.Add(Case("continues_twice_then_fails", "An incomplete reply is continued twice, then fails as incomplete_completion", TestTags.Negative, () =>
            {
                ApiTerminalPolicy policy = new ApiTerminalPolicy(true, null, null);
                ApiTerminalDecision first = policy.Decide(Reply("Working on it.", "stop"), 0, 10);
                AssertEqual(ApiTerminalActionEnum.Continue, first.Action);
                AssertContains("(1/2)", first.Warning!);
                AssertContains("[ARMADA:RESULT] COMPLETE line.", first.FollowUp!);
                AssertEqual(ApiTerminalActionEnum.Continue, policy.Decide(Reply("Still working.", "stop"), 1, 10).Action);
                ApiTerminalDecision last = policy.Decide(Reply("Still working.", "stop"), 2, 10);
                AssertEqual(ApiTerminalActionEnum.Fail, last.Action);
                AssertEqual("incomplete_completion", last.Failure!.ErrorType);
                AssertEqual("[error] incomplete completion; the mission is not finished.", last.ErrorDiagnostic);
                AssertEqual(3, policy.IncompleteResponses);
            }));
            cases.Add(Case("no_turns_left_fails_at_once", "An incomplete reply on the last turn fails without a continuation", TestTags.Negative, () =>
            {
                ApiTerminalDecision decision = new ApiTerminalPolicy(false, null, null).Decide(Reply(String.Empty, "stop"), 9, 10);
                AssertEqual(ApiTerminalActionEnum.Fail, decision.Action);
            }));
            cases.Add(Case("token_limit_is_remembered", "A token-limit reply is never complete, and the failure names the configured limit even after a later incomplete reply", TestTags.Negative, () =>
            {
                ApiTerminalPolicy policy = new ApiTerminalPolicy(false, 512, null);
                ChatResponse cut = Reply(Done, "length");
                cut.Usage = new TokenUsage { CompletionTokens = 512 };
                AssertEqual(ApiTerminalActionEnum.Continue, policy.Decide(cut, 0, 10).Action);
                policy.Decide(Reply(String.Empty, "stop"), 1, 10);
                ApiTerminalDecision last = policy.Decide(Reply(String.Empty, "stop"), 2, 10);
                AssertEqual("output_token_limit_reached", last.Failure!.ErrorType);
                AssertContains("configured Max Output Tokens: 512", last.Failure.Reason!);
                AssertTrue(ApiTerminalPolicy.IsTokenLimit("MAX_TOKENS"));
                AssertTrue(ApiTerminalPolicy.IsTokenLimit("max_output_tokens"));
                AssertFalse(ApiTerminalPolicy.IsTokenLimit("stop"));
            }));
            cases.Add(Case("forced_submit_twice", "A stage with a submit tool that stopped without submitting is forced twice, then falls back to the text rules", TestTags.Negative, () =>
            {
                NativeSubmitTool submit = new NativeSubmitTool(StageSubmissionKindEnum.Verdict, "Judge", 1);
                ApiTerminalPolicy policy = new ApiTerminalPolicy(false, null, submit);
                ApiTerminalDecision first = policy.Decide(Reply("Looks fine.", "stop"), 0, 10);
                AssertEqual(ApiTerminalActionEnum.ForceSubmit, first.Action);
                AssertEqual(submit.ForcePrompt(), first.FollowUp);
                AssertContains("requiring it (1/2)", first.Warning!);
                AssertEqual(ApiTerminalActionEnum.ForceSubmit, policy.Decide(Reply("Looks fine.", "stop"), 1, 10).Action);
                AssertEqual(ApiTerminalActionEnum.Complete, policy.Decide(Reply("Looks fine.", "stop"), 2, 10).Action, "without the completion line requirement the text completes");
                AssertEqual(2, policy.ForcedSubmits);
            }));
            cases.Add(Case("text_contract_skips_forced_submit", "A verdict line in text meets the contract, so no submit is forced", TestTags.Positive, () =>
            {
                NativeSubmitTool submit = new NativeSubmitTool(StageSubmissionKindEnum.Verdict, "Judge", 1);
                ApiTerminalDecision decision = new ApiTerminalPolicy(false, null, submit).Decide(Reply("All checks pass.\n[ARMADA:VERDICT] PASS", "stop"), 0, 10);
                AssertEqual(ApiTerminalActionEnum.Complete, decision.Action);
            }));
            cases.Add(Case("plain_reply", "A plain reply completes, or fails as token limit or incomplete", TestTags.Positive, () =>
            {
                ApiTerminalPolicy policy = new ApiTerminalPolicy(true, null, null);
                AssertEqual(ApiTerminalActionEnum.Complete, policy.DecidePlain(Reply(Done, "stop")).Action);
                AssertEqual("output_token_limit_reached", policy.DecidePlain(Reply(Done, "length")).Failure!.ErrorType);
                ApiTerminalDecision incomplete = policy.DecidePlain(Reply("No marker.", "stop"));
                AssertEqual("incomplete_completion", incomplete.Failure!.ErrorType);
                AssertEqual("Plain inference did not produce a valid mission completion.", incomplete.Failure.Reason);
                AssertThrows<ArgumentNullException>(() => policy.DecidePlain(null!));
            }));
            return new TestSuiteDescriptor(SuiteId, "API runtime terminal policy", cases);
        }

        private static ChatResponse Reply(string text, string finishReason)
        {
            return new ChatResponse { Success = true, Text = text, FinishReason = finishReason };
        }

        private static TestCaseDescriptor Case(string id, string name, string tag, Action body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, ct =>
            {
                body();
                return Task.CompletedTask;
            }, tags: new List<string> { tag });
        }
    }
}
