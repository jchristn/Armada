namespace Armada.Core.Database
{
    using System;

    /// <summary>
    /// A stored objective field could not be decoded. The stored text is left unchanged.
    /// </summary>
    public class StoredObjectiveDataException : InvalidOperationException
    {
        #region Public-Members

        /// <summary>
        /// Objective identifier of the row that could not be decoded.
        /// </summary>
        public string ObjectiveId { get; }

        /// <summary>
        /// Stored field name that could not be decoded.
        /// </summary>
        public string FieldName { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create an exception that names the objective and the field.
        /// </summary>
        /// <param name="objectiveId">Objective identifier. Required.</param>
        /// <param name="fieldName">Stored field name. Required.</param>
        public StoredObjectiveDataException(string objectiveId, string fieldName)
            : this(objectiveId, fieldName, null)
        {
        }

        /// <summary>
        /// Create an exception that names the objective and the field and keeps the decode failure.
        /// </summary>
        /// <param name="objectiveId">Objective identifier. Required.</param>
        /// <param name="fieldName">Stored field name. Required.</param>
        /// <param name="innerException">Decode failure, or null.</param>
        public StoredObjectiveDataException(string objectiveId, string fieldName, Exception? innerException)
            : base(BuildMessage(objectiveId, fieldName), innerException)
        {
            ObjectiveId = objectiveId;
            FieldName = fieldName;
        }

        #endregion

        #region Private-Methods

        private static string BuildMessage(string objectiveId, string fieldName)
        {
            if (objectiveId == null) throw new ArgumentNullException(nameof(objectiveId));
            if (fieldName == null) throw new ArgumentNullException(nameof(fieldName));
            return "Stored objective '" + objectiveId + "' field '" + fieldName + "' cannot be decoded.";
        }

        #endregion
    }
}
