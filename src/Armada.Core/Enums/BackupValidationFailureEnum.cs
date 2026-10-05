namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Why a file offered for restore is not a usable Armada backup.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BackupValidationFailureEnum
    {
        /// <summary>
        /// The file is not a readable ZIP archive.
        /// </summary>
        NotAZipArchive,

        /// <summary>
        /// The ZIP has no armada.db entry.
        /// </summary>
        MissingDatabase,

        /// <summary>
        /// The armada.db entry is not a SQLite database with Armada's schema_migrations table.
        /// </summary>
        NotAnArmadaDatabase
    }
}
