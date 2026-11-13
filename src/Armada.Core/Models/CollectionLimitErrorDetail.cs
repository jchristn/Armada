namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Services;

    /// <summary>
    /// Machine-readable detail carried in the Data field of a REST 400 Bad Request response when a request carries, or
    /// would store or produce, more items in one collection than <see cref="CollectionLimits"/> allows.
    /// </summary>
    public class CollectionLimitErrorDetail
    {
        #region Public-Members

        /// <summary>
        /// Stable error code; always <see cref="CollectionLimitExceededException.ErrorCode"/> (CollectionLimitExceeded).
        /// </summary>
        public string Code
        {
            get => _Code;
            set => _Code = value ?? String.Empty;
        }

        /// <summary>
        /// The field holding the collection, for example Ids.
        /// </summary>
        public string Field
        {
            get => _Field;
            set => _Field = value ?? String.Empty;
        }

        /// <summary>
        /// Most items the field accepts.
        /// </summary>
        public int Limit { get; set; } = 0;

        /// <summary>
        /// Items the request carried or would produce.
        /// </summary>
        public long Actual { get; set; } = 0;

        #endregion

        #region Private-Members

        private string _Code = CollectionLimitExceededException.ErrorCode;
        private string _Field = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CollectionLimitErrorDetail()
        {
        }

        /// <summary>
        /// Build the detail for an exception.
        /// </summary>
        /// <param name="ex">Collection-limit exception.</param>
        /// <returns>Detail.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="ex"/> is null.</exception>
        public static CollectionLimitErrorDetail FromException(CollectionLimitExceededException ex)
        {
            if (ex == null) throw new ArgumentNullException(nameof(ex));
            return new CollectionLimitErrorDetail
            {
                Code = CollectionLimitExceededException.ErrorCode,
                Field = ex.Field,
                Limit = ex.Limit,
                Actual = ex.Actual
            };
        }

        #endregion
    }
}
