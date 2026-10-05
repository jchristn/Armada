namespace Armada.Core.Database
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Conversion between <see cref="MissionFailureKindEnum"/> and the missions.failure_kind column, shared by
    /// every database provider. The column stores the enum member name; null means no failure kind was recorded.
    /// An unrecognized stored value reads back as <see cref="MissionFailureKindEnum.Unknown"/> rather than failing
    /// the whole row.
    /// </summary>
    public static class MissionFailureKindColumn
    {
        #region Public-Methods

        /// <summary>
        /// Convert a failure kind to its column value.
        /// </summary>
        /// <param name="kind">Failure kind, or null.</param>
        /// <returns>The enum member name, or <see cref="DBNull.Value"/> when null.</returns>
        public static object ToDbValue(MissionFailureKindEnum? kind)
        {
            if (!kind.HasValue) return DBNull.Value;
            return kind.Value.ToString();
        }

        /// <summary>
        /// Convert a column value to a failure kind.
        /// </summary>
        /// <param name="value">Raw column value.</param>
        /// <returns>The failure kind, or null when the column is null or empty.</returns>
        public static MissionFailureKindEnum? FromDbValue(object? value)
        {
            if (value == null || value == DBNull.Value) return null;
            string? text = value.ToString();
            if (String.IsNullOrWhiteSpace(text)) return null;
            if (Enum.TryParse(text.Trim(), true, out MissionFailureKindEnum parsed)) return parsed;
            return MissionFailureKindEnum.Unknown;
        }

        #endregion
    }
}
