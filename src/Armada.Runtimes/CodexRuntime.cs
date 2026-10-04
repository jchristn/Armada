namespace Armada.Runtimes
{
    using Armada.Core.Models;
    using Armada.Core.Services;
    using System.Diagnostics;
    using SyslogLogging;

    /// <summary>
    /// Agent runtime adapter for OpenAI Codex CLI.
    /// </summary>
    public class CodexRuntime : BaseAgentRuntime
    {
        #region Public-Members

        /// <summary>
        /// Runtime display name.
        /// </summary>
        public override string Name => "Codex";

        /// <summary>
        /// Codex does not support session resume.
        /// </summary>
        public override bool SupportsResume => false;

        /// <summary>
        /// Path to the codex CLI executable.
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
        /// Approval mode for codex operations.
        /// </summary>
        public string ApprovalMode { get; set; } = "full-auto";

        #endregion

        #region Private-Members

        private string _ExecutablePath = "codex";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        public CodexRuntime(LoggingModule logging) : base(logging)
        {
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Get the codex CLI command.
        /// </summary>
        /// <summary>
        /// The runtime this adapter drives.
        /// </summary>
        protected override Armada.Core.Enums.AgentRuntimeEnum RuntimeType => Armada.Core.Enums.AgentRuntimeEnum.Codex;

        /// <summary>
        /// Get the command to execute for this runtime.
        /// </summary>
        protected override string GetCommand()
        {
            return ResolveExecutable(_ExecutablePath);
        }

        /// <summary>
        /// Build Codex CLI arguments.
        /// </summary>
        protected override List<string> BuildArguments(
            string workingDirectory,
            string prompt,
            string? model,
            string? finalMessageFilePath,
            Captain? captain)
        {
            List<string> args = new List<string>();

            args.Add("exec");

            if (!CaptainRuntimeOptions.GetAutoApprove(captain))
            {
                // No approval bypass: run sandboxed to the workspace (no network, no writes outside the worktree).
                args.Add("--sandbox");
                args.Add("workspace-write");
            }
            else if (String.Equals(ApprovalMode, "dangerous", StringComparison.OrdinalIgnoreCase))
            {
                args.Add("--dangerously-bypass-approvals-and-sandbox");
            }
            else if (OperatingSystem.IsWindows() && String.Equals(ApprovalMode, "full-auto", StringComparison.OrdinalIgnoreCase))
            {
                args.Add("--dangerously-bypass-approvals-and-sandbox");
            }
            else if (String.Equals(ApprovalMode, "full-auto", StringComparison.OrdinalIgnoreCase))
            {
                args.Add("--full-auto");
            }

            if (!String.IsNullOrEmpty(model))
            {
                args.Add("--model");
                args.Add(model);
            }

            // Per-captain reasoning effort -> Codex config override (minimal/low/medium/high).
            string? reasoningEffort = ReasoningEffortTranslator.ToCodexReasoningEffort(captain?.ReasoningEffort);
            if (!String.IsNullOrEmpty(reasoningEffort))
            {
                args.Add("-c");
                args.Add("model_reasoning_effort=" + reasoningEffort);
            }

            if (!String.IsNullOrEmpty(finalMessageFilePath))
            {
                args.Add("--output-last-message");
                args.Add(finalMessageFilePath);
            }

            // The prompt is delivered on stdin (see UsePromptStdin), not as a CLI argument. On Windows the
            // codex executable is an npm ".cmd" wrapper; a multi-line argument passed through cmd.exe is
            // truncated at the first newline, so the agent would receive only the first line of the prompt.
            // 'codex exec' reads the prompt from stdin when no positional prompt is supplied.
            return args;
        }

        /// <summary>
        /// Deliver the prompt on stdin rather than as a command-line argument. 'codex exec' reads the prompt
        /// from stdin, and this avoids the Windows cmd.exe multi-line-argument truncation.
        /// </summary>
        protected override bool UsePromptStdin => true;

        #endregion
    }
}
