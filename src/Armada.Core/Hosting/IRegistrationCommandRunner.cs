namespace Armada.Core.Hosting
{
    using System.Collections.Generic;

    /// <summary>
    /// Runs the service-manager commands that registration needs. The production implementation starts a
    /// process; tests substitute a recorder so the exact command lines can be asserted without touching the
    /// machine's service manager.
    /// </summary>
    public interface IRegistrationCommandRunner
    {
        /// <summary>
        /// Run a command and wait for it to exit.
        /// </summary>
        /// <param name="fileName">Executable name or path (for example "systemctl").</param>
        /// <param name="arguments">Arguments, one element per argv entry (no shell quoting).</param>
        /// <returns>The exit code and captured output.</returns>
        CommandResult Run(string fileName, IReadOnlyList<string> arguments);
    }
}
