namespace Armada.Core.Models
{
    /// <summary>
    /// An entity that was skipped during a batch delete operation.
    /// </summary>
    public class DeleteMultipleSkipped
    {
        #region Public-Members

        /// <summary>
        /// Entity ID that was skipped.
        /// </summary>
        public string Id { get; set; } = "";

        /// <summary>
        /// Reason the entity was skipped.
        /// </summary>
        public string Reason { get; set; } = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public DeleteMultipleSkipped()
        {
        }

        /// <summary>
        /// Instantiate with ID and reason.
        /// </summary>
        /// <param name="id">Entity ID.</param>
        /// <param name="reason">Reason for skipping.</param>
        public DeleteMultipleSkipped(string id, string reason)
        {
            Id = id ?? "";
            Reason = reason ?? "";
        }

        #endregion
    }
}
