namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Outcome of running a captain's agent process once on a prompt (outside any mission or dock).
    /// </summary>
    public class CaptainPromptResult
    {
        #region Public-Members

        /// <summary>
        /// Process identifier of the agent, or null when it never started.
        /// </summary>
        public int? ProcessId { get; set; } = null;

        /// <summary>
        /// Process exit code, or null when unknown (never started, killed, or timed out).
        /// </summary>
        public int? ExitCode { get; set; } = null;

        /// <summary>
        /// True when the process was stopped because it exceeded the time limit.
        /// </summary>
        public bool TimedOut { get; set; } = false;

        /// <summary>
        /// True when the process was stopped because the caller cancelled.
        /// </summary>
        public bool Cancelled { get; set; } = false;

        /// <summary>
        /// Tail of the agent's standard output (bounded), useful for diagnostics. Never null.
        /// </summary>
        public string Output
        {
            get => _Output;
            set => _Output = value ?? String.Empty;
        }

        /// <summary>
        /// The agent's final response when its runtime writes one (Codex, Mux, API endpoints), otherwise null.
        /// </summary>
        public string? FinalMessage { get; set; } = null;

        /// <summary>
        /// Error message when the process could not be started, or null.
        /// </summary>
        public string? Error { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Output = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CaptainPromptResult()
        {
        }

        #endregion
    }
}
