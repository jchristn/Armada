namespace Armada.Runtimes
{
    using Armada.Core.Models;
    using Armada.Core.Services;
    using System.Diagnostics;
    using SyslogLogging;

    /// <summary>
    /// Agent runtime adapter for Google Gemini CLI.
    /// </summary>
    public class GeminiRuntime : BaseAgentRuntime
    {
        #region Public-Members

        /// <summary>
        /// Runtime display name.
        /// </summary>
        public override string Name => "Gemini";

        /// <summary>
        /// Gemini CLI does not support session resume.
        /// </summary>
        public override bool SupportsResume => false;

        /// <summary>
        /// Path to the gemini CLI executable.
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
        /// Approval mode for Gemini operations.
        /// Current CLI values include default, auto_edit, yolo, and plan.
        /// </summary>
        public string ApprovalMode { get; set; } = "yolo";

        #endregion

        #region Private-Members

        private string _ExecutablePath = "gemini";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        public GeminiRuntime(LoggingModule logging) : base(logging)
        {
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Get the gemini CLI command.
        /// </summary>
        /// <summary>
        /// The runtime this adapter drives.
        /// </summary>
        protected override Armada.Core.Enums.AgentRuntimeEnum RuntimeType => Armada.Core.Enums.AgentRuntimeEnum.Gemini;

        /// <summary>
        /// Get the command to execute for this runtime.
        /// </summary>
        protected override string GetCommand()
        {
            return ResolveExecutable(_ExecutablePath);
        }

        /// <summary>
        /// Build Gemini CLI arguments.
        /// </summary>
        protected override List<string> BuildArguments(
            string workingDirectory,
            string prompt,
            string? model,
            string? finalMessageFilePath,
            Captain? captain)
        {
            List<string> args = new List<string>();

            if (!String.IsNullOrEmpty(model))
            {
                args.Add("--model");
                args.Add(model);
            }

            // The prompt is delivered on stdin (see UsePromptStdin), not via -p, because on Windows the
            // gemini executable is an npm ".cmd" wrapper and a multi-line -p argument passed through cmd.exe
            // is truncated at the first newline. Gemini reads the prompt from stdin when it is run
            // non-interactively (piped stdin) with no -p argument.
            args.Add("--approval-mode");
            args.Add(CaptainRuntimeOptions.GetAutoApprove(captain) ? ApprovalMode : "auto_edit");

            return args;
        }

        /// <summary>
        /// Deliver the prompt on stdin rather than as a -p argument, avoiding the Windows cmd.exe
        /// multi-line-argument truncation. Gemini reads the prompt from stdin in non-interactive mode.
        /// </summary>
        protected override bool UsePromptStdin => true;

        #endregion
    }
}
