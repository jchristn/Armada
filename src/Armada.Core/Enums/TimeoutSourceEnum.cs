namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Where a resolved timeout value came from (RS-8.8).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TimeoutSourceEnum
    {
        /// <summary>
        /// The global setting.
        /// </summary>
        [EnumMember(Value = "Global")]
        Global,

        /// <summary>
        /// The global setting scaled from the model profile's measured speed.
        /// </summary>
        [EnumMember(Value = "Measured")]
        Measured,

        /// <summary>
        /// An administrator's override on the model profile.
        /// </summary>
        [EnumMember(Value = "Profile")]
        Profile
    }
}
