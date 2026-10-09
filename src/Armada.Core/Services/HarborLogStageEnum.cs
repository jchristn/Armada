namespace Armada.Core.Services
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Where a mission dock is in its life, for labelling a run of git and file work in it.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HarborLogStageEnum
    {
        /// <summary>
        /// Not a mission dock, or unknown.
        /// </summary>
        [EnumMember(Value = "None")]
        None,

        /// <summary>
        /// The dock exists and its job has not started.
        /// </summary>
        [EnumMember(Value = "Preparing")]
        Preparing,

        /// <summary>
        /// The dock's job is running.
        /// </summary>
        [EnumMember(Value = "Running")]
        Running,

        /// <summary>
        /// The dock's job has exited (landing and cleanup).
        /// </summary>
        [EnumMember(Value = "Finishing")]
        Finishing
    }
}
