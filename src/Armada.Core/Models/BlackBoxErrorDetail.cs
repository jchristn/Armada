namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Machine-readable detail carried in the Data field of a black box REST error: the stable error code
    /// (BlackBoxNotFound, BlackBoxDeleting, BlackBoxObjectCorrupt, BlackBoxObjectMissing, BlackBoxStoreUnavailable,
    /// BlackBoxActionsUnavailable, BlackBoxConfirmationRequired, InvalidArgument or CollectionLimitExceeded) and, for a
    /// request validation error, the field.
    /// </summary>
    public class BlackBoxErrorDetail
    {
        #region Public-Members

        /// <summary>
        /// Stable error code.
        /// </summary>
        public string Code
        {
            get => _Code;
            set => _Code = value ?? String.Empty;
        }

        /// <summary>
        /// The request field at fault (for example audience, offset or section), or null.
        /// </summary>
        public string? Field { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Code = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxErrorDetail()
        {
        }

        /// <summary>
        /// Instantiate with a code and field.
        /// </summary>
        /// <param name="code">Error code.</param>
        /// <param name="field">Field, or null.</param>
        public BlackBoxErrorDetail(string code, string? field = null)
        {
            Code = code;
            Field = field;
        }

        #endregion
    }
}
