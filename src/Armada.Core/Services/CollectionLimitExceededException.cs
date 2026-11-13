namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// Raised when a request carries, or would store, more items in one collection than
    /// <see cref="CollectionLimits"/> allows: too many ids in a bulk request, too many entries in a list stored on an
    /// entity, or a summary window that would produce too many buckets. Armada never truncates such a collection
    /// silently; it rejects the request instead. REST maps it to 400 Bad Request, MCP to ErrorCode InvalidArgument
    /// with Code <see cref="ErrorCode"/>.
    /// </summary>
    public class CollectionLimitExceededException : ArgumentException
    {
        #region Public-Members

        /// <summary>
        /// Stable machine-readable code carried by every collection-limit error (REST Data.Code, MCP Code).
        /// </summary>
        public const string ErrorCode = "CollectionLimitExceeded";

        /// <summary>
        /// The field holding the collection, for example Ids or ProtectedBranchPatterns. Never null.
        /// </summary>
        public string Field { get; }

        /// <summary>
        /// Most items the field accepts.
        /// </summary>
        public int Limit { get; }

        /// <summary>
        /// Items the request carried or would produce.
        /// </summary>
        public long Actual { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with a message naming the field, the limit, and the actual count.
        /// </summary>
        /// <param name="field">Field holding the collection.</param>
        /// <param name="limit">Most items the field accepts.</param>
        /// <param name="actual">Items the request carried or would produce.</param>
        public CollectionLimitExceededException(string field, int limit, long actual)
            : base(field + " has " + actual + " items; the most allowed is " + limit + ".", field)
        {
            Field = field ?? String.Empty;
            Limit = limit;
            Actual = actual;
        }

        /// <summary>
        /// Instantiate with a custom message.
        /// </summary>
        /// <param name="field">Field holding the collection.</param>
        /// <param name="limit">Most items the field accepts.</param>
        /// <param name="actual">Items the request carried or would produce.</param>
        /// <param name="message">Human-readable message naming the field and the limit.</param>
        public CollectionLimitExceededException(string field, int limit, long actual, string message)
            : base(message, field)
        {
            Field = field ?? String.Empty;
            Limit = limit;
            Actual = actual;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override string Message
        {
            get
            {
                // ArgumentException appends " (Parameter 'x')"; the field is already named in the text.
                string message = base.Message;
                string suffix = " (Parameter '" + ParamName + "')";
                return message.EndsWith(suffix, StringComparison.Ordinal) ? message.Substring(0, message.Length - suffix.Length) : message;
            }
        }

        #endregion
    }
}
