namespace Armada.Helm.Infrastructure
{
    using System;

    /// <summary>
    /// Thrown when the CLI cannot use, or refuses, the selected Admiral target. Callers branch on
    /// <see cref="ErrorCode"/>, never on the message.
    /// </summary>
    public class AdmiralTargetException : Exception
    {
        #region Public-Members

        /// <summary>
        /// The error code.
        /// </summary>
        public AdmiralTargetErrorEnum ErrorCode { get; }

        /// <summary>
        /// The command that raised the error (for example <c>server start</c>), or null.
        /// </summary>
        public string? CommandName { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="errorCode">Error code.</param>
        /// <param name="message">Message shown to the user (never contains a credential).</param>
        /// <param name="commandName">Command name, or null.</param>
        public AdmiralTargetException(AdmiralTargetErrorEnum errorCode, string message, string? commandName = null)
            : base(message)
        {
            ErrorCode = errorCode;
            CommandName = commandName;
        }

        #endregion
    }
}
