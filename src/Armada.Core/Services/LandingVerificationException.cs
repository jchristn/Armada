namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// Raised when a landing check ran against the integrated commit and did not pass: it exited nonzero, timed out, or
    /// changed the commit or working tree. It is an <see cref="InvalidOperationException"/>, so existing callers keep
    /// working; callers that classify failures read the check facts from its properties instead of the message.
    /// </summary>
    public class LandingVerificationException : InvalidOperationException
    {
        #region Public-Members

        /// <summary>
        /// The verification status: Failed or TimedOut.
        /// </summary>
        public string Status { get; }

        /// <summary>
        /// The check command that did not pass.
        /// </summary>
        public string Command { get; }

        /// <summary>
        /// The check's exit code, when it exited.
        /// </summary>
        public int? ExitCode { get; }

        /// <summary>
        /// The integrated commit the check ran against.
        /// </summary>
        public string CommitHash { get; }

        /// <summary>
        /// True when the check changed the commit or the working tree.
        /// </summary>
        public bool Mutated { get; }

        /// <summary>
        /// True when the check exceeded its time budget.
        /// </summary>
        public bool TimedOut => String.Equals(Status, "TimedOut", StringComparison.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">What failed.</param>
        /// <param name="status">Verification status (Failed or TimedOut).</param>
        /// <param name="command">The check command.</param>
        /// <param name="exitCode">The check's exit code, or null.</param>
        /// <param name="commitHash">The integrated commit.</param>
        /// <param name="mutated">Whether the check changed the commit or working tree.</param>
        public LandingVerificationException(string message, string status, string command, int? exitCode, string commitHash, bool mutated)
            : base(message)
        {
            Status = status ?? String.Empty;
            Command = command ?? String.Empty;
            ExitCode = exitCode;
            CommitHash = commitHash ?? String.Empty;
            Mutated = mutated;
        }

        #endregion
    }
}
