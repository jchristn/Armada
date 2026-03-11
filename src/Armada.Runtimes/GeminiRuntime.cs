namespace Armada.Runtimes
{
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
        /// Sandbox mode for Gemini operations.
        /// Options: none, permissive, strict.
        /// </summary>
        public string SandboxMode { get; set; } = "none";

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
        protected override string GetCommand()
        {
            return _ExecutablePath;
        }

        /// <summary>
        /// Build Gemini CLI arguments.
        /// </summary>
        protected override List<string> BuildArguments(string prompt)
        {
            List<string> args = new List<string>();

            args.Add("--sandbox");
            args.Add(SandboxMode);
            args.Add("-p");
            args.Add(prompt);

            return args;
        }

        #endregion
    }
}
