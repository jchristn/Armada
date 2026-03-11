namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Sort order for enumeration results.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum EnumerationOrderEnum
    {
        /// <summary>
        /// Sort by created date ascending (oldest first).
        /// </summary>
        [EnumMember(Value = "CreatedAscending")]
        CreatedAscending,

        /// <summary>
        /// Sort by created date descending (newest first).
        /// </summary>
        [EnumMember(Value = "CreatedDescending")]
        CreatedDescending
    }
}
