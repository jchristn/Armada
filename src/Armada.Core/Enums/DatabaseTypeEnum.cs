namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Supported database types.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DatabaseTypeEnum
    {
        /// <summary>
        /// SQLite embedded database.
        /// </summary>
        [EnumMember(Value = "Sqlite")]
        Sqlite,

        /// <summary>
        /// PostgreSQL database.
        /// </summary>
        [EnumMember(Value = "Postgresql")]
        Postgresql,

        /// <summary>
        /// Microsoft SQL Server database.
        /// </summary>
        [EnumMember(Value = "SqlServer")]
        SqlServer,

        /// <summary>
        /// MySQL database.
        /// </summary>
        [EnumMember(Value = "Mysql")]
        Mysql
    }
}
