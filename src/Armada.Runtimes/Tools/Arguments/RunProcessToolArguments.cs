namespace Armada.Runtimes.Tools.Arguments
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Typed arguments for the run_process built-in tool.
    /// </summary>
    public class RunProcessToolArguments : IToolArguments
    {
        #region Public-Members

        /// <summary>
        /// The command to run. When <see cref="Args"/> is non-empty this is the executable; otherwise it is a
        /// command line handed to the runtime shell. Required.
        /// </summary>
        [JsonPropertyName("command")]
        public string? Command { get; set; } = null;

        /// <summary>
        /// The argument vector for <see cref="Command"/>, passed to the process without a shell, or null.
        /// </summary>
        [JsonPropertyName("args")]
        public List<string?>? Args { get; set; } = null;

        /// <summary>
        /// The working directory for the process, or null for the agent's working directory.
        /// </summary>
        [JsonPropertyName("working_directory")]
        public string? WorkingDirectory { get; set; } = null;

        /// <summary>
        /// The timeout in milliseconds, or null for the default.
        /// </summary>
        [JsonPropertyName("timeout_ms")]
        public int? TimeoutMs { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validates that the command is present and that no argument is null.
        /// </summary>
        /// <returns>Null when valid; otherwise the problem message.</returns>
        public string? Validate()
        {
            string? commandProblem = ToolArgumentParser.Required(Command, "command");
            if (commandProblem != null) return commandProblem;

            if (Args != null)
            {
                for (int i = 0; i < Args.Count; i++)
                {
                    if (Args[i] == null) return "Parameter 'args' element at index " + i + " must be a string, not null.";
                }
            }

            return null;
        }

        #endregion
    }
}
