namespace Armada.Core.Models
{
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;

    /// <summary>
    /// Configuration for an agent runtime.
    /// </summary>
    public class AgentRuntime
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier.
        /// </summary>
        public string Id
        {
            get => _Id;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Runtime display name.
        /// </summary>
        public string Name
        {
            get => _Name;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Name));
                _Name = value;
            }
        }

        /// <summary>
        /// Runtime type.
        /// </summary>
        public AgentRuntimeEnum RuntimeType { get; set; } = AgentRuntimeEnum.ClaudeCode;

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
        /// Default arguments for the agent command.
        /// </summary>
        public List<string> Args
        {
            get => _Args;
            set => _Args = value ?? new List<string>();
        }

        /// <summary>
        /// Whether the runtime supports session resume.
        /// </summary>
        public bool SupportsResume { get; set; } = false;

        /// <summary>
        /// Whether the runtime is active and available.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Creation timestamp in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.AgentRuntimeIdPrefix, 24);
        private string _Name = "Agent";
        private string _Command = "claude";
        private List<string> _Args = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AgentRuntime()
        {
        }

        /// <summary>
        /// Instantiate with name and command.
        /// </summary>
        /// <param name="name">Runtime name.</param>
        /// <param name="command">Command to execute.</param>
        /// <param name="runtimeType">Runtime type.</param>
        public AgentRuntime(string name, string command, AgentRuntimeEnum runtimeType = AgentRuntimeEnum.ClaudeCode)
        {
            Name = name;
            Command = command;
            RuntimeType = runtimeType;
        }

        #endregion
    }
}
