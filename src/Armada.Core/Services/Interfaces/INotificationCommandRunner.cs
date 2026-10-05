namespace Armada.Core.Services.Interfaces
{
    using System.Collections.Generic;

    /// <summary>
    /// Runs the platform command that shows a desktop notification. Production starts the process; tests record the
    /// command instead, so test runs never raise real notifications.
    /// </summary>
    public interface INotificationCommandRunner
    {
        /// <summary>
        /// Run a command with its arguments (passed as an argument list, never through a shell).
        /// </summary>
        /// <param name="fileName">Executable.</param>
        /// <param name="arguments">Arguments.</param>
        void Run(string fileName, IReadOnlyList<string> arguments);
    }
}
