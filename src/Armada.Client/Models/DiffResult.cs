namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// A mission's diff.
    /// </summary>
    public class DiffResult
    {
        #region Public-Members

        /// <summary>
        /// Unified diff text.
        /// </summary>
        public string Diff { get; set; } = "";

        /// <summary>
        /// Branch the diff was taken from, or null.
        /// </summary>
        public string? Branch { get; set; } = null;

        /// <summary>
        /// Error message when the diff could not be produced, or null.
        /// </summary>
        public string? Error { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DiffResult()
        {
        }

        #endregion
    }
}
