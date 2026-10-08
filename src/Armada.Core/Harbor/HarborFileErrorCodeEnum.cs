namespace Armada.Core.Harbor
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Why a <see cref="HarborFileRequest"/> failed, so the Admiral can report it the way it reports the same failure on
    /// its own disk.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HarborFileErrorCodeEnum
    {
        /// <summary>
        /// The request succeeded.
        /// </summary>
        [EnumMember(Value = "None")]
        None,

        /// <summary>
        /// The path does not exist.
        /// </summary>
        [EnumMember(Value = "NotFound")]
        NotFound,

        /// <summary>
        /// The path is outside the request's root or the Harbor's docks folder, goes through a symbolic link, or is a
        /// folder Workspace does not show.
        /// </summary>
        [EnumMember(Value = "Refused")]
        Refused,

        /// <summary>
        /// The file changed after it was opened (SaveFile).
        /// </summary>
        [EnumMember(Value = "Conflict")]
        Conflict,

        /// <summary>
        /// The request does not fit the path (for example a directory where a file was expected, or a missing argument).
        /// </summary>
        [EnumMember(Value = "Invalid")]
        Invalid,

        /// <summary>
        /// The operation failed on the Harbor host (for example an I/O error).
        /// </summary>
        [EnumMember(Value = "Failed")]
        Failed
    }
}
