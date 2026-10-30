namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Where a black box entry's object bytes are stored. The index rows are always in the database.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BlackBoxStorageTargetEnum
    {
        /// <summary>
        /// blackbox_objects.data in the Armada database (default).
        /// </summary>
        [EnumMember(Value = "Database")]
        Database,

        /// <summary>
        /// A directory on the Admiral host.
        /// </summary>
        [EnumMember(Value = "Disk")]
        Disk,

        /// <summary>
        /// Amazon S3 or an S3-compatible store.
        /// </summary>
        [EnumMember(Value = "S3")]
        S3,

        /// <summary>
        /// Azure Blob Storage.
        /// </summary>
        [EnumMember(Value = "AzureBlob")]
        AzureBlob,

        /// <summary>
        /// Google Cloud Storage.
        /// </summary>
        [EnumMember(Value = "GoogleCloud")]
        GoogleCloud,

        /// <summary>
        /// A CIFS (SMB) share.
        /// </summary>
        [EnumMember(Value = "Cifs")]
        Cifs,

        /// <summary>
        /// An NFSv3 export.
        /// </summary>
        [EnumMember(Value = "Nfs")]
        Nfs,

        /// <summary>
        /// A value this build does not recognize (written by a newer Armada); tolerant readers map unknown values here.
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown
    }
}
