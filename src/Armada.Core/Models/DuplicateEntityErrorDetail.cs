namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Services;

    /// <summary>
    /// Machine-readable detail carried in the Data field of a REST 409 Conflict response when a create or update
    /// would store a value that must be unique and is already taken.
    /// </summary>
    public class DuplicateEntityErrorDetail
    {
        #region Public-Members

        /// <summary>
        /// Stable error code; always <see cref="DuplicateEntityException.ErrorCode"/> (DuplicateEntity).
        /// </summary>
        public string Code
        {
            get => _Code;
            set => _Code = value ?? String.Empty;
        }

        /// <summary>
        /// Entity type, for example Captain or Fleet.
        /// </summary>
        public string EntityType
        {
            get => _EntityType;
            set => _EntityType = value ?? String.Empty;
        }

        /// <summary>
        /// The unique field that is already taken, for example Name or Email, or null when the database reported the
        /// violation without saying which field.
        /// </summary>
        public string? Field { get; set; } = null;

        /// <summary>
        /// The value that is already taken, or null when unknown.
        /// </summary>
        public string? Value { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Code = DuplicateEntityException.ErrorCode;
        private string _EntityType = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DuplicateEntityErrorDetail()
        {
        }

        /// <summary>
        /// Build the detail for an exception.
        /// </summary>
        /// <param name="ex">Duplicate-entity exception.</param>
        /// <returns>Detail.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="ex"/> is null.</exception>
        public static DuplicateEntityErrorDetail FromException(DuplicateEntityException ex)
        {
            if (ex == null) throw new ArgumentNullException(nameof(ex));
            return new DuplicateEntityErrorDetail
            {
                Code = DuplicateEntityException.ErrorCode,
                EntityType = ex.EntityType,
                Field = ex.Field,
                Value = ex.Value
            };
        }

        #endregion
    }
}
