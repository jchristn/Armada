namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// The answer to one CLI permission prompt.
    /// </summary>
    public class CliPermissionPromptOutcome
    {
        #region Public-Members

        /// <summary>
        /// True when the tool may run with its original input.
        /// </summary>
        public bool Allowed { get; set; } = false;

        /// <summary>
        /// Message for the captain (the reason for a denial).
        /// </summary>
        public string Message
        {
            get => _Message;
            set => _Message = value ?? String.Empty;
        }

        /// <summary>
        /// The stored request.
        /// </summary>
        public CliPermissionRequest? Request { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Message = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CliPermissionPromptOutcome()
        {
        }

        #endregion
    }
}
