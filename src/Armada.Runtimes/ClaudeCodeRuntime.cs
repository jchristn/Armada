namespace Armada.Runtimes
{
    using Armada.Core.Models;
    using Armada.Core.Services;
    using System.Diagnostics;
    using System.Globalization;
    using SyslogLogging;

    /// <summary>
    /// Agent runtime adapter for Anthropic Claude Code CLI.
    /// </summary>
    public class ClaudeCodeRuntime : BaseAgentRuntime
    {
        #region Public-Members

        /// <summary>
        /// Runtime display name.
        /// </summary>
        public override string Name => "Claude Code";

        /// <summary>
        /// Claude Code supports session resume.
        /// </summary>
        public override bool SupportsResume => true;

        /// <summary>
        /// Path to the claude CLI executable.
        /// </summary>
        public string ExecutablePath
        {
            get => _ExecutablePath;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(ExecutablePath));
                _ExecutablePath = value;
            }
        }

        /// <summary>
        /// Whether to use --dangerously-skip-permissions flag.
        /// </summary>
        public bool SkipPermissions { get; set; } = true;

        /// <summary>
        /// When true, run in streaming-JSON mode (--output-format stream-json --include-partial-messages) so
        /// the caller can render the model's reply token-by-token and read a clean final message plus metrics
        /// from the terminal "result" event. Used by interactive chat, which reads the raw events. Missions leave this
        /// false and set <see cref="BaseAgentRuntime.StructuredProgress"/> instead: stream-json without partial messages,
        /// decoded into the same readable output as text mode plus live activity.
        /// </summary>
        public bool StreamJsonOutput { get; set; } = false;

        /// <summary>
        /// MCP tool that answers Claude Code's permission prompts (--permission-prompt-tool), for example
        /// mcp__armada__cli_permission_prompt, or null. Used only when the launch does not bypass permissions: tools that
        /// would need approval are then routed to that tool instead of being refused. The tool's MCP server must be in the
        /// launch's MCP configuration (the scoped "armada" server of a token launch).
        /// </summary>
        public string? PermissionPromptTool { get; set; } = null;

        /// <summary>
        /// Seconds the permission prompt tool may wait for a decision, or null. When set with
        /// <see cref="PermissionPromptTool"/>, MCP_TOOL_TIMEOUT is raised above it (plus two minutes) so Claude Code does
        /// not abandon the waiting call first.
        /// </summary>
        public int? PermissionPromptTimeoutSeconds { get; set; } = null;

        #endregion

        #region Private-Members

        private string _ExecutablePath = "claude";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        public ClaudeCodeRuntime(LoggingModule logging) : base(logging)
        {
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Get the claude CLI command.
        /// </summary>
        /// <summary>
        /// The runtime this adapter drives.
        /// </summary>
        protected override Armada.Core.Enums.AgentRuntimeEnum RuntimeType => Armada.Core.Enums.AgentRuntimeEnum.ClaudeCode;

        /// <summary>
        /// Get the command to execute for this runtime.
        /// </summary>
        protected override string GetCommand()
        {
            return ResolveExecutable(_ExecutablePath);
        }

        /// <summary>
        /// Recognize Claude Code's own structured error output: in stream-json mode the terminal "result" event with
        /// is_error set (stdout); in text mode the whole-line "API Error: STATUS {json}" and
        /// "Claude AI usage limit reached|EPOCH" lines the CLI prints. Text-mode output never contains tool output, so
        /// these lines can only come from the CLI.
        /// </summary>
        /// <param name="line">One output line.</param>
        /// <param name="fromStdout">True for a stdout line.</param>
        /// <returns>The provider error, or null.</returns>
        protected override RuntimeProviderError? TryParseProviderError(string line, bool fromStdout)
        {
            if (StreamJsonOutput)
            {
                return fromStdout ? RuntimeProviderErrorParser.TryParseClaudeStreamJsonLine(line) : null;
            }

            // A mission streaming its progress: stdout carries stream-json events (and the odd plain warning line).
            if (StructuredProgressActive && fromStdout)
            {
                return RuntimeProviderErrorParser.TryParseClaudeStreamJsonLine(line) ?? RuntimeProviderErrorParser.TryParseClaudeTextLine(line);
            }

            return RuntimeProviderErrorParser.TryParseClaudeTextLine(line);
        }

        /// <summary>
        /// Build Claude Code CLI arguments.
        /// </summary>
        protected override List<string> BuildArguments(
            string workingDirectory,
            string prompt,
            string? model,
            string? finalMessageFilePath,
            Captain? captain)
        {
            List<string> args = new List<string>();

            args.Add("--print");
            args.Add("--verbose");

            if (StreamJsonOutput)
            {
                // Emit newline-delimited JSON events with incremental text deltas so chat can stream the
                // reply token-by-token and read a clean final message + metrics from the "result" event.
                args.Add("--output-format");
                args.Add("stream-json");
                args.Add("--include-partial-messages");
            }
            else if (StructuredProgressActive)
            {
                // A mission streaming its progress: one complete event per assistant message (text, thinking, tool
                // calls) and the terminal result event with the final reply, without per-token deltas.
                args.Add("--output-format");
                args.Add("stream-json");
            }

            if (!String.IsNullOrEmpty(model))
            {
                args.Add("--model");
                args.Add(model);
            }

            if (SkipPermissions && CaptainRuntimeOptions.GetAutoApprove(captain))
            {
                args.Add("--dangerously-skip-permissions");
            }
            else
            {
                // Without the bypass, file edits are accepted and every other tool (including shell commands) needs an
                // allow rule in the project's Claude Code settings; anything else is refused in print mode.
                args.Add("--permission-mode");
                args.Add("acceptEdits");

                // Armada's own MCP tools stay usable: each call is authorized by Armada for the captain's caller (and
                // Ask thread calls are turned into proposals), so pre-approving the armada server does not widen what
                // the captain can do. Without this, print mode refuses every Armada tool call.
                args.Add("--allowedTools");
                args.Add("mcp__armada");

                // ApproveInArmada: a tool that would need approval is sent to Armada's permission prompt tool, which
                // holds it until an approver (or a CLI permission rule) decides, instead of being refused.
                if (!String.IsNullOrEmpty(PermissionPromptTool))
                {
                    args.Add("--permission-prompt-tool");
                    args.Add(PermissionPromptTool!);
                }
            }

            // The prompt is delivered on stdin (see UsePromptStdin), not as a CLI argument. On Windows the
            // claude executable is an npm ".cmd" wrapper; a multi-line argument passed through cmd.exe is
            // truncated at the first newline, so the agent would receive only the first line of the prompt.
            // Reading the prompt from stdin preserves the full multi-line content on every platform.
            return args;
        }

        /// <summary>
        /// Chat's streaming-JSON mode reads the raw events itself.
        /// </summary>
        protected override bool InteractiveStructuredOutput => StreamJsonOutput;

        /// <summary>
        /// Deliver the prompt on stdin rather than as a command-line argument. Claude Code reads the prompt
        /// from stdin in --print mode, and this avoids the Windows cmd.exe multi-line-argument truncation.
        /// </summary>
        protected override bool UsePromptStdin => true;

        /// <summary>
        /// Apply Claude Code specific environment variables.
        /// </summary>
        protected override void ApplyEnvironment(ProcessStartInfo startInfo, Captain? captain)
        {
            startInfo.Environment["CLAUDE_CODE_DISABLE_NONINTERACTIVE_HINT"] = "1";

            // Remove nesting detection variables so captains can launch
            // even when the Admiral or CLI was started from within a Claude Code session
            startInfo.Environment.Remove("CLAUDECODE");
            startInfo.Environment.Remove("CLAUDE_CODE_ENTRYPOINT");

            // A permission prompt can wait for an approver; keep Claude Code from timing out the MCP call first.
            if (!String.IsNullOrEmpty(PermissionPromptTool) && PermissionPromptTimeoutSeconds.HasValue && !(SkipPermissions && CaptainRuntimeOptions.GetAutoApprove(captain)))
            {
                long timeoutMs = ((long)PermissionPromptTimeoutSeconds.Value + 120L) * 1000L;
                startInfo.Environment["MCP_TOOL_TIMEOUT"] = timeoutMs.ToString(CultureInfo.InvariantCulture);
            }

            // Per-captain reasoning effort -> Claude Code extended-thinking budget.
            int? thinkingTokens = ReasoningEffortTranslator.ToClaudeThinkingTokens(captain?.ReasoningEffort);
            if (thinkingTokens.HasValue)
            {
                startInfo.Environment["MAX_THINKING_TOKENS"] = thinkingTokens.Value.ToString(CultureInfo.InvariantCulture);
            }
        }

        #endregion
    }
}
