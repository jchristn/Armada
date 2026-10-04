namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Author role of an Ask Armada thread message.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AskMessageRoleEnum
    {
        /// <summary>
        /// Written by the thread's owner.
        /// </summary>
        [EnumMember(Value = "User")]
        User,

        /// <summary>
        /// Written by the thread's captain.
        /// </summary>
        [EnumMember(Value = "Assistant")]
        Assistant,

        /// <summary>
        /// Written by Armada itself (action results, work updates, errors).
        /// </summary>
        [EnumMember(Value = "System")]
        System
    }
}
