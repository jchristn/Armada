namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// One id skipped by a batch delete, with the reason.
    /// </summary>
    public class BatchDeleteSkipped
    {
        #region Public-Members

        /// <summary>
        /// Identifier that was skipped.
        /// </summary>
        public string Id { get; set; } = "";

        /// <summary>
        /// Why it was skipped.
        /// </summary>
        public string Reason { get; set; } = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BatchDeleteSkipped()
        {
        }

        #endregion
    }
}
