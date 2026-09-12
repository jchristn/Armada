namespace Armada.Core.Harbor
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Which standard stream a chunk of captain output came from.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HarborOutputStreamEnum
    {
        /// <summary>
        /// Standard output.
        /// </summary>
        [EnumMember(Value = "Stdout")]
        Stdout,

        /// <summary>
        /// Standard error.
        /// </summary>
        [EnumMember(Value = "Stderr")]
        Stderr
    }
}
