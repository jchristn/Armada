namespace Armada.Runtimes
{
    using System;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using PolyPrompt.Models;

    /// <summary>
    /// Decides how an API captain run ends when a response requests no tool calls: complete, ask the captain to continue
    /// (at most <see cref="MaxContinuations"/> times), require the native submit tool (at most
    /// <see cref="MaxForcedSubmits"/> times, RS-6.3), or fail with a typed error. Also judges the single reply of a run
    /// without tools. Holds the continuation and forced-submit counts and the last output-token-limit error between turns.
    /// Not thread-safe: one instance serves one run.
    /// </summary>
    public sealed class ApiTerminalPolicy
    {
        #region Public-Members

        /// <summary>
        /// Continuation prompts allowed after incomplete terminal responses.
        /// </summary>
        public const int MaxContinuations = 2;

        /// <summary>
        /// Forced submit turns allowed when the captain stops without submitting.
        /// </summary>
        public const int MaxForcedSubmits = 2;

        /// <summary>
        /// Incomplete terminal responses seen so far.
        /// </summary>
        public int IncompleteResponses
        {
            get { return _IncompleteResponses; }
        }

        /// <summary>
        /// Forced submit turns requested so far.
        /// </summary>
        public int ForcedSubmits
        {
            get { return _ForcedSubmits; }
        }

        #endregion

        #region Private-Members

        // A complete think block is <think> or <think ...> through </think>. The name must end at the tag
        // boundary, so a longer name and an unmatched tag stay in the text.
        private static readonly Regex _CompleteThinkBlock = new Regex(
            @"<think(?:\s[^>]*)?>.*?</think\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private readonly bool _RequiresCompletion;
        private readonly int? _ConfiguredMaxTokens;
        private readonly NativeSubmitTool? _SubmitTool;
        private readonly bool _ReasoningHygiene;
        private int _IncompleteResponses = 0;
        private int _ForcedSubmits = 0;
        private RuntimeProviderError? _TokenLimitFailure = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create the policy for one run.
        /// </summary>
        /// <param name="requiresCompletion">Whether a finished reply must carry the standalone completion line.</param>
        /// <param name="configuredMaxTokens">The endpoint's Max Output Tokens, named in the token-limit error, or null.</param>
        /// <param name="submitTool">The stage's native submit tool, or null when the stage has none.</param>
        /// <param name="reasoningHygiene">Strip all leaked reasoning with <see cref="ReasoningText.Strip"/> (RS-7.6); false removes only complete think blocks, as in 1.4.0.</param>
        public ApiTerminalPolicy(bool requiresCompletion, int? configuredMaxTokens, NativeSubmitTool? submitTool, bool reasoningHygiene = true)
        {
            _ReasoningHygiene = reasoningHygiene;
            _RequiresCompletion = requiresCompletion;
            _ConfiguredMaxTokens = configuredMaxTokens;
            _SubmitTool = submitTool;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Decide what follows a successful response that requested no tool calls. Only this terminal response can
        /// finish the run; earlier narration is not a result.
        /// </summary>
        /// <param name="response">The response.</param>
        /// <param name="iteration">Zero-based index of the turn that produced it.</param>
        /// <param name="maxIterations">Model-turn limit for the run.</param>
        /// <returns>The decision.</returns>
        /// <exception cref="ArgumentNullException">Thrown when response is null.</exception>
        public ApiTerminalDecision Decide(ChatResponse response, int iteration, int maxIterations)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            if (IsTokenLimit(response.FinishReason)) _TokenLimitFailure = new RuntimeProviderError
            {
                ErrorType = "output_token_limit_reached",
                Reason = "The inference response hit the output-token limit (finish reason: " + response.FinishReason
                    + "; configured Max Output Tokens: " + (_ConfiguredMaxTokens?.ToString() ?? "provider default")
                    + "; completion tokens: " + response.Usage?.CompletionTokens + "). The mission did not finish."
            };

            ApiTerminalDecision decision = new ApiTerminalDecision();
            decision.FinalText = VisibleText(response.Text);
            bool turnsLeft = iteration + 1 < maxIterations;

            // RS-6.3: a stage with a submit tool that stopped without submitting, and whose reply does not meet the
            // text contract either, gets a forced submit turn instead of a failed stage.
            if (_SubmitTool != null && !_SubmitTool.Submitted && _ForcedSubmits < MaxForcedSubmits && turnsLeft
                && !_SubmitTool.TextMeetsContract(decision.FinalText) && !IsTokenLimit(response.FinishReason))
            {
                _ForcedSubmits++;
                decision.Action = ApiTerminalActionEnum.ForceSubmit;
                decision.Warning = "[warning] the captain stopped without calling " + _SubmitTool.Name + "; requiring it (" + _ForcedSubmits + "/" + MaxForcedSubmits + ").";
                decision.FollowUp = _SubmitTool.ForcePrompt();
                return decision;
            }

            bool complete = (_SubmitTool != null && _SubmitTool.Submitted && !String.IsNullOrWhiteSpace(decision.FinalText))
                || IsCompleteResponse(decision.FinalText, response.FinishReason, _RequiresCompletion);
            if (complete)
            {
                decision.Action = ApiTerminalActionEnum.Complete;
                return decision;
            }

            _IncompleteResponses++;
            if (_IncompleteResponses <= MaxContinuations && turnsLeft)
            {
                decision.Action = ApiTerminalActionEnum.Continue;
                decision.Warning = "[warning] incomplete terminal response; asking the captain to continue (" + _IncompleteResponses + "/" + MaxContinuations + ").";
                decision.FollowUp = "Your last response did not complete the task. Continue implementing and validating the mission using tools. Do not repeat an introductory plan. When finished, provide a substantive summary"
                    + (_RequiresCompletion ? " and the required standalone [ARMADA:RESULT] COMPLETE line." : ".");
                return decision;
            }

            decision.Action = ApiTerminalActionEnum.Fail;
            decision.Failure = _TokenLimitFailure ?? new RuntimeProviderError
            {
                ErrorType = "incomplete_completion",
                Reason = "The endpoint stopped without a nonempty final response and the required mission completion marker after bounded continuation attempts."
            };
            decision.ErrorDiagnostic = "[error] incomplete completion; the mission is not finished.";
            return decision;
        }

        /// <summary>
        /// Judge the single successful reply of a run without tools: it is complete, or the run fails.
        /// </summary>
        /// <param name="reply">The reply.</param>
        /// <returns>A Complete or Fail decision.</returns>
        /// <exception cref="ArgumentNullException">Thrown when reply is null.</exception>
        public ApiTerminalDecision DecidePlain(ChatResponse reply)
        {
            if (reply == null) throw new ArgumentNullException(nameof(reply));
            ApiTerminalDecision decision = new ApiTerminalDecision();
            decision.FinalText = VisibleText(reply.Text);
            if (IsCompleteResponse(decision.FinalText, reply.FinishReason, _RequiresCompletion)) return decision;

            bool tokenLimit = IsTokenLimit(reply.FinishReason);
            decision.Action = ApiTerminalActionEnum.Fail;
            decision.Failure = new RuntimeProviderError
            {
                ErrorType = tokenLimit ? "output_token_limit_reached" : "incomplete_completion",
                Reason = tokenLimit
                    ? "The inference response hit its output-token limit (finish reason: " + reply.FinishReason + "). The mission did not finish."
                    : "Plain inference did not produce a valid mission completion."
            };
            return decision;
        }

        /// <summary>
        /// The visible part of assistant text: with reasoning hygiene, <see cref="ReasoningText.Strip"/> (complete think
        /// blocks, the text before a bare closing tag, an unclosed leading block); without it,
        /// <see cref="StripThinkBlocks"/>.
        /// </summary>
        /// <param name="text">Assistant text from the inference client.</param>
        /// <returns>The visible text, or an empty string.</returns>
        public string VisibleText(string? text)
        {
            return _ReasoningHygiene ? ReasoningText.Strip(text) : StripThinkBlocks(text);
        }

        /// <summary>
        /// Remove every complete think block from assistant text before that text is emitted or stored.
        /// A block opens with a think tag whose name ends at the tag boundary and closes with the matching end tag.
        /// A longer name, an unmatched tag, and text with no complete block stay as they were.
        /// </summary>
        /// <param name="text">Assistant text from the inference client.</param>
        /// <returns>The text with complete think blocks removed, or an empty string.</returns>
        public static string StripThinkBlocks(string? text)
        {
            if (String.IsNullOrEmpty(text)) return String.Empty;
            return _CompleteThinkBlock.Replace(text, String.Empty);
        }

        /// <summary>
        /// Whether a finish reason means the response hit its output-token limit.
        /// </summary>
        /// <param name="reason">Provider finish reason, or null.</param>
        /// <returns>True for length, max_tokens and max_output_tokens.</returns>
        public static bool IsTokenLimit(string? reason)
        {
            return String.Equals(reason, "length", StringComparison.OrdinalIgnoreCase)
                || String.Equals(reason, "max_tokens", StringComparison.OrdinalIgnoreCase)
                || String.Equals(reason, "max_output_tokens", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether visible text finishes a mission: nonempty, not cut at the token limit and, when required, carrying the
        /// completion line (or the verifier's blocked outcome) next to substantive text.
        /// </summary>
        /// <param name="text">Visible response text.</param>
        /// <param name="finishReason">Provider finish reason, or null.</param>
        /// <param name="requiresCompletion">Whether the completion line is required.</param>
        /// <returns>True when the text finishes the mission.</returns>
        public static bool IsCompleteResponse(string? text, string? finishReason, bool requiresCompletion)
        {
            if (String.IsNullOrWhiteSpace(text) || IsTokenLimit(finishReason)) return false;
            if (!requiresCompletion) return true;
            string[] lines = text.Replace("\r", "").Split('\n');
            // The same decoration-tolerant matcher Core uses. A verifier that found a blocking product defect ends with the
            // blocked outcome instead; that is also a finished reply.
            bool IsEnding(string line) => CompletionLine.IsCompletionLine(line)
                || String.Equals(ProtocolLines.StripDecoration(line), VerifierContract.BlockedMarker, StringComparison.Ordinal);
            return lines.Any(IsEnding) && lines.Any(line => !String.IsNullOrWhiteSpace(line) && !IsEnding(line));
        }

        #endregion
    }
}
