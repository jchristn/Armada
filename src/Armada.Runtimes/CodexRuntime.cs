namespace Armada.Runtimes
{
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
        protected override string GetCommand()
        {
            return ResolveExecutable(_ExecutablePath);
        }

        /// <summary>
        /// Build Codex CLI arguments.
        /// </summary>
        protected override List<string> BuildArguments(string prompt, string? model, string? finalMessageFilePath)
        {
            List<string> args = new List<string>();

            args.Add("exec");

            if (String.Equals(ApprovalMode, "dangerous", StringComparison.OrdinalIgnoreCase))
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

            if (!String.IsNullOrEmpty(finalMessageFilePath))
            {
                args.Add("--output-last-message");
                args.Add(finalMessageFilePath);
            }

            args.Add(prompt);

            return args;
        }

        #endregion
    }
}
