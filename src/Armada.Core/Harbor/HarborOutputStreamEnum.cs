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
        Stderr,

        /// <summary>
        /// The runtime's final-message artifact, sent once just before the job exits when the launch set
        /// <see cref="HarborLaunchRequest.ReturnFinalMessage"/>. Never sent otherwise, so an Admiral that does not ask
        /// for it never receives it.
        /// </summary>
        [EnumMember(Value = "FinalMessage")]
        FinalMessage
    }
}
