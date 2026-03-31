namespace Armada.Core.Settings
{
    using Armada.Core.Enums;

    /// <summary>
    /// Per-agent-runtime configuration.
    /// </summary>
    public class AgentSettings
    {
        #region Public-Members

        /// <summary>
        /// Runtime type this setting applies to.
        /// </summary>
        public AgentRuntimeEnum Runtime { get; set; } = AgentRuntimeEnum.ClaudeCode;

        /// <summary>
        /// Command to execute the agent.
        /// </summary>
        public string Command
        {
            get => _Command;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Command));
                _Command = value;
            }
        }

        /// <summary>
        /// Default arguments passed to the agent command.
        /// </summary>
        public string Args { get; set; } = "";

        /// <summary>
        /// Environment variables to set for the agent process.
        /// </summary>
        public Dictionary<string, string> Environment { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Whether the agent supports session resume.
        /// </summary>
        public bool SupportsResume { get; set; } = false;

        /// <summary>
        /// Maximum concurrent instances of this runtime type.
        /// Zero means unlimited.
        /// </summary>
        public int MaxConcurrent { get; set; } = 0;

        #endregion

        #region Private-Members

        private string _Command = "claude";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public AgentSettings()
        {
        }

        /// <summary>
        /// Instantiate with runtime type and command.
        /// </summary>
        /// <param name="runtime">Runtime type.</param>
        /// <param name="command">Command to execute.</param>
        /// <param name="args">Default arguments.</param>
        public AgentSettings(AgentRuntimeEnum runtime, string command, string args = "")
        {
            Runtime = runtime;
            Command = command;
            Args = args;
        }

        #endregion
    }
}
