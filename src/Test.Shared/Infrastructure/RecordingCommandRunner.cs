namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Hosting;

    /// <summary>
    /// Registration command runner that records every command instead of running it. Each call is recorded as
    /// "file arg1 arg2" (arguments joined by single spaces, unquoted). A responder can script exit codes and output;
    /// without one every command succeeds with empty output.
    /// </summary>
    public sealed class RecordingCommandRunner : IRegistrationCommandRunner
    {
        #region Public-Members

        /// <summary>
        /// Commands in the order they were run.
        /// </summary>
        public List<string> Calls { get; } = new List<string>();

        /// <summary>
        /// Optional responder; returning null means success with empty output.
        /// </summary>
        public Func<string, IReadOnlyList<string>, CommandResult?>? Responder { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public CommandResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add(fileName + (arguments.Count > 0 ? " " + String.Join(" ", arguments) : String.Empty));
            CommandResult? scripted = Responder?.Invoke(fileName, arguments);
            return scripted ?? new CommandResult();
        }

        /// <summary>
        /// True when any recorded command starts with the given text.
        /// </summary>
        /// <param name="prefix">Command prefix, for example "systemctl --user enable".</param>
        /// <returns>True when found.</returns>
        public bool Ran(string prefix)
        {
            return Calls.Exists(c => c.StartsWith(prefix, StringComparison.Ordinal));
        }

        #endregion
    }
}
