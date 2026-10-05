namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Raised by restore when the offered file is not an Armada backup (not a ZIP, no armada.db, or not an Armada
    /// database). Nothing has been changed when it is thrown. REST answers 400 BadRequest; it derives from
    /// <see cref="InvalidOperationException"/> so existing MCP and WebSocket mappings are unchanged.
    /// </summary>
    public class BackupValidationException : InvalidOperationException
    {
        #region Public-Members

        /// <summary>
        /// Why the backup was rejected.
        /// </summary>
        public BackupValidationFailureEnum Reason { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="reason">Why the backup was rejected.</param>
        /// <param name="message">Human-readable message.</param>
        /// <param name="inner">Underlying exception, or null.</param>
        public BackupValidationException(BackupValidationFailureEnum reason, string message, Exception? inner = null)
            : base(message, inner)
        {
            Reason = reason;
        }

        #endregion
    }
}
