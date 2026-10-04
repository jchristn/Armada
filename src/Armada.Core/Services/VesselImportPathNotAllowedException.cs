namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// Raised when a vessel import or browse path lies outside the allowed roots (Import.AllowedRoots, or the user
    /// profile directory when none are configured). Routes map it to HTTP 403.
    /// </summary>
    public class VesselImportPathNotAllowedException : Exception
    {
        #region Public-Members

        /// <summary>
        /// The normalized path that was rejected, or null.
        /// </summary>
        public string? RejectedPath { get; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Error message.</param>
        /// <param name="rejectedPath">The normalized path that was rejected.</param>
        public VesselImportPathNotAllowedException(string message, string? rejectedPath) : base(message)
        {
            RejectedPath = rejectedPath;
        }

        #endregion
    }
}
