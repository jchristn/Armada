namespace Armada.Core.Models
{
    /// <summary>
    /// The result of running a shell command in a vessel's workspace.
    /// </summary>
    public class WorkspaceExecResult
    {
        /// <summary>
        /// The command that was executed.
        /// </summary>
        public string Command { get; set; } = string.Empty;

        /// <summary>
        /// The working directory the command ran in.
        /// </summary>
        public string WorkingDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Where the command ran: "Admiral", or "Harbor Name (hbr_...)" for a checkout on a Harbor.
        /// </summary>
        public string Host { get; set; } = "Admiral";

        /// <summary>
        /// The process exit code (-1 when the command timed out or failed to start).
        /// </summary>
        public int ExitCode { get; set; } = -1;

        /// <summary>
        /// Captured standard output (truncated to a safe maximum).
        /// </summary>
        public string Stdout { get; set; } = string.Empty;

        /// <summary>
        /// Captured standard error (truncated to a safe maximum).
        /// </summary>
        public string Stderr { get; set; } = string.Empty;

        /// <summary>
        /// Whether the command was killed for exceeding its timeout.
        /// </summary>
        public bool TimedOut { get; set; } = false;

        /// <summary>
        /// Wall-clock duration in milliseconds.
        /// </summary>
        public double DurationMs { get; set; } = 0;
    }
}
