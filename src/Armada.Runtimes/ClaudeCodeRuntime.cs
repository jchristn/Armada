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
        /// from the terminal "result" event. Used by interactive chat; missions leave this false so their
        /// output stays human-readable and progress-signal parsing is unaffected.
        /// </summary>
        public bool StreamJsonOutput { get; set; } = false;

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
            }

            // The prompt is delivered on stdin (see UsePromptStdin), not as a CLI argument. On Windows the
            // claude executable is an npm ".cmd" wrapper; a multi-line argument passed through cmd.exe is
            // truncated at the first newline, so the agent would receive only the first line of the prompt.
            // Reading the prompt from stdin preserves the full multi-line content on every platform.
            return args;
        }

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
