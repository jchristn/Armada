namespace Armada.Core.Services.Health
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Raw result of one dependency tool invocation.
    /// </summary>
    public class DependencyToolResult
    {
        #region Public-Members

        /// <summary>
        /// How the invocation ended. Defaults to Completed.
        /// </summary>
        public DependencyToolOutcomeEnum Outcome { get; set; } = DependencyToolOutcomeEnum.Completed;

        /// <summary>
        /// Process exit code (meaningful only when Outcome is Completed).
        /// </summary>
        public int ExitCode { get; set; } = 0;

        /// <summary>
        /// Captured standard output. Never null.
        /// </summary>
        public string StandardOutput
        {
            get => _StandardOutput;
            set => _StandardOutput = value ?? String.Empty;
        }

        /// <summary>
        /// Captured standard error. Never null.
        /// </summary>
        public string StandardError
        {
            get => _StandardError;
            set => _StandardError = value ?? String.Empty;
        }

        #endregion

        #region Private-Members

        private string _StandardOutput = String.Empty;
        private string _StandardError = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DependencyToolResult()
        {
        }

        #endregion
    }
}
