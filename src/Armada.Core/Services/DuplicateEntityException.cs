namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// Raised when a create or update would store a value that must be unique and is already taken: a name, email,
    /// file name, or key checked before the write (<see cref="DuplicateEntityGuard"/>), or a unique-constraint
    /// violation a database provider reported during the write (translated by the database layer so provider text
    /// never reaches API clients). REST maps it to 409 Conflict with a <see cref="Armada.Core.Models.DuplicateEntityErrorDetail"/>
    /// in Data, MCP to ErrorCode Conflict with Code <see cref="ErrorCode"/>, and the WebSocket API to Conflict.
    /// </summary>
    public class DuplicateEntityException : InvalidOperationException
    {
        #region Public-Members

        /// <summary>
        /// Stable machine-readable code carried by every duplicate-entity error (REST Data.Code, MCP Code).
        /// </summary>
        public const string ErrorCode = "DuplicateEntity";

        /// <summary>
        /// Entity type, for example Captain or Fleet. Never null.
        /// </summary>
        public string EntityType { get; }

        /// <summary>
        /// The unique field that is already taken, for example Name, or null when the database reported the
        /// violation without saying which field.
        /// </summary>
        public string? Field { get; }

        /// <summary>
        /// The value that is already taken, or null when unknown.
        /// </summary>
        public string? Value { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="entityType">Entity type, for example Captain.</param>
        /// <param name="field">Unique field that is taken, or null when unknown.</param>
        /// <param name="value">Value that is taken, or null when unknown.</param>
        /// <param name="message">Human-readable message naming the entity and, when known, the field.</param>
        /// <param name="innerException">Provider exception, or null.</param>
        public DuplicateEntityException(string entityType, string? field, string? value, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            EntityType = entityType ?? String.Empty;
            Field = field;
            Value = value;
        }

        #endregion
    }
}
